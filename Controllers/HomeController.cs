using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly ITaskService _taskService;
    private readonly IEmployeeService _employeeService;
    private readonly IHolidayService _holidayService;

    public HomeController(
        ILogger<HomeController> logger,
        ApplicationDbContext context,
        ITaskService taskService,
        IEmployeeService employeeService,
        IHolidayService holidayService)
    {
        _logger = logger;
        _context = context;
        _taskService = taskService;
        _employeeService = employeeService;
        _holidayService = holidayService;
    }

    public async Task<IActionResult> Index(DateTime? date)
    {
        try
        {
            var currentDate = date?.Date ?? DateTime.Today;

            var hiddenTasks = await GetHiddenTasks();
            ViewBag.HiddenTasks = hiddenTasks;

            var employeeScores = await _employeeService.GetEmployeeScoresAsync();
            ViewBag.EmployeeScores = employeeScores;

            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .AsNoTracking()
                .ToListAsync();

            var visibleTasks = await _taskService.GetTasksVisibleOnDateAsync(currentDate);

            var taskData = await GetTaskDataDictionary(currentDate);

            var filteredTaskData = taskData
                .Where(kvp => visibleTasks.Any(t => t.Id.ToString() == kvp.Key.Split('_')[1]))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var employees = await _context.Employees
                .Include(e => e.Department)
                .Where(e => e.IsActive)
                .OrderBy(e => e.Name)
                .AsNoTracking()
                .ToListAsync();

            var branchAssignments = await GetBranchAssignments(currentDate);

            var holidays = await _context.Holidays.AsNoTracking().ToListAsync();

            var viewModel = new DashboardViewModel
            {
                CurrentDate = currentDate,
                Branches = branches,
                Tasks = visibleTasks,
                Employees = employees,
                TaskData = filteredTaskData,
                NotesData = await GetNotesData(),
                Holidays = holidays,
                BranchAssignments = branchAssignments,
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard");
            TempData["ErrorMessage"] = "Error loading dashboard. Please try again.";
            return View(new DashboardViewModel());
        }
    }

    // ========== UPDATE TASK TIME ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTaskTime(int branchId, int taskItemId, string date, DateTime completionTime)
    {
        try
        {
            var taskDate = DateTime.Parse(date).Date;
            var task = await _context.TaskItems.FindAsync(taskItemId);

            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            // Convert completion time to local time for comparison
            var completionTimeLocal = completionTime.Kind == DateTimeKind.Utc 
                ? completionTime.ToLocalTime() 
                : completionTime;
            
            var deadline = CalculateDeadline(task, taskDate);
            
            // Convert deadline to local time for comparison
            var deadlineLocal = deadline.Kind == DateTimeKind.Utc 
                ? deadline.ToLocalTime() 
                : deadline;

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta != null ? ta.Employee : null)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate.Date == taskDate.Date);

            if (dailyTask == null)
            {
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = taskItemId,
                    TaskDate = taskDate,
                    IsCompleted = true,
                    CompletedAt = completionTime.ToUniversalTime()
                };
                _context.DailyTasks.Add(dailyTask);
            }
            else
            {
                dailyTask.IsCompleted = true;
                dailyTask.CompletedAt = completionTime.ToUniversalTime();
            }

            await _context.SaveChangesAsync();

            // Auto-assign to employee
            await AutoAssignTaskToEmployee(dailyTask, taskDate);

            // Calculate adjusted deadline with any existing adjustment
            var adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0;
            var adjustedDeadlineLocal = deadlineLocal.AddMinutes(adjustmentMinutes);
            
            // Get delay type and text based on adjusted deadline
            var delayType = GetDelayType(adjustedDeadlineLocal, completionTimeLocal);
            var delayText = GetDelayText(adjustedDeadlineLocal, completionTimeLocal);

            var response = new
            {
                success = true,
                taskData = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = completionTimeLocal,
                    delayType = delayType,
                    delayText = delayText,
                    adjustmentMinutes = adjustmentMinutes,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = adjustedDeadlineLocal
                }
            };

            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating task time");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ========== GET TASK STATUS ==========
    [HttpGet]
    public async Task<IActionResult> GetTaskStatus(int branchId, int taskItemId, string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date).Date;
            var task = await _context.TaskItems.FindAsync(taskItemId);
            
            if (task == null)
                return Json(new { exists = false });

            var deadline = CalculateDeadline(task, taskDate);
            var deadlineLocal = deadline.Kind == DateTimeKind.Utc ? deadline.ToLocalTime() : deadline;

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta != null ? ta.Employee : null)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate.Date == taskDate.Date);

            if (dailyTask == null)
            {
                return Json(new { exists = false });
            }

            // Calculate adjusted deadline
            var adjustedDeadlineLocal = deadlineLocal.AddMinutes(dailyTask.AdjustmentMinutes ?? 0);
            
            string delayType = "pending";
            string delayText = "Pending";
            DateTime? completedAtLocal = null;
            
            if (dailyTask.IsCompleted && dailyTask.CompletedAt.HasValue)
            {
                completedAtLocal = dailyTask.CompletedAt.Value.ToLocalTime();
                delayType = GetDelayType(adjustedDeadlineLocal, completedAtLocal.Value);
                delayText = GetDelayText(adjustedDeadlineLocal, completedAtLocal.Value);
            }

            var response = new
            {
                exists = true,
                branchId = branchId,
                taskId = taskItemId,
                isCompleted = dailyTask.IsCompleted,
                completedAt = completedAtLocal,
                delayType = delayType,
                delayText = delayText,
                adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                adjustmentReason = dailyTask.AdjustmentReason ?? "",
                assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                deadline = adjustedDeadlineLocal
            };

            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task status");
            return Json(new { exists = false, error = ex.Message });
        }
    }

    // ========== RESET SINGLE TASK ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTask(int branchId, int taskItemId, string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date).Date;
            var task = await _context.TaskItems.FindAsync(taskItemId);
            
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var deadline = CalculateDeadline(task, taskDate);
            var deadlineLocal = deadline.Kind == DateTimeKind.Utc ? deadline.ToLocalTime() : deadline;

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta != null ? ta.Employee : null)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate.Date == taskDate.Date);

            if (dailyTask != null)
            {
                dailyTask.IsCompleted = false;
                dailyTask.CompletedAt = null;
                await _context.SaveChangesAsync();
            }

            var adjustedDeadline = deadlineLocal.AddMinutes(dailyTask?.AdjustmentMinutes ?? 0);

            var response = new
            {
                success = true,
                taskData = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = false,
                    completedAt = (DateTime?)null,
                    delayType = "pending",
                    delayText = "Pending",
                    adjustmentMinutes = dailyTask?.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask?.AdjustmentReason ?? "",
                    assignedTo = dailyTask?.TaskAssignment?.Employee?.Name ?? "",
                    deadline = adjustedDeadline
                }
            };

            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting task");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ========== RESET ALL FOR TASK ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAllForTask(int taskItemId, string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date).Date;
            var task = await _context.TaskItems.FindAsync(taskItemId);

            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            var count = 0;
            var taskData = new Dictionary<int, object>();

            foreach (var branch in branches)
            {
                if (branch.HiddenTasks != null && branch.HiddenTasks.Contains(task.Name))
                    continue;

                var dailyTask = await _context.DailyTasks
                    .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta != null ? ta.Employee : null)
                    .FirstOrDefaultAsync(dt => dt.BranchId == branch.Id &&
                                               dt.TaskItemId == taskItemId &&
                                               dt.TaskDate.Date == taskDate.Date);

                var deadline = CalculateDeadline(task, taskDate);
                var deadlineLocal = deadline.Kind == DateTimeKind.Utc ? deadline.ToLocalTime() : deadline;
                var adjustedDeadline = deadlineLocal.AddMinutes(dailyTask?.AdjustmentMinutes ?? 0);

                if (dailyTask != null && dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = false;
                    dailyTask.CompletedAt = null;
                    count++;
                }

                taskData[branch.Id] = new
                {
                    branchId = branch.Id,
                    taskId = taskItemId,
                    isCompleted = false,
                    completedAt = (DateTime?)null,
                    delayType = "pending",
                    delayText = "Pending",
                    adjustmentMinutes = dailyTask?.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask?.AdjustmentReason ?? "",
                    assignedTo = dailyTask?.TaskAssignment?.Employee?.Name ?? "",
                    deadline = adjustedDeadline
                };
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                count = count,
                taskData = taskData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reset all for task");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ========== RESET ALL TASKS FOR A BRANCH ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAllTasksForBranch(int branchId, string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date).Date;
            var branch = await _context.Branches.FindAsync(branchId);

            if (branch == null)
                return Json(new { success = false, message = "Branch not found" });

            var visibleTasks = await _taskService.GetTasksVisibleOnDateAsync(taskDate);
            var hiddenTaskNames = branch.HiddenTasks ?? new List<string>();
            var visibleTaskIds = visibleTasks
                .Where(t => !hiddenTaskNames.Contains(t.Name))
                .Select(t => t.Id)
                .ToList();

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta != null ? ta.Employee : null)
                .Where(dt => dt.BranchId == branchId &&
                             dt.TaskDate.Date == taskDate.Date &&
                             visibleTaskIds.Contains(dt.TaskItemId) &&
                             dt.IsCompleted)
                .ToListAsync();

            var count = 0;
            foreach (var dailyTask in dailyTasks)
            {
                dailyTask.IsCompleted = false;
                dailyTask.CompletedAt = null;
                count++;
            }

            await _context.SaveChangesAsync();

            // Build response data for all tasks
            var taskData = new Dictionary<int, object>();
            foreach (var task in visibleTasks.Where(t => !hiddenTaskNames.Contains(t.Name)))
            {
                var dailyTask = dailyTasks.FirstOrDefault(dt => dt.TaskItemId == task.Id);
                var deadline = CalculateDeadline(task, taskDate);
                var deadlineLocal = deadline.Kind == DateTimeKind.Utc ? deadline.ToLocalTime() : deadline;
                var adjustedDeadline = deadlineLocal.AddMinutes(dailyTask?.AdjustmentMinutes ?? 0);

                taskData[task.Id] = new
                {
                    branchId = branchId,
                    taskId = task.Id,
                    isCompleted = false,
                    completedAt = (DateTime?)null,
                    delayType = "pending",
                    delayText = "Pending",
                    adjustmentMinutes = dailyTask?.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask?.AdjustmentReason ?? "",
                    assignedTo = dailyTask?.TaskAssignment?.Employee?.Name ?? "",
                    deadline = adjustedDeadline
                };
            }

            return Json(new
            {
                success = true,
                count = count,
                taskData = taskData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reset all tasks for branch");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ========== RESET ALL TASKS FOR ALL BRANCHES ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAllTasksForAllBranches(string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date).Date;
            var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            var allTasks = await _taskService.GetTasksVisibleOnDateAsync(taskDate);
            var totalReset = 0;
            var allTaskData = new Dictionary<string, object>(); // Key: "branchId_taskId"

            foreach (var branch in branches)
            {
                var hiddenTaskNames = branch.HiddenTasks ?? new List<string>();
                var visibleTaskIds = allTasks
                    .Where(t => !hiddenTaskNames.Contains(t.Name))
                    .Select(t => t.Id)
                    .ToList();

                var dailyTasks = await _context.DailyTasks
                    .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta != null ? ta.Employee : null)
                    .Where(dt => dt.BranchId == branch.Id &&
                                 dt.TaskDate.Date == taskDate.Date &&
                                 visibleTaskIds.Contains(dt.TaskItemId) &&
                                 dt.IsCompleted)
                    .ToListAsync();

                foreach (var dailyTask in dailyTasks)
                {
                    dailyTask.IsCompleted = false;
                    dailyTask.CompletedAt = null;
                    totalReset++;
                }

                // Build response data
                foreach (var task in allTasks.Where(t => !hiddenTaskNames.Contains(t.Name)))
                {
                    var dailyTask = dailyTasks.FirstOrDefault(dt => dt.TaskItemId == task.Id);
                    var deadline = CalculateDeadline(task, taskDate);
                    var deadlineLocal = deadline.Kind == DateTimeKind.Utc ? deadline.ToLocalTime() : deadline;
                    var adjustedDeadline = deadlineLocal.AddMinutes(dailyTask?.AdjustmentMinutes ?? 0);
                    var key = $"{branch.Id}_{task.Id}";

                    allTaskData[key] = new
                    {
                        branchId = branch.Id,
                        taskId = task.Id,
                        isCompleted = false,
                        completedAt = (DateTime?)null,
                        delayType = "pending",
                        delayText = "Pending",
                        adjustmentMinutes = dailyTask?.AdjustmentMinutes ?? 0,
                        adjustmentReason = dailyTask?.AdjustmentReason ?? "",
                        assignedTo = dailyTask?.TaskAssignment?.Employee?.Name ?? "",
                        deadline = adjustedDeadline
                    };
                }
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                count = totalReset,
                taskData = allTaskData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reset all tasks for all branches");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ========== COMPLETE ALL FOR TASK ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteAllForTask(int taskItemId, string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date).Date;
            var completionTime = DateTime.Now;
            var task = await _context.TaskItems.FindAsync(taskItemId);

            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var deadline = CalculateDeadline(task, taskDate);
            var deadlineLocal = deadline.Kind == DateTimeKind.Utc ? deadline.ToLocalTime() : deadline;
            var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            var count = 0;
            var taskData = new Dictionary<int, object>();

            foreach (var branch in branches)
            {
                if (branch.HiddenTasks != null && branch.HiddenTasks.Contains(task.Name))
                {
                    continue;
                }

                var dailyTask = await _context.DailyTasks
                    .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta != null ? ta.Employee : null)
                    .FirstOrDefaultAsync(dt => dt.BranchId == branch.Id &&
                                               dt.TaskItemId == taskItemId &&
                                               dt.TaskDate.Date == taskDate.Date);

                var completionTimeLocal = completionTime.Kind == DateTimeKind.Utc ? completionTime.ToLocalTime() : completionTime;

                if (dailyTask == null)
                {
                    dailyTask = new DailyTask
                    {
                        BranchId = branch.Id,
                        TaskItemId = taskItemId,
                        TaskDate = taskDate,
                        IsCompleted = true,
                        CompletedAt = completionTime.ToUniversalTime()
                    };
                    _context.DailyTasks.Add(dailyTask);
                    count++;
                    
                    await AutoAssignTaskToEmployee(dailyTask, taskDate);
                }
                else if (!dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = true;
                    dailyTask.CompletedAt = completionTime.ToUniversalTime();
                    count++;
                }

                var adjustedDeadline = deadlineLocal.AddMinutes(dailyTask.AdjustmentMinutes ?? 0);
                var delayType = GetDelayType(adjustedDeadline, completionTimeLocal);
                var delayText = GetDelayText(adjustedDeadline, completionTimeLocal);

                taskData[branch.Id] = new
                {
                    branchId = branch.Id,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = completionTimeLocal,
                    delayType = delayType,
                    delayText = delayText,
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = adjustedDeadline
                };
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                count = count,
                taskData = taskData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in complete all for task");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ========== SAVE ADJUSTMENT ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAdjustment(int branchId, int taskItemId, string date, int adjustmentMinutes, string adjustmentReason)
    {
        try
        {
            var taskDate = DateTime.Parse(date).Date;
            var task = await _context.TaskItems.FindAsync(taskItemId);
            
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var deadline = CalculateDeadline(task, taskDate);
            var deadlineLocal = deadline.Kind == DateTimeKind.Utc ? deadline.ToLocalTime() : deadline;

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta != null ? ta.Employee : null)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate.Date == taskDate.Date);

            if (dailyTask == null)
            {
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = taskItemId,
                    TaskDate = taskDate,
                    AdjustmentMinutes = adjustmentMinutes > 0 ? adjustmentMinutes : null,
                    AdjustmentReason = adjustmentReason
                };
                _context.DailyTasks.Add(dailyTask);
            }
            else
            {
                dailyTask.AdjustmentMinutes = adjustmentMinutes > 0 ? adjustmentMinutes : null;
                dailyTask.AdjustmentReason = adjustmentReason;
            }

            await _context.SaveChangesAsync();

            var adjustedDeadlineLocal = deadlineLocal.AddMinutes(adjustmentMinutes);
            var completedAt = dailyTask.IsCompleted ? dailyTask.CompletedAt : null;
            var completedAtLocal = completedAt.HasValue ? completedAt.Value.ToLocalTime() : (DateTime?)null;

            var response = new
            {
                success = true,
                taskData = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = dailyTask.IsCompleted,
                    completedAt = completedAtLocal,
                    delayType = GetDelayType(adjustedDeadlineLocal, completedAtLocal),
                    delayText = GetDelayText(adjustedDeadlineLocal, completedAtLocal),
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = adjustedDeadlineLocal
                }
            };

            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving adjustment");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ========== BULK UPDATE ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkUpdate(int taskItemId, DateTime completionDateTime, List<int> branchIds)
    {
        try
        {
            var task = await _context.TaskItems.FindAsync(taskItemId);
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var taskDate = completionDateTime.Date;
            var deadline = CalculateDeadline(task, taskDate);
            var deadlineLocal = deadline.Kind == DateTimeKind.Utc ? deadline.ToLocalTime() : deadline;
            var completionTimeLocal = completionDateTime.Kind == DateTimeKind.Utc ? completionDateTime.ToLocalTime() : completionDateTime;
            var count = 0;
            var taskData = new Dictionary<int, object>();

            foreach (var branchId in branchIds)
            {
                var branch = await _context.Branches.FindAsync(branchId);

                if (branch != null && branch.HiddenTasks != null && branch.HiddenTasks.Contains(task.Name))
                {
                    continue;
                }

                var dailyTask = await _context.DailyTasks
                    .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta != null ? ta.Employee : null)
                    .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                               dt.TaskItemId == taskItemId &&
                                               dt.TaskDate.Date == taskDate.Date);

                if (dailyTask == null)
                {
                    dailyTask = new DailyTask
                    {
                        BranchId = branchId,
                        TaskItemId = taskItemId,
                        TaskDate = taskDate,
                        IsCompleted = true,
                        CompletedAt = completionDateTime.ToUniversalTime(),
                        IsBulkUpdated = true,
                        BulkUpdateTime = DateTime.UtcNow
                    };
                    _context.DailyTasks.Add(dailyTask);
                    count++;
                    
                    await AutoAssignTaskToEmployee(dailyTask, taskDate);
                }
                else if (!dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = true;
                    dailyTask.CompletedAt = completionDateTime.ToUniversalTime();
                    dailyTask.IsBulkUpdated = true;
                    dailyTask.BulkUpdateTime = DateTime.UtcNow;
                    count++;
                }

                var adjustedDeadline = deadlineLocal.AddMinutes(dailyTask.AdjustmentMinutes ?? 0);
                var delayType = GetDelayType(adjustedDeadline, completionTimeLocal);
                var delayText = GetDelayText(adjustedDeadline, completionTimeLocal);

                taskData[branchId] = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = completionTimeLocal,
                    delayType = delayType,
                    delayText = delayText,
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = adjustedDeadline
                };
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                count = count,
                taskData = taskData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bulk update");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ========== SAVE NOTES ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotes(int branchId, string notes)
    {
        try
        {
            var branch = await _context.Branches.FindAsync(branchId);
            if (branch != null)
            {
                branch.Notes = notes ?? string.Empty;
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Notes saved for branch {branchId}");
            }

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving notes");
            return Json(new { success = false, message = "Error saving notes" });
        }
    }

    // ========== HELPER METHODS ==========

    private async Task<Dictionary<string, DailyTask>> GetTaskDataDictionary(DateTime date)
    {
        try
        {
            var tasks = await _context.DailyTasks
                .Include(d => d.TaskAssignment)
                    .ThenInclude(ta => ta != null ? ta.Employee : null)
                .Where(d => d.TaskDate.Date == date.Date)
                .AsNoTracking()
                .ToListAsync();

            var dict = new Dictionary<string, DailyTask>();
            foreach (var task in tasks)
            {
                dict[$"{task.BranchId}_{task.TaskItemId}"] = task;
            }
            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task data dictionary");
            return new Dictionary<string, DailyTask>();
        }
    }

    private async Task<Dictionary<string, string>> GetNotesData()
    {
        try
        {
            return await _context.Branches
                .Where(b => !string.IsNullOrEmpty(b.Notes))
                .ToDictionaryAsync(b => b.Id.ToString(), b => b.Notes ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notes data");
            return new Dictionary<string, string>();
        }
    }

    private async Task<Dictionary<int, string>> GetBranchAssignments(DateTime date)
    {
        try
        {
            var assignments = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .Where(ba => ba.StartDate.Date <= date.Date &&
                             (ba.EndDate == null || ba.EndDate.Value.Date >= date.Date))
                .AsNoTracking()
                .ToListAsync();

            return assignments
                .Where(ba => ba.Employee != null)
                .ToDictionary(
                    ba => ba.BranchId,
                    ba => ba.Employee!.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch assignments");
            return new Dictionary<int, string>();
        }
    }

    private async Task<Dictionary<int, List<string>>> GetHiddenTasks()
    {
        try
        {
            var hiddenTasks = new Dictionary<int, List<string>>();
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .AsNoTracking()
                .ToListAsync();

            foreach (var branch in branches)
            {
                hiddenTasks[branch.Id] = branch.HiddenTasks ?? new List<string>();
            }

            return hiddenTasks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hidden tasks");
            return new Dictionary<int, List<string>>();
        }
    }

    private async Task AutoAssignTaskToEmployee(DailyTask dailyTask, DateTime date)
    {
        try
        {
            var branchAssignment = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.BranchId == dailyTask.BranchId &&
                                           ba.StartDate.Date <= date.Date &&
                                           (ba.EndDate == null || ba.EndDate.Value.Date >= date.Date));

            if (branchAssignment != null && branchAssignment.EmployeeId > 0)
            {
                var existingAssignment = await _context.TaskAssignments
                    .FirstOrDefaultAsync(ta => ta.DailyTaskId == dailyTask.Id);

                if (existingAssignment == null)
                {
                    var taskAssignment = new TaskAssignment
                    {
                        DailyTaskId = dailyTask.Id,
                        EmployeeId = branchAssignment.EmployeeId,
                        AssignedAt = DateTime.UtcNow
                    };
                    _context.TaskAssignments.Add(taskAssignment);
                    await _context.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-assigning task to employee");
        }
    }

    private DateTime CalculateDeadline(TaskItem task, DateTime taskDate)
    {
        var deadline = taskDate.Date;
        if (task.IsSameDay)
            deadline = deadline.Add(task.Deadline);
        else
            deadline = deadline.AddDays(1).Add(task.Deadline);
        return deadline;
    }

    private string GetDelayType(DateTime deadline, DateTime? completedAt)
    {
        if (!completedAt.HasValue) return "pending";

        var completedTime = completedAt.Value;

        if (completedTime < deadline)
            return "early";
        if (completedTime <= deadline.AddSeconds(30))
            return "on-time";

        var diff = completedTime - deadline;
        if (diff.TotalMinutes < 60) return "minutes";
        if (diff.TotalHours < 24) return "hours";
        return "days";
    }

    private string GetDelayText(DateTime deadline, DateTime? completedAt)
    {
        if (!completedAt.HasValue) return "Pending";

        var completedTime = completedAt.Value;

        if (completedTime < deadline)
        {
            var diff = deadline - completedTime;
            if (diff.TotalDays >= 1) return $"{diff.TotalDays:F0}d early";
            if (diff.TotalHours >= 1) return $"{diff.TotalHours:F0}h early";
            if (diff.TotalMinutes >= 1) return $"{diff.TotalMinutes:F0}m early";
            return "Early";
        }

        if (completedTime <= deadline.AddSeconds(30))
            return "On time";

        var lateDiff = completedTime - deadline;
        if (lateDiff.TotalMinutes < 60) return $"{lateDiff.TotalMinutes:F0}m late";
        if (lateDiff.TotalHours < 24) return $"{lateDiff.TotalHours:F0}h late";
        return $"{lateDiff.TotalDays:F0}d late";
    }

    // ========== DIAGNOSTIC ENDPOINTS ==========
    [HttpGet]
    public async Task<IActionResult> CheckHolidays(DateTime? date)
    {
        var checkDate = date?.Date ?? DateTime.Today;
        var isHoliday = await _holidayService.IsHolidayAsync(checkDate);
        var dayOfWeek = checkDate.DayOfWeek.ToString();
        
        var weeklyHolidays = await _context.Holidays
            .Where(h => h.IsWeekly)
            .Select(h => new { h.WeekDay, h.Description })
            .ToListAsync();
        
        var specificHolidays = await _context.Holidays
            .Where(h => !h.IsWeekly && h.HolidayDate.Date == checkDate.Date)
            .ToListAsync();
        
        return Json(new
        {
            date = checkDate.ToString("yyyy-MM-dd"),
            dayOfWeek = dayOfWeek,
            isHoliday = isHoliday,
            weeklyHolidays = weeklyHolidays,
            specificHolidays = specificHolidays.Select(h => new { h.HolidayDate, h.Description })
        });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
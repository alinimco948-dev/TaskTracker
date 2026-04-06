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
    private readonly ITaskCalculationService _taskCalculationService;
    private readonly ITimezoneService _timezoneService;

    public HomeController(
        ILogger<HomeController> logger,
        ApplicationDbContext context,
        ITaskService taskService,
        IEmployeeService employeeService,
        IHolidayService holidayService,
        ITaskCalculationService taskCalculationService,
        ITimezoneService timezoneService)
    {
        _logger = logger;
        _context = context;
        _taskService = taskService;
        _employeeService = employeeService;
        _holidayService = holidayService;
        _taskCalculationService = taskCalculationService;
        _timezoneService = timezoneService;
    }

    #region Dashboard Index
public async Task<IActionResult> Index(DateTime? date)
{
    try
    {
        _logger.LogInformation("=== Dashboard Index Started ===");
        
        var localNow = _timezoneService.GetCurrentLocalTime();
        _logger.LogInformation($"Local now: {localNow}");
        
        var localDate = date?.Date ?? localNow.Date;
        _logger.LogInformation($"Local date: {localDate}");
        
        var utcStartOfDay = _timezoneService.GetStartOfDayLocal(localDate);
        var utcEndOfDay = _timezoneService.GetEndOfDayLocal(localDate);
        
        _logger.LogInformation($"UTC range: {utcStartOfDay} to {utcEndOfDay}");

        // Check if current date is a holiday
        var isHoliday = await _taskCalculationService.IsHolidayAsync(utcStartOfDay);
        var holidayName = string.Empty;
        
        if (isHoliday)
        {
            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => (!h.IsWeekly && h.HolidayDate.Date == utcStartOfDay.Date) ||
                                          (h.IsWeekly && h.WeekDay == (int)localDate.DayOfWeek));
            holidayName = holiday?.Description ?? GetHolidayNameFromDayOfWeek(localDate.DayOfWeek);
            _logger.LogInformation($"Date {localDate:yyyy-MM-dd} is a holiday: {holidayName}");
        }

        _logger.LogInformation("Getting hidden tasks...");
        var hiddenTasks = await GetHiddenTasksAsync();
        ViewBag.HiddenTasks = hiddenTasks;

        _logger.LogInformation("Getting employee scores...");
        var employeeScores = await _employeeService.GetEmployeeScoresAsync();
        ViewBag.EmployeeScores = employeeScores;

        _logger.LogInformation("Getting branches...");
        var branches = await _context.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToListAsync();
        _logger.LogInformation($"Found {branches.Count} branches");

        _logger.LogInformation("Getting visible tasks...");
        var visibleTasks = await GetTasksVisibleOnDateAsync(utcStartOfDay);
        _logger.LogInformation($"Found {visibleTasks.Count} visible tasks");

        _logger.LogInformation("Getting task data dictionary...");
        var taskData = await GetTaskDataDictionaryAsync(utcStartOfDay, utcEndOfDay);
        _logger.LogInformation($"Found {taskData.Count} task records");

        _logger.LogInformation("Filtering task data...");
        var filteredTaskData = taskData
            .Where(kvp => visibleTasks.Any(t => t.Id.ToString() == kvp.Key.Split('_')[1]))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _logger.LogInformation("Getting employees...");
        var employees = await _context.Employees
            .Include(e => e.Department)
            .Where(e => e.IsActive)
            .OrderBy(e => e.Name)
            .AsNoTracking()
            .ToListAsync();
        _logger.LogInformation($"Found {employees.Count} employees");

        _logger.LogInformation("Getting branch assignments...");
        var branchAssignments = await GetBranchAssignmentsAsync(utcStartOfDay, utcEndOfDay);
        _logger.LogInformation($"Found {branchAssignments.Count} branch assignments");

        _logger.LogInformation("Getting holidays...");
        var holidays = await _context.Holidays.AsNoTracking().ToListAsync();

        _logger.LogInformation("Creating view model...");
        var viewModel = new DashboardViewModel
        {
            CurrentDate = localDate,
            Branches = branches,
            Tasks = visibleTasks,
            Employees = employees,
            TaskData = filteredTaskData,
            NotesData = await GetNotesDataAsync(),
            Holidays = holidays,
            BranchAssignments = branchAssignments,
            IsHoliday = isHoliday,
            HolidayName = holidayName
        };

        _logger.LogInformation("=== Dashboard Index Completed Successfully ===");
        return View(viewModel);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error loading dashboard: {Message}", ex.Message);
        _logger.LogError(ex, "Stack Trace: {StackTrace}", ex.StackTrace);
        
        // Log inner exception if exists
        if (ex.InnerException != null)
        {
            _logger.LogError(ex.InnerException, "Inner exception: {Message}", ex.InnerException.Message);
        }
        
        TempData["ErrorMessage"] = $"Error loading dashboard: {ex.Message}";
        return View(new DashboardViewModel());
    }
}
    
    
    
    
    
    #endregion

    #region Private Helper Methods

    private async Task<Dictionary<string, DailyTask>> GetTaskDataDictionaryAsync(DateTime utcStart, DateTime utcEnd)
    {
        try
        {
            var tasks = await _context.DailyTasks
                .Include(d => d.TaskAssignment)
                    .ThenInclude(ta => ta != null ? ta.Employee : null)
                .Where(d => d.TaskDate >= utcStart && d.TaskDate <= utcEnd)
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

    private async Task<Dictionary<string, string>> GetNotesDataAsync()
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

    private async Task<Dictionary<int, string>> GetBranchAssignmentsAsync(DateTime utcStart, DateTime utcEnd)
    {
        try
        {
            var assignments = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .Where(ba => ba.StartDate <= utcEnd &&
                             (ba.EndDate == null || ba.EndDate >= utcStart))
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

    private async Task<Dictionary<int, List<string>>> GetHiddenTasksAsync()
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

  private async Task<List<TaskItem>> GetTasksVisibleOnDateAsync(DateTime utcDate)
{
    try
    {
        var allTasks = await _context.TaskItems
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .AsNoTracking()
            .ToListAsync();

        // Use local date for visibility check
        var localDate = _timezoneService.ConvertToLocalTime(utcDate);
        
        var visibleTasks = new List<TaskItem>();
        foreach (var task in allTasks)
        {
            try
            {
                if (_taskCalculationService.IsTaskVisibleOnDate(task, localDate))
                {
                    visibleTasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking visibility for task {TaskId}", task.Id);
                // Continue with other tasks
            }
        }
        
        return visibleTasks;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting visible tasks for date {Date}", utcDate);
        return new List<TaskItem>();
    }
}
    private async Task AutoAssignTaskToEmployeeAsync(DailyTask dailyTask, DateTime utcDate)
    {
        try
        {
            var branchAssignment = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.BranchId == dailyTask.BranchId &&
                                           ba.StartDate <= utcDate &&
                                           (ba.EndDate == null || ba.EndDate >= utcDate));

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
                    
                    _logger.LogInformation($"Auto-assigned task {dailyTask.TaskItemId} to employee {branchAssignment.EmployeeId}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-assigning task to employee");
        }
    }

    private string GetHolidayNameFromDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Friday => "Friday Holiday",
            DayOfWeek.Saturday => "Saturday Holiday",
            DayOfWeek.Sunday => "Sunday Holiday",
            _ => $"{dayOfWeek} Holiday"
        };
    }

    #endregion

    #region API Endpoints - Task Status

[HttpGet]
public async Task<IActionResult> GetTaskStatus(int branchId, int taskItemId, string date)
{
    try
    {
        // Parse the date as local
        var localDate = DateTime.Parse(date).Date;
        
        // Convert to UTC for database query
        var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
        var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
        
        var task = await _context.TaskItems.FindAsync(taskItemId);
        if (task == null)
            return Json(new { exists = false });

        var dailyTask = await _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta != null ? ta.Employee : null)
            .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                       dt.TaskItemId == taskItemId &&
                                       dt.TaskDate >= utcStart &&
                                       dt.TaskDate <= utcEnd);

        if (dailyTask == null)
        {
            return Json(new { exists = false });
        }

        var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dailyTask);

        var response = new
        {
            exists = true,
            branchId = branchId,
            taskId = taskItemId,
            isCompleted = dailyTask.IsCompleted,
            completedAt = dailyTask.CompletedAt.HasValue ? _timezoneService.ConvertToLocalTime(dailyTask.CompletedAt.Value) : (DateTime?)null,
            delayType = delayInfo.DelayType,
            delayText = delayInfo.DelayText,
            adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
            adjustmentReason = dailyTask.AdjustmentReason ?? "",
            assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
            deadline = delayInfo.Deadline,
            holidayAdjusted = delayInfo.WasAdjustedForHoliday,
            holidayNote = delayInfo.HolidayAdjustmentNote
        };

        return Json(response);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting task status: {Message}", ex.Message);
        return Json(new { exists = false, error = ex.Message });
    }
}
    #endregion

    #region API Endpoints - Update Task Time

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTaskTime(int branchId, int taskItemId, string date, DateTime completionTime)
    {
        try
        {
            var localDate = DateTime.Parse(date).Date;
            var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
            
            var task = await _context.TaskItems.FindAsync(taskItemId);
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            // Convert completion time to UTC
            var utcCompletionTime = _timezoneService.ConvertToUtc(completionTime);

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta != null ? ta.Employee : null)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate >= utcStart &&
                                           dt.TaskDate <= utcEnd);

            if (dailyTask == null)
            {
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = taskItemId,
                    TaskDate = utcStart,
                    IsCompleted = true,
                    CompletedAt = utcCompletionTime
                };
                _context.DailyTasks.Add(dailyTask);
            }
            else
            {
                dailyTask.IsCompleted = true;
                dailyTask.CompletedAt = utcCompletionTime;
            }

            await _context.SaveChangesAsync();

            await AutoAssignTaskToEmployeeAsync(dailyTask, utcStart);

            var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dailyTask);

            var response = new
            {
                success = true,
                taskData = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = _timezoneService.ConvertToLocalTime(utcCompletionTime),
                    delayType = delayInfo.DelayType,
                    delayText = delayInfo.DelayText,
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = delayInfo.Deadline,
                    holidayAdjusted = delayInfo.WasAdjustedForHoliday,
                    holidayNote = delayInfo.HolidayAdjustmentNote
                }
            };

            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating task time: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region API Endpoints - Reset Tasks

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTask(int branchId, int taskItemId, string date)
    {
        try
        {
            var localDate = DateTime.Parse(date).Date;
            var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);

            var dailyTask = await _context.DailyTasks
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate >= utcStart &&
                                           dt.TaskDate <= utcEnd);

            if (dailyTask != null)
            {
                dailyTask.IsCompleted = false;
                dailyTask.CompletedAt = null;
                await _context.SaveChangesAsync();
            }

            var task = await _context.TaskItems.FindAsync(taskItemId);
            var deadline = _taskCalculationService.CalculateDeadline(task, utcStart);
            var adjustedDeadline = deadline.AddMinutes(dailyTask?.AdjustmentMinutes ?? 0);
            var localDeadline = _timezoneService.ConvertToLocalTime(adjustedDeadline);

            return Json(new
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
                    deadline = localDeadline
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting task: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAllForTask(int taskItemId, string date)
    {
        try
        {
            var localDate = DateTime.Parse(date).Date;
            var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
            
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
                    .FirstOrDefaultAsync(dt => dt.BranchId == branch.Id &&
                                               dt.TaskItemId == taskItemId &&
                                               dt.TaskDate >= utcStart &&
                                               dt.TaskDate <= utcEnd);

                if (dailyTask != null && dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = false;
                    dailyTask.CompletedAt = null;
                    count++;
                }

                var deadline = _taskCalculationService.CalculateDeadline(task, utcStart);
                var adjustedDeadline = deadline.AddMinutes(dailyTask?.AdjustmentMinutes ?? 0);
                var localDeadline = _timezoneService.ConvertToLocalTime(adjustedDeadline);

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
                    deadline = localDeadline
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
            _logger.LogError(ex, "Error in reset all for task: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAllTasksForBranch(int branchId, string date)
    {
        try
        {
            var localDate = DateTime.Parse(date).Date;
            var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
            
            var dailyTasks = await _context.DailyTasks
                .Where(dt => dt.BranchId == branchId && 
                             dt.TaskDate >= utcStart && 
                             dt.TaskDate <= utcEnd &&
                             dt.IsCompleted)
                .ToListAsync();
            
            var count = 0;
            foreach (var task in dailyTasks)
            {
                task.IsCompleted = false;
                task.CompletedAt = null;
                count++;
            }
            
            await _context.SaveChangesAsync();
            return Json(new { success = true, count = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting all tasks for branch: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAllTasksForAllBranches(string date)
    {
        try
        {
            var localDate = DateTime.Parse(date).Date;
            var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
            
            var dailyTasks = await _context.DailyTasks
                .Where(dt => dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd && dt.IsCompleted)
                .ToListAsync();
            
            var count = 0;
            foreach (var task in dailyTasks)
            {
                task.IsCompleted = false;
                task.CompletedAt = null;
                count++;
            }
            
            await _context.SaveChangesAsync();
            return Json(new { success = true, count = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting all tasks for all branches: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region API Endpoints - Complete Tasks

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteAllForTask(int taskItemId, string date)
    {
        try
        {
            var localDate = DateTime.Parse(date).Date;
            var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
            var completionTime = DateTime.UtcNow;
            
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
                    .FirstOrDefaultAsync(dt => dt.BranchId == branch.Id &&
                                               dt.TaskItemId == taskItemId &&
                                               dt.TaskDate >= utcStart &&
                                               dt.TaskDate <= utcEnd);

                if (dailyTask == null)
                {
                    dailyTask = new DailyTask
                    {
                        BranchId = branch.Id,
                        TaskItemId = taskItemId,
                        TaskDate = utcStart,
                        IsCompleted = true,
                        CompletedAt = completionTime,
                        IsBulkUpdated = true,
                        BulkUpdateTime = DateTime.UtcNow
                    };
                    _context.DailyTasks.Add(dailyTask);
                    count++;
                    
                    await AutoAssignTaskToEmployeeAsync(dailyTask, utcStart);
                }
                else if (!dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = true;
                    dailyTask.CompletedAt = completionTime;
                    count++;
                }

                var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dailyTask);

                taskData[branch.Id] = new
                {
                    branchId = branch.Id,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = _timezoneService.ConvertToLocalTime(completionTime),
                    delayType = delayInfo.DelayType,
                    delayText = delayInfo.DelayText,
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = delayInfo.Deadline,
                    holidayAdjusted = delayInfo.WasAdjustedForHoliday,
                    holidayNote = delayInfo.HolidayAdjustmentNote
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
            _logger.LogError(ex, "Error in complete all for task: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region API Endpoints - Adjustments

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAdjustment(int branchId, int taskItemId, string date, int adjustmentMinutes, string adjustmentReason)
    {
        try
        {
            var localDate = DateTime.Parse(date).Date;
            var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
            
            var task = await _context.TaskItems.FindAsync(taskItemId);
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var dailyTask = await _context.DailyTasks
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate >= utcStart &&
                                           dt.TaskDate <= utcEnd);

            if (dailyTask == null)
            {
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = taskItemId,
                    TaskDate = utcStart,
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

            var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dailyTask);

            var response = new
            {
                success = true,
                taskData = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = dailyTask.IsCompleted,
                    completedAt = dailyTask.CompletedAt.HasValue ? _timezoneService.ConvertToLocalTime(dailyTask.CompletedAt.Value) : (DateTime?)null,
                    delayType = delayInfo.DelayType,
                    delayText = delayInfo.DelayText,
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = delayInfo.Deadline,
                    holidayAdjusted = delayInfo.WasAdjustedForHoliday,
                    holidayNote = delayInfo.HolidayAdjustmentNote
                }
            };

            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving adjustment: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region API Endpoints - Notes

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
            _logger.LogError(ex, "Error saving notes: {Message}", ex.Message);
            return Json(new { success = false, message = "Error saving notes" });
        }
    }

    #endregion

    #region API Endpoints - Bulk Update

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkUpdate(int taskItemId, DateTime completionDateTime, List<int> branchIds)
    {
        try
        {
            var task = await _context.TaskItems.FindAsync(taskItemId);
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var localDate = completionDateTime.Date;
            var utcTaskDate = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
            var utcCompletion = _timezoneService.ConvertToUtc(completionDateTime);
            
            var count = 0;
            var taskData = new Dictionary<int, object>();

            foreach (var branchId in branchIds)
            {
                var branch = await _context.Branches.FindAsync(branchId);

                if (branch != null && branch.HiddenTasks != null && branch.HiddenTasks.Contains(task.Name))
                    continue;

                var dailyTask = await _context.DailyTasks
                    .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                               dt.TaskItemId == taskItemId &&
                                               dt.TaskDate >= utcTaskDate &&
                                               dt.TaskDate <= utcEnd);

                if (dailyTask == null)
                {
                    dailyTask = new DailyTask
                    {
                        BranchId = branchId,
                        TaskItemId = taskItemId,
                        TaskDate = utcTaskDate,
                        IsCompleted = true,
                        CompletedAt = utcCompletion,
                        IsBulkUpdated = true,
                        BulkUpdateTime = DateTime.UtcNow
                    };
                    _context.DailyTasks.Add(dailyTask);
                    count++;
                    
                    await AutoAssignTaskToEmployeeAsync(dailyTask, utcTaskDate);
                }
                else if (!dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = true;
                    dailyTask.CompletedAt = utcCompletion;
                    dailyTask.IsBulkUpdated = true;
                    dailyTask.BulkUpdateTime = DateTime.UtcNow;
                    count++;
                }

                var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dailyTask);

                taskData[branchId] = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = _timezoneService.ConvertToLocalTime(utcCompletion),
                    delayType = delayInfo.DelayType,
                    delayText = delayInfo.DelayText,
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = delayInfo.Deadline,
                    holidayAdjusted = delayInfo.WasAdjustedForHoliday,
                    holidayNote = delayInfo.HolidayAdjustmentNote
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
            _logger.LogError(ex, "Error in bulk update: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region Diagnostic Endpoints

    [HttpGet]
    public async Task<IActionResult> DebugDateRange(DateTime? date)
    {
        var localDate = date?.Date ?? _timezoneService.GetCurrentLocalTime().Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
        var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
        
        var dailyTasks = await _context.DailyTasks
            .Where(dt => dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd)
            .Select(dt => new { dt.Id, dt.BranchId, dt.TaskItemId, dt.TaskDate, dt.IsCompleted })
            .ToListAsync();
        
        var branchAssignments = await _context.BranchAssignments
            .Where(ba => ba.StartDate <= utcEnd && (ba.EndDate == null || ba.EndDate >= utcStart))
            .Select(ba => new { ba.Id, ba.BranchId, ba.EmployeeId, ba.StartDate, ba.EndDate })
            .ToListAsync();
        
        return Json(new
        {
            localDate = localDate.ToString("yyyy-MM-dd"),
            utcRange = new { start = utcStart.ToString("yyyy-MM-dd HH:mm:ss"), end = utcEnd.ToString("yyyy-MM-dd HH:mm:ss") },
            serverTimeUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            serverTimeLocal = _timezoneService.GetCurrentLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            timezone = _timezoneService.GetTimezoneDisplayName(),
            dailyTasksCount = dailyTasks.Count,
            dailyTasks = dailyTasks.Take(10),
            branchAssignmentsCount = branchAssignments.Count,
            branchAssignments = branchAssignments.Take(10)
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboardStats(DateTime date)
    {
        var utcStart = _timezoneService.GetStartOfDayLocal(date);
        var utcEnd = _timezoneService.GetEndOfDayLocal(date);
        
        var dailyTasks = await _context.DailyTasks
            .Where(dt => dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd)
            .ToListAsync();
        
        var completed = dailyTasks.Count(dt => dt.IsCompleted);
        var pending = dailyTasks.Count(dt => !dt.IsCompleted);
        var completionRate = (completed + pending) > 0 ? Math.Round((double)completed / (completed + pending) * 100) : 0;
        
        return Json(new { completed, pending, completionRate });
    }

    [HttpGet]
    public async Task<IActionResult> GetSparklineData(DateTime date)
    {
        var result = new Dictionary<int, List<int>>();
        var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
        
        for (int i = 6; i >= 0; i--)
        {
            var pastDate = date.AddDays(-i);
            var utcStart = _timezoneService.GetStartOfDayLocal(pastDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(pastDate);
            
            foreach (var branch in branches)
            {
                if (!result.ContainsKey(branch.Id))
                    result[branch.Id] = new List<int>();
                
                var dailyTasks = await _context.DailyTasks
                    .Where(dt => dt.BranchId == branch.Id && dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd)
                    .ToListAsync();
                
                var completed = dailyTasks.Count(dt => dt.IsCompleted);
                result[branch.Id].Add(completed);
            }
        }
        
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> CheckHolidays(DateTime? date)
    {
        var checkDate = date?.Date ?? _timezoneService.GetCurrentLocalTime().Date;
        var utcDate = _timezoneService.GetStartOfDayLocal(checkDate);
        var isHoliday = await _taskCalculationService.IsHolidayAsync(utcDate);
        var dayOfWeek = checkDate.DayOfWeek.ToString();
        
        var weeklyHolidays = await _context.Holidays
            .Where(h => h.IsWeekly)
            .Select(h => new { h.WeekDay, h.Description })
            .ToListAsync();
        
        var specificHolidays = await _context.Holidays
            .Where(h => !h.IsWeekly && h.HolidayDate.Date == utcDate.Date)
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
// Add this diagnostic action to HomeController temporarily
[HttpGet]
public async Task<IActionResult> TestDatabase()
{
    try
    {
        var canConnect = await _context.Database.CanConnectAsync();
        var branchesCount = await _context.Branches.CountAsync();
        var tasksCount = await _context.TaskItems.CountAsync();
        
        return Json(new { 
            success = true, 
            canConnect, 
            branchesCount, 
            tasksCount,
            message = "Database connection successful" 
        });
    }
    catch (Exception ex)
    {
        return Json(new { success = false, error = ex.Message });
    }
}
[HttpGet]
public async Task<IActionResult> DebugTaskDates(int branchId, int taskId, string date)
{
    try
    {
        var localDate = DateTime.Parse(date).Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
        var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
        
        var dailyTask = await _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                       dt.TaskItemId == taskId &&
                                       dt.TaskDate >= utcStart &&
                                       dt.TaskDate <= utcEnd);
        
        if (dailyTask == null)
        {
            return Json(new { 
                exists = false,
                localDate = localDate.ToString("yyyy-MM-dd HH:mm:ss"),
                utcStart = utcStart.ToString("yyyy-MM-dd HH:mm:ss"),
                utcEnd = utcEnd.ToString("yyyy-MM-dd HH:mm:ss"),
                serverTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                localTime = _timezoneService.GetCurrentLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        
        var deadline = _taskCalculationService.CalculateDeadline(dailyTask.TaskItem, dailyTask.TaskDate);
        
        return Json(new
        {
            exists = true,
            storedTaskDate = dailyTask.TaskDate.ToString("yyyy-MM-dd HH:mm:ss"),
            storedTaskDateKind = dailyTask.TaskDate.Kind.ToString(),
            storedCompletedAt = dailyTask.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            storedCompletedAtKind = dailyTask.CompletedAt?.Kind.ToString(),
            calculatedDeadline = deadline.ToString("yyyy-MM-dd HH:mm:ss"),
            deadlineKind = deadline.Kind.ToString(),
            localDeadline = _timezoneService.ConvertToLocalTime(deadline).ToString("yyyy-MM-dd HH:mm:ss"),
            isCompleted = dailyTask.IsCompleted,
            isOnTime = _taskCalculationService.IsTaskOnTime(dailyTask)
        });
    }
    catch (Exception ex)
    {
        return Json(new { error = ex.Message });
    }
}
[HttpGet]
public IActionResult Test()
{
    try
    {
        var now = _timezoneService.GetCurrentLocalTime();
        return Content($"Timezone test passed: {now}");
    }
    catch (Exception ex)
    {
        return Content($"Error: {ex.Message}");
    }
}
    #endregion

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services;
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
// Add this method to HomeController.cs
public async Task<IActionResult> DashboardDiagnostic()
{
    try
    {
        var currentDate = DateTime.Today;
        
        var branches = await _context.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync();
        
        var tasks = await _taskService.GetTasksVisibleOnDateAsync(currentDate);
        
        var hiddenTasks = await GetHiddenTasks();
        
        var taskData = await GetTaskDataDictionary(currentDate);
        
        var result = new
        {
            BranchCount = branches.Count,
            Branches = branches.Select(b => new { b.Id, b.Name, b.IsActive }),
            TaskCount = tasks.Count,
            Tasks = tasks.Select(t => new { t.Id, t.Name }),
            HiddenTasksCount = hiddenTasks.Count,
            TaskDataCount = taskData.Count,
            SampleTaskData = taskData.Take(5).Select(kvp => new { Key = kvp.Key, Completed = kvp.Value?.IsCompleted })
        };
        
        return Json(result);
    }
    catch (Exception ex)
    {
        return Content($"Error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "text/plain");
    }
}
    public async Task<IActionResult> Index(DateTime? date)
    {
        try
        {
            var currentDate = date?.Date ?? DateTime.Today;

            ViewBag.CurrentDate = currentDate;
            
            var hiddenTasks = await GetHiddenTasks();
            ViewBag.HiddenTasks = hiddenTasks;
            
            var employeeScores = await _employeeService.GetEmployeeScoresAsync();
            ViewBag.EmployeeScores = employeeScores;

            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .AsNoTracking()
                .ToListAsync();
            ViewBag.Branches = branches;

            var visibleTasks = await _taskService.GetTasksVisibleOnDateAsync(currentDate);
            ViewBag.Tasks = visibleTasks;

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

       var viewModel = new DashboardViewModel
{
    CurrentDate = currentDate,
    Branches = branches,
    Tasks = visibleTasks,
    Employees = employees,
    TaskData = filteredTaskData,
    NotesData = await GetNotesData(), // This now handles NULLs
    Holidays = await _context.Holidays.AsNoTracking().ToListAsync(),
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

    // TEST ENDPOINT - Check database connection and data
    public async Task<IActionResult> TestDb()
    {
        try
        {
            var branchCount = await _context.Branches.CountAsync();
            var taskCount = await _context.TaskItems.CountAsync();
            var departmentCount = await _context.Departments.CountAsync();
            
            var branches = await _context.Branches.Take(5).Select(b => b.Name).ToListAsync();
            var tasks = await _context.TaskItems.Take(5).Select(t => t.Name).ToListAsync();
            
            var result = $@"
=== DATABASE STATUS ===
Connection: SUCCESS

Table Counts:
- Departments: {departmentCount}
- Branches: {branchCount}
- Tasks: {taskCount}

Sample Branches (first 5):
{string.Join("\n", branches.Select((b, i) => $"  {i+1}. {b}"))}

Sample Tasks (first 5):
{string.Join("\n", tasks.Select((t, i) => $"  {i+1}. {t}"))}

=== END ===
";
            
            return Content(result, "text/plain");
        }
        catch (Exception ex)
        {
            return Content($"ERROR: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "text/plain");
        }
    }

    // TEST ENDPOINT - Check database schema
    public async Task<IActionResult> TestSchema()
    {
        try
        {
            var tables = new List<string>();
            
            // Get all tables using raw SQL
            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = @"
                    SELECT table_name 
                    FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_type = 'BASE TABLE'
                    ORDER BY table_name;
                ";
                
                await _context.Database.OpenConnectionAsync();
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        tables.Add(reader.GetString(0));
                    }
                }
                _context.Database.CloseConnection();
            }
            
            var result = $"=== DATABASE SCHEMA ===\n\nTables in database:\n{string.Join("\n", tables.Select((t, i) => $"  {i+1}. {t}"))}\n\n=== END ===";
            return Content(result, "text/plain");
        }
        catch (Exception ex)
        {
            return Content($"ERROR: {ex.Message}", "text/plain");
        }
    }

    private async Task<Dictionary<string, DailyTask>> GetTaskDataDictionary(DateTime date)
    {
        try
        {
            var tasks = await _context.DailyTasks
                .Include(d => d.TaskAssignment)
                    .ThenInclude(ta => ta.Employee)
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
        // Only include branches that have notes
        var branchesWithNotes = await _context.Branches
            .Where(b => b.Notes != null && b.Notes.Trim() != "")
            .Select(b => new { b.Id, b.Notes })
            .ToListAsync();
        
        var dict = new Dictionary<string, string>();
        foreach (var branch in branchesWithNotes)
        {
            dict[branch.Id.ToString()] = branch.Notes ?? string.Empty;
        }
        return dict;
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTaskTime(int branchId, int taskItemId, string date, DateTime completionTime)
    {
        try
        {
            var taskDate = DateTime.Parse(date);
            var task = await _context.TaskItems.FindAsync(taskItemId);
            
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var deadline = CalculateDeadline(task, taskDate);

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta.Employee)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate.Date == taskDate.Date);

            if (dailyTask == null)
            {
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = taskItemId,
                    TaskDate = taskDate.Date,
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

            var response = new
            {
                success = true,
                taskData = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = completionTime,
                    delayType = GetDelayType(deadline, completionTime),
                    delayText = GetDelayText(deadline, completionTime),
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = deadline
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

    private async Task AutoAssignTaskToEmployee(DailyTask dailyTask, DateTime date)
    {
        try
        {
            var branchAssignment = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.BranchId == dailyTask.BranchId &&
                                           ba.StartDate.Date <= date.Date &&
                                           (ba.EndDate == null || ba.EndDate.Value.Date >= date.Date));

            if (branchAssignment != null)
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

    [HttpGet]
    public async Task<IActionResult> GetTaskStatus(int branchId, int taskItemId, string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date);
            var task = await _context.TaskItems.FindAsync(taskItemId);
            
            if (task == null)
                return Json(new { exists = false });

            var deadline = CalculateDeadline(task, taskDate);

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta.Employee)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate.Date == taskDate.Date);

            if (dailyTask == null)
            {
                return Json(new { exists = false });
            }

            var response = new
            {
                exists = true,
                branchId = branchId,
                taskId = taskItemId,
                isCompleted = dailyTask.IsCompleted,
                completedAt = dailyTask.CompletedAt,
                delayType = GetDelayType(deadline, dailyTask.CompletedAt),
                delayText = GetDelayText(deadline, dailyTask.CompletedAt),
                adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                adjustmentReason = dailyTask.AdjustmentReason ?? "",
                assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                deadline = deadline
            };

            return Json(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task status");
            return Json(new { exists = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTask(int branchId, int taskItemId, string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date);
            var task = await _context.TaskItems.FindAsync(taskItemId);
            
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var deadline = CalculateDeadline(task, taskDate);

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta.Employee)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate.Date == taskDate.Date);

            if (dailyTask != null)
            {
                dailyTask.IsCompleted = false;
                dailyTask.CompletedAt = null;
                await _context.SaveChangesAsync();
            }

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
                    deadline = deadline
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteAllForTask(int taskItemId, string date)
    {
        try
        {
            var taskDate = DateTime.Parse(date);
            var completionTime = DateTime.Now;
            var task = await _context.TaskItems.FindAsync(taskItemId);

            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var deadline = CalculateDeadline(task, taskDate);
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
                    .ThenInclude(ta => ta.Employee)
                    .FirstOrDefaultAsync(dt => dt.BranchId == branch.Id &&
                                               dt.TaskItemId == taskItemId &&
                                               dt.TaskDate.Date == taskDate.Date);

                if (dailyTask == null)
                {
                    dailyTask = new DailyTask
                    {
                        BranchId = branch.Id,
                        TaskItemId = taskItemId,
                        TaskDate = taskDate.Date,
                        IsCompleted = true,
                        CompletedAt = completionTime.ToUniversalTime()
                    };
                    _context.DailyTasks.Add(dailyTask);
                    count++;
                }
                else if (!dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = true;
                    dailyTask.CompletedAt = completionTime.ToUniversalTime();
                    count++;
                }

                taskData[branch.Id] = new
                {
                    branchId = branch.Id,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = completionTime,
                    delayType = GetDelayType(deadline, completionTime),
                    delayText = GetDelayText(deadline, completionTime),
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = deadline
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAdjustment(int branchId, int taskItemId, string date, int adjustmentMinutes, string adjustmentReason)
    {
        try
        {
            var taskDate = DateTime.Parse(date);
            var task = await _context.TaskItems.FindAsync(taskItemId);
            
            if (task == null)
                return Json(new { success = false, message = "Task not found" });

            var deadline = CalculateDeadline(task, taskDate);

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta.Employee)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskItemId &&
                                           dt.TaskDate.Date == taskDate.Date);

            if (dailyTask == null)
            {
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = taskItemId,
                    TaskDate = taskDate.Date,
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

            var adjustedDeadline = deadline.AddMinutes(adjustmentMinutes);

            var response = new
            {
                success = true,
                taskData = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = dailyTask.IsCompleted,
                    completedAt = dailyTask.CompletedAt,
                    delayType = GetDelayType(adjustedDeadline, dailyTask.CompletedAt),
                    delayText = GetDelayText(adjustedDeadline, dailyTask.CompletedAt),
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = adjustedDeadline
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
                    .ThenInclude(ta => ta.Employee)
                    .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                               dt.TaskItemId == taskItemId &&
                                               dt.TaskDate.Date == taskDate.Date);

                if (dailyTask == null)
                {
                    dailyTask = new DailyTask
                    {
                        BranchId = branchId,
                        TaskItemId = taskItemId,
                        TaskDate = taskDate.Date,
                        IsCompleted = true,
                        CompletedAt = completionDateTime.ToUniversalTime(),
                        IsBulkUpdated = true,
                        BulkUpdateTime = DateTime.UtcNow
                    };
                    _context.DailyTasks.Add(dailyTask);
                    count++;
                }
                else if (!dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = true;
                    dailyTask.CompletedAt = completionDateTime.ToUniversalTime();
                    dailyTask.IsBulkUpdated = true;
                    dailyTask.BulkUpdateTime = DateTime.UtcNow;
                    count++;
                }

                taskData[branchId] = new
                {
                    branchId = branchId,
                    taskId = taskItemId,
                    isCompleted = true,
                    completedAt = completionDateTime,
                    delayType = GetDelayType(deadline, completionDateTime),
                    delayText = GetDelayText(deadline, completionDateTime),
                    adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                    adjustmentReason = dailyTask.AdjustmentReason ?? "",
                    assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                    deadline = deadline
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotes(int branchId, string notes)
    {
        try
        {
            var branch = await _context.Branches.FindAsync(branchId);
            if (branch != null)
            {
                branch.Notes = notes;
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

        if (completedTime < deadline) return "early";
        if (completedTime <= deadline.AddSeconds(30)) return "on-time";

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

        if (completedTime <= deadline.AddSeconds(30)) return "On time";

        var lateDiff = completedTime - deadline;
        if (lateDiff.TotalMinutes < 60) return $"{lateDiff.TotalMinutes:F0}m late";
        if (lateDiff.TotalHours < 24) return $"{lateDiff.TotalHours:F0}h late";
        return $"{lateDiff.TotalDays:F0}d late";
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

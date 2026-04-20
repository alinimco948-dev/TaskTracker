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
    private readonly IDashboardService _dashboardService;

    public HomeController(
        ILogger<HomeController> logger,
        ApplicationDbContext context,
        ITaskService taskService,
        IEmployeeService employeeService,
        IHolidayService holidayService,
        ITaskCalculationService taskCalculationService,
        ITimezoneService timezoneService,
        IDashboardService dashboardService)
    {
        _logger = logger;
        _context = context;
        _taskService = taskService;
        _employeeService = employeeService;
        _holidayService = holidayService;
        _taskCalculationService = taskCalculationService;
        _timezoneService = timezoneService;
        _dashboardService = dashboardService;
    }

    #region Dashboard Index

  public async Task<IActionResult> Index(DateTime? date)
{
    try
    {
        _logger.LogInformation("=== Dashboard Index Started ===");
        
        var localNow = _timezoneService.GetCurrentLocalTime();
        var localDate = date?.Date ?? localNow.Date;
        
        var viewModel = await _dashboardService.GetDashboardViewModelAsync(localDate);
        
        // ADD THIS LINE to populate hidden tasks
        ViewBag.HiddenTasks = await _taskService.GetHiddenTasksAsync();
        
        _logger.LogInformation("=== Dashboard Index Completed Successfully ===");
        return View(viewModel);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error loading dashboard: {Message}", ex.Message);
        TempData["ErrorMessage"] = $"Error loading dashboard: {ex.Message}";
        return View(new DashboardViewModel());
    }
}
    #endregion
[HttpGet]
public IActionResult TestTimezone()
{
    var utcNow = DateTime.UtcNow;
    var localNow = _timezoneService.GetCurrentLocalTime();
    var timezoneName = _timezoneService.GetTimezoneDisplayName();
    
    return Json(new
    {
        utcNow = utcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        localNow = localNow.ToString("yyyy-MM-dd HH:mm:ss"),
        timezone = timezoneName,
        utcOffset = TimeZoneInfo.Local.BaseUtcOffset.ToString()
    });
}

[HttpGet]
public IActionResult TestGrading()
{
    var tests = new List<Dictionary<string, object>>();
    
    // Test 1: Zero tasks
    tests.Add(new Dictionary<string, object> { { "name", "Zero tasks returns 0" }, { "expected", 0.0 }, { "actual", Services.TaskCalculationService.CalculateWeightedScoreStatic(0, 0, 0) } });
    // Test 2: 100% / 100%
    tests.Add(new Dictionary<string, object> { { "name", "100% completion, 100% on-time" }, { "expected", 100.0 }, { "actual", Services.TaskCalculationService.CalculateWeightedScoreStatic(10, 10, 10) } });
    // Test 3: 70% / 100%
    tests.Add(new Dictionary<string, object> { { "name", "70% completion, 100% on-time" }, { "expected", 79.0 }, { "actual", Services.TaskCalculationService.CalculateWeightedScoreStatic(10, 7, 7) } });
    // Test 4: 50% / 100%
    tests.Add(new Dictionary<string, object> { { "name", "50% completion, 100% on-time" }, { "expected", 65.0 }, { "actual", Services.TaskCalculationService.CalculateWeightedScoreStatic(10, 5, 5) } });
    // Test 5: 70% / 50%
    tests.Add(new Dictionary<string, object> { { "name", "70% completion, 50% on-time" }, { "expected", 64.0 }, { "actual", Services.TaskCalculationService.CalculateWeightedScoreStatic(10, 7, 3) } });
    // Test 6: 0% completion
    tests.Add(new Dictionary<string, object> { { "name", "0% completion = 0%" }, { "expected", 0.0 }, { "actual", Services.TaskCalculationService.CalculateWeightedScoreStatic(10, 0, 0) } });
    // Test 7: All late
    tests.Add(new Dictionary<string, object> { { "name", "100% late = 70%" }, { "expected", 70.0 }, { "actual", Services.TaskCalculationService.CalculateWeightedScoreStatic(10, 10, 0) } });
    
    var results = new List<object>();
    int passed = 0;
    foreach (var t in tests)
    {
        var expected = Convert.ToDouble(t["expected"]);
        var actual = Convert.ToDouble(t["actual"]);
        var pass = Math.Abs(expected - actual) < 0.1;
        if (pass) passed++;
        results.Add(new { name = t["name"], expected, actual, passed = pass });
    }
    
    return Json(new {
        totalTests = results.Count,
        passed,
        failed = results.Count - passed,
        results
    });
}
    #region API Endpoints - Batch Operations

[HttpGet]
public async Task<IActionResult> GetAllTaskStatuses(string date, string branchIds, string taskIds)
{
    try
    {
        var localDate = DateTime.Parse(date).Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
        var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
        
        var branchIdList = !string.IsNullOrEmpty(branchIds) 
            ? branchIds.Split(',').Select(int.Parse).ToList() 
            : null;
        var taskIdList = !string.IsNullOrEmpty(taskIds) 
            ? taskIds.Split(',').Select(int.Parse).ToList() 
            : null;
        
        var query = _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta!.Employee)
            .Where(dt => dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd);
        
        if (branchIdList?.Any() == true)
            query = query.Where(dt => branchIdList.Contains(dt.BranchId));
        if (taskIdList?.Any() == true)
            query = query.Where(dt => taskIdList.Contains(dt.TaskItemId));
        
        var dailyTasks = await query.ToListAsync();
        
        var result = new Dictionary<string, object>();
        
        foreach (var dt in dailyTasks)
        {
            if (dt.TaskItem == null) continue;
            
            var key = $"{dt.BranchId}_{dt.TaskItemId}";
            var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dt);
            
            result[key] = new
            {
                exists = true,
                branchId = dt.BranchId,
                taskId = dt.TaskItemId,
                isCompleted = dt.IsCompleted,
                completedAt = dt.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                delayType = delayInfo.DelayType,
                delayText = delayInfo.DelayText,
                adjustmentMinutes = dt.AdjustmentMinutes ?? 0,
                adjustmentReason = dt.AdjustmentReason ?? "",
                assignedTo = dt.TaskAssignment?.Employee?.Name ?? "",
                deadline = delayInfo.Deadline?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                isOnTime = delayInfo.IsOnTime,
                holidayAdjusted = delayInfo.WasAdjustedForHoliday,
                holidayNote = delayInfo.HolidayAdjustmentNote
            };
        }
        
        return Json(result);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting all task statuses");
        return Json(new Dictionary<string, object>());
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
        _logger.LogInformation($"Saving notes for branch {branchId}");
        
        var branch = await _context.Branches.FindAsync(branchId);
        if (branch == null)
        {
            return Json(new { success = false, message = "Branch not found" });
        }
        
        branch.Notes = notes ?? string.Empty;
        branch.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        
        _logger.LogInformation($"Notes saved successfully for branch {branch.Name}");
        return Json(new { success = true });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error saving notes for branch {BranchId}", branchId);
        return Json(new { success = false, message = ex.Message });
    }
}

#endregion
#region API Endpoints - Task Status
[HttpGet]
public async Task<IActionResult> GetTaskStatus(int branchId, int taskItemId, string date)
{
    try
    {
        var localDate = DateTime.Parse(date).Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
        var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
        
        var task = await _context.TaskItems.FindAsync(taskItemId);
        if (task == null)
            return Json(new { exists = false });

        var dailyTask = await _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta!.Employee)
            .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                       dt.TaskItemId == taskItemId &&
                                       dt.TaskDate >= utcStart &&
                                       dt.TaskDate <= utcEnd);

        if (dailyTask == null)
        {
            return Json(new { exists = false });
        }

        // Calculate all values for debug
        var localTaskDate = _timezoneService.ConvertToLocalTime(dailyTask.TaskDate);
        var deadline = dailyTask.TaskItem != null 
            ? await _taskCalculationService.CalculateDeadline(dailyTask.TaskItem, dailyTask.TaskDate)
            : DateTime.MaxValue;
        var localDeadline = _timezoneService.ConvertToLocalTime(deadline);
        DateTime? localCompleted = null;
        double diffMinutes = 0;
        string diffResult = "N/A";
        
        if (dailyTask.CompletedAt.HasValue)
        {
            localCompleted = _timezoneService.ConvertToLocalTime(dailyTask.CompletedAt.Value);
            diffMinutes = (localCompleted.Value - localDeadline).TotalMinutes;
            diffResult = diffMinutes > 0 ? "LATE" : (diffMinutes < 0 ? "EARLY" : "ON TIME");
        }

        var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dailyTask);

        string? completedAtString = dailyTask.CompletedAt.HasValue 
            ? dailyTask.CompletedAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ") 
            : null;
        
        string? deadlineString = delayInfo.Deadline.HasValue 
            ? delayInfo.Deadline.Value.ToString("yyyy-MM-ddTHH:mm:ssZ") 
            : null;

        var response = new
        {
            exists = true,
            branchId = branchId,
            taskId = taskItemId,
            isCompleted = dailyTask.IsCompleted,
            completedAt = completedAtString,
            delayType = delayInfo.DelayType,
            delayText = delayInfo.DelayText,
            adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
            adjustmentReason = dailyTask.AdjustmentReason ?? "",
            assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
            deadline = deadlineString,
            isOnTime = delayInfo.IsOnTime,
            holidayAdjusted = delayInfo.WasAdjustedForHoliday,
            holidayNote = delayInfo.HolidayAdjustmentNote,
            // ========== DEBUG INFO ==========
            debug = new
            {
                taskName = dailyTask.TaskItem?.Name ?? "Unknown",
                isSameDay = dailyTask.TaskItem?.IsSameDay ?? false,
                deadlineTime = dailyTask.TaskItem?.Deadline.ToString() ?? "N/A",
                taskDateUtc = dailyTask.TaskDate.ToString("yyyy-MM-dd HH:mm:ss"),
                taskDateLocal = localTaskDate.ToString("yyyy-MM-dd HH:mm:ss"),
                deadlineUtc = deadline.ToString("yyyy-MM-dd HH:mm:ss"),
                deadlineLocal = localDeadline.ToString("yyyy-MM-dd HH:mm:ss"),
                completedAtLocal = localCompleted?.ToString("yyyy-MM-dd HH:mm:ss"),
                diffMinutes = diffMinutes,
                diffResult = diffResult
            }
        };

        return Json(response);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting task status: {Message}", ex.Message);
        return Json(new { exists = false, error = ex.Message });
    }
}


[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> BulkUpdate([FromBody] BulkUpdateRequest request)
{
    try
    {
        _logger.LogInformation($"BulkUpdate: TaskId={request.taskItemId}, DateTime={request.completionDateTime}, ViewingDate={request.viewingDate}, Branches={request.branchIds?.Count ?? 0}");
        
        if (request.branchIds == null || !request.branchIds.Any())
        {
            return Json(new { success = false, message = "No branches selected" });
        }
        
        var task = await _context.TaskItems.FindAsync(request.taskItemId);
        if (task == null)
        {
            return Json(new { success = false, message = "Task not found" });
        }
        
        DateTime localCompletionTime;
        if (!DateTime.TryParse(request.completionDateTime, out localCompletionTime))
        {
            return Json(new { success = false, message = "Invalid completion time" });
        }
        
        DateTime localViewingDate;
        if (!DateTime.TryParse(request.viewingDate, out localViewingDate))
        {
            _logger.LogWarning("No viewing date provided or invalid format '{ViewingDate}', defaulting to today", request.viewingDate);
            localViewingDate = _timezoneService.GetCurrentLocalTime().Date;
        }
        
        _logger.LogInformation("Viewing date parsed: {LocalViewingDate}, Date parts: Year={Year}, Month={Month}, Day={Day}", 
            localViewingDate, localViewingDate.Year, localViewingDate.Month, localViewingDate.Day);
        
        var utcCompletionTime = _timezoneService.ConvertToUtc(localCompletionTime);
        var utcStart = _timezoneService.GetStartOfDayLocal(localViewingDate.Date);
        var utcEnd = _timezoneService.GetEndOfDayLocal(localViewingDate.Date);
        
        _logger.LogInformation("Date conversion: LocalDate={LocalDate}, UtcStart={UtcStart}, UtcEnd={UtcEnd}", 
            localViewingDate.Date.ToString("yyyy-MM-dd"), utcStart.ToString("yyyy-MM-dd HH:mm:ss"), utcEnd.ToString("yyyy-MM-dd HH:mm:ss"));
        
        _logger.LogInformation($"BulkUpdate using date range: {utcStart} to {utcEnd}");
        
        var count = 0;
        var skippedHidden = 0;
        var created = 0;
        var updated = 0;
        var notFound = 0;

        // ---- Batch load #1: all relevant branches in one query ----
        var branchList = await _context.Branches
            .Where(b => request.branchIds.Contains(b.Id))
            .AsNoTracking()
            .ToListAsync();
        var branchLookup = branchList.ToDictionary(b => b.Id);

        // ---- Batch load #2: all existing DailyTasks for this date+task in one query ----
        var existingTasks = await _context.DailyTasks
            .Where(dt => request.branchIds.Contains(dt.BranchId) &&
                         dt.TaskItemId == request.taskItemId &&
                         dt.TaskDate >= utcStart &&
                         dt.TaskDate <= utcEnd)
            .ToListAsync();
        var existingLookup = existingTasks.ToDictionary(dt => dt.BranchId);

        foreach (var branchId in request.branchIds)
        {
            _logger.LogDebug("Processing branch {BranchId} for task {TaskId}", branchId, request.taskItemId);
            
            // Check if task is hidden for this branch (in-memory lookup)
            if (!branchLookup.TryGetValue(branchId, out var branch))
            {
                _logger.LogDebug("Branch {BranchId} not found, skipping", branchId);
                notFound++;
                continue;
            }

            if (branch.HiddenTasks?.Contains(task.Name) == true)
            {
                _logger.LogDebug("Task {TaskName} is hidden for branch {BranchId}, skipping", task.Name, branchId);
                skippedHidden++;
                continue;
            }
            
            if (!existingLookup.TryGetValue(branchId, out var dailyTask))
            {
                _logger.LogDebug("No existing task found - creating new for branch {BranchId}", branchId);
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = request.taskItemId,
                    TaskDate = utcStart,
                    IsCompleted = true,
                    CompletedAt = utcCompletionTime,
                    IsBulkUpdated = true,
                    BulkUpdateTime = utcCompletionTime
                };
                _context.DailyTasks.Add(dailyTask);
                created++;
                count++;
            }
            else
            {
                _logger.LogDebug("Updating existing task {TaskId} for branch {BranchId}", dailyTask.Id, branchId);
                dailyTask.IsCompleted = true;
                dailyTask.CompletedAt = utcCompletionTime;
                dailyTask.IsBulkUpdated = true;
                dailyTask.BulkUpdateTime = utcCompletionTime;
                updated++;
                count++;
            }
        }
        
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("BulkUpdate Summary - Total: {Count}, Created: {Created}, Updated: {Updated}, Skipped(Hidden): {Skipped}, NotFound: {NotFound}", 
            count, created, updated, skippedHidden, notFound);
        
        return Json(new { success = true, count = count, created = created, updated = updated, skippedHidden = skippedHidden });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in bulk update");
        return Json(new { success = false, message = ex.Message });
    }
}

[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SaveAdjustment(int branchId, int taskItemId, string date, int adjustmentMinutes, string adjustmentReason)
{
    try
    {
        _logger.LogInformation($"SaveAdjustment: Branch={branchId}, Task={taskItemId}, Date={date}, Minutes={adjustmentMinutes}");
        
        var viewingLocalDate = DateTime.Parse(date).Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(viewingLocalDate);
        var utcEnd = _timezoneService.GetEndOfDayLocal(viewingLocalDate);
        
        var dailyTask = await _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                       dt.TaskItemId == taskItemId &&
                                       dt.TaskDate >= utcStart &&
                                       dt.TaskDate <= utcEnd);
        
        if (dailyTask == null)
        {
            // Create new daily task if it doesn't exist
            dailyTask = new DailyTask
            {
                BranchId = branchId,
                TaskItemId = taskItemId,
                TaskDate = utcStart,
                IsCompleted = false,
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
        
        // If the task was already completed, recalculate delay info
        object? taskData = null;
        if (dailyTask.IsCompleted && dailyTask.CompletedAt.HasValue)
        {
            var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dailyTask);
            taskData = new
            {
                isCompleted = dailyTask.IsCompleted,
                completedAt = dailyTask.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                delayType = delayInfo.DelayType,
                delayText = delayInfo.DelayText,
                adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                adjustmentReason = dailyTask.AdjustmentReason ?? "",
                isOnTime = delayInfo.IsOnTime
            };
        }
        
        return Json(new { success = true, taskData = taskData });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error saving adjustment");
        return Json(new { success = false, message = ex.Message });
    }
}

[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> CompleteAllForTask(int taskItemId, string date)
{
    try
    {
        var localDate = DateTime.Parse(date).Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
        var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
        
        var task = await _context.TaskItems.FindAsync(taskItemId);
        if (task == null)
            return Json(new { success = false, message = "Task not found" });
        
        // Batch load branches + existing daily tasks in 2 queries (no N+1)
        var branches = await _context.Branches.Where(b => b.IsActive).AsNoTracking().ToListAsync();
        var activeBranchIds = branches.Select(b => b.Id).ToList();
        var now = DateTime.UtcNow;

        var existingTasks = await _context.DailyTasks
            .Where(dt => activeBranchIds.Contains(dt.BranchId) &&
                         dt.TaskItemId == taskItemId &&
                         dt.TaskDate >= utcStart &&
                         dt.TaskDate <= utcEnd)
            .ToListAsync();
        var existingLookup = existingTasks.ToDictionary(dt => dt.BranchId);

        var count = 0;
        
        foreach (var branch in branches)
        {
            if (branch.HiddenTasks != null && branch.HiddenTasks.Contains(task.Name))
                continue;
            
            if (!existingLookup.TryGetValue(branch.Id, out var dailyTask))
            {
                _context.DailyTasks.Add(new DailyTask
                {
                    BranchId = branch.Id,
                    TaskItemId = taskItemId,
                    TaskDate = utcStart,
                    IsCompleted = true,
                    CompletedAt = now
                });
                count++;
            }
            else if (!dailyTask.IsCompleted)
            {
                dailyTask.IsCompleted = true;
                dailyTask.CompletedAt = now;
                count++;
            }
        }
        
        await _context.SaveChangesAsync();
        return Json(new { success = true, count = count });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in complete all for task");
        return Json(new { success = false, message = ex.Message });
    }
}
    #endregion

    #region API Endpoints - Update Task Time
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> UpdateTaskTime(int branchId, int taskItemId, string date, string completionTime)
{
    try
    {
        _logger.LogInformation($"UpdateTaskTime called: BranchId={branchId}, TaskId={taskItemId}, Date={date}, CompletionTime={completionTime}");
        
        var viewingLocalDate = DateTime.Parse(date).Date;
        
        var task = await _context.TaskItems.FindAsync(taskItemId);
        if (task == null)
        {
            _logger.LogWarning($"Task {taskItemId} not found");
            return Json(new { success = false, message = "Task not found" });
        }

        DateTime localCompletionTime;
        DateTime utcCompletionTime;
        
        if (DateTime.TryParse(completionTime, out localCompletionTime))
        {
            utcCompletionTime = _timezoneService.ConvertToUtc(localCompletionTime);
            _logger.LogInformation($"Local time received: {localCompletionTime}, Converted to UTC: {utcCompletionTime}");
        }
        else
        {
            _logger.LogWarning($"Failed to parse completion time: {completionTime}");
            return Json(new { success = false, message = "Invalid time format" });
        }

        var taskDateUtc = _timezoneService.GetStartOfDayLocal(viewingLocalDate);
        
        var utcStart = taskDateUtc;
        var utcEnd = _timezoneService.GetEndOfDayLocal(viewingLocalDate);
        
        var dailyTask = await _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta!.Employee)
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
                TaskDate = taskDateUtc,
                IsCompleted = true,
                CompletedAt = utcCompletionTime
            };
            _context.DailyTasks.Add(dailyTask);
            _logger.LogInformation($"Created new DailyTask for Branch {branchId}, Task {taskItemId}");
        }
        else
        {
            dailyTask.IsCompleted = true;
            dailyTask.CompletedAt = utcCompletionTime;
            _logger.LogInformation($"Updated existing DailyTask {dailyTask.Id}");
        }

        await _context.SaveChangesAsync();

        await AutoAssignTaskToEmployeeAsync(dailyTask, taskDateUtc);

        var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dailyTask);
        
        _logger.LogInformation($"Delay Info: Type={delayInfo.DelayType}, Text={delayInfo.DelayText}, IsOnTime={delayInfo.IsOnTime}");

        var response = new
        {
            success = true,
            taskData = new
            {
                branchId = branchId,
                taskId = taskItemId,
                isCompleted = true,
                completedAt = utcCompletionTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                delayType = delayInfo.DelayType,
                delayText = delayInfo.DelayText,
                adjustmentMinutes = dailyTask.AdjustmentMinutes ?? 0,
                adjustmentReason = dailyTask.AdjustmentReason ?? "",
                assignedTo = dailyTask.TaskAssignment?.Employee?.Name ?? "",
                deadline = delayInfo.Deadline?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                isOnTime = delayInfo.IsOnTime,
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
            // FIX: CS8604 - Check for null task
            if (task == null)
            {
                return Json(new { success = false, message = "Task not found" });
            }
            
            var deadline = await _taskCalculationService.CalculateDeadline(task, utcStart);
            var adjustmentMinutes = dailyTask?.AdjustmentMinutes ?? 0;
            var adjustedDeadline = deadline.AddMinutes(adjustmentMinutes);
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

            // Batch load: branches + existing daily tasks in 2 queries (no N+1)
            var branches = await _context.Branches.Where(b => b.IsActive).AsNoTracking().ToListAsync();
            var activeBranchIds = branches.Select(b => b.Id).ToList();

            var existingTasks = await _context.DailyTasks
                .Where(dt => activeBranchIds.Contains(dt.BranchId) &&
                             dt.TaskItemId == taskItemId &&
                             dt.TaskDate >= utcStart &&
                             dt.TaskDate <= utcEnd)
                .ToListAsync();
            var existingLookup = existingTasks.ToDictionary(dt => dt.BranchId);

            // Calculate deadline once (same for all branches for this task+date)
            var deadline = await _taskCalculationService.CalculateDeadline(task, utcStart);

            var count = 0;
            var taskData = new Dictionary<int, object>();

            foreach (var branch in branches)
            {
                if (branch.HiddenTasks != null && branch.HiddenTasks.Contains(task.Name))
                    continue;

                existingLookup.TryGetValue(branch.Id, out var dailyTask);

                if (dailyTask != null && dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = false;
                    dailyTask.CompletedAt = null;
                    count++;
                }

                var adjustmentMinutes = dailyTask?.AdjustmentMinutes ?? 0;
                var localDeadline = _timezoneService.ConvertToLocalTime(deadline.AddMinutes(adjustmentMinutes));

                taskData[branch.Id] = new
                {
                    branchId = branch.Id,
                    taskId = taskItemId,
                    isCompleted = false,
                    completedAt = (DateTime?)null,
                    delayType = "pending",
                    delayText = "Pending",
                    adjustmentMinutes,
                    adjustmentReason = dailyTask?.AdjustmentReason ?? "",
                    deadline = localDeadline
                };
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, count, taskData });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reset all for task: {Message}", ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region API Endpoints - Undo

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoLastAction([FromBody] UndoRequest request)
    {
        try
        {
            if (request == null || request.ActionId == 0)
                return Json(new { success = false, message = "Invalid undo request" });

            var lastAction = HttpContext.Session.GetString($"LastAction_{request.ActionId}");
            
            if (string.IsNullOrEmpty(lastAction))
                return Json(new { success = false, message = "No action to undo" });
            
            HttpContext.Session.Remove($"LastAction_{request.ActionId}");
            
            return Json(new { success = true, message = "Action undone successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undoing action");
            return Json(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region Private Helper Methods

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

public class UndoRequest
{
    public int ActionId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
}
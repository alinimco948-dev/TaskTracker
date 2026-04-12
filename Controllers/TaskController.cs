using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Controllers;

public class TaskController : Controller
{
    private readonly ITaskService _taskService;
    private readonly IBranchService _branchService;
    private readonly ILogger<TaskController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly ITimezoneService _timezoneService;

    public TaskController(
        ITaskService taskService,
        IBranchService branchService,
        ILogger<TaskController> logger,
        ApplicationDbContext context,
        ITimezoneService timezoneService)
    {
        _taskService = taskService;
        _branchService = branchService;
        _logger = logger;
        _context = context;
        _timezoneService = timezoneService;
    }

    public async Task<IActionResult> Index()
    {
        var tasks = await _taskService.GetAllTasksAsync();
        var branches = await _branchService.GetAllBranchesAsync();
        ViewBag.Branches = branches;
        return View(tasks);
    }

    // GET: Task/Create
    public IActionResult Create()
    {
        return View(new TaskItemViewModel());
    }

    // POST: Task/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Id,Name,Description,Deadline,IsSameDay,DisplayOrder,IsActive,ExecutionType,WeeklyDays,MonthlyPattern,DurationDays,StartDate,EndDate,MaxOccurrences,AvailableFrom,AvailableTo")] TaskItemViewModel model)
    {
        try
        {
            _logger.LogInformation($"Creating task: Name={model.Name}, ExecutionType={model.ExecutionType}");

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                _logger.LogWarning($"Model state invalid: {string.Join(", ", errors)}");
                return View(model);
            }

            // Check if task name already exists
            var existingTask = await _context.TaskItems
                .FirstOrDefaultAsync(t => t.Name.ToLower() == model.Name.ToLower());

            if (existingTask != null)
            {
                ModelState.AddModelError("Name", "A task with this name already exists");
                _logger.LogWarning($"Task name already exists: {model.Name}");
                return View(model);
            }

            // Handle display order from text input
            int displayOrder = 0;
            var textOrder = Request.Form["TextOrder"].ToString();

            if (!string.IsNullOrEmpty(textOrder))
            {
                displayOrder = await ParseTextOrder(textOrder);
                _logger.LogInformation($"Parsed text order '{textOrder}' to display order {displayOrder}");
            }
            else if (model.DisplayOrder > 0)
            {
                displayOrder = model.DisplayOrder;
            }

            // If still 0, assign next available order
            if (displayOrder == 0)
            {
                var maxOrderValue = await _context.TaskItems.MaxAsync(t => (int?)t.DisplayOrder) ?? 0;
                displayOrder = maxOrderValue + 1;
                _logger.LogInformation($"Auto-assigned display order: {displayOrder}");
            }

            // Convert dates to UTC using timezone service
            DateTime? startDateUtc = model.StartDate.HasValue
                ? _timezoneService.GetStartOfDayLocal(model.StartDate.Value)
                : null;
            DateTime? endDateUtc = model.EndDate.HasValue
                ? _timezoneService.GetEndOfDayLocal(model.EndDate.Value)
                : null;
            DateTime? availableFromUtc = model.AvailableFrom.HasValue
                ? _timezoneService.GetStartOfDayLocal(model.AvailableFrom.Value)
                : null;
            DateTime? availableToUtc = model.AvailableTo.HasValue
                ? _timezoneService.GetEndOfDayLocal(model.AvailableTo.Value)
                : null;

            // Calculate EndDate for Multi-Day tasks based on DurationDays
            DateTime? calculatedEndDate = endDateUtc;
            if (model.ExecutionType == TaskExecutionType.MultiDay && model.DurationDays.HasValue && startDateUtc.HasValue)
            {
                calculatedEndDate = startDateUtc.Value.AddDays(model.DurationDays.Value - 1);
            }

            // Create the task
            var task = new TaskItem
            {
                Name = model.Name.Trim(),
                Deadline = model.Deadline,
                IsSameDay = model.IsSameDay,
                DisplayOrder = displayOrder,
                Description = model.Description ?? string.Empty,
                IsActive = model.IsActive,
                ExecutionType = model.ExecutionType,
                WeeklyDays = model.WeeklyDays != null && model.WeeklyDays.Any()
                    ? string.Join(",", model.WeeklyDays)
                    : null,
                MonthlyPattern = model.MonthlyPattern ?? "15",
                DurationDays = model.DurationDays,
                StartDate = startDateUtc,
                EndDate = calculatedEndDate,
                MaxOccurrences = model.MaxOccurrences,
                AvailableFrom = availableFromUtc,
                AvailableTo = availableToUtc,
                CreatedAt = DateTime.UtcNow
            };

            _context.TaskItems.Add(task);
            await _context.SaveChangesAsync();
            _logger.LogInformation($"Task created successfully with ID: {task.Id}");

            // Auto-assign this task to all active employees with branch assignments
            await AutoAssignTaskToAllEmployees(task);

            TempData["SuccessMessage"] = $"Task '{task.Name}' created and assigned to employees successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error creating task");
            ModelState.AddModelError("", $"Database error: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating task");
            ModelState.AddModelError("", $"Unable to create task: {ex.Message}");
        }

        return View(model);
    }

    private async Task<int> ParseTextOrder(string textOrder)
    {
        var lowerText = textOrder.ToLower().Trim();

        // Number
        if (int.TryParse(lowerText, out int orderNumber))
        {
            return orderNumber;
        }

        // Top / First
        if (lowerText == "top" || lowerText == "first")
        {
            return 1;
        }

        // Bottom / Last
        if (lowerText == "bottom" || lowerText == "last")
        {
            var maxOrderValue = await _context.TaskItems.MaxAsync(t => (int?)t.DisplayOrder) ?? 0;
            return maxOrderValue + 1;
        }

        // After:TaskName
        if (lowerText.StartsWith("after:"))
        {
            var taskName = lowerText.Substring(6);
            var existingTask = await _context.TaskItems
                .FirstOrDefaultAsync(t => t.Name.ToLower() == taskName);

            if (existingTask != null)
            {
                return existingTask.DisplayOrder + 1;
            }
        }

        // Before:TaskName
        if (lowerText.StartsWith("before:"))
        {
            var taskName = lowerText.Substring(7);
            var existingTask = await _context.TaskItems
                .FirstOrDefaultAsync(t => t.Name.ToLower() == taskName);

            if (existingTask != null)
            {
                return existingTask.DisplayOrder;
            }
        }

        // Default to auto
        var maxOrderValueAuto = await _context.TaskItems.MaxAsync(t => (int?)t.DisplayOrder) ?? 0;
        return maxOrderValueAuto + 1;
    }

    private async Task AutoAssignTaskToAllEmployees(TaskItem task)
    {
        try
        {
            _logger.LogInformation($"Auto-assigning task '{task.Name}' to employees...");

            // Get all active employees with their branch assignments
            var employees = await _context.Employees
                .Where(e => e.IsActive)
                .Include(e => e.BranchAssignments)
                .ThenInclude(ba => ba.Branch)
                .ToListAsync();

            // Ensure dates are in UTC
            var startDate = task.StartDate.HasValue
                ? task.StartDate.Value
                : _timezoneService.GetStartOfDayLocal(_timezoneService.GetCurrentLocalTime());
            var endDate = task.EndDate.HasValue
                ? task.EndDate.Value
                : _timezoneService.GetEndOfDayLocal(_timezoneService.GetCurrentLocalTime().AddYears(1));

            var taskDates = GetTaskDates(task, startDate, endDate);

            var createdCount = 0;

            foreach (var employee in employees)
            {
                // Get active branch assignments for this employee
                var activeAssignments = employee.BranchAssignments
                    .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= _timezoneService.GetCurrentLocalTime().Date)
                    .ToList();

                foreach (var assignment in activeAssignments)
                {
                    foreach (var taskDate in taskDates)
                    {
                        // Check if date is within assignment period
                        if (taskDate.Date < assignment.StartDate.Date) continue;
                        if (assignment.EndDate.HasValue && taskDate.Date > assignment.EndDate.Value.Date) continue;

                        // Check if daily task already exists
                        var existingDailyTask = await _context.DailyTasks
                            .FirstOrDefaultAsync(dt => dt.BranchId == assignment.BranchId &&
                                                       dt.TaskItemId == task.Id &&
                                                       dt.TaskDate.Date == taskDate.Date);

                        if (existingDailyTask == null)
                        {
                            // Create DailyTask with UTC date
                            var dailyTask = new DailyTask
                            {
                                BranchId = assignment.BranchId,
                                TaskItemId = task.Id,
                                TaskDate = taskDate.Date,
                                IsCompleted = false,
                                CompletedAt = null
                            };
                            _context.DailyTasks.Add(dailyTask);
                            await _context.SaveChangesAsync();

                            // Create TaskAssignment
                            var taskAssignment = new TaskAssignment
                            {
                                EmployeeId = employee.Id,
                                DailyTaskId = dailyTask.Id,
                                AssignedAt = DateTime.UtcNow
                            };
                            _context.TaskAssignments.Add(taskAssignment);
                            createdCount++;
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation($"Auto-assigned task '{task.Name}' to {createdCount} employee-task combinations");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-assigning task {TaskName}", task.Name);
        }
    }

    private List<DateTime> GetTaskDates(TaskItem task, DateTime startDate, DateTime endDate)
    {
        var dates = new List<DateTime>();
        var current = startDate.Date;

        while (current <= endDate.Date)
        {
            if (IsTaskVisibleOnDate(task, current))
            {
                dates.Add(current);
            }
            current = current.AddDays(1);
        }

        // Limit by max occurrences if specified
        if (task.MaxOccurrences.HasValue && dates.Count > task.MaxOccurrences.Value)
        {
            return dates.Take(task.MaxOccurrences.Value).ToList();
        }

        return dates;
    }

    private bool IsTaskVisibleOnDate(TaskItem task, DateTime date)
    {
        // Use local date for visibility check (since task schedules are date-based)
        var compareDate = date.Date;

        // Check if task is active
        if (!task.IsActive) return false;

        // Check availability range
        if (task.AvailableFrom.HasValue && compareDate < task.AvailableFrom.Value.Date)
            return false;
        if (task.AvailableTo.HasValue && compareDate > task.AvailableTo.Value.Date)
            return false;

        // Check task schedule range
        if (task.StartDate.HasValue && compareDate < task.StartDate.Value.Date)
            return false;
        if (task.EndDate.HasValue && compareDate > task.EndDate.Value.Date)
            return false;

        // Check recurrence pattern
        switch (task.ExecutionType)
        {
            case TaskExecutionType.RecurringDaily:
                return true;

            case TaskExecutionType.RecurringWeekly:
                if (string.IsNullOrEmpty(task.WeeklyDays)) return false;
                var weeklyDays = task.WeeklyDays.Split(',').Select(int.Parse).ToList();
                return weeklyDays.Contains((int)date.DayOfWeek);

            case TaskExecutionType.RecurringMonthly:
                if (string.IsNullOrEmpty(task.MonthlyPattern)) return false;
                var pattern = task.MonthlyPattern.ToLower();
                if (pattern == "last")
                    return date.Day == DateTime.DaysInMonth(date.Year, date.Month);
                if (int.TryParse(pattern, out int dayOfMonth))
                    return date.Day == dayOfMonth;
                var parts = pattern.Split('-');
                if (parts.Length == 2)
                {
                    var ordinal = parts[0];
                    var weekday = parts[1];
                    return IsNthWeekdayOfMonth(date, ordinal, weekday);
                }
                return false;

            case TaskExecutionType.MultiDay:
                if (!task.StartDate.HasValue) return false;
                var multiDayEndDate = task.EndDate.HasValue ? task.EndDate.Value :
                                     (task.DurationDays.HasValue ? task.StartDate.Value.AddDays(task.DurationDays.Value - 1) : task.StartDate.Value);
                return date >= task.StartDate.Value && date <= multiDayEndDate;

            case TaskExecutionType.OneTime:
                return task.StartDate.HasValue && date.Date == task.StartDate.Value.Date;

            default:
                return true;
        }
    }

    private bool IsNthWeekdayOfMonth(DateTime date, string ordinal, string weekday)
    {
        var weekNumber = GetWeekNumber(ordinal);
        if (weekNumber == -1) return false;

        var targetWeekday = GetWeekdayNumber(weekday);
        if (targetWeekday == -1) return false;

        var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
        var firstOccurrence = firstDayOfMonth.AddDays((targetWeekday - (int)firstDayOfMonth.DayOfWeek + 7) % 7);
        var targetDate = firstOccurrence.AddDays(7 * (weekNumber - 1));

        return date.Date == targetDate.Date && date.Month == targetDate.Month;
    }

    private int GetWeekNumber(string ordinal)
    {
        return ordinal.ToLower() switch
        {
            "first" => 1,
            "second" => 2,
            "third" => 3,
            "fourth" => 4,
            "last" => 5,
            _ => -1
        };
    }

    private int GetWeekdayNumber(string weekday)
    {
        return weekday.ToLower() switch
        {
            "sunday" => 0,
            "monday" => 1,
            "tuesday" => 2,
            "wednesday" => 3,
            "thursday" => 4,
            "friday" => 5,
            "saturday" => 6,
            _ => -1
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetNextDisplayOrder()
    {
        try
        {
            var maxOrder = await _context.TaskItems.MaxAsync(t => (int?)t.DisplayOrder) ?? 0;
            return Json(new { success = true, nextOrder = maxOrder + 1 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next display order");
            return Json(new { success = false, message = ex.Message });
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var task = await _taskService.GetTaskByIdAsync(id);
        if (task == null) return NotFound();

        var model = new TaskItemViewModel
        {
            Id = task.Id,
            Name = task.Name,
            Deadline = task.Deadline,
            IsSameDay = task.IsSameDay,
            DisplayOrder = task.DisplayOrder,
            Description = task.Description ?? string.Empty,
            IsActive = task.IsActive,
            ExecutionType = task.ExecutionType,
            WeeklyDays = !string.IsNullOrEmpty(task.WeeklyDays) ? task.WeeklyDays.Split(',').Select(int.Parse).ToList() : new List<int>(),
            MonthlyPattern = task.MonthlyPattern ?? "15",
            DurationDays = task.DurationDays,
            StartDate = task.StartDate.HasValue ? _timezoneService.ConvertToLocalTime(task.StartDate.Value) : null,
            EndDate = task.EndDate.HasValue ? _timezoneService.ConvertToLocalTime(task.EndDate.Value) : null,
            MaxOccurrences = task.MaxOccurrences,
            AvailableFrom = task.AvailableFrom.HasValue ? _timezoneService.ConvertToLocalTime(task.AvailableFrom.Value) : null,
            AvailableTo = task.AvailableTo.HasValue ? _timezoneService.ConvertToLocalTime(task.AvailableTo.Value) : null
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TaskItemViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await _taskService.UpdateTaskAsync(model);
                TempData["SuccessMessage"] = "Task updated successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task");
                ModelState.AddModelError("", "Unable to update task. Please try again.");
            }
        }

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _taskService.DeleteTaskAsync(id);
        return Json(new { success = result, message = result ? "Task deleted successfully" : "Error deleting task" });
    }

    [HttpPost]
    public async Task<IActionResult> Reorder([FromBody] List<int> taskIds)
    {
        var result = await _taskService.ReorderTasksAsync(taskIds);
        return Json(new { success = result });
    }
}
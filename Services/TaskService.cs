using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class TaskService : ITaskService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TaskService> _logger;
    private readonly IAuditService _auditService;
    private readonly IHolidayService _holidayService;
    private readonly TaskSchedulerService _taskScheduler;

    public TaskService(
        ApplicationDbContext context,
        ILogger<TaskService> logger,
        IAuditService auditService,
        IHolidayService holidayService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
        _holidayService = holidayService;
        _taskScheduler = new TaskSchedulerService();
    }

    #region Basic CRUD

    public async Task<List<TaskListViewModel>> GetAllTasksAsync()
    {
        try
        {
            var tasks = await _context.TaskItems
                .OrderBy(t => t.DisplayOrder)
                .ToListAsync();

            return tasks.Select(t => new TaskListViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Deadline = t.Deadline,
                IsSameDay = t.IsSameDay,
                DisplayOrder = t.DisplayOrder,
                IsActive = t.IsActive,
                ExecutionType = t.ExecutionType,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                DurationDays = t.DurationDays
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tasks");
            return new List<TaskListViewModel>();
        }
    }

    public async Task<TaskItem?> GetTaskByIdAsync(int id)
    {
        return await _context.TaskItems.FindAsync(id);
    }

    public async Task<TaskItem> CreateTaskAsync(TaskItemViewModel model)
    {
        try
        {
            // Check if task name already exists
            if (await _context.TaskItems.AnyAsync(t => t.Name == model.Name))
            {
                throw new InvalidOperationException($"Task with name '{model.Name}' already exists");
            }

            // Parse display order from text if provided
            int displayOrder = await ParseDisplayOrder(model.DisplayOrder, model.Name);

            // Convert dates to UTC
            DateTime? startDateUtc = model.StartDate.HasValue
                ? DateTime.SpecifyKind(model.StartDate.Value, DateTimeKind.Utc)
                : null;
            DateTime? endDateUtc = model.EndDate.HasValue
                ? DateTime.SpecifyKind(model.EndDate.Value, DateTimeKind.Utc)
                : null;
            DateTime? availableFromUtc = model.AvailableFrom.HasValue
                ? DateTime.SpecifyKind(model.AvailableFrom.Value, DateTimeKind.Utc)
                : null;
            DateTime? availableToUtc = model.AvailableTo.HasValue
                ? DateTime.SpecifyKind(model.AvailableTo.Value, DateTimeKind.Utc)
                : null;

            // Calculate EndDate for Multi-Day tasks based on DurationDays
            DateTime? calculatedEndDate = endDateUtc;
            if (model.ExecutionType == TaskExecutionType.MultiDay && model.DurationDays.HasValue && startDateUtc.HasValue)
            {
                calculatedEndDate = startDateUtc.Value.AddDays(model.DurationDays.Value - 1);
            }

            var task = new TaskItem
            {
                Name = model.Name,
                Deadline = model.Deadline,
                IsSameDay = model.IsSameDay,
                DisplayOrder = displayOrder,
                Description = model.Description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                ExecutionType = model.ExecutionType,
                WeeklyDays = model.WeeklyDays.Any() ? string.Join(",", model.WeeklyDays) : null,
                MonthlyPattern = model.MonthlyPattern,
                DurationDays = model.DurationDays,
                StartDate = startDateUtc,
                EndDate = calculatedEndDate,
                MaxOccurrences = model.MaxOccurrences,
                AvailableFrom = availableFromUtc,
                AvailableTo = availableToUtc
            };

            _context.TaskItems.Add(task);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Create",
                "TaskItem",
                task.Id,
                $"Created task: {task.Name} (Type: {task.ExecutionType}, Order: {task.DisplayOrder})"
            );

            return task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating task");
            throw;
        }
    }

    public async Task<TaskItem?> UpdateTaskAsync(TaskItemViewModel model)
    {
        try
        {
            var task = await _context.TaskItems.FindAsync(model.Id);
            if (task == null) return null;

            // Check if task name already exists (excluding current)
            if (await _context.TaskItems.AnyAsync(t => t.Name == model.Name && t.Id != model.Id))
            {
                throw new InvalidOperationException($"Task with name '{model.Name}' already exists");
            }

            // Parse display order from text if provided
            int displayOrder = await ParseDisplayOrder(model.DisplayOrder, model.Name, model.Id);

            // Convert dates to UTC
            DateTime? startDateUtc = model.StartDate.HasValue
                ? DateTime.SpecifyKind(model.StartDate.Value, DateTimeKind.Utc)
                : null;
            DateTime? endDateUtc = model.EndDate.HasValue
                ? DateTime.SpecifyKind(model.EndDate.Value, DateTimeKind.Utc)
                : null;
            DateTime? availableFromUtc = model.AvailableFrom.HasValue
                ? DateTime.SpecifyKind(model.AvailableFrom.Value, DateTimeKind.Utc)
                : null;
            DateTime? availableToUtc = model.AvailableTo.HasValue
                ? DateTime.SpecifyKind(model.AvailableTo.Value, DateTimeKind.Utc)
                : null;

            // Calculate EndDate for Multi-Day tasks based on DurationDays
            DateTime? calculatedEndDate = endDateUtc;
            if (model.ExecutionType == TaskExecutionType.MultiDay && model.DurationDays.HasValue && startDateUtc.HasValue)
            {
                calculatedEndDate = startDateUtc.Value.AddDays(model.DurationDays.Value - 1);
            }

            var oldValues = $"Name:{task.Name}, Type:{task.ExecutionType}, Order:{task.DisplayOrder}";

            task.Name = model.Name;
            task.Deadline = model.Deadline;
            task.IsSameDay = model.IsSameDay;
            task.DisplayOrder = displayOrder;
            task.Description = model.Description;
            task.IsActive = model.IsActive;
            task.UpdatedAt = DateTime.UtcNow;
            task.ExecutionType = model.ExecutionType;
            task.WeeklyDays = model.WeeklyDays.Any() ? string.Join(",", model.WeeklyDays) : null;
            task.MonthlyPattern = model.MonthlyPattern;
            task.DurationDays = model.DurationDays;
            task.StartDate = startDateUtc;
            task.EndDate = calculatedEndDate;
            task.MaxOccurrences = model.MaxOccurrences;
            task.AvailableFrom = availableFromUtc;
            task.AvailableTo = availableToUtc;

            // Reorder tasks if display order changed
            if (task.DisplayOrder != displayOrder)
            {
                await ReorderTasksAfterInsert(displayOrder, task.Id);
            }

            await _context.SaveChangesAsync();

            var newValues = $"Name:{task.Name}, Type:{task.ExecutionType}, Order:{task.DisplayOrder}";

            await _auditService.LogAsync(
                "Update",
                "TaskItem",
                task.Id,
                $"Updated task: {task.Name}",
                null,
                oldValues,
                newValues
            );

            return task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating task {Id}", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteTaskAsync(int id)
    {
        try
        {
            var task = await _context.TaskItems.FindAsync(id);
            if (task == null) return false;

            // Check if task has daily tasks
            var hasDailyTasks = await _context.DailyTasks
                .AnyAsync(dt => dt.TaskItemId == id);

            if (hasDailyTasks)
            {
                // Soft delete
                task.IsActive = false;
                task.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    "Deactivate",
                    "TaskItem",
                    task.Id,
                    $"Deactivated task: {task.Name}"
                );
            }
            else
            {
                _context.TaskItems.Remove(task);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    "Delete",
                    "TaskItem",
                    task.Id,
                    $"Deleted task: {task.Name}"
                );
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting task {Id}", id);
            return false;
        }
    }

    #endregion

    #region Display Order Parsing

    private async Task<int> ParseDisplayOrder(int currentOrder, string taskName, int? excludeId = null)
    {
        // If current order is not 0, use it
        if (currentOrder > 0)
        {
            return currentOrder;
        }

        // Default to auto order
        var maxOrderValue = await _context.TaskItems
            .Where(t => !excludeId.HasValue || t.Id != excludeId.Value)
            .MaxAsync(t => (int?)t.DisplayOrder) ?? 0;

        return maxOrderValue + 1;
    }

    public async Task<int> ParseTextOrder(string textOrder)
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

    private async Task ReorderTasksAfterInsert(int newOrder, int newTaskId)
    {
        var tasksToUpdate = await _context.TaskItems
            .Where(t => t.Id != newTaskId && t.DisplayOrder >= newOrder)
            .OrderBy(t => t.DisplayOrder)
            .ToListAsync();

        for (int i = 0; i < tasksToUpdate.Count; i++)
        {
            tasksToUpdate[i].DisplayOrder = newOrder + i + 1;
            tasksToUpdate[i].UpdatedAt = DateTime.UtcNow;
        }
    }

    #endregion

    #region Task Ordering

    public async Task<bool> ReorderTasksAsync(List<int> taskIds)
    {
        try
        {
            for (int i = 0; i < taskIds.Count; i++)
            {
                var task = await _context.TaskItems.FindAsync(taskIds[i]);
                if (task != null)
                {
                    task.DisplayOrder = i + 1;
                    task.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Reorder",
                "TaskItem",
                null,
                "Reordered tasks"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering tasks");
            return false;
        }
    }

    #endregion

    #region Task Visibility

    public async Task<Dictionary<int, List<string>>> GetHiddenTasksAsync()
    {
        try
        {
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .ToListAsync();

            return branches.ToDictionary(
                b => b.Id,
                b => b.HiddenTasks
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hidden tasks");
            return new Dictionary<int, List<string>>();
        }
    }

    public async Task<bool> UpdateTaskVisibilityAsync(int branchId, string taskName, bool isVisible)
    {
        try
        {
            var branch = await _context.Branches.FindAsync(branchId);
            if (branch == null) return false;

            var hiddenTasks = branch.HiddenTasks;

            if (isVisible)
            {
                hiddenTasks.Remove(taskName);
            }
            else if (!hiddenTasks.Contains(taskName))
            {
                hiddenTasks.Add(taskName);
            }

            branch.HiddenTasks = hiddenTasks;
            branch.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating task visibility");
            return false;
        }
    }

    #endregion

    #region Task Scheduling

    public async Task<List<TaskItem>> GetTasksVisibleOnDateAsync(DateTime date)
    {
        try
        {
            var allTasks = await _context.TaskItems
                .Where(t => t.IsActive)
                .OrderBy(t => t.DisplayOrder)
                .ToListAsync();

            // Ensure date is UTC for comparison
            var compareDate = date.Date;

            var visibleTasks = allTasks
                .Where(t => _taskScheduler.IsTaskVisibleOnDate(t, compareDate))
                .ToList();

            _logger.LogDebug("Found {Count} visible tasks for date {Date}", visibleTasks.Count, date);
            return visibleTasks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting visible tasks for date {Date}", date);
            return new List<TaskItem>();
        }
    }

    public async Task<bool> IsTaskCompletedForDateAsync(int branchId, int taskId, DateTime date)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null) return false;

        var dailyTask = await _context.DailyTasks
            .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                       dt.TaskItemId == taskId &&
                                       dt.TaskDate.Date == date.Date);

        return _taskScheduler.IsTaskCompletedForDate(task, dailyTask, date);
    }

    public async Task<string> GetTaskScheduleSummaryAsync(int taskId)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null) return "Task not found";

        return _taskScheduler.GetTaskScheduleSummary(task);
    }

    public async Task<DateTime?> GetNextOccurrenceAsync(int taskId, DateTime fromDate)
    {
        var task = await _context.TaskItems.FindAsync(taskId);
        if (task == null) return null;

        return _taskScheduler.GetNextOccurrence(task, fromDate);
    }

    #endregion

    #region Daily Task Operations

    public async Task<bool> ToggleTaskCompletionAsync(int branchId, int taskId, DateTime date, DateTime? completionTime = null)
    {
        try
        {
            var task = await _context.TaskItems.FindAsync(taskId);
            if (task == null) return false;

            // Ensure dates are UTC
            var taskDate = date.Date;
            var completionTimeUtc = completionTime.HasValue
                ? DateTime.SpecifyKind(completionTime.Value, DateTimeKind.Utc)
                : DateTime.UtcNow;

            var dailyTask = await _context.DailyTasks
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskId &&
                                           dt.TaskDate.Date == taskDate);

            // Handle Multi-Day tasks (complete once for entire range)
            if (task.ExecutionType == TaskExecutionType.MultiDay)
            {
                if (dailyTask == null)
                {
                    // Create a single completion record
                    var completionRecord = new DailyTask
                    {
                        BranchId = branchId,
                        TaskItemId = taskId,
                        TaskDate = taskDate,
                        IsCompleted = true,
                        CompletedAt = completionTimeUtc
                    };
                    await _context.DailyTasks.AddAsync(completionRecord);
                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(
                        "Complete",
                        "TaskItem",
                        taskId,
                        $"Completed Multi-Day task: {task.Name} (Duration: {task.DurationDays} days)"
                    );

                    return true;
                }
                else if (!dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = true;
                    dailyTask.CompletedAt = completionTimeUtc;
                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(
                        "Complete",
                        "TaskItem",
                        taskId,
                        $"Completed Multi-Day task: {task.Name}"
                    );

                    return true;
                }
                return true;
            }

            // Handle Recurring Daily, Weekly, Monthly, One-Time tasks (complete per occurrence)
            if (dailyTask == null)
            {
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = taskId,
                    TaskDate = taskDate,
                    IsCompleted = true,
                    CompletedAt = completionTimeUtc
                };
                await _context.DailyTasks.AddAsync(dailyTask);
            }
            else if (!dailyTask.IsCompleted)
            {
                dailyTask.IsCompleted = true;
                dailyTask.CompletedAt = completionTimeUtc;
                _context.DailyTasks.Update(dailyTask);
            }
            else
            {
                // Already completed
                return true;
            }

            await _context.SaveChangesAsync();

            // Auto-assign to employee
            await AutoAssignTaskToEmployee(dailyTask, taskDate);

            await _auditService.LogAsync(
                "Complete",
                "TaskItem",
                taskId,
                $"Completed task: {task.Name} on {taskDate:yyyy-MM-dd}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling task completion");
            return false;
        }
    }

    public async Task<bool> ResetTaskAsync(int branchId, int taskId, DateTime date)
    {
        try
        {
            var task = await _context.TaskItems.FindAsync(taskId);
            if (task == null) return false;

            var dailyTask = await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskId &&
                                           dt.TaskDate.Date == date.Date);

            if (dailyTask == null) return true;

            // For Multi-Day tasks, resetting removes the completion record
            if (task.ExecutionType == TaskExecutionType.MultiDay)
            {
                if (dailyTask.TaskAssignment != null)
                {
                    _context.TaskAssignments.Remove(dailyTask.TaskAssignment);
                }
                _context.DailyTasks.Remove(dailyTask);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    "Reset",
                    "TaskItem",
                    taskId,
                    $"Reset Multi-Day task: {task.Name}"
                );

                return true;
            }

            // For other task types, reset the specific occurrence
            if (dailyTask.TaskAssignment != null)
            {
                _context.TaskAssignments.Remove(dailyTask.TaskAssignment);
            }

            _context.DailyTasks.Remove(dailyTask);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Reset",
                "TaskItem",
                taskId,
                $"Reset task: {task.Name} on {date:yyyy-MM-dd}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting task");
            return false;
        }
    }

    public async Task<bool> UpdateTaskAdjustmentAsync(int branchId, int taskId, DateTime date, int? adjustmentMinutes, string? reason)
    {
        try
        {
            var dailyTask = await _context.DailyTasks
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskId &&
                                           dt.TaskDate.Date == date.Date);

            if (dailyTask == null)
            {
                dailyTask = new DailyTask
                {
                    BranchId = branchId,
                    TaskItemId = taskId,
                    TaskDate = date.Date,
                    IsCompleted = false,
                    AdjustmentMinutes = adjustmentMinutes > 0 ? adjustmentMinutes : null,
                    AdjustmentReason = reason ?? string.Empty
                };
                await _context.DailyTasks.AddAsync(dailyTask);
            }
            else
            {
                dailyTask.AdjustmentMinutes = adjustmentMinutes > 0 ? adjustmentMinutes : null;
                dailyTask.AdjustmentReason = reason ?? string.Empty;
                _context.DailyTasks.Update(dailyTask);
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Adjustment",
                "TaskItem",
                taskId,
                $"Added adjustment: {adjustmentMinutes} minutes for task on {date:yyyy-MM-dd}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating task adjustment");
            return false;
        }
    }

    #endregion

    #region Bulk Operations

    public async Task<int> BulkUpdateTasksAsync(int taskId, DateTime completionDateTime, List<int> branchIds)
    {
        try
        {
            var task = await _context.TaskItems.FindAsync(taskId);
            if (task == null) return 0;

            var date = completionDateTime.Date;
            var utcCompletion = DateTime.SpecifyKind(completionDateTime, DateTimeKind.Utc);
            int updatedCount = 0;

            foreach (var branchId in branchIds)
            {
                // Check if task is hidden for this branch
                var branch = await _context.Branches.FindAsync(branchId);
                if (branch != null && branch.HiddenTasks.Contains(task.Name))
                {
                    continue;
                }

                var dailyTask = await _context.DailyTasks
                    .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                               dt.TaskItemId == taskId &&
                                               dt.TaskDate.Date == date.Date);

                if (dailyTask == null)
                {
                    dailyTask = new DailyTask
                    {
                        BranchId = branchId,
                        TaskItemId = taskId,
                        TaskDate = date,
                        IsCompleted = true,
                        CompletedAt = utcCompletion,
                        IsBulkUpdated = true,
                        BulkUpdateTime = DateTime.UtcNow
                    };
                    await _context.DailyTasks.AddAsync(dailyTask);
                    updatedCount++;
                }
                else if (!dailyTask.IsCompleted)
                {
                    dailyTask.IsCompleted = true;
                    dailyTask.CompletedAt = utcCompletion;
                    dailyTask.IsBulkUpdated = true;
                    dailyTask.BulkUpdateTime = DateTime.UtcNow;
                    _context.DailyTasks.Update(dailyTask);
                    updatedCount++;
                }
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "BulkUpdate",
                "TaskItem",
                taskId,
                $"Bulk updated {updatedCount} tasks for {task.Name}"
            );

            return updatedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bulk update");
            return 0;
        }
    }

    public async Task<int> CompleteAllForTaskAsync(int taskId, DateTime date)
    {
        try
        {
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .Select(b => b.Id)
                .ToListAsync();

            return await BulkUpdateTasksAsync(taskId, DateTime.UtcNow, branches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing all for task");
            return 0;
        }
    }

    #endregion

    #region Task Status

    public async Task<DailyTask?> GetTaskStatusAsync(int branchId, int taskId, DateTime date)
    {
        try
        {
            return await _context.DailyTasks
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta.Employee)
                .Include(dt => dt.TaskItem)
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == taskId &&
                                           dt.TaskDate.Date == date.Date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task status");
            return null;
        }
    }

    public async Task<DelayResult> CalculateDelayAsync(DateTime? completionTime, TaskItem task, DateTime viewingDate, int branchId)
    {
        try
        {
            var deadline = _taskScheduler.GetTaskDeadline(task, viewingDate);
            var isHoliday = await _holidayService.IsHolidayAsync(viewingDate);

            // Get any adjustment from daily task
            var dailyTask = await _context.DailyTasks
                .FirstOrDefaultAsync(dt => dt.BranchId == branchId &&
                                           dt.TaskItemId == task.Id &&
                                           dt.TaskDate.Date == viewingDate.Date);

            if (dailyTask?.AdjustmentMinutes > 0)
            {
                deadline = deadline.AddMinutes(dailyTask.AdjustmentMinutes.Value);
            }

            if (!completionTime.HasValue)
            {
                var now = DateTime.UtcNow;

                if (now <= deadline)
                {
                    return new DelayResult
                    {
                        Type = isHoliday ? "holiday" : "on-time",
                        Text = isHoliday ? "Holiday" : string.Empty,
                        IsHoliday = isHoliday
                    };
                }

                var diff = now - deadline;
                return FormatDelay(diff, isHoliday);
            }

            var compTimeUtc = DateTime.SpecifyKind(completionTime.Value, DateTimeKind.Utc);
            var deadlineUtc = DateTime.SpecifyKind(deadline, DateTimeKind.Utc);

            if (compTimeUtc <= deadlineUtc)
            {
                return new DelayResult
                {
                    Type = isHoliday ? "holiday" : "on-time",
                    Text = isHoliday ? "Holiday" : "on time",
                    IsHoliday = isHoliday,
                    HasAdjustment = dailyTask?.AdjustmentMinutes > 0,
                    AdjustmentMinutes = dailyTask?.AdjustmentMinutes ?? 0
                };
            }

            var diffLate = compTimeUtc - deadlineUtc;
            return FormatDelay(diffLate, isHoliday);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating delay");
            return new DelayResult { Type = "error", Text = "Error" };
        }
    }

    #endregion

    #region Validation

    public async Task<bool> IsTaskNameUniqueAsync(string name, int? excludeId = null)
    {
        if (excludeId.HasValue)
        {
            return !await _context.TaskItems
                .AnyAsync(t => t.Name == name && t.Id != excludeId.Value);
        }
        return !await _context.TaskItems.AnyAsync(t => t.Name == name);
    }

    #endregion

    #region Helper Methods

    private async Task AutoAssignTaskToEmployee(DailyTask dailyTask, DateTime date)
    {
        try
        {
            // Find employee assigned to this branch on this date
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
                    await _context.TaskAssignments.AddAsync(taskAssignment);
                    await _context.SaveChangesAsync();

                    _logger.LogDebug("Task {TaskId} auto-assigned to employee {EmployeeId}",
                        dailyTask.Id, branchAssignment.EmployeeId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-assigning task to employee");
        }
    }

    private DelayResult FormatDelay(TimeSpan diff, bool isHoliday)
    {
        var mins = (int)diff.TotalMinutes;
        var hours = (int)diff.TotalHours;
        var days = (int)diff.TotalDays;

        var result = new DelayResult
        {
            IsHoliday = isHoliday
        };

        if (days > 0)
        {
            result.Type = "days";
            result.Text = $"{days}d late";
        }
        else if (hours > 0)
        {
            result.Type = "hours";
            result.Text = $"{hours}h {mins % 60}m late";
        }
        else
        {
            result.Type = "minutes";
            result.Text = $"{mins}m late";
        }

        return result;
    }

    #endregion
}
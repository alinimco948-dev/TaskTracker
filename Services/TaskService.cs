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
    private readonly ITaskCalculationService _taskCalculationService;

    public TaskService(
        ApplicationDbContext context,
        ILogger<TaskService> logger,
        IAuditService auditService,
        IHolidayService holidayService,
        ITaskCalculationService taskCalculationService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
        _holidayService = holidayService;
        _taskCalculationService = taskCalculationService;
    }

    public async Task<List<TaskListViewModel>> GetAllTasksAsync()
    {
        try
        {
            var tasks = await _context.TaskItems.OrderBy(t => t.DisplayOrder).ToListAsync();
            return tasks.Select(t => new TaskListViewModel
            {
                Id = t.Id, Name = t.Name, Deadline = t.Deadline, IsSameDay = t.IsSameDay,
                DisplayOrder = t.DisplayOrder, IsActive = t.IsActive, ExecutionType = t.ExecutionType,
                StartDate = t.StartDate, EndDate = t.EndDate, DurationDays = t.DurationDays
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tasks");
            return new List<TaskListViewModel>();
        }
    }

    public async Task<TaskItem?> GetTaskByIdAsync(int id) => await _context.TaskItems.FindAsync(id);

    public async Task<TaskItem> CreateTaskAsync(TaskItemViewModel model)
    {
        try
        {
            if (await _context.TaskItems.AnyAsync(t => t.Name == model.Name))
                throw new InvalidOperationException($"Task with name '{model.Name}' already exists");

            int displayOrder = model.DisplayOrder > 0 ? model.DisplayOrder : (await _context.TaskItems.MaxAsync(t => (int?)t.DisplayOrder) ?? 0) + 1;

            var task = new TaskItem
            {
                Name = model.Name, Deadline = model.Deadline, IsSameDay = model.IsSameDay,
                DisplayOrder = displayOrder, Description = model.Description, IsActive = true,
                CreatedAt = DateTime.UtcNow, ExecutionType = model.ExecutionType,
                WeeklyDays = model.WeeklyDays.Any() ? string.Join(",", model.WeeklyDays) : null,
                MonthlyPattern = model.MonthlyPattern, DurationDays = model.DurationDays,
                StartDate = model.StartDate.HasValue ? DateTime.SpecifyKind(model.StartDate.Value, DateTimeKind.Utc) : null,
                EndDate = model.ExecutionType == TaskExecutionType.MultiDay && model.DurationDays.HasValue && model.StartDate.HasValue
                    ? DateTime.SpecifyKind(model.StartDate.Value, DateTimeKind.Utc).AddDays(model.DurationDays.Value - 1)
                    : (model.EndDate.HasValue ? DateTime.SpecifyKind(model.EndDate.Value, DateTimeKind.Utc) : null),
                MaxOccurrences = model.MaxOccurrences,
                AvailableFrom = model.AvailableFrom.HasValue ? DateTime.SpecifyKind(model.AvailableFrom.Value, DateTimeKind.Utc) : null,
                AvailableTo = model.AvailableTo.HasValue ? DateTime.SpecifyKind(model.AvailableTo.Value, DateTimeKind.Utc) : null
            };

            _context.TaskItems.Add(task);
            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Create", "TaskItem", task.Id, $"Created task: {task.Name} (Type: {task.ExecutionType})");
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

            if (await _context.TaskItems.AnyAsync(t => t.Name == model.Name && t.Id != model.Id))
                throw new InvalidOperationException($"Task with name '{model.Name}' already exists");

            task.Name = model.Name; task.Deadline = model.Deadline; task.IsSameDay = model.IsSameDay;
            task.Description = model.Description; task.IsActive = model.IsActive; task.UpdatedAt = DateTime.UtcNow;
            task.ExecutionType = model.ExecutionType;
            task.WeeklyDays = model.WeeklyDays.Any() ? string.Join(",", model.WeeklyDays) : null;
            task.MonthlyPattern = model.MonthlyPattern; task.DurationDays = model.DurationDays;
            task.StartDate = model.StartDate.HasValue ? DateTime.SpecifyKind(model.StartDate.Value, DateTimeKind.Utc) : null;
            task.EndDate = model.ExecutionType == TaskExecutionType.MultiDay && model.DurationDays.HasValue && model.StartDate.HasValue
                ? DateTime.SpecifyKind(model.StartDate.Value, DateTimeKind.Utc).AddDays(model.DurationDays.Value - 1)
                : (model.EndDate.HasValue ? DateTime.SpecifyKind(model.EndDate.Value, DateTimeKind.Utc) : null);
            task.MaxOccurrences = model.MaxOccurrences;
            task.AvailableFrom = model.AvailableFrom.HasValue ? DateTime.SpecifyKind(model.AvailableFrom.Value, DateTimeKind.Utc) : null;
            task.AvailableTo = model.AvailableTo.HasValue ? DateTime.SpecifyKind(model.AvailableTo.Value, DateTimeKind.Utc) : null;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Update", "TaskItem", task.Id, $"Updated task: {task.Name}");
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

            if (await _context.DailyTasks.AnyAsync(dt => dt.TaskItemId == id))
            {
                task.IsActive = false;
                task.UpdatedAt = DateTime.UtcNow;
                await _auditService.LogAsync("Deactivate", "TaskItem", task.Id, $"Deactivated task: {task.Name}");
            }
            else
            {
                _context.TaskItems.Remove(task);
                await _auditService.LogAsync("Delete", "TaskItem", task.Id, $"Deleted task: {task.Name}");
            }
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting task {Id}", id);
            return false;
        }
    }

    public async Task<bool> ReorderTasksAsync(List<int> taskIds)
    {
        try
        {
            for (int i = 0; i < taskIds.Count; i++)
            {
                var task = await _context.TaskItems.FindAsync(taskIds[i]);
                if (task != null) task.DisplayOrder = i + 1;
            }
            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Reorder", "TaskItem", null, "Reordered tasks");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering tasks");
            return false;
        }
    }

    public async Task<Dictionary<int, List<string>>> GetHiddenTasksAsync()
    {
        try
        {
            var branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            return branches.ToDictionary(b => b.Id, b => b.HiddenTasks);
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
            if (isVisible) hiddenTasks.Remove(taskName);
            else if (!hiddenTasks.Contains(taskName)) hiddenTasks.Add(taskName);

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

    public async Task<List<TaskItem>> GetTasksVisibleOnDateAsync(DateTime date)
    {
        try
        {
            var allTasks = await _context.TaskItems.Where(t => t.IsActive).OrderBy(t => t.DisplayOrder).ToListAsync();
            return allTasks.Where(t => _taskCalculationService.IsTaskVisibleOnDate(t, date)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting visible tasks for date {Date}", date);
            return new List<TaskItem>();
        }
    }

    public async Task<DelayResult> CalculateDelayAsync(DateTime? completionTime, TaskItem task, DateTime viewingDate, int branchId)
    {
        try
        {
            var dailyTask = await _context.DailyTasks.FirstOrDefaultAsync(dt => dt.BranchId == branchId && dt.TaskItemId == task.Id && dt.TaskDate.Date == viewingDate.Date);
            var isHoliday = await _holidayService.IsHolidayAsync(viewingDate);

            var deadline = _taskCalculationService.CalculateDeadline(task, viewingDate);
            if (dailyTask?.AdjustmentMinutes > 0) deadline = deadline.AddMinutes(dailyTask.AdjustmentMinutes.Value);

            if (!completionTime.HasValue)
            {
                var now = DateTime.UtcNow;
                if (now <= deadline) return new DelayResult { Type = isHoliday ? "holiday" : "on-time", IsHoliday = isHoliday };
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

    private DelayResult FormatDelay(TimeSpan diff, bool isHoliday)
    {
        var mins = (int)diff.TotalMinutes;
        var hours = (int)diff.TotalHours;
        var days = (int)diff.TotalDays;

        if (days > 0) return new DelayResult { Type = "days", Text = $"{days}d late", IsHoliday = isHoliday };
        if (hours > 0) return new DelayResult { Type = "hours", Text = $"{hours}h {mins % 60}m late", IsHoliday = isHoliday };
        return new DelayResult { Type = "minutes", Text = $"{mins}m late", IsHoliday = isHoliday };
    }
}
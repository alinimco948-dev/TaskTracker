using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services.Interfaces;

public interface ITaskService
{
    Task<List<TaskListViewModel>> GetAllTasksAsync();
    Task<TaskItem?> GetTaskByIdAsync(int id);
    Task<TaskItem> CreateTaskAsync(TaskItemViewModel model);
    Task<TaskItem?> UpdateTaskAsync(TaskItemViewModel model);
    Task<bool> DeleteTaskAsync(int id);
    Task<bool> ReorderTasksAsync(List<int> taskIds);
    Task<Dictionary<int, List<string>>> GetHiddenTasksAsync();
    Task<bool> UpdateTaskVisibilityAsync(int branchId, string taskName, bool isVisible);
    Task<DelayResult> CalculateDelayAsync(DateTime? completionTime, TaskItem task, DateTime viewingDate, int branchId);

    // NEW: Scheduling methods
    Task<List<TaskItem>> GetTasksVisibleOnDateAsync(DateTime date);
    
    // Report controller helper methods
    Task<List<TaskListViewModel>> GetActiveTaskSummariesAsync();
}

public class DelayResult
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool HasAdjustment { get; set; }
    public int AdjustmentMinutes { get; set; }
    public bool IsHoliday { get; set; }
}
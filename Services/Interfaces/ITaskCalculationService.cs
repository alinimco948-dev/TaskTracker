using TaskTracker.Models.Entities;

namespace TaskTracker.Services.Interfaces;

public interface ITaskCalculationService
{
    // ========== Core Calculations ==========
    Task<DateTime> CalculateDeadline(TaskItem task, DateTime taskDate);
    Task<DateTime> GetLocalDeadline(TaskItem task, DateTime taskDate);
    bool IsTaskOnTime(DailyTask dailyTask);
    bool IsTaskOnTime(DateTime taskDate, DateTime? completedAt, TimeSpan deadlineTime, bool isSameDay, int? adjustmentMinutes);
    Task<TaskDelayInfo> GetTaskDelayInfoAsync(DailyTask dailyTask);
    
    // ========== Holiday-Aware Calculations ==========
    Task<bool> IsHolidayAsync(DateTime date);
    Task<DateTime> GetNextWorkingDayAsync(DateTime date);
    Task<DateTime> GetNextWorkingDayAsync(DateTime date, bool includeSameDay);
    Task<DateTime> AdjustDeadlineForHolidaysAsync(DateTime deadline);
    Task<DateTime> AdjustDeadlineForHolidaysAsync(DateTime deadline, DateTime stopAtDate);
    Task<TaskDelayInfo> GetHolidayAdjustedDelayInfoAsync(DailyTask dailyTask);
    
    // ========== Task Visibility ==========
    bool IsTaskVisibleOnDate(TaskItem task, DateTime date);
    List<DateTime> GetTaskDatesInRange(TaskItem task, DateTime startDate, DateTime endDate);
    int GetTaskCountInRange(TaskItem task, DateTime startDate, DateTime endDate);
    
    // ========== Task Statistics ==========
    Task<TaskStatistics> GetEmployeeTaskStatisticsAsync(int employeeId, DateTime startDate, DateTime endDate);
    Task<BranchTaskStatistics> GetBranchTaskStatisticsAsync(int branchId, DateTime startDate, DateTime endDate);
    Task<DepartmentTaskStatistics> GetDepartmentTaskStatisticsAsync(int departmentId, DateTime startDate, DateTime endDate);
    
    // ========== Schedule Information ==========
    string GetTaskScheduleSummary(TaskItem task);
    DateTime? GetNextOccurrence(TaskItem task, DateTime fromDate);
    
    // ========== Utility ==========
    DateTime GetCurrentUtcDate();
}

// ========== Result Classes ==========
public class TaskStatistics
{
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public int LateTasks { get; set; }
    public int PendingTasks { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
    public double WeightedScore { get; set; }
}

public class BranchTaskStatistics
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public int LateTasks { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
    public double WeightedScore { get; set; }
    public Dictionary<string, int> TaskBreakdown { get; set; } = new();
    public Dictionary<int, double> EmployeeScores { get; set; } = new();
}

public class DepartmentTaskStatistics
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
    public double WeightedScore { get; set; }
    public Dictionary<string, BranchTaskStatistics> BranchStats { get; set; } = new();
}

public class TaskDelayInfo
{
    public bool IsOnTime { get; set; }
    public string DelayType { get; set; } = string.Empty;
    public string DelayText { get; set; } = string.Empty;
    public DateTime? Deadline { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? AdjustmentMinutes { get; set; }
    public bool WasAdjustedForHoliday { get; set; }
    public string HolidayAdjustmentNote { get; set; } = string.Empty;
}
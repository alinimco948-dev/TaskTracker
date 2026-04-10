namespace TaskTracker.Models.ViewModels;

public class ExecutiveSummaryViewModel
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    
    // Global Health
    public int TotalTasksAssigned { get; set; }
    public int TotalTasksCompleted { get; set; }
    public int TotalTasksPending { get; set; }
    public int TotalTasksOnTime { get; set; }
    public int TotalTasksLate { get; set; }
    
    public double GlobalCompletionRate => TotalTasksAssigned > 0 
        ? Math.Round((double)TotalTasksCompleted / TotalTasksAssigned * 100, 1) : 0;
        
    public double GlobalOnTimeRate => TotalTasksCompleted > 0 
        ? Math.Round((double)TotalTasksOnTime / TotalTasksCompleted * 100, 1) : 0;

    // Daily Trends (Chart Data)
    public List<DailyTrendData> DailyTrends { get; set; } = new();

    // Department Comparison (Chart Data)
    public List<DepartmentComparisonData> DepartmentComparisons { get; set; } = new();

    // High Risk Watchlist
    public List<BranchRiskData> BottomBranches { get; set; } = new();
    public List<TaskRiskData> BottomTasks { get; set; } = new();
}

public class DailyTrendData
{
    public string DateLabel { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public double CompletionRate { get; set; }
}

public class DepartmentComparisonData
{
    public string DepartmentName { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public double CompletionRate { get; set; }
}

public class BranchRiskData
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public double CompletionRate { get; set; }
    public int PendingTasks { get; set; }
}

public class TaskRiskData
{
    public int TaskId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public double CompletionRate { get; set; }
    public int PendingTasks { get; set; }
}

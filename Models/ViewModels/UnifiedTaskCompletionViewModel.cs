namespace TaskTracker.Models.ViewModels;

public class UnifiedTaskCompletionViewModel
{
    // Basic Info
    public int TaskId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public TimeSpan Deadline { get; set; }
    public bool IsSameDay { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool HasData { get; set; }
    
    // Summary Stats
    public int TotalAssignments { get; set; }
    public int Completed { get; set; }
    public int Pending { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
    
    // Timing Stats
    public TimeSpan? AverageCompletionTime { get; set; }
    public TimeSpan? FastestCompletion { get; set; }
    public TimeSpan? SlowestCompletion { get; set; }
    
    // KPI Cards - USING StatsCardViewModel from AlertViewModel.cs
    public List<StatsCardViewModel> KpiCards { get; set; } = new();
    
    // Breakdowns - USING shared classes from ReportSharedViewModels.cs
    public Dictionary<string, BranchTaskStat> BranchStats { get; set; } = new();
    public Dictionary<string, EmployeeTaskStat> EmployeeStats { get; set; } = new();
    public Dictionary<string, int> DailyCompletions { get; set; } = new();
    public List<DailyTaskBreakdownItem> DailyBreakdown { get; set; } = new();
    
    // Insights - USING shared InsightsPanelViewModel
    public InsightsPanelViewModel InsightsPanel { get; set; } = new();
    
    // For backward compatibility
    public List<string> Insights 
    { 
        get => InsightsPanel.Insights; 
        set => InsightsPanel.Insights = value; 
    }
    public List<string> Recommendations 
    { 
        get => InsightsPanel.Recommendations; 
        set => InsightsPanel.Recommendations = value; 
    }
    
    // Filter
    public ReportFilterViewModel FilterViewModel { get; set; } = new();
    
    // Footer
    public string FooterText { get; set; } = string.Empty;
}
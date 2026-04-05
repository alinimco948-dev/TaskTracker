using TaskTracker.Models.ViewModels;

namespace TaskTracker.Models.ViewModels;

public class UnifiedPerformanceViewModel
{
    // Basic Info
    public string ReportType { get; set; } = "Employee";
    public string EntityType { get; set; } = "Employee";
    public int EntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string HeaderIcon { get; set; } = "fa-user";
    public string HeaderGradient { get; set; } = "from-blue-600 to-indigo-700";
    public string EmptyStateIcon { get; set; } = "fa-user-circle";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool HasData { get; set; }
    
    // Performance Metrics
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int PendingTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public int LateTasks { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
    public double OverallScore { get; set; }
    
    // KPI Cards - USING StatsCardViewModel from AlertViewModel.cs
    public List<StatsCardViewModel> KpiCards { get; set; } = new();
    
    // Chart Data - USING shared PerformanceTrendChartViewModel
    public PerformanceTrendChartViewModel TrendChartData { get; set; } = new();
    
    // Comparison Data - USING shared ComparisonTableViewModel
    public ComparisonTableViewModel ComparisonTable { get; set; } = new();
    
    // Performance Distribution - USING shared PerformanceDistributionItem
    public List<PerformanceDistributionItem> PerformanceDistribution { get; set; } = new();
    
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
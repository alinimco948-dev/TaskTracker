using Microsoft.AspNetCore.Mvc.Rendering;

namespace TaskTracker.Models.ViewModels;

// ========== FILTERS ==========
public class ReportFilterViewModel
{
    public string? EntitySelectLabel { get; set; }
    public string EntityIdName { get; set; } = "entityId";
    public string EntitySelectPlaceholder { get; set; } = "Choose...";
    public List<SelectListItem> EntitySelectItems { get; set; } = new();
    public int? SelectedEntityId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string ButtonText { get; set; } = "Generate";
    public bool ShowExport { get; set; }
    public bool ExportDataAvailable { get; set; }
}

// ========== HEADER ==========
public class PerformanceHeaderViewModel
{
    public string ReportType { get; set; } = string.Empty;
    public string Icon { get; set; } = "fa-chart-line";
    public string GradientColors { get; set; } = "from-blue-600 to-indigo-700";
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string SubtitleColor { get; set; } = "text-blue-100";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Rating { get; set; } = string.Empty;
    public string RatingIcon { get; set; } = "fa-chart-line";
    public string RatingTextColor { get; set; } = "text-blue-100";
}

// ========== INSIGHTS ==========
public class InsightsPanelViewModel
{
    public List<string> Insights { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

// ========== CHARTS ==========
public class PerformanceTrendChartViewModel
{
    public string ChartId { get; set; } = "performanceTrendChart";
    public string Title { get; set; } = "Performance Trend";
    public string Subtitle { get; set; } = "Daily completion and on-time rates";
    public List<string> Labels { get; set; } = new();
    public List<double> CompletionRates { get; set; } = new();
    public List<double> OnTimeRates { get; set; } = new();
}

// ========== COMPARISON TABLE ==========
public class ComparisonTableItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public double Score { get; set; }
    public string ScoreColor { get; set; } = "text-gray-600";
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public double OnTimeRate { get; set; }
    public double VsAverage { get; set; }
    public string VsAverageText => VsAverage >= 0 ? $"+{VsAverage}%" : $"{VsAverage}%";
    public string VsAverageClass => VsAverage >= 0 ? "text-green-600" : "text-red-600";
    public string VsAverageIcon => VsAverage >= 0 ? "fa-arrow-up" : "fa-arrow-down";
    public string StatusText { get; set; } = string.Empty;
    public string StatusBadgeClass { get; set; } = string.Empty;
    public bool IsTopPerformer { get; set; }
    public bool IsLowPerformer { get; set; }
}

public class ComparisonTableViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Icon { get; set; } = "fa-chart-line";
    public string IconColor { get; set; } = "text-blue-600";
    public string HeaderGradient { get; set; } = "from-blue-50 to-indigo-50";
    public List<string> Headers { get; set; } = new() { "Name", "Score", "vs Avg", "Total", "Completed", "On Time", "On Time Rate", "Status" };
    public List<ComparisonTableItemViewModel> Items { get; set; } = new();
    public bool ShowAverage { get; set; }
    public bool ShowVsAverage { get; set; }
    public double AverageValue { get; set; }
    public string AverageColor { get; set; } = "text-blue-600";
}

// ========== PERFORMANCE DISTRIBUTION ==========
public class PerformanceDistributionItem
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
    public string BgColor { get; set; } = string.Empty;
    public string TextColor { get; set; } = string.Empty;
}

// ========== TASK STATS (Single Source) ==========
public class BranchTaskStat
{
    public string BranchName { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Completed { get; set; }
    public double CompletionRate { get; set; }
}

public class EmployeeTaskStat
{
    public string EmployeeName { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Completed { get; set; }
    public double CompletionRate { get; set; }
}

public class DailyTaskBreakdownItem
{
    public DateTime Date { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsOnTime { get; set; }
    public string CompletionTime { get; set; } = string.Empty;
}

// ========== FOOTER ==========
public class ReportFooterViewModel
{
    public string FooterText { get; set; } = string.Empty;
}
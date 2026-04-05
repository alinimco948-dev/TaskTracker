namespace TaskTracker.Models.ViewModels;

public class AlertViewModel
{
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "info";
    public bool Dismissible { get; set; } = true;
}

// ENHANCED - Add progress bar support
public class StatsCardViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueColor { get; set; } = "text-gray-800";
    public string Subtext { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string IconColor { get; set; } = "text-gray-600";
    public string IconBgColor { get; set; } = "bg-gray-100";
    
    // NEW - Progress bar support (merged from KpiCardViewModel)
    public bool ShowProgressBar { get; set; }
    public double ProgressValue { get; set; }
    public string ProgressBarColor { get; set; } = "bg-blue-500";
}

public class PerformanceBarViewModel
{
    public int Value { get; set; }
    public string BarColor { get; set; } = "#3b82f6";
    public string Icon { get; set; } = "fa-chart-line";
    public string TextColor { get; set; } = "text-blue-600";
    public string Tooltip { get; set; } = string.Empty;
}

public class EmptyStateViewModel
{
    public string Icon { get; set; } = "fa-inbox";
    public string IconColor { get; set; } = "text-gray-300";
    public string Title { get; set; } = "No Data Found";
    public string Message { get; set; } = string.Empty;
    public string ButtonText { get; set; } = string.Empty;
    public string ButtonUrl { get; set; } = string.Empty;
    public string ButtonIcon { get; set; } = "fa-plus-circle";
    public string ButtonColor { get; set; } = "bg-blue-600";
}
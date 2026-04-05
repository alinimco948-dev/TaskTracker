namespace TaskTracker.Models.ViewModels;

public class EmployeeRankingViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    
    // Weighted Score (70% completion + 30% on-time)
    public int PerformanceScore { get; set; }
    
    // Individual metrics for transparency
    public int CompletionRate { get; set; }
    public int OnTimeRate { get; set; }
    
    public int Rank { get; set; }
    public bool IsActive { get; set; }
    public string Initials { get; set; } = string.Empty;
    
    // Task statistics
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public int LateTasks { get; set; }
}
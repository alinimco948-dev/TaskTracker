namespace TaskTracker.Models.ViewModels;

public class EmployeeRankingViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int PerformanceScore { get; set; }
    public int Rank { get; set; }
    public bool IsActive { get; set; }
    public string Initials { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
}
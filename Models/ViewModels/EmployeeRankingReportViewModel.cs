namespace TaskTracker.Models.ViewModels;

public class EmployeeRankingReportViewModel : ReportBaseViewModel
{
    public List<EmployeeRankingViewModel> Rankings { get; set; } = new();
    
    public int TotalEmployees { get; set; }
    public double AverageScore { get; set; }
    public int TopScore { get; set; }
    
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services.Interfaces;

public interface IReportService
{
    // ========== Report Management ==========
    Task<Report> CreateReportAsync(Report report);
    Task<Report?> UpdateReportAsync(Report report);
    Task<bool> DeleteReportAsync(int id);
    Task<Report?> GetReportByIdAsync(int id);
    Task<List<Report>> GetUserReportsAsync(string userId);
    Task<List<Report>> GetPublicReportsAsync();
    Task<List<Report>> GetReportsByTypeAsync(string reportType);
    Task<List<Report>> GetReportsByCategoryAsync(string category);
    Task<List<Report>> GetScheduledReportsAsync();
    Task<List<Report>> GetTemplatesAsync();

    // ========== Report Execution ==========
    Task<object> ExecuteReportAsync(int reportId, Dictionary<string, object>? parameters = null);
    Task<EmployeePerformanceViewModel> ExecuteEmployeeReportAsync(int employeeId, DateTime startDate, DateTime endDate);
    Task<BranchPerformanceViewModel> ExecuteBranchReportAsync(int branchId, DateTime startDate, DateTime endDate);
    Task<DepartmentPerformanceViewModel> ExecuteDepartmentReportAsync(int departmentId, DateTime startDate, DateTime endDate);
    Task<TaskCompletionViewModel> ExecuteTaskReportAsync(int taskId, DateTime startDate, DateTime endDate);
    Task<AuditLogReportViewModel> ExecuteAuditReportAsync(DateTime startDate, DateTime endDate, string? action = null, string? entityType = null);
    Task<List<Dictionary<string, object>>> ExecuteCustomReportAsync(CustomReportRequest request);
    Task<ExecutiveSummaryViewModel> GetExecutiveSummaryAsync(DateTime startDate, DateTime endDate);
    
    // ========== Employee Comparison & Ranking ==========
    Task<List<EmployeeRankingViewModel>> GetEmployeeRankingAsync(DateTime? startDate = null, DateTime? endDate = null);
    
    // UPDATED: New method signature with employeeIds and comparisonMode
    Task<EmployeeComparisonViewModel> ExecuteEmployeeComparisonReportAsync(int? branchId, List<int> employeeIds, DateTime startDate, DateTime endDate, string comparisonMode = "branch");

    // ========== Report Export ==========
    Task<byte[]> ExportToExcelAsync(object data, string reportName);
    Task<byte[]> ExportToCsvAsync(object data, string reportName);
    Task<byte[]> ExportToPdfAsync(object data, string reportName);

    // ========== Report Scheduling ==========
    Task<bool> ScheduleReportAsync(int reportId, string cronExpression, List<string> recipients);
    Task<bool> UnscheduleReportAsync(int reportId);
    Task ProcessScheduledReportsAsync();

    // ========== Report Insights ==========
    Task<List<string>> GenerateInsightsAsync(EmployeePerformanceViewModel report);
    Task<List<string>> GenerateRecommendationsAsync(EmployeePerformanceViewModel report);

    // ========== Report Templates ==========
    Task<Report> CreateFromTemplateAsync(string templateName, string newName);
}
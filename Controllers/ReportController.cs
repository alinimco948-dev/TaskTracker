using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskTracker.Controllers;

public class ReportController : Controller
{
    private readonly IReportService _reportService;
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private readonly IDepartmentService _departmentService;
    private readonly ITaskService _taskService;
    private readonly IAuditService _auditService;
    private readonly ITaskCalculationService _taskCalculationService;
    private readonly ILogger<ReportController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly ITimezoneService _timezoneService;

    public ReportController(
        IReportService reportService,
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ITaskService taskService,
        IAuditService auditService,
        ITaskCalculationService taskCalculationService,
        ILogger<ReportController> logger,
        ApplicationDbContext context,
        ITimezoneService timezoneService)
    {
        _reportService = reportService;
        _employeeService = employeeService;
        _branchService = branchService;
        _departmentService = departmentService;
        _taskService = taskService;
        _auditService = auditService;
        _taskCalculationService = taskCalculationService;
        _logger = logger;
        _context = context;
        _timezoneService = timezoneService;
    }

    // GET: Report/ExecutiveSummary
    public async Task<IActionResult> ExecutiveSummary(DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var end = endDate ?? _timezoneService.GetCurrentLocalTime().Date;
            var start = startDate ?? end.AddDays(-30);

            var model = await _reportService.GetExecutiveSummaryAsync(start, end);
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating executive summary report");
            TempData["ErrorMessage"] = "Could not generate Executive Summary at this time.";
            return RedirectToAction("Index", "Home");
        }
    }

    // GET: Report/EmployeePerformance
    public async Task<IActionResult> EmployeePerformance(int? employeeId, DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 50)
    {
        try
        {
            startDate ??= DateTime.Today.AddMonths(-1);
            endDate ??= DateTime.Today;

            var allEmployees = await _context.Employees
                .Where(e => e.IsActive)
                .Select(e => new { e.Id, e.Name, e.Position })
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Employees = allEmployees;
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            if (employeeId.HasValue && employeeId.Value > 0)
            {
                var report = await _reportService.ExecuteEmployeeReportAsync(employeeId.Value, startDate.Value, endDate.Value);
                
                if (report.DailyBreakdown != null && report.DailyBreakdown.Any())
                {
                    report.DailyBreakdown = report.DailyBreakdown
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();
                }
                
                ViewBag.CurrentPage = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalItems = report.DailyBreakdown?.Count ?? 0;
                
                return View(report);
            }

            return View(new EmployeePerformanceViewModel
            {
                StartDate = startDate.Value,
                EndDate = endDate.Value,
                EmployeeId = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating employee performance report");
            TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
            return View(new EmployeePerformanceViewModel
            {
                StartDate = startDate ?? DateTime.Today.AddMonths(-1),
                EndDate = endDate ?? DateTime.Today,
                EmployeeId = 0
            });
        }
    }

    // GET: Report/EmployeeComparison
   // GET: Report/EmployeeComparison
public async Task<IActionResult> EmployeeComparison(int? branchId, List<int> employeeIds, DateTime? startDate, DateTime? endDate, string comparisonMode = "branch")
{
    try
    {
        if (!startDate.HasValue)
            startDate = DateTime.Today.AddMonths(-1);
        if (!endDate.HasValue)
            endDate = DateTime.Today;
        
        // Get all branches for dropdown
        var branches = await _context.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name })
            .AsNoTracking()
            .ToListAsync();
        
        // Get all employees for selected mode dropdown
        var allEmployees = await _context.Employees
            .Where(e => e.IsActive)
            .OrderBy(e => e.Name)
            .Select(e => new { e.Id, e.Name, e.Position })
            .AsNoTracking()
            .ToListAsync();
        
        ViewBag.Branches = branches;
        ViewBag.AllEmployees = allEmployees;
        ViewBag.SelectedBranchId = branchId;
        ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
        ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");
        
        var report = await _reportService.ExecuteEmployeeComparisonReportAsync(branchId, employeeIds, startDate.Value, endDate.Value, comparisonMode);
        
        return View(report);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error generating employee comparison report");
        TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
        return View(new EmployeeComparisonViewModel());
    }
}
    // GET: Report/ExportEmployeeComparison
public async Task<IActionResult> ExportEmployeeComparison(int? branchId, List<int> employeeIds, DateTime startDate, DateTime endDate, string comparisonMode = "branch", string format = "excel")
{
    try
    {
        var report = await _reportService.ExecuteEmployeeComparisonReportAsync(branchId, employeeIds ?? new List<int>(), startDate, endDate, comparisonMode);
        
        var exportData = new List<Dictionary<string, object>>();
        
        // Header section
        var headerDict = new Dictionary<string, object>();
        headerDict["Report Type"] = "Employee Performance Comparison";
        headerDict["Comparison Mode"] = comparisonMode == "branch" ? $"Branch: {report.BranchName}" : "Selected Employees";
        headerDict["Period"] = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}";
        headerDict["Total Employees Compared"] = report.TotalEmployeesCompared;
        headerDict["Average Score (%)"] = report.AverageScore;
        headerDict["Highest Score (%)"] = report.HighestScore;
        headerDict["Lowest Score (%)"] = report.LowestScore;
        exportData.Add(headerDict);
        
        var separatorDict = new Dictionary<string, object>();
        separatorDict[""] = "";
        exportData.Add(separatorDict);
        
        // Best vs Worst Comparison
        if (report.ComparisonResult != null && report.ComparisonResult.BestPerformer != null && report.ComparisonResult.BestPerformer.Id > 0)
        {
            var comparisonHeader = new Dictionary<string, object>();
            comparisonHeader["PERFORMANCE COMPARISON ANALYSIS"] = "";
            exportData.Add(comparisonHeader);
            
            var bestDict = new Dictionary<string, object>();
            bestDict["Best Performer"] = report.ComparisonResult.BestPerformer.Name;
            bestDict["Best Score (%)"] = report.ComparisonResult.BestPerformer.PerformanceScore;
            bestDict["Best Completion (%)"] = report.ComparisonResult.BestPerformer.CompletionRate;
            bestDict["Best On-Time (%)"] = report.ComparisonResult.BestPerformer.OnTimeRate;
            exportData.Add(bestDict);
            
            var worstDict = new Dictionary<string, object>();
            worstDict["Needs Improvement"] = report.ComparisonResult.WorstPerformer.Name;
            worstDict["Worst Score (%)"] = report.ComparisonResult.WorstPerformer.PerformanceScore;
            worstDict["Worst Completion (%)"] = report.ComparisonResult.WorstPerformer.CompletionRate;
            worstDict["Worst On-Time (%)"] = report.ComparisonResult.WorstPerformer.OnTimeRate;
            exportData.Add(worstDict);
            
            var gapDict = new Dictionary<string, object>();
            gapDict["Score Difference (%)"] = report.ComparisonResult.ScoreDifference;
            gapDict["Completion Gap (%)"] = report.ComparisonResult.CompletionGap;
            gapDict["On-Time Gap (%)"] = report.ComparisonResult.OnTimeGap;
            gapDict["Tasks Difference"] = report.ComparisonResult.TasksGap;
            exportData.Add(gapDict);
            
            exportData.Add(separatorDict);
        }
        
        // Employee Rankings section
        var sectionHeaderDict = new Dictionary<string, object>();
        sectionHeaderDict["EMPLOYEE COMPARISON DETAILS"] = "";
        exportData.Add(sectionHeaderDict);
        
        var columnHeadersDict = new Dictionary<string, object>();
        columnHeadersDict["Rank"] = "Rank";
        columnHeadersDict["Employee"] = "Employee";
        columnHeadersDict["Employee ID"] = "Employee ID";
        columnHeadersDict["Position"] = "Position";
        columnHeadersDict["Department"] = "Department";
        columnHeadersDict["Score (%)"] = "Score (%)";
        columnHeadersDict["Completion Rate (%)"] = "Completion Rate (%)";
        columnHeadersDict["On-Time Rate (%)"] = "On-Time Rate (%)";
        columnHeadersDict["Total Tasks"] = "Total Tasks";
        columnHeadersDict["Completed"] = "Completed";
        columnHeadersDict["On-Time"] = "On-Time";
        columnHeadersDict["Late"] = "Late";
        columnHeadersDict["Pending"] = "Pending";
        columnHeadersDict["Status"] = "Status";
        exportData.Add(columnHeadersDict);
        
        foreach (var emp in report.Employees.OrderByDescending(e => e.PerformanceScore))
        {
            var empDict = new Dictionary<string, object>();
            empDict["Rank"] = emp.Rank;
            empDict["Employee"] = emp.Name;
            empDict["Employee ID"] = emp.EmployeeId;
            empDict["Position"] = emp.Position;
            empDict["Department"] = emp.Department;
            empDict["Score (%)"] = emp.PerformanceScore;
            empDict["Completion Rate (%)"] = emp.CompletionRate;
            empDict["On-Time Rate (%)"] = emp.OnTimeRate;
            empDict["Total Tasks"] = emp.TotalTasks;
            empDict["Completed"] = emp.CompletedTasks;
            empDict["On-Time"] = emp.OnTimeTasks;
            empDict["Late"] = emp.LateTasks;
            empDict["Pending"] = emp.PendingTasks;
            empDict["Status"] = emp.PerformanceLevel;
            exportData.Add(empDict);
        }
        
        // Entities Compared section
        exportData.Add(separatorDict);
        var entitiesHeader = new Dictionary<string, object>();
        entitiesHeader["ENTITIES COMPARED"] = "";
        exportData.Add(entitiesHeader);
        
        var entitiesDict = new Dictionary<string, object>();
        entitiesDict["Comparison Mode"] = comparisonMode == "branch" ? $"By Branch - {report.BranchName}" : "Selected Employees";
        entitiesDict["Employees Compared"] = string.Join(", ", report.Employees.Select(e => e.Name));
        entitiesDict["Total Employees"] = report.TotalEmployeesCompared;
        entitiesDict["Period"] = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}";
        exportData.Add(entitiesDict);
        
        // Focus Areas section
        if (report.ComparisonResult != null && (report.ComparisonResult.CommonStrengths.Any() || report.ComparisonResult.CommonWeaknesses.Any()))
        {
            exportData.Add(separatorDict);
            var focusHeader = new Dictionary<string, object>();
            focusHeader["FOCUS AREAS"] = "";
            exportData.Add(focusHeader);
            
            if (report.ComparisonResult.CommonStrengths.Any())
            {
                var strengthsDict = new Dictionary<string, object>();
                strengthsDict["Common Strengths"] = string.Join(", ", report.ComparisonResult.CommonStrengths);
                exportData.Add(strengthsDict);
            }
            
            if (report.ComparisonResult.CommonWeaknesses.Any())
            {
                var weaknessesDict = new Dictionary<string, object>();
                weaknessesDict["Areas Needing Improvement"] = string.Join(", ", report.ComparisonResult.CommonWeaknesses);
                exportData.Add(weaknessesDict);
            }
        }
        
        // Insights section
        if (report.KeyInsights.Any() || report.Recommendations.Any())
        {
            exportData.Add(separatorDict);
            var insightsHeader = new Dictionary<string, object>();
            insightsHeader["INSIGHTS & RECOMMENDATIONS"] = "";
            exportData.Add(insightsHeader);
            
            if (report.KeyInsights.Any())
            {
                foreach (var insight in report.KeyInsights)
                {
                    var insightDict = new Dictionary<string, object>();
                    insightDict["Insight"] = insight;
                    exportData.Add(insightDict);
                }
            }
            
            if (report.Recommendations.Any())
            {
                foreach (var rec in report.Recommendations)
                {
                    var recDict = new Dictionary<string, object>();
                    recDict["Recommendation"] = rec;
                    exportData.Add(recDict);
                }
            }
        }
        
        byte[] fileData;
        string fileName = $"Employee_Comparison_{DateTime.Now:yyyyMMdd_HHmmss}";
        string contentType;
        
        switch (format.ToLower())
        {
            case "csv":
                fileData = await _reportService.ExportToCsvAsync(exportData, "EmployeeComparison");
                fileName += ".csv";
                contentType = "text/csv";
                break;
            case "pdf":
                fileData = await _reportService.ExportToPdfAsync(exportData, "EmployeeComparison");
                fileName += ".pdf";
                contentType = "application/pdf";
                break;
            default:
                fileData = await _reportService.ExportToExcelAsync(exportData, "EmployeeComparison");
                fileName += ".xlsx";
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                break;
        }
        
        return File(fileData, contentType, fileName);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error exporting employee comparison report: {Message}", ex.Message);
        TempData["ErrorMessage"] = $"Error exporting report: {ex.Message}";
        return RedirectToAction(nameof(EmployeeComparison), new { branchId, employeeIds, startDate, endDate, comparisonMode });
    }
}
    // GET: Report/EmployeeRanking
    public async Task<IActionResult> EmployeeRanking(DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 20)
    {
        try
        {
            startDate ??= DateTime.UtcNow.AddMonths(-1);
            endDate ??= DateTime.UtcNow;

            var rankings = await _reportService.GetEmployeeRankingAsync(startDate, endDate);
            
            var totalItems = rankings.Count;
            var pagedRankings = rankings
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");
            ViewBag.TotalEmployees = totalItems;
            ViewBag.AverageScore = rankings.Any() ? Math.Round(rankings.Average(r => r.PerformanceScore), 0) : 0;
            ViewBag.TopScore = rankings.Any() ? rankings.Max(r => r.PerformanceScore) : 0;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            return View(pagedRankings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating employee ranking");
            TempData["ErrorMessage"] = "Error generating ranking report. Please try again.";
            return View(new List<EmployeeRankingViewModel>());
        }
    }

    // GET: Report/BranchPerformance
    public async Task<IActionResult> BranchPerformance(int? branchId, DateTime? startDate, DateTime? endDate)
    {
        try
        {
            if (!startDate.HasValue)
                startDate = DateTime.Today.AddMonths(-1);
            if (!endDate.HasValue)
                endDate = DateTime.Today;

            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => new { b.Id, b.Name, b.Code })
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Branches = branches;
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            if (!branchId.HasValue || branchId.Value == 0)
            {
                return View(new BranchPerformanceViewModel
                {
                    BranchId = 0,
                    BranchName = "",
                    StartDate = startDate.Value,
                    EndDate = endDate.Value
                });
            }

            var report = await _reportService.ExecuteBranchReportAsync(branchId.Value, startDate.Value, endDate.Value);
            
            return View(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating branch performance report");
            TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
            
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => new { b.Id, b.Name, b.Code })
                .AsNoTracking()
                .ToListAsync();
            ViewBag.Branches = branches;
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            
            return View(new BranchPerformanceViewModel
            {
                BranchId = branchId ?? 0,
                StartDate = startDate ?? DateTime.Today.AddMonths(-1),
                EndDate = endDate ?? DateTime.Today
            });
        }
    }

    // GET: Report/ExportBranchReport
    public async Task<IActionResult> ExportBranchReport(int branchId, DateTime startDate, DateTime endDate, string format = "excel")
    {
        try
        {
            var report = await _reportService.ExecuteBranchReportAsync(branchId, startDate, endDate);
            
            var exportData = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["Branch Name"] = report.BranchName,
                    ["Branch Code"] = report.BranchCode,
                    ["Department"] = report.Department,
                    ["Location"] = report.Location,
                    ["Period"] = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
                    ["Total Tasks"] = report.TotalTasks,
                    ["Completed Tasks"] = report.CompletedTasks,
                    ["Pending Tasks"] = report.PendingTasks,
                    ["Completion Rate (%)"] = report.CompletionRate,
                    ["On Time Tasks"] = report.OnTimeTasks,
                    ["Late Tasks"] = report.LateTasks,
                    ["On Time Rate (%)"] = report.OnTimeRate,
                    ["Overall Score"] = report.OverallScore,
                    ["Active Employees"] = report.ActiveEmployees
                }
            };
            
            if (report.EmployeeScores != null && report.EmployeeScores.Any())
            {
                exportData.Add(new Dictionary<string, object> { [""] = "", ["EMPLOYEE PERFORMANCE"] = "", [""] = "" });
                foreach (var emp in report.EmployeeScores.OrderByDescending(e => e.Value))
                {
                    exportData.Add(new Dictionary<string, object>
                    {
                        ["Employee"] = emp.Key,
                        ["Performance Score (%)"] = emp.Value
                    });
                }
            }
            
            if (report.DailyBreakdown != null && report.DailyBreakdown.Any())
            {
                exportData.Add(new Dictionary<string, object> { [""] = "", ["DAILY BREAKDOWN"] = "", [""] = "" });
                foreach (var daily in report.DailyBreakdown.Take(100))
                {
                    exportData.Add(new Dictionary<string, object>
                    {
                        ["Date"] = daily.Date.ToString("yyyy-MM-dd"),
                        ["Task"] = daily.TaskName,
                        ["Status"] = daily.Status,
                        ["Assigned To"] = daily.AssignedTo,
                        ["Completion Time"] = daily.CompletionTime?.ToString("HH:mm") ?? "",
                        ["Is On Time"] = daily.IsOnTime ? "Yes" : "No"
                    });
                }
            }
            
            byte[] fileData;
            string fileName = $"{SanitizeFileName(report.BranchName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}";
            string contentType;
            
            switch (format.ToLower())
            {
                case "csv":
                    fileData = await _reportService.ExportToCsvAsync(exportData, report.BranchName);
                    fileName += ".csv";
                    contentType = "text/csv";
                    break;
                case "pdf":
                    fileData = await _reportService.ExportToPdfAsync(exportData, report.BranchName);
                    fileName += ".pdf";
                    contentType = "application/pdf";
                    break;
                default:
                    fileData = await _reportService.ExportToExcelAsync(exportData, report.BranchName);
                    fileName += ".xlsx";
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    break;
            }
            
            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting branch report");
            TempData["ErrorMessage"] = "Error exporting report. Please try again.";
            return RedirectToAction(nameof(BranchPerformance), new { branchId, startDate, endDate });
        }
    }

    // GET: Report/DepartmentPerformance
    public async Task<IActionResult> DepartmentPerformance(int? departmentId, DateTime? startDate, DateTime? endDate)
    {
        try
        {
            if (!startDate.HasValue)
                startDate = DateTime.Today.AddMonths(-1);
            if (!endDate.HasValue)
                endDate = DateTime.Today;

            var departments = await _context.Departments
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .Select(d => new { d.Id, d.Name, d.Code })
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Departments = departments;
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            if (!departmentId.HasValue || departmentId.Value == 0)
            {
                return View(new DepartmentPerformanceViewModel
                {
                    DepartmentId = 0,
                    DepartmentName = "",
                    StartDate = startDate.Value,
                    EndDate = endDate.Value
                });
            }

            var report = await _reportService.ExecuteDepartmentReportAsync(departmentId.Value, startDate.Value, endDate.Value);
            
            return View(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating department performance report");
            TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
            
            var departments = await _context.Departments
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .Select(d => new { d.Id, d.Name, d.Code })
                .AsNoTracking()
                .ToListAsync();
            ViewBag.Departments = departments;
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            
            return View(new DepartmentPerformanceViewModel
            {
                DepartmentId = departmentId ?? 0,
                StartDate = startDate ?? DateTime.Today.AddMonths(-1),
                EndDate = endDate ?? DateTime.Today
            });
        }
    }

    // GET: Report/ExportDepartmentReport
    public async Task<IActionResult> ExportDepartmentReport(int departmentId, DateTime startDate, DateTime endDate, string format = "excel")
    {
        try
        {
            var report = await _reportService.ExecuteDepartmentReportAsync(departmentId, startDate, endDate);
            
            var exportData = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["Department Name"] = report.DepartmentName,
                    ["Department Code"] = report.DepartmentCode,
                    ["Period"] = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
                    ["Total Branches"] = report.TotalBranches,
                    ["Active Branches"] = report.ActiveBranches,
                    ["Total Employees"] = report.TotalEmployees,
                    ["Total Tasks"] = report.TotalTasks,
                    ["Completed Tasks"] = report.CompletedTasks,
                    ["Overall Completion Rate (%)"] = report.OverallCompletionRate,
                    ["Overall On-Time Rate (%)"] = report.OverallOnTimeRate
                }
            };
            
            if (report.BranchPerformance != null && report.BranchPerformance.Any())
            {
                exportData.Add(new Dictionary<string, object> { [""] = "", ["BRANCH PERFORMANCE"] = "", [""] = "" });
                foreach (var branch in report.BranchPerformance.OrderByDescending(b => b.Value.CompletionRate))
                {
                    exportData.Add(new Dictionary<string, object>
                    {
                        ["Branch Name"] = branch.Key,
                        ["Total Tasks"] = branch.Value.TotalTasks,
                        ["Completed Tasks"] = branch.Value.CompletedTasks,
                        ["Completion Rate (%)"] = branch.Value.CompletionRate,
                        ["On-Time Rate (%)"] = branch.Value.OnTimeRate,
                        ["Employee Count"] = branch.Value.EmployeeCount
                    });
                }
            }
            
            byte[] fileData;
            string fileName = $"{SanitizeFileName(report.DepartmentName)}_Department_Report_{DateTime.Now:yyyyMMdd_HHmmss}";
            string contentType;
            
            switch (format.ToLower())
            {
                case "csv":
                    fileData = await _reportService.ExportToCsvAsync(exportData, report.DepartmentName);
                    fileName += ".csv";
                    contentType = "text/csv";
                    break;
                case "pdf":
                    fileData = await _reportService.ExportToPdfAsync(exportData, report.DepartmentName);
                    fileName += ".pdf";
                    contentType = "application/pdf";
                    break;
                default:
                    fileData = await _reportService.ExportToExcelAsync(exportData, report.DepartmentName);
                    fileName += ".xlsx";
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    break;
            }
            
            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting department report");
            TempData["ErrorMessage"] = "Error exporting report. Please try again.";
            return RedirectToAction(nameof(DepartmentPerformance), new { departmentId, startDate, endDate });
        }
    }

    // GET: Report/TaskCompletion
    public async Task<IActionResult> TaskCompletion(int? taskId, DateTime? startDate, DateTime? endDate)
    {
        try
        {
            startDate ??= DateTime.Today.AddMonths(-1);
            endDate ??= DateTime.Today;

            var tasks = await _context.TaskItems
                .Where(t => t.IsActive)
                .Select(t => new { t.Id, t.Name })
                .ToListAsync();

            ViewBag.Tasks = tasks;
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            if (taskId.HasValue && taskId.Value > 0)
            {
                var report = await _reportService.ExecuteTaskReportAsync(taskId.Value, startDate.Value, endDate.Value);
                return View(report);
            }

            return View(new TaskCompletionViewModel
            {
                StartDate = startDate.Value,
                EndDate = endDate.Value
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating task completion report");
            TempData["ErrorMessage"] = "Error generating report. Please try again.";
            return View(new TaskCompletionViewModel
            {
                StartDate = startDate ?? DateTime.Today.AddMonths(-1),
                EndDate = endDate ?? DateTime.Today
            });
        }
    }

    // GET: Report/ExportTaskReport
    public async Task<IActionResult> ExportTaskReport(int taskId, DateTime startDate, DateTime endDate, string format = "excel")
    {
        try
        {
            var report = await _reportService.ExecuteTaskReportAsync(taskId, startDate, endDate);
            
            var exportData = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["Task Name"] = report.TaskName,
                    ["Period"] = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
                    ["Total Assignments"] = report.TotalAssignments,
                    ["Completed"] = report.Completed,
                    ["Completion Rate (%)"] = report.CompletionRate,
                    ["On Time"] = report.OnTime,
                    ["On Time Rate (%)"] = report.OnTimeRate,
                    ["Late"] = report.Late,
                    ["Pending"] = report.Pending
                }
            };
            
            foreach (var branch in report.BranchStats ?? new Dictionary<string, BranchTaskStat>())
            {
                exportData.Add(new Dictionary<string, object>
                {
                    ["Branch"] = branch.Key,
                    ["Total"] = branch.Value.Total,
                    ["Completed"] = branch.Value.Completed,
                    ["Completion Rate (%)"] = branch.Value.CompletionRate
                });
            }
            
            byte[] fileData;
            string fileName = $"{SanitizeFileName(report.TaskName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}";
            string contentType;
            
            switch (format.ToLower())
            {
                case "csv":
                    fileData = await _reportService.ExportToCsvAsync(exportData, report.TaskName);
                    fileName += ".csv";
                    contentType = "text/csv";
                    break;
                case "pdf":
                    fileData = await _reportService.ExportToPdfAsync(exportData, report.TaskName);
                    fileName += ".pdf";
                    contentType = "application/pdf";
                    break;
                default:
                    fileData = await _reportService.ExportToExcelAsync(exportData, report.TaskName);
                    fileName += ".xlsx";
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    break;
            }
            
            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting task report");
            TempData["ErrorMessage"] = "Error exporting report. Please try again.";
            return RedirectToAction(nameof(TaskCompletion), new { taskId, startDate, endDate });
        }
    }

    // GET: Report/AuditLog
    public async Task<IActionResult> AuditLog(DateTime? startDate, DateTime? endDate, string? action = null, string? entityType = null, int page = 1, int pageSize = 50)
    {
        try
        {
            if (!startDate.HasValue)
                startDate = DateTime.Today.AddDays(-30);
            if (!endDate.HasValue)
                endDate = DateTime.Today;

            var report = await _reportService.ExecuteAuditReportAsync(startDate.Value, endDate.Value, action, entityType);
            
            var totalItems = report.Events.Count;
            var pagedEvents = report.Events
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");
            ViewBag.Actions = await _context.AuditLogs
                .Where(a => a.Timestamp >= startDate.Value && a.Timestamp <= endDate.Value)
                .Select(a => a.Action)
                .Distinct()
                .Take(50)
                .ToListAsync();
            ViewBag.EntityTypes = await _context.AuditLogs
                .Where(a => a.Timestamp >= startDate.Value && a.Timestamp <= endDate.Value)
                .Select(a => a.EntityType)
                .Distinct()
                .Take(50)
                .ToListAsync();
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = totalItems;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            ViewBag.SelectedAction = action;
            ViewBag.SelectedEntityType = entityType;
            
            report.Events = pagedEvents;
            
            return View(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating audit log report");
            TempData["ErrorMessage"] = "Error generating audit log report. Please try again.";
            return View(new AuditLogReportViewModel());
        }
    }

    // GET: Report/ExportAuditLog
    public async Task<IActionResult> ExportAuditLog(DateTime startDate, DateTime endDate, string? action = null, string? entityType = null, string format = "excel")
    {
        try
        {
            var report = await _reportService.ExecuteAuditReportAsync(startDate, endDate, action, entityType);
            
            var exportData = report.Events.Select(e => new Dictionary<string, object>
            {
                ["Timestamp"] = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                ["Action"] = e.Action,
                ["Entity Type"] = e.EntityType,
                ["Entity ID"] = e.EntityId ?? 0,
                ["Description"] = e.Description ?? "",
                ["User"] = e.UserName ?? "",
                ["IP Address"] = e.IpAddress ?? ""
            }).ToList();
            
            byte[] fileData;
            string fileName = $"AuditLog_{DateTime.Now:yyyyMMdd_HHmmss}";
            string contentType;
            
            switch (format.ToLower())
            {
                case "csv":
                    fileData = await _reportService.ExportToCsvAsync(exportData, "AuditLog");
                    fileName += ".csv";
                    contentType = "text/csv";
                    break;
                case "pdf":
                    fileData = await _reportService.ExportToPdfAsync(exportData, "AuditLog");
                    fileName += ".pdf";
                    contentType = "application/pdf";
                    break;
                default:
                    fileData = await _reportService.ExportToExcelAsync(exportData, "AuditLog");
                    fileName += ".xlsx";
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    break;
            }
            
            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting audit log");
            TempData["ErrorMessage"] = "Error exporting audit log. Please try again.";
            return RedirectToAction(nameof(AuditLog), new { startDate, endDate, action, entityType });
        }
    }

     // Helper Methods for sanitization
    private string SanitizeInput(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        // Remove any HTML tags
        input = Regex.Replace(input, @"<[^>]*>", string.Empty);
        // Remove any script tags
        input = Regex.Replace(input, @"<script.*?</script>", string.Empty, RegexOptions.IgnoreCase);
        // Remove any potential SQL injection patterns (basic)
        input = Regex.Replace(input, @"['"";\-]", string.Empty);
        
        return input.Trim();
    }
    
    
    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "Report";
        
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
        
        return fileName;
    }
}
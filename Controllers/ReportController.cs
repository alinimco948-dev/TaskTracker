using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;
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
    private readonly IGradingService _gradingService;
    private readonly ILogger<ReportController> _logger;
    private readonly ITimezoneService _timezoneService;

    public ReportController(
        IReportService reportService,
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ITaskService taskService,
        IAuditService auditService,
        IGradingService gradingService,
        ILogger<ReportController> logger,
        ITimezoneService timezoneService)
    {
        _reportService = reportService;
        _employeeService = employeeService;
        _branchService = branchService;
        _departmentService = departmentService;
        _taskService = taskService;
        _auditService = auditService;
        _gradingService = gradingService;
        _logger = logger;
        _timezoneService = timezoneService;
    }

    private DateTime GetDefaultStartDate() => _timezoneService.GetCurrentLocalTime().AddMonths(-1).Date;
    private DateTime GetDefaultEndDate() => _timezoneService.GetCurrentLocalTime().Date;

    // GET: Report/ExecutiveSummary
    [HttpGet]
    public async Task<IActionResult> ExecutiveSummary(DateTime? startDate = null, DateTime? endDate = null, bool generate = false)
    {
        try
        {
            var end = endDate ?? GetDefaultEndDate();
            var start = startDate ?? end.AddDays(-30);

            var model = new ExecutiveSummaryViewModel
            {
                StartDate = start,
                EndDate = end,
                IsGenerated = false
            };

            if (generate)
            {
                var reportData = await _reportService.GetExecutiveSummaryAsync(start, end);
                
                // Map properties
                model.TotalTasksAssigned = reportData.TotalTasksAssigned;
                model.TotalTasksCompleted = reportData.TotalTasksCompleted;
                model.TotalTasksPending = reportData.TotalTasksPending;
                model.TotalTasksOnTime = reportData.TotalTasksOnTime;
                model.TotalTasksLate = reportData.TotalTasksLate;
                model.DailyTrends = reportData.DailyTrends;
                model.DepartmentComparisons = reportData.DepartmentComparisons;
                model.BottomBranches = reportData.BottomBranches;
                model.BottomTasks = reportData.BottomTasks;
                model.IsGenerated = true;
            }

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
    [HttpGet]
    public async Task<IActionResult> EmployeePerformance(int? employeeId, DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 50, bool generate = false)
    {
        try
        {
            var start = startDate ?? GetDefaultStartDate();
            var end = endDate ?? GetDefaultEndDate();

            var employees = await _employeeService.GetActiveEmployeesSummaryAsync();
            ViewBag.Employees = employees.Select(e => new { e.Id, e.Name, e.Position }).ToList();
            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");

            var model = new EmployeePerformanceViewModel 
            { 
                StartDate = start, 
                EndDate = end, 
                EmployeeId = employeeId ?? 0,
                IsGenerated = false
            };

            if (generate && employeeId.HasValue && employeeId.Value > 0)
            {
                var report = await _reportService.ExecuteEmployeeReportAsync(employeeId.Value, start, end);
                report.IsGenerated = true;

                if (report.DailyBreakdown?.Any() == true)
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

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating employee performance report");
            TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
            return View(new EmployeePerformanceViewModel
            {
                StartDate = GetDefaultStartDate(),
                EndDate = GetDefaultEndDate(),
                EmployeeId = employeeId ?? 0,
                IsGenerated = false
            });
        }
    }

    // GET: Report/EmployeeComparison
    [HttpGet]
    public async Task<IActionResult> EmployeeComparison(int? branchId, List<int>? employeeIds, DateTime? startDate, DateTime? endDate, string comparisonMode = "branch", bool generate = false)
    {
        try
        {
            var start = startDate ?? GetDefaultStartDate();
            var end = endDate ?? GetDefaultEndDate();

            var branches = await _branchService.GetActiveBranchesSummaryAsync();
            var employees = await _employeeService.GetActiveEmployeesSummaryAsync();

            ViewBag.Branches = branches.Select(b => new { b.Id, b.Name }).ToList();
            ViewBag.AllEmployees = employees.Select(e => new { e.Id, e.Name, e.Position }).ToList();
            ViewBag.SelectedBranchId = branchId;
            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");

            if (generate)
            {
                var report = await _reportService.ExecuteEmployeeComparisonReportAsync(
                    branchId, employeeIds ?? new List<int>(), start, end, comparisonMode);
                report.IsGenerated = true;
                return View(report);
            }

            return View(new EmployeeComparisonViewModel 
            { 
                StartDate = start, 
                EndDate = end, 
                IsGenerated = false 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating employee comparison report");
            TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
            return View(new EmployeeComparisonViewModel { IsGenerated = false });
        }
    }

    // GET: Report/ExportEmployeeComparison
    [HttpGet]
    public async Task<IActionResult> ExportEmployeeComparison(int? branchId, List<int>? employeeIds, DateTime startDate, DateTime endDate, string comparisonMode = "branch", string format = "excel")
    {
        try
        {
            var report = await _reportService.ExecuteEmployeeComparisonReportAsync(
                branchId, employeeIds ?? new List<int>(), startDate, endDate, comparisonMode);

            var exportData = BuildEmployeeComparisonExportData(report, startDate, endDate, comparisonMode);

            var (fileData, fileName, contentType) = format.ToLower() switch
            {
                "csv" => (await _reportService.ExportToCsvAsync(exportData, "EmployeeComparison"), $"EmployeeComparison_{DateTime.Now:yyyyMMdd_HHmmss}.csv", "text/csv"),
                "pdf" => (await _reportService.ExportToPdfAsync(exportData, "EmployeeComparison"), $"EmployeeComparison_{DateTime.Now:yyyyMMdd_HHmmss}.pdf", "application/pdf"),
                _ => (await _reportService.ExportToExcelAsync(exportData, "EmployeeComparison"), $"EmployeeComparison_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            };

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
    [HttpGet]
    public async Task<IActionResult> EmployeeRanking(DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 20, bool generate = false)
    {
        try
        {
            var start = startDate ?? _timezoneService.GetCurrentLocalTime().AddMonths(-1);
            var end = endDate ?? _timezoneService.GetCurrentLocalTime();

            var model = new EmployeeRankingReportViewModel
            {
                StartDate = start,
                EndDate = end,
                CurrentPage = page,
                PageSize = pageSize,
                IsGenerated = false
            };

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");

            if (generate)
            {
                var rankings = await _reportService.GetEmployeeRankingAsync(start, end);

                var totalItems = rankings.Count;
                var pagedRankings = rankings.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                model.Rankings = pagedRankings;
                model.TotalEmployees = totalItems;
                model.AverageScore = rankings.Any() ? Math.Round(rankings.Average(r => r.PerformanceScore), 0) : 0;
                model.TopScore = rankings.Any() ? rankings.Max(r => r.PerformanceScore) : 0;
                model.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);
                model.IsGenerated = true;

                ViewBag.TotalEmployees = totalItems;
                ViewBag.AverageScore = model.AverageScore;
                ViewBag.TopScore = model.TopScore;
                ViewBag.CurrentPage = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = model.TotalPages;
            }

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating employee ranking");
            TempData["ErrorMessage"] = "Error generating ranking report. Please try again.";
            return View(new EmployeeRankingReportViewModel { IsGenerated = false });
        }
    }

    // GET: Report/BranchPerformance
    [HttpGet]
    public async Task<IActionResult> BranchPerformance(int? branchId, DateTime? startDate, DateTime? endDate, bool generate = false)
    {
        try
        {
            var start = startDate ?? GetDefaultStartDate();
            var end = endDate ?? GetDefaultEndDate();

            var branches = await _branchService.GetActiveBranchesSummaryAsync();
            ViewBag.Branches = branches.Select(b => new { b.Id, b.Name, b.Code }).ToList();
            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");

            var model = new BranchPerformanceViewModel 
            { 
                BranchId = branchId ?? 0, 
                BranchName = "", 
                StartDate = start, 
                EndDate = end,
                IsGenerated = false
            };

            if (generate && branchId.HasValue && branchId.Value > 0)
            {
                var report = await _reportService.ExecuteBranchReportAsync(branchId.Value, start, end);
                report.IsGenerated = true;
                return View(report);
            }

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating branch performance report");
            TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";

            var branches = await _branchService.GetActiveBranchesSummaryAsync();
            ViewBag.Branches = branches.Select(b => new { b.Id, b.Name, b.Code }).ToList();
            ViewBag.StartDate = (startDate ?? GetDefaultStartDate()).ToString("yyyy-MM-dd");
            ViewBag.EndDate = (endDate ?? GetDefaultEndDate()).ToString("yyyy-MM-dd");

            return View(new BranchPerformanceViewModel
            {
                BranchId = branchId ?? 0,
                StartDate = startDate ?? GetDefaultStartDate(),
                EndDate = endDate ?? GetDefaultEndDate(),
                IsGenerated = false
            });
        }
    }

    // GET: Report/ExportBranchReport
    [HttpGet]
    public async Task<IActionResult> ExportBranchReport(int branchId, DateTime startDate, DateTime endDate, string format = "excel")
    {
        try
        {
            var report = await _reportService.ExecuteBranchReportAsync(branchId, startDate, endDate);
            var exportData = BuildBranchReportExportData(report, startDate, endDate);

            var (fileData, fileName, contentType) = format.ToLower() switch
            {
                "csv" => (await _reportService.ExportToCsvAsync(exportData, report.BranchName), $"{SanitizeFileName(report.BranchName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv", "text/csv"),
                "pdf" => (await _reportService.ExportToPdfAsync(exportData, report.BranchName), $"{SanitizeFileName(report.BranchName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf", "application/pdf"),
                _ => (await _reportService.ExportToExcelAsync(exportData, report.BranchName), $"{SanitizeFileName(report.BranchName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            };

            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting branch report");
            TempData["ErrorMessage"] = "Error exporting report. Please try again.";
            return RedirectToAction(nameof(BranchPerformance), new { branchId, startDate, endDate });
        }
    }



    // GET: Report/TaskCompletion
    [HttpGet]
    public async Task<IActionResult> TaskCompletion(int? taskId, DateTime? startDate, DateTime? endDate, bool generate = false)
    {
        try
        {
            var start = startDate ?? GetDefaultStartDate();
            var end = endDate ?? GetDefaultEndDate();

            var tasks = await _taskService.GetActiveTaskSummariesAsync();
            ViewBag.Tasks = tasks.Select(t => new { t.Id, t.Name }).ToList();
            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");

            var model = new TaskCompletionViewModel 
            { 
                StartDate = start, 
                EndDate = end, 
                TaskId = taskId ?? 0,
                IsGenerated = false 
            };

            if (generate && taskId.HasValue && taskId.Value > 0)
            {
                var report = await _reportService.ExecuteTaskReportAsync(taskId.Value, start, end);
                report.IsGenerated = true;
                return View(report);
            }

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating task completion report");
            TempData["ErrorMessage"] = "Error generating report. Please try again.";
            return View(new TaskCompletionViewModel
            {
                StartDate = GetDefaultStartDate(),
                EndDate = GetDefaultEndDate(),
                IsGenerated = false
            });
        }
    }

    // GET: Report/ExportTaskReport
    [HttpGet]
    public async Task<IActionResult> ExportTaskReport(int taskId, DateTime startDate, DateTime endDate, string format = "excel")
    {
        try
        {
            var report = await _reportService.ExecuteTaskReportAsync(taskId, startDate, endDate);
            var exportData = BuildTaskReportExportData(report, startDate, endDate);

            var (fileData, fileName, contentType) = format.ToLower() switch
            {
                "csv" => (await _reportService.ExportToCsvAsync(exportData, report.TaskName), $"{SanitizeFileName(report.TaskName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv", "text/csv"),
                "pdf" => (await _reportService.ExportToPdfAsync(exportData, report.TaskName), $"{SanitizeFileName(report.TaskName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf", "application/pdf"),
                _ => (await _reportService.ExportToExcelAsync(exportData, report.TaskName), $"{SanitizeFileName(report.TaskName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            };

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
    [HttpGet]
    public async Task<IActionResult> AuditLog(DateTime? startDate, DateTime? endDate, string? action = null, string? entityType = null, int page = 1, int pageSize = 50, bool generate = false)
    {
        try
        {
            var start = startDate ?? _timezoneService.GetCurrentLocalTime().AddDays(-30);
            var end = endDate ?? GetDefaultEndDate();

            var (actions, entityTypes) = await _auditService.GetFilterOptionsAsync(start, end);

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");
            ViewBag.Actions = actions;
            ViewBag.EntityTypes = entityTypes;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.SelectedAction = action;
            ViewBag.SelectedEntityType = entityType;

            var model = new AuditLogReportViewModel
            {
                StartDate = start,
                EndDate = end,
                IsGenerated = false
            };

            if (generate)
            {
                var report = await _reportService.ExecuteAuditReportAsync(start, end, action, entityType);

                var totalItems = report.Events.Count;
                var pagedEvents = report.Events.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                report.Events = pagedEvents;
                report.IsGenerated = true;

                ViewBag.TotalItems = totalItems;
                ViewBag.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);

                return View(report);
            }

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating audit log report");
            TempData["ErrorMessage"] = "Error generating audit log report. Please try again.";
            return View(new AuditLogReportViewModel { IsGenerated = false });
        }
    }

    // GET: Report/ExportAuditLog
    [HttpGet]
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

            var (fileData, fileName, contentType) = format.ToLower() switch
            {
                "csv" => (await _reportService.ExportToCsvAsync(exportData, "AuditLog"), $"AuditLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv", "text/csv"),
                "pdf" => (await _reportService.ExportToPdfAsync(exportData, "AuditLog"), $"AuditLog_{DateTime.Now:yyyyMMdd_HHmmss}.pdf", "application/pdf"),
                _ => (await _reportService.ExportToExcelAsync(exportData, "AuditLog"), $"AuditLog_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            };

            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting audit log");
            TempData["ErrorMessage"] = "Error exporting audit log. Please try again.";
            return RedirectToAction(nameof(AuditLog), new { startDate, endDate, action, entityType });
        }
    }

    // ========== Private Helper Methods ==========

    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "Report";
        return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
    }

    private List<Dictionary<string, object>> BuildEmployeeComparisonExportData(EmployeeComparisonViewModel report, DateTime startDate, DateTime endDate, string comparisonMode)
    {
        var exportData = new List<Dictionary<string, object>>();

        exportData.Add(new Dictionary<string, object>
        {
            ["Report Type"] = "Employee Performance Comparison",
            ["Comparison Mode"] = comparisonMode == "branch" ? $"Branch: {report.BranchName}" : "Selected Employees",
            ["Period"] = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
            ["Total Employees Compared"] = report.TotalEmployeesCompared,
            ["Average Score (%)"] = report.AverageScore,
            ["Highest Score (%)"] = report.HighestScore,
            ["Lowest Score (%)"] = report.LowestScore
        });

        exportData.Add(new Dictionary<string, object> { [""] = "" });

        if (report.ComparisonResult?.BestPerformer?.Id > 0)
        {
            exportData.Add(new Dictionary<string, object> { ["PERFORMANCE COMPARISON ANALYSIS"] = "" });
            exportData.Add(new Dictionary<string, object>
            {
                ["Best Performer"] = report.ComparisonResult.BestPerformer.Name,
                ["Best Score (%)"] = report.ComparisonResult.BestPerformer.PerformanceScore,
                ["Best Completion (%)"] = report.ComparisonResult.BestPerformer.CompletionRate,
                ["Best On-Time (%)"] = report.ComparisonResult.BestPerformer.OnTimeRate
            });
            exportData.Add(new Dictionary<string, object>
            {
                ["Needs Improvement"] = report.ComparisonResult.WorstPerformer.Name,
                ["Worst Score (%)"] = report.ComparisonResult.WorstPerformer.PerformanceScore,
                ["Worst Completion (%)"] = report.ComparisonResult.WorstPerformer.CompletionRate,
                ["Worst On-Time (%)"] = report.ComparisonResult.WorstPerformer.OnTimeRate
            });
            exportData.Add(new Dictionary<string, object>
            {
                ["Score Difference (%)"] = report.ComparisonResult.ScoreDifference,
                ["Completion Gap (%)"] = report.ComparisonResult.CompletionGap,
                ["On-Time Gap (%)"] = report.ComparisonResult.OnTimeGap,
                ["Tasks Difference"] = report.ComparisonResult.TasksGap
            });
            exportData.Add(new Dictionary<string, object> { [""] = "" });
        }

        exportData.Add(new Dictionary<string, object> { ["EMPLOYEE COMPARISON DETAILS"] = "" });
        exportData.Add(new Dictionary<string, object>
        {
            ["Rank"] = "Rank", ["Employee"] = "Employee", ["Employee ID"] = "Employee ID",
            ["Position"] = "Position", ["Department"] = "Department", ["Score (%)"] = "Score (%)",
            ["Completion Rate (%)"] = "Completion Rate (%)", ["On-Time Rate (%)"] = "On-Time Rate (%)",
            ["Total Tasks"] = "Total Tasks", ["Completed"] = "Completed", ["On-Time"] = "On-Time",
            ["Late"] = "Late", ["Pending"] = "Pending", ["Status"] = "Status"
        });

        foreach (var emp in report.Employees.OrderByDescending(e => e.PerformanceScore))
        {
            exportData.Add(new Dictionary<string, object>
            {
                ["Rank"] = emp.Rank, ["Employee"] = emp.Name, ["Employee ID"] = emp.EmployeeId,
                ["Position"] = emp.Position, ["Department"] = emp.Department,
                ["Score (%)"] = emp.PerformanceScore, ["Completion Rate (%)"] = emp.CompletionRate,
                ["On-Time Rate (%)"] = emp.OnTimeRate, ["Total Tasks"] = emp.TotalTasks,
                ["Completed"] = emp.CompletedTasks, ["On-Time"] = emp.OnTimeTasks,
                ["Late"] = emp.LateTasks, ["Pending"] = emp.PendingTasks, ["Status"] = emp.PerformanceLevel
            });
        }

        return exportData;
    }

    private List<Dictionary<string, object>> BuildBranchReportExportData(BranchPerformanceViewModel report, DateTime startDate, DateTime endDate)
    {
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

        if (report.EmployeeScores?.Any() == true)
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

        return exportData;
    }


    private List<Dictionary<string, object>> BuildTaskReportExportData(TaskCompletionViewModel report, DateTime startDate, DateTime endDate)
    {
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

        return exportData;
    }
}
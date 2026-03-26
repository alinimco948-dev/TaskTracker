using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;
using System.Text.Json;

namespace TaskTracker.Controllers;

public class ReportController : Controller
{
    private readonly IReportService _reportService;
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private readonly IDepartmentService _departmentService;
    private readonly ITaskService _taskService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ReportController> _logger;
    private readonly ApplicationDbContext _context;

    public ReportController(
        IReportService reportService,
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ITaskService taskService,
        IAuditService auditService,
        ILogger<ReportController> logger,
        ApplicationDbContext context)
    {
        _reportService = reportService;
        _employeeService = employeeService;
        _branchService = branchService;
        _departmentService = departmentService;
        _taskService = taskService;
        _auditService = auditService;
        _logger = logger;
        _context = context;
    }

    // GET: Report/EmployeePerformance
    public async Task<IActionResult> EmployeePerformance(int? employeeId, DateTime? startDate, DateTime? endDate)
    {
        try
        {
            startDate ??= DateTime.Today.AddMonths(-1);
            endDate ??= DateTime.Today;

            // Get all employees for dropdown
            var allEmployees = await _context.Employees
                .Where(e => e.IsActive)
                .Select(e => new { e.Id, e.Name, e.Position })
                .ToListAsync();

            ViewBag.Employees = allEmployees;
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            if (employeeId.HasValue && employeeId.Value > 0)
            {
                var report = await GenerateEmployeePerformanceReport(employeeId.Value, startDate.Value, endDate.Value);
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

    private async Task<EmployeePerformanceViewModel> GenerateEmployeePerformanceReport(int employeeId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var employee = await _context.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee == null)
            {
                return new EmployeePerformanceViewModel
                {
                    EmployeeId = employeeId,
                    EmployeeName = "Employee Not Found",
                    StartDate = startDate,
                    EndDate = endDate
                };
            }

            // Get ALL branch assignments for this employee (active and past)
            var branchAssignments = await _context.BranchAssignments
                .Include(ba => ba.Branch)
                .Where(ba => ba.EmployeeId == employeeId)
                .OrderBy(ba => ba.StartDate)
                .ToListAsync();

            // Get the branches the employee is assigned to
            var assignedBranchIds = branchAssignments.Select(ba => ba.BranchId).Distinct().ToList();

            // Get daily tasks ONLY for branches the employee is assigned to, within the date range
            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Include(dt => dt.Branch)
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta.Employee)
                .Where(dt => dt.TaskAssignment != null &&
                             dt.TaskAssignment.EmployeeId == employeeId &&
                             assignedBranchIds.Contains(dt.BranchId) &&
                             dt.TaskDate.Date >= startDate.Date &&
                             dt.TaskDate.Date <= endDate.Date)
                .OrderBy(dt => dt.TaskDate)
                .ToListAsync();

            var report = new EmployeePerformanceViewModel
            {
                EmployeeId = employeeId,
                EmployeeName = employee.Name,
                EmployeeIdNumber = employee.EmployeeId ?? employee.Id.ToString(),
                Department = employee.Department?.Name ?? "N/A",
                Position = employee.Position ?? "N/A",
                StartDate = startDate,
                EndDate = endDate,
                TaskBreakdown = new Dictionary<string, TaskPerformanceStats>(),
                DailyBreakdown = new List<DailyPerformance>(),
                BranchBreakdown = new Dictionary<string, BranchStatViewModel>(),
                Insights = new List<string>(),
                Strengths = new List<string>(),
                Weaknesses = new List<string>(),
                Recommendations = new List<string>(),
                DailyTrendDates = new List<string>(),
                DailyCompletionRates = new List<double>(),
                DailyOnTimeRates = new List<double>(),
                TaskTypeLabels = new List<string>(),
                TaskTypeValues = new List<int>(),
                AverageCompletionTimes = new List<double>()
            };

            // Add branch assignments with their timelines
            foreach (var assignment in branchAssignments)
            {
                var branchName = assignment.Branch?.Name ?? "Unknown";
                if (!report.BranchBreakdown.ContainsKey(branchName))
                {
                    var startDateAssignment = assignment.StartDate;
                    var endDateAssignment = assignment.EndDate;
                    var isActive = !assignment.EndDate.HasValue || assignment.EndDate.Value.Date >= DateTime.Today.Date;

                    var assignmentPeriod = isActive
                        ? $"From {startDateAssignment:MMM dd, yyyy} → Present"
                        : $"From {startDateAssignment:MMM dd, yyyy} to {endDateAssignment:MMM dd, yyyy}";

                    report.BranchBreakdown[branchName] = new BranchStatViewModel
                    {
                        BranchName = branchName,
                        TotalTasks = 0,
                        CompletedTasks = 0,
                        StartDate = startDateAssignment,
                        EndDate = endDateAssignment,
                        IsActive = isActive,
                        AssignmentPeriod = assignmentPeriod
                    };
                }
            }

            // Process daily tasks
            foreach (var dt in dailyTasks)
            {
                if (dt.TaskItem == null) continue;

                var taskName = dt.TaskItem.Name;
                var taskType = GetTaskTypeName(dt.TaskItem.ExecutionType);
                var isCompleted = dt.IsCompleted;
                var isOnTime = false;
                var deadline = CalculateDeadline(dt.TaskItem, dt.TaskDate);
                var completionTimeStr = "";
                var score = 0;

                if (isCompleted && dt.CompletedAt.HasValue)
                {
                    if (dt.AdjustmentMinutes > 0)
                    {
                        deadline = deadline.AddMinutes(dt.AdjustmentMinutes.Value);
                    }
                    isOnTime = dt.CompletedAt.Value <= deadline;
                    completionTimeStr = dt.CompletedAt.Value.ToLocalTime().ToString("HH:mm");
                    score = isOnTime ? 100 : 50;
                }

                report.DailyBreakdown.Add(new DailyPerformance
                {
                    Date = dt.TaskDate,
                    BranchName = dt.Branch?.Name ?? "Unknown",
                    TaskName = taskName,
                    TaskType = taskType,
                    Deadline = dt.TaskItem?.Deadline.ToString(@"hh\:mm") ?? "N/A",
                    CompletionTime = completionTimeStr,
                    CompletionDateTime = dt.CompletedAt?.ToLocalTime(),
                    DeadlineDateTime = deadline,
                    Status = isCompleted ? "Completed" : "Pending",
                    IsOnTime = isOnTime,
                    AssignedTo = employee.Name,
                    AdjustmentMinutes = dt.AdjustmentMinutes,
                    AdjustmentReason = dt.AdjustmentReason ?? "",
                    Score = score
                });

                // Update task breakdown
                if (!report.TaskBreakdown.ContainsKey(taskName))
                {
                    report.TaskBreakdown[taskName] = new TaskPerformanceStats();
                }
                report.TaskBreakdown[taskName].Total++;
                if (isCompleted)
                {
                    report.TaskBreakdown[taskName].Completed++;
                    if (isOnTime) report.TaskBreakdown[taskName].OnTime++;
                    else report.TaskBreakdown[taskName].Late++;
                }
                else
                {
                    report.TaskBreakdown[taskName].Pending++;
                }

                // Update branch breakdown
                var branchName = dt.Branch?.Name ?? "Unknown";
                if (report.BranchBreakdown.ContainsKey(branchName))
                {
                    report.BranchBreakdown[branchName].TotalTasks++;
                    if (isCompleted)
                    {
                        report.BranchBreakdown[branchName].CompletedTasks++;
                    }
                }
            }

            // Calculate totals
            report.TotalTasks = report.DailyBreakdown.Count;
            report.CompletedTasks = report.DailyBreakdown.Count(d => d.Status == "Completed");
            report.PendingTasks = report.TotalTasks - report.CompletedTasks;
            report.OnTimeTasks = report.DailyBreakdown.Count(d => d.IsOnTime);
            report.LateTasks = report.CompletedTasks - report.OnTimeTasks;
            report.CompletionRate = report.TotalTasks > 0 ? Math.Round((double)report.CompletedTasks / report.TotalTasks * 100, 1) : 0;
            report.OnTimeRate = report.CompletedTasks > 0 ? Math.Round((double)report.OnTimeTasks / report.CompletedTasks * 100, 1) : 0;
            report.OverallScore = report.CompletedTasks > 0 ? Math.Round((double)report.OnTimeTasks / report.CompletedTasks * 100, 1) : 0;

            // Calculate branch completion rates
            foreach (var branch in report.BranchBreakdown.Values)
            {
                branch.CompletionRate = branch.TotalTasks > 0
                    ? Math.Round((double)branch.CompletedTasks / branch.TotalTasks * 100, 1)
                    : 0;
            }

            // Calculate task breakdown rates
            foreach (var task in report.TaskBreakdown.Values)
            {
                task.CompletionRate = task.Total > 0
                    ? Math.Round((double)task.Completed / task.Total * 100, 1)
                    : 0;
                task.OnTimeRate = task.Completed > 0
                    ? Math.Round((double)task.OnTime / task.Completed * 100, 1)
                    : 0;
            }

            GenerateEmployeeInsights(report);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating employee report for {EmployeeId}", employeeId);
            throw;
        }
    }

    private string GetTaskTypeName(TaskExecutionType? executionType)
    {
        return executionType switch
        {
            TaskExecutionType.RecurringDaily => "Daily",
            TaskExecutionType.RecurringWeekly => "Weekly",
            TaskExecutionType.RecurringMonthly => "Monthly",
            TaskExecutionType.MultiDay => "Multi-Day",
            TaskExecutionType.OneTime => "One-Time",
            _ => "Standard"
        };
    }

    private DateTime CalculateDeadline(TaskItem task, DateTime taskDate)
    {
        var deadline = taskDate.Date;
        if (task.IsSameDay)
            deadline = deadline.Add(task.Deadline);
        else
            deadline = deadline.AddDays(1).Add(task.Deadline);
        return deadline;
    }

    private void GenerateEmployeeInsights(EmployeePerformanceViewModel report)
    {
        if (report.OverallScore >= 90)
        {
            report.Strengths.Add("🏆 Exceptional performer - consistently exceeds expectations");
            report.Recommendations.Add("Consider mentoring other team members");
        }
        else if (report.OverallScore >= 75)
        {
            report.Strengths.Add("📈 Strong performer with good consistency");
            report.Recommendations.Add("Focus on maintaining quality while increasing task volume");
        }
        else if (report.OverallScore >= 60)
        {
            report.Strengths.Add("📊 Meets basic requirements consistently");
            report.Recommendations.Add("Focus on improving punctuality");
        }
        else
        {
            report.Weaknesses.Add("⚠️ Performance below expectations");
            report.Recommendations.Add("Schedule coaching session to identify challenges");
        }

        if (report.CompletionRate >= 90)
        {
            report.Strengths.Add($"✅ Excellent completion rate ({report.CompletionRate}%)");
        }
        else if (report.CompletionRate < 70 && report.CompletionRate > 0)
        {
            report.Weaknesses.Add($"❗ Low completion rate ({report.CompletionRate}%)");
            report.Recommendations.Add("Review workload and prioritize pending tasks");
        }

        if (report.OnTimeRate >= 90)
        {
            report.Strengths.Add($"⏰ Outstanding punctuality ({report.OnTimeRate}%)");
        }
        else if (report.OnTimeRate < 70 && report.OnTimeRate > 0)
        {
            report.Weaknesses.Add($"⏰ Frequent late submissions ({report.OnTimeRate}% on time)");
            report.Recommendations.Add("Improve time management and deadline tracking");
        }
    }

    // Other report methods (BranchPerformance, DepartmentPerformance, etc.) remain the same...
    // GET: Report/BranchPerformance
    public async Task<IActionResult> BranchPerformance(int? branchId, DateTime? startDate, DateTime? endDate)
    {
        try
        {
            startDate ??= DateTime.Today.AddMonths(-1);
            endDate ??= DateTime.Today;

            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .Select(b => new { b.Id, b.Name })
                .ToListAsync();

            ViewBag.Branches = branches;
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            if (branchId.HasValue && branchId.Value > 0)
            {
                var report = await _reportService.ExecuteBranchReportAsync(branchId.Value, startDate.Value, endDate.Value);
                return View(report);
            }

            return View(new BranchPerformanceViewModel
            {
                StartDate = startDate.Value,
                EndDate = endDate.Value
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating branch performance report");
            TempData["ErrorMessage"] = "Error generating report. Please try again.";
            return View(new BranchPerformanceViewModel
            {
                StartDate = startDate ?? DateTime.Today.AddMonths(-1),
                EndDate = endDate ?? DateTime.Today
            });
        }
    }

    // GET: Report/DepartmentPerformance
    public async Task<IActionResult> DepartmentPerformance(int? departmentId, DateTime? startDate, DateTime? endDate)
    {
        try
        {
            startDate ??= DateTime.Today.AddMonths(-1);
            endDate ??= DateTime.Today;

            var departments = await _context.Departments
                .Where(d => d.IsActive)
                .Select(d => new { d.Id, d.Name })
                .ToListAsync();

            ViewBag.Departments = departments;
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            if (departmentId.HasValue && departmentId.Value > 0)
            {
                var report = await _reportService.ExecuteDepartmentReportAsync(departmentId.Value, startDate.Value, endDate.Value);
                return View(report);
            }

            return View(new DepartmentPerformanceViewModel
            {
                StartDate = startDate.Value,
                EndDate = endDate.Value
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating department performance report");
            TempData["ErrorMessage"] = "Error generating report. Please try again.";
            return View(new DepartmentPerformanceViewModel
            {
                StartDate = startDate ?? DateTime.Today.AddMonths(-1),
                EndDate = endDate ?? DateTime.Today
            });
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

    // GET: Report/AuditLog
    public async Task<IActionResult> AuditLog(DateTime? startDate, DateTime? endDate, string? action = null, string? entityType = null)
    {
        try
        {
            startDate ??= DateTime.Today.AddMonths(-1);
            endDate ??= DateTime.Today;

            ViewBag.Actions = await _auditService.GetDistinctActionsAsync();
            ViewBag.EntityTypes = await _auditService.GetDistinctEntityTypesAsync();
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");
            ViewBag.SelectedAction = action;
            ViewBag.SelectedEntityType = entityType;

            var report = await _reportService.ExecuteAuditReportAsync(startDate.Value, endDate.Value, action, entityType);
            return View(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating audit log report");
            TempData["ErrorMessage"] = "Error generating report. Please try again.";
            return View(new AuditLogReportViewModel
            {
                StartDate = startDate ?? DateTime.Today.AddMonths(-1),
                EndDate = endDate ?? DateTime.Today
            });
        }
    }

    // GET: Report/Custom
    public async Task<IActionResult> Custom()
    {
        try
        {
            ViewBag.Employees = await _employeeService.GetAllEmployeesAsync();
            ViewBag.Branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Tasks = await _taskService.GetAllTasksAsync();
            ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();

            return View(new CustomReportRequest
            {
                StartDate = DateTime.Today.AddMonths(-1),
                EndDate = DateTime.Today
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading custom report builder");
            TempData["ErrorMessage"] = "Error loading report builder. Please try again.";
            return View(new CustomReportRequest
            {
                StartDate = DateTime.Today.AddMonths(-1),
                EndDate = DateTime.Today
            });
        }
    }

    // POST: Report/GenerateCustom
    [HttpPost]
    public async Task<IActionResult> GenerateCustom([FromBody] CustomReportRequest request)
    {
        try
        {
            var data = await _reportService.ExecuteCustomReportAsync(request);
            return Json(new { success = true, data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating custom report");
            return Json(new { success = false, message = "Error generating report" });
        }
    }

    // POST: Report/SaveReport
    [HttpPost]
    public async Task<IActionResult> SaveReport([FromBody] SaveReportRequest request)
    {
        try
        {
            var report = new Report
            {
                Name = request.Name,
                Description = request.Description,
                ReportType = request.ReportType,
                Configuration = JsonSerializer.Serialize(request.Configuration),
                Columns = JsonSerializer.Serialize(request.Columns),
                Filters = JsonSerializer.Serialize(request.Filters),
                SortBy = request.SortBy,
                IsAscending = request.IsAscending,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                CreatedBy = User.Identity?.Name ?? "System",
                IsActive = true,
                IsPublic = false
            };

            var savedReport = await _reportService.CreateReportAsync(report);
            return Json(new { success = true, reportId = savedReport.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving report");
            return Json(new { success = false, message = "Error saving report" });
        }
    }

    // GET: Report/Export
    public async Task<IActionResult> Export(int reportId, string format = "excel")
    {
        try
        {
            var report = await _reportService.GetReportByIdAsync(reportId);
            if (report == null)
                return NotFound();

            var data = await _reportService.ExecuteReportAsync(reportId);

            byte[] fileData;
            string fileName;
            string contentType;

            switch (format.ToLower())
            {
                case "csv":
                    fileData = await _reportService.ExportToCsvAsync(data, report.Name);
                    fileName = $"{report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                    contentType = "text/csv";
                    break;
                case "pdf":
                    fileData = await _reportService.ExportToPdfAsync(data, report.Name);
                    fileName = $"{report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                    contentType = "application/pdf";
                    break;
                default:
                    fileData = await _reportService.ExportToExcelAsync(data, report.Name);
                    fileName = $"{report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    break;
            }

            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting report {ReportId}", reportId);
            TempData["ErrorMessage"] = "Error exporting report. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    // POST: Report/Delete
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _reportService.DeleteReportAsync(id);
            return Json(new { success = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting report {ReportId}", id);
            return Json(new { success = false, message = "Error deleting report" });
        }
    }

    // POST: Report/Schedule
    [HttpPost]
    public async Task<IActionResult> Schedule(int reportId, string cronExpression, List<string> recipients)
    {
        try
        {
            var result = await _reportService.ScheduleReportAsync(reportId, cronExpression, recipients);
            return Json(new { success = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling report {ReportId}", reportId);
            return Json(new { success = false, message = "Error scheduling report" });
        }
    }

    // POST: Report/Unschedule
    [HttpPost]
    public async Task<IActionResult> Unschedule(int reportId)
    {
        try
        {
            var result = await _reportService.UnscheduleReportAsync(reportId);
            return Json(new { success = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unscheduling report {ReportId}", reportId);
            return Json(new { success = false, message = "Error unscheduling report" });
        }
    }

    private string GetScheduleFrequency(string? cronExpression)
    {
        return cronExpression switch
        {
            "0 9 * * *" => "Daily",
            "0 9 * * 1" => "Weekly",
            "0 9 1 * *" => "Monthly",
            _ => "Custom"
        };
    }
}
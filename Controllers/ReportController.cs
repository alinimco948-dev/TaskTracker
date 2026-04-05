using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    private readonly ITaskCalculationService _taskCalculationService;
    private readonly ILogger<ReportController> _logger;
    private readonly ApplicationDbContext _context;

    public ReportController(
        IReportService reportService,
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ITaskService taskService,
        IAuditService auditService,
        ITaskCalculationService taskCalculationService,
        ILogger<ReportController> logger,
        ApplicationDbContext context)
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
    }

    #region Original Report Actions

    // GET: Report/EmployeePerformance
    public async Task<IActionResult> EmployeePerformance(int? employeeId, DateTime? startDate, DateTime? endDate)
    {
        try
        {
            startDate ??= DateTime.Today.AddMonths(-1);
            endDate ??= DateTime.Today;

            var allEmployees = await _context.Employees
                .Where(e => e.IsActive)
                .Select(e => new { e.Id, e.Name, e.Position })
                .ToListAsync();

            ViewBag.Employees = allEmployees;
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");

            if (employeeId.HasValue && employeeId.Value > 0)
            {
                var report = await _reportService.ExecuteEmployeeReportAsync(employeeId.Value, startDate.Value, endDate.Value);
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

    // GET: Report/EmployeeRanking
    public async Task<IActionResult> EmployeeRanking(DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            startDate ??= DateTime.UtcNow.AddMonths(-1);
            endDate ??= DateTime.UtcNow;

            var rankings = await _reportService.GetEmployeeRankingAsync(startDate, endDate);

            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");
            ViewBag.TotalEmployees = rankings.Count;
            ViewBag.AverageScore = rankings.Any() ? Math.Round(rankings.Average(r => r.PerformanceScore), 0) : 0;
            ViewBag.TopScore = rankings.Any() ? rankings.Max(r => r.PerformanceScore) : 0;

            return View(rankings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating employee ranking");
            TempData["ErrorMessage"] = "Error generating ranking report. Please try again.";
            return View(new List<EmployeeRankingViewModel>());
        }
    }

    // GET: Report/ExportRanking
    public async Task<IActionResult> ExportRanking(DateTime? startDate = null, DateTime? endDate = null, string format = "excel")
    {
        try
        {
            startDate ??= DateTime.UtcNow.AddMonths(-1);
            endDate ??= DateTime.UtcNow;

            var rankings = await _reportService.GetEmployeeRankingAsync(startDate, endDate);

            var exportData = rankings.Select(r => new Dictionary<string, object>
            {
                ["Rank"] = r.Rank,
                ["Employee Name"] = r.Name,
                ["Employee ID"] = r.EmployeeId,
                ["Position"] = r.Position,
                ["Department"] = r.Department,
                ["Performance Score (%)"] = r.PerformanceScore,
                ["Completion Rate (%)"] = r.CompletionRate,
                ["On-Time Rate (%)"] = r.OnTimeRate,
                ["Total Tasks"] = r.TotalTasks,
                ["Completed Tasks"] = r.CompletedTasks,
                ["On-Time Tasks"] = r.OnTimeTasks,
                ["Late Tasks"] = r.LateTasks,
                ["Status"] = r.IsActive ? "Active" : "Inactive"
            }).ToList();

            byte[] fileData;
            string fileName;
            string contentType;

            switch (format.ToLower())
            {
                case "csv":
                    fileData = await _reportService.ExportToCsvAsync(exportData, "EmployeeRanking");
                    fileName = $"EmployeeRanking_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                    contentType = "text/csv";
                    break;
                case "pdf":
                    fileData = await _reportService.ExportToPdfAsync(exportData, "EmployeeRanking");
                    fileName = $"EmployeeRanking_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                    contentType = "application/pdf";
                    break;
                default:
                    fileData = await _reportService.ExportToExcelAsync(exportData, "EmployeeRanking");
                    fileName = $"EmployeeRanking_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    break;
            }

            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting employee ranking");
            TempData["ErrorMessage"] = "Error exporting report. Please try again.";
            return RedirectToAction(nameof(EmployeeRanking));
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
            return RedirectToAction(nameof(Custom));
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

    // GET: Report/VerifyTaskConsistency
    [HttpGet]
    public async Task<IActionResult> VerifyTaskConsistency(int employeeId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var stats = await _taskCalculationService.GetEmployeeTaskStatisticsAsync(employeeId, startDate, endDate);

            var employee = await _context.Employees
                .Include(e => e.BranchAssignments)
                .FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee == null)
                return NotFound();

            var assignedBranchIds = employee.BranchAssignments
                .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
                .Select(ba => ba.BranchId)
                .ToList();

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Where(dt => assignedBranchIds.Contains(dt.BranchId) &&
                             dt.TaskDate.Date >= startDate.Date &&
                             dt.TaskDate.Date <= endDate.Date)
                .ToListAsync();

            var taskDetails = new List<object>();
            foreach (var dt in dailyTasks)
            {
                var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dt);

                taskDetails.Add(new
                {
                    dt.Id,
                    dt.TaskDate,
                    dt.TaskItem?.Name,
                    dt.IsCompleted,
                    dt.CompletedAt,
                    Deadline = delayInfo.Deadline,
                    IsOnTime = delayInfo.IsOnTime,
                    DelayText = delayInfo.DelayText,
                    AdjustmentMinutes = dt.AdjustmentMinutes,
                    dt.AdjustmentReason
                });
            }

            return Json(new
            {
                employeeName = employee.Name,
                startDate,
                endDate,
                statistics = stats,
                taskDetails = taskDetails
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
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
            string fileName;
            string contentType;
            
            switch (format.ToLower())
            {
                case "csv":
                    fileData = await _reportService.ExportToCsvAsync(exportData, report.TaskName);
                    fileName = $"{report.TaskName}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                    contentType = "text/csv";
                    break;
                case "pdf":
                    fileData = await _reportService.ExportToPdfAsync(exportData, report.TaskName);
                    fileName = $"{report.TaskName}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                    contentType = "application/pdf";
                    break;
                default:
                    fileData = await _reportService.ExportToExcelAsync(exportData, report.TaskName);
                    fileName = $"{report.TaskName}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
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

    #endregion

    #region Unified Task Completion (Additional Feature)

    // GET: Report/UnifiedTaskCompletion
    public async Task<IActionResult> UnifiedTaskCompletion(int? taskId, DateTime? startDate, DateTime? endDate)
    {
        try
        {
            startDate ??= DateTime.Today.AddMonths(-1);
            endDate ??= DateTime.Today;

            var tasks = await _taskService.GetAllTasksAsync();
            
            var viewModel = new UnifiedTaskCompletionViewModel
            {
                TaskId = taskId ?? 0,
                StartDate = startDate.Value,
                EndDate = endDate.Value,
                HasData = false,
                FilterViewModel = new ReportFilterViewModel
                {
                    EntitySelectLabel = "Select Task",
                    EntityIdName = "taskId",
                    EntitySelectPlaceholder = "Choose a task...",
                    EntitySelectItems = tasks.Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name }).ToList(),
                    SelectedEntityId = taskId,
                    StartDate = startDate,
                    EndDate = endDate,
                    ButtonText = "Generate",
                    ShowExport = taskId.HasValue && taskId.Value > 0
                }
            };

            if (taskId.HasValue && taskId.Value > 0)
            {
                await LoadTaskCompletionData(viewModel, taskId.Value, startDate.Value, endDate.Value);
            }

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating unified task completion report");
            TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
            return View(new UnifiedTaskCompletionViewModel());
        }
    }

    // GET: Report/ExportUnifiedTaskReport
    public async Task<IActionResult> ExportUnifiedTaskReport(int taskId, DateTime startDate, DateTime endDate, string format = "excel")
    {
        try
        {
            var viewModel = new UnifiedTaskCompletionViewModel();
            await LoadTaskCompletionData(viewModel, taskId, startDate, endDate);

            var exportData = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["Task Name"] = viewModel.TaskName,
                    ["Period"] = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
                    ["Total Assignments"] = viewModel.TotalAssignments,
                    ["Completed"] = viewModel.Completed,
                    ["Completion Rate (%)"] = viewModel.CompletionRate,
                    ["On Time"] = viewModel.OnTime,
                    ["On Time Rate (%)"] = viewModel.OnTimeRate,
                    ["Late"] = viewModel.Late,
                    ["Pending"] = viewModel.Pending,
                    ["Average Completion Time"] = viewModel.AverageCompletionTime?.ToString(@"hh\:mm") ?? "N/A"
                }
            };

            byte[] fileData;
            string fileName = $"TaskReport_{viewModel.TaskName}_{DateTime.Now:yyyyMMdd_HHmmss}";
            string contentType;

            switch (format.ToLower())
            {
                case "csv":
                    fileData = await _reportService.ExportToCsvAsync(exportData, fileName);
                    fileName += ".csv";
                    contentType = "text/csv";
                    break;
                default:
                    fileData = await _reportService.ExportToExcelAsync(exportData, fileName);
                    fileName += ".xlsx";
                    contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    break;
            }

            return File(fileData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting task report");
            TempData["ErrorMessage"] = "Error exporting report";
            return RedirectToAction(nameof(UnifiedTaskCompletion), new { taskId, startDate, endDate });
        }
    }

    #endregion

    #region Helper Methods for Unified Task Completion

    private async Task LoadTaskCompletionData(UnifiedTaskCompletionViewModel vm, int taskId, DateTime startDate, DateTime endDate)
    {
        var task = await _taskService.GetTaskByIdAsync(taskId);
        if (task == null) return;

        var report = await _reportService.ExecuteTaskReportAsync(taskId, startDate, endDate);

        vm.TaskId = taskId;
        vm.TaskName = task.Name;
        vm.Deadline = task.Deadline;
        vm.IsSameDay = task.IsSameDay;
        vm.HasData = report.TotalAssignments > 0;
        vm.TotalAssignments = report.TotalAssignments;
        vm.Completed = report.Completed;
        vm.Pending = report.Pending;
        vm.OnTime = report.OnTime;
        vm.Late = report.Late;
        vm.CompletionRate = report.CompletionRate;
        vm.OnTimeRate = report.OnTimeRate;
        vm.AverageCompletionTime = report.AverageCompletionTime;
        vm.FastestCompletion = report.FastestCompletion;
        vm.SlowestCompletion = report.SlowestCompletion;
        vm.BranchStats = report.BranchStats;
        vm.EmployeeStats = report.EmployeeStats;
        vm.DailyCompletions = report.DailyCompletions;

        // Build Daily Breakdown from the report data
        vm.DailyBreakdown = new List<DailyTaskBreakdownItem>();
        
        // Get daily tasks for detailed breakdown
        var dailyTasks = await _context.DailyTasks
            .Include(dt => dt.Branch)
            .Include(dt => dt.TaskAssignment)
                .ThenInclude(ta => ta.Employee)
            .Where(dt => dt.TaskItemId == taskId && 
                         dt.TaskDate.Date >= startDate.Date && 
                         dt.TaskDate.Date <= endDate.Date)
            .OrderByDescending(dt => dt.TaskDate)
            .Take(50)
            .ToListAsync();

        foreach (var dt in dailyTasks)
        {
            var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dt);
            
            vm.DailyBreakdown.Add(new DailyTaskBreakdownItem
            {
                Date = dt.TaskDate,
                BranchName = dt.Branch?.Name ?? "Unknown",
                AssignedTo = dt.TaskAssignment?.Employee?.Name ?? "Unassigned",
                Status = dt.IsCompleted ? "Completed" : "Pending",
                IsOnTime = delayInfo.IsOnTime,
                CompletionTime = dt.CompletedAt?.ToLocalTime().ToString("HH:mm") ?? ""
            });
        }

        // Build KPI Cards
        vm.KpiCards = new List<StatsCardViewModel>
        {
            new() { Label = "Total Assignments", Value = vm.TotalAssignments.ToString(), Icon = "fa-calendar-alt", IconBgColor = "bg-blue-100", IconColor = "text-blue-600" },
            new() { Label = "Completed", Value = vm.Completed.ToString(), Subtext = $"{vm.CompletionRate}% completion", ValueColor = "text-green-600", Icon = "fa-check-circle", IconBgColor = "bg-green-100", IconColor = "text-green-600", ShowProgressBar = true, ProgressValue = vm.CompletionRate, ProgressBarColor = "bg-green-500" },
            new() { Label = "On Time", Value = vm.OnTime.ToString(), Subtext = $"{vm.OnTimeRate}% of completed", ValueColor = "text-blue-600", Icon = "fa-clock", IconBgColor = "bg-blue-100", IconColor = "text-blue-600", ShowProgressBar = true, ProgressValue = vm.OnTimeRate, ProgressBarColor = "bg-blue-500" },
            new() { Label = "Pending", Value = vm.Pending.ToString(), ValueColor = "text-yellow-600", Icon = "fa-hourglass-half", IconBgColor = "bg-yellow-100", IconColor = "text-yellow-600" }
        };

        // Generate insights
        vm.Insights = new List<string>();
        vm.Recommendations = new List<string>();

        if (vm.CompletionRate >= 90)
            vm.Insights.Add($"✅ Excellent completion rate! {vm.CompletionRate}% of assignments completed.");
        else if (vm.CompletionRate >= 70)
            vm.Insights.Add($"📈 Good completion rate of {vm.CompletionRate}%. Room for improvement.");
        else if (vm.CompletionRate > 0)
            vm.Insights.Add($"⚠️ Low completion rate of {vm.CompletionRate}%. This task needs attention.");

        if (vm.OnTimeRate >= 90)
            vm.Insights.Add($"⏰ Excellent punctuality! {vm.OnTimeRate}% completed on time.");
        else if (vm.OnTimeRate >= 70)
            vm.Insights.Add($"📊 Good punctuality at {vm.OnTimeRate}%.");
        else if (vm.OnTimeRate > 0)
            vm.Insights.Add($"⌛ Punctuality needs improvement: {vm.OnTimeRate}% on time.");

        if (vm.BranchStats != null && vm.BranchStats.Any())
        {
            var topBranch = vm.BranchStats.OrderByDescending(b => b.Value.CompletionRate).First();
            vm.Insights.Add($"🏆 Top performing branch: {topBranch.Key} ({topBranch.Value.CompletionRate}%)");
        }

        if (vm.EmployeeStats != null && vm.EmployeeStats.Any())
        {
            var topEmployee = vm.EmployeeStats.OrderByDescending(e => e.Value.CompletionRate).First();
            vm.Insights.Add($"⭐ Top performer: {topEmployee.Key} ({topEmployee.Value.CompletionRate}%)");
        }

        // Recommendations
        if (vm.CompletionRate < 70 && vm.CompletionRate > 0)
        {
            vm.Recommendations.Add("• Review why this task isn't being completed consistently");
            vm.Recommendations.Add("• Consider adjusting deadline or providing additional training");
        }

        if (vm.OnTimeRate < 70 && vm.Completed > 0)
        {
            vm.Recommendations.Add("• Start this task earlier in the day to meet deadlines");
            vm.Recommendations.Add("• Set reminders 30 minutes before deadline");
        }

        if (vm.Pending > vm.Completed && vm.Pending > 0)
        {
            vm.Recommendations.Add("• Prioritize this task over others when assigned");
        }

        vm.FooterText = $"Data reflects task \"{task.Name}\" from {startDate:MMM dd, yyyy} to {endDate:MMM dd, yyyy}";
        
        vm.FilterViewModel.ShowExport = vm.HasData;
        vm.FilterViewModel.ExportDataAvailable = vm.HasData;
    }

    #endregion

    #region Scheduling Actions

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

    #endregion
}
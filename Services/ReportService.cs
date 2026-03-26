using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;
using System.Text.Json;

namespace TaskTracker.Services;

public class ReportService : IReportService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ReportService> _logger;
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private readonly IDepartmentService _departmentService;
    private readonly ITaskService _taskService;
    private readonly IAuditService _auditService;
    private readonly IHolidayService _holidayService;

    public ReportService(
        ApplicationDbContext context,
        ILogger<ReportService> logger,
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ITaskService taskService,
        IAuditService auditService,
        IHolidayService holidayService)
    {
        _context = context;
        _logger = logger;
        _employeeService = employeeService;
        _branchService = branchService;
        _departmentService = departmentService;
        _taskService = taskService;
        _auditService = auditService;
        _holidayService = holidayService;
        ExcelPackage.License.SetNonCommercialPersonal($"TaskTracker-{Environment.UserName}");
    }

    #region Helper Methods

    private DateTime CalculateDeadline(TaskItem task, DateTime taskDate)
    {
        var deadline = taskDate.Date;
        if (task.IsSameDay)
            deadline = deadline.Add(task.Deadline);
        else
            deadline = deadline.AddDays(1).Add(task.Deadline);
        return deadline;
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

    private bool IsTaskOnTime(DailyTask dailyTask)
    {
        if (!dailyTask.IsCompleted || !dailyTask.CompletedAt.HasValue || dailyTask.TaskItem == null)
            return false;

        var deadline = CalculateDeadline(dailyTask.TaskItem, dailyTask.TaskDate);
        if (dailyTask.AdjustmentMinutes > 0)
        {
            deadline = deadline.AddMinutes(dailyTask.AdjustmentMinutes.Value);
        }

        return dailyTask.CompletedAt.Value <= deadline.AddSeconds(30);
    }

    private string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpper();
        return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
    }

    #endregion

    #region Report Management

    public async Task<Report> CreateReportAsync(Report report)
    {
        try
        {
            report.CreatedAt = DateTime.UtcNow;
            report.RunCount = 0;
            _context.Reports.Add(report);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Create",
                "Report",
                report.Id,
                $"Created report: {report.Name}"
            );

            _logger.LogInformation("Report created: {ReportName}", report.Name);
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating report");
            throw;
        }
    }

    public async Task<Report?> UpdateReportAsync(Report report)
    {
        try
        {
            var existing = await _context.Reports.FindAsync(report.Id);
            if (existing == null) return null;

            existing.Name = report.Name;
            existing.Description = report.Description;
            existing.Category = report.Category;
            existing.Configuration = report.Configuration;
            existing.Columns = report.Columns;
            existing.Filters = report.Filters;
            existing.SortBy = report.SortBy;
            existing.IsAscending = report.IsAscending;
            existing.StartDate = report.StartDate;
            existing.EndDate = report.EndDate;
            existing.IsScheduled = report.IsScheduled;
            existing.ScheduleCron = report.ScheduleCron;
            existing.ExportFormat = report.ExportFormat;
            existing.IncludeCharts = report.IncludeCharts;
            existing.IsPublic = report.IsPublic;
            existing.Tags = report.Tags;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Update",
                "Report",
                report.Id,
                $"Updated report: {report.Name}"
            );

            _logger.LogInformation("Report updated: {ReportName}", report.Name);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating report {ReportId}", report.Id);
            throw;
        }
    }

    public async Task<bool> DeleteReportAsync(int id)
    {
        try
        {
            var report = await _context.Reports.FindAsync(id);
            if (report == null) return false;

            _context.Reports.Remove(report);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Delete",
                "Report",
                report.Id,
                $"Deleted report: {report.Name}"
            );

            _logger.LogInformation("Report deleted: {ReportName}", report.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting report {ReportId}", id);
            return false;
        }
    }

    public async Task<Report?> GetReportByIdAsync(int id)
    {
        return await _context.Reports.FindAsync(id);
    }

    public async Task<List<Report>> GetUserReportsAsync(string userId)
    {
        return await _context.Reports
            .Where(r => r.CreatedBy == userId || r.IsPublic)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Report>> GetPublicReportsAsync()
    {
        return await _context.Reports
            .Where(r => r.IsPublic && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Report>> GetReportsByTypeAsync(string reportType)
    {
        return await _context.Reports
            .Where(r => r.ReportType == reportType && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Report>> GetReportsByCategoryAsync(string category)
    {
        return await _context.Reports
            .Where(r => r.Category == category && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Report>> GetScheduledReportsAsync()
    {
        return await _context.Reports
            .Where(r => r.IsScheduled && r.IsActive)
            .ToListAsync();
    }

    public async Task<List<Report>> GetTemplatesAsync()
    {
        return await _context.Reports
            .Where(r => r.IsPublic && r.IsActive)
            .OrderBy(r => r.Category)
            .ThenBy(r => r.Name)
            .ToListAsync();
    }

    #endregion

    #region Report Execution

    public async Task<object> ExecuteReportAsync(int reportId, Dictionary<string, object>? parameters = null)
    {
        try
        {
            var report = await _context.Reports.FindAsync(reportId);
            if (report == null)
                throw new Exception($"Report {reportId} not found");

            report.LastRunAt = DateTime.UtcNow;
            report.RunCount = (report.RunCount ?? 0) + 1;

            object result;
            var startDate = parameters != null && parameters.ContainsKey("startDate")
                ? DateTime.Parse(parameters["startDate"]?.ToString() ?? DateTime.Today.AddMonths(-1).ToString())
                : report.StartDate ?? DateTime.Today.AddMonths(-1);

            var endDate = parameters != null && parameters.ContainsKey("endDate")
                ? DateTime.Parse(parameters["endDate"]?.ToString() ?? DateTime.Today.ToString())
                : report.EndDate ?? DateTime.Today;

            switch (report.ReportType?.ToLower())
            {
                case "employee":
                    var employeeId = parameters != null && parameters.ContainsKey("employeeId")
                        ? Convert.ToInt32(parameters["employeeId"])
                        : 0;
                    result = await ExecuteEmployeeReportAsync(employeeId, startDate, endDate);
                    break;

                case "branch":
                    var branchId = parameters != null && parameters.ContainsKey("branchId")
                        ? Convert.ToInt32(parameters["branchId"])
                        : 0;
                    result = await ExecuteBranchReportAsync(branchId, startDate, endDate);
                    break;

                case "department":
                    var departmentId = parameters != null && parameters.ContainsKey("departmentId")
                        ? Convert.ToInt32(parameters["departmentId"])
                        : 0;
                    result = await ExecuteDepartmentReportAsync(departmentId, startDate, endDate);
                    break;

                case "task":
                    var taskId = parameters != null && parameters.ContainsKey("taskId")
                        ? Convert.ToInt32(parameters["taskId"])
                        : 0;
                    result = await ExecuteTaskReportAsync(taskId, startDate, endDate);
                    break;

                case "audit":
                    var action = parameters?.ContainsKey("action") == true
                        ? parameters["action"]?.ToString()
                        : null;
                    var entityType = parameters?.ContainsKey("entityType") == true
                        ? parameters["entityType"]?.ToString()
                        : null;
                    result = await ExecuteAuditReportAsync(startDate, endDate, action, entityType);
                    break;

                default:
                    var customRequest = new CustomReportRequest
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        BranchIds = parameters != null && parameters.ContainsKey("branchIds")
                            ? JsonSerializer.Deserialize<List<int>>(parameters["branchIds"]?.ToString() ?? "[]") ?? new List<int>()
                            : new List<int>(),
                        TaskIds = parameters != null && parameters.ContainsKey("taskIds")
                            ? JsonSerializer.Deserialize<List<int>>(parameters["taskIds"]?.ToString() ?? "[]") ?? new List<int>()
                            : new List<int>(),
                        EmployeeIds = parameters != null && parameters.ContainsKey("employeeIds")
                            ? JsonSerializer.Deserialize<List<int>>(parameters["employeeIds"]?.ToString() ?? "[]") ?? new List<int>()
                            : new List<int>(),
                        DepartmentIds = parameters != null && parameters.ContainsKey("departmentIds")
                            ? JsonSerializer.Deserialize<List<int>>(parameters["departmentIds"]?.ToString() ?? "[]") ?? new List<int>()
                            : new List<int>(),
                        Status = parameters?.ContainsKey("status") == true
                            ? parameters["status"]?.ToString() ?? "All"
                            : "All"
                    };
                    result = await ExecuteCustomReportAsync(customRequest);
                    break;
            }

            await _context.SaveChangesAsync();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing report {ReportId}", reportId);
            throw;
        }
    }

    public async Task<EmployeePerformanceViewModel> ExecuteEmployeeReportAsync(int employeeId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var employee = await _context.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee == null)
                return new EmployeePerformanceViewModel
                {
                    EmployeeId = employeeId,
                    EmployeeName = "Unknown",
                    StartDate = startDate,
                    EndDate = endDate
                };

            // Get all DailyTasks assigned to this employee via TaskAssignment
            var assignedTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Include(dt => dt.Branch)
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta.Employee)
                .Where(dt => dt.TaskAssignment != null &&
                             dt.TaskAssignment.EmployeeId == employeeId &&
                             dt.TaskDate.Date >= startDate.Date &&
                             dt.TaskDate.Date <= endDate.Date)
                .OrderBy(dt => dt.TaskDate)
                .ToListAsync();

            // Get branch assignments for this employee
            var branchAssignments = await _context.BranchAssignments
                .Include(ba => ba.Branch)
                .Where(ba => ba.EmployeeId == employeeId &&
                             ba.StartDate.Date <= endDate.Date &&
                             (ba.EndDate == null || ba.EndDate.Value.Date >= startDate.Date))
                .ToListAsync();

            var assignedBranchIds = branchAssignments.Select(ba => ba.BranchId).Distinct().ToList();

            // Get unassigned daily tasks (no TaskAssignment) for branches this employee is assigned to
            var unassignedTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Include(dt => dt.Branch)
                .Where(dt => dt.TaskAssignment == null &&
                             assignedBranchIds.Contains(dt.BranchId) &&
                             dt.TaskDate.Date >= startDate.Date &&
                             dt.TaskDate.Date <= endDate.Date)
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

            // Initialize task breakdown with all active tasks
            var allTasks = await _context.TaskItems.Where(t => t.IsActive).ToListAsync();
            foreach (var task in allTasks)
            {
                report.TaskBreakdown[task.Name] = new TaskPerformanceStats();
            }

            // Process assigned tasks
            foreach (var dt in assignedTasks)
            {
                ProcessDailyTaskForReport(dt, report, true);
            }

            // Process unassigned tasks
            foreach (var dt in unassignedTasks)
            {
                ProcessDailyTaskForReport(dt, report, false);
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

            // Add branch assignments to breakdown
            foreach (var assignment in branchAssignments)
            {
                var branchName = assignment.Branch?.Name ?? "Unknown";
                if (!report.BranchBreakdown.ContainsKey(branchName))
                {
                    report.BranchBreakdown[branchName] = new BranchStatViewModel
                    {
                        BranchName = branchName,
                        TotalTasks = 0,
                        CompletedTasks = 0,
                        StartDate = assignment.StartDate,
                        EndDate = assignment.EndDate,
                        IsActive = !assignment.EndDate.HasValue || assignment.EndDate.Value.Date >= DateTime.Today.Date,
                        AssignmentPeriod = assignment.EndDate.HasValue
                            ? $"From {assignment.StartDate:MMM dd, yyyy} to {assignment.EndDate:MMM dd, yyyy}"
                            : $"From {assignment.StartDate:MMM dd, yyyy} → Present"
                    };
                }
            }

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

            // Build trend data
            BuildEmployeeTrendData(report);

            // Generate insights
            GenerateEmployeeInsights(report);

            // Build task type distribution
            var taskTypeGroups = report.DailyBreakdown
                .Where(d => d.Status == "Completed")
                .GroupBy(d => d.TaskType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            report.TaskTypeLabels = taskTypeGroups.Select(g => g.Type).ToList();
            report.TaskTypeValues = taskTypeGroups.Select(g => g.Count).ToList();

            // Build average completion times
            var avgTimes = report.DailyBreakdown
                .Where(d => d.Status == "Completed" && d.CompletionDateTime.HasValue)
                .GroupBy(d => d.TaskType)
                .Select(g => new
                {
                    Type = g.Key,
                    AvgMinutes = g.Average(d =>
                    {
                        var diff = d.CompletionDateTime.Value - d.DeadlineDateTime;
                        return Math.Abs(diff.TotalMinutes);
                    })
                })
                .ToList();

            report.AverageCompletionTimes = avgTimes.Select(a => Math.Round(a.AvgMinutes, 1)).ToList();

            // Prepare chart data
            report.PerformanceChart = GeneratePerformanceChart(report);
            report.TaskDistributionChart = GenerateTaskDistributionChart(report);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing employee report for {EmployeeId}", employeeId);
            throw;
        }
    }

    private void ProcessDailyTaskForReport(DailyTask dt, EmployeePerformanceViewModel report, bool isAssigned)
    {
        if (dt.TaskItem == null) return;

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
            AssignedTo = dt.TaskAssignment?.Employee?.Name ?? (isAssigned ? "Assigned" : "Unassigned"),
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

    private void BuildEmployeeTrendData(EmployeePerformanceViewModel report)
    {
        var dailyStats = new Dictionary<DateTime, (int Total, int Completed, int OnTime)>();
        foreach (var item in report.DailyBreakdown)
        {
            var dateKey = item.Date.Date;
            if (!dailyStats.ContainsKey(dateKey))
                dailyStats[dateKey] = (0, 0, 0);
            var stats = dailyStats[dateKey];
            stats.Total++;
            if (item.Status == "Completed") stats.Completed++;
            if (item.IsOnTime) stats.OnTime++;
            dailyStats[dateKey] = stats;
        }

        var dateRange = (report.EndDate - report.StartDate).Days;
        var daysToShow = Math.Min(dateRange, 30);
        var dateLabels = new List<string>();
        var completionRates = new List<double>();
        var onTimeRates = new List<double>();

        for (int i = daysToShow; i >= 0; i--)
        {
            var date = report.EndDate.Date.AddDays(-i);
            dateLabels.Add(date.ToString("MMM dd"));

            if (dailyStats.ContainsKey(date))
            {
                var stats = dailyStats[date];
                completionRates.Add(stats.Total > 0 ? Math.Round((double)stats.Completed / stats.Total * 100, 1) : 0);
                onTimeRates.Add(stats.Completed > 0 ? Math.Round((double)stats.OnTime / stats.Completed * 100, 1) : 0);
            }
            else
            {
                completionRates.Add(0);
                onTimeRates.Add(0);
            }
        }

        report.DailyTrendDates = dateLabels;
        report.DailyCompletionRates = completionRates;
        report.DailyOnTimeRates = onTimeRates;
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

    private ChartDataViewModel GeneratePerformanceChart(EmployeePerformanceViewModel report)
    {
        var chart = new ChartDataViewModel();

        var dailyData = report.DailyBreakdown
            .GroupBy(d => d.Date.Date)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Date = g.Key.ToString("MM/dd"),
                Completed = g.Count(d => d.Status == "Completed"),
                OnTime = g.Count(d => d.IsOnTime)
            })
            .ToList();

        chart.Labels = dailyData.Select(d => d.Date).ToList();

        chart.Datasets.Add(new ChartDatasetViewModel
        {
            Label = "Completed Tasks",
            Data = dailyData.Select(d => (double)d.Completed).ToList(),
            BackgroundColor = "rgba(99, 102, 241, 0.5)",
            BorderColor = "rgb(99, 102, 241)"
        });

        chart.Datasets.Add(new ChartDatasetViewModel
        {
            Label = "On Time Tasks",
            Data = dailyData.Select(d => (double)d.OnTime).ToList(),
            BackgroundColor = "rgba(34, 197, 94, 0.5)",
            BorderColor = "rgb(34, 197, 94)"
        });

        return chart;
    }

    private ChartDataViewModel GenerateTaskDistributionChart(EmployeePerformanceViewModel report)
    {
        var chart = new ChartDataViewModel();

        chart.Labels = report.TaskBreakdown.Keys.ToList();

        chart.Datasets.Add(new ChartDatasetViewModel
        {
            Label = "Total",
            Data = report.TaskBreakdown.Values.Select(v => (double)v.Total).ToList(),
            BackgroundColor = "rgba(99, 102, 241, 0.5)",
            BorderColor = "rgb(99, 102, 241)"
        });

        chart.Datasets.Add(new ChartDatasetViewModel
        {
            Label = "Completed",
            Data = report.TaskBreakdown.Values.Select(v => (double)v.Completed).ToList(),
            BackgroundColor = "rgba(34, 197, 94, 0.5)",
            BorderColor = "rgb(34, 197, 94)"
        });

        return chart;
    }

    public async Task<BranchPerformanceViewModel> ExecuteBranchReportAsync(int branchId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var branch = await _context.Branches
                .Include(b => b.Department)
                .FirstOrDefaultAsync(b => b.Id == branchId);

            if (branch == null)
                return new BranchPerformanceViewModel
                {
                    BranchId = branchId,
                    BranchName = "Unknown",
                    StartDate = startDate,
                    EndDate = endDate
                };

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta.Employee)
                .Where(dt => dt.BranchId == branchId &&
                             dt.TaskDate.Date >= startDate.Date &&
                             dt.TaskDate.Date <= endDate.Date)
                .OrderBy(dt => dt.TaskDate)
                .ToListAsync();

            var report = new BranchPerformanceViewModel
            {
                BranchId = branchId,
                BranchName = branch.Name,
                BranchCode = branch.Code ?? "N/A",
                Department = branch.Department?.Name ?? "N/A",
                Location = branch.Address ?? "N/A",
                StartDate = startDate,
                EndDate = endDate,
                TaskBreakdown = new Dictionary<string, BranchTaskStats>(),
                DailyBreakdown = new List<BranchDailyTaskViewModel>(),
                EmployeeScores = new Dictionary<string, double>(),
                MonthlyTrends = new Dictionary<string, MonthlyStatViewModel>(),
                Insights = new List<string>(),
                TopPerformers = new List<EmployeePerformanceSummary>(),
                NeedsImprovement = new List<EmployeePerformanceSummary>()
            };

            // Get employee count
            report.TotalEmployees = await _context.BranchAssignments
                .Where(ba => ba.BranchId == branchId && ba.EndDate == null)
                .CountAsync();

            report.ActiveEmployees = report.TotalEmployees;

            // Initialize task breakdown
            var tasks = await _context.TaskItems.Where(t => t.IsActive).ToListAsync();
            foreach (var task in tasks)
            {
                report.TaskBreakdown[task.Name] = new BranchTaskStats();
            }

            // Track employee performance
            var employeeStats = new Dictionary<int, (int Total, int Completed, int OnTime)>();

            foreach (var dt in dailyTasks)
            {
                if (dt.TaskItem == null) continue;

                var taskName = dt.TaskItem.Name;
                if (!report.TaskBreakdown.ContainsKey(taskName))
                {
                    report.TaskBreakdown[taskName] = new BranchTaskStats();
                }

                report.TaskBreakdown[taskName].Total++;
                report.TotalTasks++;

                var dailyTask = new BranchDailyTaskViewModel
                {
                    Date = dt.TaskDate,
                    TaskName = taskName,
                    Status = dt.IsCompleted ? "Completed" : "Pending",
                    AssignedTo = dt.TaskAssignment?.Employee?.Name ?? "Unassigned"
                };

                if (dt.IsCompleted && dt.CompletedAt.HasValue)
                {
                    report.TaskBreakdown[taskName].Completed++;
                    report.CompletedTasks++;

                    dailyTask.CompletionTime = dt.CompletedAt.Value;
                    dailyTask.Deadline = CalculateDeadline(dt.TaskItem, dt.TaskDate);

                    if (dt.AdjustmentMinutes > 0)
                    {
                        dailyTask.Deadline = dailyTask.Deadline.AddMinutes(dt.AdjustmentMinutes.Value);
                        dailyTask.AdjustmentMinutes = dt.AdjustmentMinutes;
                    }

                    dailyTask.IsOnTime = dt.CompletedAt.Value <= dailyTask.Deadline;

                    if (dailyTask.IsOnTime)
                    {
                        report.TaskBreakdown[taskName].OnTime++;
                        report.OnTimeTasks++;

                        if (dt.TaskAssignment?.EmployeeId > 0)
                        {
                            var empId = dt.TaskAssignment.EmployeeId;
                            var stats = employeeStats.GetValueOrDefault(empId);
                            stats.Total++;
                            stats.Completed++;
                            stats.OnTime++;
                            employeeStats[empId] = stats;
                        }
                    }
                    else
                    {
                        report.TaskBreakdown[taskName].Late++;
                        report.LateTasks++;

                        if (dt.TaskAssignment?.EmployeeId > 0)
                        {
                            var empId = dt.TaskAssignment.EmployeeId;
                            var stats = employeeStats.GetValueOrDefault(empId);
                            stats.Total++;
                            stats.Completed++;
                            employeeStats[empId] = stats;
                        }
                    }
                }
                else
                {
                    report.TaskBreakdown[taskName].Pending++;
                    report.PendingTasks++;

                    if (dt.TaskAssignment?.EmployeeId > 0)
                    {
                        var empId = dt.TaskAssignment.EmployeeId;
                        var stats = employeeStats.GetValueOrDefault(empId);
                        stats.Total++;
                        employeeStats[empId] = stats;
                    }
                }

                report.DailyBreakdown.Add(dailyTask);

                // Monthly trends
                var monthKey = dt.TaskDate.ToString("yyyy-MM");
                if (!report.MonthlyTrends.ContainsKey(monthKey))
                {
                    report.MonthlyTrends[monthKey] = new MonthlyStatViewModel
                    {
                        Month = dt.TaskDate.ToString("MMM yyyy")
                    };
                }
                report.MonthlyTrends[monthKey].TotalTasks++;
                if (dt.IsCompleted) report.MonthlyTrends[monthKey].CompletedTasks++;
            }

            // Calculate employee scores
            foreach (var emp in employeeStats)
            {
                var employee = await _context.Employees.FindAsync(emp.Key);
                if (employee != null)
                {
                    var total = emp.Value.Total;
                    var onTime = emp.Value.OnTime;

                    var score = total > 0
                        ? Math.Round((double)onTime / total * 100, 1)
                        : 0;

                    report.EmployeeScores[employee.Name] = score;

                    var perf = new EmployeePerformanceSummary
                    {
                        EmployeeId = emp.Key,
                        EmployeeName = employee.Name,
                        TasksCompleted = emp.Value.Completed,
                        OnTimeRate = emp.Value.Completed > 0
                            ? Math.Round((double)onTime / emp.Value.Completed * 100, 1)
                            : 0,
                        Score = score
                    };

                    if (score >= 80)
                        report.TopPerformers.Add(perf);
                    else if (score < 60 && score > 0)
                        report.NeedsImprovement.Add(perf);
                }
            }

            // Calculate rates
            foreach (var kvp in report.TaskBreakdown)
            {
                var stats = kvp.Value;
                stats.CompletionRate = stats.Total > 0
                    ? Math.Round((double)stats.Completed / stats.Total * 100, 1)
                    : 0;
                stats.OnTimeRate = stats.Completed > 0
                    ? Math.Round((double)stats.OnTime / stats.Completed * 100, 1)
                    : 0;
            }

            foreach (var month in report.MonthlyTrends.Values)
            {
                month.CompletionRate = month.TotalTasks > 0
                    ? Math.Round((double)month.CompletedTasks / month.TotalTasks * 100, 1)
                    : 0;
            }

            report.CompletionRate = report.TotalTasks > 0
                ? Math.Round((double)report.CompletedTasks / report.TotalTasks * 100, 1)
                : 0;

            report.OnTimeRate = report.CompletedTasks > 0
                ? Math.Round((double)report.OnTimeTasks / report.CompletedTasks * 100, 1)
                : 0;

            report.OverallScore = report.CompletedTasks > 0
                ? Math.Round((double)report.OnTimeTasks / report.CompletedTasks * 100, 1)
                : 0;

            // Sort performers
            report.TopPerformers = report.TopPerformers.OrderByDescending(p => p.Score).Take(5).ToList();
            report.NeedsImprovement = report.NeedsImprovement.OrderBy(p => p.Score).Take(5).ToList();

            // Generate insights
            report.Insights = GenerateBranchInsights(report);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing branch report for {BranchId}", branchId);
            throw;
        }
    }

    private List<string> GenerateBranchInsights(BranchPerformanceViewModel report)
    {
        var insights = new List<string>();

        if (report.OverallScore >= 90)
            insights.Add("🌟 Excellent branch performance!");
        else if (report.OverallScore >= 75)
            insights.Add("👍 Good branch performance");
        else if (report.OverallScore >= 60)
            insights.Add("📊 Average branch performance");
        else
            insights.Add("⚠️ Branch performance needs improvement");

        if (report.TopPerformers.Any())
            insights.Add($"🏆 Top performer: {report.TopPerformers.First().EmployeeName} with {report.TopPerformers.First().Score}% score");

        if (report.NeedsImprovement.Any())
            insights.Add($"📉 Needs improvement: {report.NeedsImprovement.First().EmployeeName} with {report.NeedsImprovement.First().Score}% score");

        return insights;
    }

    public async Task<DepartmentPerformanceViewModel> ExecuteDepartmentReportAsync(int departmentId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == departmentId);

            if (department == null)
                return new DepartmentPerformanceViewModel
                {
                    DepartmentId = departmentId,
                    DepartmentName = "Unknown",
                    StartDate = startDate,
                    EndDate = endDate
                };

            var branches = await _context.Branches
                .Include(b => b.DailyTasks)
                    .ThenInclude(dt => dt.TaskItem)
                .Where(b => b.DepartmentId == departmentId && b.IsActive)
                .ToListAsync();

            var report = new DepartmentPerformanceViewModel
            {
                DepartmentId = departmentId,
                DepartmentName = department.Name,
                DepartmentCode = department.Code ?? "N/A",
                StartDate = startDate,
                EndDate = endDate,
                BranchPerformance = new Dictionary<string, BranchPerformanceSummary>(),
                TaskMatrix = new Dictionary<string, Dictionary<string, TaskStatSummary>>(),
                TopBranches = new List<BranchPerformanceSummary>(),
                BottomBranches = new List<BranchPerformanceSummary>(),
                Insights = new List<string>(),
                Recommendations = new List<string>()
            };

            report.TotalBranches = branches.Count;
            report.ActiveBranches = branches.Count(b => b.IsActive);

            var allEmployees = new List<Employee>();
            var allTasks = new List<DailyTask>();
            var taskStats = new Dictionary<string, TaskStatSummary>();

            foreach (var branch in branches)
            {
                var branchEmployees = await _context.BranchAssignments
                    .Where(ba => ba.BranchId == branch.Id && ba.EndDate == null)
                    .Select(ba => ba.Employee)
                    .Where(e => e != null)
                    .ToListAsync();

                allEmployees.AddRange(branchEmployees);

                var branchTasks = branch.DailyTasks
                    .Where(dt => dt.TaskDate.Date >= startDate.Date &&
                                dt.TaskDate.Date <= endDate.Date)
                    .ToList();

                allTasks.AddRange(branchTasks);

                var completed = branchTasks.Count(t => t.IsCompleted);
                var onTime = branchTasks.Count(t => t.IsCompleted && t.CompletedAt.HasValue &&
                    t.CompletedAt.Value <= CalculateDeadline(t.TaskItem!, t.TaskDate));

                var branchSummary = new BranchPerformanceSummary
                {
                    BranchId = branch.Id,
                    BranchName = branch.Name,
                    TotalTasks = branchTasks.Count,
                    CompletedTasks = completed,
                    CompletionRate = branchTasks.Count > 0
                        ? Math.Round((double)completed / branchTasks.Count * 100, 1)
                        : 0,
                    OnTimeRate = completed > 0
                        ? Math.Round((double)onTime / completed * 100, 1)
                        : 0,
                    EmployeeCount = branchEmployees.Count
                };

                report.BranchPerformance[branch.Name] = branchSummary;

                // Task matrix for this branch
                var branchTaskStats = new Dictionary<string, TaskStatSummary>();
                foreach (var task in branchTasks.GroupBy(t => t.TaskItemId))
                {
                    var taskItem = await _context.TaskItems.FindAsync(task.Key);
                    if (taskItem == null) continue;

                    var taskName = taskItem.Name;
                    var taskTotal = task.Count();
                    var taskCompleted = task.Count(t => t.IsCompleted);

                    var stats = new TaskStatSummary
                    {
                        Total = taskTotal,
                        Completed = taskCompleted,
                        CompletionRate = taskTotal > 0
                            ? Math.Round((double)taskCompleted / taskTotal * 100, 1)
                            : 0
                    };

                    branchTaskStats[taskName] = stats;

                    // Update overall task stats
                    if (!taskStats.ContainsKey(taskName))
                        taskStats[taskName] = new TaskStatSummary();
                    taskStats[taskName].Total += taskTotal;
                    taskStats[taskName].Completed += taskCompleted;
                }
                report.TaskMatrix[branch.Name] = branchTaskStats;
            }

            // Calculate task completion rates
            foreach (var task in taskStats)
            {
                task.Value.CompletionRate = task.Value.Total > 0
                    ? Math.Round((double)task.Value.Completed / task.Value.Total * 100, 1)
                    : 0;
            }

            report.TotalEmployees = allEmployees.Count;
            report.ActiveEmployees = allEmployees.Count(e => e.IsActive);
            report.TotalTasks = allTasks.Count;
            report.CompletedTasks = allTasks.Count(t => t.IsCompleted);
            report.OverallCompletionRate = report.TotalTasks > 0
                ? Math.Round((double)report.CompletedTasks / report.TotalTasks * 100, 1)
                : 0;

            var totalOnTime = allTasks.Count(t => t.IsCompleted && t.CompletedAt.HasValue &&
                t.CompletedAt.Value <= CalculateDeadline(t.TaskItem!, t.TaskDate));
            report.OverallOnTimeRate = report.CompletedTasks > 0
                ? Math.Round((double)totalOnTime / report.CompletedTasks * 100, 1)
                : 0;

            // Top and bottom branches
            report.TopBranches = report.BranchPerformance.Values
                .OrderByDescending(b => b.CompletionRate)
                .Take(3)
                .ToList();

            report.BottomBranches = report.BranchPerformance.Values
                .Where(b => b.TotalTasks > 0)
                .OrderBy(b => b.CompletionRate)
                .Take(3)
                .ToList();

            // Generate insights
            report.Insights = GenerateDepartmentInsights(report);
            report.Recommendations = GenerateDepartmentRecommendations(report);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing department report for {DepartmentId}", departmentId);
            throw;
        }
    }

    private List<string> GenerateDepartmentInsights(DepartmentPerformanceViewModel report)
    {
        var insights = new List<string>();

        if (report.OverallCompletionRate >= 90)
            insights.Add("🌟 Excellent department-wide completion rate!");
        else if (report.OverallCompletionRate >= 75)
            insights.Add("👍 Good department performance");
        else if (report.OverallCompletionRate >= 60)
            insights.Add("📊 Average department performance");
        else
            insights.Add("⚠️ Department performance needs attention");

        if (report.TopBranches.Any())
            insights.Add($"🏆 Top branch: {report.TopBranches.First().BranchName} with {report.TopBranches.First().CompletionRate}% completion");

        if (report.BottomBranches.Any())
            insights.Add($"📉 Branch needing attention: {report.BottomBranches.First().BranchName} with {report.BottomBranches.First().CompletionRate}% completion");

        return insights;
    }

    private List<string> GenerateDepartmentRecommendations(DepartmentPerformanceViewModel report)
    {
        var recommendations = new List<string>();

        if (report.BottomBranches.Any())
        {
            recommendations.Add($"🎯 Focus on improving {report.BottomBranches.First().BranchName} branch");
        }

        if (report.OverallCompletionRate < 80)
        {
            recommendations.Add("📊 Review processes across all branches");
        }

        return recommendations;
    }

    public async Task<TaskCompletionViewModel> ExecuteTaskReportAsync(int taskId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var task = await _context.TaskItems.FindAsync(taskId);
            if (task == null)
                return new TaskCompletionViewModel
                {
                    TaskId = taskId,
                    TaskName = "Unknown",
                    StartDate = startDate,
                    EndDate = endDate
                };

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.Branch)
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta.Employee)
                .Where(dt => dt.TaskItemId == taskId &&
                             dt.TaskDate.Date >= startDate.Date &&
                             dt.TaskDate.Date <= endDate.Date)
                .ToListAsync();

            var report = new TaskCompletionViewModel
            {
                TaskId = taskId,
                TaskName = task.Name,
                Deadline = task.Deadline,
                IsSameDay = task.IsSameDay,
                StartDate = startDate,
                EndDate = endDate,
                BranchStats = new Dictionary<string, BranchTaskStat>(),
                EmployeeStats = new Dictionary<string, EmployeeTaskStat>(),
                DailyCompletions = new Dictionary<string, int>()
            };

            var completionTimes = new List<TimeSpan>();

            foreach (var dt in dailyTasks)
            {
                report.TotalAssignments++;

                if (dt.IsCompleted)
                {
                    report.Completed++;
                    if (dt.CompletedAt.HasValue)
                    {
                        var deadline = CalculateDeadline(task, dt.TaskDate);
                        if (dt.AdjustmentMinutes > 0)
                            deadline = deadline.AddMinutes(dt.AdjustmentMinutes.Value);

                        if (dt.CompletedAt.Value <= deadline)
                            report.OnTime++;
                        else
                            report.Late++;

                        completionTimes.Add(dt.CompletedAt.Value.TimeOfDay);
                    }
                }
                else
                {
                    report.Pending++;
                }

                // Branch stats
                var branchName = dt.Branch?.Name ?? "Unknown";
                if (!report.BranchStats.ContainsKey(branchName))
                {
                    report.BranchStats[branchName] = new BranchTaskStat
                    {
                        BranchName = branchName
                    };
                }
                report.BranchStats[branchName].Total++;
                if (dt.IsCompleted) report.BranchStats[branchName].Completed++;

                // Employee stats
                if (dt.TaskAssignment?.Employee != null)
                {
                    var empName = dt.TaskAssignment.Employee.Name;
                    if (!report.EmployeeStats.ContainsKey(empName))
                    {
                        report.EmployeeStats[empName] = new EmployeeTaskStat
                        {
                            EmployeeName = empName
                        };
                    }
                    report.EmployeeStats[empName].Total++;
                    if (dt.IsCompleted) report.EmployeeStats[empName].Completed++;
                }

                // Daily completions
                var dateKey = dt.TaskDate.ToString("yyyy-MM-dd");
                if (!report.DailyCompletions.ContainsKey(dateKey))
                    report.DailyCompletions[dateKey] = 0;
                if (dt.IsCompleted)
                    report.DailyCompletions[dateKey]++;
            }

            // Calculate rates
            report.CompletionRate = report.TotalAssignments > 0
                ? Math.Round((double)report.Completed / report.TotalAssignments * 100, 1)
                : 0;

            report.OnTimeRate = report.Completed > 0
                ? Math.Round((double)report.OnTime / report.Completed * 100, 1)
                : 0;

            foreach (var branch in report.BranchStats.Values)
            {
                branch.CompletionRate = branch.Total > 0
                    ? Math.Round((double)branch.Completed / branch.Total * 100, 1)
                    : 0;
            }

            foreach (var emp in report.EmployeeStats.Values)
            {
                emp.CompletionRate = emp.Total > 0
                    ? Math.Round((double)emp.Completed / emp.Total * 100, 1)
                    : 0;
            }

            // Calculate timing stats
            if (completionTimes.Any())
            {
                var avgTicks = (long)completionTimes.Average(t => t.Ticks);
                report.AverageCompletionTime = new TimeSpan(avgTicks);
                report.FastestCompletion = completionTimes.Min();
                report.SlowestCompletion = completionTimes.Max();
            }

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing task report for {TaskId}", taskId);
            throw;
        }
    }

    public async Task<AuditLogReportViewModel> ExecuteAuditReportAsync(DateTime startDate, DateTime endDate, string? action = null, string? entityType = null)
    {
        try
        {
            var query = _context.AuditLogs
                .Where(a => a.Timestamp.Date >= startDate.Date &&
                           a.Timestamp.Date <= endDate.Date);

            if (!string.IsNullOrEmpty(action))
                query = query.Where(a => a.Action == action);

            if (!string.IsNullOrEmpty(entityType))
                query = query.Where(a => a.EntityType == entityType);

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync();

            var report = new AuditLogReportViewModel
            {
                StartDate = startDate,
                EndDate = endDate,
                TotalEvents = logs.Count,
                EventsByAction = logs.GroupBy(l => l.Action)
                    .ToDictionary(g => g.Key, g => g.Count()),
                EventsByEntity = logs.GroupBy(l => l.EntityType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                EventsByUser = logs.GroupBy(l => l.UserName)
                    .ToDictionary(g => g.Key, g => g.Count()),
                Events = logs.Select(l => new AuditLogEntry
                {
                    Timestamp = l.Timestamp,
                    Action = l.Action,
                    EntityType = l.EntityType,
                    EntityId = l.EntityId,
                    Description = l.Description,
                    UserName = l.UserName,
                    IpAddress = l.IpAddress
                }).ToList(),
                EventsByDay = logs.GroupBy(l => l.Timestamp.Date.ToString("yyyy-MM-dd"))
                    .ToDictionary(g => g.Key, g => g.Count()),
                EventsByHour = logs.GroupBy(l => l.Timestamp.Hour.ToString("D2"))
                    .ToDictionary(g => g.Key, g => g.Count())
            };

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing audit report");
            throw;
        }
    }

    public async Task<List<Dictionary<string, object>>> ExecuteCustomReportAsync(CustomReportRequest request)
    {
        try
        {
            var result = new List<Dictionary<string, object>>();

            var query = _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Include(dt => dt.Branch)
                    .ThenInclude(b => b.Department)
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta.Employee)
                .Where(dt => dt.TaskDate.Date >= request.StartDate.Date &&
                            dt.TaskDate.Date <= request.EndDate.Date);

            // Apply filters
            if (request.BranchIds != null && request.BranchIds.Any())
            {
                query = query.Where(dt => request.BranchIds.Contains(dt.BranchId));
            }

            if (request.TaskIds != null && request.TaskIds.Any())
            {
                query = query.Where(dt => request.TaskIds.Contains(dt.TaskItemId));
            }

            if (request.EmployeeIds != null && request.EmployeeIds.Any())
            {
                query = query.Where(dt => dt.TaskAssignment != null &&
                                          request.EmployeeIds.Contains(dt.TaskAssignment.EmployeeId));
            }

            if (request.DepartmentIds != null && request.DepartmentIds.Any())
            {
                query = query.Where(dt => dt.Branch != null &&
                                          dt.Branch.DepartmentId != null &&
                                          request.DepartmentIds.Contains(dt.Branch.DepartmentId.Value));
            }

            if (!string.IsNullOrEmpty(request.Status) && request.Status != "All")
            {
                var isCompleted = request.Status == "Completed";
                query = query.Where(dt => dt.IsCompleted == isCompleted);
            }

            var data = await query.ToListAsync();

            foreach (var item in data)
            {
                var row = new Dictionary<string, object>
                {
                    ["Date"] = item.TaskDate.ToString("yyyy-MM-dd"),
                    ["Branch"] = item.Branch?.Name ?? "Unknown",
                    ["Department"] = item.Branch?.Department?.Name ?? "Unknown",
                    ["Task"] = item.TaskItem?.Name ?? "Unknown",
                    ["Status"] = item.IsCompleted ? "Completed" : "Pending",
                    ["CompletionTime"] = item.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    ["AssignedTo"] = item.TaskAssignment?.Employee?.Name ?? "Unassigned",
                    ["Adjustment"] = item.AdjustmentMinutes ?? 0,
                    ["AdjustmentReason"] = item.AdjustmentReason ?? "",
                    ["BulkUpdated"] = item.IsBulkUpdated ? "Yes" : "No"
                };

                result.Add(row);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing custom report");
            throw;
        }
    }

    #endregion

    #region Report Export

    public async Task<byte[]> ExportToExcelAsync(object data, string reportName)
    {
        try
        {
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Report");

                if (data is IEnumerable<Dictionary<string, object>> listData && listData.Any())
                {
                    // Add headers
                    var headers = listData.First().Keys.ToList();
                    for (int i = 0; i < headers.Count; i++)
                    {
                        worksheet.Cells[1, i + 1].Value = headers[i];
                        worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                        worksheet.Cells[1, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    }

                    // Add data
                    int row = 2;
                    foreach (var item in listData)
                    {
                        for (int i = 0; i < headers.Count; i++)
                        {
                            var value = item.ContainsKey(headers[i]) ? item[headers[i]] : null;
                            worksheet.Cells[row, i + 1].Value = value?.ToString();
                        }
                        row++;
                    }

                    worksheet.Cells.AutoFitColumns();
                }
                else if (data is System.Collections.IEnumerable enumerable)
                {
                    var row = 1;
                    foreach (var item in enumerable)
                    {
                        var col = 1;
                        foreach (var prop in item.GetType().GetProperties())
                        {
                            if (row == 1)
                            {
                                worksheet.Cells[1, col].Value = prop.Name;
                                worksheet.Cells[1, col].Style.Font.Bold = true;
                            }
                            worksheet.Cells[row + 1, col].Value = prop.GetValue(item)?.ToString();
                            col++;
                        }
                        row++;
                    }
                }

                return await Task.FromResult(package.GetAsByteArray());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to Excel");
            throw;
        }
    }

    public async Task<byte[]> ExportToCsvAsync(object data, string reportName)
    {
        try
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new StreamWriter(memoryStream))
            {
                if (data is IEnumerable<Dictionary<string, object>> listData && listData.Any())
                {
                    // Write headers
                    var headers = listData.First().Keys.ToList();
                    await writer.WriteLineAsync(string.Join(",", headers));

                    // Write data
                    foreach (var item in listData)
                    {
                        var values = headers.Select(h =>
                            $"\"{(item.ContainsKey(h) ? item[h]?.ToString()?.Replace("\"", "\"\"") : "")}\"");
                        await writer.WriteLineAsync(string.Join(",", values));
                    }
                }
                else if (data is System.Collections.IEnumerable enumerable)
                {
                    var first = true;
                    foreach (var item in enumerable)
                    {
                        var props = item.GetType().GetProperties();

                        if (first)
                        {
                            await writer.WriteLineAsync(string.Join(",", props.Select(p => p.Name)));
                            first = false;
                        }

                        await writer.WriteLineAsync(string.Join(",",
                            props.Select(p => $"\"{p.GetValue(item)?.ToString()?.Replace("\"", "\"\"")}\"")));
                    }
                }

                await writer.FlushAsync();
                return memoryStream.ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to CSV");
            throw;
        }
    }

    public async Task<byte[]> ExportToPdfAsync(object data, string reportName)
    {
        // PDF export would require a PDF library like iTextSharp or QuestPDF
        // This is a placeholder - implement based on your PDF library choice
        _logger.LogWarning("PDF export not implemented - returning empty byte array");
        return await Task.FromResult(Array.Empty<byte>());
    }

    #endregion

    #region Report Scheduling

    public async Task<bool> ScheduleReportAsync(int reportId, string cronExpression, List<string> recipients)
    {
        try
        {
            var report = await _context.Reports.FindAsync(reportId);
            if (report == null) return false;

            report.IsScheduled = true;
            report.ScheduleCron = cronExpression;
            report.Recipients = JsonSerializer.Serialize(recipients);
            report.NextRunDate = CalculateNextRunDate(cronExpression);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Schedule",
                "Report",
                reportId,
                $"Scheduled report: {report.Name}"
            );

            _logger.LogInformation("Report {ReportId} scheduled with cron: {Cron}", reportId, cronExpression);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling report {ReportId}", reportId);
            return false;
        }
    }

    public async Task<bool> UnscheduleReportAsync(int reportId)
    {
        try
        {
            var report = await _context.Reports.FindAsync(reportId);
            if (report == null) return false;

            report.IsScheduled = false;
            report.ScheduleCron = null;
            report.NextRunDate = null;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Unschedule",
                "Report",
                reportId,
                $"Unscheduled report: {report.Name}"
            );

            _logger.LogInformation("Report {ReportId} unscheduled", reportId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unscheduling report {ReportId}", reportId);
            return false;
        }
    }

    public async Task ProcessScheduledReportsAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var dueReports = await _context.Reports
                .Where(r => r.IsScheduled &&
                           r.IsActive &&
                           r.NextRunDate.HasValue &&
                           r.NextRunDate.Value <= now)
                .ToListAsync();

            foreach (var report in dueReports)
            {
                try
                {
                    _logger.LogInformation("Processing scheduled report: {ReportName}", report.Name);

                    var result = await ExecuteReportAsync(report.Id);

                    byte[] fileData;
                    string fileName;

                    switch (report.ExportFormat?.ToLower())
                    {
                        case "csv":
                            fileData = await ExportToCsvAsync(result, report.Name);
                            fileName = $"{report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                            break;
                        case "pdf":
                            fileData = await ExportToPdfAsync(result, report.Name);
                            fileName = $"{report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                            break;
                        default:
                            fileData = await ExportToExcelAsync(result, report.Name);
                            fileName = $"{report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                            break;
                    }

                    // TODO: Send email with attachment
                    _logger.LogInformation("Report {ReportName} generated, ready to send to {Recipients}",
                        report.Name, report.Recipients);

                    report.NextRunDate = CalculateNextRunDate(report.ScheduleCron ?? "");
                    report.LastRunAt = DateTime.UtcNow;
                    report.LastError = null;

                    _logger.LogInformation("Scheduled report {ReportName} processed successfully", report.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing scheduled report {ReportName}", report.Name);
                    report.LastError = ex.Message;
                }
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing scheduled reports");
        }
    }

    private DateTime? CalculateNextRunDate(string cronExpression)
    {
        if (string.IsNullOrEmpty(cronExpression))
            return null;

        var now = DateTime.UtcNow;

        // Simple parsing for common patterns
        if (cronExpression == "0 9 * * *") // Daily at 9 AM
        {
            var next = now.Date.AddHours(9);
            if (next <= now) next = next.AddDays(1);
            return next;
        }
        else if (cronExpression == "0 9 * * 1") // Weekly on Monday at 9 AM
        {
            var next = now.Date.AddHours(9);
            while (next.DayOfWeek != DayOfWeek.Monday || next <= now)
            {
                next = next.AddDays(1);
            }
            return next;
        }
        else if (cronExpression == "0 9 1 * *") // Monthly on 1st at 9 AM
        {
            var next = new DateTime(now.Year, now.Month, 1).AddHours(9);
            if (next <= now) next = next.AddMonths(1);
            return next;
        }

        return now.AddDays(1);
    }

    #endregion

    #region Report Templates

    public async Task<Report> CreateFromTemplateAsync(string templateName, string newName)
    {
        try
        {
            var template = await _context.Reports
                .FirstOrDefaultAsync(r => r.Name == templateName && r.IsPublic);

            if (template == null)
                throw new Exception($"Template {templateName} not found");

            var newReport = new Report
            {
                Name = newName,
                Description = template.Description,
                ReportType = template.ReportType,
                Category = template.Category,
                Configuration = template.Configuration,
                Columns = template.Columns,
                Filters = template.Filters,
                SortBy = template.SortBy,
                IsAscending = template.IsAscending,
                StartDate = template.StartDate,
                EndDate = template.EndDate,
                ExportFormat = template.ExportFormat,
                IncludeCharts = template.IncludeCharts,
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                IsPublic = false
            };

            _context.Reports.Add(newReport);
            await _context.SaveChangesAsync();

            return newReport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating report from template {TemplateName}", templateName);
            throw;
        }
    }

    #endregion

    #region Insights and Recommendations

    public async Task<List<string>> GenerateInsightsAsync(EmployeePerformanceViewModel report)
    {
        var insights = new List<string>();

        if (report.OverallScore >= 90)
        {
            insights.Add("🌟 Excellent performance! Consistently completing tasks on time.");
            report.Strengths?.Add("Excellent overall performance");
        }
        else if (report.OverallScore >= 75)
        {
            insights.Add("👍 Good performance. Keep up the good work!");
            report.Strengths?.Add("Good overall performance");
        }
        else if (report.OverallScore >= 60)
        {
            insights.Add("📊 Average performance. There's room for improvement.");
            report.Weaknesses?.Add("Average performance - room for improvement");
        }
        else
        {
            insights.Add("⚠️ Needs improvement. Focus on completing tasks on time.");
            report.Weaknesses?.Add("Performance needs significant improvement");
        }

        if (report.CompletionRate >= 95)
        {
            insights.Add("✅ Outstanding task completion rate!");
            report.Strengths?.Add("Excellent task completion rate");
        }
        else if (report.CompletionRate < 70)
        {
            insights.Add("⚠️ Low task completion rate. Review pending tasks.");
            report.Weaknesses?.Add("Low task completion rate");
        }

        if (report.OnTimeRate >= 90)
        {
            insights.Add("⏰ Excellent punctuality! Most tasks completed on time.");
            report.Strengths?.Add("Excellent punctuality");
        }
        else if (report.OnTimeRate < 60)
        {
            insights.Add("⌛ Time management needs improvement. Many tasks are late.");
            report.Weaknesses?.Add("Time management needs improvement");
        }

        return await Task.FromResult(insights);
    }

    public async Task<List<string>> GenerateRecommendationsAsync(EmployeePerformanceViewModel report)
    {
        var recommendations = new List<string>();

        if (report.CompletionRate < 80)
        {
            recommendations.Add("🎯 Focus on completing pending tasks before starting new ones");
        }

        if (report.OnTimeRate < 70)
        {
            recommendations.Add("⏰ Start tasks earlier in the day to meet deadlines");
        }

        if (report.OverallScore >= 80)
        {
            recommendations.Add("🌟 Consider mentoring other team members");
        }
        else if (report.OverallScore >= 60)
        {
            recommendations.Add("📊 Create a daily task schedule and prioritize urgent items");
        }
        else
        {
            recommendations.Add("🎯 Meet with supervisor to create an improvement plan");
        }

        return await Task.FromResult(recommendations);
    }

    #endregion
}
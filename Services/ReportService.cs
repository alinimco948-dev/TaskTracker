using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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
    private readonly ITaskCalculationService _taskCalculationService;
    private readonly ITimezoneService _timezoneService;

    public ReportService(
        ApplicationDbContext context,
        ILogger<ReportService> logger,
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ITaskService taskService,
        IAuditService auditService,
        IHolidayService holidayService,
        ITaskCalculationService taskCalculationService,
        ITimezoneService timezoneService)
    {
        _context = context;
        _logger = logger;
        _employeeService = employeeService;
        _branchService = branchService;
        _departmentService = departmentService;
        _taskService = taskService;
        _auditService = auditService;
        _holidayService = holidayService;
        _taskCalculationService = taskCalculationService;
        _timezoneService = timezoneService;
        ExcelPackage.License.SetNonCommercialPersonal($"TaskTracker-{Environment.UserName}");
    }

    #region Report Management

    public async Task<Report> CreateReportAsync(Report report)
    {
        try
        {
            report.CreatedAt = DateTime.UtcNow;
            report.RunCount = 0;
            _context.Reports.Add(report);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync("Create", "Report", report.Id, $"Created report: {report.Name}");
            _logger.LogInformation("Report created: {ReportName}", report.Name);
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating report");
            throw;
        }
    }
public async Task<EmployeeComparisonViewModel> ExecuteEmployeeComparisonReportAsync(int? branchId, List<int> selectedEmployeeIds, DateTime startDate, DateTime endDate, string comparisonMode = "branch")
{
    try
    {
        var utcStartDate = _timezoneService.GetStartOfDayLocal(startDate);
        var utcEndDate = _timezoneService.GetEndOfDayLocal(endDate);
        
        List<Employee> employees;
        string branchName = "All Branches";
        
        if (comparisonMode == "selected" && selectedEmployeeIds != null && selectedEmployeeIds.Any())
        {
            // Get selected employees only
            employees = await _context.Employees
                .Include(e => e.Department)
                .Where(e => selectedEmployeeIds.Contains(e.Id) && e.IsActive)
                .ToListAsync();
            branchName = "Selected Employees";
        }
        else if (branchId.HasValue && branchId.Value > 0)
        {
            // Get employees by branch
            var branch = await _context.Branches.FindAsync(branchId.Value);
            branchName = branch?.Name ?? "Selected Branch";
            
            var employeeIds = await _context.BranchAssignments
                .Where(ba => ba.BranchId == branchId.Value && 
                            (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
                .Select(ba => ba.EmployeeId)
                .Distinct()
                .ToListAsync();
            
            employees = await _context.Employees
                .Include(e => e.Department)
                .Where(e => employeeIds.Contains(e.Id) && e.IsActive)
                .ToListAsync();
        }
        else
        {
            // Get all active employees
            employees = await _context.Employees
                .Include(e => e.Department)
                .Where(e => e.IsActive)
                .ToListAsync();
            branchName = "All Employees";
        }
        
        if (employees == null || !employees.Any())
        {
            return new EmployeeComparisonViewModel
            {
                BranchId = branchId,
                BranchName = branchName,
                SelectedEmployeeIds = selectedEmployeeIds ?? new List<int>(),
                StartDate = startDate,
                EndDate = endDate,
                ComparisonMode = comparisonMode,
                TotalEmployeesCompared = 0,
                Employees = new List<EmployeeComparisonItem>(),
                KeyInsights = new List<string> { "No employees found for the selected criteria" },
                Recommendations = new List<string>()
            };
        }
        
        // ---- Batch load: branch assignments for ALL employees in one query ----
        var empIds = employees.Select(e => e.Id).ToList();
        var allBranchAssignments = await _context.BranchAssignments
            .Include(ba => ba.Branch)
            .Where(ba => empIds.Contains(ba.EmployeeId) && 
                        (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
            .AsNoTracking()
            .ToListAsync();
        var branchAssignmentsByEmp = allBranchAssignments
            .GroupBy(ba => ba.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(ba => ba.Branch?.Name ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)).ToList());

        var employeeComparisons = new List<EmployeeComparisonItem>();
        
        foreach (var emp in employees)
        {
            // In-memory lookup instead of DB call
            var employeeBranches = branchAssignmentsByEmp.TryGetValue(emp.Id, out var eb) ? eb : new List<string>();
            
            // Get task statistics
            var stats = await _taskCalculationService.GetEmployeeTaskStatisticsAsync(emp.Id, utcStartDate, utcEndDate);
            
            // Get daily tasks for comparison
            var dailyTasks = await GetEmployeeDailyTasksAsync(emp.Id, utcStartDate, utcEndDate);
            
            // Analyze strengths and weaknesses
            var strengths = new List<string>();
            var weaknesses = new List<string>();
            
            if (stats.CompletionRate >= 90)
                strengths.Add("Excellent completion rate");
            else if (stats.CompletionRate < 70 && stats.TotalTasks > 0)
                weaknesses.Add($"Low completion rate ({stats.CompletionRate}%)");
            
            if (stats.OnTimeRate >= 90)
                strengths.Add("Excellent punctuality");
            else if (stats.OnTimeRate < 70 && stats.CompletedTasks > 0)
                weaknesses.Add($"Frequent late submissions ({stats.OnTimeRate}% on time)");
            
            if (stats.TotalTasks > 50)
                strengths.Add("High task volume handled");
            
            var performanceLevel = stats.WeightedScore switch
            {
                >= 90 => "Excellent",
                >= 75 => "Good",
                >= 60 => "Average",
                _ => "Needs Improvement"
            };
            
            var comparison = new EmployeeComparisonItem
            {
                Id = emp.Id,
                Name = emp.Name,
                EmployeeId = emp.EmployeeId,
                Position = emp.Position ?? "N/A",
                Department = emp.Department?.Name ?? "Unassigned",
                Branches = employeeBranches,
                PerformanceScore = stats.WeightedScore,
                CompletionRate = stats.CompletionRate,
                OnTimeRate = stats.OnTimeRate,
                TotalTasks = stats.TotalTasks,
                CompletedTasks = stats.CompletedTasks,
                OnTimeTasks = stats.OnTimeTasks,
                LateTasks = stats.LateTasks,
                PendingTasks = stats.PendingTasks,
                PerformanceLevel = performanceLevel,
                Strengths = string.Join(", ", strengths),
                Weaknesses = string.Join(", ", weaknesses),
                DailyTasks = dailyTasks
            };
            
            employeeComparisons.Add(comparison);
        }
        
        // Sort employees by performance score — compute average ONCE (was O(N²) before)
        var avgScore = employeeComparisons.Any() ? employeeComparisons.Average(e => e.PerformanceScore) : 0;
        var sortedEmployees = employeeComparisons.OrderByDescending(e => e.PerformanceScore).ToList();
        for (int i = 0; i < sortedEmployees.Count; i++)
        {
            sortedEmployees[i].Rank = i + 1;
            sortedEmployees[i].VsAverage = Math.Round(sortedEmployees[i].PerformanceScore - avgScore, 1);
        }
        
        var allScores = employeeComparisons.Select(e => e.PerformanceScore).ToList();
        var averageScore = allScores.Any() ? allScores.Average() : 0;
        var highestScore = allScores.Any() ? allScores.Max() : 0;
        var lowestScore = allScores.Any() ? allScores.Min() : 0;
        
        // Comparison Analysis
        var bestPerformer = sortedEmployees.FirstOrDefault() ?? new EmployeeComparisonItem();
        var worstPerformer = sortedEmployees.LastOrDefault() ?? new EmployeeComparisonItem();
        
        var comparisonResult = new ComparisonAnalysis
        {
            BestPerformer = bestPerformer,
            WorstPerformer = worstPerformer,
            ScoreDifference = Math.Round(bestPerformer.PerformanceScore - worstPerformer.PerformanceScore, 1),
            CompletionGap = Math.Round(bestPerformer.CompletionRate - worstPerformer.CompletionRate, 1),
            OnTimeGap = Math.Round(bestPerformer.OnTimeRate - worstPerformer.OnTimeRate, 1),
            TasksGap = bestPerformer.TotalTasks - worstPerformer.TotalTasks,
            CommonStrengths = GetCommonItems(employeeComparisons.Select(e => e.Strengths).ToList(), 3),
            CommonWeaknesses = GetCommonItems(employeeComparisons.Select(e => e.Weaknesses).ToList(), 3),
            UniqueStrengths = GetUniqueItems(employeeComparisons.Select(e => e.Strengths).ToList(), bestPerformer.Name),
            UniqueWeaknesses = GetUniqueItems(employeeComparisons.Select(e => e.Weaknesses).ToList(), worstPerformer.Name)
        };
        
        // Generate insights
        var insights = new List<string>();
        insights.Add($"📊 Comparing {employeeComparisons.Count} employee(s)");
        
        if (comparisonMode == "selected" && selectedEmployeeIds != null && selectedEmployeeIds.Count == 2)
        {
            var emp1 = sortedEmployees.FirstOrDefault();
            var emp2 = sortedEmployees.LastOrDefault();
            if (emp1 != null && emp2 != null)
            {
                insights.Add($"🎯 {emp1.Name} is performing {comparisonResult.ScoreDifference}% better than {emp2.Name}");
                if (comparisonResult.CompletionGap > 0)
                    insights.Add($"✅ {emp1.Name} completes {comparisonResult.CompletionGap}% more tasks than {emp2.Name}");
                if (comparisonResult.OnTimeGap > 0)
                    insights.Add($"⏰ {emp1.Name} is {comparisonResult.OnTimeGap}% more punctual than {emp2.Name}");
            }
        }
        else if (comparisonMode == "branch")
        {
            insights.Add($"🏆 Best Performer: {bestPerformer.Name} with {bestPerformer.PerformanceScore}%");
            insights.Add($"⚠️ Needs Improvement: {worstPerformer.Name} with {worstPerformer.PerformanceScore}%");
            insights.Add($"📈 Performance Gap: {comparisonResult.ScoreDifference}% difference between best and worst");
        }
        
        insights.Add($"📋 Total Tasks Completed: {employeeComparisons.Sum(e => e.CompletedTasks)} out of {employeeComparisons.Sum(e => e.TotalTasks)}");
        
        // Generate recommendations
        var recommendations = new List<string>();
        if (comparisonResult.ScoreDifference > 20)
        {
            recommendations.Add($"🎯 Focus on improving {worstPerformer.Name}'s performance to match {bestPerformer.Name}");
            recommendations.Add("👥 Consider pairing employees for knowledge sharing");
        }
        if (employeeComparisons.Any(e => e.OnTimeRate < 70))
        {
            recommendations.Add("⏰ Implement time management training for employees with low punctuality");
        }
        if (employeeComparisons.Any(e => e.CompletionRate < 70))
        {
            recommendations.Add("📋 Review workload distribution for employees with low completion rates");
        }
        
        return new EmployeeComparisonViewModel
        {
            BranchId = branchId,
            BranchName = branchName,
            SelectedEmployeeIds = selectedEmployeeIds ?? new List<int>(),
            StartDate = startDate,
            EndDate = endDate,
            ComparisonMode = comparisonMode,
            TotalEmployeesCompared = employeeComparisons.Count,
            AverageScore = Math.Round(averageScore, 1),
            HighestScore = Math.Round(highestScore, 1),
            LowestScore = Math.Round(lowestScore, 1),
            Employees = sortedEmployees,
            ComparisonResult = comparisonResult,
            KeyInsights = insights,
            Recommendations = recommendations
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error executing employee comparison report");
        return new EmployeeComparisonViewModel
        {
            BranchId = branchId,
            SelectedEmployeeIds = selectedEmployeeIds ?? new List<int>(),
            StartDate = startDate,
            EndDate = endDate,
            ComparisonMode = comparisonMode,
            TotalEmployeesCompared = 0,
            Employees = new List<EmployeeComparisonItem>(),
            KeyInsights = new List<string> { $"Error generating report: {ex.Message}" },
            Recommendations = new List<string>()
        };
    }
}

private async Task<List<DailyTaskComparison>> GetEmployeeDailyTasksAsync(int employeeId, DateTime startDate, DateTime endDate)
{
    try
    {
        var assignments = await _context.BranchAssignments
            .Where(ba => ba.EmployeeId == employeeId)
            .Select(ba => ba.BranchId)
            .ToListAsync();
        
        var dailyTasks = await _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .Where(dt => assignments.Contains(dt.BranchId) && 
                        dt.TaskDate >= startDate && 
                        dt.TaskDate <= endDate)
            .OrderByDescending(dt => dt.TaskDate)
            .Take(20)
            .AsNoTracking()
            .ToListAsync();
        
        // Pre-fetch holidays once instead of per row
        var holidays = await _holidayService.GetAllHolidaysAsync();

        var result = new List<DailyTaskComparison>();
        foreach (var dt in dailyTasks)
        {
            var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dt, holidays);
            result.Add(new DailyTaskComparison
            {
                Date = _timezoneService.ConvertToLocalTime(dt.TaskDate),
                TaskName = dt.TaskItem?.Name ?? "Unknown",
                IsCompleted = dt.IsCompleted,
                IsOnTime = delayInfo.IsOnTime,
                Status = dt.IsCompleted ? (delayInfo.IsOnTime ? "On Time" : "Late") : "Pending"
            });
        }
        
        return result;
    }
    catch
    {
        return new List<DailyTaskComparison>();
    }
}

private List<string> GetCommonItems(List<string> itemsList, int topCount)
{
    var allItems = itemsList
        .Where(s => !string.IsNullOrEmpty(s))
        .SelectMany(s => s.Split(','))
        .Select(s => s.Trim())
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();
    
    return allItems
        .GroupBy(s => s)
        .OrderByDescending(g => g.Count())
        .Take(topCount)
        .Select(g => g.Key)
        .ToList();
}

private List<string> GetUniqueItems(List<string> itemsList, string employeeName)
{
    // This is simplified - would need proper implementation
    return new List<string>();
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

            await _auditService.LogAsync("Update", "Report", report.Id, $"Updated report: {report.Name}");
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

            await _auditService.LogAsync("Delete", "Report", report.Id, $"Deleted report: {report.Name}");
            _logger.LogInformation("Report deleted: {ReportName}", report.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting report {ReportId}", id);
            return false;
        }
    }

    public async Task<Report?> GetReportByIdAsync(int id) => await _context.Reports.FindAsync(id);

    public async Task<List<Report>> GetUserReportsAsync(string userId)
        => await _context.Reports.Where(r => r.CreatedBy == userId || r.IsPublic).OrderByDescending(r => r.CreatedAt).ToListAsync();

    public async Task<List<Report>> GetPublicReportsAsync()
        => await _context.Reports.Where(r => r.IsPublic && r.IsActive).OrderByDescending(r => r.CreatedAt).ToListAsync();

    public async Task<List<Report>> GetReportsByTypeAsync(string reportType)
        => await _context.Reports.Where(r => r.ReportType == reportType && r.IsActive).OrderByDescending(r => r.CreatedAt).ToListAsync();

    public async Task<List<Report>> GetReportsByCategoryAsync(string category)
        => await _context.Reports.Where(r => r.Category == category && r.IsActive).OrderByDescending(r => r.CreatedAt).ToListAsync();

    public async Task<List<Report>> GetScheduledReportsAsync()
        => await _context.Reports.Where(r => r.IsScheduled && r.IsActive).ToListAsync();

    public async Task<List<Report>> GetTemplatesAsync()
        => await _context.Reports.Where(r => r.IsPublic && r.IsActive).OrderBy(r => r.Category).ThenBy(r => r.Name).ToListAsync();

    #endregion

    #region Report Execution

    public async Task<object> ExecuteReportAsync(int reportId, Dictionary<string, object>? parameters = null)
    {
        try
        {
            var report = await _context.Reports.FindAsync(reportId);
            if (report == null) throw new Exception($"Report {reportId} not found");

            report.LastRunAt = DateTime.UtcNow;
            report.RunCount = (report.RunCount ?? 0) + 1;

            var todayLocal = _timezoneService.GetCurrentLocalTime();
            
            var startDateParam = parameters?.ContainsKey("startDate") == true
                ? DateTime.Parse(parameters["startDate"]?.ToString() ?? todayLocal.AddMonths(-1).ToString())
                : report.StartDate ?? todayLocal.AddMonths(-1);

            var endDateParam = parameters?.ContainsKey("endDate") == true
                ? DateTime.Parse(parameters["endDate"]?.ToString() ?? todayLocal.ToString())
                : report.EndDate ?? todayLocal;

            // Convert to UTC for database queries
            var startDate = _timezoneService.GetStartOfDayLocal(startDateParam);
            var endDate = _timezoneService.GetEndOfDayLocal(endDateParam);

            object result = report.ReportType?.ToLower() switch
            {
                "employee" => await ExecuteEmployeeReportAsync(
                    parameters?.ContainsKey("employeeId") == true ? Convert.ToInt32(parameters["employeeId"]) : 0,
                    startDate, endDate),
                "branch" => await ExecuteBranchReportAsync(
                    parameters?.ContainsKey("branchId") == true ? Convert.ToInt32(parameters["branchId"]) : 0,
                    startDate, endDate),

                "task" => await ExecuteTaskReportAsync(
                    parameters?.ContainsKey("taskId") == true ? Convert.ToInt32(parameters["taskId"]) : 0,
                    startDate, endDate),
                "audit" => await ExecuteAuditReportAsync(startDateParam, endDateParam,
                    parameters?.ContainsKey("action") == true ? parameters["action"]?.ToString() : null,
                    parameters?.ContainsKey("entityType") == true ? parameters["entityType"]?.ToString() : null),
                _ => await ExecuteCustomReportAsync(new CustomReportRequest
                {
                    StartDate = startDateParam,
                    EndDate = endDateParam,
                    BranchIds = parameters?.ContainsKey("branchIds") == true
                        ? JsonSerializer.Deserialize<List<int>>(parameters["branchIds"]?.ToString() ?? "[]") ?? new List<int>()
                        : new List<int>(),
                    TaskIds = parameters?.ContainsKey("taskIds") == true
                        ? JsonSerializer.Deserialize<List<int>>(parameters["taskIds"]?.ToString() ?? "[]") ?? new List<int>()
                        : new List<int>(),
                    EmployeeIds = parameters?.ContainsKey("employeeIds") == true
                        ? JsonSerializer.Deserialize<List<int>>(parameters["employeeIds"]?.ToString() ?? "[]") ?? new List<int>()
                        : new List<int>(),
                    DepartmentIds = parameters?.ContainsKey("departmentIds") == true
                        ? JsonSerializer.Deserialize<List<int>>(parameters["departmentIds"]?.ToString() ?? "[]") ?? new List<int>()
                        : new List<int>(),
                    Status = parameters?.ContainsKey("status") == true ? parameters["status"]?.ToString() ?? "All" : "All"
                })
            };

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
                    StartDate = _timezoneService.ConvertToLocalTime(startDate), 
                    EndDate = _timezoneService.ConvertToLocalTime(endDate) 
                };

            var stats = await _taskCalculationService.GetEmployeeTaskStatisticsAsync(employeeId, startDate, endDate);

            var branchAssignments = await _context.BranchAssignments
                .Include(ba => ba.Branch)
                .Where(ba => ba.EmployeeId == employeeId)
                .OrderBy(ba => ba.StartDate)
                .ToListAsync();

            var assignedBranchIds = branchAssignments.Select(ba => ba.BranchId).Distinct().ToList();

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Include(dt => dt.Branch)
                .Where(dt => assignedBranchIds.Contains(dt.BranchId) &&
                             dt.TaskDate >= startDate &&
                             dt.TaskDate <= endDate)
                .OrderBy(dt => dt.TaskDate)
                .ToListAsync();

            var dailyBreakdown = new List<DailyPerformance>();
            var taskBreakdown = new Dictionary<string, TaskPerformanceStats>();
            var branchBreakdown = new Dictionary<string, BranchStatViewModel>();
            var todayLocal = _timezoneService.GetCurrentLocalTime().Date;

            // Pre-fetch holidays once (cached) to avoid N async calls inside the loop
            var holidays = await _holidayService.GetAllHolidaysAsync();

            foreach (var dt in dailyTasks)
            {
                if (dt.TaskItem == null) continue;

                // Compute deadline in-memory using the cached holiday list
                var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dt, holidays);
                var taskName = dt.TaskItem.Name;
                var localTaskDate = _timezoneService.ConvertToLocalTime(dt.TaskDate);

                dailyBreakdown.Add(new DailyPerformance
                {
                    Date = localTaskDate,
                    BranchName = dt.Branch?.Name ?? "Unknown",
                    TaskName = taskName,
                    TaskType = GetTaskTypeName(dt.TaskItem.ExecutionType),
                    Deadline = dt.TaskItem.Deadline.ToString(@"hh\:mm"),
                    CompletionTime = dt.CompletedAt.HasValue ? _timezoneService.ConvertToLocalTime(dt.CompletedAt.Value).ToString("HH:mm") : "",
                    CompletionDateTime = dt.CompletedAt.HasValue ? _timezoneService.ConvertToLocalTime(dt.CompletedAt.Value) : null,
                    DeadlineDateTime = delayInfo.Deadline ?? DateTime.MinValue,
                    Status = dt.IsCompleted ? "Completed" : "Pending",
                    IsOnTime = delayInfo.IsOnTime,
                    AssignedTo = employee.Name,
                    AdjustmentMinutes = dt.AdjustmentMinutes,
                    AdjustmentReason = dt.AdjustmentReason ?? "",
                    Score = dt.IsCompleted ? (delayInfo.IsOnTime ? 100 : 50) : 0,
                    DelayText = delayInfo.DelayText
                });

                if (!taskBreakdown.ContainsKey(taskName))
                    taskBreakdown[taskName] = new TaskPerformanceStats();

                taskBreakdown[taskName].Total++;
                if (dt.IsCompleted)
                {
                    taskBreakdown[taskName].Completed++;
                    if (delayInfo.IsOnTime) taskBreakdown[taskName].OnTime++;
                    else taskBreakdown[taskName].Late++;
                }
                else
                {
                    taskBreakdown[taskName].Pending++;
                }

                var branchName = dt.Branch?.Name ?? "Unknown";
                if (!branchBreakdown.ContainsKey(branchName))
                {
                    var assignment = branchAssignments.FirstOrDefault(ba => ba.BranchId == dt.BranchId);
                    branchBreakdown[branchName] = new BranchStatViewModel
                    {
                        BranchName = branchName,
                        StartDate = assignment?.StartDate,
                        EndDate = assignment?.EndDate,
                        IsActive = assignment?.EndDate == null || _timezoneService.ConvertToLocalTime(assignment.EndDate.Value).Date >= todayLocal,
                        AssignmentPeriod = assignment != null 
                            ? (assignment.EndDate == null 
                                ? $"From {_timezoneService.ConvertToLocalTime(assignment.StartDate):MMM dd, yyyy} → Present"
                                : $"From {_timezoneService.ConvertToLocalTime(assignment.StartDate):MMM dd, yyyy} to {_timezoneService.ConvertToLocalTime(assignment.EndDate.Value):MMM dd, yyyy}")
                            : "Unknown"
                    };
                }

                branchBreakdown[branchName].TotalTasks++;
                if (dt.IsCompleted) branchBreakdown[branchName].CompletedTasks++;
            }

            foreach (var task in taskBreakdown.Values)
            {
                task.CompletionRate = task.Total > 0 ? Math.Round((double)task.Completed / task.Total * 100, 1) : 0;
                task.OnTimeRate = task.Completed > 0 ? Math.Round((double)task.OnTime / task.Completed * 100, 1) : 0;
            }

            foreach (var branch in branchBreakdown.Values)
            {
                branch.CompletionRate = branch.TotalTasks > 0 
                    ? Math.Round((double)branch.CompletedTasks / branch.TotalTasks * 100, 1) 
                    : 0;
            }

            var localStartDate = _timezoneService.ConvertToLocalTime(startDate);
            var localEndDate = _timezoneService.ConvertToLocalTime(endDate);

            var insights = await GenerateInsightsAsync(new EmployeePerformanceViewModel
            {
                OverallScore = stats.WeightedScore,
                CompletionRate = stats.CompletionRate,
                OnTimeRate = stats.OnTimeRate,
                PendingTasks = stats.PendingTasks,
                LateTasks = stats.LateTasks
            });

            var strengths = new List<string>();
            var weaknesses = new List<string>();
            var recommendations = await GenerateRecommendationsAsync(new EmployeePerformanceViewModel
            {
                OverallScore = stats.WeightedScore,
                CompletionRate = stats.CompletionRate,
                OnTimeRate = stats.OnTimeRate,
                PendingTasks = stats.PendingTasks,
                LateTasks = stats.LateTasks,
                DailyBreakdown = dailyBreakdown
            });

            if (stats.WeightedScore >= 90)
                strengths.Add("🏆 Exceptional performer - consistently exceeds expectations");
            else if (stats.WeightedScore >= 75)
                strengths.Add("📈 Strong performer with good consistency");
            else if (stats.WeightedScore >= 60)
                strengths.Add("📊 Meets basic requirements consistently");
            else
                weaknesses.Add("⚠️ Performance below expectations");

            if (stats.CompletionRate >= 90)
                strengths.Add($"✅ Excellent completion rate ({stats.CompletionRate}%)");
            else if (stats.CompletionRate < 70 && stats.CompletionRate > 0)
                weaknesses.Add($"❗ Low completion rate ({stats.CompletionRate}%)");

            if (stats.OnTimeRate >= 90)
                strengths.Add($"⏰ Outstanding punctuality ({stats.OnTimeRate}%)");
            else if (stats.OnTimeRate < 70 && stats.OnTimeRate > 0)
                weaknesses.Add($"⏰ Frequent late submissions ({stats.OnTimeRate}% on time)");

            var dailyTrendDates = dailyBreakdown
                .GroupBy(d => d.Date.Date)
                .Select(g => g.Key.ToString("MMM dd"))
                .ToList();

            var dailyCompletionRates = dailyBreakdown
                .GroupBy(d => d.Date.Date)
                .Select(g => Math.Round((double)g.Count(x => x.Status == "Completed") / g.Count() * 100, 1))
                .ToList();

            var dailyOnTimeRates = dailyBreakdown
                .Where(d => d.Status == "Completed")
                .GroupBy(d => d.Date.Date)
                .Select(g => Math.Round((double)g.Count(x => x.IsOnTime) / g.Count() * 100, 1))
                .ToList();

            var taskTypeLabels = dailyBreakdown
                .Select(d => d.TaskType)
                .Distinct()
                .ToList();

            var taskTypeValues = taskTypeLabels
                .Select(type => dailyBreakdown.Count(d => d.TaskType == type))
                .ToList();

            // Offloading Razor view logic to Backend
            var highPriorityTasks = taskBreakdown.Where(t => t.Value.Pending >= 3 || t.Value.Late >= 3 || t.Value.CompletionRate < 40).ToList();
            var needsImprovementTasks = taskBreakdown.Where(t => t.Value.CompletionRate >= 40 && t.Value.CompletionRate < 70).ToList();
            var strongTasks = taskBreakdown.Where(t => t.Value.CompletionRate >= 90 && t.Value.OnTimeRate >= 85).ToList();

            var pendingTasksList = dailyBreakdown.Where(d => d.Status != "Completed")
                .GroupBy(t => t.TaskName).Select(g => new GroupedTaskPerformance {
                    TaskName = g.Key, Count = g.Count(),
                    Dates = g.Select(t => t.Date.ToString("MMM dd")).Distinct().OrderBy(d => d).ToList(),
                    DateObjects = g.Select(t => t.Date).Distinct().OrderBy(d => d).ToList(),
                    Details = g.OrderByDescending(t => t.Date).ToList()
                }).OrderByDescending(t => t.Count).ToList();

            var lateTasksList = dailyBreakdown.Where(d => d.Status == "Completed" && !d.IsOnTime)
                .GroupBy(t => t.TaskName).Select(g => new GroupedTaskPerformance {
                    TaskName = g.Key, Count = g.Count(),
                    Dates = g.Select(t => t.Date.ToString("MMM dd")).Distinct().OrderBy(d => d).ToList(),
                    DateObjects = g.Select(t => t.Date).Distinct().OrderBy(d => d).ToList(),
                    Details = g.OrderByDescending(t => t.Date).ToList()
                }).OrderByDescending(t => t.Count).ToList();

            return new EmployeePerformanceViewModel
            {
                EmployeeId = employeeId,
                EmployeeName = employee.Name,
                EmployeeIdNumber = employee.EmployeeId ?? employee.Id.ToString(),
                Department = employee.Department?.Name ?? "N/A",
                Position = employee.Position ?? "N/A",
                StartDate = localStartDate,
                EndDate = localEndDate,
                TotalTasks = stats.TotalTasks,
                CompletedTasks = stats.CompletedTasks,
                PendingTasks = stats.PendingTasks,
                OnTimeTasks = stats.OnTimeTasks,
                LateTasks = stats.LateTasks,
                CompletionRate = stats.CompletionRate,
                OnTimeRate = stats.OnTimeRate,
                OverallScore = stats.WeightedScore,
                TaskBreakdown = taskBreakdown,
                DailyBreakdown = dailyBreakdown,
                BranchBreakdown = branchBreakdown,
                Insights = insights,
                Strengths = strengths,
                Weaknesses = weaknesses,
                Recommendations = recommendations,
                DailyTrendDates = dailyTrendDates,
                DailyCompletionRates = dailyCompletionRates,
                DailyOnTimeRates = dailyOnTimeRates,
                TaskTypeLabels = taskTypeLabels,
                TaskTypeValues = taskTypeValues,
                AverageCompletionTimes = new List<double>(),
                HighPriorityTasks = highPriorityTasks,
                NeedsImprovementTasks = needsImprovementTasks,
                StrongTasks = strongTasks,
                HasStruggleTasks = highPriorityTasks.Any() || needsImprovementTasks.Any(),
                PendingTasksList = pendingTasksList,
                LateTasksList = lateTasksList
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing employee report for {EmployeeId}", employeeId);
            throw;
        }
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
                    StartDate = _timezoneService.ConvertToLocalTime(startDate), 
                    EndDate = _timezoneService.ConvertToLocalTime(endDate) 
                };

            var stats = await _taskCalculationService.GetBranchTaskStatisticsAsync(branchId, startDate, endDate);

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta!.Employee)
                .Where(dt => dt.BranchId == branchId && 
                             dt.TaskDate >= startDate && 
                             dt.TaskDate <= endDate)
                .OrderBy(dt => dt.TaskDate)
                .ToListAsync();

            var dailyBreakdown = new List<BranchDailyTaskViewModel>();
            var taskBreakdown = new Dictionary<string, BranchTaskStats>();

            // Pre-fetch holidays once (cached) so we don't await per row
            var holidays = await _holidayService.GetAllHolidaysAsync();

            foreach (var dt in dailyTasks)
            {
                var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dt, holidays);
                
                dailyBreakdown.Add(new BranchDailyTaskViewModel
                {
                    Date = _timezoneService.ConvertToLocalTime(dt.TaskDate),
                    TaskName = dt.TaskItem?.Name ?? "Unknown",
                    Status = dt.IsCompleted ? "Completed" : "Pending",
                    CompletionTime = dt.CompletedAt.HasValue ? _timezoneService.ConvertToLocalTime(dt.CompletedAt.Value) : null,
                    Deadline = delayInfo.Deadline ?? DateTime.MinValue,
                    IsOnTime = delayInfo.IsOnTime,
                    AssignedTo = dt.TaskAssignment?.Employee?.Name ?? "Unassigned",
                    AdjustmentMinutes = dt.AdjustmentMinutes
                });

                if (dt.TaskItem != null)
                {
                    var taskName = dt.TaskItem.Name;
                    if (!taskBreakdown.ContainsKey(taskName))
                        taskBreakdown[taskName] = new BranchTaskStats();
                    
                    taskBreakdown[taskName].Total++;
                    if (dt.IsCompleted)
                    {
                        taskBreakdown[taskName].Completed++;
                        if (delayInfo.IsOnTime) taskBreakdown[taskName].OnTime++;
                        else taskBreakdown[taskName].Late++;
                    }
                    else
                    {
                        taskBreakdown[taskName].Pending++;
                    }
                }
            }

            foreach (var task in taskBreakdown.Values)
            {
                task.CompletionRate = task.Total > 0 ? Math.Round((double)task.Completed / task.Total * 100, 1) : 0;
                task.OnTimeRate = task.Completed > 0 ? Math.Round((double)task.OnTime / task.Completed * 100, 1) : 0;
            }

            // Batch: load employees and compute their stats concurrently
            var employees = await _context.Employees
                .Where(e => e.BranchAssignments.Any(ba => ba.BranchId == branchId &&
                            (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)))
                .AsNoTracking()
                .ToListAsync();

            var topPerformers = new List<EmployeePerformanceSummary>();
            var needsImprovement = new List<EmployeePerformanceSummary>();

            // Run all employee stat fetches concurrently instead of sequentially
            var empStatTasks = employees.Select(emp =>
                _taskCalculationService.GetEmployeeTaskStatisticsAsync(emp.Id, startDate, endDate)
                    .ContinueWith(t => (emp, stats: t.Result))).ToList();
            var empResults = await Task.WhenAll(empStatTasks);

            foreach (var (emp, empStats) in empResults)
            {
                var summary = new EmployeePerformanceSummary
                {
                    EmployeeId = emp.Id,
                    EmployeeName = emp.Name,
                    TasksCompleted = empStats.CompletedTasks,
                    OnTimeRate = empStats.OnTimeRate,
                    Score = empStats.WeightedScore
                };

                if (empStats.WeightedScore >= 80)
                    topPerformers.Add(summary);
                else if (empStats.WeightedScore < 60)
                    needsImprovement.Add(summary);
            }

            var insights = new List<string>();
            if (stats.CompletionRate >= 90)
                insights.Add($"🏆 Excellent completion rate of {stats.CompletionRate}%");
            else if (stats.CompletionRate < 70)
                insights.Add($"⚠️ Low completion rate of {stats.CompletionRate}% - needs attention");

            if (stats.OnTimeRate >= 90)
                insights.Add($"⏰ Outstanding punctuality with {stats.OnTimeRate}% on-time rate");
            else if (stats.OnTimeRate < 70)
                insights.Add($"⌛ Punctuality needs improvement ({stats.OnTimeRate}% on time)");

            if (stats.EmployeeScores.Count > 0)
            {
                var avgEmployeeScore = stats.EmployeeScores.Values.Average();
                insights.Add($"👥 Average employee performance score: {avgEmployeeScore:F1}%");
            }

            var localStartDate = _timezoneService.ConvertToLocalTime(startDate);
            var localEndDate = _timezoneService.ConvertToLocalTime(endDate);

            return new BranchPerformanceViewModel
            {
                BranchId = branchId,
                BranchName = branch.Name,
                BranchCode = branch.Code ?? "N/A",
                Department = branch.Department?.Name ?? "N/A",
                Location = branch.Address ?? "N/A",
                StartDate = localStartDate,
                EndDate = localEndDate,
                TotalTasks = stats.TotalTasks,
                CompletedTasks = stats.CompletedTasks,
                PendingTasks = stats.TotalTasks - stats.CompletedTasks,
                OnTimeTasks = stats.OnTimeTasks,
                LateTasks = stats.LateTasks,
                CompletionRate = stats.CompletionRate,
                OnTimeRate = stats.OnTimeRate,
                OverallScore = stats.WeightedScore,
                TotalEmployees = employees.Count,
                ActiveEmployees = employees.Count,
                EmployeeScores = stats.EmployeeScores.ToDictionary(
                    k => employees.FirstOrDefault(e => e.Id == k.Key)?.Name ?? k.Key.ToString(), 
                    v => v.Value),
                TaskBreakdown = taskBreakdown,
                DailyBreakdown = dailyBreakdown,
                TopPerformers = topPerformers.OrderByDescending(p => p.Score).Take(5).ToList(),
                NeedsImprovement = needsImprovement.OrderBy(p => p.Score).Take(5).ToList(),
                Insights = insights
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing branch report for {BranchId}", branchId);
            throw;
        }
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
                    StartDate = _timezoneService.ConvertToLocalTime(startDate), 
                    EndDate = _timezoneService.ConvertToLocalTime(endDate) 
                };

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.Branch)
                .Include(dt => dt.TaskAssignment)
                    .ThenInclude(ta => ta!.Employee)
                .Where(dt => dt.TaskItemId == taskId && 
                             dt.TaskDate >= startDate && 
                             dt.TaskDate <= endDate)
                .ToListAsync();

            var report = new TaskCompletionViewModel
            {
                TaskId = taskId,
                TaskName = task.Name,
                Deadline = task.Deadline,
                IsSameDay = task.IsSameDay,
                StartDate = _timezoneService.ConvertToLocalTime(startDate),
                EndDate = _timezoneService.ConvertToLocalTime(endDate),
                BranchStats = new Dictionary<string, BranchTaskStat>(),
                EmployeeStats = new Dictionary<string, EmployeeTaskStat>(),
                DailyCompletions = new Dictionary<string, int>()
            };

            var completionTimes = new List<TimeSpan>();

            foreach (var dt in dailyTasks)
            {
                var delayInfo = await _taskCalculationService.GetHolidayAdjustedDelayInfoAsync(dt);
                
                report.TotalAssignments++;
                
                if (dt.IsCompleted)
                {
                    report.Completed++;
                    if (delayInfo.IsOnTime) report.OnTime++;
                    else report.Late++;
                    
                    if (dt.CompletedAt.HasValue)
                        completionTimes.Add(dt.CompletedAt.Value.TimeOfDay);
                }
                else
                {
                    report.Pending++;
                }

                var branchName = dt.Branch?.Name ?? "Unknown";
                if (!report.BranchStats.ContainsKey(branchName))
                    report.BranchStats[branchName] = new BranchTaskStat { BranchName = branchName };
                
                report.BranchStats[branchName].Total++;
                if (dt.IsCompleted) report.BranchStats[branchName].Completed++;

                if (dt.TaskAssignment?.Employee != null)
                {
                    var empName = dt.TaskAssignment.Employee.Name;
                    if (!report.EmployeeStats.ContainsKey(empName))
                        report.EmployeeStats[empName] = new EmployeeTaskStat { EmployeeName = empName };
                    
                    report.EmployeeStats[empName].Total++;
                    if (dt.IsCompleted) report.EmployeeStats[empName].Completed++;
                }

                var dateKey = dt.TaskDate.ToString("yyyy-MM-dd");
                if (!report.DailyCompletions.ContainsKey(dateKey))
                    report.DailyCompletions[dateKey] = 0;
                if (dt.IsCompleted) report.DailyCompletions[dateKey]++;
            }

            report.CompletionRate = report.TotalAssignments > 0 
                ? Math.Round((double)report.Completed / report.TotalAssignments * 100, 1) 
                : 0;
            
            report.OnTimeRate = report.Completed > 0 
                ? Math.Round((double)report.OnTime / report.Completed * 100, 1) 
                : 0;

            foreach (var branch in report.BranchStats.Values)
                branch.CompletionRate = branch.Total > 0 
                    ? Math.Round((double)branch.Completed / branch.Total * 100, 1) 
                    : 0;
            
            foreach (var emp in report.EmployeeStats.Values)
                emp.CompletionRate = emp.Total > 0 
                    ? Math.Round((double)emp.Completed / emp.Total * 100, 1) 
                    : 0;

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
        var query = _context.AuditLogs.Where(a => a.Timestamp.Date >= startDate.Date && a.Timestamp.Date <= endDate.Date);
        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);

        var logs = await query.OrderByDescending(a => a.Timestamp).ToListAsync();

        return new AuditLogReportViewModel
        {
            StartDate = startDate,
            EndDate = endDate,
            TotalEvents = logs.Count,
            EventsByAction = logs.GroupBy(l => l.Action).ToDictionary(g => g.Key, g => g.Count()),
            EventsByEntity = logs.GroupBy(l => l.EntityType).ToDictionary(g => g.Key, g => g.Count()),
            EventsByUser = logs.GroupBy(l => l.UserName).ToDictionary(g => g.Key, g => g.Count()),
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
            EventsByDay = logs.GroupBy(l => l.Timestamp.Date.ToString("yyyy-MM-dd")).ToDictionary(g => g.Key, g => g.Count()),
            EventsByHour = logs.GroupBy(l => l.Timestamp.Hour.ToString("D2")).ToDictionary(g => g.Key, g => g.Count())
        };
    }

    public async Task<List<Dictionary<string, object>>> ExecuteCustomReportAsync(CustomReportRequest request)
    {
        var utcStartDate = _timezoneService.GetStartOfDayLocal(request.StartDate);
        var utcEndDate = _timezoneService.GetEndOfDayLocal(request.EndDate);
        
        var query = _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .Include(dt => dt.Branch).ThenInclude(b => b!.Department)
            .Include(dt => dt.TaskAssignment).ThenInclude(ta => ta!.Employee)
            .Where(dt => dt.TaskDate >= utcStartDate && dt.TaskDate <= utcEndDate);

        if (request.BranchIds?.Any() == true) query = query.Where(dt => request.BranchIds.Contains(dt.BranchId));
        if (request.TaskIds?.Any() == true) query = query.Where(dt => request.TaskIds.Contains(dt.TaskItemId));
        if (request.EmployeeIds?.Any() == true) query = query.Where(dt => dt.TaskAssignment != null && request.EmployeeIds.Contains(dt.TaskAssignment.EmployeeId));
        if (request.DepartmentIds?.Any() == true) query = query.Where(dt => dt.Branch != null && dt.Branch.DepartmentId != null && request.DepartmentIds.Contains(dt.Branch.DepartmentId.Value));
        if (!string.IsNullOrEmpty(request.Status) && request.Status != "All") query = query.Where(dt => dt.IsCompleted == (request.Status == "Completed"));

        var data = await query.ToListAsync();
        
        return data.Select(item => new Dictionary<string, object>
        {
            ["Date"] = _timezoneService.ConvertToLocalTime(item.TaskDate).ToString("yyyy-MM-dd"),
            ["Branch"] = item.Branch?.Name ?? "Unknown",
            ["Department"] = item.Branch?.Department?.Name ?? "Unknown",
            ["Task"] = item.TaskItem?.Name ?? "Unknown",
            ["Status"] = item.IsCompleted ? "Completed" : "Pending",
            ["CompletionTime"] = item.CompletedAt.HasValue ? _timezoneService.ConvertToLocalTime(item.CompletedAt.Value).ToString("yyyy-MM-dd HH:mm:ss") : "",
            ["AssignedTo"] = item.TaskAssignment?.Employee?.Name ?? "Unassigned",
            ["Adjustment"] = item.AdjustmentMinutes ?? 0,
            ["AdjustmentReason"] = item.AdjustmentReason ?? "",
            ["BulkUpdated"] = item.IsBulkUpdated ? "Yes" : "No"
        }).ToList();
    }

    public async Task<List<EmployeeRankingViewModel>> GetEmployeeRankingAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var actualStartDateLocal = startDate ?? _timezoneService.GetCurrentLocalTime().AddMonths(-1);
            var actualEndDateLocal = endDate ?? _timezoneService.GetCurrentLocalTime();
            
            var utcStartDate = _timezoneService.GetStartOfDayLocal(actualStartDateLocal);
            var utcEndDate = _timezoneService.GetEndOfDayLocal(actualEndDateLocal);

            var employees = await _context.Employees
                .Include(e => e.Department)
                .Where(e => e.IsActive)
                .ToListAsync();

            var rankingList = new List<EmployeeRankingViewModel>();

            foreach (var emp in employees)
            {
                var stats = await _taskCalculationService.GetEmployeeTaskStatisticsAsync(emp.Id, utcStartDate, utcEndDate);

                rankingList.Add(new EmployeeRankingViewModel
                {
                    Id = emp.Id,
                    Name = emp.Name,
                    EmployeeId = emp.EmployeeId,
                    Position = emp.Position ?? "N/A",
                    Department = emp.Department?.Name ?? "Unassigned",
                    PerformanceScore = (int)Math.Round(stats.WeightedScore),
                    CompletionRate = (int)Math.Round(stats.CompletionRate),
                    OnTimeRate = (int)Math.Round(stats.OnTimeRate),
                    TotalTasks = stats.TotalTasks,
                    CompletedTasks = stats.CompletedTasks,
                    OnTimeTasks = stats.OnTimeTasks,
                    LateTasks = stats.LateTasks,
                    IsActive = emp.IsActive,
                    Initials = GetInitials(emp.Name)
                });
            }

            rankingList = rankingList.OrderByDescending(e => e.PerformanceScore).ToList();
            for (int i = 0; i < rankingList.Count; i++) rankingList[i].Rank = i + 1;

            return rankingList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employee ranking");
            return new List<EmployeeRankingViewModel>();
        }
    }

    #endregion

    #region Report Export

    public async Task<byte[]> ExportToExcelAsync(object data, string reportName)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");

        if (data is IEnumerable<Dictionary<string, object>> listData && listData.Any())
        {
            var headers = listData.First().Keys.ToList();
            for (int i = 0; i < headers.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
                worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                worksheet.Cells[1, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }

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

        return package.GetAsByteArray();
    }

    public async Task<byte[]> ExportToCsvAsync(object data, string reportName)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream);

        if (data is IEnumerable<Dictionary<string, object>> listData && listData.Any())
        {
            var headers = listData.First().Keys.ToList();
            await writer.WriteLineAsync(string.Join(",", headers));

            foreach (var item in listData)
            {
                var values = headers.Select(h => $"\"{(item.ContainsKey(h) ? item[h]?.ToString()?.Replace("\"", "\"\"") : "")}\"");
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
                await writer.WriteLineAsync(string.Join(",", props.Select(p => $"\"{p.GetValue(item)?.ToString()?.Replace("\"", "\"\"")}\"")));
            }
        }

        await writer.FlushAsync();
        return memoryStream.ToArray();
    }

    public async Task<byte[]> ExportToPdfAsync(object data, string reportName)
    {
        try
        {
            var exportData = data as List<Dictionary<string, object>>;
            
            QuestPDF.Settings.License = LicenseType.Community;
            
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComposeHeader(c, reportName));
                    page.Content().Element(c => ComposeContent(c, exportData));
                    page.Footer().Element(ComposeFooter);
                });
            });

            return document.GeneratePdf();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for {ReportName}", reportName);
            return Array.Empty<byte>();
        }
    }

    private void ComposeHeader(IContainer container, string reportName)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("TaskTracker Report")
                    .Bold().FontSize(16).FontColor(Colors.Blue.Darken2);
                column.Item().Text($"Report: {reportName}")
                    .FontSize(12).FontColor(Colors.Grey.Darken1);
                column.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}")
                    .FontSize(10).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private void ComposeContent(IContainer container, List<Dictionary<string, object>>? data)
    {
        if (data == null || !data.Any())
        {
            container.Padding(20).Text("No data available").FontSize(12);
            return;
        }

        container.PaddingVertical(10).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                var headers = data.First().Keys.ToList();
                foreach (var _ in headers)
                    columns.RelativeColumn();
            });

            table.Header(header =>
            {
                header.Cell().Background(Colors.Blue.Darken2).Padding(5)
                    .Text(data.First().Keys.First()).Bold().FontColor(Colors.White);
                foreach (var headerKey in data.First().Keys.Skip(1))
                {
                    header.Cell().Background(Colors.Blue.Darken2).Padding(5)
                        .Text(headerKey).Bold().FontColor(Colors.White);
                }
            });

            bool isAlternate = false;
            foreach (var row in data)
            {
                var bgColor = isAlternate ? Colors.Grey.Lighten4 : Colors.White;
                var values = row.Values.ToList();
                
                for (int i = 0; i < values.Count; i++)
                {
                    table.Cell().Background(bgColor).Padding(5)
                        .Text(values[i]?.ToString() ?? "");
                }
                isAlternate = !isAlternate;
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
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
            await _auditService.LogAsync("Schedule", "Report", reportId, $"Scheduled report: {report.Name}");
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
            await _auditService.LogAsync("Unschedule", "Report", reportId, $"Unscheduled report: {report.Name}");
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
        var now = DateTime.UtcNow;
        var dueReports = await _context.Reports
            .Where(r => r.IsScheduled && r.IsActive && r.NextRunDate.HasValue && r.NextRunDate.Value <= now)
            .ToListAsync();

        foreach (var report in dueReports)
        {
            try
            {
                _logger.LogInformation("Processing scheduled report: {ReportName}", report.Name);
                var result = await ExecuteReportAsync(report.Id);

                byte[] fileData = report.ExportFormat?.ToLower() switch
                {
                    "csv" => await ExportToCsvAsync(result, report.Name),
                    "pdf" => await ExportToPdfAsync(result, report.Name),
                    _ => await ExportToExcelAsync(result, report.Name)
                };

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

    private DateTime? CalculateNextRunDate(string cronExpression)
    {
        if (string.IsNullOrEmpty(cronExpression)) return null;
        var now = DateTime.UtcNow;

        return cronExpression switch
        {
            "0 9 * * *" => now.Date.AddHours(9) <= now ? now.Date.AddDays(1).AddHours(9) : now.Date.AddHours(9),
            "0 9 * * 1" => GetNextWeekday(now, DayOfWeek.Monday, 9),
            "0 9 1 * *" => new DateTime(now.Year, now.Month, 1).AddHours(9) <= now 
                ? new DateTime(now.Year, now.Month, 1).AddMonths(1).AddHours(9) 
                : new DateTime(now.Year, now.Month, 1).AddHours(9),
            _ => now.AddDays(1)
        };
    }

    private DateTime GetNextWeekday(DateTime from, DayOfWeek weekday, int hour)
    {
        var daysUntil = ((int)weekday - (int)from.DayOfWeek + 7) % 7;
        var next = from.Date.AddDays(daysUntil == 0 ? 7 : daysUntil).AddHours(hour);
        return next <= from ? next.AddDays(7) : next;
    }

    #endregion

    #region Report Templates

    public async Task<Report> CreateFromTemplateAsync(string templateName, string newName)
    {
        var template = await _context.Reports.FirstOrDefaultAsync(r => r.Name == templateName && r.IsPublic);
        if (template == null) throw new Exception($"Template {templateName} not found");

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

    #endregion

    #region Insights and Recommendations

    public async Task<List<string>> GenerateInsightsAsync(EmployeePerformanceViewModel report)
    {
        var insights = new List<string>();

        if (report.OverallScore >= 90)
            insights.Add("🌟 Excellent performance! Consistently completing tasks on time.");
        else if (report.OverallScore >= 75)
            insights.Add("👍 Good performance. Keep up the good work!");
        else if (report.OverallScore >= 60)
            insights.Add("📊 Average performance. There's room for improvement.");
        else
            insights.Add("⚠️ Needs improvement. Focus on completing tasks on time.");

        if (report.CompletionRate >= 95)
            insights.Add("✅ Outstanding task completion rate!");
        else if (report.CompletionRate < 70 && report.CompletionRate > 0)
            insights.Add("⚠️ Low task completion rate. Review pending tasks.");

        if (report.OnTimeRate >= 90)
            insights.Add("⏰ Excellent punctuality! Most tasks completed on time.");
        else if (report.OnTimeRate < 60 && report.OnTimeRate > 0)
            insights.Add("⌛ Time management needs improvement. Many tasks are late.");

        if (report.PendingTasks > 0)
            insights.Add($"📋 {report.PendingTasks} pending task(s) require attention.");
        
        if (report.LateTasks > 0)
            insights.Add($"⚠️ {report.LateTasks} task(s) were completed late.");

        return await Task.FromResult(insights);
    }

    public async Task<List<string>> GenerateRecommendationsAsync(EmployeePerformanceViewModel report)
    {
        var recommendations = new List<string>();

        if (report.CompletionRate < 80)
            recommendations.Add("🎯 Focus on completing pending tasks before starting new ones");
        
        if (report.OnTimeRate < 70)
            recommendations.Add("⏰ Start tasks earlier in the day to meet deadlines");

        if (report.OverallScore >= 80)
        {
            recommendations.Add("🌟 Consider mentoring other team members");
            recommendations.Add("📈 Continue maintaining high performance standards");
        }
        else if (report.OverallScore >= 60)
        {
            recommendations.Add("📊 Create a daily task schedule and prioritize urgent items");
            recommendations.Add("🎯 Set personal deadlines 15 minutes before actual deadlines");
        }
        else
        {
            recommendations.Add("🎯 Meet with supervisor to create an improvement plan");
            recommendations.Add("📋 Break down tasks into smaller, manageable steps");
            recommendations.Add("⏰ Use reminders and alarms for task deadlines");
        }

        if (report.PendingTasks > 0 && report.DailyBreakdown != null)
        {
            var pendingTasks = report.DailyBreakdown
                .Where(d => d.Status != "Completed")
                .Select(d => d.TaskName)
                .Distinct()
                .Take(3);
            
            if (pendingTasks.Any())
                recommendations.Add($"📋 Prioritize pending tasks: {string.Join(", ", pendingTasks)}");
        }

        if (report.LateTasks > 0 && report.OnTimeRate < 70)
        {
            recommendations.Add("⏰ Review deadlines and time management for tasks with late submissions");
        }

        return await Task.FromResult(recommendations);
    }

    #endregion

    #region Private Helpers

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

    private string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpper();
        return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
    }

    public async Task<ExecutiveSummaryViewModel> GetExecutiveSummaryAsync(DateTime startDate, DateTime endDate)
    {
        var utcStartDate = _timezoneService.GetStartOfDayLocal(startDate);
        var utcEndDate = _timezoneService.GetEndOfDayLocal(endDate);

        var model = new ExecutiveSummaryViewModel
        {
            StartDate = startDate,
            EndDate = endDate
        };

        var allTasks = await _context.DailyTasks
            .Include(dt => dt.Branch)
                .ThenInclude(b => b!.Department)
            .Include(dt => dt.TaskItem)
            .Where(dt => dt.TaskDate >= utcStartDate && dt.TaskDate <= utcEndDate)
            .ToListAsync();

        model.TotalTasksAssigned = allTasks.Count;
        model.TotalTasksCompleted = allTasks.Count(t => t.IsCompleted);
        model.TotalTasksPending = allTasks.Count(t => !t.IsCompleted);
        
        int onTimeCount = 0;
        int lateCount = 0;

        // Group by Day for Trends
        var groupedByDay = allTasks.GroupBy(t => _timezoneService.ConvertToLocalTime(t.TaskDate).Date).OrderBy(g => g.Key).ToList();
        foreach (var dayGroup in groupedByDay)
        {
            var dayTasks = dayGroup.ToList();
            model.DailyTrends.Add(new DailyTrendData
            {
                DateLabel = dayGroup.Key.ToString("MMM dd"),
                TotalTasks = dayTasks.Count,
                CompletedTasks = dayTasks.Count(t => t.IsCompleted),
                CompletionRate = dayTasks.Count > 0 ? Math.Round((double)dayTasks.Count(t => t.IsCompleted) / dayTasks.Count * 100, 1) : 0
            });
        }

        // Branch and Dept mapping
        var branchScores = new Dictionary<int, BranchRiskData>();
        var taskScores = new Dictionary<int, TaskRiskData>();
        var deptScores = new Dictionary<string, DepartmentComparisonData>();

        foreach (var task in allTasks)
        {
            // Simple On-Time Calculation
            if (task.IsCompleted && task.CompletedAt.HasValue && task.TaskItem != null)
            {
                // In a true implementation, we'd use GetHolidayAdjustedDelayInfoAsync, but for memory performance over potentially 10,000s of global tasks we will do a fast calculation:
                var deadline = await _taskCalculationService.CalculateDeadline(task.TaskItem, task.TaskDate);
                var adjustedDeadline = deadline.AddMinutes(task.AdjustmentMinutes ?? 0);
                if (task.CompletedAt.Value <= adjustedDeadline.AddMinutes(5)) onTimeCount++;
                else lateCount++;
            }

            if (task.Branch != null)
            {
                if (!branchScores.ContainsKey(task.BranchId))
                    branchScores[task.BranchId] = new BranchRiskData { BranchId = task.BranchId, BranchName = task.Branch.Name };
                
                var bs = branchScores[task.BranchId];
                if (!task.IsCompleted) bs.PendingTasks++;

                var deptName = task.Branch.Department?.Name ?? "Unassigned";
                if (!deptScores.ContainsKey(deptName))
                    deptScores[deptName] = new DepartmentComparisonData { DepartmentName = deptName };
                
                var ds = deptScores[deptName];
                ds.TotalTasks++;
                if (task.IsCompleted) ds.CompletedTasks++;
            }

            if (task.TaskItem != null)
            {
                if (!taskScores.ContainsKey(task.TaskItemId))
                    taskScores[task.TaskItemId] = new TaskRiskData { TaskId = task.TaskItemId, TaskName = task.TaskItem.Name };
                
                var ts = taskScores[task.TaskItemId];
                if (!task.IsCompleted) ts.PendingTasks++;
            }
        }

        model.TotalTasksOnTime = onTimeCount;
        model.TotalTasksLate = lateCount;

        // Finalize Department scores
        foreach (var dept in deptScores.Values)
        {
            dept.CompletionRate = dept.TotalTasks > 0 ? Math.Round((double)dept.CompletedTasks / dept.TotalTasks * 100, 1) : 0;
            model.DepartmentComparisons.Add(dept);
        }

        // Calculate Branch Completion Rates (Bottom 3 risks)
        var allBranchIds = allTasks.Select(t => t.BranchId).Distinct();
        foreach (var bid in allBranchIds)
        {
            var totalBranch = allTasks.Count(t => t.BranchId == bid);
            var bRisk = branchScores[bid];
            bRisk.CompletionRate = totalBranch > 0 ? Math.Round((double)(totalBranch - bRisk.PendingTasks) / totalBranch * 100, 1) : 0;
        }

        model.BottomBranches = branchScores.Values.OrderBy(b => b.CompletionRate).ThenByDescending(b => b.PendingTasks).Take(5).ToList();

        // Calculate Task Completion Rates (Bottom 3 risks)
        var allTaskIds = allTasks.Select(t => t.TaskItemId).Distinct();
        foreach (var tid in allTaskIds)
        {
            var totalTask = allTasks.Count(t => t.TaskItemId == tid);
            var tRisk = taskScores[tid];
            tRisk.CompletionRate = totalTask > 0 ? Math.Round((double)(totalTask - tRisk.PendingTasks) / totalTask * 100, 1) : 0;
        }

        model.BottomTasks = taskScores.Values.OrderBy(t => t.CompletionRate).ThenByDescending(t => t.PendingTasks).Take(5).ToList();

        return model;
    }

    #endregion
}
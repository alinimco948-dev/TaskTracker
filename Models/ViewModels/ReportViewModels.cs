using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TaskTracker.Models.ViewModels;

// ========== Base ViewModels ==========
public class ReportBaseViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public Dictionary<string, object> Filters { get; set; } = new();
    public List<string> Columns { get; set; } = new();
}

// ========== Chart Data Classes ==========
public class ChartDataViewModel
{
    public List<string> Labels { get; set; } = new();
    public List<ChartDatasetViewModel> Datasets { get; set; } = new();
}

public class ChartDatasetViewModel
{
    public string Label { get; set; } = string.Empty;
    public List<double> Data { get; set; } = new();
    public string BackgroundColor { get; set; } = string.Empty;
    public string BorderColor { get; set; } = string.Empty;
    public bool Fill { get; set; }
}

// ========== Task Performance Stats ==========
public class TaskPerformanceStats
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Pending { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
}

// ========== Branch Stat ViewModel ==========
public class BranchStatViewModel
{
    public string BranchName { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public double CompletionRate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public string AssignmentPeriod { get; set; } = string.Empty;
}

// ========== Daily Performance ==========
public class DailyPerformance
{
    public DateTime Date { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Deadline { get; set; } = string.Empty;
    public string CompletionTime { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? CompletionDateTime { get; set; }
    public DateTime DeadlineDateTime { get; set; }
    public bool IsOnTime { get; set; }
    public string AssignedTo { get; set; } = string.Empty;
    public int? AdjustmentMinutes { get; set; }
    public string AdjustmentReason { get; set; } = string.Empty;
    public int Score { get; set; }
}

// ========== Employee Performance ==========
public class EmployeePerformanceViewModel : ReportBaseViewModel
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeIdNumber { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int? SelectedEmployeeId { get; set; }

    // Summary Stats
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int PendingTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public int LateTasks { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
    public double OverallScore { get; set; }

    // Chart Data
    public List<string> DailyTrendDates { get; set; } = new();
    public List<double> DailyCompletionRates { get; set; } = new();
    public List<double> DailyOnTimeRates { get; set; } = new();
    public List<string> TaskTypeLabels { get; set; } = new();
    public List<int> TaskTypeValues { get; set; } = new();
    public List<double> AverageCompletionTimes { get; set; } = new();

    // Breakdowns
    public Dictionary<string, TaskPerformanceStats> TaskBreakdown { get; set; } = new();
    public List<DailyPerformance> DailyBreakdown { get; set; } = new();
    public Dictionary<string, BranchStatViewModel> BranchBreakdown { get; set; } = new();

    // Insights
    public List<string> Insights { get; set; } = new();
    public List<string> Strengths { get; set; } = new();
    public List<string> Weaknesses { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();

    // Chart Data
    public ChartDataViewModel PerformanceChart { get; set; } = new();
    public ChartDataViewModel TaskDistributionChart { get; set; } = new();
}

// ========== Branch Performance ==========
public class BranchPerformanceViewModel : ReportBaseViewModel
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int? SelectedBranchId { get; set; }

    // Summary Stats
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int PendingTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public int LateTasks { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
    public double OverallScore { get; set; }

    // Employee Stats
    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public Dictionary<string, double> EmployeeScores { get; set; } = new();

    // Breakdowns
    public Dictionary<string, BranchTaskStats> TaskBreakdown { get; set; } = new();
    public List<BranchDailyTaskViewModel> DailyBreakdown { get; set; } = new();
    public Dictionary<string, MonthlyStatViewModel> MonthlyTrends { get; set; } = new();

    // Insights
    public List<string> Insights { get; set; } = new();
    public List<EmployeePerformanceSummary> TopPerformers { get; set; } = new();
    public List<EmployeePerformanceSummary> NeedsImprovement { get; set; } = new();
}

public class BranchTaskStats
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Pending { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
}

public class BranchDailyTaskViewModel
{
    public DateTime Date { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? CompletionTime { get; set; }
    public DateTime Deadline { get; set; }
    public bool IsOnTime { get; set; }
    public string AssignedTo { get; set; } = string.Empty;
    public int? AdjustmentMinutes { get; set; }
}

public class MonthlyStatViewModel
{
    public string Month { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public double CompletionRate { get; set; }
}

public class EmployeePerformanceSummary
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int TasksCompleted { get; set; }
    public double OnTimeRate { get; set; }
    public double Score { get; set; }
}

// ========== Department Performance ==========
public class DepartmentPerformanceViewModel : ReportBaseViewModel
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string DepartmentCode { get; set; } = string.Empty;
    public string Manager { get; set; } = string.Empty;
    public int? SelectedDepartmentId { get; set; }

    // Summary Stats
    public int TotalBranches { get; set; }
    public int ActiveBranches { get; set; }
    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public double OverallCompletionRate { get; set; }
    public double OverallOnTimeRate { get; set; }

    // Branch Performance
    public Dictionary<string, BranchPerformanceSummary> BranchPerformance { get; set; } = new();

    // Task Matrix
    public Dictionary<string, Dictionary<string, TaskStatSummary>> TaskMatrix { get; set; } = new();

    // Rankings
    public List<BranchPerformanceSummary> TopBranches { get; set; } = new();
    public List<BranchPerformanceSummary> BottomBranches { get; set; } = new();

    // Insights
    public List<string> Insights { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

public class BranchPerformanceSummary
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }
    public int EmployeeCount { get; set; }
}

public class TaskStatSummary
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public double CompletionRate { get; set; }
}

// ========== Task Completion ==========
// REMOVED duplicate BranchTaskStat and EmployeeTaskStat - using from ReportSharedViewModels.cs
public class TaskCompletionViewModel : ReportBaseViewModel
{
    public int TaskId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public TimeSpan Deadline { get; set; }
    public bool IsSameDay { get; set; }

    // Summary Stats
    public int TotalAssignments { get; set; }
    public int Completed { get; set; }
    public int Pending { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public double CompletionRate { get; set; }
    public double OnTimeRate { get; set; }

    // Breakdowns - USING shared classes from ReportSharedViewModels.cs
    public Dictionary<string, BranchTaskStat> BranchStats { get; set; } = new();
    public Dictionary<string, EmployeeTaskStat> EmployeeStats { get; set; } = new();
    public Dictionary<string, int> DailyCompletions { get; set; } = new();

    // Timing Stats
    public TimeSpan? AverageCompletionTime { get; set; }
    public TimeSpan? FastestCompletion { get; set; }
    public TimeSpan? SlowestCompletion { get; set; }
}

// ========== Audit Log ==========
public class AuditLogReportViewModel : ReportBaseViewModel
{
    public int TotalEvents { get; set; }
    public Dictionary<string, int> EventsByAction { get; set; } = new();
    public Dictionary<string, int> EventsByEntity { get; set; } = new();
    public Dictionary<string, int> EventsByUser { get; set; } = new();
    public List<AuditLogEntry> Events { get; set; } = new();
    public Dictionary<string, int> EventsByDay { get; set; } = new();
    public Dictionary<string, int> EventsByHour { get; set; } = new();
}

public class AuditLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}

// ========== Custom Report ==========
public class CustomReportRequest
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<int> BranchIds { get; set; } = new();
    public List<int> TaskIds { get; set; } = new();
    public List<int> EmployeeIds { get; set; } = new();
    public List<int> DepartmentIds { get; set; } = new();
    public string Status { get; set; } = "All";
}

public class SaveReportRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public object Configuration { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public object Filters { get; set; } = new();
    public string SortBy { get; set; } = string.Empty;
    public bool IsAscending { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

// ========== Report Index ==========
public class ReportIndexViewModel
{
    public List<ReportSummary> SavedReports { get; set; } = new();
    public List<string> ReportTypes { get; set; } = new();
    public List<string> Categories { get; set; } = new();
}

public class ReportSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool IsScheduled { get; set; }
    public string? ScheduleFrequency { get; set; }
    public bool IsPublic { get; set; }
    public bool IsActive { get; set; }
    public List<string> Tags { get; set; } = new();
    public int RunCount { get; set; }
}
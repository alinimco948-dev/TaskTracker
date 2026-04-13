using System;
using System.Collections.Generic;

namespace TaskTracker.Models.ViewModels
{
    public class EmployeeComparisonViewModel
    {
        // Report Parameters
        public int? BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public List<int> SelectedEmployeeIds { get; set; } = new();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string ComparisonMode { get; set; } = "branch"; // "branch", "selected"
        
        // Summary Statistics
        public int TotalEmployeesCompared { get; set; }
        public double AverageScore { get; set; }
        public double HighestScore { get; set; }
        public double LowestScore { get; set; }
        
        // Employee Comparison Results
        public List<EmployeeComparisonItem> Employees { get; set; } = new();
        
        // Comparison Analysis
        public ComparisonAnalysis ComparisonResult { get; set; } = new();
        
        // Insights & Recommendations
        public List<string> KeyInsights { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }
    
    public class EmployeeComparisonItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public List<string> Branches { get; set; } = new();
        public double PerformanceScore { get; set; }
        public double CompletionRate { get; set; }
        public double OnTimeRate { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int OnTimeTasks { get; set; }
        public int LateTasks { get; set; }
        public int PendingTasks { get; set; }
        public string PerformanceLevel { get; set; } = string.Empty;
        public string Strengths { get; set; } = string.Empty;
        public string Weaknesses { get; set; } = string.Empty;
        public int Rank { get; set; }
        public double VsAverage { get; set; }
        public List<DailyTaskComparison> DailyTasks { get; set; } = new();
    }
    
    public class DailyTaskComparison
    {
        public DateTime Date { get; set; }
        public string TaskName { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool IsOnTime { get; set; }
        public string Status { get; set; } = string.Empty;
    }
    
    public class ComparisonAnalysis
    {
        public EmployeeComparisonItem BestPerformer { get; set; } = new();
        public EmployeeComparisonItem WorstPerformer { get; set; } = new();
        public double ScoreDifference { get; set; }
        public double CompletionGap { get; set; }
        public double OnTimeGap { get; set; }
        public int TasksGap { get; set; }
        public List<string> CommonStrengths { get; set; } = new();
        public List<string> CommonWeaknesses { get; set; } = new();
        public List<string> UniqueStrengths { get; set; } = new();
        public List<string> UniqueWeaknesses { get; set; } = new();
    }

    public class BulkUpdateRequest
    {
        public int taskItemId { get; set; }
        public string completionDateTime { get; set; } = string.Empty;
        public string viewingDate { get; set; } = string.Empty;
        public List<int> branchIds { get; set; } = new();
    }
}
using System.ComponentModel.DataAnnotations;

namespace TaskTracker.Models.ViewModels;

public class EmployeeViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Employee ID is required")]
    [StringLength(20)]
    public string EmployeeId { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    public DateTime? HireDate { get; set; }

    [StringLength(50)]
    public string Position { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    public int? ManagerId { get; set; }

    public bool IsActive { get; set; } = true;
}

public class EmployeeListViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string CurrentBranch { get; set; } = string.Empty;
    public int PerformanceScore { get; set; }
    public bool IsActive { get; set; }
    public string Initials { get; set; } = string.Empty;
}

public class EmployeeDetailsViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime? HireDate { get; set; }
    public string Position { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Manager { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int PendingTasks { get; set; }
    public int OnTimeTasks { get; set; }
    public double PerformanceScore { get; set; }

    public List<BranchHistoryItem> BranchHistory { get; set; } = new();
    public List<TaskHistoryItem> RecentTasks { get; set; } = new();
    public List<string> ChartLabels { get; set; } = new();
    public List<int> ChartData { get; set; } = new();
}

public class BranchHistoryItem
{
    public string BranchName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Duration { get; set; }
}

public class TaskHistoryItem
{
    public DateTime Date { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsOnTime { get; set; }
}
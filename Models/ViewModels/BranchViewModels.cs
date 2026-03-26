using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TaskTracker.Models.ViewModels;

public class BranchViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Branch name is required")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    [StringLength(1000)]
    public string Notes { get; set; } = string.Empty;

    public List<int> HiddenTaskIds { get; set; } = new();

    public bool IsActive { get; set; } = true;
}

public class BranchListViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
    public double CompletionRate { get; set; }
    public bool IsActive { get; set; }
    public List<string> HiddenTasks { get; set; } = new(); // This must be here
}

public class BranchDetailsViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public int EmployeeCount { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public double CompletionRate { get; set; }

    public List<string> HiddenTasks { get; set; } = new();
    public List<BranchEmployeeItem> CurrentEmployees { get; set; } = new();
    public List<BranchTaskItem> RecentTasks { get; set; } = new();
}

public class BranchEmployeeItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
}

public class BranchTaskItem
{
    public DateTime Date { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}
using System.ComponentModel.DataAnnotations;

namespace TaskTracker.Models.ViewModels;

public class DepartmentViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Department name is required")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class DepartmentListViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int BranchCount { get; set; }
    public int EmployeeCount { get; set; }
    public bool IsActive { get; set; }
}

public class DepartmentDetailsViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public int BranchCount { get; set; }
    public int EmployeeCount { get; set; }
    public double OverallCompletionRate { get; set; }

    public List<DepartmentBranchItem> Branches { get; set; } = new();
    public List<DepartmentEmployeeItem> Employees { get; set; } = new();
}

public class DepartmentBranchItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double CompletionRate { get; set; }
}

public class DepartmentEmployeeItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
}
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models.Entities;

[Table("Employees")]
public class Employee
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string EmployeeId { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Address { get; set; }

    public DateTime? HireDate { get; set; }

    [MaxLength(50)]
    public string? Position { get; set; }

    public int? DepartmentId { get; set; }

    public int? ManagerId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(DepartmentId))]
    public virtual Department? Department { get; set; }

    [ForeignKey(nameof(ManagerId))]
    public virtual Employee? Manager { get; set; }

    // Collection of subordinates (employees who report to this employee)
    // Use InverseProperty to explicitly define the relationship
    [InverseProperty(nameof(Manager))]
    public virtual ICollection<Employee> Subordinates { get; set; } = new HashSet<Employee>();

    // Branch assignments for this employee
    public virtual ICollection<BranchAssignment> BranchAssignments { get; set; } = new HashSet<BranchAssignment>();

    // Task assignments for this employee
    public virtual ICollection<TaskAssignment> TaskAssignments { get; set; } = new HashSet<TaskAssignment>();
}
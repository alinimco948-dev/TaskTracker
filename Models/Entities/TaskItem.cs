using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models.Entities;

public enum TaskExecutionType
{
    [Display(Name = "Daily - Complete each day")]
    RecurringDaily = 0,

    [Display(Name = "Multi-Day - Complete once")]
    MultiDay = 1,

    [Display(Name = "Weekly - Complete each week")]
    RecurringWeekly = 2,

    [Display(Name = "Monthly - Complete each month")]
    RecurringMonthly = 3,

    [Display(Name = "One-Time - Complete once")]
    OneTime = 4
}



[Table("TaskItems")]
public class TaskItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public TimeSpan Deadline { get; set; }

    public bool IsSameDay { get; set; } = true;

    public int DisplayOrder { get; set; }

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Task Execution Type
    public TaskExecutionType ExecutionType { get; set; } = TaskExecutionType.RecurringDaily;

    // Scheduling properties
    [MaxLength(50)]
    public string? WeeklyDays { get; set; }

    [MaxLength(50)]
    public string? MonthlyPattern { get; set; }

    public int? DurationDays { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? MaxOccurrences { get; set; }
    public DateTime? AvailableFrom { get; set; }
    public DateTime? AvailableTo { get; set; }

    // Navigation properties
    public virtual ICollection<DailyTask> DailyTasks { get; set; } = new HashSet<DailyTask>();
}
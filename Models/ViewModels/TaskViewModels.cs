using System.ComponentModel.DataAnnotations;
using TaskTracker.Models.Entities;

namespace TaskTracker.Models.ViewModels;

public class TaskItemViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Task name is required")]
    [StringLength(50, ErrorMessage = "Task name cannot exceed 50 characters")]
    [Display(Name = "Task Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Deadline is required")]
    [Display(Name = "Deadline Time")]
    [DataType(DataType.Time)]
    public TimeSpan Deadline { get; set; } = new TimeSpan(21, 0, 0);

    [Display(Name = "Same Day Deadline")]
    public bool IsSameDay { get; set; } = true;

    [Display(Name = "Display Order")]
    public int DisplayOrder { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    [Display(Name = "Description")]
    [DataType(DataType.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    // ========== TASK EXECUTION TYPE ==========
    [Display(Name = "Task Type")]
    public TaskExecutionType ExecutionType { get; set; } = TaskExecutionType.RecurringDaily;

    // ========== SCHEDULING PROPERTIES ==========

    [Display(Name = "Weekly Days")]
    public List<int> WeeklyDays { get; set; } = new();

    [Display(Name = "Monthly Pattern")]
    public string MonthlyPattern { get; set; } = "15";

    [Display(Name = "Duration (Days)")]
    public int? DurationDays { get; set; }

    [Display(Name = "Start Date")]
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [Display(Name = "End Date")]
    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }

    [Display(Name = "Max Occurrences")]
    public int? MaxOccurrences { get; set; }

    [Display(Name = "Available From")]
    [DataType(DataType.Date)]
    public DateTime? AvailableFrom { get; set; }

    [Display(Name = "Available To")]
    [DataType(DataType.Date)]
    public DateTime? AvailableTo { get; set; }
}

public class TaskListViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeSpan Deadline { get; set; }
    public bool IsSameDay { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }

    // Scheduling info
    public TaskExecutionType ExecutionType { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? DurationDays { get; set; }

    // Read-only computed properties
    public string FormattedDeadline => Deadline.ToString(@"hh\:mm");
    public string Type => IsSameDay ? "Same Day" : "Next Day";
    public string TypeColor => IsSameDay ? "blue" : "purple";
    public string Status => IsActive ? "Active" : "Inactive";
    public string StatusColor => IsActive ? "green" : "red";
    public string ExecutionTypeDisplay => GetExecutionTypeDisplay();
    public string ScheduleInfo => GetScheduleInfo();

    private string GetExecutionTypeDisplay()
    {
        return ExecutionType switch
        {
            TaskExecutionType.RecurringDaily => "Daily Task",
            TaskExecutionType.MultiDay => "Multi-Day Task",
            TaskExecutionType.RecurringWeekly => "Weekly Task",
            TaskExecutionType.RecurringMonthly => "Monthly Task",
            TaskExecutionType.OneTime => "One-Time Task",
            _ => "Unknown"
        };
    }

    private string GetScheduleInfo()
    {
        if (!IsActive) return "Inactive";

        return ExecutionType switch
        {
            TaskExecutionType.RecurringDaily => "Daily - Complete each day",
            TaskExecutionType.MultiDay => DurationDays.HasValue ? $"Multi-day ({DurationDays} days) - Complete once" : "Multi-day task - Complete once",
            TaskExecutionType.RecurringWeekly => "Weekly - Complete each occurrence",
            TaskExecutionType.RecurringMonthly => "Monthly - Complete each occurrence",
            TaskExecutionType.OneTime => StartDate.HasValue ? $"One-time on {StartDate.Value:MMM d, yyyy}" : "One-time task",
            _ => "Unknown"
        };
    }
}
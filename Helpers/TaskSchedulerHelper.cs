using TaskTracker.Models.Entities;
using TaskTracker.Services;

namespace TaskTracker.Helpers;

public static class TaskSchedulerHelper
{
    public static string GetScheduleDescription(this TaskItem task)
    {
        if (!task.IsActive) return "Inactive";

        switch (task.ExecutionType)
        {
            case TaskExecutionType.RecurringDaily:
                return "Daily - Complete each day";

            case TaskExecutionType.MultiDay:
                if (task.DurationDays.HasValue)
                    return $"Multi-Day ({task.DurationDays} days) - Complete once";
                return "Multi-Day - Complete once";

            case TaskExecutionType.RecurringWeekly:
                if (!string.IsNullOrEmpty(task.WeeklyDays))
                {
                    var days = task.WeeklyDays.Split(',').Select(int.Parse);
                    var dayNames = days.Select(d => GetDayName(d));
                    return $"Weekly on {string.Join(", ", dayNames)}";
                }
                return "Weekly";

            case TaskExecutionType.RecurringMonthly:
                if (task.MonthlyPattern == "last")
                    return "Monthly on last day";
                if (int.TryParse(task.MonthlyPattern, out int day))
                    return $"Monthly on day {day}";
                return $"Monthly on {task.MonthlyPattern}";

            case TaskExecutionType.OneTime:
                if (task.StartDate.HasValue)
                    return $"One-time on {task.StartDate.Value:MMM d, yyyy}";
                return "One-time";

            default:
                return "Unknown schedule";
        }
    }

    public static string GetDateRangeDescription(this TaskItem task)
    {
        var parts = new List<string>();

        if (task.StartDate.HasValue)
            parts.Add($"From {task.StartDate.Value:MMM d, yyyy}");

        if (task.EndDate.HasValue)
            parts.Add($"To {task.EndDate.Value:MMM d, yyyy}");

        if (task.AvailableFrom.HasValue)
            parts.Add($"Available from {task.AvailableFrom.Value:MMM d, yyyy}");

        if (task.AvailableTo.HasValue)
            parts.Add($"Available to {task.AvailableTo.Value:MMM d, yyyy}");

        if (task.MaxOccurrences.HasValue)
            parts.Add($"Max {task.MaxOccurrences.Value} occurrences");

        return parts.Any() ? string.Join(", ", parts) : "Always active";
    }

    public static string GetNextOccurrence(this TaskItem task, DateTime fromDate)
    {
        var scheduler = new TaskSchedulerService();
        var current = fromDate.Date;

        for (int i = 0; i < 365; i++) // Check up to one year ahead
        {
            if (scheduler.IsTaskVisibleOnDate(task, current))
            {
                return current.ToString("MMM d, yyyy");
            }
            current = current.AddDays(1);
        }

        return "No future occurrences";
    }

    public static string GetTaskTypeDisplay(this TaskExecutionType type)
    {
        return type switch
        {
            TaskExecutionType.RecurringDaily => "Daily Task",
            TaskExecutionType.MultiDay => "Multi-Day Task",
            TaskExecutionType.RecurringWeekly => "Weekly Task",
            TaskExecutionType.RecurringMonthly => "Monthly Task",
            TaskExecutionType.OneTime => "One-Time Task",
            _ => "Unknown"
        };
    }

    public static string GetTaskTypeIcon(this TaskExecutionType type)
    {
        return type switch
        {
            TaskExecutionType.RecurringDaily => "fa-calendar-day",
            TaskExecutionType.MultiDay => "fa-calendar-range",
            TaskExecutionType.RecurringWeekly => "fa-calendar-week",
            TaskExecutionType.RecurringMonthly => "fa-calendar-alt",
            TaskExecutionType.OneTime => "fa-hourglass-start",
            _ => "fa-clock"
        };
    }

    public static string GetTaskTypeShort(this TaskExecutionType type)
    {
        return type switch
        {
            TaskExecutionType.RecurringDaily => "Daily",
            TaskExecutionType.MultiDay => "Multi",
            TaskExecutionType.RecurringWeekly => "Weekly",
            TaskExecutionType.RecurringMonthly => "Monthly",
            TaskExecutionType.OneTime => "OneTime",
            _ => ""
        };
    }

    private static string GetDayName(int day)
    {
        return day switch
        {
            0 => "Sunday",
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            _ => day.ToString()
        };
    }
}
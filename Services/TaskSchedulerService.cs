using TaskTracker.Models.Entities;

namespace TaskTracker.Services;

public class TaskSchedulerService
{
    /// <summary>
    /// Check if a task should be visible on a given date based on its schedule
    /// </summary>
    public bool IsTaskVisibleOnDate(TaskItem task, DateTime date)
    {
        // Check if task is active
        if (!task.IsActive) return false;

        // Check availability range - null means "forever" (no end date)
        if (task.AvailableFrom.HasValue && date < task.AvailableFrom.Value)
            return false;

        // If AvailableTo is null, it means FOREVER AVAILABLE - no end date check needed
        if (task.AvailableTo.HasValue && date > task.AvailableTo.Value)
            return false;

        // Check task schedule range - null EndDate means FOREVER TASK
        if (task.StartDate.HasValue && date < task.StartDate.Value)
            return false;
        if (task.EndDate.HasValue && date > task.EndDate.Value)
            return false;

        // Check recurrence pattern based on task type
        switch (task.ExecutionType)
        {
            case TaskExecutionType.RecurringDaily:
                return true; // Appears every day within date range

            case TaskExecutionType.MultiDay:
                return IsMultiDayTaskVisible(task, date);

            case TaskExecutionType.RecurringWeekly:
                return IsWeeklyTaskVisible(task, date);

            case TaskExecutionType.RecurringMonthly:
                return IsMonthlyTaskVisible(task, date);

            case TaskExecutionType.OneTime:
                return IsOneTimeTaskVisible(task, date);

            default:
                return true;
        }
    }

    /// <summary>
    /// Check if a task should be considered completed for a given date
    /// For Multi-Day tasks, once completed, it's done for all dates
    /// For Recurring tasks, each occurrence must be completed separately
    /// </summary>
    public bool IsTaskCompletedForDate(TaskItem task, DailyTask? dailyTask, DateTime date)
    {
        if (dailyTask == null) return false;

        // For Multi-Day tasks: once completed, it's done for all dates in the range
        if (task.ExecutionType == TaskExecutionType.MultiDay)
        {
            // If the task has been completed at any time, it's completed for all dates
            return dailyTask.IsCompleted;
        }

        // For all other task types, completion is per occurrence
        return dailyTask.IsCompleted && dailyTask.TaskDate.Date == date.Date;
    }

    /// <summary>
    /// Check if a Multi-Day task is visible on a given date
    /// </summary>
    private bool IsMultiDayTaskVisible(TaskItem task, DateTime date)
    {
        if (!task.StartDate.HasValue) return false;

        var startDate = task.StartDate.Value;
        var endDate = task.EndDate.HasValue ? task.EndDate.Value :
                      (task.DurationDays.HasValue ? startDate.AddDays(task.DurationDays.Value - 1) : startDate);

        return date >= startDate && date <= endDate;
    }

    /// <summary>
    /// Check if a Weekly task is visible on a given date
    /// </summary>
    private bool IsWeeklyTaskVisible(TaskItem task, DateTime date)
    {
        if (string.IsNullOrEmpty(task.WeeklyDays)) return false;

        var weeklyDays = task.WeeklyDays.Split(',')
            .Select(int.Parse)
            .ToList();

        var dayOfWeek = (int)date.DayOfWeek;
        return weeklyDays.Contains(dayOfWeek);
    }

    /// <summary>
    /// Check if a Monthly task is visible on a given date
    /// </summary>
    private bool IsMonthlyTaskVisible(TaskItem task, DateTime date)
    {
        if (string.IsNullOrEmpty(task.MonthlyPattern)) return false;

        var pattern = task.MonthlyPattern.ToLower();

        // Check for last day of month
        if (pattern == "last")
        {
            return date.Day == DateTime.DaysInMonth(date.Year, date.Month);
        }

        // Check for specific day of month (e.g., "15")
        if (int.TryParse(pattern, out int dayOfMonth))
        {
            return date.Day == dayOfMonth;
        }

        // Check for specific weekday pattern (e.g., "first-monday", "third-friday")
        var parts = pattern.Split('-');
        if (parts.Length == 2)
        {
            var ordinal = parts[0];
            var weekday = parts[1];

            var targetWeekday = GetWeekdayNumber(weekday);
            if (targetWeekday == -1) return false;

            return IsNthWeekdayOfMonth(date, ordinal, targetWeekday);
        }

        return false;
    }

    /// <summary>
    /// Check if a One-Time task is visible on a given date
    /// </summary>
    private bool IsOneTimeTaskVisible(TaskItem task, DateTime date)
    {
        if (task.StartDate.HasValue)
        {
            return date.Date == task.StartDate.Value.Date;
        }
        return false;
    }

    /// <summary>
    /// Check if date is the Nth weekday of the month
    /// </summary>
    private bool IsNthWeekdayOfMonth(DateTime date, string ordinal, int targetWeekday)
    {
        var weekNumber = GetWeekNumber(ordinal);
        if (weekNumber == -1) return false;

        var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
        var firstOccurrence = firstDayOfMonth.AddDays((targetWeekday - (int)firstDayOfMonth.DayOfWeek + 7) % 7);

        var targetDate = firstOccurrence.AddDays(7 * (weekNumber - 1));

        return date.Date == targetDate.Date && date.Month == targetDate.Month;
    }

    /// <summary>
    /// Convert ordinal string to week number
    /// </summary>
    private int GetWeekNumber(string ordinal)
    {
        return ordinal.ToLower() switch
        {
            "first" => 1,
            "second" => 2,
            "third" => 3,
            "fourth" => 4,
            "fifth" => 5,
            "last" => 5,
            _ => -1
        };
    }

    /// <summary>
    /// Convert weekday string to day number (0-6, Sunday=0)
    /// </summary>
    private int GetWeekdayNumber(string weekday)
    {
        return weekday.ToLower() switch
        {
            "sunday" => 0,
            "monday" => 1,
            "tuesday" => 2,
            "wednesday" => 3,
            "thursday" => 4,
            "friday" => 5,
            "saturday" => 6,
            _ => -1
        };
    }

    /// <summary>
    /// Get the deadline date for a task
    /// </summary>
    public DateTime GetTaskDeadline(TaskItem task, DateTime taskDate)
    {
        // For Multi-Day tasks, deadline is at the END of the duration
        if (task.ExecutionType == TaskExecutionType.MultiDay && task.StartDate.HasValue)
        {
            var endDate = task.EndDate.HasValue ? task.EndDate.Value :
                          (task.DurationDays.HasValue ? task.StartDate.Value.AddDays(task.DurationDays.Value - 1) : task.StartDate.Value);

            var deadline = endDate.Date;
            deadline = deadline.Add(task.Deadline);
            return deadline;
        }

        // For other tasks, deadline is on the task date (same day or next day)
        var baseDeadline = taskDate.Date;
        if (task.IsSameDay)
        {
            baseDeadline = baseDeadline.Add(task.Deadline);
        }
        else
        {
            baseDeadline = baseDeadline.AddDays(1).Add(task.Deadline);
        }
        return baseDeadline;
    }

    /// <summary>
    /// Get the start date of a task (for display purposes)
    /// </summary>
    public DateTime? GetTaskStartDate(TaskItem task)
    {
        return task.StartDate;
    }

    /// <summary>
    /// Get the end date of a task (null = forever)
    /// </summary>
    public DateTime? GetTaskEndDate(TaskItem task)
    {
        return task.EndDate;
    }

    /// <summary>
    /// Get the availability start date (null = no start restriction)
    /// </summary>
    public DateTime? GetAvailabilityStartDate(TaskItem task)
    {
        return task.AvailableFrom;
    }

    /// <summary>
    /// Get the availability end date (null = forever available)
    /// </summary>
    public DateTime? GetAvailabilityEndDate(TaskItem task)
    {
        return task.AvailableTo;
    }

    /// <summary>
    /// Check if a task runs forever (no end date)
    /// </summary>
    public bool IsTaskForever(TaskItem task)
    {
        return !task.EndDate.HasValue;
    }

    /// <summary>
    /// Check if a task is available forever (no availability end date)
    /// </summary>
    public bool IsTaskAvailableForever(TaskItem task)
    {
        return !task.AvailableTo.HasValue;
    }

    /// <summary>
    /// Get all dates when a task should be visible between start and end dates
    /// </summary>
    public List<DateTime> GetTaskDates(TaskItem task, DateTime startDate, DateTime endDate)
    {
        var dates = new List<DateTime>();
        var current = startDate.Date;

        while (current <= endDate.Date)
        {
            if (IsTaskVisibleOnDate(task, current))
            {
                dates.Add(current);
            }
            current = current.AddDays(1);
        }

        // Limit by max occurrences if specified
        if (task.MaxOccurrences.HasValue && dates.Count > task.MaxOccurrences.Value)
        {
            return dates.Take(task.MaxOccurrences.Value).ToList();
        }

        return dates;
    }

    /// <summary>
    /// Get the next occurrence date after a given date
    /// </summary>
    public DateTime? GetNextOccurrence(TaskItem task, DateTime fromDate)
    {
        var current = fromDate.Date;
        var maxChecks = 365; // Check up to one year ahead

        for (int i = 0; i < maxChecks; i++)
        {
            if (IsTaskVisibleOnDate(task, current))
            {
                return current;
            }
            current = current.AddDays(1);
        }

        return null;
    }

    /// <summary>
    /// Get the previous occurrence date before a given date
    /// </summary>
    public DateTime? GetPreviousOccurrence(TaskItem task, DateTime fromDate)
    {
        var current = fromDate.Date;
        var maxChecks = 365;

        for (int i = 0; i < maxChecks; i++)
        {
            current = current.AddDays(-1);
            if (IsTaskVisibleOnDate(task, current))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>
    /// Get task schedule summary for display
    /// </summary>
    public string GetTaskScheduleSummary(TaskItem task)
    {
        var parts = new List<string>();

        // Task type
        parts.Add(GetTaskTypeName(task.ExecutionType));

        // Schedule period
        if (task.StartDate.HasValue)
        {
            var startStr = task.StartDate.Value.ToString("MMM d, yyyy");
            if (task.EndDate.HasValue)
            {
                var endStr = task.EndDate.Value.ToString("MMM d, yyyy");
                parts.Add($"from {startStr} to {endStr}");
            }
            else
            {
                parts.Add($"from {startStr} → FOREVER");
            }
        }

        // Recurrence details
        var recurrenceDetails = GetRecurrenceDetails(task);
        if (!string.IsNullOrEmpty(recurrenceDetails))
        {
            parts.Add(recurrenceDetails);
        }

        // Availability
        if (task.AvailableFrom.HasValue)
        {
            var fromStr = task.AvailableFrom.Value.ToString("MMM d, yyyy");
            if (task.AvailableTo.HasValue)
            {
                var toStr = task.AvailableTo.Value.ToString("MMM d, yyyy");
                parts.Add($"available {fromStr} to {toStr}");
            }
            else
            {
                parts.Add($"available from {fromStr} → FOREVER");
            }
        }
        else if (task.AvailableTo.HasValue)
        {
            var toStr = task.AvailableTo.Value.ToString("MMM d, yyyy");
            parts.Add($"available until {toStr}");
        }

        // Occurrences
        if (task.MaxOccurrences.HasValue)
        {
            parts.Add($"max {task.MaxOccurrences.Value} occurrences");
        }
        else if (!task.EndDate.HasValue && task.ExecutionType != TaskExecutionType.OneTime)
        {
            parts.Add("unlimited occurrences");
        }

        return string.Join(" • ", parts);
    }

    /// <summary>
    /// Get recurrence details string for display
    /// </summary>
    private string GetRecurrenceDetails(TaskItem task)
    {
        switch (task.ExecutionType)
        {
            case TaskExecutionType.RecurringDaily:
                return "daily";

            case TaskExecutionType.RecurringWeekly:
                if (!string.IsNullOrEmpty(task.WeeklyDays))
                {
                    var days = task.WeeklyDays.Split(',')
                        .Select(int.Parse)
                        .Select(d => GetShortDayName(d));
                    return $"weekly on {string.Join(", ", days)}";
                }
                return "weekly";

            case TaskExecutionType.RecurringMonthly:
                if (!string.IsNullOrEmpty(task.MonthlyPattern))
                {
                    if (task.MonthlyPattern == "last")
                        return "monthly on last day";
                    if (int.TryParse(task.MonthlyPattern, out int day))
                        return $"monthly on day {day}";
                    return $"monthly on {task.MonthlyPattern}";
                }
                return "monthly";

            case TaskExecutionType.MultiDay:
                if (task.DurationDays.HasValue)
                    return $"{task.DurationDays.Value} days duration";
                return "multi-day";

            case TaskExecutionType.OneTime:
                return "one-time";

            default:
                return string.Empty;
        }
    }

    private string GetTaskTypeName(TaskExecutionType type)
    {
        return type switch
        {
            TaskExecutionType.RecurringDaily => "Daily Task",
            TaskExecutionType.MultiDay => "Multi-Day Task",
            TaskExecutionType.RecurringWeekly => "Weekly Task",
            TaskExecutionType.RecurringMonthly => "Monthly Task",
            TaskExecutionType.OneTime => "One-Time Task",
            _ => "Task"
        };
    }

    private string GetShortDayName(int day)
    {
        return day switch
        {
            0 => "Sun",
            1 => "Mon",
            2 => "Tue",
            3 => "Wed",
            4 => "Thu",
            5 => "Fri",
            6 => "Sat",
            _ => day.ToString()
        };
    }
}
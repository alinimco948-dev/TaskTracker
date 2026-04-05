using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class TaskCalculationService : ITaskCalculationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TaskCalculationService> _logger;
    private List<Holiday>? _cachedHolidays;
    private DateTime _holidayCacheDate = DateTime.MinValue;

    public TaskCalculationService(
        ApplicationDbContext context,
        ILogger<TaskCalculationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Core Calculations
public DateTime CalculateDeadline(TaskItem task, DateTime taskDate)
{
    // taskDate comes from the dashboard (local date selected by user)
    // Convert it to local date first
    var localTaskDate = taskDate.Kind == DateTimeKind.Utc 
        ? taskDate.ToLocalTime().Date 
        : taskDate.Date;
    
    // Calculate deadline in LOCAL time
    var localDeadline = localTaskDate;
    
    if (task.IsSameDay)
    {
        localDeadline = localDeadline.Add(task.Deadline);
    }
    else
    {
        localDeadline = localDeadline.AddDays(1).Add(task.Deadline);
    }
    
    // Convert to UTC for storage and comparison
    var utcDeadline = localDeadline.ToUniversalTime();
    
    _logger.LogInformation($"CalculateDeadline: TaskDate={taskDate:yyyy-MM-dd HH:mm}, LocalDeadline={localDeadline:yyyy-MM-dd HH:mm}, UtcDeadline={utcDeadline:yyyy-MM-dd HH:mm}");
    
    return utcDeadline;
}

// Add this helper method to convert UTC deadline back to Local for display
public DateTime GetLocalDeadline(TaskItem task, DateTime taskDate)
{
    var localTaskDate = taskDate.Kind == DateTimeKind.Utc 
        ? taskDate.ToLocalTime().Date 
        : taskDate.Date;
    
    var localDeadline = localTaskDate;
    
    if (task.IsSameDay)
    {
        localDeadline = localDeadline.Add(task.Deadline);
    }
    else
    {
        localDeadline = localDeadline.AddDays(1).Add(task.Deadline);
    }
    
    return localDeadline;
}

   
   
    public bool IsTaskOnTime(DailyTask dailyTask)
    {
        if (dailyTask == null || !dailyTask.IsCompleted || !dailyTask.CompletedAt.HasValue || dailyTask.TaskItem == null)
            return false;

        var deadline = CalculateDeadline(dailyTask.TaskItem, dailyTask.TaskDate);
        
        if (dailyTask.AdjustmentMinutes.HasValue && dailyTask.AdjustmentMinutes.Value > 0)
        {
            deadline = deadline.AddMinutes(dailyTask.AdjustmentMinutes.Value);
        }

        var diffSeconds = (dailyTask.CompletedAt.Value - deadline).TotalSeconds;
        return diffSeconds <= 300; // 5 minute grace period
    }

    public bool IsTaskOnTime(DateTime taskDate, DateTime? completedAt, TimeSpan deadlineTime, bool isSameDay, int? adjustmentMinutes)
    {
        if (!completedAt.HasValue)
            return false;

        var deadline = taskDate.Date;
        if (isSameDay)
        {
            deadline = deadline.Add(deadlineTime);
        }
        else
        {
            deadline = deadline.AddDays(1).Add(deadlineTime);
        }

        if (adjustmentMinutes.HasValue && adjustmentMinutes.Value > 0)
        {
            deadline = deadline.AddMinutes(adjustmentMinutes.Value);
        }

        var diffSeconds = (completedAt.Value - deadline).TotalSeconds;
        return diffSeconds <= 300;
    }

public async Task<TaskDelayInfo> GetTaskDelayInfoAsync(DailyTask dailyTask)
{
    return await GetHolidayAdjustedDelayInfoAsync(dailyTask);
}
    #endregion

    #region Holiday Helpers with Caching

    private async Task<List<Holiday>> GetCachedHolidaysAsync()
    {
        if (_cachedHolidays == null || DateTime.UtcNow - _holidayCacheDate > TimeSpan.FromHours(1))
        {
            _cachedHolidays = await _context.Holidays.AsNoTracking().ToListAsync();
            _holidayCacheDate = DateTime.UtcNow;
            _logger.LogInformation("Holidays cached: {Count} holidays loaded", _cachedHolidays.Count);
        }
        return _cachedHolidays;
    }

    public async Task<bool> IsHolidayAsync(DateTime date)
    {
        try
        {
            var holidays = await GetCachedHolidaysAsync();
            var dateOnly = date.Date;

            var isSpecific = holidays.Any(h => !h.IsWeekly && h.HolidayDate.Date == dateOnly);
            if (isSpecific) return true;

            var dayOfWeek = (int)date.DayOfWeek;
            var isWeekly = holidays.Any(h => h.IsWeekly && h.WeekDay == dayOfWeek);

            return isWeekly;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if date is holiday");
            return false;
        }
    }

    public async Task<DateTime> GetNextWorkingDayAsync(DateTime date)
    {
        return await GetNextWorkingDayAsync(date, false);
    }

    public async Task<DateTime> GetNextWorkingDayAsync(DateTime date, bool includeSameDay)
    {
        var workingDay = includeSameDay ? date.Date : date.Date.AddDays(1);
        var maxAttempts = 30;
        
        for (int i = 0; i < maxAttempts; i++)
        {
            if (!await IsHolidayAsync(workingDay))
            {
                return workingDay;
            }
            workingDay = workingDay.AddDays(1);
        }
        
        return workingDay;
    }

    public async Task<DateTime> AdjustDeadlineForHolidaysAsync(DateTime deadline)
    {
        var adjustedDeadline = deadline;
        var maxAttempts = 30;
        var daysAdded = 0;
        
        for (int i = 0; i < maxAttempts; i++)
        {
            if (!await IsHolidayAsync(adjustedDeadline))
            {
                if (daysAdded > 0)
                {
                    _logger.LogInformation($"Deadline adjusted: {deadline:yyyy-MM-dd HH:mm} -> {adjustedDeadline:yyyy-MM-dd HH:mm} (+{daysAdded} days for holidays)");
                }
                return adjustedDeadline;
            }
            adjustedDeadline = adjustedDeadline.AddDays(1);
            daysAdded++;
        }
        
        return adjustedDeadline;
    }

   public async Task<TaskDelayInfo> GetHolidayAdjustedDelayInfoAsync(DailyTask dailyTask)
{
    if (dailyTask == null || !dailyTask.IsCompleted || !dailyTask.CompletedAt.HasValue)
    {
        return new TaskDelayInfo
        {
            IsOnTime = false,
            DelayType = "pending",
            DelayText = "Pending",
            Deadline = null,
            CompletedAt = null,
            AdjustmentMinutes = dailyTask?.AdjustmentMinutes,
            WasAdjustedForHoliday = false,
            HolidayAdjustmentNote = string.Empty
        };
    }

    if (dailyTask.TaskItem == null)
    {
        return new TaskDelayInfo
        {
            IsOnTime = false,
            DelayType = "unknown",
            DelayText = "Unknown",
            Deadline = null,
            CompletedAt = dailyTask.CompletedAt,
            AdjustmentMinutes = dailyTask.AdjustmentMinutes,
            WasAdjustedForHoliday = false,
            HolidayAdjustmentNote = string.Empty
        };
    }

    // Get the task date in LOCAL time for deadline calculation
    var taskDate = dailyTask.TaskDate;
    var localTaskDate = taskDate.Kind == DateTimeKind.Utc 
        ? taskDate.ToLocalTime().Date 
        : taskDate.Date;
    
    // Calculate deadline in LOCAL time
    var localDeadline = localTaskDate;
    if (dailyTask.TaskItem.IsSameDay)
    {
        localDeadline = localDeadline.Add(dailyTask.TaskItem.Deadline);
    }
    else
    {
        localDeadline = localDeadline.AddDays(1).Add(dailyTask.TaskItem.Deadline);
    }
    
    // Add adjustment minutes if any
    if (dailyTask.AdjustmentMinutes.HasValue && dailyTask.AdjustmentMinutes.Value > 0)
    {
        localDeadline = localDeadline.AddMinutes(dailyTask.AdjustmentMinutes.Value);
    }
    
    // Convert completed time to LOCAL for comparison
    var localCompletedAt = dailyTask.CompletedAt.Value.Kind == DateTimeKind.Utc
        ? dailyTask.CompletedAt.Value.ToLocalTime()
        : dailyTask.CompletedAt.Value;
    
    // Calculate difference using LOCAL times
    var diffSeconds = (localCompletedAt - localDeadline).TotalSeconds;
    const int graceSeconds = 300; // 5 minutes
    
    // Check if deadline falls on holiday (using local date)
    var isDeadlineHoliday = await IsHolidayAsync(localDeadline);
    var holidayNote = string.Empty;
    var wasAdjustedForHoliday = false;
    
    if (isDeadlineHoliday)
    {
        var adjustedLocalDeadline = await GetNextWorkingDayAsync(localDeadline);
        var adjustedDiffSeconds = (localCompletedAt - adjustedLocalDeadline).TotalSeconds;
        wasAdjustedForHoliday = true;
        holidayNote = $"Deadline moved from {localDeadline:MMM dd, yyyy HH:mm} to {adjustedLocalDeadline:MMM dd, yyyy HH:mm} due to holiday";
        diffSeconds = adjustedDiffSeconds;
    }
    
    if (Math.Abs(diffSeconds) <= graceSeconds)
    {
        return new TaskDelayInfo
        {
            IsOnTime = true,
            DelayType = "on-time",
            DelayText = "On Time",
            Deadline = localDeadline,
            CompletedAt = localCompletedAt,
            AdjustmentMinutes = dailyTask.AdjustmentMinutes,
            WasAdjustedForHoliday = wasAdjustedForHoliday,
            HolidayAdjustmentNote = holidayNote
        };
    }
    
    if (diffSeconds < 0)
    {
        var earlyMinutes = (int)Math.Round(Math.Abs(diffSeconds) / 60);
        var earlyHours = earlyMinutes / 60;
        var earlyDays = earlyMinutes / 1440;
        
        if (earlyMinutes < 60)
        {
            return new TaskDelayInfo
            {
                IsOnTime = true,
                DelayType = "early",
                DelayText = $"{earlyMinutes}m early",
                Deadline = localDeadline,
                CompletedAt = localCompletedAt,
                AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                WasAdjustedForHoliday = wasAdjustedForHoliday,
                HolidayAdjustmentNote = holidayNote
            };
        }
        else if (earlyMinutes < 1440)
        {
            var remainingMinutes = earlyMinutes % 60;
            if (remainingMinutes > 0)
                return new TaskDelayInfo
                {
                    IsOnTime = true,
                    DelayType = "early",
                    DelayText = $"{earlyHours}h {remainingMinutes}m early",
                    Deadline = localDeadline,
                    CompletedAt = localCompletedAt,
                    AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                    WasAdjustedForHoliday = wasAdjustedForHoliday,
                    HolidayAdjustmentNote = holidayNote
                };
            else
                return new TaskDelayInfo
                {
                    IsOnTime = true,
                    DelayType = "early",
                    DelayText = $"{earlyHours}h early",
                    Deadline = localDeadline,
                    CompletedAt = localCompletedAt,
                    AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                    WasAdjustedForHoliday = wasAdjustedForHoliday,
                    HolidayAdjustmentNote = holidayNote
                };
        }
        else
        {
            var remainingHours = (earlyMinutes % 1440) / 60;
            if (remainingHours > 0)
                return new TaskDelayInfo
                {
                    IsOnTime = true,
                    DelayType = "early",
                    DelayText = $"{earlyDays}d {remainingHours}h early",
                    Deadline = localDeadline,
                    CompletedAt = localCompletedAt,
                    AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                    WasAdjustedForHoliday = wasAdjustedForHoliday,
                    HolidayAdjustmentNote = holidayNote
                };
            else
                return new TaskDelayInfo
                {
                    IsOnTime = true,
                    DelayType = "early",
                    DelayText = $"{earlyDays}d early",
                    Deadline = localDeadline,
                    CompletedAt = localCompletedAt,
                    AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                    WasAdjustedForHoliday = wasAdjustedForHoliday,
                    HolidayAdjustmentNote = holidayNote
                };
        }
    }
    
    var lateMinutes = (int)Math.Round(diffSeconds / 60);
    var lateHours = lateMinutes / 60;
    var lateDays = lateMinutes / 1440;
    
    if (lateMinutes < 60)
    {
        return new TaskDelayInfo
        {
            IsOnTime = false,
            DelayType = "minutes",
            DelayText = $"{lateMinutes}m late",
            Deadline = localDeadline,
            CompletedAt = localCompletedAt,
            AdjustmentMinutes = dailyTask.AdjustmentMinutes,
            WasAdjustedForHoliday = wasAdjustedForHoliday,
            HolidayAdjustmentNote = holidayNote
        };
    }
    else if (lateMinutes < 1440)
    {
        var remainingMinutes = lateMinutes % 60;
        if (remainingMinutes > 0)
            return new TaskDelayInfo
            {
                IsOnTime = false,
                DelayType = "hours",
                DelayText = $"{lateHours}h {remainingMinutes}m late",
                Deadline = localDeadline,
                CompletedAt = localCompletedAt,
                AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                WasAdjustedForHoliday = wasAdjustedForHoliday,
                HolidayAdjustmentNote = holidayNote
            };
        else
            return new TaskDelayInfo
            {
                IsOnTime = false,
                DelayType = "hours",
                DelayText = $"{lateHours}h late",
                Deadline = localDeadline,
                CompletedAt = localCompletedAt,
                AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                WasAdjustedForHoliday = wasAdjustedForHoliday,
                HolidayAdjustmentNote = holidayNote
            };
    }
    else
    {
        var remainingHours = (lateMinutes % 1440) / 60;
        if (remainingHours > 0)
            return new TaskDelayInfo
            {
                IsOnTime = false,
                DelayType = "days",
                DelayText = $"{lateDays}d {remainingHours}h late",
                Deadline = localDeadline,
                CompletedAt = localCompletedAt,
                AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                WasAdjustedForHoliday = wasAdjustedForHoliday,
                HolidayAdjustmentNote = holidayNote
            };
        else
            return new TaskDelayInfo
            {
                IsOnTime = false,
                DelayType = "days",
                DelayText = $"{lateDays}d late",
                Deadline = localDeadline,
                CompletedAt = localCompletedAt,
                AdjustmentMinutes = dailyTask.AdjustmentMinutes,
                WasAdjustedForHoliday = wasAdjustedForHoliday,
                HolidayAdjustmentNote = holidayNote
            };
    }
}
    #endregion

    #region Task Visibility

    public bool IsTaskVisibleOnDate(TaskItem task, DateTime date)
    {
        if (!task.IsActive) return false;
        
        var compareDate = date.Date;
        
        if (task.AvailableFrom.HasValue && compareDate < task.AvailableFrom.Value.Date)
            return false;
        if (task.AvailableTo.HasValue && compareDate > task.AvailableTo.Value.Date)
            return false;
        
        if (task.StartDate.HasValue && compareDate < task.StartDate.Value.Date)
            return false;
        if (task.EndDate.HasValue && compareDate > task.EndDate.Value.Date)
            return false;
        
        switch (task.ExecutionType)
        {
            case TaskExecutionType.RecurringDaily:
                return true;
                
            case TaskExecutionType.RecurringWeekly:
                if (string.IsNullOrEmpty(task.WeeklyDays)) return false;
                var weeklyDays = task.WeeklyDays.Split(',').Select(int.Parse).ToList();
                return weeklyDays.Contains((int)date.DayOfWeek);
                
            case TaskExecutionType.RecurringMonthly:
                if (string.IsNullOrEmpty(task.MonthlyPattern)) return false;
                var pattern = task.MonthlyPattern.ToLower();
                
                // NEW: First 4 days of month
                if (pattern == "first4days")
                {
                    return date.Day >= 1 && date.Day <= 4;
                }
                
                if (pattern == "last")
                    return date.Day == DateTime.DaysInMonth(date.Year, date.Month);
                if (int.TryParse(pattern, out int dayOfMonth))
                    return date.Day == dayOfMonth;
                
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
                
            case TaskExecutionType.MultiDay:
                if (!task.StartDate.HasValue) return false;
                var multiDayEndDate = task.EndDate.HasValue ? task.EndDate.Value :
                                     (task.DurationDays.HasValue ? task.StartDate.Value.AddDays(task.DurationDays.Value - 1) : task.StartDate.Value);
                return date >= task.StartDate.Value && date <= multiDayEndDate;
                
            case TaskExecutionType.OneTime:
                return task.StartDate.HasValue && date.Date == task.StartDate.Value.Date;
                
            default:
                return true;
        }
    }

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

    private bool IsNthWeekdayOfMonth(DateTime date, string ordinal, int targetWeekday)
    {
        var weekNumber = GetWeekNumber(ordinal);
        if (weekNumber == -1) return false;

        var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
        var firstOccurrence = firstDayOfMonth.AddDays((targetWeekday - (int)firstDayOfMonth.DayOfWeek + 7) % 7);
        var targetDate = firstOccurrence.AddDays(7 * (weekNumber - 1));

        return date.Date == targetDate.Date && date.Month == targetDate.Month;
    }

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

    public List<DateTime> GetTaskDatesInRange(TaskItem task, DateTime startDate, DateTime endDate)
    {
        var dates = new List<DateTime>();
        var currentDate = startDate.Date;
        var maxDate = endDate.Date;
        
        if (maxDate > startDate.AddYears(1))
        {
            maxDate = startDate.AddYears(1);
        }
        
        for (var date = currentDate; date <= maxDate; date = date.AddDays(1))
        {
            if (IsTaskVisibleOnDate(task, date))
            {
                dates.Add(date);
            }
        }
        
        return dates;
    }

    public int GetTaskCountInRange(TaskItem task, DateTime startDate, DateTime endDate)
    {
        return GetTaskDatesInRange(task, startDate, endDate).Count;
    }

    #endregion

    #region Task Statistics

    public async Task<TaskStatistics> GetEmployeeTaskStatisticsAsync(int employeeId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var employee = await _context.Employees
                .Include(e => e.BranchAssignments)
                .FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee == null)
            {
                return new TaskStatistics();
            }

            var assignedBranchIds = employee.BranchAssignments
                .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
                .Select(ba => ba.BranchId)
                .ToList();

            if (!assignedBranchIds.Any())
            {
                return new TaskStatistics();
            }

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Where(dt => assignedBranchIds.Contains(dt.BranchId) &&
                             dt.TaskDate.Date >= startDate.Date &&
                             dt.TaskDate.Date <= endDate.Date)
                .ToListAsync();

            var relevantTasks = new List<DailyTask>();
            foreach (var dt in dailyTasks)
            {
                var assignment = employee.BranchAssignments
                    .FirstOrDefault(ba => ba.BranchId == dt.BranchId &&
                                         (ba.EndDate == null || dt.TaskDate.Date <= ba.EndDate.Value.Date) &&
                                         dt.TaskDate.Date >= ba.StartDate.Date);
                
                if (assignment != null)
                {
                    relevantTasks.Add(dt);
                }
            }

            var totalTasks = relevantTasks.Count;
            var completedTasks = relevantTasks.Count(dt => dt.IsCompleted);
            
            var onTimeTasks = 0;
            foreach (var dt in relevantTasks.Where(d => d.IsCompleted))
            {
                var delayInfo = await GetHolidayAdjustedDelayInfoAsync(dt);
                if (delayInfo.IsOnTime)
                {
                    onTimeTasks++;
                }
            }
            
            var lateTasks = completedTasks - onTimeTasks;
            var pendingTasks = totalTasks - completedTasks;
            
            var completionRate = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0;
            var onTimeRate = completedTasks > 0 ? Math.Round((double)onTimeTasks / completedTasks * 100, 1) : 0;
            var weightedScore = (completionRate * 0.7) + (onTimeRate * 0.3);

            return new TaskStatistics
            {
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                OnTimeTasks = onTimeTasks,
                LateTasks = lateTasks,
                PendingTasks = pendingTasks,
                CompletionRate = completionRate,
                OnTimeRate = onTimeRate,
                WeightedScore = weightedScore
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating task statistics for employee {EmployeeId}", employeeId);
            return new TaskStatistics();
        }
    }

    public async Task<BranchTaskStatistics> GetBranchTaskStatisticsAsync(int branchId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var branch = await _context.Branches
                .Include(b => b.DailyTasks)
                    .ThenInclude(dt => dt.TaskItem)
                .FirstOrDefaultAsync(b => b.Id == branchId);

            if (branch == null)
            {
                return new BranchTaskStatistics { BranchId = branchId, BranchName = "Unknown" };
            }

            var branchTasks = branch.DailyTasks
                .Where(dt => dt.TaskDate.Date >= startDate.Date && dt.TaskDate.Date <= endDate.Date)
                .ToList();

            var totalTasks = branchTasks.Count;
            var completedTasks = branchTasks.Count(dt => dt.IsCompleted);
            
            var onTimeTasks = 0;
            foreach (var dt in branchTasks.Where(d => d.IsCompleted))
            {
                var delayInfo = await GetHolidayAdjustedDelayInfoAsync(dt);
                if (delayInfo.IsOnTime)
                {
                    onTimeTasks++;
                }
            }
            
            var lateTasks = completedTasks - onTimeTasks;
            
            var completionRate = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0;
            var onTimeRate = completedTasks > 0 ? Math.Round((double)onTimeTasks / completedTasks * 100, 1) : 0;
            var weightedScore = (completionRate * 0.7) + (onTimeRate * 0.3);

            var taskBreakdown = new Dictionary<string, int>();
            foreach (var dt in branchTasks.Where(dt => dt.TaskItem != null && dt.IsCompleted))
            {
                var taskName = dt.TaskItem!.Name;
                if (!taskBreakdown.ContainsKey(taskName))
                    taskBreakdown[taskName] = 0;
                taskBreakdown[taskName]++;
            }

            var employeeScores = new Dictionary<int, double>();
            var assignments = await _context.BranchAssignments
                .Where(ba => ba.BranchId == branchId && ba.EndDate == null)
                .ToListAsync();

            foreach (var assignment in assignments)
            {
                var stats = await GetEmployeeTaskStatisticsAsync(assignment.EmployeeId, startDate, endDate);
                employeeScores[assignment.EmployeeId] = stats.WeightedScore;
            }

            return new BranchTaskStatistics
            {
                BranchId = branchId,
                BranchName = branch.Name,
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                OnTimeTasks = onTimeTasks,
                LateTasks = lateTasks,
                CompletionRate = completionRate,
                OnTimeRate = onTimeRate,
                WeightedScore = weightedScore,
                TaskBreakdown = taskBreakdown,
                EmployeeScores = employeeScores
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating branch statistics for {BranchId}", branchId);
            return new BranchTaskStatistics { BranchId = branchId };
        }
    }

    public async Task<DepartmentTaskStatistics> GetDepartmentTaskStatisticsAsync(int departmentId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == departmentId);

            if (department == null)
            {
                return new DepartmentTaskStatistics { DepartmentId = departmentId, DepartmentName = "Unknown" };
            }

            var branches = await _context.Branches
                .Where(b => b.DepartmentId == departmentId && b.IsActive)
                .ToListAsync();

            var branchStats = new Dictionary<string, BranchTaskStatistics>();
            var totalTasks = 0;
            var completedTasks = 0;
            var onTimeTasks = 0;

            foreach (var branch in branches)
            {
                var stats = await GetBranchTaskStatisticsAsync(branch.Id, startDate, endDate);
                branchStats[branch.Name] = stats;
                totalTasks += stats.TotalTasks;
                completedTasks += stats.CompletedTasks;
                onTimeTasks += stats.OnTimeTasks;
            }

            var completionRate = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0;
            var onTimeRate = completedTasks > 0 ? Math.Round((double)onTimeTasks / completedTasks * 100, 1) : 0;
            var weightedScore = (completionRate * 0.7) + (onTimeRate * 0.3);

            return new DepartmentTaskStatistics
            {
                DepartmentId = departmentId,
                DepartmentName = department.Name,
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                OnTimeTasks = onTimeTasks,
                CompletionRate = completionRate,
                OnTimeRate = onTimeRate,
                WeightedScore = weightedScore,
                BranchStats = branchStats
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating department statistics for {DepartmentId}", departmentId);
            return new DepartmentTaskStatistics { DepartmentId = departmentId };
        }
    }

    #endregion

    #region Schedule Information

    public string GetTaskScheduleSummary(TaskItem task)
    {
        var parts = new List<string>();

        parts.Add(GetTaskTypeName(task.ExecutionType));

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

        var recurrenceDetails = GetRecurrenceDetails(task);
        if (!string.IsNullOrEmpty(recurrenceDetails))
        {
            parts.Add(recurrenceDetails);
        }

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

    public DateTime? GetNextOccurrence(TaskItem task, DateTime fromDate)
    {
        var current = fromDate.Date;
        var maxChecks = 365;

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
                    if (task.MonthlyPattern == "first4days")
                        return "monthly on first 4 days (Days 1-4)";
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

    #endregion
}
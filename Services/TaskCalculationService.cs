using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class TaskCalculationService : ITaskCalculationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TaskCalculationService> _logger;
    private readonly ITimezoneService _timezoneService;
    private List<Holiday>? _cachedHolidays;
    private DateTime _holidayCacheDate = DateTime.MinValue;

    public TaskCalculationService(
        ApplicationDbContext context,
        ILogger<TaskCalculationService> logger,
        ITimezoneService timezoneService)
    {
        _context = context;
        _logger = logger;
        _timezoneService = timezoneService;
        _cachedHolidays = new List<Holiday>();
    }

    public DateTime GetCurrentUtcDate() => DateTime.UtcNow.Date;

    // ========== UNIFIED SCORING SYSTEM ==========
    // All scoring across the application uses these constants and method
    
    public double CompletionWeight => 0.7;
    public double OnTimeWeight => 0.3;
    
    // Instance wrapper delegates to static for interface compliance
    public double CalculateWeightedScore(int totalTasks, int completedTasks, int onTimeTasks)
        => CalculateWeightedScoreStatic(totalTasks, completedTasks, onTimeTasks);
    
    public (double completionRate, double onTimeRate, double weightedScore) CalculateScores(int totalTasks, int completedTasks, int onTimeTasks)
        => CalculateScoresStatic(totalTasks, completedTasks, onTimeTasks);
    
    public static double CalculateWeightedScoreStatic(int totalTasks, int completedTasks, int onTimeTasks)
    {
        const double completionWeight = 0.7;
        const double onTimeWeight = 0.3;
        
        if (totalTasks == 0) return 0;
        
        var completionRate = Math.Round((double)completedTasks / totalTasks * 100, 1);
        var onTimeRate = completedTasks > 0 
            ? Math.Round((double)onTimeTasks / completedTasks * 100, 1) 
            : 0;
        
        double weightedScore;
        
        if (completedTasks == 0)
        {
            weightedScore = completionRate * completionWeight;
        }
        else if (onTimeTasks == completedTasks)
        {
            weightedScore = completionRate;
        }
        else
        {
            weightedScore = (completionRate * completionWeight) + (onTimeRate * onTimeWeight);
        }
        
        return Math.Round(weightedScore, 1);
    }
    
    public static (double completionRate, double onTimeRate, double weightedScore) CalculateScoresStatic(int totalTasks, int completedTasks, int onTimeTasks)
    {
        var completionRate = totalTasks > 0 
            ? Math.Round((double)completedTasks / totalTasks * 100, 1) 
            : 0;
        var onTimeRate = completedTasks > 0 
            ? Math.Round((double)onTimeTasks / completedTasks * 100, 1) 
            : 0;
        var weightedScore = CalculateWeightedScoreStatic(totalTasks, completedTasks, onTimeTasks);
        return (completionRate, onTimeRate, weightedScore);
    }

    #region Core Deadline Calculation

    public async Task<DateTime> CalculateDeadline(TaskItem task, DateTime taskDate)
    {
        var localTaskDate = _timezoneService.ConvertToLocalTime(taskDate).Date;
        
        var baseDeadlineDate = task.IsSameDay 
            ? localTaskDate 
            : localTaskDate.AddDays(1);
            
        var baseDeadline = baseDeadlineDate.Add(task.Deadline);
        
        var adjustedDeadline = await SkipHolidaysAsync(baseDeadline);
        
        _logger.LogInformation("DEADLINE CALCULATION: TaskDate={TaskDate}, IsSameDay={IsSameDay}, BaseDeadline={BaseDeadline}, AdjustedDeadline={AdjustedDeadline}",
            localTaskDate, task.IsSameDay, baseDeadline, adjustedDeadline);
        
        return _timezoneService.ConvertToUtc(adjustedDeadline);
    }

    public async Task<DateTime> GetLocalDeadline(TaskItem task, DateTime taskDate)
    {
        var utcDeadline = await CalculateDeadline(task, taskDate);
        return _timezoneService.ConvertToLocalTime(utcDeadline);
    }

    #endregion

    #region Holiday-Safe Deadline Adjustment

    private async Task<DateTime> SkipHolidaysAsync(DateTime deadline)
    {
        var current = deadline.Date;
        var timeOfDay = deadline.TimeOfDay;
        var maxDays = 30;
        
        for (int i = 0; i < maxDays; i++)
        {
            if (!await IsHolidayAsync(current))
            {
                if (i > 0)
                {
                    _logger.LogInformation("Deadline moved from {OldDate} to {NewDate} due to holiday(s)", deadline.Date, current);
                }
                return current.Add(timeOfDay);
            }
            _logger.LogDebug("Skipping holiday: {Date}", current);
            current = current.AddDays(1);
        }
        
        return current.Add(timeOfDay);
    }

    #endregion

    #region Holiday Detection (Reusable)

    private async Task<List<Holiday>> GetCachedHolidaysAsync()
    {
        if (_cachedHolidays == null || DateTime.UtcNow - _holidayCacheDate > TimeSpan.FromHours(1))
        {
            _cachedHolidays = await _context.Holidays.AsNoTracking().ToListAsync();
            _holidayCacheDate = DateTime.UtcNow;
            _logger.LogInformation("Holiday cache refreshed: {Count} holidays loaded", _cachedHolidays.Count);
        }
        return _cachedHolidays;
    }

    public async Task<bool> IsHolidayAsync(DateTime date)
    {
        try
        {
            var holidays = await GetCachedHolidaysAsync();
            var localDate = _timezoneService.ConvertToLocalTime(date).Date;
            
            var isSpecificHoliday = holidays.Any(h => !h.IsWeekly && h.HolidayDate.Date == localDate);
            if (isSpecificHoliday) return true;
            
            var dayOfWeek = (int)localDate.DayOfWeek;
            var isWeeklyHoliday = holidays.Any(h => h.IsWeekly && h.WeekDay == dayOfWeek);
            
            _logger.LogDebug("IsHolidayAsync({Date}): Specific={Specific}, Weekly={Weekly}", localDate, isSpecificHoliday, isWeeklyHoliday);
            return isWeeklyHoliday;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if date is holiday");
            return false;
        }
    }

    public async Task<List<DateTime>> GetHolidaysInRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var holidays = await GetCachedHolidaysAsync();
            var holidayDates = new List<DateTime>();
            var start = startDate.Date;
            var end = endDate.Date;

            var specificHolidays = holidays
                .Where(h => !h.IsWeekly && h.HolidayDate.Date >= start && h.HolidayDate.Date <= end)
                .Select(h => h.HolidayDate.Date)
                .ToList();
            holidayDates.AddRange(specificHolidays);

            var weeklyHolidays = holidays.Where(h => h.IsWeekly && h.WeekDay.HasValue).ToList();
            
            for (var current = start; current <= end; current = current.AddDays(1))
            {
                var dayOfWeek = (int)current.DayOfWeek;
                if (weeklyHolidays.Any(h => h.WeekDay == dayOfWeek) && !holidayDates.Contains(current))
                {
                    holidayDates.Add(current);
                }
            }

            return holidayDates.OrderBy(d => d).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting holidays in range");
            return new List<DateTime>();
        }
    }

    public async Task<DateTime> GetNextWorkingDayAsync(DateTime date)
    {
        return await GetNextWorkingDayAsync(date, false);
    }

    public async Task<DateTime> GetNextWorkingDayAsync(DateTime date, bool includeSameDay)
    {
        var checkDate = includeSameDay ? date.Date : date.Date.AddDays(1);
        var maxAttempts = 30;
        
        for (int i = 0; i < maxAttempts; i++)
        {
            if (!await IsHolidayAsync(checkDate))
            {
                return checkDate.Add(date.TimeOfDay);
            }
            checkDate = checkDate.AddDays(1);
        }
        
        return checkDate.Add(date.TimeOfDay);
    }

    public async Task<DateTime> AdjustDeadlineForHolidaysAsync(DateTime deadline)
    {
        return await AdjustDeadlineForHolidaysAsync(deadline, deadline.Date.AddDays(7));
    }

    public async Task<DateTime> AdjustDeadlineForHolidaysAsync(DateTime deadline, DateTime stopAtDate)
    {
        return await SkipHolidaysAsync(deadline);
    }

    #endregion

    #region Delay Calculation

    public async Task<TaskDelayInfo> GetTaskDelayInfoAsync(DailyTask dailyTask)
    {
        return await GetHolidayAdjustedDelayInfoAsync(dailyTask);
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

        // Calculate deadline with holiday adjustment
        var utcDeadline = await CalculateDeadline(dailyTask.TaskItem, dailyTask.TaskDate);
        
        // Apply adjustment minutes
        if (dailyTask.AdjustmentMinutes.HasValue && dailyTask.AdjustmentMinutes.Value > 0)
        {
            utcDeadline = utcDeadline.AddMinutes(dailyTask.AdjustmentMinutes.Value);
        }
        
        // Convert to local for comparison
        var localDeadline = _timezoneService.ConvertToLocalTime(utcDeadline);
        var localCompleted = _timezoneService.ConvertToLocalTime(dailyTask.CompletedAt.Value);
        
        // Calculate difference
        var diffMinutes = (localCompleted - localDeadline).TotalMinutes;
        
        // Check if deadline was adjusted for holiday
        var originalDeadline = dailyTask.TaskItem.IsSameDay
            ? _timezoneService.ConvertToLocalTime(dailyTask.TaskDate).Date.Add(dailyTask.TaskItem.Deadline)
            : _timezoneService.ConvertToLocalTime(dailyTask.TaskDate).Date.AddDays(1).Add(dailyTask.TaskItem.Deadline);
            
        var wasAdjusted = localDeadline.Date != originalDeadline.Date;
        var holidayNote = wasAdjusted 
            ? $"Deadline moved from {originalDeadline:MMM d} to {localDeadline:MMM d} for holiday" 
            : "";

        _logger.LogInformation("DELAY CALCULATION: TaskDate={TaskDate}, OriginalDeadline={OriginalDeadline}, AdjustedDeadline={AdjustedDeadline}, Completed={Completed}, DiffMinutes={DiffMinutes}, WasAdjusted={WasAdjusted}",
            dailyTask.TaskDate, originalDeadline, localDeadline, localCompleted, diffMinutes, wasAdjusted);
        
        // Determine on-time status and delay info
        bool isOnTime = diffMinutes >= -5 && diffMinutes <= 5;
        
        string delayType;
        string delayText;
        
        if (diffMinutes < -5)
        {
            delayType = "early";
            delayText = FormatDuration(Math.Abs(diffMinutes), "early");
        }
        else if (diffMinutes >= -5 && diffMinutes <= 5)
        {
            delayType = "on-time";
            delayText = "On Time";
        }
        else
        {
            delayType = "late";
            delayText = FormatDuration(diffMinutes, "late");
        }
        
        return new TaskDelayInfo
        {
            IsOnTime = isOnTime,
            DelayType = delayType,
            DelayText = delayText,
            Deadline = localDeadline,
            CompletedAt = localCompleted,
            AdjustmentMinutes = dailyTask.AdjustmentMinutes,
            WasAdjustedForHoliday = wasAdjusted,
            HolidayAdjustmentNote = holidayNote
        };
    }

    private string FormatDuration(double minutes, string suffix)
    {
        var totalMinutes = (int)Math.Round(minutes);
        var days = totalMinutes / (24 * 60);
        var hours = (totalMinutes / 60) % 24;
        var mins = totalMinutes % 60;
        
        if (days > 0)
        {
            if (hours > 0 && mins > 0) return $"{days}d {hours}h {mins}m {suffix}";
            if (hours > 0) return $"{days}d {hours}h {suffix}";
            if (mins > 0) return $"{days}d {mins}m {suffix}";
            return $"{days}d {suffix}";
        }
        if (hours > 0)
        {
            if (mins > 0) return $"{hours}h {mins}m {suffix}";
            return $"{hours}h {suffix}";
        }
        return $"{mins}m {suffix}";
    }

    #endregion

    #region Legacy Methods

    public bool IsTaskOnTime(DailyTask dailyTask)
    {
        if (dailyTask == null || !dailyTask.IsCompleted || !dailyTask.CompletedAt.HasValue || dailyTask.TaskItem == null)
            return false;

        var deadline = CalculateDeadline(dailyTask.TaskItem, dailyTask.TaskDate).Result;
        
        if (dailyTask.AdjustmentMinutes.HasValue && dailyTask.AdjustmentMinutes.Value > 0)
        {
            deadline = deadline.AddMinutes(dailyTask.AdjustmentMinutes.Value);
        }

        var diffSeconds = (dailyTask.CompletedAt.Value - deadline).TotalSeconds;
        return diffSeconds <= 300;
    }

    public bool IsTaskOnTime(DateTime taskDate, DateTime? completedAt, TimeSpan deadlineTime, bool isSameDay, int? adjustmentMinutes)
    {
        if (!completedAt.HasValue)
            return false;

        var deadline = taskDate.Date;
        deadline = isSameDay ? deadline.Add(deadlineTime) : deadline.AddDays(1).Add(deadlineTime);

        if (adjustmentMinutes.HasValue && adjustmentMinutes.Value > 0)
        {
            deadline = deadline.AddMinutes(adjustmentMinutes.Value);
        }

        var diffSeconds = (completedAt.Value - deadline).TotalSeconds;
        return diffSeconds <= 300;
    }

    #endregion

    #region Task Visibility

    public bool IsTaskVisibleOnDate(TaskItem task, DateTime date)
    {
        try
        {
            if (task == null || !task.IsActive) return false;
            
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
                    
                    if (pattern == "first4days")
                        return date.Day >= 1 && date.Day <= 4;
                    
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
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error in IsTaskVisibleOnDate for task {task?.Name}");
            return false;
        }
    }

    private int GetWeekdayNumber(string weekday) => weekday.ToLower() switch
    {
        "sunday" => 0, "monday" => 1, "tuesday" => 2, "wednesday" => 3,
        "thursday" => 4, "friday" => 5, "saturday" => 6, _ => -1
    };

    private bool IsNthWeekdayOfMonth(DateTime date, string ordinal, int targetWeekday)
    {
        var weekNumber = GetWeekNumber(ordinal);
        if (weekNumber == -1) return false;

        var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
        var firstOccurrence = firstDayOfMonth.AddDays((targetWeekday - (int)firstDayOfMonth.DayOfWeek + 7) % 7);
        var targetDate = firstOccurrence.AddDays(7 * (weekNumber - 1));

        return date.Date == targetDate.Date && date.Month == date.Month;
    }

    private int GetWeekNumber(string ordinal) => ordinal.ToLower() switch
    {
        "first" => 1, "second" => 2, "third" => 3, "fourth" => 4, "fifth" => 5, "last" => 5, _ => -1
    };

    public List<DateTime> GetTaskDatesInRange(TaskItem task, DateTime startDate, DateTime endDate)
    {
        var dates = new List<DateTime>();
        if (task == null) return dates;
        
        try
        {
            var localStartDate = _timezoneService.ConvertToLocalTime(startDate.Date);
            var localEndDate = _timezoneService.ConvertToLocalTime(endDate.Date);
            var maxDate = localEndDate > localStartDate.AddYears(1) ? localStartDate.AddYears(1) : localEndDate;
            
            for (var date = localStartDate; date <= maxDate; date = date.AddDays(1))
            {
                try
                {
                    if (IsTaskVisibleOnDate(task, date))
                    {
                        dates.Add(_timezoneService.ConvertToUtc(date));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking visibility for task {TaskName} on date {Date}", task?.Name, date);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetTaskDatesInRange for task {TaskName}", task?.Name);
        }
        
        return dates;
    }

    public int GetTaskCountInRange(TaskItem task, DateTime startDate, DateTime endDate)
    {
        try
        {
            return GetTaskDatesInRange(task, startDate, endDate).Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating task count for task {TaskName}", task?.Name);
            return 0;
        }
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

            if (employee == null) return new TaskStatistics();

            var assignmentsDuringPeriod = employee.BranchAssignments
                .Where(ba => ba.StartDate.Date <= endDate.Date && 
                            (ba.EndDate == null || ba.EndDate.Value.Date >= startDate.Date))
                .ToList();

            if (!assignmentsDuringPeriod.Any()) return new TaskStatistics();

            var assignedBranchIds = assignmentsDuringPeriod.Select(ba => ba.BranchId).ToList();

            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Where(dt => assignedBranchIds.Contains(dt.BranchId) &&
                             dt.TaskDate.Date >= startDate.Date &&
                             dt.TaskDate.Date <= endDate.Date)
                .ToListAsync();

            var relevantTasks = dailyTasks.Where(dt =>
            {
                var assignment = assignmentsDuringPeriod
                    .FirstOrDefault(ba => ba.BranchId == dt.BranchId &&
                                         dt.TaskDate.Date >= ba.StartDate.Date &&
                                         (ba.EndDate == null || dt.TaskDate.Date <= ba.EndDate.Value.Date));
                return assignment != null && dt.TaskItem != null && !dt.IsDeleted;
            }).ToList();

            var totalTasks = relevantTasks.Count;
            var completedTasks = relevantTasks.Count(dt => dt.IsCompleted);
            
            var onTimeTasks = 0;
            foreach (var dt in relevantTasks.Where(d => d.IsCompleted))
            {
                if ((await GetHolidayAdjustedDelayInfoAsync(dt)).IsOnTime) onTimeTasks++;
            }
            
            var lateTasks = completedTasks - onTimeTasks;
            var pendingTasks = totalTasks - completedTasks;
            
            var (completionRate, onTimeRate, weightedScore) = CalculateScores(totalTasks, completedTasks, onTimeTasks);

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

            if (branch == null) return new BranchTaskStatistics { BranchId = branchId, BranchName = "Unknown" };

            var branchTasks = branch.DailyTasks
                .Where(dt => dt.TaskDate.Date >= startDate.Date && dt.TaskDate.Date <= endDate.Date)
                .ToList();

            var totalTasks = branchTasks.Count;
            var completedTasks = branchTasks.Count(dt => dt.IsCompleted);
            
            var onTimeTasks = 0;
            foreach (var dt in branchTasks.Where(d => d.IsCompleted))
            {
                if ((await GetHolidayAdjustedDelayInfoAsync(dt)).IsOnTime) onTimeTasks++;
            }
            
            var lateTasks = completedTasks - onTimeTasks;
            
            var (completionRate, onTimeRate, weightedScore) = CalculateScores(totalTasks, completedTasks, onTimeTasks);

            var taskBreakdown = new Dictionary<string, int>();
            foreach (var dt in branchTasks.Where(dt => dt.TaskItem != null && dt.IsCompleted))
            {
                var taskName = dt.TaskItem!.Name;
                if (!taskBreakdown.ContainsKey(taskName)) taskBreakdown[taskName] = 0;
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

            if (department == null) return new DepartmentTaskStatistics { DepartmentId = departmentId, DepartmentName = "Unknown" };

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
            var weightedScore = CalculateWeightedScoreStatic(totalTasks, completedTasks, onTimeTasks);

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
            parts.Add(task.EndDate.HasValue 
                ? $"from {startStr} to {task.EndDate.Value.ToString("MMM d, yyyy")}" 
                : $"from {startStr} → FOREVER");
        }

        var recurrenceDetails = GetRecurrenceDetails(task);
        if (!string.IsNullOrEmpty(recurrenceDetails)) parts.Add(recurrenceDetails);

        if (task.AvailableFrom.HasValue)
        {
            var fromStr = task.AvailableFrom.Value.ToString("MMM d, yyyy");
            parts.Add(task.AvailableTo.HasValue 
                ? $"available {fromStr} to {task.AvailableTo.Value.ToString("MMM d, yyyy")}" 
                : $"available from {fromStr} → FOREVER");
        }
        else if (task.AvailableTo.HasValue)
        {
            parts.Add($"available until {task.AvailableTo.Value.ToString("MMM d, yyyy")}");
        }

        if (task.MaxOccurrences.HasValue)
            parts.Add($"max {task.MaxOccurrences.Value} occurrences");
        else if (!task.EndDate.HasValue && task.ExecutionType != TaskExecutionType.OneTime)
            parts.Add("unlimited occurrences");

        return string.Join(" • ", parts);
    }

    public DateTime? GetNextOccurrence(TaskItem task, DateTime fromDate)
    {
        var current = fromDate.Date;
        for (int i = 0; i < 365; i++)
        {
            if (IsTaskVisibleOnDate(task, current)) return current;
            current = current.AddDays(1);
        }
        return null;
    }

    private string GetTaskTypeName(TaskExecutionType type) => type switch
    {
        TaskExecutionType.RecurringDaily => "Daily Task",
        TaskExecutionType.MultiDay => "Multi-Day Task",
        TaskExecutionType.RecurringWeekly => "Weekly Task",
        TaskExecutionType.RecurringMonthly => "Monthly Task",
        TaskExecutionType.OneTime => "One-Time Task",
        _ => "Task"
    };

    private string GetRecurrenceDetails(TaskItem task)
    {
        return task.ExecutionType switch
        {
            TaskExecutionType.RecurringDaily => "daily",
            TaskExecutionType.RecurringWeekly => !string.IsNullOrEmpty(task.WeeklyDays)
                ? $"weekly on {string.Join(", ", task.WeeklyDays.Split(',').Select(int.Parse).Select(GetShortDayName))}"
                : "weekly",
            TaskExecutionType.RecurringMonthly => task.MonthlyPattern?.ToLower() switch
            {
                "last" => "monthly on last day",
                "first4days" => "monthly on first 4 days",
                null => "monthly",
                _ => int.TryParse(task.MonthlyPattern, out var day) ? $"monthly on day {day}" : $"monthly on {task.MonthlyPattern}"
            },
            TaskExecutionType.MultiDay => task.DurationDays.HasValue ? $"{task.DurationDays.Value} days duration" : "multi-day",
            TaskExecutionType.OneTime => "one-time",
            _ => string.Empty
        };
    }

    private string GetShortDayName(int day) => day switch
    {
        0 => "Sun", 1 => "Mon", 2 => "Tue", 3 => "Wed", 4 => "Thu", 5 => "Fri", 6 => "Sat", _ => day.ToString()
    };

    #endregion
}
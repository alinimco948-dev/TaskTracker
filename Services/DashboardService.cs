using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _context;
    private readonly ITaskService _taskService;  // ADD THIS
    private readonly IHolidayService _holidayService;  // ADD THIS
    private readonly ITaskCalculationService _taskCalculationService;
    private readonly ITimezoneService _timezoneService;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        ApplicationDbContext context,
        ITaskService taskService,  // ADD THIS PARAMETER
        IHolidayService holidayService,  // ADD THIS PARAMETER
        ITaskCalculationService taskCalculationService,
        ITimezoneService timezoneService,
        ILogger<DashboardService> logger)
    {
        _context = context;
        _taskService = taskService;  // ADD THIS
        _holidayService = holidayService;  // ADD THIS
        _taskCalculationService = taskCalculationService;
        _timezoneService = timezoneService;
        _logger = logger;
    }

    public async Task<DashboardViewModel> GetDashboardViewModelAsync(DateTime localDate)
    {
        try
        {
            _logger.LogInformation("Getting dashboard view model for date: {LocalDate}", localDate);
            
            var utcStart = _timezoneService.GetStartOfDayLocal(localDate);
            var utcEnd = _timezoneService.GetEndOfDayLocal(localDate);
            
            // Get branches
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();
            
            // Get tasks visible on this date
            var tasks = await _taskService.GetTasksVisibleOnDateAsync(localDate);
            
            // Get task data for the date
            var taskData = new Dictionary<string, DailyTask>();
            var dailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Where(dt => dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd)
                .ToListAsync();
            
            foreach (var dt in dailyTasks)
            {
                taskData[$"{dt.BranchId}_{dt.TaskItemId}"] = dt;
            }
            
            // Get branch assignments
            var branchAssignments = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .Where(ba => ba.StartDate <= utcEnd && (ba.EndDate == null || ba.EndDate >= utcStart))
                .ToDictionaryAsync(ba => ba.BranchId, ba => ba.Employee?.Name ?? "");
            
            // Get notes data
            var notesData = await _context.Branches
                .Where(b => b.IsActive)
                .ToDictionaryAsync(b => b.Id.ToString(), b => b.Notes ?? "");
            
            // Check if today is a holiday (using the holiday service)
            var isHoliday = await _holidayService.IsHolidayAsync(utcStart);
            var holidayName = "";
            
            if (isHoliday)
            {
                var holidays = await _holidayService.GetAllHolidaysAsync();
                var holiday = holidays.FirstOrDefault(h => 
                    (h.IsWeekly && h.WeekDay == (int)localDate.DayOfWeek) ||
                    (!h.IsWeekly && h.HolidayDate.Date == localDate.Date));
                holidayName = holiday?.Description ?? (isHoliday ? "Holiday" : "");
            }
            
            return new DashboardViewModel
            {
                CurrentDate = localDate,
                Branches = branches,
                Tasks = tasks,
                TaskData = taskData,
                BranchAssignments = branchAssignments,
                NotesData = notesData,
                IsHoliday = isHoliday,
                HolidayName = holidayName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard view model");
            throw;
        }
    }

    private async Task<Dictionary<string, DailyTask>> GetTaskDataDictionaryAsync(DateTime utcStart, DateTime utcEnd)
    {
        try
        {
            var tasks = await _context.DailyTasks
                .Include(d => d.TaskAssignment)
                    .ThenInclude(ta => ta != null ? ta.Employee : null)
                .Where(d => d.TaskDate >= utcStart && d.TaskDate <= utcEnd)
                .AsNoTracking()
                .ToListAsync();

            var dict = new Dictionary<string, DailyTask>();
            foreach (var task in tasks)
            {
                dict[$"{task.BranchId}_{task.TaskItemId}"] = task;
            }
            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task data dictionary");
            return new Dictionary<string, DailyTask>();
        }
    }

    private async Task<Dictionary<int, List<string>>> GetBranchEmployeesAsync(DateTime utcStart, DateTime utcEnd)
    {
        try
        {
            var assignments = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .Where(ba => ba.StartDate <= utcEnd &&
                             (ba.EndDate == null || ba.EndDate >= utcStart))
                .AsNoTracking()
                .ToListAsync();

            return assignments
                .Where(ba => ba.Employee != null)
                .GroupBy(ba => ba.BranchId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(ba => ba.Employee!.Name).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch employees");
            return new Dictionary<int, List<string>>();
        }
    }

    private async Task<Dictionary<string, string>> GetNotesDataAsync()
    {
        try
        {
            return await _context.Branches
                .Where(b => !string.IsNullOrEmpty(b.Notes))
                .ToDictionaryAsync(b => b.Id.ToString(), b => b.Notes ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notes data");
            return new Dictionary<string, string>();
        }
    }

    private async Task<Dictionary<int, List<string>>> GetHiddenTasksAsync()
    {
        try
        {
            var hiddenTasks = new Dictionary<int, List<string>>();
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .AsNoTracking()
                .ToListAsync();

            foreach (var branch in branches)
            {
                hiddenTasks[branch.Id] = branch.HiddenTasks ?? new List<string>();
            }

            return hiddenTasks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hidden tasks");
            return new Dictionary<int, List<string>>();
        }
    }

    private async Task<List<TaskItem>> GetTasksVisibleOnDateAsync(DateTime utcDate)
    {
        try
        {
            var allTasks = await _context.TaskItems
                .Where(t => t.IsActive)
                .OrderBy(t => t.DisplayOrder)
                .AsNoTracking()
                .ToListAsync();

            var localDate = _timezoneService.ConvertToLocalTime(utcDate);
            _logger.LogInformation($"Getting visible tasks for localDate: {localDate:yyyy-MM-dd}");
            
            var visibleTasks = new List<TaskItem>();
            foreach (var task in allTasks)
            {
                try
                {
                    if (_taskCalculationService.IsTaskVisibleOnDate(task, localDate))
                    {
                        visibleTasks.Add(task);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking visibility for task {TaskId}", task.Id);
                }
            }
            
            return visibleTasks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting visible tasks for date {Date}", utcDate);
            return new List<TaskItem>();
        }
    }

    private string GetHolidayNameFromDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Friday => "Friday Holiday",
            DayOfWeek.Saturday => "Saturday Holiday",
            DayOfWeek.Sunday => "Sunday Holiday",
            _ => $"{dayOfWeek} Holiday"
        };
    }
}
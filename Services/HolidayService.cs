using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class HolidayService : IHolidayService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<HolidayService> _logger;
    private readonly IAuditService _auditService;

    public HolidayService(
        ApplicationDbContext context,
        ILogger<HolidayService> logger,
        IAuditService auditService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<List<Holiday>> GetAllHolidaysAsync()
    {
        try
        {
            return await _context.Holidays
                .OrderBy(h => h.IsWeekly)
                .ThenBy(h => h.WeekDay)
                .ThenBy(h => h.HolidayDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all holidays");
            return new List<Holiday>();
        }
    }

    public async Task<Holiday?> GetHolidayByIdAsync(int id)
    {
        return await _context.Holidays.FindAsync(id);
    }

    public async Task<List<Holiday>> GetWeeklyHolidaysAsync()
    {
        return await _context.Holidays
            .Where(h => h.IsWeekly)
            .OrderBy(h => h.WeekDay)
            .ToListAsync();
    }

    public async Task<List<Holiday>> GetSpecificHolidaysAsync()
    {
        return await _context.Holidays
            .Where(h => !h.IsWeekly)
            .OrderBy(h => h.HolidayDate)
            .ToListAsync();
    }

    public async Task<Holiday> AddWeeklyHolidayAsync(int weekDay, string? description = null)
    {
        try
        {
            var dayName = GetDayOfWeekName(weekDay);
            
            // Check if weekly holiday already exists for this day
            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => h.IsWeekly && h.WeekDay == weekDay);

            if (existing != null)
            {
                _logger.LogInformation($"Weekly holiday for {dayName} already exists");
                return existing;
            }

            var holiday = new Holiday
            {
                HolidayDate = new DateTime(2000, 1, 1), // Placeholder date (not used for weekly)
                IsWeekly = true,
                WeekDay = weekDay,
                Description = description ?? dayName
            };

            _context.Holidays.Add(holiday);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Create",
                "Holiday",
                holiday.Id,
                $"Added weekly holiday for {dayName}"
            );

            _logger.LogInformation("Added weekly holiday for {DayName}", dayName);
            return holiday;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding weekly holiday for day {WeekDay}", weekDay);
            throw;
        }
    }

    public async Task<Holiday> AddSpecificHolidayAsync(DateTime date, string? description = null)
    {
        try
        {
            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => !h.IsWeekly && h.HolidayDate.Date == date.Date);

            if (existing != null)
            {
                throw new InvalidOperationException($"Holiday already exists for {date:yyyy-MM-dd}");
            }

            var holiday = new Holiday
            {
                HolidayDate = date.Date,
                IsWeekly = false,
                WeekDay = null,
                Description = description ?? $"Holiday on {date:MMMM d, yyyy}"
            };

            _context.Holidays.Add(holiday);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Create",
                "Holiday",
                holiday.Id,
                $"Added specific holiday for {date:yyyy-MM-dd}"
            );

            _logger.LogInformation("Added specific holiday for {Date}", date);
            return holiday;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding specific holiday for date {Date}", date);
            throw;
        }
    }

    public async Task<bool> RemoveHolidayAsync(int id)
    {
        try
        {
            var holiday = await _context.Holidays.FindAsync(id);
            if (holiday == null) return false;

            var description = holiday.Description;
            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Delete",
                "Holiday",
                holiday.Id,
                $"Removed holiday: {description}"
            );

            _logger.LogInformation("Removed holiday: {Description}", description);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing holiday {Id}", id);
            return false;
        }
    }

    public async Task<bool> RemoveWeeklyHolidayAsync(int weekDay)
    {
        try
        {
            var dayName = GetDayOfWeekName(weekDay);
            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => h.IsWeekly && h.WeekDay == weekDay);

            if (holiday == null) return false;

            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Delete",
                "Holiday",
                holiday.Id,
                $"Removed weekly holiday for {dayName}"
            );

            _logger.LogInformation("Removed weekly holiday for {DayName}", dayName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing weekly holiday for day {WeekDay}", weekDay);
            return false;
        }
    }

    public async Task<bool> RemoveSpecificHolidayAsync(DateTime date)
    {
        try
        {
            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => !h.IsWeekly && h.HolidayDate.Date == date.Date);

            if (holiday == null) return false;

            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Delete",
                "Holiday",
                holiday.Id,
                $"Removed specific holiday for {date:yyyy-MM-dd}"
            );

            _logger.LogInformation("Removed specific holiday for {Date}", date);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing specific holiday for date {Date}", date);
            return false;
        }
    }

    public async Task<bool> IsHolidayAsync(DateTime date)
    {
        try
        {
            var dateOnly = date.Date;

            // Check specific holidays
            var isSpecific = await _context.Holidays
                .AnyAsync(h => !h.IsWeekly && h.HolidayDate.Date == dateOnly);

            if (isSpecific) return true;

            // Check weekly holidays (based on day of week)
            var dayOfWeek = (int)date.DayOfWeek;
            var isWeekly = await _context.Holidays
                .AnyAsync(h => h.IsWeekly && h.WeekDay == dayOfWeek);

            return isWeekly;
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
            var holidays = new List<DateTime>();

            // Get specific holidays in range
            var specific = await _context.Holidays
                .Where(h => !h.IsWeekly &&
                           h.HolidayDate.Date >= startDate.Date &&
                           h.HolidayDate.Date <= endDate.Date)
                .Select(h => h.HolidayDate.Date)
                .ToListAsync();

            holidays.AddRange(specific);

            // Get weekly holidays
            var weekly = await _context.Holidays
                .Where(h => h.IsWeekly && h.WeekDay.HasValue)
                .ToListAsync();

            var current = startDate.Date;
            while (current <= endDate.Date)
            {
                var dayOfWeek = (int)current.DayOfWeek;
                if (weekly.Any(h => h.WeekDay == dayOfWeek))
                {
                    holidays.Add(current);
                }
                current = current.AddDays(1);
            }

            return holidays.Distinct().OrderBy(d => d).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting holidays in range");
            return new List<DateTime>();
        }
    }

    private string GetDayOfWeekName(int weekDay)
    {
        return weekDay switch
        {
            0 => "Sunday",
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            _ => $"Day {weekDay}"
        };
    }
}
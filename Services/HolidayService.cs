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
            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => h.IsWeekly && h.WeekDay == weekDay);

            if (existing != null)
            {
                return existing;
            }

            var holiday = new Holiday
            {
                HolidayDate = DateTime.Today,
                IsWeekly = true,
                WeekDay = weekDay,
                Description = description ?? GetDayOfWeekName(weekDay)
            };

            _context.Holidays.Add(holiday);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Create",
                "Holiday",
                holiday.Id,
                $"Added weekly holiday for {GetDayOfWeekName(weekDay)}"
            );

            return holiday;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding weekly holiday");
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

            return holiday;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding specific holiday");
            throw;
        }
    }

    public async Task<bool> RemoveHolidayAsync(int id)
    {
        try
        {
            var holiday = await _context.Holidays.FindAsync(id);
            if (holiday == null) return false;

            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Delete",
                "Holiday",
                holiday.Id,
                $"Removed holiday: {holiday.Description}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing holiday");
            return false;
        }
    }

    public async Task<bool> RemoveWeeklyHolidayAsync(int weekDay)
    {
        try
        {
            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => h.IsWeekly && h.WeekDay == weekDay);

            if (holiday == null) return false;

            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Delete",
                "Holiday",
                holiday.Id,
                $"Removed weekly holiday for {GetDayOfWeekName(weekDay)}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing weekly holiday");
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

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing specific holiday");
            return false;
        }
    }

    public async Task<bool> IsHolidayAsync(DateTime date)
    {
        var dateOnly = date.Date;

        var isSpecific = await _context.Holidays
            .AnyAsync(h => !h.IsWeekly && h.HolidayDate.Date == dateOnly);

        if (isSpecific) return true;

        var dayOfWeek = (int)date.DayOfWeek;
        var isWeekly = await _context.Holidays
            .AnyAsync(h => h.IsWeekly && h.WeekDay == dayOfWeek);

        return isWeekly;
    }

    public async Task<List<DateTime>> GetHolidaysInRangeAsync(DateTime startDate, DateTime endDate)
    {
        var holidays = new List<DateTime>();

        var specific = await _context.Holidays
            .Where(h => !h.IsWeekly &&
                       h.HolidayDate.Date >= startDate.Date &&
                       h.HolidayDate.Date <= endDate.Date)
            .Select(h => h.HolidayDate.Date)
            .ToListAsync();

        holidays.AddRange(specific);

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
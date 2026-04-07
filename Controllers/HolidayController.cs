using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Controllers;


public class HolidayController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IHolidayService _holidayService;
    private readonly ILogger<HolidayController> _logger;

    public HolidayController(
        ApplicationDbContext context,
        IHolidayService holidayService,
        ILogger<HolidayController> logger)
    {
        _context = context;
        _holidayService = holidayService;
        _logger = logger;
    }

    // GET: Holiday
    public async Task<IActionResult> Index()
    {
        var holidays = await _context.Holidays
            .OrderBy(h => h.IsWeekly)
            .ThenBy(h => h.WeekDay)
            .ThenBy(h => h.HolidayDate)
            .AsNoTracking()
            .ToListAsync();

        return View(holidays);
    }

    // POST: Holiday/ToggleWeekly
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleWeekly(int weekDay)
    {
        try
        {
            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => h.IsWeekly && h.WeekDay == weekDay);

            if (existing != null)
            {
                _context.Holidays.Remove(existing);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Weekly holiday removed for {GetDayOfWeekName(weekDay)}" });
            }
            else
            {
                var holiday = new Holiday
                {
                    HolidayDate = DateTime.Today,
                    IsWeekly = true,
                    WeekDay = weekDay,
                    Description = GetDayOfWeekName(weekDay)
                };
                _context.Holidays.Add(holiday);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Weekly holiday added for {GetDayOfWeekName(weekDay)}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling weekly holiday");
            return Json(new { success = false, message = "Error updating holiday" });
        }
    }

    // POST: Holiday/AddWeeklyWithDescription
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddWeeklyWithDescription(int weekDay, string description)
    {
        try
        {
            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => h.IsWeekly && h.WeekDay == weekDay);

            if (existing != null)
            {
                existing.Description = string.IsNullOrEmpty(description) ? GetDayOfWeekName(weekDay) : description;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Updated description for {GetDayOfWeekName(weekDay)}" });
            }
            else
            {
                var holiday = new Holiday
                {
                    HolidayDate = DateTime.Today,
                    IsWeekly = true,
                    WeekDay = weekDay,
                    Description = string.IsNullOrEmpty(description) ? GetDayOfWeekName(weekDay) : description
                };
                _context.Holidays.Add(holiday);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Added {GetDayOfWeekName(weekDay)} as weekly holiday" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding weekly holiday with description");
            return Json(new { success = false, message = "Error updating holiday" });
        }
    }

    // POST: Holiday/AddSpecific (AJAX compatible)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSpecific(DateTime date, string description = "")
    {
        try
        {
            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => !h.IsWeekly && h.HolidayDate.Date == date.Date);

            if (existing != null)
            {
                return Json(new { success = false, message = $"Holiday already exists for {date:MMMM d, yyyy}" });
            }

            var holiday = new Holiday
            {
                HolidayDate = date.Date,
                IsWeekly = false,
                Description = string.IsNullOrEmpty(description) ? $"Holiday on {date:MMMM d, yyyy}" : description
            };

            _context.Holidays.Add(holiday);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Holiday added for {date:MMMM d, yyyy}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding specific holiday");
            return Json(new { success = false, message = "Error adding holiday" });
        }
    }

    // POST: Holiday/UpdateSpecificDescription
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSpecificDescription(DateTime date, string description)
    {
        try
        {
            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => !h.IsWeekly && h.HolidayDate.Date == date.Date);

            if (holiday == null)
            {
                return Json(new { success = false, message = "Holiday not found" });
            }

            holiday.Description = string.IsNullOrEmpty(description) ? $"Holiday on {date:MMMM d, yyyy}" : description;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Description updated for {date:MMMM d, yyyy}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating specific holiday description");
            return Json(new { success = false, message = "Error updating description" });
        }
    }

    // POST: Holiday/RemoveSpecific
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveSpecific(DateTime date)
    {
        try
        {
            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => !h.IsWeekly && h.HolidayDate.Date == date.Date);

            if (holiday != null)
            {
                _context.Holidays.Remove(holiday);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Holiday removed for {date:MMMM d, yyyy}" });
            }

            return Json(new { success = false, message = "Holiday not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing specific holiday");
            return Json(new { success = false, message = "Error removing holiday" });
        }
    }

    // POST: Holiday/RemoveWeekly
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveWeekly(int weekDay)
    {
        try
        {
            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h => h.IsWeekly && h.WeekDay == weekDay);

            if (holiday != null)
            {
                _context.Holidays.Remove(holiday);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Weekly holiday removed for {GetDayOfWeekName(weekDay)}" });
            }

            return Json(new { success = false, message = "Holiday not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing weekly holiday");
            return Json(new { success = false, message = "Error removing holiday" });
        }
    }

    // POST: Holiday/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var holiday = await _context.Holidays.FindAsync(id);
            if (holiday != null)
            {
                _context.Holidays.Remove(holiday);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Holiday deleted successfully!" });
            }
            return Json(new { success = false, message = "Holiday not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting holiday");
            return Json(new { success = false, message = "Error deleting holiday" });
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
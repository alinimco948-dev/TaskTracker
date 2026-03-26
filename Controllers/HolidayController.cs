using Microsoft.AspNetCore.Mvc;
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
            .ToListAsync();

        return View(holidays);
    }

    // POST: Holiday/ToggleWeekly
    [HttpPost]
    public async Task<IActionResult> ToggleWeekly(int weekDay)
    {
        try
        {
            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => h.IsWeekly && h.WeekDay == weekDay);

            if (existing != null)
            {
                _context.Holidays.Remove(existing);
                TempData["SuccessMessage"] = $"Weekly holiday removed";
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
                TempData["SuccessMessage"] = $"Weekly holiday added for {GetDayOfWeekName(weekDay)}";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling weekly holiday");
            TempData["ErrorMessage"] = "Error updating holiday";
            return RedirectToAction(nameof(Index));
        }
    }

    // POST: Holiday/AddSpecific
    [HttpPost]
    public async Task<IActionResult> AddSpecific(DateTime date, string description = "")
    {
        try
        {
            var existing = await _context.Holidays
                .FirstOrDefaultAsync(h => !h.IsWeekly && h.HolidayDate.Date == date.Date);

            if (existing != null)
            {
                TempData["ErrorMessage"] = "Holiday already exists for this date";
                return RedirectToAction(nameof(Index));
            }

            var holiday = new Holiday
            {
                HolidayDate = date,
                IsWeekly = false,
                Description = string.IsNullOrEmpty(description) ? $"Holiday on {date:MMMM d, yyyy}" : description
            };

            _context.Holidays.Add(holiday);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Holiday added for {date:MMMM d, yyyy}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding specific holiday");
            TempData["ErrorMessage"] = "Error adding holiday";
            return RedirectToAction(nameof(Index));
        }
    }

    // POST: Holiday/RemoveSpecific
    [HttpPost]
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
                TempData["SuccessMessage"] = $"Holiday removed for {date:MMMM d, yyyy}";
            }

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing specific holiday");
            TempData["ErrorMessage"] = "Error removing holiday";
            return RedirectToAction(nameof(Index));
        }
    }

    // POST: Holiday/RemoveWeekly
    [HttpPost]
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
                TempData["SuccessMessage"] = $"Weekly holiday removed for {GetDayOfWeekName(weekDay)}";
            }

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing weekly holiday");
            TempData["ErrorMessage"] = "Error removing holiday";
            return RedirectToAction(nameof(Index));
        }
    }

    // POST: Holiday/Delete/5
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var holiday = await _context.Holidays.FindAsync(id);
            if (holiday != null)
            {
                _context.Holidays.Remove(holiday);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Holiday deleted successfully!";
            }
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting holiday");
            TempData["ErrorMessage"] = "Error deleting holiday";
            return RedirectToAction(nameof(Index));
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
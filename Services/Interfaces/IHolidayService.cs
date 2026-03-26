using TaskTracker.Models.Entities;

namespace TaskTracker.Services.Interfaces;

public interface IHolidayService
{
    Task<List<Holiday>> GetAllHolidaysAsync();
    Task<Holiday?> GetHolidayByIdAsync(int id);
    Task<List<Holiday>> GetWeeklyHolidaysAsync();
    Task<List<Holiday>> GetSpecificHolidaysAsync();
    Task<Holiday> AddWeeklyHolidayAsync(int weekDay, string? description = null);
    Task<Holiday> AddSpecificHolidayAsync(DateTime date, string? description = null);
    Task<bool> RemoveHolidayAsync(int id);
    Task<bool> RemoveWeeklyHolidayAsync(int weekDay);
    Task<bool> RemoveSpecificHolidayAsync(DateTime date);
    Task<bool> IsHolidayAsync(DateTime date);
    Task<List<DateTime>> GetHolidaysInRangeAsync(DateTime startDate, DateTime endDate);
}
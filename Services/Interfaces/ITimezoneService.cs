using System;

namespace TaskTracker.Services.Interfaces;

public interface ITimezoneService
{
    DateTime ConvertToLocalTime(DateTime utcDate);
    DateTime ConvertToUtc(DateTime localDate);
    DateTime GetCurrentLocalTime();
    DateTime GetStartOfDayLocal(DateTime localDate);
    DateTime GetEndOfDayLocal(DateTime localDate);
    string GetTimezoneDisplayName();
}
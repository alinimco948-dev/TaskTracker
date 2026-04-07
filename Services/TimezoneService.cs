using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class TimezoneService : ITimezoneService
{
    private readonly ILogger<TimezoneService> _logger;
    private readonly TimeZoneInfo _timeZoneInfo;

    public TimezoneService(IConfiguration configuration, ILogger<TimezoneService> logger)
    {
        _logger = logger;
        var timeZoneId = configuration["AppSettings:TimeZoneId"] ?? "E. Africa Standard Time";
        
        TimeZoneInfo? foundZone = null;
        
        // Try to find by Windows ID first
        try
        {
            foundZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            _logger.LogInformation("Found timezone by Windows ID: {TimeZoneId}", timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning("TimeZone {TimeZoneId} not found as Windows ID, trying IANA...", timeZoneId);
            
            var ianaMapping = new Dictionary<string, string>
            {
                { "E. Africa Standard Time", "Africa/Nairobi" },
                { "Arab Standard Time", "Asia/Riyadh" },
                { "Israel Standard Time", "Asia/Jerusalem" },
                { "GMT Standard Time", "Europe/London" },
                { "W. Europe Standard Time", "Europe/Berlin" },
                { "Eastern Standard Time", "America/New_York" },
                { "Central Standard Time", "America/Chicago" },
                { "Mountain Standard Time", "America/Denver" },
                { "Pacific Standard Time", "America/Los_Angeles" },
                { "South Africa Standard Time", "Africa/Johannesburg" },
                { "Turkey Standard Time", "Europe/Istanbul" },
                { "Russian Standard Time", "Europe/Moscow" },
                { "India Standard Time", "Asia/Kolkata" },
                { "China Standard Time", "Asia/Shanghai" },
                { "Japan Standard Time", "Asia/Tokyo" },
                { "AUS Eastern Standard Time", "Australia/Sydney" }
            };
            
            if (ianaMapping.TryGetValue(timeZoneId, out var ianaId))
            {
                try
                {
                    foundZone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
                    _logger.LogInformation("Found timezone by IANA ID: {IanaId}", ianaId);
                }
                catch (TimeZoneNotFoundException)
                {
                    _logger.LogWarning("IANA ID {IanaId} not found", ianaId);
                }
            }
        }
        
        _timeZoneInfo = foundZone ?? TimeZoneInfo.Utc;
        _logger.LogInformation("Timezone initialized: {TimeZoneId} (UTC offset: {Offset})", 
            _timeZoneInfo.Id, _timeZoneInfo.BaseUtcOffset);
    }

    public DateTime ConvertToLocalTime(DateTime utcDate)
    {
        DateTime utcDateTime;
        
        if (utcDate.Kind == DateTimeKind.Unspecified)
        {
            utcDateTime = DateTime.SpecifyKind(utcDate, DateTimeKind.Utc);
            _logger.LogDebug("Converted Unspecified DateTime to UTC: {Date}", utcDateTime);
        }
        else if (utcDate.Kind == DateTimeKind.Local)
        {
            utcDateTime = utcDate.ToUniversalTime();
            _logger.LogDebug("Converted Local DateTime to UTC: {Date}", utcDateTime);
        }
        else
        {
            utcDateTime = utcDate;
        }
        
        try
        {
            var result = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, _timeZoneInfo);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting UTC to local time for date {Date}", utcDateTime);
            return utcDateTime;
        }
    }

  public DateTime ConvertToUtc(DateTime localDate)
{
    // Debug log
    _logger.LogInformation($"ConvertToUtc called with: {localDate}, Kind: {localDate.Kind}");
    
    // If the date already has UTC kind, return as is
    if (localDate.Kind == DateTimeKind.Utc)
    {
        _logger.LogInformation("Date already UTC, returning as is");
        return localDate;
    }
    
    // For dates coming from JavaScript datetime-local (string parsed to DateTime)
    // They come as Unspecified but are actually LOCAL time
    if (localDate.Kind == DateTimeKind.Unspecified)
    {
        // Get the offset for the configured timezone (for UTC+3, offset is +3 hours)
        var offset = _timeZoneInfo.GetUtcOffset(localDate);
        // Subtract offset to get UTC (10 PM local - 3 hours = 7 PM UTC)
        var result = localDate.Subtract(offset);
        _logger.LogInformation("Local time: {Local}, Offset: {Offset}, UTC: {Result}", localDate, offset, result);
        return DateTime.SpecifyKind(result, DateTimeKind.Utc);
    }
    
    // Otherwise convert from local to UTC using configured timezone
    try
    {
        var result = TimeZoneInfo.ConvertTimeToUtc(localDate, _timeZoneInfo);
        _logger.LogInformation("Converted from local {Local} to UTC {Utc}", localDate, result);
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error converting local to UTC for date {Date}", localDate);
        // Fallback: subtract offset
        var offset = _timeZoneInfo.GetUtcOffset(localDate);
        return DateTime.SpecifyKind(localDate.Subtract(offset), DateTimeKind.Utc);
    }
}
  
  
  
  
      public DateTime GetCurrentLocalTime()
    {
        return ConvertToLocalTime(DateTime.UtcNow);
    }

    public DateTime GetStartOfDayLocal(DateTime localDate)
    {
        DateTime localDateTime;
        
        if (localDate.Kind == DateTimeKind.Unspecified)
        {
            localDateTime = DateTime.SpecifyKind(localDate, DateTimeKind.Local);
        }
        else if (localDate.Kind == DateTimeKind.Utc)
        {
            localDateTime = ConvertToLocalTime(localDate);
        }
        else
        {
            localDateTime = localDate;
        }
        
        var startOfDay = localDateTime.Date;
        return ConvertToUtc(startOfDay);
    }

    public DateTime GetEndOfDayLocal(DateTime localDate)
    {
        DateTime localDateTime;
        
        if (localDate.Kind == DateTimeKind.Unspecified)
        {
            localDateTime = DateTime.SpecifyKind(localDate, DateTimeKind.Local);
        }
        else if (localDate.Kind == DateTimeKind.Utc)
        {
            localDateTime = ConvertToLocalTime(localDate);
        }
        else
        {
            localDateTime = localDate;
        }
        
        var endOfDay = localDateTime.Date.AddDays(1).AddSeconds(-1);
        return ConvertToUtc(endOfDay);
    }

    public string GetTimezoneDisplayName()
    {
        return _timeZoneInfo.DisplayName;
    }
}
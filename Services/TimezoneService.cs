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
            
            // Try to find by IANA ID (for Linux)
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
        // Ensure the date has UTC kind
        DateTime utcDateTime;
        
        if (utcDate.Kind == DateTimeKind.Unspecified)
        {
            // Assume unspecified means UTC
            utcDateTime = DateTime.SpecifyKind(utcDate, DateTimeKind.Utc);
            _logger.LogDebug("Converted Unspecified DateTime to UTC: {Date}", utcDateTime);
        }
        else if (utcDate.Kind == DateTimeKind.Local)
        {
            // Convert local to UTC first
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
            // Fallback to UTC
            return utcDateTime;
        }
    }

    public DateTime ConvertToUtc(DateTime localDate)
    {
        // Ensure the date has appropriate kind
        DateTime localDateTime;
        
        if (localDate.Kind == DateTimeKind.Unspecified)
        {
            // Assume unspecified means local time in the configured timezone
            localDateTime = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
            _logger.LogDebug("Converting Unspecified DateTime to UTC using configured timezone: {Date}", localDateTime);
        }
        else if (localDate.Kind == DateTimeKind.Utc)
        {
            // Already UTC
            return localDate;
        }
        else
        {
            localDateTime = localDate;
        }
        
        try
        {
            // Convert from the configured timezone to UTC
            var result = TimeZoneInfo.ConvertTimeToUtc(localDateTime, _timeZoneInfo);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting local to UTC for date {Date}", localDateTime);
            // Fallback to assuming it's already UTC
            return DateTime.SpecifyKind(localDateTime, DateTimeKind.Utc);
        }
    }

    public DateTime GetCurrentLocalTime()
    {
        return ConvertToLocalTime(DateTime.UtcNow);
    }

    public DateTime GetStartOfDayLocal(DateTime localDate)
    {
        // First ensure we have a local date
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
        // First ensure we have a local date
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
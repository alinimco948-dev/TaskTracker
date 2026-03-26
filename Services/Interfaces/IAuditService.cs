using TaskTracker.Models.Entities;

namespace TaskTracker.Services.Interfaces;

public interface IAuditService
{
    Task LogAsync(string action, string entityType, int? entityId, string description, string? changes = null, string? oldValues = null, string? newValues = null);
    Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startDate = null, DateTime? endDate = null, string? action = null, string? entityType = null);
    Task<List<string>> GetDistinctActionsAsync();
    Task<List<string>> GetDistinctEntityTypesAsync();
}
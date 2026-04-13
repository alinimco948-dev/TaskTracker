using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;
using ViewHelpers = TaskTracker.Models.ViewModels.ViewHelpers;

namespace TaskTracker.Services;

public class DepartmentService : IDepartmentService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DepartmentService> _logger;
    private readonly IAuditService _auditService;
    private readonly ITimezoneService _timezoneService;

    public DepartmentService(
        ApplicationDbContext context,
        ILogger<DepartmentService> logger,
        IAuditService auditService,
        ITimezoneService timezoneService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
        _timezoneService = timezoneService;
    }

    public async Task<List<DepartmentListViewModel>> GetAllDepartmentsAsync()
    {
        try
        {
            var departments = await _context.Departments
                .Include(d => d.Branches)
                .Include(d => d.Employees)
                .OrderBy(d => d.Name)
                .ToListAsync();

            return departments.Select(d => new DepartmentListViewModel
            {
                Id = d.Id,
                Name = d.Name,
                Code = d.Code ?? string.Empty,
                Description = d.Description ?? string.Empty,
                BranchCount = d.Branches?.Count(b => b.IsActive) ?? 0,
                EmployeeCount = d.Employees?.Count(e => e.IsActive) ?? 0,
                IsActive = d.IsActive
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all departments");
            return new List<DepartmentListViewModel>();
        }
    }

    public async Task<Department?> GetDepartmentByIdAsync(int id)
    {
        return await _context.Departments
            .Include(d => d.Branches)
            .Include(d => d.Employees)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Department> CreateDepartmentAsync(DepartmentViewModel model)
    {
        try
        {
            var department = new Department
            {
                Name = model.Name,
                Code = model.Code,
                Description = model.Description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Departments.Add(department);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Create",
                "Department",
                department.Id,
                $"Created department: {department.Name}"
            );

            return department;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating department");
            throw;
        }
    }

    public async Task<Department?> UpdateDepartmentAsync(DepartmentViewModel model)
    {
        try
        {
            var department = await _context.Departments.FindAsync(model.Id);
            if (department == null) return null;

            var oldValues = $"Name:{department.Name}, Code:{department.Code}";

            department.Name = model.Name;
            department.Code = model.Code;
            department.Description = model.Description;
            department.IsActive = model.IsActive;
            department.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var newValues = $"Name:{department.Name}, Code:{department.Code}";

            await _auditService.LogAsync(
                "Update",
                "Department",
                department.Id,
                $"Updated department: {department.Name}",
                null,
                oldValues,
                newValues
            );

            return department;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating department {Id}", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteDepartmentAsync(int id)
    {
        try
        {
            var department = await _context.Departments.FindAsync(id);
            if (department == null) return false;

            // Check if department has branches or employees
            var hasBranches = await _context.Branches.AnyAsync(b => b.DepartmentId == id && b.IsActive);
            var hasEmployees = await _context.Employees.AnyAsync(e => e.DepartmentId == id && e.IsActive);

            if (hasBranches || hasEmployees)
            {
                _logger.LogWarning("Cannot delete department {Id} with active branches or employees", id);
                return false;
            }

            _context.Departments.Remove(department);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Delete",
                "Department",
                department.Id,
                $"Deleted department: {department.Name}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting department {Id}", id);
            return false;
        }
    }

    public async Task<DepartmentDetailsViewModel> GetDepartmentDetailsAsync(int id)
    {
        try
        {
            var department = await _context.Departments
                .Include(d => d.Branches)
                .Include(d => d.Employees)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (department == null)
                throw new Exception($"Department {id} not found");

            var branchList = department.Branches != null && department.Branches.Any()
                ? department.Branches.Where(b => b.IsActive).ToList()
                : new List<Branch>();

            var branchIds = branchList.Select(b => b.Id).ToList();
            var branchCompletionRates = new Dictionary<int, double>();
            
            if (branchIds.Any())
            {
                var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
                var utcStart = _timezoneService.GetStartOfDayLocal(todayLocal.AddDays(-7));
                var utcEnd = _timezoneService.GetEndOfDayLocal(todayLocal);
                
                var taskStats = await _context.DailyTasks
                    .Where(dt => branchIds.Contains(dt.BranchId) && dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd)
                    .GroupBy(dt => dt.BranchId)
                    .Select(g => new { BranchId = g.Key, Total = g.Count(), Completed = g.Count(x => x.IsCompleted) })
                    .ToListAsync();
                    
                branchCompletionRates = taskStats.ToDictionary(x => x.BranchId, x => x.Total > 0 ? Math.Round((double)x.Completed / x.Total * 100, 1) : 0);
            }

            var branches = branchList.Select(b => new DepartmentBranchItem
            {
                Id = b.Id,
                Name = b.Name,
                Address = b.Address ?? string.Empty,
                CompletionRate = branchCompletionRates.GetValueOrDefault(b.Id, 0)
            }).ToList();

            var employees = department.Employees != null && department.Employees.Any()
                ? department.Employees.Where(e => e.IsActive).Select(e => new DepartmentEmployeeItem
                {
                    Id = e.Id,
                    Name = e.Name,
                    Position = e.Position ?? string.Empty,
                    Initials = ViewHelpers.GetInitials(e.Name)
                }).ToList()
                : new List<DepartmentEmployeeItem>();

            var totalTasks = await GetDepartmentTaskCountAsync(id);
            var completedTasks = await GetDepartmentCompletedTaskCountAsync(id);
            var completionRate = totalTasks > 0
                ? Math.Round((double)completedTasks / totalTasks * 100, 1)
                : 0;

            return new DepartmentDetailsViewModel
            {
                Id = department.Id,
                Name = department.Name,
                Code = department.Code ?? string.Empty,
                Description = department.Description ?? string.Empty,
                IsActive = department.IsActive,
                CreatedAt = department.CreatedAt,
                BranchCount = branches.Count,
                EmployeeCount = employees.Count,
                OverallCompletionRate = completionRate,
                Branches = branches,
                Employees = employees
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting department details for {Id}", id);
            throw;
        }
    }

    public async Task<Dictionary<int, int>> GetDepartmentBranchCountsAsync()
    {
        return await _context.Branches
            .Where(b => b.IsActive && b.DepartmentId != null)
            .GroupBy(b => b.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.DepartmentId, g => g.Count);
    }

    public async Task<Dictionary<int, int>> GetDepartmentEmployeeCountsAsync()
    {
        return await _context.Employees
            .Where(e => e.IsActive && e.DepartmentId != null)
            .GroupBy(e => e.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.DepartmentId, g => g.Count);
    }

    #region Private Helper Methods

    private async Task<double> GetBranchCompletionRateAsync(int branchId)
    {
        var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(todayLocal.AddDays(-7));
        var utcEnd = _timezoneService.GetEndOfDayLocal(todayLocal);
        
        var totalTask = await _context.DailyTasks
            .Where(dt => dt.BranchId == branchId && dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd)
            .CountAsync();
        
        if (totalTask == 0) return 0;
        
        var completedTask = await _context.DailyTasks
            .Where(dt => dt.BranchId == branchId && dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd && dt.IsCompleted)
            .CountAsync();
        
        return Math.Round((double)completedTask / totalTask * 100, 1);
    }

    private async Task<int> GetDepartmentTaskCountAsync(int departmentId)
    {
        var branchIds = await _context.Branches
            .Where(b => b.DepartmentId == departmentId && b.IsActive)
            .Select(b => b.Id)
            .ToListAsync();

        if (!branchIds.Any()) return 0;

        var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(todayLocal);
        var utcEnd = _timezoneService.GetEndOfDayLocal(todayLocal);
        
        return await _context.DailyTasks
            .CountAsync(dt => branchIds.Contains(dt.BranchId) &&
                             dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd);
    }

    private async Task<int> GetDepartmentCompletedTaskCountAsync(int departmentId)
    {
        var branchIds = await _context.Branches
            .Where(b => b.DepartmentId == departmentId && b.IsActive)
            .Select(b => b.Id)
            .ToListAsync();

        if (!branchIds.Any()) return 0;

        var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
        var utcStart = _timezoneService.GetStartOfDayLocal(todayLocal);
        var utcEnd = _timezoneService.GetEndOfDayLocal(todayLocal);
        
        return await _context.DailyTasks
            .CountAsync(dt => branchIds.Contains(dt.BranchId) &&
                             dt.TaskDate >= utcStart && dt.TaskDate <= utcEnd &&
                             dt.IsCompleted);
    }

    #endregion

    public async Task<List<DepartmentListViewModel>> GetActiveDepartmentsSummaryAsync()
    {
        return await _context.Departments
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentListViewModel
            {
                Id = d.Id,
                Name = d.Name,
                Code = d.Code
            })
            .ToListAsync();
    }
}
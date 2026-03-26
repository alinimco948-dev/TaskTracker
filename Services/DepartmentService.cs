using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class DepartmentService : IDepartmentService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DepartmentService> _logger;
    private readonly IAuditService _auditService;

    public DepartmentService(
        ApplicationDbContext context,
        ILogger<DepartmentService> logger,
        IAuditService auditService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
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

            var branches = department.Branches?
                .Where(b => b.IsActive)
                .Select(b => new DepartmentBranchItem
                {
                    Id = b.Id,
                    Name = b.Name,
                    Address = b.Address ?? string.Empty,
                    CompletionRate = GetBranchCompletionRate(b.Id)
                })
                .ToList() ?? new();

            var employees = department.Employees?
                .Where(e => e.IsActive)
                .Select(e => new DepartmentEmployeeItem
                {
                    Id = e.Id,
                    Name = e.Name,
                    Position = e.Position ?? string.Empty,
                    Initials = GetInitials(e.Name)
                })
                .ToList() ?? new();

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

    private double GetBranchCompletionRate(int branchId)
    {
        var today = DateTime.Today;
        var tasks = _context.DailyTasks
            .Where(dt => dt.BranchId == branchId && dt.TaskDate.Date == today.Date)
            .ToList();

        if (!tasks.Any()) return 0;

        var completed = tasks.Count(t => t.IsCompleted);
        return Math.Round((double)completed / tasks.Count * 100, 1);
    }

    private async Task<int> GetDepartmentTaskCountAsync(int departmentId)
    {
        var branchIds = await _context.Branches
            .Where(b => b.DepartmentId == departmentId)
            .Select(b => b.Id)
            .ToListAsync();

        return await _context.DailyTasks
            .CountAsync(dt => branchIds.Contains(dt.BranchId) &&
                             dt.TaskDate.Date == DateTime.Today.Date);
    }

    private async Task<int> GetDepartmentCompletedTaskCountAsync(int departmentId)
    {
        var branchIds = await _context.Branches
            .Where(b => b.DepartmentId == departmentId)
            .Select(b => b.Id)
            .ToListAsync();

        return await _context.DailyTasks
            .CountAsync(dt => branchIds.Contains(dt.BranchId) &&
                             dt.TaskDate.Date == DateTime.Today.Date &&
                             dt.IsCompleted);
    }

    private string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpper();
        return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
    }
}
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class BranchService : IBranchService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BranchService> _logger;
    private readonly IAuditService _auditService;

    public BranchService(
        ApplicationDbContext context,
        ILogger<BranchService> logger,
        IAuditService auditService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<List<BranchListViewModel>> GetAllBranchesAsync()
    {
        try
        {
            var branches = await _context.Branches
                .Include(b => b.Department)
                .Include(b => b.BranchAssignments)
                .OrderBy(b => b.Name)
                .ToListAsync();

            var result = new List<BranchListViewModel>();

            foreach (var branch in branches)
            {
                var employeeCount = branch.BranchAssignments
                    .Count(ba => ba.EndDate == null);

                var completionRate = await GetBranchCompletionRateAsync(branch.Id, DateTime.Today);

                result.Add(new BranchListViewModel
                {
                    Id = branch.Id,
                    Name = branch.Name,
                    Code = branch.Code ?? string.Empty,
                    Department = branch.Department?.Name ?? "Unassigned",
                    Address = branch.Address ?? string.Empty,
                    EmployeeCount = employeeCount,
                    CompletionRate = completionRate,
                    IsActive = branch.IsActive,
                    HiddenTasks = branch.HiddenTasks // Make sure this line exists
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all branches");
            return new List<BranchListViewModel>();
        }
    }
    public async Task<Branch?> GetBranchByIdAsync(int id)
    {
        return await _context.Branches
            .Include(b => b.Department)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Branch> CreateBranchAsync(BranchViewModel model)
    {
        try
        {
            var branch = new Branch
            {
                Name = model.Name,
                Code = model.Code,
                Address = model.Address,
                Phone = model.Phone,
                Email = model.Email,
                DepartmentId = model.DepartmentId,
                Notes = model.Notes,
                HiddenTasks = await GetTaskNamesAsync(model.HiddenTaskIds),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Branches.Add(branch);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Create",
                "Branch",
                branch.Id,
                $"Created branch: {branch.Name}"
            );

            return branch;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating branch");
            throw;
        }
    }

    public async Task<Branch?> UpdateBranchAsync(BranchViewModel model)
    {
        try
        {
            var branch = await _context.Branches.FindAsync(model.Id);
            if (branch == null) return null;

            var oldValues = $"Name:{branch.Name}, Dept:{branch.DepartmentId}";

            branch.Name = model.Name;
            branch.Code = model.Code;
            branch.Address = model.Address;
            branch.Phone = model.Phone;
            branch.Email = model.Email;
            branch.DepartmentId = model.DepartmentId;
            branch.Notes = model.Notes;
            branch.HiddenTasks = await GetTaskNamesAsync(model.HiddenTaskIds);
            branch.IsActive = model.IsActive;
            branch.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var newValues = $"Name:{branch.Name}, Dept:{branch.DepartmentId}";

            await _auditService.LogAsync(
                "Update",
                "Branch",
                branch.Id,
                $"Updated branch: {branch.Name}",
                null,
                oldValues,
                newValues
            );

            return branch;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating branch {Id}", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteBranchAsync(int id)
    {
        try
        {
            var branch = await _context.Branches.FindAsync(id);
            if (branch == null) return false;

            // Soft delete
            branch.IsActive = false;
            branch.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Delete",
                "Branch",
                branch.Id,
                $"Deactivated branch: {branch.Name}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting branch {Id}", id);
            return false;
        }
    }

    public async Task<BranchDetailsViewModel> GetBranchDetailsAsync(int id)
    {
        try
        {
            var branch = await _context.Branches
                .Include(b => b.Department)
                .Include(b => b.BranchAssignments)
                    .ThenInclude(ba => ba.Employee)
                .Include(b => b.DailyTasks)
                    .ThenInclude(dt => dt.TaskItem)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (branch == null)
                throw new Exception($"Branch {id} not found");

            var employeeCount = branch.BranchAssignments
                .Count(ba => ba.EndDate == null);

            var tasks = branch.DailyTasks
                .Where(dt => dt.TaskDate.Date == DateTime.Today.Date)
                .ToList();

            var totalTasks = tasks.Count;
            var completedTasks = tasks.Count(t => t.IsCompleted);
            var completionRate = totalTasks > 0
                ? Math.Round((double)completedTasks / totalTasks * 100, 1)
                : 0;

            var currentEmployees = branch.BranchAssignments
                .Where(ba => ba.EndDate == null && ba.Employee != null)
                .Select(ba => new BranchEmployeeItem
                {
                    Id = ba.Employee!.Id,
                    Name = ba.Employee.Name,
                    Position = ba.Employee.Position ?? string.Empty,
                    Initials = GetInitials(ba.Employee.Name)
                })
                .ToList();

            var recentTasks = branch.DailyTasks
                .OrderByDescending(dt => dt.TaskDate)
                .Take(10)
                .Select(dt => new BranchTaskItem
                {
                    Date = dt.TaskDate,
                    TaskName = dt.TaskItem?.Name ?? "Unknown",
                    IsCompleted = dt.IsCompleted
                })
                .ToList();

            return new BranchDetailsViewModel
            {
                Id = branch.Id,
                Name = branch.Name,
                Code = branch.Code ?? string.Empty,
                Address = branch.Address ?? string.Empty,
                Phone = branch.Phone ?? string.Empty,
                Email = branch.Email ?? string.Empty,
                Department = branch.Department?.Name ?? "Unassigned",
                Notes = branch.Notes ?? string.Empty,
                IsActive = branch.IsActive,
                CreatedAt = branch.CreatedAt,

                EmployeeCount = employeeCount,
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                CompletionRate = completionRate,

                HiddenTasks = branch.HiddenTasks,
                CurrentEmployees = currentEmployees,
                RecentTasks = recentTasks
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch details for {Id}", id);
            throw;
        }
    }

    public async Task<bool> AssignEmployeeAsync(int branchId, int employeeId, DateTime startDate)
    {
        try
        {
            // End current assignment if exists
            var currentAssignment = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.EmployeeId == employeeId && ba.EndDate == null);

            if (currentAssignment != null)
            {
                currentAssignment.EndDate = startDate.Date.AddDays(-1);
            }

            var assignment = new BranchAssignment
            {
                BranchId = branchId,
                EmployeeId = employeeId,
                StartDate = startDate.Date
            };

            _context.BranchAssignments.Add(assignment);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Assign",
                "BranchAssignment",
                assignment.Id,
                $"Employee {employeeId} assigned to branch {branchId}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning employee to branch");
            return false;
        }
    }

    public async Task<bool> EndAssignmentAsync(int assignmentId)
    {
        try
        {
            var assignment = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .FirstOrDefaultAsync(ba => ba.Id == assignmentId);

            if (assignment == null) return false;

            assignment.EndDate = DateTime.Today;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "End",
                "BranchAssignment",
                assignmentId,
                $"Assignment ended for {assignment.Employee?.Name}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending assignment");
            return false;
        }
    }

    public async Task<List<BranchAssignment>> GetAssignmentHistoryAsync(int branchId)
    {
        return await _context.BranchAssignments
            .Include(ba => ba.Employee)
            .Where(ba => ba.BranchId == branchId)
            .OrderByDescending(ba => ba.StartDate)
            .ToListAsync();
    }
    public async Task<bool> UpdateTaskVisibilityAsync(int branchId, List<string> visibleTasks)
    {
        try
        {
            var branch = await _context.Branches.FindAsync(branchId);
            if (branch == null) return false;

            var allTasks = await _context.TaskItems
                .Where(t => t.IsActive)
                .Select(t => t.Name)
                .ToListAsync();

            var hiddenTasks = allTasks.Except(visibleTasks).ToList();
            branch.HiddenTasks = hiddenTasks;
            branch.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Update",
                "Branch",
                branchId,
                $"Updated task visibility for branch: {branch.Name}"
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating task visibility for branch {BranchId}", branchId);
            return false;
        }
    }

    // Add this overload if you need single task updates
    public async Task<bool> UpdateSingleTaskVisibilityAsync(int branchId, string taskName, bool isVisible)
    {
        try
        {
            var branch = await _context.Branches.FindAsync(branchId);
            if (branch == null) return false;

            var hiddenTasks = branch.HiddenTasks;

            if (isVisible)
            {
                hiddenTasks.Remove(taskName);
            }
            else if (!hiddenTasks.Contains(taskName))
            {
                hiddenTasks.Add(taskName);
            }

            branch.HiddenTasks = hiddenTasks;
            branch.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating single task visibility");
            return false;
        }
    }




    public async Task<Dictionary<int, int>> GetBranchEmployeeCountsAsync()
    {
        var assignments = await _context.BranchAssignments
            .Where(ba => ba.EndDate == null)
            .GroupBy(ba => ba.BranchId)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.BranchId, g => g.Count);

        return assignments;
    }

    public async Task<Dictionary<int, double>> GetBranchCompletionRatesAsync(DateTime date)
    {
        var rates = new Dictionary<int, double>();
        var branches = await _context.Branches
            .Where(b => b.IsActive)
            .ToListAsync();

        foreach (var branch in branches)
        {
            rates[branch.Id] = await GetBranchCompletionRateAsync(branch.Id, date);
        }

        return rates;
    }

    private async Task<double> GetBranchCompletionRateAsync(int branchId, DateTime date)
    {
        var tasks = await _context.DailyTasks
            .Where(dt => dt.BranchId == branchId && dt.TaskDate.Date == date.Date)
            .ToListAsync();

        if (!tasks.Any()) return 0;

        var completed = tasks.Count(t => t.IsCompleted);
        return Math.Round((double)completed / tasks.Count * 100, 1);
    }

    private async Task<List<string>> GetTaskNamesAsync(List<int> taskIds)
    {
        if (taskIds == null || !taskIds.Any()) return new List<string>();

        return await _context.TaskItems
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => t.Name)
            .ToListAsync();
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
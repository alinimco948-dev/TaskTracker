using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Services;

public class EmployeeService : IEmployeeService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<EmployeeService> _logger;
    private readonly IAuditService _auditService;
    private readonly ITaskCalculationService _taskCalculationService;
    private readonly ITimezoneService _timezoneService;

    public EmployeeService(
        ApplicationDbContext context,
        ILogger<EmployeeService> logger,
        IAuditService auditService,
        ITaskCalculationService taskCalculationService,
        ITimezoneService timezoneService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
        _taskCalculationService = taskCalculationService;
        _timezoneService = timezoneService;
    }

    public async Task<List<EmployeeListViewModel>> GetAllEmployeesAsync()
    {
        try
        {
            var employees = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.BranchAssignments).ThenInclude(ba => ba.Branch)
                .AsNoTracking()
                .OrderBy(e => e.Name)
                .ToListAsync();

            var allTasks = await _context.TaskItems.Where(t => t.IsActive).ToListAsync();
            var result = new List<EmployeeListViewModel>();
            var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
            var todayUtc = _timezoneService.GetStartOfDayLocal(todayLocal);

            foreach (var emp in employees)
            {
                var currentBranches = emp.BranchAssignments
                    .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal)
                    .Select(ba => ba.Branch?.Name ?? string.Empty)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                var activeAssignments = emp.BranchAssignments
                    .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal)
                    .ToList();
                var assignedBranchIds = activeAssignments.Select(ba => ba.BranchId).ToList();

                if (!assignedBranchIds.Any())
                {
                    result.Add(new EmployeeListViewModel
                    {
                        Id = emp.Id,
                        Name = emp.Name,
                        EmployeeId = emp.EmployeeId,
                        Email = emp.Email ?? string.Empty,
                        Position = emp.Position ?? string.Empty,
                        Department = emp.Department?.Name ?? "Unassigned",
                        CurrentBranch = "Not Assigned",
                        Branches = new List<string>(),
                        PerformanceScore = 0,
                        TotalTasks = 0,
                        CompletedTasks = 0,
                        IsActive = emp.IsActive,
                        Initials = GetInitials(emp.Name)
                    });
                    continue;
                }

                var completedDailyTasks = await _context.DailyTasks
                    .Where(dt => assignedBranchIds.Contains(dt.BranchId) && dt.IsCompleted)
                    .ToListAsync();
                    
                var expectedTotalTasks = 0;
                var lookAheadDays = 90;
                var startDateUtc = todayUtc;
                var endDateUtc = todayUtc.AddDays(lookAheadDays);

                foreach (var assignment in activeAssignments)
                {
                    var branch = await _context.Branches.FindAsync(assignment.BranchId);
                    if (branch == null) continue;

                    var hiddenTaskNames = branch.HiddenTasks ?? new List<string>();
                    var assignmentStartUtc = assignment.StartDate.Date >= startDateUtc ? assignment.StartDate : startDateUtc;
                    var assignmentEndUtc = assignment.EndDate?.Date ?? endDateUtc;

                    foreach (var task in allTasks)
                    {
                        if (hiddenTaskNames.Contains(task.Name)) continue;
                        expectedTotalTasks += _taskCalculationService.GetTaskCountInRange(task, assignmentStartUtc, assignmentEndUtc);
                    }
                }

                var completedTasks = completedDailyTasks.Count;
                var totalTasks = Math.Max(expectedTotalTasks, completedTasks);
                var completionRate = totalTasks > 0 ? (int)Math.Round((double)completedTasks / totalTasks * 100) : 0;

                result.Add(new EmployeeListViewModel
                {
                    Id = emp.Id,
                    Name = emp.Name,
                    EmployeeId = emp.EmployeeId,
                    Email = emp.Email ?? string.Empty,
                    Position = emp.Position ?? string.Empty,
                    Department = emp.Department?.Name ?? "Unassigned",
                    CurrentBranch = currentBranches.Any() ? string.Join(", ", currentBranches) : "Not Assigned",
                    Branches = currentBranches,
                    PerformanceScore = completionRate,
                    TotalTasks = totalTasks,
                    CompletedTasks = completedTasks,
                    IsActive = emp.IsActive,
                    Initials = GetInitials(emp.Name)
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all employees");
            return new List<EmployeeListViewModel>();
        }
    }

    public async Task<Employee?> GetEmployeeByIdAsync(int id)
        => await _context.Employees
            .Include(e => e.Department)
            .Include(e => e.Manager)
            .Include(e => e.BranchAssignments).ThenInclude(ba => ba.Branch)
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task<Employee?> GetEmployeeByEmployeeIdAsync(string employeeId)
        => await _context.Employees
            .Include(e => e.BranchAssignments)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

    public async Task<Employee> CreateEmployeeAsync(EmployeeViewModel model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.Name))
                throw new ArgumentException("Employee name is required");
            if (string.IsNullOrWhiteSpace(model.EmployeeId))
                throw new ArgumentException("Employee ID is required");

            var employee = new Employee
            {
                Name = model.Name.Trim(),
                EmployeeId = model.EmployeeId.Trim(),
                Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
                Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim(),
                HireDate = model.HireDate.HasValue ? _timezoneService.GetStartOfDayLocal(model.HireDate.Value) : null,
                Position = string.IsNullOrWhiteSpace(model.Position) ? null : model.Position.Trim(),
                DepartmentId = model.DepartmentId,
                ManagerId = model.ManagerId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Create",
                "Employee",
                employee.Id,
                $"Created employee: {employee.Name} (ID: {employee.EmployeeId})"
            );

            return employee;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error creating employee");
            if (ex.InnerException?.Message?.Contains("IX_Employees_Email") == true)
                throw new Exception($"Email '{model.Email}' is already in use.");
            if (ex.InnerException?.Message?.Contains("IX_Employees_EmployeeId") == true)
                throw new Exception($"Employee ID '{model.EmployeeId}' is already in use.");
            throw new Exception($"Database error: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    public async Task<Employee?> UpdateEmployeeAsync(EmployeeViewModel model)
    {
        try
        {
            var employee = await _context.Employees
                .Include(e => e.BranchAssignments)
                .FirstOrDefaultAsync(e => e.Id == model.Id);
                
            if (employee == null) return null;

            var oldValues = $"Name:{employee.Name}, Position:{employee.Position}, Dept:{employee.DepartmentId}";

            employee.Name = model.Name;
            employee.EmployeeId = model.EmployeeId;
            employee.Email = model.Email;
            employee.Phone = model.Phone;
            employee.Address = model.Address;
            employee.HireDate = model.HireDate.HasValue ? _timezoneService.GetStartOfDayLocal(model.HireDate.Value) : null;
            employee.Position = model.Position;
            employee.DepartmentId = model.DepartmentId;
            employee.ManagerId = model.ManagerId;
            employee.IsActive = model.IsActive;
            employee.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Update",
                "Employee",
                employee.Id,
                $"Updated employee: {employee.Name}",
                null,
                oldValues,
                $"Name:{employee.Name}, Position:{employee.Position}, Dept:{employee.DepartmentId}"
            );

            return employee;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating employee {Id}", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteEmployeeAsync(int id)
    {
        try
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return false;

            var hasSubordinates = await _context.Employees.AnyAsync(e => e.ManagerId == id);

            if (hasSubordinates)
            {
                employee.IsActive = false;
                employee.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _auditService.LogAsync(
                    "Deactivate",
                    "Employee",
                    employee.Id,
                    $"Deactivated employee (has subordinates): {employee.Name}"
                );
            }
            else
            {
                var assignments = await _context.BranchAssignments
                    .Where(ba => ba.EmployeeId == id)
                    .ToListAsync();
                _context.BranchAssignments.RemoveRange(assignments);
                _context.Employees.Remove(employee);
                await _context.SaveChangesAsync();
                await _auditService.LogAsync(
                    "Delete",
                    "Employee",
                    employee.Id,
                    $"Deleted employee: {employee.Name}"
                );
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting employee {Id}", id);
            return false;
        }
    }

    public async Task<EmployeeDetailsViewModel> GetEmployeeDetailsAsync(int id, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var actualStartDateLocal = startDate ?? _timezoneService.GetCurrentLocalTime().AddDays(-30);
            var actualEndDateLocal = endDate ?? _timezoneService.GetCurrentLocalTime();
            
            var utcStartDate = _timezoneService.GetStartOfDayLocal(actualStartDateLocal);
            var utcEndDate = _timezoneService.GetEndOfDayLocal(actualEndDateLocal);

            var employee = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Manager)
                .Include(e => e.BranchAssignments).ThenInclude(ba => ba.Branch)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee == null)
                throw new Exception($"Employee {id} not found");

            var stats = await _taskCalculationService.GetEmployeeTaskStatisticsAsync(id, utcStartDate, utcEndDate);
            var todayLocal = _timezoneService.GetCurrentLocalTime().Date;

            var currentBranches = employee.BranchAssignments
                .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal)
                .Select(ba => ba.Branch?.Name ?? string.Empty)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            var branchHistory = employee.BranchAssignments
                .OrderByDescending(ba => ba.StartDate)
                .Select(ba => new BranchHistoryItem
                {
                    Id = ba.Id,
                    BranchId = ba.BranchId,
                    BranchName = ba.Branch?.Name ?? "Unknown",
                    StartDate = ba.StartDate,
                    EndDate = ba.EndDate,
                    Duration = ba.EndDate.HasValue ? (ba.EndDate.Value - ba.StartDate).Days : (DateTime.UtcNow - ba.StartDate).Days,
                    IsActive = !ba.EndDate.HasValue || ba.EndDate.Value.Date >= todayLocal
                }).ToList();

            var assignedBranchIds = employee.BranchAssignments
                .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal)
                .Select(ba => ba.BranchId).ToList();

            var recentTasks = new List<TaskHistoryItem>();
            if (assignedBranchIds.Any())
            {
                var dailyTasks = await _context.DailyTasks
                    .Include(dt => dt.TaskItem).Include(dt => dt.Branch)
                    .Where(dt => assignedBranchIds.Contains(dt.BranchId) && 
                                 dt.TaskDate >= utcStartDate && 
                                 dt.TaskDate <= utcEndDate)
                    .OrderByDescending(dt => dt.TaskDate).Take(10).ToListAsync();

                recentTasks = dailyTasks.Select(dt => new TaskHistoryItem
                {
                    Date = dt.TaskDate,
                    BranchName = dt.Branch?.Name ?? "Unknown",
                    TaskName = dt.TaskItem?.Name ?? "Unknown",
                    IsCompleted = dt.IsCompleted,
                    IsOnTime = _taskCalculationService.IsTaskOnTime(dt)
                }).ToList();
            }

            // Chart data using local dates for display
            var chartLabels = new List<string>();
            var chartData = new List<int>();
            var daysInRange = (actualEndDateLocal - actualStartDateLocal).Days;

            for (int i = daysInRange; i >= 0; i--)
            {
                var date = actualEndDateLocal.AddDays(-i).Date;
                chartLabels.Add(date.ToString("MMM dd"));
                
                var dayUtcStart = _timezoneService.GetStartOfDayLocal(date);
                var dayUtcEnd = _timezoneService.GetEndOfDayLocal(date);

                var tasksOnDay = 0;
                if (assignedBranchIds.Any())
                    tasksOnDay = await _context.DailyTasks.CountAsync(dt => assignedBranchIds.Contains(dt.BranchId) && 
                                                                          dt.TaskDate >= dayUtcStart && 
                                                                          dt.TaskDate <= dayUtcEnd && 
                                                                          dt.IsCompleted);
                chartData.Add(tasksOnDay);
            }

            return new EmployeeDetailsViewModel
            {
                Id = employee.Id,
                Name = employee.Name,
                EmployeeId = employee.EmployeeId,
                Email = employee.Email ?? string.Empty,
                Phone = employee.Phone ?? string.Empty,
                Address = employee.Address ?? string.Empty,
                HireDate = employee.HireDate,
                Position = employee.Position ?? string.Empty,
                Department = employee.Department?.Name ?? "Unassigned",
                Manager = employee.Manager?.Name ?? "None",
                IsActive = employee.IsActive,
                CreatedAt = employee.CreatedAt,
                StartDate = actualStartDateLocal,
                EndDate = actualEndDateLocal,
                TotalTasks = stats.TotalTasks,
                CompletedTasks = stats.CompletedTasks,
                PendingTasks = stats.PendingTasks,
                OnTimeTasks = stats.OnTimeTasks,
                PerformanceScore = (int)Math.Round(stats.WeightedScore),
                CurrentBranches = currentBranches,
                BranchHistory = branchHistory,
                RecentTasks = recentTasks,
                ChartLabels = chartLabels,
                ChartData = chartData
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employee details for {Id}", id);
            throw;
        }
    }

    public async Task<int> CalculateEmployeeScoreAsync(int employeeId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var actualStartDate = startDate.HasValue 
            ? _timezoneService.GetStartOfDayLocal(startDate.Value) 
            : _timezoneService.GetStartOfDayLocal(_timezoneService.GetCurrentLocalTime().AddMonths(-1));
        var actualEndDate = endDate.HasValue 
            ? _timezoneService.GetEndOfDayLocal(endDate.Value) 
            : _timezoneService.GetEndOfDayLocal(_timezoneService.GetCurrentLocalTime());
        
        var stats = await _taskCalculationService.GetEmployeeTaskStatisticsAsync(employeeId, actualStartDate, actualEndDate);
        return (int)Math.Round(stats.WeightedScore);
    }

    public async Task<string?> GetCurrentBranchAsync(int employeeId)
        => (await GetCurrentBranchesAsync(employeeId)).FirstOrDefault();

    public async Task<List<string>> GetCurrentBranchesAsync(int employeeId)
    {
        var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
        return await _context.BranchAssignments
            .Include(ba => ba.Branch)
            .Where(ba => ba.EmployeeId == employeeId && 
                        (ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal))
            .Select(ba => ba.Branch != null ? ba.Branch.Name : string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToListAsync();
    }

    public async Task<List<BranchHistoryItem>> GetBranchHistoryAsync(int employeeId)
    {
        var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
        return await _context.BranchAssignments
            .Include(ba => ba.Branch)
            .Where(ba => ba.EmployeeId == employeeId)
            .OrderByDescending(ba => ba.StartDate)
            .Select(ba => new BranchHistoryItem
            {
                Id = ba.Id,
                BranchId = ba.BranchId,
                BranchName = ba.Branch != null ? ba.Branch.Name : "Unknown",
                StartDate = ba.StartDate,
                EndDate = ba.EndDate,
                Duration = ba.EndDate.HasValue ? (ba.EndDate.Value - ba.StartDate).Days : (DateTime.UtcNow - ba.StartDate).Days,
                IsActive = !ba.EndDate.HasValue || ba.EndDate.Value.Date >= todayLocal
            }).ToListAsync();
    }

    public async Task<List<TaskHistoryItem>> GetRecentTasksAsync(int employeeId, int count = 10)
    {
        var employee = await _context.Employees
            .Include(e => e.BranchAssignments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
            
        if (employee == null) return new List<TaskHistoryItem>();

        var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
        var assignedBranchIds = employee.BranchAssignments
            .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal)
            .Select(ba => ba.BranchId).ToList();

        if (!assignedBranchIds.Any()) return new List<TaskHistoryItem>();

        var dailyTasks = await _context.DailyTasks
            .Include(dt => dt.TaskItem).Include(dt => dt.Branch)
            .Where(dt => assignedBranchIds.Contains(dt.BranchId))
            .OrderByDescending(dt => dt.TaskDate)
            .Take(count)
            .ToListAsync();

        return dailyTasks.Select(dt => new TaskHistoryItem
        {
            Date = dt.TaskDate,
            BranchName = dt.Branch?.Name ?? "Unknown",
            TaskName = dt.TaskItem?.Name ?? "Unknown",
            IsCompleted = dt.IsCompleted,
            IsOnTime = _taskCalculationService.IsTaskOnTime(dt)
        }).ToList();
    }

    public async Task<Dictionary<int, int>> GetEmployeeScoresAsync()
    {
        var scores = new Dictionary<int, int>();
        var employees = await _context.Employees.Where(e => e.IsActive).ToListAsync();
        var endDateUtc = _timezoneService.GetEndOfDayLocal(_timezoneService.GetCurrentLocalTime());
        var startDateUtc = _timezoneService.GetStartOfDayLocal(_timezoneService.GetCurrentLocalTime().AddDays(-30));
        
        foreach (var emp in employees)
        {
            var stats = await _taskCalculationService.GetEmployeeTaskStatisticsAsync(emp.Id, startDateUtc, endDateUtc);
            scores[emp.Id] = (int)Math.Round(stats.WeightedScore);
        }
        return scores;
    }

    #region Multi-Branch Management

    public async Task<bool> AssignToBranchAsync(int employeeId, int branchId, DateTime startDate)
    {
        try
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            var branch = await _context.Branches.FindAsync(branchId);
            
            if (employee == null || branch == null) return false;

            var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
            var utcStartDate = _timezoneService.GetStartOfDayLocal(startDate);

            var existing = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.EmployeeId == employeeId && 
                                           ba.BranchId == branchId && 
                                           (ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal));

            if (existing != null) return false;

            var assignment = new BranchAssignment
            {
                EmployeeId = employeeId,
                BranchId = branchId,
                StartDate = utcStartDate
            };

            _context.BranchAssignments.Add(assignment);
            await _context.SaveChangesAsync();
            
            await _auditService.LogAsync(
                "Assign",
                "BranchAssignment",
                assignment.Id,
                $"Employee {employee.Name} assigned to branch {branch.Name}"
            );
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning employee to branch");
            return false;
        }
    }

    public async Task<bool> RemoveFromBranchAsync(int employeeId, int branchId)
    {
        try
        {
            var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
            var assignment = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.EmployeeId == employeeId && 
                                           ba.BranchId == branchId && 
                                           (ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal));
                                           
            if (assignment == null) return false;

            assignment.EndDate = _timezoneService.GetStartOfDayLocal(todayLocal);
            await _context.SaveChangesAsync();
            
            await _auditService.LogAsync(
                "Remove",
                "BranchAssignment",
                assignment.Id,
                $"Employee {employeeId} removed from branch {branchId}"
            );
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing employee from branch");
            return false;
        }
    }

    public async Task<bool> EndBranchAssignmentAsync(int assignmentId)
    {
        try
        {
            var assignment = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .Include(ba => ba.Branch)
                .FirstOrDefaultAsync(ba => ba.Id == assignmentId);
                
            if (assignment == null) return false;

            var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
            assignment.EndDate = _timezoneService.GetStartOfDayLocal(todayLocal);
            await _context.SaveChangesAsync();
            
            await _auditService.LogAsync(
                "End",
                "BranchAssignment",
                assignmentId,
                $"Ended assignment for {assignment.Employee?.Name} at {assignment.Branch?.Name}"
            );
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending branch assignment");
            return false;
        }
    }

    public async Task<List<Branch>> GetEmployeeBranchesAsync(int employeeId)
    {
        var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
        return await _context.BranchAssignments
            .Where(ba => ba.EmployeeId == employeeId && 
                        (ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal))
            .Select(ba => ba.Branch!)
            .Where(b => b != null && b.IsActive)
            .ToListAsync();
    }

    public async Task<List<Employee>> GetBranchEmployeesAsync(int branchId)
    {
        var todayLocal = _timezoneService.GetCurrentLocalTime().Date;
        return await _context.BranchAssignments
            .Where(ba => ba.BranchId == branchId && 
                        (ba.EndDate == null || ba.EndDate.Value.Date >= todayLocal))
            .Select(ba => ba.Employee!)
            .Where(e => e != null && e.IsActive)
            .ToListAsync();
    }

    #endregion

    #region Helper Methods

    private string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpper();
        return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
    }

    #endregion
}
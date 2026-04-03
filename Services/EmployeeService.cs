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

    public EmployeeService(
        ApplicationDbContext context,
        ILogger<EmployeeService> logger,
        IAuditService auditService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<List<EmployeeListViewModel>> GetAllEmployeesAsync()
    {
        try
        {
            var employees = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.BranchAssignments)
                    .ThenInclude(ba => ba.Branch)
                .AsNoTracking()
                .OrderBy(e => e.Name)
                .ToListAsync();

            // Get all active tasks
            var allTasks = await _context.TaskItems
                .Where(t => t.IsActive)
                .ToListAsync();

            var result = new List<EmployeeListViewModel>();

            foreach (var emp in employees)
            {
                // Get ALL current branches (for display only)
                var currentBranches = emp.BranchAssignments
                    .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
                    .Select(ba => ba.Branch != null ? ba.Branch.Name : string.Empty)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                // Get active branch assignments
                var activeAssignments = emp.BranchAssignments
                    .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
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

                // Get completed tasks from DailyTasks
                var completedDailyTasks = await _context.DailyTasks
                    .Where(dt => assignedBranchIds.Contains(dt.BranchId) && dt.IsCompleted)
                    .ToListAsync();

                // Calculate expected total tasks based on schedules
                var expectedTotalTasks = 0;
                var today = DateTime.UtcNow.Date;
                var lookAheadDays = 90;

                foreach (var assignment in activeAssignments)
                {
                    var branch = await _context.Branches.FindAsync(assignment.BranchId);
                    if (branch == null) continue;

                    var hiddenTaskNames = branch.HiddenTasks ?? new List<string>();
                    
                    var startDate = assignment.StartDate.Date;
                    var endDate = assignment.EndDate?.Date ?? today.AddDays(lookAheadDays);

                    foreach (var task in allTasks)
                    {
                        if (hiddenTaskNames.Contains(task.Name))
                            continue;

                        var taskCount = GetTaskCountInRange(task, startDate, endDate);
                        expectedTotalTasks += taskCount;
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
    {
        return await _context.Employees
            .Include(e => e.Department)
            .Include(e => e.Manager)
            .Include(e => e.BranchAssignments)
                .ThenInclude(ba => ba.Branch)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<Employee?> GetEmployeeByEmployeeIdAsync(string employeeId)
    {
        return await _context.Employees
            .Include(e => e.BranchAssignments)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
    }

    public async Task<Employee> CreateEmployeeAsync(EmployeeViewModel model)
    {
        try
        {
            _logger.LogInformation("CreateEmployeeAsync started");

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
                HireDate = model.HireDate.HasValue ? DateTime.SpecifyKind(model.HireDate.Value, DateTimeKind.Utc) : null,
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
            {
                throw new Exception($"Email '{model.Email}' is already in use. Please use a different email address.");
            }
            if (ex.InnerException?.Message?.Contains("IX_Employees_EmployeeId") == true)
            {
                throw new Exception($"Employee ID '{model.EmployeeId}' is already in use. Please use a unique ID.");
            }
            throw new Exception($"Database error: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating employee");
            throw;
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
            employee.HireDate = model.HireDate.HasValue ? DateTime.SpecifyKind(model.HireDate.Value, DateTimeKind.Utc) : null;
            employee.Position = model.Position;
            employee.DepartmentId = model.DepartmentId;
            employee.ManagerId = model.ManagerId;
            employee.IsActive = model.IsActive;
            employee.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var newValues = $"Name:{employee.Name}, Position:{employee.Position}, Dept:{employee.DepartmentId}";

            await _auditService.LogAsync(
                "Update",
                "Employee",
                employee.Id,
                $"Updated employee: {employee.Name}",
                null,
                oldValues,
                newValues
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
            // Set default date range (last 30 days if not specified)
            var actualStartDate = startDate ?? DateTime.UtcNow.AddDays(-30);
            var actualEndDate = endDate ?? DateTime.UtcNow;

            var employee = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Manager)
                .Include(e => e.BranchAssignments)
                    .ThenInclude(ba => ba.Branch)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee == null)
                throw new Exception($"Employee {id} not found");

            // Get active branch assignments
            var activeAssignments = employee.BranchAssignments
                .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
                .ToList();

            var assignedBranchIds = activeAssignments.Select(ba => ba.BranchId).ToList();

            // Check if employee was active during the selected date range
            var earliestAssignmentStart = activeAssignments.Any() ? activeAssignments.Min(a => a.StartDate.Date) : (DateTime?)null;
            var latestAssignmentEnd = activeAssignments.Any() && activeAssignments.Any(a => a.EndDate.HasValue) 
                ? activeAssignments.Where(a => a.EndDate.HasValue).Max(a => a.EndDate!.Value.Date) 
                : (DateTime?)null;

            // Determine if the employee has any activity in the selected range
            var hasActivityInRange = false;
            var warningMessage = string.Empty;

            // Check if selected range is before employee's earliest assignment
            if (earliestAssignmentStart.HasValue && actualEndDate.Date < earliestAssignmentStart.Value)
            {
                warningMessage = $"The selected date range ({actualStartDate:MMM dd, yyyy} - {actualEndDate:MMM dd, yyyy}) is before the employee's first branch assignment ({earliestAssignmentStart.Value:MMM dd, yyyy}). No tasks were assigned during this period.";
            }
            // Check if selected range is after employee's last assignment
            else if (latestAssignmentEnd.HasValue && actualStartDate.Date > latestAssignmentEnd.Value)
            {
                warningMessage = $"The selected date range ({actualStartDate:MMM dd, yyyy} - {actualEndDate:MMM dd, yyyy}) is after the employee's last branch assignment ({latestAssignmentEnd.Value:MMM dd, yyyy}). No tasks were assigned during this period.";
            }
            // Check if selected range is before hire date
            else if (employee.HireDate.HasValue && actualEndDate.Date < employee.HireDate.Value.Date)
            {
                warningMessage = $"The selected date range ({actualStartDate:MMM dd, yyyy} - {actualEndDate:MMM dd, yyyy}) is before the employee's hire date ({employee.HireDate.Value:MMM dd, yyyy}). No tasks were assigned during this period.";
            }
            else
            {
                hasActivityInRange = true;
            }

            // Get all active tasks
            var allTasks = await _context.TaskItems
                .Where(t => t.IsActive)
                .ToListAsync();

            // Get completed tasks within date range
            var completedDailyTasks = await _context.DailyTasks
                .Include(dt => dt.TaskItem)
                .Include(dt => dt.Branch)
                .Where(dt => assignedBranchIds.Contains(dt.BranchId) && 
                             dt.IsCompleted &&
                             dt.TaskDate.Date >= actualStartDate.Date &&
                             dt.TaskDate.Date <= actualEndDate.Date)
                .ToListAsync();

            // Create lookup for completed tasks
            var completedKeySet = new HashSet<string>();
            foreach (var dt in completedDailyTasks)
            {
                var key = $"{dt.BranchId}_{dt.TaskItemId}_{dt.TaskDate.Date:yyyy-MM-dd}";
                completedKeySet.Add(key);
            }

            // Calculate all expected tasks within the date range and assignment periods
            var expectedTasks = new List<(DateTime Date, string BranchName, string TaskName, bool IsCompleted, DateTime? CompletedAt, int? AdjustmentMinutes)>();

            if (hasActivityInRange)
            {
                foreach (var assignment in activeAssignments)
                {
                    var branch = await _context.Branches.FindAsync(assignment.BranchId);
                    if (branch == null) continue;

                    var hiddenTaskNames = branch.HiddenTasks ?? new List<string>();
                    
                    // The date range for this assignment is the intersection of:
                    // 1. The user's selected date range
                    // 2. The employee's assignment period for this branch
                    var rangeStart = actualStartDate.Date > assignment.StartDate.Date ? actualStartDate.Date : assignment.StartDate.Date;
                    var rangeEnd = actualEndDate.Date;
                    if (assignment.EndDate.HasValue && assignment.EndDate.Value.Date < rangeEnd)
                    {
                        rangeEnd = assignment.EndDate.Value.Date;
                    }

                    // Also ensure range is not before hire date
                    if (employee.HireDate.HasValue && rangeStart < employee.HireDate.Value.Date)
                    {
                        rangeStart = employee.HireDate.Value.Date;
                    }

                    if (rangeStart > rangeEnd) continue;

                    foreach (var task in allTasks)
                    {
                        if (hiddenTaskNames.Contains(task.Name))
                            continue;

                        var taskDates = GetTaskDatesInRange(task, rangeStart, rangeEnd);
                        
                        foreach (var taskDate in taskDates)
                        {
                            var key = $"{assignment.BranchId}_{task.Id}_{taskDate.Date:yyyy-MM-dd}";
                            var isCompleted = completedKeySet.Contains(key);
                            
                            var completedTask = completedDailyTasks.FirstOrDefault(dt => 
                                dt.BranchId == assignment.BranchId && 
                                dt.TaskItemId == task.Id && 
                                dt.TaskDate.Date == taskDate.Date);
                            
                            expectedTasks.Add((
                                taskDate, 
                                branch.Name, 
                                task.Name, 
                                isCompleted,
                                completedTask?.CompletedAt,
                                completedTask?.AdjustmentMinutes
                            ));
                        }
                    }
                }
            }

            // Order by date
            expectedTasks = expectedTasks.OrderBy(t => t.Date).ToList();
            
            var totalTasks = expectedTasks.Count;
            var completedTasks = expectedTasks.Count(t => t.IsCompleted);
            var onTimeTasks = expectedTasks.Count(t => t.IsCompleted);
            var pendingTasks = totalTasks - completedTasks;
            var completionRate = totalTasks > 0 ? (int)Math.Round((double)completedTasks / totalTasks * 100) : 0;

            // Get ALL current branches
            var currentBranches = employee.BranchAssignments
                .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
                .Select(ba => ba.Branch != null ? ba.Branch.Name : string.Empty)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            // Get branch history
            var branchHistory = employee.BranchAssignments
                .OrderByDescending(ba => ba.StartDate)
                .Select(ba => new BranchHistoryItem
                {
                    Id = ba.Id,
                    BranchId = ba.BranchId,
                    BranchName = ba.Branch != null ? ba.Branch.Name : "Unknown",
                    StartDate = ba.StartDate,
                    EndDate = ba.EndDate,
                    Duration = ba.EndDate.HasValue
                        ? (ba.EndDate.Value - ba.StartDate).Days
                        : (DateTime.UtcNow - ba.StartDate).Days,
                    IsActive = !ba.EndDate.HasValue || ba.EndDate.Value.Date >= DateTime.UtcNow.Date
                })
                .ToList();

            // Get recent task items (last 10 within date range)
            var recentTaskItems = expectedTasks
                .OrderByDescending(t => t.Date)
                .Take(10)
                .Select(t => new TaskHistoryItem
                {
                    Date = t.Date,
                    BranchName = t.BranchName,
                    TaskName = t.TaskName,
                    IsCompleted = t.IsCompleted,
                    IsOnTime = t.IsCompleted
                })
                .ToList();

            // Build chart data for the date range
            var chartLabels = new List<string>();
            var chartData = new List<int>();
            
            var daysInRange = (actualEndDate - actualStartDate).Days;
            for (int i = daysInRange; i >= 0; i--)
            {
                var date = actualEndDate.AddDays(-i).Date;
                chartLabels.Add(date.ToString("MMM dd"));
                var tasksOnDay = expectedTasks.Count(t => t.Date.Date == date && t.IsCompleted);
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

                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                PendingTasks = pendingTasks,
                OnTimeTasks = onTimeTasks,
                PerformanceScore = completionRate,

                CurrentBranches = currentBranches,
                BranchHistory = branchHistory,
                RecentTasks = recentTaskItems,
                ChartLabels = chartLabels,
                ChartData = chartData,
                StartDate = actualStartDate,
                EndDate = actualEndDate,
                WarningMessage = warningMessage
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
        try
        {
            startDate ??= DateTime.UtcNow.AddMonths(-1);
            endDate ??= DateTime.UtcNow;

            var employee = await _context.Employees
                .Include(e => e.BranchAssignments)
                .FirstOrDefaultAsync(e => e.Id == employeeId);

            if (employee == null) return 0;

            var activeAssignments = employee.BranchAssignments
                .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
                .ToList();

            var assignedBranchIds = activeAssignments.Select(ba => ba.BranchId).ToList();

            if (!assignedBranchIds.Any()) return 0;

            var allTasks = await _context.TaskItems
                .Where(t => t.IsActive)
                .ToListAsync();

            var completedDailyTasks = await _context.DailyTasks
                .Where(dt => assignedBranchIds.Contains(dt.BranchId) && 
                            dt.IsCompleted &&
                            dt.TaskDate.Date >= startDate.Value.Date &&
                            dt.TaskDate.Date <= endDate.Value.Date)
                .ToListAsync();

            var expectedTotalTasks = 0;

            foreach (var assignment in activeAssignments)
            {
                var branch = await _context.Branches.FindAsync(assignment.BranchId);
                if (branch == null) continue;

                var hiddenTaskNames = branch.HiddenTasks ?? new List<string>();
                
                var assignStartDate = assignment.StartDate.Date;
                var assignEndDate = assignment.EndDate?.Date ?? endDate.Value.Date;

                foreach (var task in allTasks)
                {
                    if (hiddenTaskNames.Contains(task.Name))
                        continue;

                    var taskDates = GetTaskDatesInRange(task, assignStartDate, assignEndDate);
                    taskDates = taskDates.Where(d => d >= startDate.Value.Date && d <= endDate.Value.Date).ToList();
                    expectedTotalTasks += taskDates.Count;
                }
            }

            var completedTasks = completedDailyTasks.Count;
            
            return expectedTotalTasks > 0 ? (int)Math.Round((double)completedTasks / expectedTotalTasks * 100) : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating score for employee {EmployeeId}", employeeId);
            return 0;
        }
    }

    public async Task<string?> GetCurrentBranchAsync(int employeeId)
    {
        var branches = await GetCurrentBranchesAsync(employeeId);
        return branches.FirstOrDefault();
    }

    public async Task<List<string>> GetCurrentBranchesAsync(int employeeId)
    {
        var branches = await _context.BranchAssignments
            .Include(ba => ba.Branch)
            .Where(ba => ba.EmployeeId == employeeId &&
                        (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
            .Select(ba => ba.Branch != null ? ba.Branch.Name : string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToListAsync();

        return branches;
    }

    public async Task<List<BranchHistoryItem>> GetBranchHistoryAsync(int employeeId)
    {
        var history = await _context.BranchAssignments
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
                Duration = ba.EndDate.HasValue
                    ? (ba.EndDate.Value - ba.StartDate).Days
                    : (DateTime.UtcNow - ba.StartDate).Days,
                IsActive = !ba.EndDate.HasValue || ba.EndDate.Value.Date >= DateTime.UtcNow.Date
            })
            .ToListAsync();

        return history;
    }

    public async Task<List<TaskHistoryItem>> GetRecentTasksAsync(int employeeId, int count = 10)
    {
        var employee = await _context.Employees
            .Include(e => e.BranchAssignments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);

        if (employee == null) return new List<TaskHistoryItem>();

        var activeAssignments = employee.BranchAssignments
            .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
            .ToList();

        var assignedBranchIds = activeAssignments.Select(ba => ba.BranchId).ToList();

        if (!assignedBranchIds.Any()) return new List<TaskHistoryItem>();

        var allTasks = await _context.TaskItems
            .Where(t => t.IsActive)
            .ToListAsync();

        var completedDailyTasks = await _context.DailyTasks
            .Include(dt => dt.TaskItem)
            .Include(dt => dt.Branch)
            .Where(dt => assignedBranchIds.Contains(dt.BranchId))
            .ToListAsync();

        var completedKeySet = new HashSet<string>();
        foreach (var dt in completedDailyTasks)
        {
            var key = $"{dt.BranchId}_{dt.TaskItemId}_{dt.TaskDate.Date:yyyy-MM-dd}";
            completedKeySet.Add(key);
        }

        var expectedTasks = new List<(DateTime Date, string BranchName, string TaskName, bool IsCompleted)>();

        foreach (var assignment in activeAssignments)
        {
            var branch = await _context.Branches.FindAsync(assignment.BranchId);
            if (branch == null) continue;

            var hiddenTaskNames = branch.HiddenTasks ?? new List<string>();
            
            var startDate = assignment.StartDate.Date;
            var endDate = assignment.EndDate?.Date ?? DateTime.UtcNow.Date;

            foreach (var task in allTasks)
            {
                if (hiddenTaskNames.Contains(task.Name))
                    continue;

                var taskDates = GetTaskDatesInRange(task, startDate, endDate);
                
                foreach (var taskDate in taskDates)
                {
                    var key = $"{assignment.BranchId}_{task.Id}_{taskDate.Date:yyyy-MM-dd}";
                    var isCompleted = completedKeySet.Contains(key);
                    
                    expectedTasks.Add((taskDate, branch.Name, task.Name, isCompleted));
                }
            }
        }

        return expectedTasks
            .OrderByDescending(t => t.Date)
            .Take(count)
            .Select(t => new TaskHistoryItem
            {
                Date = t.Date,
                BranchName = t.BranchName,
                TaskName = t.TaskName,
                IsCompleted = t.IsCompleted,
                IsOnTime = t.IsCompleted
            })
            .ToList();
    }

    public async Task<Dictionary<int, int>> GetEmployeeScoresAsync()
    {
        var scores = new Dictionary<int, int>();
        var employees = await _context.Employees
            .Where(e => e.IsActive)
            .ToListAsync();

        foreach (var emp in employees)
        {
            var score = await CalculateEmployeeScoreAsync(emp.Id, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
            scores[emp.Id] = score;
        }

        return scores;
    }

    // ========== MULTI-BRANCH MANAGEMENT METHODS ==========

    public async Task<bool> AssignToBranchAsync(int employeeId, int branchId, DateTime startDate)
    {
        try
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            var branch = await _context.Branches.FindAsync(branchId);
            
            if (employee == null)
            {
                _logger.LogWarning($"Employee {employeeId} not found");
                return false;
            }
            
            if (branch == null)
            {
                _logger.LogWarning($"Branch {branchId} not found");
                return false;
            }

            var existing = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.EmployeeId == employeeId &&
                                           ba.BranchId == branchId &&
                                           (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date));

            if (existing != null)
            {
                _logger.LogWarning($"Employee {employeeId} already assigned to branch {branchId}");
                return false;
            }

            var assignment = new BranchAssignment
            {
                EmployeeId = employeeId,
                BranchId = branchId,
                StartDate = startDate.Kind == DateTimeKind.Utc ? startDate.Date : DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc)
            };

            _context.BranchAssignments.Add(assignment);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Assign",
                "BranchAssignment",
                assignment.Id,
                $"Employee {employee.Name} assigned to branch {branch.Name}"
            );

            _logger.LogInformation($"Employee {employeeId} assigned to branch {branchId}");
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
            var assignment = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.EmployeeId == employeeId &&
                                           ba.BranchId == branchId &&
                                           (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date));

            if (assignment == null) return false;

            assignment.EndDate = DateTime.UtcNow.Date;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Remove",
                "BranchAssignment",
                assignment.Id,
                $"Employee {employeeId} removed from branch {branchId}"
            );

            _logger.LogInformation($"Employee {employeeId} removed from branch {branchId}");
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

            assignment.EndDate = DateTime.UtcNow.Date;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "End",
                "BranchAssignment",
                assignmentId,
                $"Ended assignment for {assignment.Employee?.Name} at {assignment.Branch?.Name}"
            );

            _logger.LogInformation($"Ended branch assignment {assignmentId}");
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
        var branches = await _context.BranchAssignments
            .Where(ba => ba.EmployeeId == employeeId && 
                        (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
            .Select(ba => ba.Branch!)
            .Where(b => b != null && b.IsActive)
            .ToListAsync();

        return branches;
    }

    public async Task<List<Employee>> GetBranchEmployeesAsync(int branchId)
    {
        var employees = await _context.BranchAssignments
            .Where(ba => ba.BranchId == branchId && 
                        (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
            .Select(ba => ba.Employee!)
            .Where(e => e != null && e.IsActive)
            .ToListAsync();

        return employees;
    }

    // ========== HELPER METHODS ==========

    private int GetTaskCountInRange(TaskItem task, DateTime startDate, DateTime endDate)
    {
        var count = 0;
        var currentDate = startDate.Date;
        var maxDate = endDate.Date;
        
        if (maxDate > startDate.AddYears(1))
        {
            maxDate = startDate.AddYears(1);
        }
        
        for (var date = currentDate; date <= maxDate; date = date.AddDays(1))
        {
            if (IsTaskVisibleOnDate(task, date))
            {
                count++;
            }
        }
        
        return count;
    }

    private List<DateTime> GetTaskDatesInRange(TaskItem task, DateTime startDate, DateTime endDate)
    {
        var dates = new List<DateTime>();
        var currentDate = startDate.Date;
        var maxDate = endDate.Date;
        
        if (maxDate > startDate.AddYears(1))
        {
            maxDate = startDate.AddYears(1);
        }
        
        for (var date = currentDate; date <= maxDate; date = date.AddDays(1))
        {
            if (IsTaskVisibleOnDate(task, date))
            {
                dates.Add(date);
            }
        }
        
        return dates;
    }

    private bool IsTaskVisibleOnDate(TaskItem task, DateTime date)
    {
        if (!task.IsActive) return false;
        
        var compareDate = date.Date;
        
        if (task.AvailableFrom.HasValue && compareDate < task.AvailableFrom.Value.Date)
            return false;
        if (task.AvailableTo.HasValue && compareDate > task.AvailableTo.Value.Date)
            return false;
        
        if (task.StartDate.HasValue && compareDate < task.StartDate.Value.Date)
            return false;
        if (task.EndDate.HasValue && compareDate > task.EndDate.Value.Date)
            return false;
        
        switch (task.ExecutionType)
        {
            case TaskExecutionType.RecurringDaily:
                return true;
                
            case TaskExecutionType.RecurringWeekly:
                if (string.IsNullOrEmpty(task.WeeklyDays)) return false;
                var weeklyDays = task.WeeklyDays.Split(',').Select(int.Parse).ToList();
                return weeklyDays.Contains((int)date.DayOfWeek);
                
            case TaskExecutionType.RecurringMonthly:
                if (string.IsNullOrEmpty(task.MonthlyPattern)) return false;
                var pattern = task.MonthlyPattern.ToLower();
                if (pattern == "last")
                    return date.Day == DateTime.DaysInMonth(date.Year, date.Month);
                if (int.TryParse(pattern, out int dayOfMonth))
                    return date.Day == dayOfMonth;
                return false;
                
            case TaskExecutionType.MultiDay:
                if (!task.StartDate.HasValue) return false;
                var multiDayEndDate = task.EndDate.HasValue ? task.EndDate.Value :
                                     (task.DurationDays.HasValue ? task.StartDate.Value.AddDays(task.DurationDays.Value - 1) : task.StartDate.Value);
                return date >= task.StartDate.Value && date <= multiDayEndDate;
                
            case TaskExecutionType.OneTime:
                return task.StartDate.HasValue && date.Date == task.StartDate.Value.Date;
                
            default:
                return true;
        }
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
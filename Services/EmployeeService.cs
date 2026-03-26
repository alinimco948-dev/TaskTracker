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

            var result = new List<EmployeeListViewModel>();

            var employeeIds = employees.Select(e => e.Id).ToList();

            // Get completed tasks count per employee (last 30 days)
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            var completedTasksCount = await _context.TaskAssignments
                .Include(ta => ta.DailyTask)
                .Where(ta => employeeIds.Contains(ta.EmployeeId) &&
                             ta.DailyTask != null &&
                             ta.DailyTask.IsCompleted &&
                             ta.DailyTask.TaskDate >= thirtyDaysAgo)
                .GroupBy(ta => ta.EmployeeId)
                .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EmployeeId, x => x.Count);

            // Get total tasks count per employee
            var totalTasksCount = await _context.TaskAssignments
                .Include(ta => ta.DailyTask)
                .Where(ta => employeeIds.Contains(ta.EmployeeId) &&
                             ta.DailyTask != null &&
                             ta.DailyTask.TaskDate >= thirtyDaysAgo)
                .GroupBy(ta => ta.EmployeeId)
                .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EmployeeId, x => x.Count);

            // Get tasks with their deadlines for on-time calculation
            var allTasks = await _context.TaskAssignments
                .Include(ta => ta.DailyTask)
                    .ThenInclude(dt => dt.TaskItem)
                .Where(ta => employeeIds.Contains(ta.EmployeeId) &&
                             ta.DailyTask != null &&
                             ta.DailyTask.IsCompleted &&
                             ta.DailyTask.CompletedAt.HasValue &&
                             ta.DailyTask.TaskDate >= thirtyDaysAgo)
                .ToListAsync();

            var onTimeDict = new Dictionary<int, int>();
            foreach (var ta in allTasks)
            {
                if (ta.DailyTask?.TaskItem != null && ta.DailyTask.CompletedAt.HasValue)
                {
                    var deadline = CalculateDeadline(ta.DailyTask.TaskItem, ta.DailyTask.TaskDate);
                    if (ta.DailyTask.AdjustmentMinutes > 0)
                    {
                        deadline = deadline.AddMinutes(ta.DailyTask.AdjustmentMinutes.Value);
                    }
                    if (ta.DailyTask.CompletedAt.Value <= deadline)
                    {
                        onTimeDict[ta.EmployeeId] = onTimeDict.GetValueOrDefault(ta.EmployeeId) + 1;
                    }
                }
            }

            foreach (var emp in employees)
            {
                var currentBranch = emp.BranchAssignments
                    .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
                    .OrderByDescending(ba => ba.StartDate)
                    .FirstOrDefault()?.Branch?.Name;

                var total = totalTasksCount.GetValueOrDefault(emp.Id);
                var onTime = onTimeDict.GetValueOrDefault(emp.Id);

                var score = total > 0 ? (int)Math.Round((double)onTime / total * 100) : 0;

                result.Add(new EmployeeListViewModel
                {
                    Id = emp.Id,
                    Name = emp.Name,
                    EmployeeId = emp.EmployeeId,
                    Email = emp.Email ?? string.Empty,
                    Position = emp.Position ?? string.Empty,
                    Department = emp.Department?.Name ?? "Unassigned",
                    CurrentBranch = currentBranch ?? "Not Assigned",
                    PerformanceScore = score,
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

    private DateTime CalculateDeadline(TaskItem task, DateTime taskDate)
    {
        var deadline = taskDate.Date;
        if (task.IsSameDay)
            deadline = deadline.Add(task.Deadline);
        else
            deadline = deadline.AddDays(1).Add(task.Deadline);
        return deadline;
    }

    public async Task<Employee?> GetEmployeeByIdAsync(int id)
    {
        return await _context.Employees
            .Include(e => e.Department)
            .Include(e => e.Manager)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<Employee?> GetEmployeeByEmployeeIdAsync(string employeeId)
    {
        return await _context.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
    }

    public async Task<Employee> CreateEmployeeAsync(EmployeeViewModel model)
    {
        try
        {
            var employee = new Employee
            {
                Name = model.Name,
                EmployeeId = model.EmployeeId,
                Email = model.Email,
                Phone = model.Phone,
                Address = model.Address,
                HireDate = model.HireDate,
                Position = model.Position,
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
            var employee = await _context.Employees.FindAsync(model.Id);
            if (employee == null) return null;

            var oldValues = $"Name:{employee.Name}, Position:{employee.Position}, Dept:{employee.DepartmentId}";

            employee.Name = model.Name;
            employee.EmployeeId = model.EmployeeId;
            employee.Email = model.Email;
            employee.Phone = model.Phone;
            employee.Address = model.Address;
            employee.HireDate = model.HireDate;
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

    public async Task<EmployeeDetailsViewModel> GetEmployeeDetailsAsync(int id)
    {
        try
        {
            var employee = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Manager)
                .Include(e => e.BranchAssignments)
                    .ThenInclude(ba => ba.Branch)
                .Include(e => e.TaskAssignments)
                    .ThenInclude(ta => ta.DailyTask)
                        .ThenInclude(dt => dt.TaskItem)
                .Include(e => e.TaskAssignments)
                    .ThenInclude(ta => ta.DailyTask)
                        .ThenInclude(dt => dt.Branch)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee == null)
                throw new Exception($"Employee {id} not found");

            var startDate = DateTime.UtcNow.AddDays(-30);
            var endDate = DateTime.UtcNow;

            var recentTasks = employee.TaskAssignments
                .Where(ta => ta.DailyTask != null &&
                             ta.DailyTask.TaskDate.Date >= startDate.Date &&
                             ta.DailyTask.TaskDate.Date <= endDate.Date)
                .ToList();

            var totalTasks = recentTasks.Count;
            var completedTasks = recentTasks.Count(ta => ta.DailyTask != null && ta.DailyTask.IsCompleted);
            var onTimeTasks = recentTasks.Count(ta => IsTaskOnTime(ta.DailyTask));
            var pendingTasks = totalTasks - completedTasks;
            var performanceScore = totalTasks > 0 ? (int)Math.Round((double)onTimeTasks / totalTasks * 100) : 0;

            var branchHistory = employee.BranchAssignments
                .OrderByDescending(ba => ba.StartDate)
                .Select(ba => new BranchHistoryItem
                {
                    BranchName = ba.Branch?.Name ?? "Unknown",
                    StartDate = ba.StartDate,
                    EndDate = ba.EndDate,
                    Duration = ba.EndDate.HasValue
                        ? (ba.EndDate.Value - ba.StartDate).Days
                        : (DateTime.UtcNow - ba.StartDate).Days
                })
                .ToList();

            var recentTaskItems = employee.TaskAssignments
                .Where(ta => ta.DailyTask != null)
                .OrderByDescending(ta => ta.DailyTask!.TaskDate)
                .Take(10)
                .Select(ta => new TaskHistoryItem
                {
                    Date = ta.DailyTask!.TaskDate,
                    BranchName = ta.DailyTask.Branch?.Name ?? "Unknown",
                    TaskName = ta.DailyTask.TaskItem?.Name ?? "Unknown",
                    IsCompleted = ta.DailyTask.IsCompleted,
                    IsOnTime = IsTaskOnTime(ta.DailyTask)
                })
                .ToList();

            var chartLabels = new List<string>();
            var chartData = new List<int>();

            for (int i = 29; i >= 0; i--)
            {
                var date = DateTime.UtcNow.AddDays(-i).Date;
                chartLabels.Add(date.ToString("MMM dd"));

                var tasksOnDay = employee.TaskAssignments
                    .Count(ta => ta.DailyTask != null &&
                                ta.DailyTask.TaskDate.Date == date &&
                                ta.DailyTask.IsCompleted);
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
                PerformanceScore = performanceScore,

                BranchHistory = branchHistory,
                RecentTasks = recentTaskItems,
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
        try
        {
            startDate ??= DateTime.UtcNow.AddMonths(-1);
            endDate ??= DateTime.UtcNow;

            var taskAssignments = await _context.TaskAssignments
                .Include(ta => ta.DailyTask)
                    .ThenInclude(dt => dt.TaskItem)
                .Where(ta => ta.EmployeeId == employeeId &&
                             ta.DailyTask != null &&
                             ta.DailyTask.TaskDate.Date >= startDate.Value.Date &&
                             ta.DailyTask.TaskDate.Date <= endDate.Value.Date)
                .ToListAsync();

            if (!taskAssignments.Any()) return 0;

            var totalTasks = taskAssignments.Count;
            var onTimeTasks = 0;

            foreach (var ta in taskAssignments)
            {
                if (IsTaskOnTime(ta.DailyTask))
                {
                    onTimeTasks++;
                }
            }

            return totalTasks > 0 ? (int)Math.Round((double)onTimeTasks / totalTasks * 100) : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating score for employee {EmployeeId}", employeeId);
            return 0;
        }
    }

    public async Task<string?> GetCurrentBranchAsync(int employeeId)
    {
        var assignment = await _context.BranchAssignments
            .Include(ba => ba.Branch)
            .Where(ba => ba.EmployeeId == employeeId &&
                        (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
            .OrderByDescending(ba => ba.StartDate)
            .FirstOrDefaultAsync();

        return assignment?.Branch?.Name;
    }

    public async Task<List<BranchHistoryItem>> GetBranchHistoryAsync(int employeeId)
    {
        var history = await _context.BranchAssignments
            .Include(ba => ba.Branch)
            .Where(ba => ba.EmployeeId == employeeId)
            .OrderByDescending(ba => ba.StartDate)
            .Select(ba => new BranchHistoryItem
            {
                BranchName = ba.Branch != null ? ba.Branch.Name : "Unknown",
                StartDate = ba.StartDate,
                EndDate = ba.EndDate,
                Duration = ba.EndDate.HasValue
                    ? (ba.EndDate.Value - ba.StartDate).Days
                    : (DateTime.UtcNow - ba.StartDate).Days
            })
            .ToListAsync();

        return history;
    }

    public async Task<List<TaskHistoryItem>> GetRecentTasksAsync(int employeeId, int count = 10)
    {
        var tasks = await _context.TaskAssignments
            .Include(ta => ta.DailyTask)
                .ThenInclude(dt => dt.TaskItem)
            .Include(ta => ta.DailyTask)
                .ThenInclude(dt => dt.Branch)
            .Where(ta => ta.EmployeeId == employeeId && ta.DailyTask != null)
            .OrderByDescending(ta => ta.DailyTask!.TaskDate)
            .Take(count)
            .Select(ta => new TaskHistoryItem
            {
                Date = ta.DailyTask!.TaskDate,
                BranchName = ta.DailyTask.Branch != null ? ta.DailyTask.Branch.Name : "Unknown",
                TaskName = ta.DailyTask.TaskItem != null ? ta.DailyTask.TaskItem.Name : "Unknown",
                IsCompleted = ta.DailyTask.IsCompleted,
                IsOnTime = IsTaskOnTime(ta.DailyTask)
            })
            .ToListAsync();

        return tasks;
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

    private bool IsTaskOnTime(DailyTask? dailyTask)
    {
        if (dailyTask == null || !dailyTask.IsCompleted || !dailyTask.CompletedAt.HasValue || dailyTask.TaskItem == null)
            return false;

        var deadline = CalculateDeadline(dailyTask.TaskItem, dailyTask.TaskDate);
        if (dailyTask.AdjustmentMinutes > 0)
        {
            deadline = deadline.AddMinutes(dailyTask.AdjustmentMinutes.Value);
        }

        return dailyTask.CompletedAt.Value <= deadline.AddSeconds(30);
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
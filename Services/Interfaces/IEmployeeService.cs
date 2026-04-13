using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services.Interfaces;

public interface IEmployeeService
{
    Task<List<EmployeeListViewModel>> GetAllEmployeesAsync();
    Task<Employee?> GetEmployeeByIdAsync(int id);
    Task<Employee?> GetEmployeeByEmployeeIdAsync(string employeeId);
    Task<Employee> CreateEmployeeAsync(EmployeeViewModel model);
    Task<Employee?> UpdateEmployeeAsync(EmployeeViewModel model);
    Task<bool> DeleteEmployeeAsync(int id);
    Task<EmployeeDetailsViewModel> GetEmployeeDetailsAsync(int id, DateTime? startDate = null, DateTime? endDate = null);
    Task<int> CalculateEmployeeScoreAsync(int employeeId, DateTime? startDate = null, DateTime? endDate = null);
    Task<string?> GetCurrentBranchAsync(int employeeId);
    Task<List<string>> GetCurrentBranchesAsync(int employeeId);
    Task<List<BranchHistoryItem>> GetBranchHistoryAsync(int employeeId);
    Task<List<TaskHistoryItem>> GetRecentTasksAsync(int employeeId, int count = 10);
    Task<Dictionary<int, int>> GetEmployeeScoresAsync();
    
    // Multi-branch management methods
    Task<bool> AssignToBranchAsync(int employeeId, int branchId, DateTime startDate);
    Task<bool> RemoveFromBranchAsync(int employeeId, int branchId);
    Task<bool> EndBranchAssignmentAsync(int assignmentId);
    Task<List<Branch>> GetEmployeeBranchesAsync(int employeeId);
    Task<List<Employee>> GetBranchEmployeesAsync(int branchId);
    
    // Report controller helper methods
    Task<List<EmployeeListViewModel>> GetActiveEmployeesSummaryAsync();
}
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services.Interfaces;

public interface IBranchService
{
    Task<List<BranchListViewModel>> GetAllBranchesAsync();
    Task<Branch?> GetBranchByIdAsync(int id);
    Task<Branch> CreateBranchAsync(BranchViewModel model);
    Task<Branch?> UpdateBranchAsync(BranchViewModel model);
    Task<bool> DeleteBranchAsync(int id);
    Task<BranchDetailsViewModel> GetBranchDetailsAsync(int id);
    Task<bool> AssignEmployeeAsync(int branchId, int employeeId, DateTime startDate);
    Task<bool> EndAssignmentAsync(int assignmentId);
    Task<List<BranchAssignment>> GetAssignmentHistoryAsync(int branchId);
    Task<bool> UpdateTaskVisibilityAsync(int branchId, List<string> visibleTasks);  // Changed to accept List<string>
    Task<Dictionary<int, int>> GetBranchEmployeeCountsAsync();
    Task<Dictionary<int, double>> GetBranchCompletionRatesAsync(DateTime date);
}
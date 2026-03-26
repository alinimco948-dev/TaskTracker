using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services.Interfaces;

public interface IDepartmentService
{
    Task<List<DepartmentListViewModel>> GetAllDepartmentsAsync();
    Task<Department?> GetDepartmentByIdAsync(int id);
    Task<Department> CreateDepartmentAsync(DepartmentViewModel model);
    Task<Department?> UpdateDepartmentAsync(DepartmentViewModel model);
    Task<bool> DeleteDepartmentAsync(int id);
    Task<DepartmentDetailsViewModel> GetDepartmentDetailsAsync(int id);
    Task<Dictionary<int, int>> GetDepartmentBranchCountsAsync();
    Task<Dictionary<int, int>> GetDepartmentEmployeeCountsAsync();
}
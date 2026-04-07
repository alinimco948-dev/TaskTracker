using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services.Interfaces;

public interface IDashboardService
{
    Task<DashboardViewModel> GetDashboardViewModelAsync(DateTime localDate);
}
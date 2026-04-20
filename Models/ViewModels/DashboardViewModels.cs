using TaskTracker.Models.Entities;

namespace TaskTracker.Models.ViewModels;

public class DashboardViewModel
{
    public DateTime CurrentDate { get; set; }
    public List<Branch> Branches { get; set; } = new();
    public List<TaskItem> Tasks { get; set; } = new();
    public List<TaskItem> AllTasks { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public Dictionary<string, DailyTask> TaskData { get; set; } = new();
    public Dictionary<string, string> NotesData { get; set; } = new();
    public List<Holiday> Holidays { get; set; } = new();
    public Dictionary<int, string> BranchAssignments { get; set; } = new();
    
    // Computed properties
    public int TotalBranches => Branches.Count;
    public int TotalVisibleTasks => Tasks.Count;
    public int CompletedTasks => TaskData.Count(kvp => kvp.Value?.IsCompleted == true);
    public int PendingTasks => (Branches.Count * Tasks.Count) - CompletedTasks;
    public bool IsHoliday { get; set; }
    public string HolidayName { get; set; } = string.Empty;

    public int ComputedTotalAssignments { get; set; }
    public int ComputedPendingTasks { get; set; }
    public Dictionary<int, List<string>> HiddenTasksDict { get; set; } = new();
}
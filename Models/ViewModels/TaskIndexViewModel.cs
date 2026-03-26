namespace TaskTracker.Models.ViewModels
;
    public class TaskIndexViewModel
    {
  
    public List<TaskListViewModel> Tasks { get; set; } = new();
    public List<BranchTaskVisibilityViewModel> Branches { get; set; } = new();
}

public class BranchTaskVisibilityViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> HiddenTasks { get; set; } = new();
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Controllers;

public class BranchController : Controller
{
    private readonly IBranchService _branchService;
    private readonly IDepartmentService _departmentService;
    private readonly ITaskService _taskService;
    private readonly IEmployeeService _employeeService;
    private readonly ILogger<BranchController> _logger;
    private readonly ApplicationDbContext _context;

    public BranchController(
        IBranchService branchService,
        IDepartmentService departmentService,
        ITaskService taskService,
        IEmployeeService employeeService,
        ILogger<BranchController> logger,
        ApplicationDbContext context)
    {
        _branchService = branchService;
        _departmentService = departmentService;
        _taskService = taskService;
        _employeeService = employeeService;
        _logger = logger;
        _context = context;
    }

    // GET: Branch
    public async Task<IActionResult> Index()
    {
        try
        {
            var branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
            var employeeCounts = await _branchService.GetBranchEmployeeCountsAsync();
            ViewBag.BranchEmployeeCount = employeeCounts;

            var totalEmployees = (await _employeeService.GetAllEmployeesAsync()).Count;
            ViewBag.TotalEmployees = totalEmployees;

            // Calculate average completion rate
            var completionRates = await _branchService.GetBranchCompletionRatesAsync(DateTime.Today);
            var avgCompletion = completionRates.Values.Any() ? completionRates.Values.Average() : 0;
            ViewBag.CompletionRate = Math.Round(avgCompletion, 1);

            return View(branches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading branch index");
            return View(new List<BranchListViewModel>());
        }
    }

    // GET: Branch/Create
    public async Task<IActionResult> Create()
    {
        try
        {
            ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.Tasks = await _taskService.GetAllTasksAsync();
            return View(new BranchViewModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading create form");
            return View(new BranchViewModel());
        }
    }

    // POST: Branch/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BranchViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await _branchService.CreateBranchAsync(model);
                TempData["SuccessMessage"] = "Branch created successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating branch");
                ModelState.AddModelError("", "Unable to create branch. Please try again.");
            }
        }

        ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
        ViewBag.Tasks = await _taskService.GetAllTasksAsync();
        return View(model);
    }

    // GET: Branch/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var branch = await _branchService.GetBranchByIdAsync(id);
            if (branch == null) return NotFound();

            var model = new BranchViewModel
            {
                Id = branch.Id,
                Name = branch.Name,
                Code = branch.Code ?? string.Empty,
                Address = branch.Address ?? string.Empty,
                Phone = branch.Phone ?? string.Empty,
                Email = branch.Email ?? string.Empty,
                DepartmentId = branch.DepartmentId,
                Notes = branch.Notes ?? string.Empty,
                IsActive = branch.IsActive
            };

            ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.Tasks = await _taskService.GetAllTasksAsync();
            ViewBag.HiddenTaskNames = branch.HiddenTasks ?? new List<string>();

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading edit form for branch {Id}", id);
            return NotFound();
        }
    }

    // POST: Branch/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(BranchViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await _branchService.UpdateBranchAsync(model);
                TempData["SuccessMessage"] = "Branch updated successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating branch");
                ModelState.AddModelError("", "Unable to update branch. Please try again.");
            }
        }

        ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
        ViewBag.Tasks = await _taskService.GetAllTasksAsync();
        return View(model);
    }

    // GET: Branch/Details/5
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var branch = await _branchService.GetBranchDetailsAsync(id);
            ViewBag.AllTasks = await _taskService.GetAllTasksAsync();
            return View(branch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch details for {Id}", id);
            return NotFound();
        }
    }

    // POST: Branch/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _branchService.DeleteBranchAsync(id);
            if (result)
            {
                return Json(new { success = true, message = "Branch deleted successfully" });
            }
            return Json(new { success = false, message = "Error deleting branch" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting branch {Id}", id);
            return Json(new { success = false, message = "Error deleting branch" });
        }
    }

    // POST: Branch/AssignEmployee
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignEmployee(int branchId, int employeeId, DateTime startDate)
    {
        try
        {
            _logger.LogInformation($"Assigning employee {employeeId} to branch {branchId} starting {startDate}");

            var result = await _branchService.AssignEmployeeAsync(branchId, employeeId, startDate);

            if (result)
            {
                return Json(new { success = true, message = "Employee assigned successfully" });
            }
            return Json(new { success = false, message = "Failed to assign employee" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning employee to branch");
            return Json(new { success = false, message = "Error assigning employee" });
        }
    }

    // POST: Branch/EndAssignment/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndAssignment(int id)
    {
        try
        {
            var result = await _branchService.EndAssignmentAsync(id);
            if (result)
            {
                return Json(new { success = true, message = "Assignment ended successfully" });
            }
            return Json(new { success = false, message = "Failed to end assignment" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending assignment {Id}", id);
            return Json(new { success = false, message = "Error ending assignment" });
        }
    }

    // GET: Branch/GetAssignmentHistory/5
    [HttpGet]
    public async Task<IActionResult> GetAssignmentHistory(int id)
    {
        try
        {
            var history = await _branchService.GetAssignmentHistoryAsync(id);
            var result = history.Select(h => new
            {
                h.Id,
                EmployeeName = h.Employee?.Name ?? "Unknown",
                StartDate = h.StartDate.ToString("yyyy-MM-dd"),
                EndDate = h.EndDate?.ToString("yyyy-MM-dd"),
                IsActive = h.EndDate == null
            });

            return Json(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting assignment history for branch {Id}", id);
            return Json(new List<object>());
        }
    }

    // POST: Branch/SaveVisibility
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveVisibility([FromBody] List<TaskVisibilityUpdate> updates)
    {
        try
        {
            _logger.LogInformation($"Received {updates?.Count ?? 0} visibility updates");

            if (updates == null || !updates.Any())
            {
                return Json(new { success = false, message = "No updates received" });
            }

            // Group updates by branch
            var branchGroups = updates.GroupBy(u => u.BranchId);
            int updatedCount = 0;

            foreach (var group in branchGroups)
            {
                var branchId = group.Key;
                var branch = await _context.Branches.FindAsync(branchId);

                if (branch == null)
                {
                    _logger.LogWarning($"Branch {branchId} not found");
                    continue;
                }

                // Get all task names
                var allTasks = await _context.TaskItems
                    .Where(t => t.IsActive)
                    .Select(t => t.Name)
                    .ToListAsync();

                // Determine which tasks should be visible (checked = visible)
                var visibleTasks = group.Where(u => u.Visible).Select(u => u.TaskName).ToList();

                // Hidden tasks are all tasks minus visible tasks
                var hiddenTasks = allTasks.Except(visibleTasks).ToList();

                _logger.LogInformation($"Branch {branchId}: {hiddenTasks.Count} hidden tasks");

                // Update the branch's hidden tasks
                branch.HiddenTasks = hiddenTasks;
                branch.UpdatedAt = DateTime.UtcNow;

                updatedCount++;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation($"Saved visibility for {updatedCount} branches");

            return Json(new { success = true, message = $"Visibility updated for {updatedCount} branches" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving visibility settings");
            return Json(new { success = false, message = "Error saving visibility settings" });
        }
    }

    // POST: Branch/UpdateSingleTaskVisibility (optional - for single task updates)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSingleTaskVisibility(int branchId, string taskName, bool isVisible)
    {
        try
        {
            var branch = await _context.Branches.FindAsync(branchId);
            if (branch == null)
            {
                return Json(new { success = false, message = "Branch not found" });
            }

            var hiddenTasks = branch.HiddenTasks ?? new List<string>();

            if (isVisible)
            {
                // Task should be visible - remove from hidden list
                if (hiddenTasks.Contains(taskName))
                {
                    hiddenTasks.Remove(taskName);
                }
            }
            else
            {
                // Task should be hidden - add to hidden list if not already there
                if (!hiddenTasks.Contains(taskName))
                {
                    hiddenTasks.Add(taskName);
                }
            }

            branch.HiddenTasks = hiddenTasks;
            branch.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation($"Updated visibility for branch {branchId}, task {taskName}: {(isVisible ? "visible" : "hidden")}");

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating single task visibility");
            return Json(new { success = false, message = "Error updating visibility" });
        }
    }

    // GET: Branch/GetAvailableEmployees
    [HttpGet]
    public async Task<IActionResult> GetAvailableEmployees(int branchId)
    {
        try
        {
            // Get employees not currently assigned to any branch
            var assignedEmployeeIds = await _context.BranchAssignments
                .Where(ba => ba.EndDate == null)
                .Select(ba => ba.EmployeeId)
                .ToListAsync();

            var availableEmployees = await _context.Employees
                .Where(e => e.IsActive && !assignedEmployeeIds.Contains(e.Id))
                .OrderBy(e => e.Name)
                .Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.Position,
                    Initials = GetInitials(e.Name)
                })
                .ToListAsync();

            return Json(availableEmployees);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available employees");
            return Json(new List<object>());
        }
    }

    // Helper method to get initials
    private string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpper();
        return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
    }
}

// Model for task visibility updates
public class TaskVisibilityUpdate
{
    public int BranchId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public bool Visible { get; set; }
}
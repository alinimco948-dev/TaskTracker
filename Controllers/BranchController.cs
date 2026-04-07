using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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

            var completionRates = await _branchService.GetBranchCompletionRatesAsync(DateTime.Today);
            var avgCompletion = completionRates.Values.Any() ? completionRates.Values.Average() : 0;
            ViewBag.CompletionRate = Math.Round(avgCompletion, 1);

            return View(branches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading branch index");
            TempData["ErrorMessage"] = "Error loading branches. Please try again.";
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
            TempData["ErrorMessage"] = "Error loading form. Please try again.";
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
                var existingBranch = await _context.Branches
                    .FirstOrDefaultAsync(b => b.Name.ToLower() == model.Name.ToLower());
                
                if (existingBranch != null)
                {
                    ModelState.AddModelError("Name", "A branch with this name already exists");
                    ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
                    ViewBag.Tasks = await _taskService.GetAllTasksAsync();
                    return View(model);
                }

                await _branchService.CreateBranchAsync(model);
                TempData["SuccessMessage"] = $"Branch '{model.Name}' created successfully!";
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
                IsActive = branch.IsActive,
                HiddenTaskIds = await GetTaskIdsFromNamesAsync(branch.HiddenTasks)
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
                var existingBranch = await _context.Branches
                    .FirstOrDefaultAsync(b => b.Name.ToLower() == model.Name.ToLower() && b.Id != model.Id);
                
                if (existingBranch != null)
                {
                    ModelState.AddModelError("Name", "A branch with this name already exists");
                    ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
                    ViewBag.Tasks = await _taskService.GetAllTasksAsync();
                    ViewBag.HiddenTaskNames = await GetHiddenTaskNamesForBranchAsync(model.Id);
                    return View(model);
                }

                await _branchService.UpdateBranchAsync(model);
                TempData["SuccessMessage"] = $"Branch '{model.Name}' updated successfully!";
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
        ViewBag.HiddenTaskNames = await GetHiddenTaskNamesForBranchAsync(model.Id);
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
            var branch = await _context.Branches.FindAsync(id);
            if (branch == null)
            {
                return Json(new { success = false, message = "Branch not found" });
            }

            var hasActiveAssignments = await _context.BranchAssignments
                .AnyAsync(ba => ba.BranchId == id && ba.EndDate == null);
            
            if (hasActiveAssignments)
            {
                return Json(new { success = false, message = "Cannot delete branch with active employee assignments. Please end all assignments first." });
            }

            var result = await _branchService.DeleteBranchAsync(id);
            if (result)
            {
                return Json(new { success = true, message = $"Branch '{branch.Name}' deleted successfully" });
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
            var branch = await _context.Branches.FindAsync(branchId);
            var employee = await _context.Employees.FindAsync(employeeId);
            
            if (branch == null)
                return Json(new { success = false, message = "Branch not found" });
            if (employee == null)
                return Json(new { success = false, message = "Employee not found" });

            var existingAssignment = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.BranchId == branchId && 
                                           ba.EmployeeId == employeeId && 
                                           ba.EndDate == null);

            if (existingAssignment != null)
            {
                return Json(new { success = false, message = $"{employee.Name} is already assigned to {branch.Name}" });
            }

            var result = await _branchService.AssignEmployeeAsync(branchId, employeeId, startDate);

            if (result)
            {
                return Json(new { success = true, message = $"Successfully assigned {employee.Name} to {branch.Name}" });
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
            var assignment = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .Include(ba => ba.Branch)
                .FirstOrDefaultAsync(ba => ba.Id == id);
            
            if (assignment == null)
                return Json(new { success = false, message = "Assignment not found" });

            var result = await _branchService.EndAssignmentAsync(id);
            if (result)
            {
                return Json(new { success = true, message = $"Ended assignment for {assignment.Employee?.Name} at {assignment.Branch?.Name}" });
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
                EmployeeId = h.Employee?.EmployeeId ?? "N/A",
                StartDate = h.StartDate.ToString("yyyy-MM-dd"),
                EndDate = h.EndDate?.ToString("yyyy-MM-dd"),
                IsActive = h.EndDate == null,
                Duration = h.EndDate.HasValue ? (h.EndDate.Value - h.StartDate).Days : (DateTime.Today - h.StartDate).Days
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
            if (updates == null || !updates.Any())
            {
                return Json(new { success = false, message = "No updates received" });
            }

            var branchGroups = updates.GroupBy(u => u.BranchId);
            int updatedCount = 0;

            foreach (var group in branchGroups)
            {
                var branchId = group.Key;
                var branch = await _context.Branches.FindAsync(branchId);

                if (branch == null) continue;

                var allTasks = await _context.TaskItems
                    .Where(t => t.IsActive)
                    .Select(t => t.Name)
                    .ToListAsync();

                var visibleTasks = group.Where(u => u.Visible).Select(u => u.TaskName).ToList();
                var hiddenTasks = allTasks.Except(visibleTasks).ToList();

                branch.HiddenTasks = hiddenTasks;
                branch.UpdatedAt = DateTime.UtcNow;
                updatedCount++;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = $"Visibility updated for {updatedCount} branches" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving visibility settings");
            return Json(new { success = false, message = "Error saving visibility settings" });
        }
    }

    // GET: Branch/GetAvailableEmployees
    [HttpGet]
    public async Task<IActionResult> GetAvailableEmployees(int branchId)
    {
        try
        {
            var assignedEmployeeIds = await _context.BranchAssignments
                .Where(ba => ba.EndDate == null)
                .Select(ba => ba.EmployeeId)
                .Distinct()
                .ToListAsync();

            var availableEmployees = await _context.Employees
                .Where(e => e.IsActive && !assignedEmployeeIds.Contains(e.Id))
                .OrderBy(e => e.Name)
                .Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.EmployeeId,
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

    // GET: Branch/GetBranchEmployees
    [HttpGet]
    public async Task<IActionResult> GetBranchEmployees(int branchId)
    {
        try
        {
            var employees = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .Where(ba => ba.BranchId == branchId && ba.EndDate == null)
                .Select(ba => new
                {
                    AssignmentId = ba.Id,
                    EmployeeId = ba.EmployeeId,
                    EmployeeName = ba.Employee != null ? ba.Employee.Name : "Unknown",
                    EmployeeNumber = ba.Employee != null ? ba.Employee.EmployeeId : "N/A",
                    Position = ba.Employee != null ? ba.Employee.Position : "N/A",
                    AssignedSince = ba.StartDate,
                    Initials = ba.Employee != null ? GetInitials(ba.Employee.Name) : "?"
                })
                .ToListAsync();

            return Json(employees);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch employees");
            return Json(new List<object>());
        }
    }

    // Helper Methods
    private async Task<List<int>> GetTaskIdsFromNamesAsync(List<string> taskNames)
    {
        if (taskNames == null || !taskNames.Any()) return new List<int>();
        
        return await _context.TaskItems
            .Where(t => taskNames.Contains(t.Name))
            .Select(t => t.Id)
            .ToListAsync();
    }

    private async Task<List<string>> GetHiddenTaskNamesForBranchAsync(int branchId)
    {
        var branch = await _context.Branches.FindAsync(branchId);
        return branch?.HiddenTasks ?? new List<string>();
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

public class TaskVisibilityUpdate
{
    public int BranchId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public bool Visible { get; set; }
}
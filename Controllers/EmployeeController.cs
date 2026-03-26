using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Controllers;

public class EmployeeController : Controller
{
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private readonly IDepartmentService _departmentService;
    private readonly ILogger<EmployeeController> _logger;

    public EmployeeController(
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ILogger<EmployeeController> logger)
    {
        _employeeService = employeeService;
        _branchService = branchService;
        _departmentService = departmentService;
        _logger = logger;
    }

    // GET: Employee
    public async Task<IActionResult> Index()
    {
        try
        {
            var employees = await _employeeService.GetAllEmployeesAsync();
            ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.CurrentAssignments = await GetCurrentAssignments();
            ViewBag.EmployeeScores = await _employeeService.GetEmployeeScoresAsync();

            // Get branches for the assignment modal
            var branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Branches = branches;

            _logger.LogInformation($"Retrieved {branches?.Count ?? 0} branches for employee index");

            return View(employees);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading employee index");
            return View(new List<EmployeeListViewModel>());
        }
    }

    // GET: Employee/Create
    public async Task<IActionResult> Create()
    {
        try
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();
            var employees = await _employeeService.GetAllEmployeesAsync();

            ViewBag.Departments = departments;
            ViewBag.Managers = employees.Where(e => e.IsActive).ToList();

            return View(new EmployeeViewModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading create form");
            return View(new EmployeeViewModel());
        }
    }


    // GET: Employee/GetBranchHistory/5
[HttpGet]
public async Task<IActionResult> GetBranchHistory(int id)
{
    try
    {
        var history = await _employeeService.GetBranchHistoryAsync(id);
        return Json(history);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting branch history for employee {Id}", id);
        return Json(new List<object>());
    }
}
    // POST: Employee/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EmployeeViewModel model)
    {
        // Remove ManagerId validation if it's causing issues
        ModelState.Remove("ManagerId");

        if (ModelState.IsValid)
        {
            try
            {
                // Check if Employee ID already exists
                var existingEmployee = await _employeeService.GetEmployeeByEmployeeIdAsync(model.EmployeeId);
                if (existingEmployee != null)
                {
                    ModelState.AddModelError("EmployeeId", "Employee ID already exists");
                    ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
                    ViewBag.Managers = await _employeeService.GetAllEmployeesAsync();
                    return View(model);
                }

                await _employeeService.CreateEmployeeAsync(model);
                TempData["SuccessMessage"] = "Employee created successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating employee");
                ModelState.AddModelError("", "Unable to create employee. Please try again.");
            }
        }

        ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
        ViewBag.Managers = await _employeeService.GetAllEmployeesAsync();
        return View(model);
    }
    // GET: Employee/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var employee = await _employeeService.GetEmployeeByIdAsync(id);
            if (employee == null) return NotFound();

            var model = new EmployeeViewModel
            {
                Id = employee.Id,
                Name = employee.Name,
                EmployeeId = employee.EmployeeId,
                Email = employee.Email ?? string.Empty,
                Phone = employee.Phone ?? string.Empty,
                Address = employee.Address ?? string.Empty,
                HireDate = employee.HireDate,
                Position = employee.Position ?? string.Empty,
                DepartmentId = employee.DepartmentId,
                ManagerId = employee.ManagerId,
                IsActive = employee.IsActive
            };

            ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
            var allEmployees = await _employeeService.GetAllEmployeesAsync();
            ViewBag.Managers = allEmployees.Where(e => e.Id != id && e.IsActive).ToList();

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading edit form for employee {Id}", id);
            return NotFound();
        }
    }

    // POST: Employee/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EmployeeViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                // Check if Employee ID already exists (excluding current employee)
                var existingEmployee = await _employeeService.GetEmployeeByEmployeeIdAsync(model.EmployeeId);
                if (existingEmployee != null && existingEmployee.Id != model.Id)
                {
                    ModelState.AddModelError("EmployeeId", "Employee ID already exists");
                    ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
                    var allEmployees = await _employeeService.GetAllEmployeesAsync();
                    ViewBag.Managers = allEmployees.Where(e => e.Id != model.Id && e.IsActive).ToList();
                    return View(model);
                }

                var updated = await _employeeService.UpdateEmployeeAsync(model);
                if (updated == null)
                {
                    return NotFound();
                }

                TempData["SuccessMessage"] = "Employee updated successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee");
                ModelState.AddModelError("", "Unable to update employee. Please try again.");
            }
        }

        ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
        var employees = await _employeeService.GetAllEmployeesAsync();
        ViewBag.Managers = employees.Where(e => e.Id != model.Id && e.IsActive).ToList();
        return View(model);
    }

    // GET: Employee/Details/5
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var employee = await _employeeService.GetEmployeeDetailsAsync(id);

            // Get branches for the assignment modal
            var branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Branches = branches;

            return View(employee);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employee details for {Id}", id);
            return NotFound();
        }
    }

    // POST: Employee/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _employeeService.DeleteEmployeeAsync(id);
            if (result)
            {
                return Json(new { success = true, message = "Employee deleted successfully" });
            }
            return Json(new { success = false, message = "Error deleting employee" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting employee {Id}", id);
            return Json(new { success = false, message = "Error deleting employee" });
        }
    }

    // POST: Employee/AssignBranch
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignBranch(int employeeId, int branchId, DateTime startDate)
    {
        try
        {
            _logger.LogInformation($"Assigning employee {employeeId} to branch {branchId} starting {startDate}");

            var result = await _branchService.AssignEmployeeAsync(branchId, employeeId, startDate);

            if (result)
            {
                return Json(new { success = true, message = "Branch assigned successfully" });
            }
            return Json(new { success = false, message = "Failed to assign branch" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning branch to employee {EmployeeId}", employeeId);
            return Json(new { success = false, message = "Error assigning branch" });
        }
    }

    // GET: Employee/GetAvailableBranches
    [HttpGet]
    public async Task<IActionResult> GetAvailableBranches()
    {
        try
        {
            var branches = await _branchService.GetAllBranchesAsync();
            var activeBranches = branches.Where(b => b.IsActive).Select(b => new
            {
                id = b.Id,
                name = b.Name,
                code = b.Code
            }).ToList();

            return Json(activeBranches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available branches");
            return Json(new List<object>());
        }
    }

    // Helper method to get current branch assignments
    private async Task<Dictionary<int, string>> GetCurrentAssignments()
    {
        var assignments = new Dictionary<int, string>();
        var employees = await _employeeService.GetAllEmployeesAsync();

        foreach (var emp in employees)
        {
            var branch = await _employeeService.GetCurrentBranchAsync(emp.Id);
            if (!string.IsNullOrEmpty(branch))
            {
                assignments[emp.Id] = branch;
            }
        }

        return assignments;
    }
}
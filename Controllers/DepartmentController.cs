using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Models.Entities;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Controllers;

public class DepartmentController : Controller
{
    private readonly IDepartmentService _departmentService;
    private readonly ILogger<DepartmentController> _logger;
    private readonly ApplicationDbContext _context;

    public DepartmentController(
        IDepartmentService departmentService,
        ILogger<DepartmentController> logger,
        ApplicationDbContext context)
    {
        _departmentService = departmentService;
        _logger = logger;
        _context = context;
    }

    // GET: Department/Index
    public async Task<IActionResult> Index()
    {
        try
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();
            var branchCounts = await _departmentService.GetDepartmentBranchCountsAsync();
            var employeeCounts = await _departmentService.GetDepartmentEmployeeCountsAsync();

            ViewBag.TotalBranches = branchCounts.Values.Sum();
            ViewBag.TotalEmployees = employeeCounts.Values.Sum();

            return View(departments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading departments");
            TempData["ErrorMessage"] = "Error loading departments. Please try again.";
            return View(new List<DepartmentListViewModel>());
        }
    }

    // GET: Department/Create
    public IActionResult Create()
    {
        return View(new DepartmentViewModel());
    }

    // POST: Department/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DepartmentViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var existing = await _context.Departments
                    .FirstOrDefaultAsync(d => d.Name.ToLower() == model.Name.ToLower());
                
                if (existing != null)
                {
                    ModelState.AddModelError("Name", "A department with this name already exists");
                    return View(model);
                }

                await _departmentService.CreateDepartmentAsync(model);
                TempData["SuccessMessage"] = $"Department '{model.Name}' created successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating department");
                ModelState.AddModelError("", "Unable to create department. Please try again.");
            }
        }

        return View(model);
    }

    // GET: Department/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var department = await _departmentService.GetDepartmentByIdAsync(id);
        if (department == null) return NotFound();

        var model = new DepartmentViewModel
        {
            Id = department.Id,
            Name = department.Name,
            Code = department.Code ?? string.Empty,
            Description = department.Description ?? string.Empty,
            IsActive = department.IsActive
        };

        return View(model);
    }

    // POST: Department/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DepartmentViewModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var existing = await _context.Departments
                    .FirstOrDefaultAsync(d => d.Name.ToLower() == model.Name.ToLower() && d.Id != model.Id);
                
                if (existing != null)
                {
                    ModelState.AddModelError("Name", "A department with this name already exists");
                    return View(model);
                }

                await _departmentService.UpdateDepartmentAsync(model);
                TempData["SuccessMessage"] = $"Department '{model.Name}' updated successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating department");
                ModelState.AddModelError("", "Unable to update department. Please try again.");
            }
        }

        return View(model);
    }

    // GET: Department/Details/5
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var department = await _departmentService.GetDepartmentDetailsAsync(id);
            if (department == null)
            {
                TempData["ErrorMessage"] = "Department not found";
                return RedirectToAction(nameof(Index));
            }
            return View(department);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting department details for {Id}", id);
            TempData["ErrorMessage"] = "Error loading department details";
            return RedirectToAction(nameof(Index));
        }
    }

    // POST: Department/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var department = await _context.Departments.FindAsync(id);
            if (department == null)
            {
                return Json(new { success = false, message = "Department not found" });
            }

            var hasBranches = await _context.Branches.AnyAsync(b => b.DepartmentId == id && b.IsActive);
            var hasEmployees = await _context.Employees.AnyAsync(e => e.DepartmentId == id && e.IsActive);

            if (hasBranches || hasEmployees)
            {
                return Json(new { success = false, message = "Cannot delete department with active branches or employees" });
            }

            var result = await _departmentService.DeleteDepartmentAsync(id);
            return Json(new { success = result, message = result ? $"Department '{department.Name}' deleted successfully" : "Error deleting department" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting department {Id}", id);
            return Json(new { success = false, message = "Error deleting department" });
        }
    }
}
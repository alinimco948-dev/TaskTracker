using Microsoft.AspNetCore.Mvc;
using TaskTracker.Models.ViewModels;
using TaskTracker.Services.Interfaces;

namespace TaskTracker.Controllers;

public class DepartmentController : Controller
{
    private readonly IDepartmentService _departmentService;
    private readonly ILogger<DepartmentController> _logger;

    public DepartmentController(
        IDepartmentService departmentService,
        ILogger<DepartmentController> logger)
    {
        _departmentService = departmentService;
        _logger = logger;
    }

    // GET: Department/Index - Returns a LIST of departments for the Index view
    public async Task<IActionResult> Index()
    {
        var departments = await _departmentService.GetAllDepartmentsAsync(); // Returns List<DepartmentListViewModel>

        var branchCounts = await _departmentService.GetDepartmentBranchCountsAsync();
        var employeeCounts = await _departmentService.GetDepartmentEmployeeCountsAsync();

        ViewBag.TotalBranches = branchCounts.Values.Sum();
        ViewBag.TotalEmployees = employeeCounts.Values.Sum();

        return View(departments); // Passes List<DepartmentListViewModel> to Index.cshtml
    }

    // GET: Department/Create - Returns a SINGLE empty view model for the Create form
    public IActionResult Create()
    {
        return View(new DepartmentViewModel()); // Returns single DepartmentViewModel to Create.cshtml
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
                await _departmentService.CreateDepartmentAsync(model);
                TempData["SuccessMessage"] = "Department created successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating department");
                ModelState.AddModelError("", "Unable to create department. Please try again.");
            }
        }

        return View(model); // Returns single DepartmentViewModel back to Create.cshtml
    }

    // GET: Department/Edit/5 - Returns a SINGLE department for editing
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

        return View(model); // Returns single DepartmentViewModel to Edit.cshtml
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
                await _departmentService.UpdateDepartmentAsync(model);
                TempData["SuccessMessage"] = "Department updated successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating department");
                ModelState.AddModelError("", "Unable to update department. Please try again.");
            }
        }

        return View(model); // Returns single DepartmentViewModel back to Edit.cshtml
    }

    // GET: Department/Details/5 - Returns a SINGLE department details
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var department = await _departmentService.GetDepartmentDetailsAsync(id); // Returns DepartmentDetailsViewModel
            return View(department); // Passes single DepartmentDetailsViewModel to Details.cshtml
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting department details");
            return NotFound();
        }
    }

    // POST: Department/Delete/5
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _departmentService.DeleteDepartmentAsync(id);
        return Json(new { success = result, message = result ? "Department deleted successfully" : "Cannot delete department with active branches or employees" });
    }
}
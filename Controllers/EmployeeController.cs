using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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
    private readonly ApplicationDbContext _context;
    private readonly ITaskCalculationService _taskCalculationService;
    private readonly ITimezoneService _timezoneService;

    public EmployeeController(
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ILogger<EmployeeController> logger,
        ApplicationDbContext context,
        ITaskCalculationService taskCalculationService,
        ITimezoneService timezoneService)
    {
        _employeeService = employeeService;
        _branchService = branchService;
        _departmentService = departmentService;
        _logger = logger;
        _context = context;
        _taskCalculationService = taskCalculationService;
        _timezoneService = timezoneService;
    }

    // GET: Employee
    public async Task<IActionResult> Index()
    {
        try
        {
            var employees = await _employeeService.GetAllEmployeesAsync();
            ViewBag.Departments = await _departmentService.GetAllDepartmentsAsync();
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => new BranchListViewModel { Id = b.Id, Name = b.Name, Code = b.Code ?? "", IsActive = b.IsActive })
                .AsNoTracking()
                .ToListAsync();
            ViewBag.Branches = branches;
            ViewBag.EmployeeScores = await _employeeService.GetEmployeeScoresAsync();

            return View(employees ?? new List<EmployeeListViewModel>());
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
            await LoadViewBagDataAsync();
            return View(new EmployeeViewModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading create form");
            ViewBag.Departments = new List<DepartmentListViewModel>();
            ViewBag.Managers = new List<EmployeeListViewModel>();
            ViewBag.Branches = new List<BranchListViewModel>();
            return View(new EmployeeViewModel());
        }
    }

    // POST: Employee/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Id,Name,EmployeeId,Email,Phone,Address,HireDate,Position,DepartmentId,ManagerId,IsActive,BranchIds")] EmployeeViewModel model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError("Name", "Name is required");
            }
            if (string.IsNullOrWhiteSpace(model.EmployeeId))
            {
                ModelState.AddModelError("EmployeeId", "Employee ID is required");
            }

            if (!string.IsNullOrWhiteSpace(model.Email))
            {
                var existingEmail = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Email == model.Email.Trim());
                
                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", $"Email '{model.Email}' is already in use.");
                }
            }

            if (!string.IsNullOrWhiteSpace(model.EmployeeId))
            {
                var existingEmployeeId = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.EmployeeId == model.EmployeeId.Trim());
                
                if (existingEmployeeId != null)
                {
                    ModelState.AddModelError("EmployeeId", $"Employee ID '{model.EmployeeId}' is already in use.");
                }
            }

            if (!ModelState.IsValid)
            {
                await LoadViewBagDataAsync();
                return View(model);
            }

            if (model.BranchIds == null)
            {
                model.BranchIds = new List<int>();
            }

            var employee = new Employee
            {
                Name = model.Name.Trim(),
                EmployeeId = model.EmployeeId.Trim(),
                Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
                Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim(),
                HireDate = model.HireDate.HasValue ? DateTime.SpecifyKind(model.HireDate.Value, DateTimeKind.Utc) : null,
                Position = string.IsNullOrWhiteSpace(model.Position) ? null : model.Position.Trim(),
                DepartmentId = model.DepartmentId,
                ManagerId = model.ManagerId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            if (model.BranchIds.Any())
            {
                foreach (var branchId in model.BranchIds)
                {
                    var assignment = new BranchAssignment
                    {
                        EmployeeId = employee.Id,
                        BranchId = branchId,
                        StartDate = DateTime.UtcNow.Date
                    };
                    _context.BranchAssignments.Add(assignment);
                }
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = $"Employee '{employee.Name}' created successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error creating employee");
            if (ex.InnerException?.Message?.Contains("IX_Employees_Email") == true)
            {
                ModelState.AddModelError("Email", "This email address is already in use.");
            }
            else if (ex.InnerException?.Message?.Contains("IX_Employees_EmployeeId") == true)
            {
                ModelState.AddModelError("EmployeeId", "This Employee ID is already in use.");
            }
            else
            {
                ModelState.AddModelError("", $"Database error: {ex.InnerException?.Message ?? ex.Message}");
            }
            await LoadViewBagDataAsync();
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating employee");
            ModelState.AddModelError("", ex.Message);
            await LoadViewBagDataAsync();
            return View(model);
        }
    }

    // GET: Employee/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Id == id);
                
            if (employee == null) return NotFound();

            // Get current active branch IDs
            var currentBranchIds = await GetCurrentBranchIdsAsync(id);

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
                IsActive = employee.IsActive,
                BranchIds = currentBranchIds
            };

            await LoadViewBagDataAsync();
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
    public async Task<IActionResult> Edit([Bind("Id,Name,EmployeeId,Email,Phone,Address,HireDate,Position,DepartmentId,ManagerId,IsActive,BranchIds")] EmployeeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await LoadViewBagDataAsync();
            return View(model);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(model.EmployeeId))
            {
                var existingEmployee = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.EmployeeId == model.EmployeeId && e.Id != model.Id);
                if (existingEmployee != null)
                {
                    ModelState.AddModelError("EmployeeId", $"Employee ID '{model.EmployeeId}' is already in use by another employee.");
                    await LoadViewBagDataAsync();
                    return View(model);
                }
            }

            if (!string.IsNullOrWhiteSpace(model.Email))
            {
                var existingEmail = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Email == model.Email.Trim() && e.Id != model.Id);
                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", $"Email '{model.Email}' is already in use by another employee.");
                    await LoadViewBagDataAsync();
                    return View(model);
                }
            }

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == model.Id);
            if (employee == null) return NotFound();

            employee.Name = model.Name;
            employee.EmployeeId = model.EmployeeId;
            employee.Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();
            employee.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
            employee.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim();
            employee.Position = string.IsNullOrWhiteSpace(model.Position) ? null : model.Position.Trim();
            employee.DepartmentId = model.DepartmentId;
            employee.ManagerId = model.ManagerId;
            employee.IsActive = model.IsActive;
            employee.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var newBranchIds = model.BranchIds ?? new List<int>();
            var todayUtc = DateTime.UtcNow.Date;
            
            var currentAssignments = await _context.BranchAssignments
                .Where(ba => ba.EmployeeId == model.Id && (ba.EndDate == null || ba.EndDate.Value.Date >= todayUtc))
                .ToListAsync();

            var currentBranchIds = currentAssignments.Select(a => a.BranchId).ToHashSet();

            foreach (var assignment in currentAssignments.Where(a => !newBranchIds.Contains(a.BranchId)))
            {
                assignment.EndDate = todayUtc;
            }

            foreach (var branchId in newBranchIds.Where(id => !currentBranchIds.Contains(id)))
            {
                _context.BranchAssignments.Add(new BranchAssignment
                {
                    EmployeeId = model.Id,
                    BranchId = branchId,
                    StartDate = todayUtc
                });
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Employee '{employee.Name}' updated successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error updating employee");
            if (ex.InnerException?.Message?.Contains("IX_Employees_Email") == true)
                ModelState.AddModelError("Email", "This email address is already in use.");
            else if (ex.InnerException?.Message?.Contains("IX_Employees_EmployeeId") == true)
                ModelState.AddModelError("EmployeeId", "This Employee ID is already in use.");
            else
                ModelState.AddModelError("", $"Database error: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating employee {Id}", model.Id);
            ModelState.AddModelError("", "Unable to update employee. Please try again.");
        }

        await LoadViewBagDataAsync();
        return View(model);
    }

    // GET: Employee/Details/5
    public async Task<IActionResult> Details(int id, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            if (!startDate.HasValue)
                startDate = DateTime.UtcNow.AddDays(-30);
            if (!endDate.HasValue)
                endDate = DateTime.UtcNow;

            var employee = await _employeeService.GetEmployeeDetailsAsync(id, startDate.Value, endDate.Value);
            if (employee == null) return NotFound();
            
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => new BranchListViewModel 
                { 
                    Id = b.Id, 
                    Name = b.Name, 
                    Code = b.Code ?? string.Empty,
                    IsActive = b.IsActive
                })
                .AsNoTracking()
                .ToListAsync();
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

    // GET: Employee/GetBranchHistory/5
    [HttpGet]
    public async Task<IActionResult> GetBranchHistory(int id)
    {
        try
        {
            var history = await _employeeService.GetBranchHistoryAsync(id);
            return Json(history ?? new List<BranchHistoryItem>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch history for employee {Id}", id);
            return Json(new List<object>());
        }
    }

    // GET: Employee/GetEmployeeBranches
    [HttpGet]
    public async Task<IActionResult> GetEmployeeBranches(int id)
    {
        try
        {
            var todayUtc = DateTime.UtcNow.Date;
            var branches = await _context.BranchAssignments
                .Include(ba => ba.Branch)
                .Where(ba => ba.EmployeeId == id && (ba.EndDate == null || ba.EndDate.Value.Date >= todayUtc))
                .Select(ba => new { 
                    ba.BranchId, 
                    BranchName = ba.Branch != null ? ba.Branch.Name : "Unknown", 
                    ba.StartDate, 
                    ba.EndDate 
                })
                .AsNoTracking()
                .ToListAsync();

            return Json(branches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employee branches for {Id}", id);
            return Json(new List<object>());
        }
    }

    // Helper method to get current branch IDs for an employee
    private async Task<List<int>> GetCurrentBranchIdsAsync(int employeeId)
    {
        var todayUtc = DateTime.UtcNow.Date;
        return await _context.BranchAssignments
            .Where(ba => ba.EmployeeId == employeeId && 
                        (ba.EndDate == null || ba.EndDate.Value.Date >= todayUtc))
            .Select(ba => ba.BranchId)
            .ToListAsync();
    }

    // Helper method to get current branch assignments
    private async Task<Dictionary<int, string>> GetCurrentAssignments()
    {
        var assignments = new Dictionary<int, string>();
        try
        {
            var employees = await _employeeService.GetAllEmployeesAsync();
            if (employees != null)
            {
                foreach (var emp in employees)
                {
                    var branches = await _employeeService.GetCurrentBranchesAsync(emp.Id);
                    if (branches != null && branches.Any())
                    {
                        assignments[emp.Id] = string.Join(", ", branches);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current assignments");
        }
        return assignments;
    }

    // Helper method to load ViewBag data
    private async Task LoadViewBagDataAsync()
    {
        try
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.Departments = departments ?? new List<DepartmentListViewModel>();
            
            var allEmployees = await _employeeService.GetAllEmployeesAsync();
            ViewBag.Managers = allEmployees?.Where(e => e.IsActive).ToList() ?? new List<EmployeeListViewModel>();
            
            var allBranches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => new BranchListViewModel 
                { 
                    Id = b.Id, 
                    Name = b.Name, 
                    Code = b.Code ?? string.Empty,
                    IsActive = b.IsActive
                })
                .AsNoTracking()
                .ToListAsync();
            
            ViewBag.Branches = allBranches;
            _logger.LogInformation($"Loaded {allBranches.Count} branches for ViewBag");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading ViewBag data");
            ViewBag.Departments = new List<DepartmentListViewModel>();
            ViewBag.Managers = new List<EmployeeListViewModel>();
            ViewBag.Branches = new List<BranchListViewModel>();
        }
    }
}
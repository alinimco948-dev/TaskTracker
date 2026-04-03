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
    private readonly ApplicationDbContext _context;

    public EmployeeController(
        IEmployeeService employeeService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ILogger<EmployeeController> logger,
        ApplicationDbContext context)
    {
        _employeeService = employeeService;
        _branchService = branchService;
        _departmentService = departmentService;
        _logger = logger;
        _context = context;
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
                .ToListAsync();
            ViewBag.Branches = branches;

            _logger.LogInformation($"Retrieved {branches?.Count ?? 0} branches for employee index");

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
            var departments = await _departmentService.GetAllDepartmentsAsync();
            var employees = await _employeeService.GetAllEmployeesAsync();
            
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
                .ToListAsync();

            ViewBag.Departments = departments ?? new List<DepartmentListViewModel>();
            ViewBag.Managers = employees?.Where(e => e.IsActive).ToList() ?? new List<EmployeeListViewModel>();
            ViewBag.Branches = allBranches;

            _logger.LogInformation($"Create form loaded with {allBranches?.Count ?? 0} branches available");

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
    public async Task<IActionResult> Create(EmployeeViewModel model)
    {
        try
        {
            _logger.LogInformation("Starting employee creation process");

            ModelState.Remove("ManagerId");

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
                    .FirstOrDefaultAsync(e => e.Email == model.Email.Trim());
                
                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", $"Email '{model.Email}' is already in use.");
                }
            }

            if (!string.IsNullOrWhiteSpace(model.EmployeeId))
            {
                var existingEmployeeId = await _context.Employees
                    .FirstOrDefaultAsync(e => e.EmployeeId == model.EmployeeId.Trim());
                
                if (existingEmployeeId != null)
                {
                    ModelState.AddModelError("EmployeeId", $"Employee ID '{model.EmployeeId}' is already in use.");
                }
            }

            if (ModelState.IsValid)
            {
                if (model.BranchIds == null)
                {
                    model.BranchIds = new List<int>();
                }

                _logger.LogInformation($"Creating employee: {model.Name}, Branches: {string.Join(", ", model.BranchIds)}");

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
                _logger.LogInformation($"Employee created with ID: {employee.Id}");

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
                    _logger.LogInformation($"Saved {model.BranchIds.Count} branch assignments");
                }

                TempData["SuccessMessage"] = $"Employee '{employee.Name}' created successfully!";
                return RedirectToAction(nameof(Index));
            }
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating employee");
            ModelState.AddModelError("", ex.Message);
        }

        await LoadViewBagData();
        return View(model);
    }

    // GET: Employee/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var employee = await _context.Employees
                .Include(e => e.BranchAssignments)
                .FirstOrDefaultAsync(e => e.Id == id);
                
            if (employee == null) return NotFound();

            var currentBranchIds = employee.BranchAssignments?
                .Where(ba => ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date)
                .Select(ba => ba.BranchId)
                .ToList() ?? new List<int>();

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

            await LoadViewBagData();
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
                _logger.LogInformation($"Editing employee {model.Id}, Branches: {string.Join(", ", model.BranchIds ?? new List<int>())}");
                
                if (!string.IsNullOrWhiteSpace(model.EmployeeId))
                {
                    var existingEmployee = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeId == model.EmployeeId && e.Id != model.Id);
                    if (existingEmployee != null)
                    {
                        ModelState.AddModelError("EmployeeId", "Employee ID already exists");
                        await LoadViewBagData();
                        return View(model);
                    }
                }

                if (!string.IsNullOrWhiteSpace(model.Email))
                {
                    var existingEmail = await _context.Employees
                        .FirstOrDefaultAsync(e => e.Email == model.Email.Trim() && e.Id != model.Id);
                    
                    if (existingEmail != null)
                    {
                        ModelState.AddModelError("Email", $"Email '{model.Email}' is already in use.");
                        await LoadViewBagData();
                        return View(model);
                    }
                }

                var employee = await _context.Employees.FindAsync(model.Id);
                if (employee == null)
                {
                    return NotFound();
                }

                employee.Name = model.Name;
                employee.EmployeeId = model.EmployeeId;
                employee.Email = model.Email;
                employee.Phone = model.Phone;
                employee.Address = model.Address;
                employee.HireDate = model.HireDate.HasValue ? DateTime.SpecifyKind(model.HireDate.Value, DateTimeKind.Utc) : null;
                employee.Position = model.Position;
                employee.DepartmentId = model.DepartmentId;
                employee.ManagerId = model.ManagerId;
                employee.IsActive = model.IsActive;
                employee.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var newBranchIds = model.BranchIds ?? new List<int>();
                
                var currentAssignments = await _context.BranchAssignments
                    .Where(ba => ba.EmployeeId == model.Id && (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
                    .ToListAsync();

                foreach (var assignment in currentAssignments)
                {
                    if (!newBranchIds.Contains(assignment.BranchId))
                    {
                        assignment.EndDate = DateTime.UtcNow.Date;
                    }
                }

                var currentBranchIds = currentAssignments.Select(a => a.BranchId).ToHashSet();
                foreach (var branchId in newBranchIds)
                {
                    if (!currentBranchIds.Contains(branchId))
                    {
                        var assignment = new BranchAssignment
                        {
                            EmployeeId = model.Id,
                            BranchId = branchId,
                            StartDate = DateTime.UtcNow.Date
                        };
                        _context.BranchAssignments.Add(assignment);
                    }
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Employee updated successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating employee");
                ModelState.AddModelError("", "Unable to update employee. Please try again.");
            }
        }

        await LoadViewBagData();
        return View(model);
    }

    // GET: Employee/Details/5
    public async Task<IActionResult> Details(int id, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            // Set default date range (last 30 days if not specified)
            if (!startDate.HasValue)
                startDate = DateTime.UtcNow.AddDays(-30);
            if (!endDate.HasValue)
                endDate = DateTime.UtcNow;

            var employee = await _employeeService.GetEmployeeDetailsAsync(id, startDate.Value, endDate.Value);
            if (employee == null) return NotFound();
            
            // Load branches for the assign branch modal
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

    // POST: Employee/Details - For date range filter
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Details(int id, DateTime startDate, DateTime endDate)
    {
        try
        {
            // Ensure end date is at least start date
            if (endDate < startDate)
            {
                endDate = startDate;
            }

            var employee = await _employeeService.GetEmployeeDetailsAsync(id, startDate, endDate);
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
                .ToListAsync();
            ViewBag.Branches = branches;

            return View(employee);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employee details for {Id} with date range", id);
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
            var branches = await _context.BranchAssignments
                .Include(ba => ba.Branch)
                .Where(ba => ba.EmployeeId == id && (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
                .Select(ba => new { 
                    ba.BranchId, 
                    BranchName = ba.Branch != null ? ba.Branch.Name : "Unknown", 
                    ba.StartDate, 
                    ba.EndDate 
                })
                .ToListAsync();

            return Json(branches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employee branches for {Id}", id);
            return Json(new List<object>());
        }
    }

    // POST: Employee/AssignBranch
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignBranch(int employeeId, int branchId, DateTime startDate)
    {
        try
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            var branch = await _context.Branches.FindAsync(branchId);
            
            if (employee == null)
            {
                return Json(new { success = false, message = "Employee not found" });
            }
            
            if (branch == null)
            {
                return Json(new { success = false, message = "Branch not found" });
            }

            var existingAssignment = await _context.BranchAssignments
                .FirstOrDefaultAsync(ba => ba.EmployeeId == employeeId && 
                                           ba.BranchId == branchId && 
                                           (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date));

            if (existingAssignment != null)
            {
                return Json(new { success = false, message = $"{employee.Name} is already assigned to {branch.Name}" });
            }

            var assignment = new BranchAssignment
            {
                EmployeeId = employeeId,
                BranchId = branchId,
                StartDate = startDate.Kind == DateTimeKind.Utc ? startDate.Date : DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc)
            };

            _context.BranchAssignments.Add(assignment);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Successfully assigned {employee.Name} to {branch.Name}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning branch");
            return Json(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    // POST: Employee/RemoveBranch
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBranch(int assignmentId)
    {
        try
        {
            var assignment = await _context.BranchAssignments
                .Include(ba => ba.Employee)
                .Include(ba => ba.Branch)
                .FirstOrDefaultAsync(ba => ba.Id == assignmentId);
                
            if (assignment == null)
            {
                return Json(new { success = false, message = "Assignment not found" });
            }

            assignment.EndDate = DateTime.UtcNow.Date;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Branch assignment ended successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing branch assignment");
            return Json(new { success = false, message = "Error removing branch assignment" });
        }
    }

    // GET: Employee/GetAvailableBranches
    [HttpGet]
    public async Task<IActionResult> GetAvailableBranches(int? employeeId = null)
    {
        try
        {
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => new { b.Id, b.Name, b.Code })
                .ToListAsync();
            
            if (employeeId.HasValue)
            {
                var assignedBranchIds = await _context.BranchAssignments
                    .Where(ba => ba.EmployeeId == employeeId.Value && (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
                    .Select(ba => ba.BranchId)
                    .ToListAsync();
                    
                branches = branches.Where(b => !assignedBranchIds.Contains(b.Id)).ToList();
            }

            return Json(branches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available branches");
            return Json(new List<object>());
        }
    }

    // GET: Employee/VerifyAssignments/{id}
    [HttpGet]
    public async Task<IActionResult> VerifyAssignments(int id)
    {
        try
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
            {
                return Json(new { success = false, message = "Employee not found" });
            }
            
            var assignments = await _context.BranchAssignments
                .Include(ba => ba.Branch)
                .Where(ba => ba.EmployeeId == id)
                .Select(ba => new
                {
                    ba.Id,
                    ba.BranchId,
                    BranchName = ba.Branch != null ? ba.Branch.Name : "Unknown",
                    ba.StartDate,
                    ba.EndDate,
                    IsActive = ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date
                })
                .ToListAsync();
            
            return Json(new
            {
                success = true,
                employeeId = id,
                employeeName = employee.Name,
                assignmentCount = assignments.Count,
                assignments = assignments
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    // GET: Employee/TestBranches
    [HttpGet]
    public async Task<IActionResult> TestBranches(int? employeeId = null)
    {
        try
        {
            var branches = await _context.Branches
                .Where(b => b.IsActive)
                .Select(b => new { b.Id, b.Name, b.Code, b.IsActive })
                .ToListAsync();
            
            var result = new { success = true, count = branches.Count, branches = branches };
            
            if (employeeId.HasValue)
            {
                var assignments = await _context.BranchAssignments
                    .Include(ba => ba.Branch)
                    .Where(ba => ba.EmployeeId == employeeId.Value && (ba.EndDate == null || ba.EndDate.Value.Date >= DateTime.UtcNow.Date))
                    .ToListAsync();
                
                var employeeResult = new
                {
                    employeeId = employeeId.Value,
                    assignmentCount = assignments.Count,
                    assignments = assignments.Select(a => new { a.BranchId, BranchName = a.Branch?.Name, a.StartDate, a.EndDate })
                };
                
                return Json(new { success = true, branches = branches, employeeAssignments = employeeResult });
            }
            
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
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
    private async Task LoadViewBagData()
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
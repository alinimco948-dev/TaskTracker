
using Microsoft.EntityFrameworkCore;
using TaskTracker.Data;
using TaskTracker.Services;
using TaskTracker.Services.Interfaces;
using OfficeOpenXml;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllersWithViews();

// Configure DbContext - use Railway's DATABASE_URL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var railwayConnection = Environment.GetEnvironmentVariable("DATABASE_URL");
    var configConnection = builder.Configuration.GetConnectionString("DefaultConnection");
    var rawConnectionString = railwayConnection ?? configConnection;
    
    // Convert PostgreSQL URI to Npgsql format if needed
    var connectionString = ConvertPostgresUriToConnectionString(rawConnectionString);
    
    options.UseNpgsql(connectionString);
});

// Helper function to convert PostgreSQL URI to Npgsql connection string
string ConvertPostgresUriToConnectionString(string uriString)
{
    if (string.IsNullOrEmpty(uriString))
        return uriString;
    
    if (uriString.Contains("Host=") || uriString.Contains("host="))
        return uriString;
    
    try
    {
        var uri = new Uri(uriString);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        var database = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(database)) database = "postgres";
        
        return $"Host={uri.Host};Port={uri.Port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }
    catch
    {
        return uriString;
    }
}

// Register services
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IBranchService, BranchService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IHolidayService, HolidayService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<TaskSchedulerService>();
builder.Services.AddScoped<ITaskCalculationService, TaskCalculationService>();
builder.Services.AddScoped<ITimezoneService, TimezoneService>();

// Add HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Add session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Add response caching
builder.Services.AddResponseCaching();

// Configure Kestrel for Railway
builder.WebHost.ConfigureKestrel(options =>
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    options.ListenAnyIP(int.Parse(port));
});

// Set EPPlus license
ExcelPackage.License.SetNonCommercialPersonal($"TaskTracker-{Environment.UserName}");

var app = builder.Build();

// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseSession();
app.UseResponseCaching();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/", () => Results.Redirect("/Home/Index"));

// Run migrations on startup (seed data will be applied automatically!)
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("=========================================");
        logger.LogInformation("Starting TaskTracker Application");
        logger.LogInformation("=========================================");
        
        // Test database connection
        logger.LogInformation("Testing database connection...");
        var canConnect = dbContext.Database.CanConnect();
        logger.LogInformation($"Database connection: {(canConnect ? "SUCCESS" : "FAILED")}");
        
        if (canConnect)
        {
            // Apply migrations - THIS WILL ALSO APPLY SEED DATA from OnModelCreating
            logger.LogInformation("Applying database migrations...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully (including seed data from DbContext)");
            
            // Verify data exists
            var branchCount = await dbContext.Branches.CountAsync();
            var taskCount = await dbContext.TaskItems.CountAsync();
            var departmentCount = await dbContext.Departments.CountAsync();
            
            logger.LogInformation($"Database contains:");
            logger.LogInformation($"  - Departments: {departmentCount}");
            logger.LogInformation($"  - Branches: {branchCount}");
            logger.LogInformation($"  - Tasks: {taskCount}");
        }
        
        logger.LogInformation("=========================================");
        logger.LogInformation("Application is ready!");
        logger.LogInformation("=========================================");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database initialization.");
    }
}

app.Run();
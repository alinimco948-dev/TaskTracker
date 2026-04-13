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
    var connectionString = ConvertPostgresUriToConnectionString(rawConnectionString ?? string.Empty);
    
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
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IGradingService, GradingService>();

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

// Configure Kestrel for Railway - use PORT env variable
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.ConfigureKestrel(options =>
{
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

// Remove HTTPS redirection for Railway deployment - use HTTP only
// app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseSession();
app.UseResponseCaching();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/", () => Results.Redirect("/Home/Index"));

// Run migrations on startup in background - don't block startup
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(2000); // Wait for app to start
        
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("Testing database connection...");
        var canConnect = await dbContext.Database.CanConnectAsync();
        logger.LogInformation($"Database connection: {(canConnect ? "SUCCESS" : "FAILED")}");
        
        if (canConnect)
        {
            logger.LogInformation("Applying database migrations...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully");
        }
    }
    catch (Exception ex)
    {
        // Log but don't crash - migrations will run on first request
        var logger = app.Services.GetService<ILogger<Program>>();
        logger?.LogError(ex, "Background migration failed");
    }
});

app.Run();
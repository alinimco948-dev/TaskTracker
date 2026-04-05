using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaskTracker.Models.Entities;

namespace TaskTracker.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees { get; set; }
    public DbSet<Branch> Branches { get; set; }
    public DbSet<Department> Departments { get; set; }
    public DbSet<TaskItem> TaskItems { get; set; }
    public DbSet<DailyTask> DailyTasks { get; set; }
    public DbSet<TaskAssignment> TaskAssignments { get; set; }
    public DbSet<BranchAssignment> BranchAssignments { get; set; }
    public DbSet<Holiday> Holidays { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Report> Reports { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ========== UTC DATE CONVERTER - FIXES ALL DATE INCONSISTENCIES ==========
        // This ensures all DateTime properties are stored and retrieved as UTC
        var dateTimeConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(dateTimeConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableDateTimeConverter);
                }
            }
        }

        // ========== EMPLOYEE CONFIGURATION ==========
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.EmployeeId)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Email)
                .HasMaxLength(100);

            entity.Property(e => e.Phone)
                .HasMaxLength(20);

            entity.Property(e => e.Address)
                .HasMaxLength(200);

            entity.Property(e => e.Position)
                .HasMaxLength(50);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.EmployeeId).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique().HasFilter("\"Email\" IS NOT NULL");
            entity.HasIndex(e => e.IsActive);

            entity.HasOne(e => e.Department)
                .WithMany(d => d.Employees)
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Manager)
                .WithMany(e => e.Subordinates)
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ========== BRANCH CONFIGURATION ==========
        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Id).UseIdentityColumn();

            entity.Property(b => b.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(b => b.Code)
                .HasMaxLength(20);

            entity.Property(b => b.Address)
                .HasMaxLength(200);

            entity.Property(b => b.Phone)
                .HasMaxLength(20);

            entity.Property(b => b.Email)
                .HasMaxLength(100);

            entity.Property(b => b.Notes)
                .HasMaxLength(1000);

            entity.Property(b => b.HiddenTasksJson)
                .HasColumnName("HiddenTasksJson")
                .HasColumnType("jsonb");

            entity.Property(b => b.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(b => b.Name);
            entity.HasIndex(b => b.Code);
            entity.HasIndex(b => b.IsActive);

            entity.HasOne(b => b.Department)
                .WithMany(d => d.Branches)
                .HasForeignKey(b => b.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ========== DEPARTMENT CONFIGURATION ==========
        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).UseIdentityColumn();

            entity.Property(d => d.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(d => d.Code)
                .HasMaxLength(20);

            entity.Property(d => d.Description)
                .HasMaxLength(500);

            entity.Property(d => d.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(d => d.Name);
            entity.HasIndex(d => d.Code);
            entity.HasIndex(d => d.IsActive);
        });

        // ========== TASK ITEM CONFIGURATION ==========
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).UseIdentityColumn();

            entity.Property(t => t.Name)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(t => t.Description)
                .HasMaxLength(500);

            entity.Property(t => t.WeeklyDays)
                .HasMaxLength(50);

            entity.Property(t => t.MonthlyPattern)
                .HasMaxLength(50);

            entity.Property(t => t.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(t => t.Name).IsUnique();
            entity.HasIndex(t => t.DisplayOrder);
            entity.HasIndex(t => t.IsActive);
            entity.HasIndex(t => t.ExecutionType);
        });

        // ========== DAILY TASK CONFIGURATION ==========
        modelBuilder.Entity<DailyTask>(entity =>
        {
            entity.HasKey(dt => dt.Id);
            entity.Property(dt => dt.Id).UseIdentityColumn();

            entity.Property(dt => dt.AdjustmentReason)
                .HasMaxLength(500);

            entity.HasIndex(dt => new { dt.BranchId, dt.TaskItemId, dt.TaskDate }).IsUnique();
            entity.HasIndex(dt => dt.TaskDate);
            entity.HasIndex(dt => dt.IsCompleted);
            entity.HasIndex(dt => dt.BranchId);
            entity.HasIndex(dt => dt.TaskItemId);

            entity.HasOne(dt => dt.Branch)
                .WithMany(b => b.DailyTasks)
                .HasForeignKey(dt => dt.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(dt => dt.TaskItem)
                .WithMany(t => t.DailyTasks)
                .HasForeignKey(dt => dt.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(dt => dt.TaskAssignment)
                .WithOne(ta => ta.DailyTask)
                .HasForeignKey<TaskAssignment>(ta => ta.DailyTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ========== TASK ASSIGNMENT CONFIGURATION ==========
        modelBuilder.Entity<TaskAssignment>(entity =>
        {
            entity.HasKey(ta => ta.Id);
            entity.Property(ta => ta.Id).UseIdentityColumn();

            entity.Property(ta => ta.AssignedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(ta => new { ta.EmployeeId, ta.DailyTaskId }).IsUnique();
            entity.HasIndex(ta => ta.EmployeeId);
            entity.HasIndex(ta => ta.DailyTaskId);

            entity.HasOne(ta => ta.Employee)
                .WithMany(e => e.TaskAssignments)
                .HasForeignKey(ta => ta.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ta => ta.DailyTask)
                .WithOne(dt => dt.TaskAssignment)
                .HasForeignKey<TaskAssignment>(ta => ta.DailyTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ========== BRANCH ASSIGNMENT CONFIGURATION ==========
        modelBuilder.Entity<BranchAssignment>(entity =>
        {
            entity.HasKey(ba => ba.Id);
            entity.Property(ba => ba.Id).UseIdentityColumn();

            entity.HasIndex(ba => new { ba.EmployeeId, ba.BranchId, ba.StartDate }).IsUnique();
            entity.HasIndex(ba => ba.EmployeeId);
            entity.HasIndex(ba => ba.BranchId);
            entity.HasIndex(ba => ba.StartDate);
            entity.HasIndex(ba => ba.EndDate);

            entity.HasOne(ba => ba.Employee)
                .WithMany(e => e.BranchAssignments)
                .HasForeignKey(ba => ba.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ba => ba.Branch)
                .WithMany(b => b.BranchAssignments)
                .HasForeignKey(ba => ba.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ========== HOLIDAY CONFIGURATION ==========
        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.Id).UseIdentityColumn();

            entity.Property(h => h.Description)
                .HasMaxLength(200);

            entity.HasIndex(h => h.HolidayDate);
            entity.HasIndex(h => new { h.IsWeekly, h.WeekDay });
        });

        // ========== AUDIT LOG CONFIGURATION ==========
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).UseIdentityColumn();

            entity.Property(a => a.Action)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(a => a.EntityType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(a => a.Description)
                .HasMaxLength(1000);

            entity.Property(a => a.UserId)
                .HasMaxLength(100);

            entity.Property(a => a.UserName)
                .HasMaxLength(100);

            entity.Property(a => a.IpAddress)
                .HasMaxLength(50);

            entity.Property(a => a.Changes)
                .HasColumnType("jsonb");

            entity.Property(a => a.OldValues)
                .HasColumnType("jsonb");

            entity.Property(a => a.NewValues)
                .HasColumnType("jsonb");

            entity.Property(a => a.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(a => a.Timestamp);
            entity.HasIndex(a => a.Action);
            entity.HasIndex(a => a.EntityType);
            entity.HasIndex(a => a.EntityId);
            entity.HasIndex(a => a.UserName);
        });

        // ========== REPORT CONFIGURATION ==========
        modelBuilder.Entity<Report>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).UseIdentityColumn();

            entity.Property(r => r.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(r => r.Description)
                .HasMaxLength(500);

            entity.Property(r => r.ReportType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(r => r.Category)
                .HasMaxLength(50);

            entity.Property(r => r.CreatedBy)
                .HasMaxLength(100);

            entity.Property(r => r.Configuration)
                .HasColumnType("jsonb");

            entity.Property(r => r.Columns)
                .HasColumnType("jsonb");

            entity.Property(r => r.Filters)
                .HasColumnType("jsonb");

            entity.Property(r => r.SortBy)
                .HasMaxLength(100);

            entity.Property(r => r.ScheduleCron)
                .HasMaxLength(100);

            entity.Property(r => r.ExportFormat)
                .HasMaxLength(20);

            entity.Property(r => r.Recipients)
                .HasColumnType("jsonb");

            entity.Property(r => r.Tags)
                .HasColumnType("jsonb");

            entity.Property(r => r.LastError)
                .HasMaxLength(500);

            entity.Property(r => r.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(r => r.ReportType);
            entity.HasIndex(r => r.Category);
            entity.HasIndex(r => r.CreatedBy);
            entity.HasIndex(r => r.IsScheduled);
            entity.HasIndex(r => r.IsPublic);
            entity.HasIndex(r => r.IsActive);
        });

        // ========== SEED DATA ==========
        var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // ========== SEED DEPARTMENT ==========
        modelBuilder.Entity<Department>().HasData(
            new Department
            {
                Id = 1,
                Name = "Finance",
                Code = "FIN",
                Description = "Financial operations, cash management, and reconciliations",
                IsActive = true,
                CreatedAt = seedDate
            }
        );

        // ========== SEED BRANCHES ==========
        var branches = new[]
        {
            new { Id = 1, Name = "Ambassador", Code = "AMB-001" },
            new { Id = 2, Name = "Arabsiyo", Code = "ARB-002" },
            new { Id = 3, Name = "Aw-Aden", Code = "AWA-003" },
            new { Id = 4, Name = "Buurta-kala-jeexan", Code = "BKT-004" },
            new { Id = 5, Name = "DLD", Code = "DLD-005" },
            new { Id = 6, Name = "Dunbuluq", Code = "DUN-006" },
            new { Id = 7, Name = "Faarah-Nour", Code = "FAN-007" },
            new { Id = 8, Name = "Faluuja", Code = "FAL-008" },
            new { Id = 9, Name = "Gabiley", Code = "GAB-009" },
            new { Id = 10, Name = "Ganad", Code = "GAN-010" },
            new { Id = 11, Name = "GNT", Code = "GNT-011" },
            new { Id = 12, Name = "Haaruun", Code = "HAA-012" },
            new { Id = 13, Name = "Iftin", Code = "IFT-013" },
            new { Id = 14, Name = "Isha-borama", Code = "ISB-014" },
            new { Id = 15, Name = "Jig", Code = "JIG-015" },
            new { Id = 16, Name = "June", Code = "JUN-016" },
            new { Id = 17, Name = "Kililka", Code = "KIL-017" },
            new { Id = 18, Name = "Laanta-Hawada", Code = "LAH-018" },
            new { Id = 19, Name = "M.mooge", Code = "MMO-019" },
            new { Id = 20, Name = "Masala", Code = "MAS-020" },
            new { Id = 21, Name = "New-Hargeisa", Code = "NEH-021" },
            new { Id = 22, Name = "SQA", Code = "SQA-022" },
            new { Id = 23, Name = "Waheen", Code = "WAH-023" },
            new { Id = 24, Name = "Wajaale", Code = "WAJ-024" },
            new { Id = 25, Name = "XWR", Code = "XWR-025" }
        };

        foreach (var branch in branches)
        {
            modelBuilder.Entity<Branch>().HasData(new Branch
            {
                Id = branch.Id,
                Name = branch.Name,
                Code = branch.Code,
                DepartmentId = 1,
                Address = $"{branch.Name} Branch, Hargeisa, Somaliland",
                Phone = $"+252 63 000{branch.Id.ToString().PadLeft(3, '0')}",
                Email = $"{branch.Name.ToLower().Replace("-", "").Replace(" ", "")}@company.com",
                IsActive = true,
                CreatedAt = seedDate,
                HiddenTasksJson = "[]"
            });
        }

        // ========== SEED TASKS ==========
        modelBuilder.Entity<TaskItem>().HasData(
            new TaskItem
            {
                Id = 1,
                Name = "Coversheet",
                Deadline = new TimeSpan(21, 0, 0),
                IsSameDay = true,
                DisplayOrder = 1,
                Description = "Complete daily coversheet and verify all entries",
                IsActive = true,
                CreatedAt = seedDate,
                ExecutionType = TaskExecutionType.RecurringDaily
            },
            new TaskItem
            {
                Id = 2,
                Name = "Daily Scan",
                Deadline = new TimeSpan(10, 0, 0),
                IsSameDay = false,
                DisplayOrder = 2,
                Description = "Scan and upload daily documents to system",
                IsActive = true,
                CreatedAt = seedDate,
                ExecutionType = TaskExecutionType.RecurringDaily
            },
            new TaskItem
            {
                Id = 3,
                Name = "Meter Scan",
                Deadline = new TimeSpan(10, 0, 0),
                IsSameDay = false,
                DisplayOrder = 3,
                Description = "Scan meter readings and record in system",
                IsActive = true,
                CreatedAt = seedDate,
                ExecutionType = TaskExecutionType.RecurringDaily
            },
            new TaskItem
            {
                Id = 4,
                Name = "Meter Deposit",
                Deadline = new TimeSpan(21, 30, 0),
                IsSameDay = true,
                DisplayOrder = 4,
                Description = "Deposit meter collections to bank",
                IsActive = true,
                CreatedAt = seedDate,
                ExecutionType = TaskExecutionType.RecurringDaily
            },
            new TaskItem
            {
                Id = 5,
                Name = "Daily Deposit",
                Deadline = new TimeSpan(8, 0, 0),
                IsSameDay = false,
                DisplayOrder = 5,
                Description = "Make daily bank deposit and update records",
                IsActive = true,
                CreatedAt = seedDate,
                ExecutionType = TaskExecutionType.RecurringDaily
            }
        );
    }
}
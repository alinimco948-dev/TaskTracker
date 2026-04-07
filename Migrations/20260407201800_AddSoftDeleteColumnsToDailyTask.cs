using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaskTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteColumnsToDailyTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Changes = table.Column<string>(type: "jsonb", nullable: true),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HolidayDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsWeekly = table.Column<bool>(type: "boolean", nullable: false),
                    WeekDay = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReportType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Configuration = table.Column<string>(type: "jsonb", nullable: false),
                    Columns = table.Column<string>(type: "jsonb", nullable: false),
                    Filters = table.Column<string>(type: "jsonb", nullable: false),
                    SortBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsAscending = table.Column<bool>(type: "boolean", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsScheduled = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleCron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NextRunDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Recipients = table.Column<string>(type: "jsonb", nullable: true),
                    ExportFormat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IncludeCharts = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RunCount = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    Tags = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Deadline = table.Column<TimeSpan>(type: "interval", nullable: false),
                    IsSameDay = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutionType = table.Column<int>(type: "integer", nullable: false),
                    WeeklyDays = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MonthlyPattern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MaxOccurrences = table.Column<int>(type: "integer", nullable: true),
                    AvailableFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AvailableTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    HiddenTasksJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branches_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HireDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    ManagerId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Employees_Employees_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    TaskItemId = table.Column<int>(type: "integer", nullable: false),
                    TaskDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsBulkUpdated = table.Column<bool>(type: "boolean", nullable: false),
                    BulkUpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdjustmentMinutes = table.Column<int>(type: "integer", nullable: true),
                    AdjustmentReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyTasks_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyTasks_TaskItems_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchAssignments_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BranchAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DailyTaskId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAssignments_DailyTasks_DailyTaskId",
                        column: x => x.DailyTaskId,
                        principalTable: "DailyTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Departments",
                columns: new[] { "Id", "Code", "CreatedAt", "Description", "IsActive", "Name", "UpdatedAt" },
                values: new object[] { 1, "FIN", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Financial operations, cash management, and reconciliations", true, "Finance", null });

            migrationBuilder.InsertData(
                table: "TaskItems",
                columns: new[] { "Id", "AvailableFrom", "AvailableTo", "CreatedAt", "Deadline", "Description", "DisplayOrder", "DurationDays", "EndDate", "ExecutionType", "IsActive", "IsSameDay", "MaxOccurrences", "MonthlyPattern", "Name", "StartDate", "UpdatedAt", "WeeklyDays" },
                values: new object[,]
                {
                    { 1, null, null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(0, 21, 0, 0, 0), "Complete daily coversheet and verify all entries", 1, null, null, 0, true, true, null, null, "Coversheet", null, null, null },
                    { 2, null, null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(0, 10, 0, 0, 0), "Scan and upload daily documents to system", 2, null, null, 0, true, false, null, null, "Daily Scan", null, null, null },
                    { 3, null, null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(0, 10, 0, 0, 0), "Scan meter readings and record in system", 3, null, null, 0, true, false, null, null, "Meter Scan", null, null, null },
                    { 4, null, null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(0, 21, 30, 0, 0), "Deposit meter collections to bank", 4, null, null, 0, true, true, null, null, "Meter Deposit", null, null, null },
                    { 5, null, null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new TimeSpan(0, 8, 0, 0, 0), "Make daily bank deposit and update records", 5, null, null, 0, true, false, null, null, "Daily Deposit", null, null, null }
                });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Address", "Code", "CreatedAt", "DepartmentId", "Email", "HiddenTasksJson", "IsActive", "Name", "Notes", "Phone", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "Ambassador Branch, Hargeisa, Somaliland", "AMB-001", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "ambassador@company.com", "[]", true, "Ambassador", "", "+252 63 000001", null },
                    { 2, "Arabsiyo Branch, Hargeisa, Somaliland", "ARB-002", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "arabsiyo@company.com", "[]", true, "Arabsiyo", "", "+252 63 000002", null },
                    { 3, "Aw-Aden Branch, Hargeisa, Somaliland", "AWA-003", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "awaden@company.com", "[]", true, "Aw-Aden", "", "+252 63 000003", null },
                    { 4, "Buurta-kala-jeexan Branch, Hargeisa, Somaliland", "BKT-004", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "buurtakalajeexan@company.com", "[]", true, "Buurta-kala-jeexan", "", "+252 63 000004", null },
                    { 5, "DLD Branch, Hargeisa, Somaliland", "DLD-005", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "dld@company.com", "[]", true, "DLD", "", "+252 63 000005", null },
                    { 6, "Dunbuluq Branch, Hargeisa, Somaliland", "DUN-006", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "dunbuluq@company.com", "[]", true, "Dunbuluq", "", "+252 63 000006", null },
                    { 7, "Faarah-Nour Branch, Hargeisa, Somaliland", "FAN-007", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "faarahnour@company.com", "[]", true, "Faarah-Nour", "", "+252 63 000007", null },
                    { 8, "Faluuja Branch, Hargeisa, Somaliland", "FAL-008", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "faluuja@company.com", "[]", true, "Faluuja", "", "+252 63 000008", null },
                    { 9, "Gabiley Branch, Hargeisa, Somaliland", "GAB-009", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "gabiley@company.com", "[]", true, "Gabiley", "", "+252 63 000009", null },
                    { 10, "Ganad Branch, Hargeisa, Somaliland", "GAN-010", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "ganad@company.com", "[]", true, "Ganad", "", "+252 63 000010", null },
                    { 11, "GNT Branch, Hargeisa, Somaliland", "GNT-011", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "gnt@company.com", "[]", true, "GNT", "", "+252 63 000011", null },
                    { 12, "Haaruun Branch, Hargeisa, Somaliland", "HAA-012", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "haaruun@company.com", "[]", true, "Haaruun", "", "+252 63 000012", null },
                    { 13, "Iftin Branch, Hargeisa, Somaliland", "IFT-013", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "iftin@company.com", "[]", true, "Iftin", "", "+252 63 000013", null },
                    { 14, "Isha-borama Branch, Hargeisa, Somaliland", "ISB-014", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "ishaborama@company.com", "[]", true, "Isha-borama", "", "+252 63 000014", null },
                    { 15, "Jig Branch, Hargeisa, Somaliland", "JIG-015", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "jig@company.com", "[]", true, "Jig", "", "+252 63 000015", null },
                    { 16, "June Branch, Hargeisa, Somaliland", "JUN-016", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "june@company.com", "[]", true, "June", "", "+252 63 000016", null },
                    { 17, "Kililka Branch, Hargeisa, Somaliland", "KIL-017", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "kililka@company.com", "[]", true, "Kililka", "", "+252 63 000017", null },
                    { 18, "Laanta-Hawada Branch, Hargeisa, Somaliland", "LAH-018", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "laantahawada@company.com", "[]", true, "Laanta-Hawada", "", "+252 63 000018", null },
                    { 19, "M.mooge Branch, Hargeisa, Somaliland", "MMO-019", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "m.mooge@company.com", "[]", true, "M.mooge", "", "+252 63 000019", null },
                    { 20, "Masala Branch, Hargeisa, Somaliland", "MAS-020", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "masala@company.com", "[]", true, "Masala", "", "+252 63 000020", null },
                    { 21, "New-Hargeisa Branch, Hargeisa, Somaliland", "NEH-021", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "newhargeisa@company.com", "[]", true, "New-Hargeisa", "", "+252 63 000021", null },
                    { 22, "SQA Branch, Hargeisa, Somaliland", "SQA-022", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "sqa@company.com", "[]", true, "SQA", "", "+252 63 000022", null },
                    { 23, "Waheen Branch, Hargeisa, Somaliland", "WAH-023", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "waheen@company.com", "[]", true, "Waheen", "", "+252 63 000023", null },
                    { 24, "Wajaale Branch, Hargeisa, Somaliland", "WAJ-024", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "wajaale@company.com", "[]", true, "Wajaale", "", "+252 63 000024", null },
                    { 25, "XWR Branch, Hargeisa, Somaliland", "XWR-025", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, "xwr@company.com", "[]", true, "XWR", "", "+252 63 000025", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Changes_GIN",
                table: "AuditLogs",
                column: "Changes")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityId",
                table: "AuditLogs",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType",
                table: "AuditLogs",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_NewValues_GIN",
                table: "AuditLogs",
                column: "NewValues")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OldValues_GIN",
                table: "AuditLogs",
                column: "OldValues")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp_Action",
                table: "AuditLogs",
                columns: new[] { "Timestamp", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp_EntityType",
                table: "AuditLogs",
                columns: new[] { "Timestamp", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserName",
                table: "AuditLogs",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserName_Timestamp",
                table: "AuditLogs",
                columns: new[] { "UserName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_BranchId",
                table: "BranchAssignments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_BranchId_EmployeeId_EndDate",
                table: "BranchAssignments",
                columns: new[] { "BranchId", "EmployeeId", "EndDate" },
                filter: "\"EndDate\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_BranchId_EndDate",
                table: "BranchAssignments",
                columns: new[] { "BranchId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_BranchId_StartDate_EndDate",
                table: "BranchAssignments",
                columns: new[] { "BranchId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_EmployeeId",
                table: "BranchAssignments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_EmployeeId_BranchId_StartDate",
                table: "BranchAssignments",
                columns: new[] { "EmployeeId", "BranchId", "StartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_EmployeeId_EndDate",
                table: "BranchAssignments",
                columns: new[] { "EmployeeId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_EndDate",
                table: "BranchAssignments",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_BranchAssignments_StartDate",
                table: "BranchAssignments",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_DepartmentId",
                table: "Branches",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_HiddenTasksJson_GIN",
                table: "Branches",
                column: "HiddenTasksJson")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_IsActive",
                table: "Branches",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_IsActive_DepartmentId",
                table: "Branches",
                columns: new[] { "IsActive", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Name",
                table: "Branches",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_BranchId",
                table: "DailyTasks",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_BranchId_IsCompleted",
                table: "DailyTasks",
                columns: new[] { "BranchId", "IsCompleted" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_BranchId_TaskDate",
                table: "DailyTasks",
                columns: new[] { "BranchId", "TaskDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_BranchId_TaskDate_IsCompleted",
                table: "DailyTasks",
                columns: new[] { "BranchId", "TaskDate", "IsCompleted" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_BranchId_TaskItemId_TaskDate",
                table: "DailyTasks",
                columns: new[] { "BranchId", "TaskItemId", "TaskDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_IsCompleted",
                table: "DailyTasks",
                column: "IsCompleted");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_TaskDate",
                table: "DailyTasks",
                column: "TaskDate");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_TaskDate_IsCompleted",
                table: "DailyTasks",
                columns: new[] { "TaskDate", "IsCompleted" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_TaskDate_IsCompleted_BranchId",
                table: "DailyTasks",
                columns: new[] { "TaskDate", "IsCompleted", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_TaskItemId",
                table: "DailyTasks",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_IsActive",
                table: "Departments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Email",
                table: "Employees",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_EmployeeId",
                table: "Employees",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_IsActive",
                table: "Employees",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_IsActive_DepartmentId",
                table: "Employees",
                columns: new[] { "IsActive", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ManagerId",
                table: "Employees",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_HolidayDate",
                table: "Holidays",
                column: "HolidayDate");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_IsWeekly_HolidayDate",
                table: "Holidays",
                columns: new[] { "IsWeekly", "HolidayDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_IsWeekly_WeekDay",
                table: "Holidays",
                columns: new[] { "IsWeekly", "WeekDay" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Category",
                table: "Reports",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Category_IsPublic",
                table: "Reports",
                columns: new[] { "Category", "IsPublic" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Configuration_GIN",
                table: "Reports",
                column: "Configuration")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_CreatedBy",
                table: "Reports",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_IsActive",
                table: "Reports",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_IsPublic",
                table: "Reports",
                column: "IsPublic");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_IsScheduled",
                table: "Reports",
                column: "IsScheduled");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_IsScheduled_NextRunDate",
                table: "Reports",
                columns: new[] { "IsScheduled", "NextRunDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReportType",
                table: "Reports",
                column: "ReportType");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReportType_IsActive",
                table: "Reports",
                columns: new[] { "ReportType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Tags_GIN",
                table: "Reports",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_DailyTaskId",
                table: "TaskAssignments",
                column: "DailyTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_EmployeeId",
                table: "TaskAssignments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_EmployeeId_AssignedAt",
                table: "TaskAssignments",
                columns: new[] { "EmployeeId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_EmployeeId_DailyTaskId",
                table: "TaskAssignments",
                columns: new[] { "EmployeeId", "DailyTaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_DisplayOrder",
                table: "TaskItems",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ExecutionType",
                table: "TaskItems",
                column: "ExecutionType");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_IsActive",
                table: "TaskItems",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_IsActive_ExecutionType",
                table: "TaskItems",
                columns: new[] { "IsActive", "ExecutionType" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_Name",
                table: "TaskItems",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_StartDate_EndDate",
                table: "TaskItems",
                columns: new[] { "StartDate", "EndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BranchAssignments");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "TaskAssignments");

            migrationBuilder.DropTable(
                name: "DailyTasks");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "TaskItems");

            migrationBuilder.DropTable(
                name: "Departments");
        }
    }
}

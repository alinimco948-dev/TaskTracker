using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace TaskTracker.Models.Entities;

[Table("Reports")]
public class Report
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string ReportType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    public string Configuration { get; set; } = "{}";

    public string Columns { get; set; } = "[]";

    public string Filters { get; set; } = "{}";

    [MaxLength(100)]
    public string? SortBy { get; set; }

    public bool IsAscending { get; set; } = true;

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public bool IsScheduled { get; set; }

    [MaxLength(100)]
    public string? ScheduleCron { get; set; }

    public DateTime? NextRunDate { get; set; }

    public string? Recipients { get; set; }

    public string ExportFormat { get; set; } = "excel";

    public bool IncludeCharts { get; set; } = true;

    [MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }  // Add this missing property

    public DateTime? LastRunAt { get; set; }

    public int? RunCount { get; set; }

    public string? LastError { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsPublic { get; set; }

    public string? Tags { get; set; }

    [NotMapped]
    public List<string> TagsList
    {
        get => string.IsNullOrEmpty(Tags)
            ? new List<string>()
            : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
        set => Tags = value is not null ? string.Join(",", value) : null;
    }

    [NotMapped]
    public List<string> RecipientsList
    {
        get => string.IsNullOrEmpty(Recipients)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(Recipients) ?? new List<string>();
        set => Recipients = JsonSerializer.Serialize(value);
    }
}
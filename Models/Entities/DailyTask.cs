using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models.Entities;

[Table("DailyTasks")]
public class DailyTask
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public int BranchId { get; set; }

    [Required]
    public int TaskItemId { get; set; }

    [Required]
    public DateTime TaskDate { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool IsBulkUpdated { get; set; }

    public DateTime? BulkUpdateTime { get; set; }

    public int? AdjustmentMinutes { get; set; }

    [MaxLength(500)]
    public string AdjustmentReason { get; set; } = string.Empty;

    // ========== SOFT DELETE PROPERTIES ==========
    public bool IsDeleted { get; set; } = false;
    
    public DateTime? DeletedAt { get; set; }
    
    [MaxLength(500)]
    public string? DeletionReason { get; set; }

    // ========== NAVIGATION PROPERTIES ==========
    [ForeignKey(nameof(BranchId))]
    public virtual Branch? Branch { get; set; }

    [ForeignKey(nameof(TaskItemId))]
    public virtual TaskItem? TaskItem { get; set; }

    // One-to-one relationship with TaskAssignment
    public virtual TaskAssignment? TaskAssignment { get; set; }
}
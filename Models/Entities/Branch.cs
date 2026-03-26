using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace TaskTracker.Models.Entities;

[Table("Branches")]
public class Branch
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    [MaxLength(1000)]
    public string Notes { get; set; } = string.Empty;

    // JSON field for storing hidden tasks - PostgreSQL JSONB type
    [Column(TypeName = "jsonb")]
    public string HiddenTasksJson { get; set; } = "[]";

    // Private field for cached hidden tasks
    private List<string>? _hiddenTasks;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(DepartmentId))]
    public virtual Department? Department { get; set; }

    public virtual ICollection<BranchAssignment> BranchAssignments { get; set; } = new HashSet<BranchAssignment>();

    public virtual ICollection<DailyTask> DailyTasks { get; set; } = new HashSet<DailyTask>();

    // Computed property for hidden tasks with caching and thread safety
    [NotMapped]
    public List<string> HiddenTasks
    {
        get
        {
            if (_hiddenTasks == null)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(HiddenTasksJson))
                    {
                        _hiddenTasks = new List<string>();
                    }
                    else
                    {
                        _hiddenTasks = JsonSerializer.Deserialize<List<string>>(HiddenTasksJson, _jsonOptions)
                                       ?? new List<string>();
                    }
                }
                catch (JsonException ex)
                {
                    // Log error if you have logging available
                    System.Diagnostics.Debug.WriteLine($"JSON deserialization error for branch {Id}: {ex.Message}");
                    _hiddenTasks = new List<string>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Unexpected error deserializing hidden tasks: {ex.Message}");
                    _hiddenTasks = new List<string>();
                }
            }
            return _hiddenTasks;
        }
        set
        {
            _hiddenTasks = value ?? new List<string>();
            try
            {
                HiddenTasksJson = JsonSerializer.Serialize(_hiddenTasks, _jsonOptions);
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON serialization error: {ex.Message}");
                HiddenTasksJson = "[]";
            }
        }
    }

    // Helper methods for task visibility management
    public bool IsTaskHidden(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName)) return false;
        return HiddenTasks.Contains(taskName);
    }

    public void HideTask(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName)) return;

        var hidden = HiddenTasks;
        if (!hidden.Contains(taskName))
        {
            hidden.Add(taskName);
            HiddenTasks = hidden;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void ShowTask(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName)) return;

        var hidden = HiddenTasks;
        if (hidden.Contains(taskName))
        {
            hidden.Remove(taskName);
            HiddenTasks = hidden;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public List<string> GetVisibleTasks(List<string> allTaskNames)
    {
        if (allTaskNames == null || allTaskNames.Count == 0)
            return new List<string>();

        var hidden = HiddenTasks;
        var visible = new List<string>();

        foreach (var taskName in allTaskNames)
        {
            if (!hidden.Contains(taskName))
            {
                visible.Add(taskName);
            }
        }

        return visible;
    }

    public void ShowAllTasks()
    {
        HiddenTasks = new List<string>();
        UpdatedAt = DateTime.UtcNow;
    }

    public void HideAllTasks(List<string> allTaskNames)
    {
        if (allTaskNames == null || allTaskNames.Count == 0) return;

        HiddenTasks = new List<string>(allTaskNames);
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateTaskVisibility(List<string> visibleTasks, List<string> allTaskNames)
    {
        if (visibleTasks == null || allTaskNames == null) return;

        var hiddenTasks = new List<string>();
        foreach (var taskName in allTaskNames)
        {
            if (!visibleTasks.Contains(taskName))
            {
                hiddenTasks.Add(taskName);
            }
        }

        HiddenTasks = hiddenTasks;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateTaskVisibilityFromCheckboxes(Dictionary<string, bool> visibilityStates)
    {
        if (visibilityStates == null || visibilityStates.Count == 0) return;

        var hiddenTasks = new List<string>();
        foreach (var kvp in visibilityStates)
        {
            if (!kvp.Value) // If not visible, add to hidden
            {
                hiddenTasks.Add(kvp.Key);
            }
        }

        HiddenTasks = hiddenTasks;
        UpdatedAt = DateTime.UtcNow;
    }

    public Dictionary<string, bool> GetVisibilityStates(List<string> allTaskNames)
    {
        var states = new Dictionary<string, bool>();
        var hidden = HiddenTasks;

        foreach (var taskName in allTaskNames)
        {
            states[taskName] = !hidden.Contains(taskName);
        }

        return states;
    }

    // Helper method to clear cached hidden tasks (useful after updates)
    public void ClearHiddenTasksCache()
    {
        _hiddenTasks = null;
    }

    // For debugging purposes
    public override string ToString()
    {
        return $"Branch: {Name} (ID: {Id}, Active: {IsActive}, HiddenTasks Count: {HiddenTasks.Count})";
    }
}
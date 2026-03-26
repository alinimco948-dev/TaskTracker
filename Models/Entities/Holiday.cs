using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models.Entities;

[Table("Holidays")]
public class Holiday
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public DateTime HolidayDate { get; set; }

    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public bool IsWeekly { get; set; }

    public int? WeekDay { get; set; } // 0-6 (Sunday=0, Monday=1, etc.)
}

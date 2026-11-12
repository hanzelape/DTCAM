using System.ComponentModel.DataAnnotations;
using FaceAttendanceSystem.Models;

namespace FaceAttendanceSystem.ViewModels;

public class ScheduleCreateViewModel
{
    public int ScheduleId { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required(ErrorMessage = "Start date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Date")]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Start time is required.")]
    [DataType(DataType.Time)]
    [Display(Name = "Start Time")]
    public TimeSpan StartTime { get; set; }

    [Required(ErrorMessage = "End time is required.")]
    [DataType(DataType.Time)]
    [Display(Name = "End Time")]
    public TimeSpan EndTime { get; set; }

    public string? Location { get; set; }

    [Display(Name = "Late Grace Period (minutes)")]
    [Range(0, int.MaxValue, ErrorMessage = "Grace period must be 0 or more minutes.")]
    public int LateGracePeriodMinutes { get; set; } = 15;

    [Display(Name = "Repeat")]
    public ScheduleRepeatType RepeatType { get; set; } = ScheduleRepeatType.None;

    /// <summary>
    /// Checked repeat weekdays (0=Sunday .. 6=Saturday).
    /// </summary>
    [Display(Name = "Repeat Days")]
    public List<int> SelectedDays { get; set; } = new();

    [Display(Name = "All Employees")]
    public bool AssignAll { get; set; }

    public List<int> SelectedDepartmentIds { get; set; } = new();

    public List<int> SelectedEmployeeIds { get; set; } = new();
}


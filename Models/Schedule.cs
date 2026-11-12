namespace FaceAttendanceSystem.Models;

public enum ScheduleRepeatType
{
    None = 0,
    Weekly = 1,
    BiWeekly = 2
}

public class Schedule
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime Date { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public string? Location { get; set; }

    public ScheduleRepeatType RepeatType { get; set; } = ScheduleRepeatType.None;

    /// <summary>
    /// Comma-separated list of repeat weekdays (0=Sunday .. 6=Saturday).
    /// Example: "1,3" means Monday and Wednesday.
    /// </summary>
    public string RepeatDays { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Number of minutes after the scheduled start time during which a check-in
    /// is still considered "On Time" instead of "Late".
    /// </summary>
    public int LateGracePeriodMinutes { get; set; } = 15;

    public List<ScheduleEmployeeAssignment> EmployeeAssignments { get; set; } = new();

    // Computed helper for attendance status logic.
    public DateTime StartDateTime => Date.Date + StartTime;
}

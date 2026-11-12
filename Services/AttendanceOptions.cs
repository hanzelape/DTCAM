namespace FaceAttendanceSystem.Services;

/// <summary>
/// Configuration options for attendance status logic.
/// </summary>
public class AttendanceOptions
{
    public const string SectionName = "Attendance";

    /// <summary>
    /// Number of minutes after the scheduled start time during which a check-in
    /// is still considered "On Time" instead of "Late".
    /// </summary>
    public int LateGracePeriodMinutes { get; set; } = 15;
}

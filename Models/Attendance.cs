namespace FaceAttendanceSystem.Models;

public class Attendance
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public Employee? Employee { get; set; }

    public DateTime TimeRecorded { get; set; } = DateTime.Now;

    public string Status { get; set; } = string.Empty;

    public decimal ConfidenceScore { get; set; }

    public string? Subject { get; set; }
    public int ScheduleId { get; set; }

    public Schedule? Schedule { get; set; }

    // Audit trail for the check-in. Null for manual admin check-ins.
    public double? CheckInLatitude { get; set; }

    public double? CheckInLongitude { get; set; }

    public double? CheckInAccuracyMeters { get; set; }

    public int? CheckInLocationId { get; set; }

    public double? CheckInDistanceMeters { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? CheckInIpAddress { get; set; }

    public bool LivenessPassed { get; set; }
}

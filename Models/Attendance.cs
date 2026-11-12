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
}

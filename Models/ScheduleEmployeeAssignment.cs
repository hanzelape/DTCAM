namespace FaceAttendanceSystem.Models;

public class ScheduleEmployeeAssignment
{
    public int Id { get; set; }

    public int ScheduleId { get; set; }

    public Schedule Schedule { get; set; } = null!;

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;
}

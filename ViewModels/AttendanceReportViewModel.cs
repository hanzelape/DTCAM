using FaceAttendanceSystem.Models;

namespace FaceAttendanceSystem.ViewModels;

public class AttendanceReportViewModel
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? EmployeeId { get; set; }
    public int? ScheduleId { get; set; }
    public string? Status { get; set; }

    public int TotalPresent { get; set; }
    public int TotalEarly { get; set; }
    public int TotalOnTime { get; set; }
    public int TotalLate { get; set; }
    public int ExpectedCount { get; set; }
    public int AbsentCount { get; set; }
    public decimal AttendanceRate { get; set; }
    public decimal AbsenteeismRate { get; set; }
    public decimal LateRate { get; set; }

    public List<Attendance> Records { get; set; } = new();
}

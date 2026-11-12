using System.ComponentModel.DataAnnotations.Schema;

namespace FaceAttendanceSystem.Models;

[Table("Employees")]
public class Employee
{
    public int Id { get; set; }

    public string EmployeeId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Department { get; set; }

    public string? PhotoPath { get; set; }

    public string? FaceEncoding { get; set; }

    public string? FaceEncodings { get; set; }

    public DateTime RegisteredAt { get; set; } = DateTime.Now;

    public bool IsActive { get; set; } = true;

    public decimal AttendanceRate { get; set; } = 100m;

    public int TotalLogIns { get; set; } = 0;

    public int TotalAbsents { get; set; } = 0;
    public int TotalLates { get; set; } = 0;

    public string? Email { get; set; }

    public string? PasswordHash { get; set; }
}

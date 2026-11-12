using System.ComponentModel.DataAnnotations;

namespace FaceAttendanceSystem.ViewModels;

public class EmployeeRegistrationViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Employee ID")]
    public string EmployeeId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Department")]
    public string? Department { get; set; } = "DTCAM";

    [Display(Name = "Captured Photo")]
    public string? CapturedPhoto { get; set; }

    [Display(Name = "Captured Photos")]
    public List<string> CapturedPhotos { get; set; } = new();

    [Display(Name = "Attendance Rate")]
    public decimal AttendanceRate { get; set; }

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "New Password (leave blank to keep current)")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
    public string? NewPassword { get; set; }
}

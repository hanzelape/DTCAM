namespace FaceAttendanceSystem.ViewModels;

public class CheckInViewModel
{
    public string? CapturedImage { get; set; }

    public bool IsRecognized { get; set; }
    public string? EmployeeName { get; set; }
    public double Confidence { get; set; }
    public string? Message { get; set; }
    public string? Status { get; set; }
}

namespace FaceAttendanceSystem.Models;

public class RecognitionResult
{
    public bool IsRecognized { get; set; }

    public int? EmployeeId { get; set; }

    public string? EmployeeName { get; set; }

    public double Confidence { get; set; }

    public string? ErrorMessage { get; set; }

    public static RecognitionResult Failed(string message) =>
        new() { IsRecognized = false, ErrorMessage = message };

    public static RecognitionResult NotImplemented() =>
        new() { IsRecognized = false, ErrorMessage = "Face recognition service is not configured." };
}

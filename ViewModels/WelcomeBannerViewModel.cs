namespace FaceAttendanceSystem.ViewModels
{
    /// <summary>
    /// Presentation data for the dashboard welcome banner. Carries only text that
    /// already exists in the current session — no values are invented here.
    /// </summary>
    public class WelcomeBannerViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

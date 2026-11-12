namespace FaceAttendanceSystem.Models;

public class AdminSession
{
    public int Id { get; set; }

    public int AdminId { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? ExpiresAt { get; set; }

    public bool IsRevoked { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Admin? Admin { get; set; }
}

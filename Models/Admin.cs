namespace FaceAttendanceSystem.Models;

public class Admin : ILockableAccount
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// When true, every admin page is blocked until the password is changed.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTime? PasswordChangedAtUtc { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTime? LockoutEndUtc { get; set; }
}

namespace FaceAttendanceSystem.Models;

/// <summary>
/// Shared lockout state for accounts that sign in with a password.
/// </summary>
public interface ILockableAccount
{
    int FailedLoginCount { get; set; }

    DateTime? LockoutEndUtc { get; set; }
}

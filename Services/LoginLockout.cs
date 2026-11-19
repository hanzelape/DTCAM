using FaceAttendanceSystem.Models;

namespace FaceAttendanceSystem.Services;

/// <summary>
/// Account lockout: after <see cref="MaxFailedAttempts"/> wrong passwords the
/// account is locked for <see cref="LockoutDuration"/>. Used by both admin and
/// employee logins. Callers persist the entity after calling these methods.
/// </summary>
public static class LoginLockout
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public const string LockedMessage =
        "This account is temporarily locked after too many failed attempts. Try again in 15 minutes.";

    public static bool IsLockedOut(ILockableAccount account) =>
        account.LockoutEndUtc.HasValue && account.LockoutEndUtc.Value > DateTime.UtcNow;

    /// <summary>Records a failed attempt. Returns true if this attempt locked the account.</summary>
    public static bool RegisterFailure(ILockableAccount account)
    {
        // An expired lockout starts a fresh count.
        if (account.LockoutEndUtc.HasValue && account.LockoutEndUtc.Value <= DateTime.UtcNow)
        {
            account.LockoutEndUtc = null;
            account.FailedLoginCount = 0;
        }

        account.FailedLoginCount++;
        if (account.FailedLoginCount >= MaxFailedAttempts)
        {
            account.LockoutEndUtc = DateTime.UtcNow.Add(LockoutDuration);
            account.FailedLoginCount = 0;
            return true;
        }

        return false;
    }

    public static void RegisterSuccess(ILockableAccount account)
    {
        account.FailedLoginCount = 0;
        account.LockoutEndUtc = null;
    }
}

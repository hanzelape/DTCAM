namespace FaceAttendanceSystem.Services;

public static class RateLimitPolicies
{
    /// <summary>10 requests / minute / IP on login POSTs.</summary>
    public const string Login = "login";

    /// <summary>30 requests / minute / IP on check-in (challenge + recognize).</summary>
    public const string CheckIn = "checkin";
}

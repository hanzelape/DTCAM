using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FaceAttendanceSystem.Services;

/// <summary>
/// Issues and consumes single-use head-turn challenges, stored server-side in
/// the user's session. The server picks the direction, so a photo never passes
/// and a pre-recorded clip turning one way fails half the time.
/// </summary>
public static class LivenessChallengeStore
{
    private const string SessionKey = "LivenessChallenge";
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public static LivenessChallenge Issue(ISession session)
    {
        var challenge = new LivenessChallenge(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            RandomNumberGenerator.GetInt32(2) == 0 ? "left" : "right",
            DateTime.UtcNow.Add(Lifetime));

        session.SetString(SessionKey, JsonSerializer.Serialize(challenge));
        return challenge;
    }

    /// <summary>
    /// Removes the stored challenge (it works once, pass or fail) and returns
    /// it only if the id matches and it has not expired.
    /// </summary>
    public static LivenessChallenge? Consume(ISession session, string? challengeId)
    {
        var json = session.GetString(SessionKey);
        session.Remove(SessionKey);

        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(challengeId))
            return null;

        LivenessChallenge? challenge;
        try
        {
            challenge = JsonSerializer.Deserialize<LivenessChallenge>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (challenge == null || challenge.ExpiresUtc < DateTime.UtcNow)
            return null;

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(challenge.Id),
                Encoding.ASCII.GetBytes(challengeId)))
            return null;

        return challenge;
    }
}

public sealed record LivenessChallenge(string Id, string Direction, DateTime ExpiresUtc);

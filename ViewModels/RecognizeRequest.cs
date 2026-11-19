namespace FaceAttendanceSystem.ViewModels;

/// <summary>
/// Body of a face check-in (employee self check-in and admin kiosk).
/// </summary>
public class RecognizeRequest
{
    public const int MinFrames = 4;
    public const int MaxFrames = 12;
    public const int MaxFrameBytes = 1024 * 1024;

    /// <summary>Ordered JPEG data URLs: frontal frames first, then the head turn.</summary>
    public List<string>? Frames { get; set; }

    /// <summary>Id returned by the LivenessChallenge endpoint.</summary>
    public string? ChallengeId { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Accuracy { get; set; }

    public bool TryDecodeFrames(out List<byte[]> frames, out string? error)
    {
        frames = new List<byte[]>();
        error = null;

        if (Frames == null || Frames.Count < MinFrames)
        {
            error = "Not enough camera frames were captured. Please try again.";
            return false;
        }

        if (Frames.Count > MaxFrames)
        {
            error = "Too many camera frames.";
            return false;
        }

        foreach (var dataUrl in Frames)
        {
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                error = "Invalid image data.";
                return false;
            }

            var comma = dataUrl.IndexOf(',');
            var base64 = comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl;

            // Reject before allocating: base64 is ~4/3 of the decoded size.
            if (base64.Length > MaxFrameBytes / 3 * 4 + 4)
            {
                error = "Image too large.";
                return false;
            }

            var buffer = new byte[base64.Length * 3 / 4 + 3];
            if (!Convert.TryFromBase64String(base64, buffer, out var written))
            {
                error = "Invalid image data.";
                return false;
            }

            frames.Add(buffer[..written]);
        }

        return true;
    }
}

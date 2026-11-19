using FaceAttendanceSystem.Models;

namespace FaceAttendanceSystem.Services;

/// <summary>
/// Server-side, fail-closed geofence check for check-ins.
///
/// A browser cannot prove its GPS reading is genuine (location can be spoofed
/// with dev tools or mock-location apps). What this does is make location
/// mandatory, validate it on the server, and log it on the attendance record
/// so suspicious check-ins can be audited.
/// </summary>
public class GeofenceService
{
    public const double MaxAccuracyMeters = 100;

    public GeofenceResult Evaluate(double? latitude, double? longitude, double? accuracy, IReadOnlyList<Location> activeLocations)
    {
        if (!latitude.HasValue || !longitude.HasValue || !accuracy.HasValue)
            return GeofenceResult.Reject("Location is required to check in. Allow location access and try again.");

        var lat = latitude.Value;
        var lon = longitude.Value;
        var acc = accuracy.Value;

        if (!double.IsFinite(lat) || !double.IsFinite(lon) || !double.IsFinite(acc) ||
            lat < -90 || lat > 90 || lon < -180 || lon > 180 || acc <= 0 ||
            (lat == 0 && lon == 0))
            return GeofenceResult.Reject("The location reported by your device is invalid.");

        if (acc > MaxAccuracyMeters)
            return GeofenceResult.Reject(
                $"Your location is too imprecise ({acc:F0} m, limit {MaxAccuracyMeters:F0} m). Turn on precise location / GPS and try again.");

        if (activeLocations.Count == 0)
            return GeofenceResult.Reject("No check-in locations are configured. Ask an administrator to add one.");

        // Pick the location we are deepest inside (or closest to the edge of).
        var nearest = activeLocations
            .Select(l => new { Location = l, Distance = CalculateDistance(lat, lon, l.Latitude, l.Longitude) })
            .OrderBy(x => x.Distance - x.Location.RadiusMeters)
            .First();

        if (nearest.Distance > nearest.Location.RadiusMeters)
        {
            return GeofenceResult.Reject(
                $"You are outside the allowed check-in area ({nearest.Distance:F0} m from {nearest.Location.Name}, limit {nearest.Location.RadiusMeters} m).",
                nearest.Location.Id,
                nearest.Distance);
        }

        return new GeofenceResult(true, null, nearest.Location.Id, nearest.Distance);
    }

    public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double deg) => deg * Math.PI / 180.0;
}

public sealed record GeofenceResult(bool Allowed, string? Message, int? LocationId, double? DistanceMeters)
{
    public static GeofenceResult Reject(string message, int? locationId = null, double? distance = null) =>
        new(false, message, locationId, distance);
}

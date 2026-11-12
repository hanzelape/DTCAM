using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FaceAttendanceSystem.Models;

[Table("EmployeeDeletionAudits")]
public class EmployeeDeletionAudit
{
    public int Id { get; set; }

    public int AdminId { get; set; }

    public string AdminUsername { get; set; } = string.Empty;

    /// <summary>
    /// The deleted employee's identifier (EmployeeId). Kept minimally for audit
    /// traceability. No name, contact details, credentials, or biometric data
    /// is retained in this record.
    /// </summary>
    public string? DeletedEmployeeIdentifier { get; set; }

    public string? Reason { get; set; }

    public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
}

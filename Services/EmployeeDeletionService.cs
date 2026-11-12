using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Services;

/// <summary>
/// Permanently deletes an employee and all employee-owned data.
///
/// Cleanup sequence (designed so a failed external cleanup never silently
/// converts into a claimed full deletion):
///   1. Validate the employee exists.
///   2. Delete the profile image from the filesystem (outside the transaction).
///      If this fails the operation aborts before any database change.
///   3. Inside an EF Core transaction: delete attendance records,
///      schedule assignments, the audit record, and finally the employee row.
///   4. Face-recognition enrollment is stored only in this database
///      (Employee.FaceEncoding / FaceEncodings). The Python API rebuilds its
///      known-face list per request from current employees, so removing the
///      employee row removes the enrollment; no separate server-side index
///      exists to purge.
///
/// Database transactions cannot roll back filesystem changes, so filesystem
/// cleanup runs first and database deletion is retriable: if the transaction
/// fails after the image was removed, the employee row still exists and a
/// retry repeats the (idempotent) image cleanup before deleting again.
/// </summary>
public class EmployeeDeletionService
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmployeeDeletionService> _logger;

    public EmployeeDeletionService(
        ApplicationDbContext context,
        IWebHostEnvironment env,
        ILogger<EmployeeDeletionService> logger)
    {
        _context = context;
        _env = env;
        _logger = logger;
    }

    public async Task<EmployeeDeletionResult> DeleteEmployeeAsync(
        int employeeId,
        int adminId,
        string adminUsername,
        string? reason = null)
    {
        var result = new EmployeeDeletionResult();

        // 1. Validate the employee exists before touching anything.
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee == null)
        {
            result.Success = false;
            result.Message = "Employee not found.";
            return result;
        }

        var photoPath = employee.PhotoPath;
        var employeeIdentifier = employee.EmployeeId;

        // 2. External filesystem cleanup first (profile image). This runs
        //    outside the transaction because transactions cannot roll back
        //    file deletions. A failure here aborts the whole operation so no
        //    database change is made and nothing is claimed as deleted.
        var imageCleanup = CleanupProfileImage(photoPath);
        result.Summary.ProfileImageDeleted = imageCleanup.Deleted;
        if (!imageCleanup.Deleted && imageCleanup.ExistsBefore)
        {
            result.Success = false;
            result.Message = "Cleanup failed: the employee's profile image could not be removed. No data was deleted.";
            result.Errors.Add(imageCleanup.Error ?? "Unknown filesystem error.");
            return result;
        }

        // 3. Database transaction for all relational employee-owned data.
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var attendanceDeleted = await _context.Attendances
                .Where(a => a.EmployeeId == employeeId)
                .ExecuteDeleteAsync();

            var assignmentDeleted = await _context.ScheduleEmployeeAssignments
                .Where(a => a.EmployeeId == employeeId)
                .ExecuteDeleteAsync();

            _context.Employees.Remove(employee);

            _context.EmployeeDeletionAudits.Add(new EmployeeDeletionAudit
            {
                AdminId = adminId,
                AdminUsername = adminUsername,
                DeletedEmployeeIdentifier = employeeIdentifier,
                Reason = reason,
                DeletedAtUtc = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            result.Summary.DeletedEmployeeDatabaseId = employee.Id;
            result.Summary.DeletedEmployeeIdentifier = employeeIdentifier;
            result.Summary.DeletedByAdmin = adminUsername;
            result.Summary.DeletedAt = DateTime.UtcNow;
            result.Summary.AttendanceRecordsDeleted = attendanceDeleted;
            result.Summary.ScheduleAssignmentsDeleted = assignmentDeleted;
            result.Summary.FaceEnrollmentCleared = true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            _logger.LogError(ex, "Permanent deletion of employee {EmployeeId} failed after image cleanup.", employeeId);

            result.Success = false;
            result.Message = "Database deletion failed. The profile image was already removed; the employee record still exists and the operation can be retried safely.";
            result.IsPartial = true;
            result.Errors.Add(ex.Message);
            return result;
        }

        // 4. Completion.
        if (!imageCleanup.ExistsBefore)
        {
            result.Summary.Warnings.Add("Profile image was not found on disk (already removed or never stored).");
        }

        result.Success = true;
        result.Message = "Employee and associated data permanently deleted successfully.";
        return result;
    }

    private ImageCleanup CleanupProfileImage(string? photoPath)
    {
        var cleanup = new ImageCleanup();

        if (string.IsNullOrWhiteSpace(photoPath))
        {
            return cleanup;
        }

        var fullPath = MapPhotoPath(photoPath);
        cleanup.ExistsBefore = System.IO.File.Exists(fullPath);

        if (!cleanup.ExistsBefore)
        {
            return cleanup;
        }

        try
        {
            System.IO.File.Delete(fullPath);
            cleanup.Deleted = true;
            _logger.LogInformation("Deleted profile image {Path} for terminated employee.", fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete profile image {Path}.", fullPath);
            cleanup.Error = ex.Message;
        }

        return cleanup;
    }

    private string MapPhotoPath(string photoPath)
    {
        var relative = photoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_env.WebRootPath, relative);
    }
}

public class EmployeeDeletionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsPartial { get; set; }
    public EmployeeDeletionSummary Summary { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class EmployeeDeletionSummary
{
    public int DeletedEmployeeDatabaseId { get; set; }
    public string? DeletedEmployeeIdentifier { get; set; }
    public string? DeletedByAdmin { get; set; }
    public DateTime DeletedAt { get; set; }
    public int AttendanceRecordsDeleted { get; set; }
    public int ScheduleAssignmentsDeleted { get; set; }
    public bool ProfileImageDeleted { get; set; }
    public bool FaceEnrollmentCleared { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

internal class ImageCleanup
{
    public bool ExistsBefore { get; set; }
    public bool Deleted { get; set; }
    public string? Error { get; set; }
}

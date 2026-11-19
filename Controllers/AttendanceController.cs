using System.Text.Json;
using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using FaceAttendanceSystem.Services;
using FaceAttendanceSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class AttendanceController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly AttendanceService _attendanceService;
    private readonly FaceRecognitionService _faceRecognitionService;
    private readonly GeofenceService _geofenceService;

    public AttendanceController(
        ApplicationDbContext context,
        AttendanceService attendanceService,
        FaceRecognitionService faceRecognitionService,
        GeofenceService geofenceService)
    {
        _context = context;
        _attendanceService = attendanceService;
        _faceRecognitionService = faceRecognitionService;
        _geofenceService = geofenceService;
    }

    public async Task<IActionResult> Index()
    {
        var attendances = await _context.Attendances
            .Include(a => a.Employee)
            .Include(a => a.Schedule)
            .OrderByDescending(a => a.TimeRecorded)
            .ToListAsync();

        return View(attendances);
    }

    public async Task<IActionResult> Today()
    {
        var today = DateTime.Today;
        var attendances = await _context.Attendances
            .Include(a => a.Employee)
            .Include(a => a.Schedule)
            .Where(a => a.TimeRecorded.Date == today)
            .OrderByDescending(a => a.TimeRecorded)
            .ToListAsync();

        return View(attendances);
    }

    public IActionResult CheckIn()
    {
        return View();
    }

    /// <summary>
    /// Issues a single-use head-turn challenge (valid 60 s) for the kiosk.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.CheckIn)]
    public IActionResult LivenessChallenge()
    {
        var challenge = LivenessChallengeStore.Issue(HttpContext.Session);
        return Json(new
        {
            challengeId = challenge.Id,
            direction = challenge.Direction,
            expiresInSeconds = (int)LivenessChallengeStore.Lifetime.TotalSeconds
        });
    }

    // Kiosk face check-in (called from attendance.js via fetch). Same liveness
    // and geofence rules as the employee self check-in.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.CheckIn)]
    public async Task<IActionResult> Recognize([FromBody] RecognizeRequest request)
    {
        var challenge = LivenessChallengeStore.Consume(HttpContext.Session, request?.ChallengeId);

        if (request == null)
            return Json(new { success = false, message = "No image received." });

        if (challenge == null)
            return Json(new { success = false, message = "The liveness challenge expired. Please try again." });

        if (!request.TryDecodeFrames(out var frames, out var frameError))
            return Json(new { success = false, message = frameError });

        var activeLocations = await _context.Locations.Where(l => l.IsActive).ToListAsync();
        var geofence = _geofenceService.Evaluate(request.Latitude, request.Longitude, request.Accuracy, activeLocations);
        if (!geofence.Allowed)
            return Json(new { success = false, message = geofence.Message });

        var candidates = await _context.Employees
            .Where(p => p.IsActive && !string.IsNullOrWhiteSpace(p.FaceEncoding))
            .ToListAsync();

        var result = await _faceRecognitionService.RecognizeWithLivenessAsync(frames, challenge.Direction, candidates);

        if (!result.LivenessPassed)
            return Json(new { success = false, message = result.ErrorMessage ?? "Liveness check failed. Please try again." });

        if (!result.IsRecognized || result.EmployeeId == null)
        {
            return Json(new
            {
                success = false,
                message = result.ErrorMessage ?? "Face not recognized. Please try again or use manual check-in."
            });
        }

        var employee = await _context.Employees.FindAsync(result.EmployeeId.Value);
        if (employee == null)
            return Json(new { success = false, message = "Recognized face not found in system." });

        var schedule = await FindClosestScheduleAsync(employee.Id);
        if (schedule == null)
            return Json(new { success = false, message = "No active schedule found for this employee." });

        var now = DateTime.Now;
        var scheduleEnd = schedule.Date.Date + schedule.EndTime;
        if (now > scheduleEnd)
        {
            return Json(new
            {
                success = false,
                message = "Check-in time has passed. Marked absent."
            });
        }

        var alreadyRecorded = await _context.Attendances.AnyAsync(a =>
            a.EmployeeId == employee.Id &&
            a.ScheduleId == schedule.Id);

        if (alreadyRecorded)
        {
            Console.WriteLine(
                $"DTCAM DEBUG: Duplicate attendance. EmployeeId={employee.Id}, ScheduleId={schedule.Id}, Time={DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            return Json(new
            {
                success = false,
                message = $"Attendance already recorded for Schedule #{schedule.Id} ({schedule.Title})."
            });
        }

        var status = _attendanceService.DetermineStatus(
            now, schedule.StartDateTime, schedule.LateGracePeriodMinutes);

        var attendance = new Attendance
        {
            EmployeeId = employee.Id,
            ScheduleId = schedule.Id,
            TimeRecorded = now,
            Status = status,
            Subject = schedule.Title,
            ConfidenceScore = (decimal)result.Confidence,
            CheckInLatitude = request.Latitude,
            CheckInLongitude = request.Longitude,
            CheckInAccuracyMeters = request.Accuracy,
            CheckInLocationId = geofence.LocationId,
            CheckInDistanceMeters = geofence.DistanceMeters,
            CheckInIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            LivenessPassed = true
        };

        _context.Attendances.Add(attendance);
        await _context.SaveChangesAsync();

        await _attendanceService.RecalculateAllEmployeesAsync(_context);

        return Json(new
        {
            success = true,
            employeeId = employee.Id,
            employeeName = employee.FullName,
            confidence = result.Confidence,
            status = status,
            schedule = schedule.Title,
            message = $"{employee.FullName} marked as {status} for {schedule.Title}."
        });
    }

    private async Task<Schedule?> FindClosestScheduleAsync(int employeeId)
    {
        var now = DateTime.Now;
        var today = now.Date;
        const int earlyCheckInMinutes = 60;

        var assignments = await _context.ScheduleEmployeeAssignments
            .Include(a => a.Schedule)
            .Where(a => a.EmployeeId == employeeId && a.Schedule.IsActive)
            .Select(a => a.Schedule)
            .Where(s => s.RepeatType == ScheduleRepeatType.None &&
                        s.Date >= today &&
                        s.Date < today.AddDays(1))
            .ToListAsync();

        var scheduleIds = assignments.Select(s => s.Id).ToList();

        var recordedIds = await _context.Attendances
            .Where(a => a.EmployeeId == employeeId &&
                        scheduleIds.Contains(a.ScheduleId))
            .Select(a => a.ScheduleId)
            .ToListAsync();

        var recorded = recordedIds.ToHashSet();

        var eligible = assignments
            .Where(s => !recorded.Contains(s.Id))
            .Where(s =>
            {
                var start = today + s.StartTime;
                var end = today + s.EndTime;

                return now >= start.AddMinutes(-earlyCheckInMinutes)
                    && now <= end;
            })
            .OrderBy(s => now >= today + s.StartTime ? 0 : 1)
            .ThenBy(s => Math.Abs(
                (now - (today + s.StartTime)).TotalMinutes))
            .ThenBy(s => s.Id)
            .ToList();

        return eligible.FirstOrDefault();
    }

    [HttpGet]
    public async Task<IActionResult> DebugSchedule(int employeeId)
    {
        var now = DateTime.Now;
        var today = now.Date;

        var assignments = await _context.ScheduleEmployeeAssignments
            .Include(a => a.Schedule)
            .Where(a => a.EmployeeId == employeeId)
            .ToListAsync();

        var result = assignments.Select(a => new
        {
            ScheduleId = a.ScheduleId,
            Title = a.Schedule.Title,
            Date = a.Schedule.Date.ToString("yyyy-MM-dd"),
            StartTime = a.Schedule.StartTime.ToString(@"hh\:mm"),
            EndTime = a.Schedule.EndTime.ToString(@"hh\:mm"),
            IsActive = a.Schedule.IsActive,
            MatchToday = a.Schedule.Date.Date == today
        }).ToList();

        return Json(new
        {
            today = today.ToString("yyyy-MM-dd"),
            now = now.ToString("yyyy-MM-dd HH:mm"),
            assignments = result
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(int employeeId, int scheduleId)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        var schedule = await _context.Schedules.FindAsync(scheduleId);

        if (employee == null || schedule == null)
        {
            TempData["Error"] = "Please select a valid staff member and schedule.";
            return RedirectToAction(nameof(CheckIn));
        }

        var isAssigned = await _context.ScheduleEmployeeAssignments
            .AnyAsync(a => a.ScheduleId == scheduleId && a.EmployeeId == employeeId);

        if (!isAssigned)
        {
            TempData["Error"] = "This employee is not assigned to the selected schedule.";
            return RedirectToAction(nameof(CheckIn));
        }

        var alreadyRecorded = await _context.Attendances.AnyAsync(a =>
            a.EmployeeId == employeeId &&
            a.ScheduleId == scheduleId);

        if (alreadyRecorded)
        {
            TempData["Error"] = "Attendance is already recorded for this activity.";
            return RedirectToAction(nameof(CheckIn));
        }

        var now = DateTime.Now;

        var status = _attendanceService.DetermineStatus(
            now, schedule.StartDateTime, schedule.LateGracePeriodMinutes);

        var attendance = new Attendance
        {
            EmployeeId = employeeId,
            ScheduleId = scheduleId,
            TimeRecorded = now,
            Status = status,
            Subject = schedule.Title,
            ConfidenceScore = 0
        };

        _context.Attendances.Add(attendance);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"{employee.FullName} marked as {status}.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadScheduleDropdown()
    {
        ViewBag.Schedules = new SelectList(
            await _context.Schedules
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.Date)
                .ThenByDescending(s => s.StartTime)
                .ToListAsync(),
            "Id",
            "Title");
    }
}

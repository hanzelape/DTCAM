using System.Text.Json;
using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using FaceAttendanceSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class AttendanceController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly AttendanceService _attendanceService;
    private readonly FaceRecognitionService _faceRecognitionService;

    public AttendanceController(
        ApplicationDbContext context,
        AttendanceService attendanceService,
        FaceRecognitionService faceRecognitionService)
    {
        _context = context;
        _attendanceService = attendanceService;
        _faceRecognitionService = faceRecognitionService;
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

    // Webcam-based face recognition check-in (called from attendance.js via fetch).
    [HttpPost]
    public async Task<IActionResult> Recognize([FromBody] RecognizeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Image))
            return Json(new { success = false, message = "No image received." });

        var imageBytes = Convert.FromBase64String(request.Image.Split(',')[1]);
        var maxBytes = 2 * 1024 * 1024;
        if (imageBytes.Length > maxBytes)
            return Json(new { success = false, message = "Image too large." });

        var candidates = await _context.Employees
            .Where(p => p.IsActive && !string.IsNullOrWhiteSpace(p.FaceEncoding))
            .ToListAsync();

        var result = await _faceRecognitionService.RecognizeFaceAsync(imageBytes, candidates);

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

        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            var locations = await _context.Locations.ToListAsync();
            var inside = false;
            Location? matchedLocation = null;
            double matchedDistance = 0;

            foreach (var loc in locations)
            {
                var distance = CalculateDistance(
                    request.Latitude.Value,
                    request.Longitude.Value,
                    loc.Latitude,
                    loc.Longitude);

                if (distance <= loc.RadiusMeters)
                {
                    inside = true;
                    matchedLocation = loc;
                    matchedDistance = distance;
                    break;
                }
            }

            if (!inside && locations.Any())
            {
                var nearest = locations
                    .Select(l => new
                    {
                        Location = l,
                        Distance = CalculateDistance(
                            request.Latitude.Value,
                            request.Longitude.Value,
                            l.Latitude,
                            l.Longitude)
                    })
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault();

                return Json(new
                {
                    success = false,
                    message = $"You are outside the allowed check-in area ({nearest!.Distance:F0)} m from {nearest.Location.Name}, limit {nearest.Location.RadiusMeters} m)."
                });
            }
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
            ConfidenceScore = (decimal)result.Confidence
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

    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
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

public class RecognizeRequest
{
    public string? Image { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

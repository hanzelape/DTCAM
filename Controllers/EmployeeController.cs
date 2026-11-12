using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using FaceAttendanceSystem.Services;
using FaceAttendanceSystem.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FaceAttendanceSystem.Controllers;

[AllowAnonymous]
public class EmployeeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly AttendanceService _attendanceService;
    private readonly FaceRecognitionService _faceRecognitionService;

    public EmployeeController(
        ApplicationDbContext context,
        AttendanceService attendanceService,
        FaceRecognitionService faceRecognitionService)
    {
        _context = context;
        _attendanceService = attendanceService;
        _faceRecognitionService = faceRecognitionService;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        var result = await HttpContext.AuthenticateAsync("EmployeeCookie");
        if (result?.Principal?.Identity?.IsAuthenticated == true)
            return RedirectToAction("Dashboard");

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(EmployeeLoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Email == model.Email);

        if (employee == null || string.IsNullOrEmpty(employee.PasswordHash) ||
            !PasswordHasher.VerifyPassword(model.Password, employee.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        if (!model.TermsAccepted)
        {
            ModelState.AddModelError(string.Empty, "You must accept the Terms and Conditions to log in.");
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, employee.Email ?? string.Empty),
            new Claim(ClaimTypes.NameIdentifier, employee.Id.ToString()),
            new Claim("FullName", employee.FullName),
            new Claim("EmployeeId", employee.EmployeeId)
        };

        var claimsIdentity = new ClaimsIdentity(claims, "EmployeeCookie");
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            "EmployeeCookie",
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        TempData["Success"] = $"Welcome, {employee.FullName}!";

        // Ask for camera + location once per login session. The popup is
        // dismissible, so this is a prompt, not a gate.
        HttpContext.Session.SetString("PromptPermissionsOnLoad", "true");

        // Land on the dashboard rather than the permission gate; see the
        // equivalent change in AccountController.Login.
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Dashboard");
    }

    /// <summary>
    /// Optional page for pre-granting camera/location access. Not a gate.
    /// </summary>
    [HttpGet]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public IActionResult GrantPermissions()
    {
        return View("~/Views/Shared/GrantPermissions.cshtml");
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    [ValidateAntiForgeryToken]
    [ActionName("GrantPermissions")]
    public IActionResult GrantPermissionsPost()
    {
        HttpContext.Session.SetString("PermissionsGranted", "true");
        return RedirectToAction("Dashboard");
    }

    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public async Task<IActionResult> Dashboard()
    {
        // No permission gate — the dashboard is always reachable.
        var employeeId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(employeeId))
            return RedirectToAction("Login");

        var employee = await _context.Employees.FindAsync(int.Parse(employeeId));
        if (employee == null)
            return RedirectToAction("Login");

        var recentRecords = await _context.Attendances
            .Include(a => a.Schedule)
            .Where(a => a.EmployeeId == employee.Id)
            .OrderByDescending(a => a.TimeRecorded)
            .Take(3)
            .ToListAsync();

        ViewBag.EmployeeName = employee.FullName;
        ViewBag.AttendanceRate = employee.AttendanceRate;
        ViewBag.TotalLogIns = employee.TotalLogIns;
        ViewBag.TotalAbsents = employee.TotalAbsents;
        ViewBag.TotalLates = employee.TotalLates;
        ViewBag.RecentRecords = recentRecords;

        return View();
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public async Task<IActionResult> Schedules()
    {
        var employeeId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(employeeId))
            return RedirectToAction("Login");

        var employee = await _context.Employees.FindAsync(int.Parse(employeeId));
        if (employee == null)
            return RedirectToAction("Login");

        var assignments = await _context.ScheduleEmployeeAssignments
            .Include(a => a.Schedule)
            .Where(a => a.EmployeeId == employee.Id)
            .OrderByDescending(a => a.Schedule.Date)
            .ThenBy(a => a.Schedule.StartTime)
            .ToListAsync();

        ViewBag.EmployeeName = employee.FullName;
        ViewBag.Schedules = assignments.Select(a => a.Schedule).ToList();
        return View();
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public async Task<IActionResult> Records()
    {
        var employeeId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(employeeId))
            return RedirectToAction("Login");

        var employee = await _context.Employees.FindAsync(int.Parse(employeeId));
        if (employee == null)
            return RedirectToAction("Login");

        var attendanceRecords = await _context.Attendances
            .Include(a => a.Schedule)
            .Where(a => a.EmployeeId == employee.Id)
            .OrderByDescending(a => a.TimeRecorded)
            .ToListAsync();

        ViewBag.EmployeeName = employee.FullName;
        ViewBag.AttendanceRecords = attendanceRecords;
        return View();
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public IActionResult CheckIn()
    {
        return View();
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public async Task<IActionResult> Recognize([FromBody] RecognizeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Image))
            return Json(new { success = false, message = "No image received." });

        var employeeId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(employeeId))
            return Json(new { success = false, message = "Not authenticated." });

        var imageBytes = Convert.FromBase64String(request.Image.Split(',')[1]);
        var maxBytes = 2 * 1024 * 1024;
        if (imageBytes.Length > maxBytes)
            return Json(new { success = false, message = "Image too large." });

        var employee = await _context.Employees.FindAsync(int.Parse(employeeId));
        if (employee == null || string.IsNullOrWhiteSpace(employee.FaceEncoding))
            return Json(new { success = false, message = "Face not registered." });

        var candidates = new List<Employee> { employee };
        var result = await _faceRecognitionService.RecognizeFaceAsync(imageBytes, candidates);

        if (!result.IsRecognized || result.EmployeeId == null)
        {
            return Json(new
            {
                success = false,
                message = result.ErrorMessage ?? "Face not recognized. Please try again."
            });
        }

        if (result.EmployeeId.Value != employee.Id)
        {
            return Json(new { success = false, message = "Face does not match your account." });
        }

        var schedule = await FindClosestScheduleAsync(employee.Id);
        if (schedule == null)
            return Json(new { success = false, message = "No active schedule found for you today." });

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
            return Json(new { success = false, message = $"Attendance already recorded for Schedule #{schedule.Id} ({schedule.Title})." });

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
                var startTime = today + s.StartTime;
                var endTime = today + s.EndTime;

                return now >= startTime.AddMinutes(-earlyCheckInMinutes)
                    && now <= endTime;
            })
            .OrderBy(s => now >= today + s.StartTime ? 0 : 1)
            .ThenBy(s => Math.Abs(
                (now - (today + s.StartTime)).TotalMinutes))
            .ThenBy(s => s.Id)
            .ToList();

        return eligible.FirstOrDefault();
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("EmployeeCookie");
        TempData["Success"] = "You have been logged out.";
        return RedirectToAction("Login");
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public IActionResult Privacy()
    {
        return View();
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public IActionResult ChangePassword()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(AuthenticationSchemes = "EmployeeCookie")]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword)
    {
        var employeeId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(employeeId))
            return RedirectToAction("Login");

        var employee = await _context.Employees.FindAsync(int.Parse(employeeId));
        if (employee == null)
            return RedirectToAction("Login");

        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            ViewData["Error"] = "Both current and new password are required.";
            return View();
        }

        if (newPassword.Length < 6)
        {
            ViewData["Error"] = "New password must be at least 6 characters.";
            return View();
        }

        if (string.IsNullOrEmpty(employee.PasswordHash) || !PasswordHasher.VerifyPassword(currentPassword, employee.PasswordHash))
        {
            ViewData["Error"] = "Current password is incorrect.";
            return View();
        }

        employee.PasswordHash = PasswordHasher.HashPassword(newPassword);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction("Dashboard");
    }
}

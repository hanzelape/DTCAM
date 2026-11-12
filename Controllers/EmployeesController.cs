using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using FaceAttendanceSystem.Services;
using FaceAttendanceSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class EmployeesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ImageStorageService _imageStorageService;
    private readonly FaceRecognitionService _faceRecognitionService;
    private readonly AttendanceService _attendanceService;
    private readonly EmployeeDeletionService _employeeDeletionService;
    private readonly ILogger<EmployeesController> _logger;

    public EmployeesController(
        ApplicationDbContext context,
        ImageStorageService imageStorageService,
        FaceRecognitionService faceRecognitionService,
        AttendanceService attendanceService,
        EmployeeDeletionService employeeDeletionService,
        ILogger<EmployeesController> logger)
    {
        _context = context;
        _imageStorageService = imageStorageService;
        _faceRecognitionService = faceRecognitionService;
        _attendanceService = attendanceService;
        _employeeDeletionService = employeeDeletionService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? search)
    {
        await _attendanceService.RecalculateAllEmployeesAsync(_context);
        var query = _context.Employees.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLower();
            query = query.Where(e =>
                (e.FullName != null && e.FullName.ToLower().Contains(search)) ||
                (e.EmployeeId != null && e.EmployeeId.ToLower().Contains(search)) ||
                (e.Department != null && e.Department.ToLower().Contains(search)));
        }

        var employees = await query.ToListAsync();
        ViewBag.Search = search;
        return View(employees);
    }

    public IActionResult Create()
    {
        return View(new EmployeeRegistrationViewModel { Department = "DTCAM" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EmployeeRegistrationViewModel model)
    {
        var registrationTimer = Stopwatch.StartNew();

        if (!ModelState.IsValid)
        {
            ViewBag.CapturedPhotos = Request.Form["CapturedPhotos"].ToString();
            return View(model);
        }

        // Enforce one-to-one: reject duplicate Employee IDs.
        var employeeId = model.EmployeeId?.Trim();
        var duplicateIdTimer = Stopwatch.StartNew();
        var duplicate = await _context.Employees
            .AnyAsync(p => p.EmployeeId == employeeId);
        _logger.LogInformation(
            "employee_registration_duplicate_id_query_ms={ElapsedMilliseconds}",
            duplicateIdTimer.Elapsed.TotalMilliseconds);

        if (duplicate)
        {
            ModelState.AddModelError(nameof(model.EmployeeId), $"Employee ID '{employeeId}' is already registered.");
            ViewBag.CapturedPhotos = Request.Form["CapturedPhotos"].ToString();
            return View(model);
        }

        var capturedPhotos = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.CapturedPhoto))
        {
            capturedPhotos.Add(model.CapturedPhoto);
        }

        var capturedPhotosJson = Request.Form["CapturedPhotos"].ToString();
        if (!string.IsNullOrWhiteSpace(capturedPhotosJson) && capturedPhotosJson != "[]")
        {
            try
            {
                var additionalPhotos = System.Text.Json.JsonSerializer.Deserialize<List<string>>(capturedPhotosJson);
                if (additionalPhotos != null)
                {
                    capturedPhotos.AddRange(additionalPhotos.Where(p => !string.IsNullOrWhiteSpace(p)));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                ModelState.AddModelError("", "Invalid captured photo data. Please capture the photos again.");
                ViewBag.CapturedPhotos = "[]";
                return View(model);
            }
        }

        if (!capturedPhotos.Any())
        {
            ModelState.AddModelError("", "Please capture at least one photo using the webcam.");
            ViewBag.CapturedPhotos = Request.Form["CapturedPhotos"].ToString();
            return View(model);
        }

        if (!TryDecodeCapturedPhoto(capturedPhotos[0], out var imageBytes))
        {
            ModelState.AddModelError("", "Invalid photo data. Please try capturing again.");
            ViewBag.CapturedPhotos = Request.Form["CapturedPhotos"].ToString();
            return View(model);
        }

        try
        {
            var employeeQueryTimer = Stopwatch.StartNew();
            var existingEmployees = await _context.Employees
                .AsNoTracking()
                .Select(employee => new Employee
                {
                    Id = employee.Id,
                    FullName = employee.FullName,
                    FaceEncoding = employee.FaceEncoding,
                    FaceEncodings = employee.FaceEncodings
                })
                .ToListAsync();
            _logger.LogInformation(
                "employee_registration_duplicate_face_candidates={CandidateCount} query_ms={ElapsedMilliseconds}",
                existingEmployees.Count,
                employeeQueryTimer.Elapsed.TotalMilliseconds);

            var firstEncodingResult = await _faceRecognitionService
                .CreateFaceEncodingAndCheckDuplicateAsync(imageBytes, existingEmployees);

            if (!string.IsNullOrWhiteSpace(firstEncodingResult.ErrorMessage))
            {
                ModelState.AddModelError("", firstEncodingResult.ErrorMessage);
                ViewBag.CapturedPhotos = Request.Form["CapturedPhotos"].ToString();
                return View(model);
            }

            if (firstEncodingResult.DuplicateCheck.IsRecognized)
            {
                var duplicateCheck = firstEncodingResult.DuplicateCheck;
                ModelState.AddModelError("", $"This face is already registered to '{duplicateCheck.EmployeeName}' (ID: {duplicateCheck.EmployeeId}), confidence {duplicateCheck.Confidence:P0}. Duplicate face registration is not allowed.");
                ViewBag.CapturedPhotos = Request.Form["CapturedPhotos"].ToString();
                return View(model);
            }

            var otherEncodingTasks = capturedPhotos.Skip(1).Select(async photo =>
            {
                if (!TryDecodeCapturedPhoto(photo, out var bytes))
                    return null;

                return await _faceRecognitionService.CreateFaceEncodingAsync(bytes);
            });
            var encodingResults = await Task.WhenAll(otherEncodingTasks);
            var encodings = new List<string> { firstEncodingResult.Encoding! };
            encodings.AddRange(encodingResults.OfType<string>().Where(encoding => !string.IsNullOrWhiteSpace(encoding)));

            var imageSaveTimer = Stopwatch.StartNew();
            var photoPath = await _imageStorageService.SaveCapturedImageAsync(imageBytes, "employees");
            _logger.LogInformation(
                "employee_registration_photo_save_ms={ElapsedMilliseconds}",
                imageSaveTimer.Elapsed.TotalMilliseconds);
            var faceEncodingsJson = System.Text.Json.JsonSerializer.Serialize(encodings);

            var employee = new Employee
            {
                EmployeeId = model.EmployeeId!,
                FullName = model.FullName,
                Department = "DTCAM",
                Email = model.Email,
                PasswordHash = PasswordHasher.HashPassword(model.Password),
                PhotoPath = photoPath,
                FaceEncoding = encodings.FirstOrDefault(),
                FaceEncodings = faceEncodingsJson,
                RegisteredAt = DateTime.Now,
                IsActive = true
            };

            _context.Employees.Add(employee);
            var employeeSaveTimer = Stopwatch.StartNew();
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "employee_registration_save_changes_ms={ElapsedMilliseconds}",
                employeeSaveTimer.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "employee_registration_completed elapsed_ms={ElapsedMilliseconds} encoded_angles={EncodedAngles}",
                registrationTimer.Elapsed.TotalMilliseconds,
                encodings.Count);
            TempData["Success"] = $"Employee '{employee.FullName}' registered successfully with {encodings.Count} face angles.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Registration failed: {ex.Message}");
            ViewBag.CapturedPhotos = Request.Form["CapturedPhotos"].ToString();
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null)
            return NotFound();

        var model = new EmployeeRegistrationViewModel
        {
            Id = employee.Id,
            EmployeeId = employee.EmployeeId,
            FullName = employee.FullName,
            Department = employee.Department,
            Email = employee.Email ?? string.Empty,
            AttendanceRate = employee.AttendanceRate
        };

        ViewBag.Departments = await LoadDepartmentsAsync();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EmployeeRegistrationViewModel model)
    {
        // Password is optional on edit (only changed when NewPassword is supplied).
        ModelState.Remove("Password");
        ModelState.Remove("CapturedPhoto");
        ModelState.Remove("CapturedPhotos");

        if (!ModelState.IsValid)
        {
            ViewBag.Departments = await LoadDepartmentsAsync();
            return View(model);
        }

        var employee = await _context.Employees.FindAsync(id);
        if (employee == null)
            return NotFound();

        employee.FullName = model.FullName;
        employee.Department = "DTCAM";
        employee.Email = model.Email ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            employee.PasswordHash = PasswordHasher.HashPassword(model.NewPassword);
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Employee '{employee.FullName}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Terminate(int id)
    {
        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var adminId = int.TryParse(adminIdClaim, out var parsed) ? parsed : 0;
        var adminName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Administrator";

        var result = await _employeeDeletionService.DeleteEmployeeAsync(id, adminId, adminName);

        if (!result.Success)
        {
            return Json(new { success = false, message = result.Message ?? "Deletion failed." });
        }

        return Json(new
        {
            success = true,
            message = "Employee and associated data permanently deleted successfully.",
            summary = result.Summary
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null)
        {
            TempData["Error"] = "Employee not found.";
            return RedirectToAction(nameof(Index));
        }

        employee.IsActive = true;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Employee '{employee.FullName}' has been reactivated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null)
            return NotFound();

        var assignments = await _context.ScheduleEmployeeAssignments
            .Include(a => a.Schedule)
            .Where(a => a.EmployeeId == id)
            .ToListAsync();

        var schedules = assignments.Select(a => a.Schedule).ToList();

        var attendanceRecords = await _context.Attendances
            .Include(a => a.Schedule)
            .Where(a => a.EmployeeId == id)
            .OrderByDescending(a => a.TimeRecorded)
            .ToListAsync();

        var now = DateTime.Now;
        var absentRecords = new List<dynamic>();

        foreach (var schedule in schedules)
        {
            var startDateTime = schedule.Date.Date + schedule.StartTime;
            var endDateTime = schedule.Date.Date + schedule.EndTime;

            if (schedule.RepeatType == Models.ScheduleRepeatType.None)
            {
                if (!attendanceRecords.Any(a => a.ScheduleId == schedule.Id) && now > endDateTime)
                {
                    absentRecords.Add(new
                    {
                        Schedule = schedule,
                        Date = schedule.Date,
                        StartTime = schedule.StartTime,
                        EndTime = schedule.EndTime
                    });
                }
            }
        }

        ViewBag.Schedules = schedules;
        ViewBag.AttendanceRecords = attendanceRecords;
        ViewBag.AbsentRecords = absentRecords;

        return View(employee);
    }

    private async Task<List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> LoadDepartmentsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var departments = await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = d.Name,
                Text = d.Name
            })
            .ToListAsync();
        _logger.LogInformation(
            "employees_departments_query_ms={ElapsedMilliseconds} department_count={DepartmentCount}",
            stopwatch.Elapsed.TotalMilliseconds,
            departments.Count);
        return departments;
    }

    private static bool TryDecodeCapturedPhoto(string photo, out byte[] imageBytes)
    {
        var separator = photo.IndexOf(',');
        if (separator < 0)
        {
            imageBytes = Array.Empty<byte>();
            return false;
        }

        try
        {
            imageBytes = Convert.FromBase64String(photo[(separator + 1)..]);
            return true;
        }
        catch (FormatException)
        {
            imageBytes = Array.Empty<byte>();
            return false;
        }
    }
}

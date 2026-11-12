using System.Diagnostics;
using FaceAttendanceSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FaceAttendanceSystem.Models;
using FaceAttendanceSystem.Services;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly AttendanceService _attendanceService;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context, AttendanceService attendanceService)
    {
        _logger = logger;
        _context = context;
        _attendanceService = attendanceService;
    }

    public async Task<IActionResult> Index()
    {
        // No permission gate here. The dashboard must be reachable whether or
        // not camera/location access was ever granted — permissions are
        // requested by the popup at the point a feature actually needs them.
        await _attendanceService.RecalculateAllEmployeesAsync(_context);

        var totalEmployees = await _context.Employees.CountAsync();
        var activeEmployees = await _context.Employees.CountAsync(e => e.IsActive);

        var start = DateTime.Today.AddDays(-30);
        var end = DateTime.Today;

        var records = await _context.Attendances
            .Include(a => a.Employee)
            .Include(a => a.Schedule)
            .Where(a => a.TimeRecorded.Date >= start.Date && a.TimeRecorded.Date <= end.Date)
            .OrderByDescending(a => a.TimeRecorded)
            .ToListAsync();

        var assignedSchedules = await _context.ScheduleEmployeeAssignments
            .Include(a => a.Schedule)
            .Include(a => a.Employee)
            .Where(a => a.Schedule.Date >= start.Date && a.Schedule.Date <= end.Date && a.Employee.IsActive)
            .ToListAsync();

        var scheduleRates = new List<decimal>();
        var presentCount = 0;
        var lateCount = 0;
        var absentCount = 0;
        var now = DateTime.Now;

        foreach (var assignment in assignedSchedules)
        {
            var schedule = assignment.Schedule;
            if (schedule == null) continue;

            var record = records.FirstOrDefault(a => a.EmployeeId == assignment.EmployeeId && a.ScheduleId == schedule.Id);
            if (record != null)
            {
                presentCount++;
                if (record.Status == "Late")
                    lateCount++;

                var startDateTime = schedule.Date.Date + schedule.StartTime;
                var endDateTime = schedule.Date.Date + schedule.EndTime;
                scheduleRates.Add(_attendanceService.CalculateAttendanceRate(record.TimeRecorded, startDateTime, endDateTime));
            }
            else
            {
                absentCount++;
                scheduleRates.Add(0m);
            }
        }

        var expectedCount = scheduleRates.Count;
        var attendanceRate = scheduleRates.Any() ? scheduleRates.Average() : 0;
        var absenteeismRate = expectedCount > 0 ? (decimal)absentCount / expectedCount * 100 : 0;
        var lateRate = presentCount > 0 ? (decimal)lateCount / presentCount * 100 : 0;

        var recentCheckIns = await _context.Attendances
            .Include(a => a.Employee)
            .Include(a => a.Schedule)
            .OrderByDescending(a => a.TimeRecorded)
            .Take(10)
            .ToListAsync();

        ViewBag.TotalEmployees = totalEmployees;
        ViewBag.ActiveEmployees = activeEmployees;
        ViewBag.TotalAttendanceToday = presentCount;
        ViewBag.EarlyToday = records.Count(a => a.Status == "Early");
        ViewBag.OnTimeToday = records.Count(a => a.Status == "On Time");
        ViewBag.LateToday = lateCount;
        ViewBag.AbsentToday = absentCount;
        ViewBag.ExpectedToday = expectedCount;
        ViewBag.AttendanceRate = attendanceRate;
        ViewBag.AbsenteeismRate = absenteeismRate;
        ViewBag.LateRate = lateRate;
        ViewBag.RecentCheckIns = recentCheckIns;

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

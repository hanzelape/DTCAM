using System.Text;
using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Services;
using FaceAttendanceSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly AttendanceService _attendanceService;

    public ReportsController(ApplicationDbContext context, AttendanceService attendanceService)
    {
        _context = context;
        _attendanceService = attendanceService;
    }

    public async Task<IActionResult> Index(
        DateTime? startDate,
        DateTime? endDate,
        int? employeeId,
        int? scheduleId,
        string? status)
    {
        await LoadDropdowns();

        var query = _context.Attendances
            .Include(a => a.Employee)
            .Include(a => a.Schedule)
            .AsQueryable();

        // Date range filter
        if (startDate.HasValue)
            query = query.Where(a => a.TimeRecorded.Date >= startDate.Value.Date);
        if (endDate.HasValue)
            query = query.Where(a => a.TimeRecorded.Date <= endDate.Value.Date);

        // Employee filter
        if (employeeId.HasValue)
            query = query.Where(a => a.EmployeeId == employeeId.Value);

        // Schedule filter
        if (scheduleId.HasValue)
            query = query.Where(a => a.ScheduleId == scheduleId.Value);

        // Status filter
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        var records = await query
            .OrderByDescending(a => a.TimeRecorded)
            .ToListAsync();

        var start = startDate ?? DateTime.Today.AddDays(-30);
        var end = endDate ?? DateTime.Today;

        var assignedSchedulesQuery = _context.ScheduleEmployeeAssignments
            .Include(a => a.Schedule)
            .Include(a => a.Employee)
            .Where(a => a.Schedule.Date >= start.Date && a.Schedule.Date <= end.Date);

        if (employeeId.HasValue)
            assignedSchedulesQuery = assignedSchedulesQuery.Where(a => a.EmployeeId == employeeId.Value);
        if (scheduleId.HasValue)
            assignedSchedulesQuery = assignedSchedulesQuery.Where(a => a.ScheduleId == scheduleId.Value);

        var assignedSchedules = await assignedSchedulesQuery.ToListAsync();

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

        var vm = new AttendanceReportViewModel
        {
            StartDate = startDate,
            EndDate = endDate,
            EmployeeId = employeeId,
            ScheduleId = scheduleId,
            Status = status,
            Records = records,
            TotalPresent = presentCount,
            TotalEarly = records.Count(a => a.Status == "Early"),
            TotalOnTime = records.Count(a => a.Status == "On Time"),
            TotalLate = lateCount,
            ExpectedCount = expectedCount,
            AbsentCount = absentCount,
            AttendanceRate = attendanceRate,
            AbsenteeismRate = absenteeismRate,
            LateRate = lateRate
        };

        return View(vm);
    }

    // CSV export of the current filtered results.
    public async Task<IActionResult> ExportCsv(
        DateTime? startDate,
        DateTime? endDate,
        int? employeeId,
        int? scheduleId,
        string? status)
    {
        var query = _context.Attendances
            .Include(a => a.Employee)
            .Include(a => a.Schedule)
            .AsQueryable();

        if (startDate.HasValue)
            query = query.Where(a => a.TimeRecorded.Date >= startDate.Value.Date);
        if (endDate.HasValue)
            query = query.Where(a => a.TimeRecorded.Date <= endDate.Value.Date);
        if (employeeId.HasValue)
            query = query.Where(a => a.EmployeeId == employeeId.Value);
        if (scheduleId.HasValue)
            query = query.Where(a => a.ScheduleId == scheduleId.Value);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        var records = await query
            .OrderByDescending(a => a.TimeRecorded)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Date/Time,Employee,Schedule,Status,Confidence");

        foreach (var r in records)
        {
            sb.AppendLine(string.Join(",",
                r.TimeRecorded.ToString("yyyy-MM-dd HH:mm:ss"),
                EscapeCsv(r.Employee?.FullName ?? ""),
                EscapeCsv(r.Schedule?.Title ?? ""),
                EscapeCsv(r.Status),
                r.ConfidenceScore.ToString()));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv",
            $"attendance-report-{DateTime.Now:yyyyMMddHHmmss}.csv");
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private async Task LoadDropdowns()
    {
        ViewBag.Employees = new SelectList(
            await _context.Employees
                .Where(p => p.IsActive)
                .OrderBy(p => p.FullName)
                .ToListAsync(),
            "Id",
            "FullName");

        ViewBag.Schedules = new SelectList(
            await _context.Schedules
                .Where(s => s.IsActive)
                .OrderBy(s => s.Title)
                .ToListAsync(),
            "Id",
            "Title");
    }
}

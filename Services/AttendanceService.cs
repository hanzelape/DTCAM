namespace FaceAttendanceSystem.Services;

using FaceAttendanceSystem.Data;
using Microsoft.EntityFrameworkCore;

public class AttendanceService
{
    /// <summary>
    /// Determines the attendance status based on when the check-in occurred,
    /// the scheduled start time, and a configurable late grace period.
    /// </summary>
    public string DetermineStatus(DateTime recordedAt, DateTime start, int lateGracePeriodMinutes)
    {
        if (recordedAt < start) return "Early";

        var onTimeCutoff = start.AddMinutes(lateGracePeriodMinutes);
        if (recordedAt <= onTimeCutoff) return "On Time";

        return "Late";
    }

    /// <summary>
    /// Calculates attendance rate percentage for a single check-in based on late minutes vs shift duration.
    /// </summary>
    public decimal CalculateAttendanceRate(DateTime checkInTime, DateTime startTime, DateTime endTime)
    {
        var shiftDuration = (endTime - startTime).TotalMinutes;
        if (shiftDuration <= 0)
            return 100m;

        var lateMinutes = (checkInTime - startTime).TotalMinutes;
        if (lateMinutes <= 0)
            return 100m;

        var minutesRendered = shiftDuration - lateMinutes;
        if (minutesRendered <= 0)
            return 0m;

        return (decimal)(minutesRendered / shiftDuration * 100);
    }

    /// <summary>
    /// Recalculates all attendance metrics for all employees based on actual schedule assignments and check-ins.
    /// </summary>
    public async Task RecalculateAllEmployeesAsync(ApplicationDbContext context)
    {
        var employees = await context.Employees.ToListAsync();
        var attendanceRecords = await context.Attendances
            .Include(a => a.Schedule)
            .ToListAsync();

        var assignments = await context.ScheduleEmployeeAssignments
            .Include(a => a.Schedule)
            .ToListAsync();

        var now = DateTime.Now;

        foreach (var employee in employees)
        {
            var employeeRecords = attendanceRecords.Where(a => a.EmployeeId == employee.Id).ToList();
            var totalLogIns = employeeRecords.Count;
            var totalLates = employeeRecords.Count(a => a.Status == "Late");

            var employeeAssignments = assignments.Where(a => a.EmployeeId == employee.Id).ToList();
            var scheduleRates = new List<decimal>();
            var totalAbsents = 0;

            foreach (var assignment in employeeAssignments)
            {
                var schedule = assignment.Schedule;
                if (schedule == null) continue;

                var record = employeeRecords.FirstOrDefault(a => a.ScheduleId == schedule.Id);
                if (record != null)
                {
                    var startDateTime = schedule.Date.Date + schedule.StartTime;
                    var endDateTime = schedule.Date.Date + schedule.EndTime;
                    scheduleRates.Add(CalculateAttendanceRate(record.TimeRecorded, startDateTime, endDateTime));
                }
                else
                {
                    if (schedule.RepeatType == Models.ScheduleRepeatType.None)
                    {
                        if (now > (schedule.Date.Date + schedule.EndTime))
                        {
                            scheduleRates.Add(0m);
                            totalAbsents++;
                        }
                    }
                    else
                    {
                        var registrationWeekStart = employee.RegisteredAt.Date.AddDays(-(int)employee.RegisteredAt.DayOfWeek);
                        var repeatDays = schedule.RepeatDays
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(d => int.Parse(d))
                            .ToList();

                        var currentDate = registrationWeekStart;
                        while (currentDate <= now.Date)
                        {
                            if (repeatDays.Contains((int)currentDate.DayOfWeek))
                            {
                                var hasAttendance = employeeRecords.Any(a => a.ScheduleId == schedule.Id && a.TimeRecorded.Date == currentDate);
                                if (!hasAttendance)
                                {
                                    if (currentDate < now.Date)
                                    {
                                        scheduleRates.Add(0m);
                                        totalAbsents++;
                                    }
                                }
                            }
                            currentDate = currentDate.AddDays(1);
                        }
                    }
                }
            }

            var attendanceRate = scheduleRates.Any() ? scheduleRates.Average() : 0m;

            employee.AttendanceRate = Math.Max(0m, Math.Min(100m, attendanceRate));
            employee.TotalLogIns = totalLogIns;
            employee.TotalAbsents = totalAbsents;
            employee.TotalLates = totalLates;
        }

        await context.SaveChangesAsync();
    }
}

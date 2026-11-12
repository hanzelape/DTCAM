using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using FaceAttendanceSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class SchedulesController : Controller
{
    private readonly ApplicationDbContext _context;

    public SchedulesController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var query = _context.Schedules
            .Include(s => s.EmployeeAssignments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLower();
            query = query.Where(s =>
                (s.Title != null && s.Title.ToLower().Contains(search)) ||
                (s.Description != null && s.Description.ToLower().Contains(search)) ||
                (s.Location != null && s.Location.ToLower().Contains(search)));
        }

        var schedules = await query
            .OrderByDescending(s => s.Date)
            .ThenBy(s => s.StartTime)
            .ToListAsync();

        ViewBag.Search = search;
        return View(schedules);
    }

    public async Task<IActionResult> Delete(int id)
    {
        var schedule = await _context.Schedules
            .Include(s => s.EmployeeAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (schedule == null)
            return NotFound();

        return View(schedule);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var schedule = await _context.Schedules
            .Include(s => s.EmployeeAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (schedule == null)
            return NotFound();

        _context.ScheduleEmployeeAssignments.RemoveRange(schedule.EmployeeAssignments);
        _context.Schedules.Remove(schedule);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Schedule '{schedule.Title}' deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var schedule = await _context.Schedules
            .Include(s => s.EmployeeAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (schedule == null)
            return NotFound();

        var model = new ScheduleCreateViewModel
        {
            Title = schedule.Title,
            Description = schedule.Description,
            Date = schedule.Date,
            StartTime = schedule.StartTime,
            EndTime = schedule.EndTime,
            Location = schedule.Location,
            RepeatType = schedule.RepeatType,
            SelectedDays = schedule.RepeatDays
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => int.TryParse(v, out var n) ? n : -1)
                .Where(v => v >= 0)
                .ToList(),
            LateGracePeriodMinutes = schedule.LateGracePeriodMinutes
        };

        model.SelectedEmployeeIds = schedule.EmployeeAssignments
            .Select(a => a.EmployeeId)
            .ToList();

        model.AssignAll = model.SelectedEmployeeIds.Count > 0 && model.SelectedEmployeeIds.Count == await _context.Employees.CountAsync();

        model.SelectedDepartmentIds = await (
            from employee in _context.Employees
            join department in _context.Departments
                on employee.Department equals department.Name
            where model.SelectedEmployeeIds.Contains(employee.Id)
            select department.Id
        )
        .Distinct()
        .ToListAsync();

        await LoadCreateOptions(model);
        return View("Edit", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ScheduleCreateViewModel model)
    {
        var schedule = await _context.Schedules
            .Include(s => s.EmployeeAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (schedule == null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            await LoadCreateOptions(model);
            return View(model);
        }

        schedule.Title = model.Title;
        schedule.Description = model.Description;
        schedule.Date = model.Date;
        schedule.StartTime = model.StartTime;
        schedule.EndTime = model.EndTime;
        schedule.Location = model.Location;
        schedule.RepeatType = model.RepeatType;
        schedule.RepeatDays = string.Join(",", model.SelectedDays.OrderBy(d => d));
        schedule.LateGracePeriodMinutes = model.LateGracePeriodMinutes;

        await AssignEmployeesToScheduleAsync(schedule.Id, model);

        TempData["Success"] = $"Schedule '{schedule.Title}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Assignments(int? scheduleId)
    {
        var model = new ScheduleCreateViewModel();
        await LoadAssignmentOptions(model);

        if (scheduleId.HasValue && scheduleId.Value > 0)
        {
            model.ScheduleId = scheduleId.Value;

            var schedule = await _context.Schedules
                .Include(s => s.EmployeeAssignments)
                .FirstOrDefaultAsync(s => s.Id == scheduleId.Value);

            if (schedule != null)
            {
                ViewBag.CurrentAssignments = schedule.EmployeeAssignments
                    .Select(a => new { a.EmployeeId, EmployeeName = a.Employee != null ? a.Employee.FullName : "Unknown" })
                    .ToList();

                ViewBag.AssignedEmployeeIds = schedule.EmployeeAssignments
                    .Select(a => a.EmployeeId)
                    .ToHashSet();
            }
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAssignment(int scheduleId, int employeeId)
    {
        var assignment = await _context.ScheduleEmployeeAssignments
            .FirstOrDefaultAsync(a => a.ScheduleId == scheduleId && a.EmployeeId == employeeId);

        if (assignment != null)
        {
            _context.ScheduleEmployeeAssignments.Remove(assignment);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Assignment removed successfully.";
        }

        return RedirectToAction(nameof(Assignments), new { scheduleId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assignments(ScheduleCreateViewModel model)
    {
        if (model.ScheduleId <= 0)
        {
            ModelState.AddModelError(nameof(model.ScheduleId), "Choose a schedule to assign.");
            await LoadAssignmentOptions(model);
            return View(model);
        }

        var schedule = await _context.Schedules
            .Include(s => s.EmployeeAssignments)
            .FirstOrDefaultAsync(s => s.Id == model.ScheduleId);

        if (schedule == null)
        {
            ModelState.AddModelError(nameof(model.ScheduleId), "Schedule not found.");
            await LoadAssignmentOptions(model);
            return View(model);
        }

        await AssignEmployeesToScheduleAsync(schedule.Id, model);

        TempData["Success"] = $"Assignments saved for '{schedule.Title}'.";
        return RedirectToAction(nameof(Assignments), new { scheduleId = model.ScheduleId });
    }

    public async Task<IActionResult> Create()
    {
        await LoadCreateOptions(new ScheduleCreateViewModel());
        return View(new ScheduleCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ScheduleCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await LoadCreateOptions(model);
            return View(model);
        }

        var schedule = new Schedule
        {
            Title = model.Title,
            Description = model.Description,
            Date = model.Date,
            StartTime = model.StartTime,
            EndTime = model.EndTime,
            Location = model.Location,
            RepeatType = model.RepeatType,
            RepeatDays = string.Join(",", model.SelectedDays.OrderBy(d => d)),
            LateGracePeriodMinutes = model.LateGracePeriodMinutes,
            IsActive = true
        };

        _context.Schedules.Add(schedule);
        await _context.SaveChangesAsync();

        await AssignEmployeesToScheduleAsync(schedule.Id, model);

        TempData["Success"] = $"Schedule '{schedule.Title}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    private async Task AssignEmployeesToScheduleAsync(int scheduleId, ScheduleCreateViewModel model)
    {
        var employees = new List<Employee>();

        if (model.AssignAll)
        {
            employees = await _context.Employees
                .Where(e => e.IsActive)
                .ToListAsync();
        }
        else if (model.SelectedDepartmentIds != null && model.SelectedDepartmentIds.Any())
        {
            var selectedDepartments = await _context.Departments
                .Where(d => model.SelectedDepartmentIds.Contains(d.Id))
                .Select(d => d.Name)
                .ToListAsync();

            employees = await _context.Employees
                .Where(e => e.Department != null && selectedDepartments.Contains(e.Department))
                .ToListAsync();

            if (model.SelectedEmployeeIds != null && model.SelectedEmployeeIds.Any())
            {
                employees = employees
                    .Where(e => model.SelectedEmployeeIds.Contains(e.Id) || selectedDepartments.Contains(e.Department ?? string.Empty))
                    .ToList();
            }
        }
        else if (model.SelectedEmployeeIds != null && model.SelectedEmployeeIds.Any())
        {
            employees = await _context.Employees
                .Where(e => model.SelectedEmployeeIds.Contains(e.Id))
                .ToListAsync();
        }
        else
        {
            employees = new List<Employee>();
        }

        if (employees.Any())
        {
            var existingIds = new HashSet<int>(await _context.ScheduleEmployeeAssignments
                .Where(a => a.ScheduleId == scheduleId)
                .Select(a => a.EmployeeId)
                .ToListAsync());

            foreach (var employee in employees)
            {
                if (existingIds.Contains(employee.Id))
                    continue;

                _context.ScheduleEmployeeAssignments.Add(new ScheduleEmployeeAssignment
                {
                    ScheduleId = scheduleId,
                    EmployeeId = employee.Id
                });
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task LoadCreateOptions(ScheduleCreateViewModel model)
    {
        ViewBag.Departments = await _context.Departments
            .OrderBy(d => d.Name)
            .Select(d => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = d.Id.ToString(),
                Text = d.Name
            })
            .ToListAsync();

        ViewBag.Employees = await _context.Employees
            .OrderBy(e => e.FullName)
            .Select(e => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = e.Id.ToString(),
                Text = e.FullName + " (" + e.EmployeeId + ")"
            })
            .ToListAsync();

        ViewBag.DepartmentEmployees = await _context.Employees
            .Where(e => e.IsActive)
            .OrderBy(e => e.FullName)
            .ToListAsync();

        model.SelectedDepartmentIds ??= new List<int>();
        model.SelectedEmployeeIds ??= new List<int>();
    }

    private async Task LoadAssignmentOptions(ScheduleCreateViewModel model)
    {
        ViewBag.Schedules = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
            await _context.Schedules
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.Date)
                .ThenBy(s => s.StartTime)
                .ToListAsync(),
            "Id",
            "Title");

        await LoadCreateOptions(model);
    }
}

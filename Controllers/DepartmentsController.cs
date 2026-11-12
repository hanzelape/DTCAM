using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class DepartmentsController : Controller
{
    private readonly ApplicationDbContext _context;

    public DepartmentsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search)
    {
        await EnsureSampleDepartmentsAsync();

        var query = _context.Departments.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLower();
            query = query.Where(d => d.Name != null && d.Name.ToLower().Contains(search));
        }

        var departments = await query
            .OrderBy(d => d.Name)
            .ToListAsync();

        ViewBag.Search = search;
        return View(departments);
    }

    public async Task<IActionResult> Details(int id)
    {
        var department = await _context.Departments.FindAsync(id);
        if (department == null)
            return NotFound();

        var employees = await _context.Employees
            .Where(e => e.Department == department.Name)
            .OrderBy(e => e.FullName)
            .ToListAsync();

        ViewBag.Employees = employees;

        var otherDepartments = await _context.Departments
            .Where(d => d.Id != id)
            .OrderBy(d => d.Name)
            .ToListAsync();

        ViewBag.OtherDepartments = otherDepartments;

        return View(department);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveEmployee(int employeeId, int newDepartmentId)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null)
        {
            TempData["Error"] = "Employee not found.";
            return RedirectToAction(nameof(Index));
        }

        var department = await _context.Departments.FindAsync(newDepartmentId);
        if (department == null)
        {
            TempData["Error"] = "Department not found.";
            return RedirectToAction(nameof(Index));
        }

        employee.Department = department.Name;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Employee '{employee.FullName}' moved to '{department.Name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        name = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Department name is required.";
            return RedirectToAction(nameof(Index));
        }

        var exists = await _context.Departments
            .AnyAsync(d => d.Name == name);

        if (exists)
        {
            TempData["Error"] = $"Department '{name}' already exists.";
            return RedirectToAction(nameof(Index));
        }

        _context.Departments.Add(new Department { Name = name, CreatedAt = DateTime.Now });
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Department '{name}' created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var department = await _context.Departments.FindAsync(id);
        if (department == null)
        {
            TempData["Error"] = "Department not found.";
            return RedirectToAction(nameof(Index));
        }

        _context.Departments.Remove(department);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Department '{department.Name}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task EnsureSampleDepartmentsAsync()
    {
        if (await _context.Departments.AnyAsync())
            return;

        _context.Departments.AddRange(
            new Department { Name = "Department 1", CreatedAt = DateTime.Now },
            new Department { Name = "Department 2", CreatedAt = DateTime.Now },
            new Department { Name = "Department 3", CreatedAt = DateTime.Now },
            new Department { Name = "Department 4", CreatedAt = DateTime.Now },
            new Department { Name = "Department 5", CreatedAt = DateTime.Now });

        await _context.SaveChangesAsync();
    }
}

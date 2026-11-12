using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class LocationsController : Controller
{
    private readonly ApplicationDbContext _context;

    public LocationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search)
    {
        await EnsureSampleLocationAsync();

        var query = _context.Locations.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(l => l.Name.Contains(search));
        }

        var locations = await query
            .OrderByDescending(l => l.IsActive)
            .ThenBy(l => l.Name)
            .ToListAsync();

        ViewBag.Search = search;
        return View(locations);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, double latitude, double longitude, int radiusMeters)
    {
        name = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Location name is required.";
            return RedirectToAction(nameof(Index));
        }

        var location = new Location
        {
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            RadiusMeters = radiusMeters >= 5 ? radiusMeters : 100,
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Location '{name}' created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id)
    {
        var locations = await _context.Locations.ToListAsync();

        foreach (var loc in locations)
        {
            loc.IsActive = loc.Id == id;
        }

        await _context.SaveChangesAsync();

        var active = locations.FirstOrDefault(l => l.Id == id);
        TempData["Success"] = active != null ? $"Location '{active.Name}' is now active." : "Active location updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var location = await _context.Locations.FindAsync(id);
        if (location == null)
        {
            TempData["Error"] = "Location not found.";
            return RedirectToAction(nameof(Index));
        }

        _context.Locations.Remove(location);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Location '{location.Name}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Active()
    {
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.IsActive);

        if (location == null)
            return Json(new { success = false, message = "No active location configured." });

        return Json(new
        {
            success = true,
            id = location.Id,
            name = location.Name,
            latitude = location.Latitude,
            longitude = location.Longitude,
            radiusMeters = location.RadiusMeters
        });
    }

    private async Task EnsureSampleLocationAsync()
    {
        if (await _context.Locations.AnyAsync())
            return;

        _context.Locations.Add(new Location
        {
            Name = "Main Office",
            Latitude = 14.5995,
            Longitude = 120.9846,
            RadiusMeters = 200,
            IsActive = true,
            CreatedAt = DateTime.Now
        });

        await _context.SaveChangesAsync();
    }
}

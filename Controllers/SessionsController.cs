using FaceAttendanceSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Controllers;

[Authorize]
public class SessionsController : Controller
{
    private readonly ApplicationDbContext _context;

    public SessionsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var sessions = await _context.AdminSessions
            .Include(s => s.Admin)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return View(sessions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id)
    {
        var session = await _context.AdminSessions.FindAsync(id);
        if (session == null)
            return NotFound();

        session.IsRevoked = true;
        session.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Session revoked successfully.";
        return RedirectToAction(nameof(Index));
    }
}

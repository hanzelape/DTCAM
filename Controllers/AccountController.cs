using System.Security.Claims;
using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using FaceAttendanceSystem.Services;
using FaceAttendanceSystem.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;

    public AccountController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (result?.Principal?.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        await EnsureDefaultAdminAsync();

        var admin = await _context.Admins
            .FirstOrDefaultAsync(a => a.Username == model.Username);

        if (admin == null || !PasswordHasher.VerifyPassword(model.Password, admin.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        if (!model.TermsAccepted)
        {
            ModelState.AddModelError(string.Empty, "You must accept the Terms and Conditions to log in.");
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = Request.Headers["User-Agent"].ToString();

        _context.AdminSessions.Add(new AdminSession
        {
            AdminId = admin.Id,
            SessionId = sessionId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ExpiresAt = model.RememberMe ? DateTime.UtcNow.AddDays(7) : DateTime.UtcNow.AddHours(8),
            IsRevoked = false
        });

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, admin.Username),
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim("DisplayName", admin.DisplayName),
            new Claim("SessionId", sessionId)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Welcome back, {admin.DisplayName}!";

        // Ask for camera + location once per login session. The popup is
        // dismissible, so this is a prompt, not a gate.
        HttpContext.Session.SetString("PromptPermissionsOnLoad", "true");

        // Land on the dashboard. Previously login redirected to
        // GrantPermissions, which blocked the whole app behind a camera
        // prompt; the permission popup is now raised by the feature that
        // actually needs it.
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    /// <summary>
    /// Optional page for pre-granting camera/location access. Not a gate:
    /// every screen works without visiting it.
    /// </summary>
    [HttpGet]
    [Authorize]
    public IActionResult GrantPermissions()
    {
        return View("~/Views/Shared/GrantPermissions.cshtml");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    [ActionName("GrantPermissions")]
    public IActionResult GrantPermissionsPost()
    {
        HttpContext.Session.SetString("PermissionsGranted", "true");
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var sessionId = User.FindFirst("SessionId")?.Value;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var session = await _context.AdminSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId);

            if (session != null)
            {
                session.IsRevoked = true;
                session.RevokedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "You have been logged out.";
        return RedirectToAction("Login");
    }

    private async Task EnsureDefaultAdminAsync()
    {
        if (!await _context.Admins.AnyAsync())
        {
            _context.Admins.Add(new Admin
            {
                Username = "admin",
                PasswordHash = PasswordHasher.HashPassword("admin123"),
                DisplayName = "Administrator",
                CreatedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }
    }
}

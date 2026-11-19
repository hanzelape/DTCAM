using System.Security.Claims;
using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using FaceAttendanceSystem.Services;
using FaceAttendanceSystem.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        var admin = await _context.Admins
            .FirstOrDefaultAsync(a => a.Username == model.Username);

        if (admin != null && LoginLockout.IsLockedOut(admin))
        {
            ModelState.AddModelError(string.Empty, LoginLockout.LockedMessage);
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        if (admin == null || !PasswordHasher.VerifyPassword(model.Password, admin.PasswordHash))
        {
            var locked = false;
            if (admin != null)
            {
                locked = LoginLockout.RegisterFailure(admin);
                await _context.SaveChangesAsync();
            }

            ModelState.AddModelError(string.Empty, locked ? LoginLockout.LockedMessage : "Invalid username or password.");
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        if (!model.TermsAccepted)
        {
            ModelState.AddModelError(string.Empty, "You must accept the Terms and Conditions to log in.");
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }

        LoginLockout.RegisterSuccess(admin);

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

        if (admin.MustChangePassword)
        {
            TempData["Success"] = "You must set a new password before continuing.";
            return RedirectToAction(nameof(ChangePassword));
        }

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

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ChangePassword()
    {
        var admin = await GetCurrentAdminAsync();
        if (admin == null)
            return RedirectToAction(nameof(Login));

        ViewBag.Forced = admin.MustChangePassword;
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var admin = await GetCurrentAdminAsync();
        if (admin == null)
            return RedirectToAction(nameof(Login));

        ViewBag.Forced = admin.MustChangePassword;

        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            ViewData["Error"] = "Current and new password are required.";
            return View();
        }

        if (!PasswordHasher.VerifyPassword(currentPassword, admin.PasswordHash))
        {
            ViewData["Error"] = "Current password is incorrect.";
            return View();
        }

        if (newPassword != confirmPassword)
        {
            ViewData["Error"] = "The new passwords do not match.";
            return View();
        }

        if (newPassword.Length < AdminBootstrapper.MinAdminPasswordLength)
        {
            ViewData["Error"] = $"New password must be at least {AdminBootstrapper.MinAdminPasswordLength} characters.";
            return View();
        }

        if (newPassword == currentPassword || newPassword == AdminBootstrapper.LegacyDefaultPassword ||
            string.Equals(newPassword, admin.Username, StringComparison.OrdinalIgnoreCase))
        {
            ViewData["Error"] = "Choose a new password that is different from your current password and username.";
            return View();
        }

        admin.PasswordHash = PasswordHasher.HashPassword(newPassword);
        admin.MustChangePassword = false;
        admin.PasswordChangedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction("Index", "Home");
    }

    private async Task<Admin?> GetCurrentAdminAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
            return null;
        return await _context.Admins.FindAsync(adminId);
    }
}

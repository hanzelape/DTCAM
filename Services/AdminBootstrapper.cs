using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Services;

/// <summary>
/// Runs once at startup:
///  - when no admin exists, creates the first one from configuration
///    (Admin__InitialUsername / Admin__InitialPassword), flagged MustChangePassword;
///  - flags any existing admin still using the old hard-coded "admin123".
/// There is no hard-coded fallback account.
/// </summary>
public static class AdminBootstrapper
{
    public const string LegacyDefaultPassword = "admin123";
    public const int MinAdminPasswordLength = 10;

    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AdminBootstrapper");
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            var admins = await db.Admins.ToListAsync();

            if (admins.Count == 0)
            {
                var username = config["Admin:InitialUsername"]?.Trim();
                var password = config["Admin:InitialPassword"];

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    logger.LogWarning(
                        "No admin account exists. Set Admin__InitialUsername and Admin__InitialPassword " +
                        "(e.g. in .env) and restart to create the first admin.");
                    return;
                }

                if (password.Length < MinAdminPasswordLength || password == LegacyDefaultPassword)
                {
                    logger.LogError(
                        "Admin__InitialPassword is too weak (minimum {Min} characters, and not the old default). " +
                        "The first admin was not created.", MinAdminPasswordLength);
                    return;
                }

                db.Admins.Add(new Admin
                {
                    Username = username,
                    PasswordHash = PasswordHasher.HashPassword(password),
                    DisplayName = "Administrator",
                    CreatedAt = DateTime.Now,
                    MustChangePassword = true
                });
                await db.SaveChangesAsync();
                logger.LogWarning(
                    "Created initial admin '{Username}' from configuration. The password must be changed at first login; " +
                    "remove Admin__InitialPassword from the environment afterwards.", username);
                return;
            }

            var flagged = 0;
            foreach (var admin in admins.Where(a => !a.MustChangePassword))
            {
                if (PasswordHasher.VerifyPassword(LegacyDefaultPassword, admin.PasswordHash))
                {
                    admin.MustChangePassword = true;
                    flagged++;
                }
            }

            if (flagged > 0)
            {
                await db.SaveChangesAsync();
                logger.LogWarning(
                    "{Count} admin account(s) still use the old default password and must change it at next login.",
                    flagged);
            }
        }
        catch (Exception ex)
        {
            // Most likely the database is down or the AddSecurityHardening migration is missing.
            logger.LogError(ex,
                "Admin bootstrap failed. Make sure the database is reachable and run `dotnet ef database update`.");
        }
    }
}

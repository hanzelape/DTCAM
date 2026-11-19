using System.Security.Claims;
using FaceAttendanceSystem.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace FaceAttendanceSystem.Services;

/// <summary>
/// Blocks every admin page until a flagged admin changes their password.
/// The flag is read from the database on each request, so cookies issued
/// before the account was flagged are covered too.
/// </summary>
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();

        // Employee pages use their own cookie scheme; non-MVC endpoints are skipped.
        if (action == null || action.ControllerName == "Employee" || IsAllowed(action))
        {
            await _next(context);
            return;
        }

        var user = context.User;
        if (user.Identity?.IsAuthenticated == true &&
            user.Identity.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme &&
            int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            var admin = await db.Admins.FindAsync(adminId);
            if (admin != null && admin.MustChangePassword)
            {
                if (AuthResponses.IsApiRequest(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        message = "You must change your password before continuing."
                    });
                    return;
                }

                context.Response.Redirect("/Account/ChangePassword");
                return;
            }
        }

        await _next(context);
    }

    private static bool IsAllowed(ControllerActionDescriptor action) =>
        (action.ControllerName == "Account" &&
            action.ActionName is "ChangePassword" or "Logout" or "Login") ||
        (action.ControllerName == "Home" && action.ActionName == "Error");
}

public static class AuthResponses
{
    /// <summary>True for fetch/XHR calls that expect JSON rather than an HTML page.</summary>
    public static bool IsApiRequest(HttpRequest request)
    {
        if (request.Headers.XRequestedWith == "XMLHttpRequest")
            return true;

        if (request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return true;

        return request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;
    }
}

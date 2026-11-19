
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Threading.RateLimiting;
using FaceAttendanceSystem.Data;
using FaceAttendanceSystem.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
// Trust forwarded HTTPS information only from local Nginx.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

// Add services to the container.
builder.Services.AddControllersWithViews();

// Cookie-based authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "FaceAttendanceSystem.Auth";
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/Login";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    UseStatusCodesForApiCalls(options.Events);
})
.AddCookie("EmployeeCookie", options =>
{
    options.Cookie.Name = "FaceAttendanceSystem.EmployeeAuth";
    options.LoginPath = "/Employee/Login";
    options.LogoutPath = "/Employee/Logout";
    options.AccessDeniedPath = "/Employee/Login";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    UseStatusCodesForApiCalls(options.Events);
});

// Per-client-IP rate limits. The client IP comes from X-Forwarded-For via
// UseForwardedHeaders (trusted only from the local nginx proxy above).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static RateLimitPartition<string> PerIp(HttpContext ctx, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });

    options.AddPolicy(RateLimitPolicies.Login, ctx => PerIp(ctx, 10));
    options.AddPolicy(RateLimitPolicies.CheckIn, ctx => PerIp(ctx, 30));

    options.OnRejected = async (context, token) =>
    {
        const string message = "Too many attempts. Please wait a minute and try again.";
        if (AuthResponses.IsApiRequest(context.HttpContext.Request))
            await context.HttpContext.Response.WriteAsJsonAsync(new { success = false, message }, token);
        else
            await context.HttpContext.Response.WriteAsync(message, token);
    };
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)));

builder.Services.AddScoped<ImageStorageService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<EmployeeDeletionService>();
builder.Services.AddSingleton<GeofenceService>();

// Attendance configuration (late grace period, etc.)
builder.Services.Configure<AttendanceOptions>(
    builder.Configuration.GetSection(AttendanceOptions.SectionName));

// Face recognition API configuration
builder.Services.Configure<FaceRecognitionOptions>(
    builder.Configuration.GetSection("FaceRecognition"));

builder.Services.AddHttpClient<FaceRecognitionService>(client =>
{
    var apiUrl = builder.Configuration["FaceRecognition:ApiUrl"];
    if (!string.IsNullOrWhiteSpace(apiUrl))
        client.BaseAddress = new Uri(apiUrl);
    client.Timeout = TimeSpan.FromSeconds(30);

    // Shared secret so the Python service only accepts calls from this app.
    var apiKey = builder.Configuration["FaceRecognition:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "FaceAttendanceSystem.Session";
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly = true;
});
var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration["FaceRecognition:ApiKey"]))
    app.Logger.LogWarning("FaceRecognition:ApiKey is not set; the face recognition API will reject every request.");

await AdminBootstrapper.RunAsync(app.Services);

app.UseForwardedHeaders();
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<MustChangePasswordMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// fetch/JSON callers get 401/403 instead of a 302 to the login page, which
// fetch would silently follow and then fail to parse as JSON.
static void UseStatusCodesForApiCalls(CookieAuthenticationEvents events)
{
    events.OnRedirectToLogin = ctx =>
    {
        if (AuthResponses.IsApiRequest(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    events.OnRedirectToAccessDenied = ctx =>
    {
        if (AuthResponses.IsApiRequest(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
}

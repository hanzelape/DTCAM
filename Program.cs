
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
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
})
.AddCookie("EmployeeCookie", options =>
{
    options.Cookie.Name = "FaceAttendanceSystem.EmployeeAuth";
    options.LoginPath = "/Employee/Login";
    options.LogoutPath = "/Employee/Logout";
    options.AccessDeniedPath = "/Employee/Login";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)));

builder.Services.AddScoped<ImageStorageService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<EmployeeDeletionService>();

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
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "FaceAttendanceSystem.Session";
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly = true;
});
var app = builder.Build();
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

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

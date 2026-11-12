using Microsoft.AspNetCore.Hosting;

namespace FaceAttendanceSystem.Services;

public class ImageStorageService
{
    private readonly IWebHostEnvironment _env;

    public ImageStorageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    // Saves a raw byte array (e.g. webcam capture) and returns the relative path
    public async Task<string> SaveCapturedImageAsync(byte[] imageBytes, string subFolder, string extension = ".jpg")
    {
        var folder = Path.Combine(_env.WebRootPath, "uploads", subFolder);
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, fileName);

        await System.IO.File.WriteAllBytesAsync(fullPath, imageBytes);

        return $"/uploads/{subFolder}/{fileName}";
    }
}

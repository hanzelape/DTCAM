using System.Net.Http.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FaceAttendanceSystem.Models;
using Microsoft.Extensions.Options;

namespace FaceAttendanceSystem.Services;

public class FaceRecognitionService
{
    private readonly HttpClient _httpClient;
    private readonly FaceRecognitionOptions _options;
    private readonly ILogger<FaceRecognitionService> _logger;

    public FaceRecognitionService(
        HttpClient httpClient,
        IOptions<FaceRecognitionOptions> options,
        ILogger<FaceRecognitionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    // Submits a registration image and gets a face template (base64 encoding string).
    public async Task<string?> CreateFaceEncodingAsync(byte[] imageBytes)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
            return null;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(fileContent, "image", "capture.jpg");

            var response = await _httpClient.PostAsync($"{_options.ApiUrl.TrimEnd('/')}/encode", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<EncodeResponse>();
            return json?.Encoding;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Face encoding request failed.");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Face encoding request timed out.");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Face encoding API returned invalid JSON.");
            return null;
        }
        finally
        {
            _logger.LogInformation(
                "face_api_operation=encode elapsed_ms={ElapsedMilliseconds} image_bytes={ImageBytes}",
                stopwatch.Elapsed.TotalMilliseconds,
                imageBytes.Length);
        }
    }

    public async Task<FaceEncodingResult> CreateFaceEncodingAndCheckDuplicateAsync(
        byte[] imageBytes,
        IEnumerable<Employee> existingEmployees)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
        {
            const string message = "Face recognition service is not configured.";
            return new FaceEncodingResult(null, RecognitionResult.NotImplemented(), message);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = new MultipartFormDataContent();

            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(fileContent, "image", "capture.jpg");

            var knownFaces = BuildKnownFaces(existingEmployees);
            if (knownFaces.Count > 0)
            {
                var knownContent = new StringContent(JsonSerializer.Serialize(knownFaces), Encoding.UTF8);
                knownContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                content.Add(knownContent, "known_faces");
            }

            var response = await _httpClient.PostAsync($"{_options.ApiUrl.TrimEnd('/')}/encode", content);
            if (!response.IsSuccessStatusCode)
            {
                var apiError = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
                var message = apiError?.Detail ?? $"Face recognition API returned {(int)response.StatusCode}.";
                _logger.LogWarning("Face encoding and duplicate check failed with status {StatusCode}.", (int)response.StatusCode);
                return new FaceEncodingResult(null, RecognitionResult.Failed(message), message);
            }

            var result = await response.Content.ReadFromJsonAsync<RecognizeResponse>();
            if (result == null || string.IsNullOrWhiteSpace(result.Encoding))
            {
                const string message = "Face recognition API returned no encoding.";
                _logger.LogWarning("{Message}", message);
                return new FaceEncodingResult(null, RecognitionResult.Failed(message), message);
            }

            var duplicateCheck = result.IsRecognized && result.Confidence >= _options.MinConfidence
                ? new RecognitionResult
                {
                    IsRecognized = true,
                    EmployeeId = result.EmployeeId,
                    EmployeeName = result.EmployeeName,
                    Confidence = result.Confidence
                }
                : new RecognitionResult { IsRecognized = false, Confidence = result.Confidence };

            return new FaceEncodingResult(result.Encoding, duplicateCheck);
        }
        catch (HttpRequestException ex)
        {
            const string message = "Could not reach the face recognition API.";
            _logger.LogError(ex, "Face encoding and duplicate check request failed.");
            return new FaceEncodingResult(null, RecognitionResult.Failed(message), message);
        }
        catch (TaskCanceledException ex)
        {
            const string message = "The face recognition API timed out. Please try again.";
            _logger.LogError(ex, "Face encoding and duplicate check request timed out.");
            return new FaceEncodingResult(null, RecognitionResult.Failed(message), message);
        }
        catch (JsonException ex)
        {
            const string message = "The face recognition API returned invalid data.";
            _logger.LogError(ex, "Face encoding and duplicate check response could not be parsed.");
            return new FaceEncodingResult(null, RecognitionResult.Failed(message), message);
        }
        finally
        {
            _logger.LogInformation(
                "face_api_operation=encode_with_duplicate_check elapsed_ms={ElapsedMilliseconds} image_bytes={ImageBytes}",
                stopwatch.Elapsed.TotalMilliseconds,
                imageBytes.Length);
        }
    }

    // Submits a webcam image + known faces and receives a recognition result.
    public async Task<RecognitionResult> RecognizeFaceAsync(byte[] imageBytes, IEnumerable<Employee> candidates)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
            return RecognitionResult.NotImplemented();

        try
        {
            using var content = new MultipartFormDataContent();

            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(fileContent, "image", "capture.jpg");

            var knownFaces = BuildKnownFaces(candidates);

            var knownJson = JsonSerializer.Serialize(knownFaces);
            var knownContent = new StringContent(knownJson, Encoding.UTF8);
            knownContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Add(knownContent, "known_faces");

            var response = await _httpClient.PostAsync($"{_options.ApiUrl.TrimEnd('/')}/recognize", content);

            if (!response.IsSuccessStatusCode)
                return RecognitionResult.Failed($"Recognition API returned {(int)response.StatusCode}.");

            var result = await response.Content.ReadFromJsonAsync<RecognizeResponse>();

            if (result == null)
                return RecognitionResult.Failed("Recognition API returned no data.");

            if (result.Confidence < _options.MinConfidence)
                return RecognitionResult.Failed("Identity could not be verified.");

            return new RecognitionResult
            {
                IsRecognized = result.IsRecognized,
                EmployeeId = result.EmployeeId,
                EmployeeName = result.EmployeeName,
                Confidence = result.Confidence
            };
        }
        catch (HttpRequestException ex)
        {
            return RecognitionResult.Failed($"Could not reach recognition API: {ex.Message}");
        }
        catch (Exception ex)
        {
            return RecognitionResult.Failed($"Recognition error: {ex.Message}");
        }
    }

    /// <summary>
    /// Liveness-checked recognition. Sends a burst of frames plus the
    /// server-chosen head-turn direction; the Python API requires a frontal
    /// frame followed by a frame turned that way, all showing the same person,
    /// and identifies that person against <paramref name="candidates"/>.
    /// </summary>
    public async Task<RecognitionResult> RecognizeWithLivenessAsync(
        IReadOnlyList<byte[]> frames,
        string challengeDirection,
        IEnumerable<Employee> candidates)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
            return RecognitionResult.NotImplemented();

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = new MultipartFormDataContent();

            for (var i = 0; i < frames.Count; i++)
            {
                var frameContent = new ByteArrayContent(frames[i]);
                frameContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                content.Add(frameContent, "images", $"frame{i}.jpg");
            }

            content.Add(new StringContent(challengeDirection, Encoding.UTF8), "challenge");

            var knownContent = new StringContent(JsonSerializer.Serialize(BuildKnownFaces(candidates)), Encoding.UTF8);
            knownContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Add(knownContent, "known_faces");

            var response = await _httpClient.PostAsync($"{_options.ApiUrl.TrimEnd('/')}/recognize-live", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Liveness recognition failed with status {StatusCode}.", (int)response.StatusCode);
                return RecognitionResult.Failed("The face recognition service rejected the request. Please try again.");
            }

            var result = await response.Content.ReadFromJsonAsync<LiveRecognizeResponse>();
            if (result == null)
                return RecognitionResult.Failed("Recognition API returned no data.");

            if (!result.LivenessPassed)
                return new RecognitionResult
                {
                    IsRecognized = false,
                    LivenessPassed = false,
                    ErrorMessage = result.LivenessReason ?? "Liveness check failed. Please try again."
                };

            if (!result.IsRecognized || result.Confidence < _options.MinConfidence)
                return new RecognitionResult
                {
                    IsRecognized = false,
                    LivenessPassed = true,
                    Confidence = result.Confidence,
                    ErrorMessage = "Identity could not be verified."
                };

            return new RecognitionResult
            {
                IsRecognized = true,
                LivenessPassed = true,
                EmployeeId = result.EmployeeId,
                EmployeeName = result.EmployeeName,
                Confidence = result.Confidence
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Liveness recognition request failed.");
            return RecognitionResult.Failed("Could not reach the face recognition service.");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Liveness recognition request timed out.");
            return RecognitionResult.Failed("The face recognition service timed out. Please try again.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Liveness recognition response could not be parsed.");
            return RecognitionResult.Failed("The face recognition service returned invalid data.");
        }
        finally
        {
            _logger.LogInformation(
                "face_api_operation=recognize_live elapsed_ms={ElapsedMilliseconds} frames={Frames}",
                stopwatch.Elapsed.TotalMilliseconds,
                frames.Count);
        }
    }

    // Checks whether a new face already exists among registered employees.
    // Returns the matched employee, or null if the face is unknown (unique).
    public async Task<RecognitionResult> CheckDuplicateFaceAsync(byte[] imageBytes, IEnumerable<Employee> existingEmployees)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
            return RecognitionResult.NotImplemented();

        try
        {
            using var content = new MultipartFormDataContent();

            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(fileContent, "image", "capture.jpg");

            var knownFaces = BuildKnownFaces(existingEmployees);

            if (knownFaces.Count == 0)
                return new RecognitionResult { IsRecognized = false };

            var knownJson = JsonSerializer.Serialize(knownFaces);
            var knownContent = new StringContent(knownJson, Encoding.UTF8);
            knownContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Add(knownContent, "known_faces");

            var response = await _httpClient.PostAsync($"{_options.ApiUrl.TrimEnd('/')}/recognize", content);

            if (!response.IsSuccessStatusCode)
                return RecognitionResult.Failed($"Recognition API returned {(int)response.StatusCode}.");

            var result = await response.Content.ReadFromJsonAsync<RecognizeResponse>();

            // A duplicate face is one that matches an existing employee above the threshold.
            if (result != null && result.IsRecognized && result.Confidence >= _options.MinConfidence)
            {
                return new RecognitionResult
                {
                    IsRecognized = true,
                    EmployeeId = result.EmployeeId,
                    EmployeeName = result.EmployeeName,
                    Confidence = result.Confidence
                };
            }

            return new RecognitionResult { IsRecognized = false };
        }
        catch (HttpRequestException)
        {
            return RecognitionResult.Failed("Could not reach recognition API.");
        }
        catch (Exception)
        {
            return RecognitionResult.Failed("Recognition error during duplicate check.");
        }
    }

    private static List<KnownFace> BuildKnownFaces(IEnumerable<Employee> employees)
    {
        var knownFaces = new List<KnownFace>();

        foreach (var p in employees)
        {
            if (string.IsNullOrWhiteSpace(p.FaceEncoding))
                continue;

            var encodings = new List<string>();

            if (!string.IsNullOrWhiteSpace(p.FaceEncodings))
            {
                try
                {
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.FaceEncodings);
                    if (parsed != null && parsed.Any())
                    {
                        encodings.AddRange(parsed);
                    }
                }
                catch
                {
                    // ignore parse errors
                }
            }

            if (!encodings.Any())
            {
                encodings.Add(p.FaceEncoding);
            }

            foreach (var encoding in encodings)
            {
                knownFaces.Add(new KnownFace
                {
                    employee_id = p.Id,
                    employee_name = p.FullName,
                    encoding = encoding
                });
            }
        }

        return knownFaces;
    }

    // Confirms that the Python service is available.
    public async Task<bool> CheckApiHealthAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
            return false;

        try
        {
            var response = await _httpClient.GetAsync($"{_options.ApiUrl.TrimEnd('/')}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private class EncodeResponse
    {
        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }
    }

    private class RecognizeResponse
    {
        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }

        [JsonPropertyName("recognized")]
        public bool IsRecognized { get; set; }

        [JsonPropertyName("employee_id")]
        public int? EmployeeId { get; set; }

        [JsonPropertyName("employee_name")]
        public string? EmployeeName { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }

    private class LiveRecognizeResponse : RecognizeResponse
    {
        [JsonPropertyName("liveness_passed")]
        public bool LivenessPassed { get; set; }

        [JsonPropertyName("liveness_reason")]
        public string? LivenessReason { get; set; }
    }

    private class ApiErrorResponse
    {
        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }

    private class KnownFace
    {
        [JsonPropertyName("employee_id")]
        public int employee_id { get; set; }

        [JsonPropertyName("employee_name")]
        public string employee_name { get; set; } = string.Empty;

        [JsonPropertyName("encoding")]
        public string encoding { get; set; } = string.Empty;
    }
}

public sealed record FaceEncodingResult(
    string? Encoding,
    RecognitionResult DuplicateCheck,
    string? ErrorMessage = null);

public class FaceRecognitionOptions
{
    public string? ApiUrl { get; set; }
    public double MinConfidence { get; set; } = 0.45;
    public long MaxUploadSizeKB { get; set; } = 2048;
    public string? UploadDirectory { get; set; }

    /// <summary>Shared secret sent as X-Api-Key; the Python API rejects requests without it.</summary>
    public string? ApiKey { get; set; }
}

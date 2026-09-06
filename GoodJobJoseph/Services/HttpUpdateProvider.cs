using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JosephExperience.Services;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

/// <summary>
/// Fetches update manifests from a static HTTPS JSON endpoint (or GitHub Releases JSON).
/// This is the "read-only" provider that never writes to the client.
/// </summary>
public class HttpUpdateProvider : UpdateProvider, IUpdateProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _manifestUrl;
    private DateTime _lastCheck = DateTime.MinValue;
    private static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromHours(12);

    /// <summary>Constructs the provider with the manifest URL.</summary>
    /// <param name="manifestUrl">Publicly accessible JSON manifest URL.</param>
    public HttpUpdateProvider(string manifestUrl)
    {
        _manifestUrl = manifestUrl;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("JosephExperience-Updater", "1.0"));
    }

    /// <summary>Minimum interval between automatic checks.</summary>
    public DateTime LastCheck
    {
        get => _lastCheck;
        set => _lastCheck = value;
    }

    /// <summary>Check for updates asynchronously.</summary>
    public override async System.Threading.Tasks.Task<UpdateCheckResultData> CheckForUpdatesAsync(
        SemanticVersion currentVersion,
        System.Threading.CancellationToken cancellationToken = default)
    {
        UpdateCheckResultData result = new UpdateCheckResultData { CurrentVersion = currentVersion.ToString() };

        try
        {
            var json = await _httpClient.GetStringAsync(_manifestUrl, cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;

            string? remoteVersion = root.GetProperty("version").GetString();
            string? minimumSupportedVersion = root.GetProperty("minimumSupportedVersion").GetString();
            string? downloadUrl = root.GetProperty("downloadUrl").GetString();
            string? sha256 = root.GetProperty("sha256").GetString();
            long? fileSize = root.GetProperty("fileSize").GetInt64();
            string? releaseDateUtc = root.GetProperty("releaseDateUtc").GetString();
            bool mandatory = root.GetProperty("mandatory").GetBoolean();

            string? releaseNotes = null;
            if (root.TryGetProperty("releaseNotes", out var notesElement))
            {
                releaseNotes = notesElement.ValueKind == JsonValueKind.Array
                    ? string.Join("\n", notesElement.EnumerateArray().Select(e => e.GetString() ?? ""))
                    : notesElement.GetRawText();
            }

            if (string.IsNullOrEmpty(remoteVersion))
            {
                result.Result = UpdateCheckResult.ManifestInvalid;
                return result;
            }

            var remoteVer = SemanticVersion.Parse(remoteVersion);
            var current = currentVersion;

            int comparison = SemanticVersion.Compare(remoteVer, current);

            if (comparison < 0)
            {
                result.Result = UpdateCheckResult.UpToDate;
                result.RemoteVersion = remoteVersion;
            }
            else if (comparison > 0)
            {
                result.Result = UpdateCheckResult.UpdateAvailable;
                result.IsMandatory = mandatory;
                result.RemoteVersion = remoteVersion;
                result.ReleaseNotes = releaseNotes;
                result.DownloadUrl = downloadUrl;
                result.Sha256 = sha256;
                result.ReleaseDate = releaseDateUtc;
                result.DownloadSize = fileSize;
                result.MinimumSupportedVersion = minimumSupportedVersion;
            }
            else
            {
                result.Result = UpdateCheckResult.UpToDate;
            }

            _lastCheck = DateTime.UtcNow;
        }
        catch (JsonException)
        {
            result.Result = UpdateCheckResult.ManifestInvalid;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Warn($"HttpUpdateProvider.CheckForUpdatesAsync failed: {ex.Message}");
            result.Result = UpdateCheckResult.NetworkUnavailable;
        }

        return result;
    }

    /// <summary>Download the update asynchronously with progress reporting.</summary>
    public override async System.Threading.Tasks.Task<UpdateDownloadProgress?> DownloadUpdateAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<UpdateDownloadProgress>? progress,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var progressResult = new UpdateDownloadProgress();

        try
        {
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new System.IO.FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

            var buffer = new byte[8192];
            long downloadedBytes = 0;
            var lastReport = DateTime.UtcNow;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                var now = DateTime.UtcNow;
                if (now - lastReport >= TimeSpan.FromMilliseconds(200) || downloadedBytes >= totalBytes && totalBytes > 0)
                {
                    lastReport = now;
                    if (progress != null && totalBytes > 0)
                    {
                        var elapsed = stopwatch.Elapsed;
                        var speed = elapsed.TotalSeconds > 0 ? downloadedBytes / elapsed.TotalSeconds : 0;
                        var eta = speed > 0 ? TimeSpan.FromSeconds((totalBytes - downloadedBytes) / speed) : TimeSpan.Zero;

                        var report = new UpdateDownloadProgress
                        {
                            Percentage = (int)(downloadedBytes * 100 / totalBytes),
                            DownloadedBytes = downloadedBytes,
                            TotalBytes = totalBytes,
                            SpeedBytesPerSecond = speed,
                            TimeRemainingSeconds = (int)eta.TotalSeconds
                        };
                        progress.Report(report);
                        progressResult.Percentage = report.Percentage;
                        progressResult.DownloadedBytes = report.DownloadedBytes;
                        progressResult.TotalBytes = report.TotalBytes;
                        progressResult.SpeedBytesPerSecond = report.SpeedBytesPerSecond;
                        progressResult.TimeRemainingSeconds = report.TimeRemainingSeconds;
                    }
                }
            }

            await fileStream.FlushAsync(cancellationToken);

            progressResult.Percentage = 100;
            progressResult.DownloadedBytes = downloadedBytes;
            progressResult.TotalBytes = totalBytes;
            if (progress != null)
                progress.Report(progressResult);

            return progressResult;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Warn($"HttpUpdateProvider.DownloadUpdateAsync failed: {ex.Message}");
            try { System.IO.File.Delete(destinationPath); } catch { }
            return null;
        }
    }

    /// <summary>Verify the integrity of a downloaded file against a manifest SHA-256.</summary>
    public override bool VerifyIntegrity(string filePath, string expectedSha256)
    {
        try
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            var actualHash = Convert.ToHexString(hash).ToLowerInvariant();
            return string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Local file-based update provider for staging and testing.
/// Reads manifests and downloads from a local directory.
/// </summary>
public class LocalUpdateProvider : UpdateProvider
{
    private readonly string _localUpdateDir;

    public LocalUpdateProvider(string localUpdateDir)
    {
        _localUpdateDir = localUpdateDir;
    }

    public override async Task<UpdateCheckResultData> CheckForUpdatesAsync(
        SemanticVersion currentVersion, CancellationToken cancellationToken = default)
    {
        var result = new UpdateCheckResultData { CurrentVersion = currentVersion.ToString() };

        try
        {
            var manifestPath = System.IO.Path.Combine(_localUpdateDir, "update-manifest.json");
            if (!File.Exists(manifestPath))
            {
                result.Result = UpdateCheckResult.NetworkUnavailable;
                return result;
            }

            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? remoteVersion = root.GetProperty("version").GetString();
            if (string.IsNullOrEmpty(remoteVersion))
            {
                result.Result = UpdateCheckResult.ManifestInvalid;
                return result;
            }

            var remoteVer = SemanticVersion.Parse(remoteVersion);
            int comparison = SemanticVersion.Compare(remoteVer, currentVersion);

            if (comparison > 0)
            {
                result.Result = UpdateCheckResult.UpdateAvailable;
                result.RemoteVersion = remoteVersion;
                result.IsMandatory = root.GetProperty("mandatory").GetBoolean();
                if (root.TryGetProperty("releaseNotes", out var notesElement) && notesElement.ValueKind == JsonValueKind.Array)
                    result.ReleaseNotes = string.Join("\n", notesElement.EnumerateArray().Select(e => e.GetString() ?? ""));
                if (root.TryGetProperty("downloadUrl", out var urlElement))
                    result.DownloadUrl = urlElement.GetString();
                if (root.TryGetProperty("sha256", out var shaElement))
                    result.Sha256 = shaElement.GetString();
                result.DownloadSize = root.GetProperty("fileSize").GetInt64();
            }
            else
            {
                result.Result = UpdateCheckResult.UpToDate;
                result.RemoteVersion = remoteVersion;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"LocalUpdateProvider.CheckForUpdatesAsync failed: {ex.Message}");
            result.Result = UpdateCheckResult.NetworkUnavailable;
        }

        return result;
    }

    public override async Task<UpdateDownloadProgress?> DownloadUpdateAsync(
        string downloadUrl, string destinationPath, IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var progressResult = new UpdateDownloadProgress();
        try
        {
            var sourcePath = downloadUrl.StartsWith(_localUpdateDir, StringComparison.OrdinalIgnoreCase)
                ? downloadUrl
                : System.IO.Path.Combine(_localUpdateDir, System.IO.Path.GetFileName(downloadUrl));

            if (!File.Exists(sourcePath))
            {
                AppLog.Warn($"Local update artifact not found: {sourcePath}");
                return null;
            }

            var totalBytes = new FileInfo(sourcePath).Length;
            var buffer = new byte[8192];
            using var source = File.OpenRead(sourcePath);
            using var dest = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (progress != null)
                {
                    var report = new UpdateDownloadProgress
                    {
                        Percentage = (int)(downloaded * 100 / totalBytes),
                        DownloadedBytes = downloaded,
                        TotalBytes = totalBytes
                    };
                    progress.Report(report);
                    progressResult.Percentage = report.Percentage;
                    progressResult.DownloadedBytes = report.DownloadedBytes;
                    progressResult.TotalBytes = report.TotalBytes;
                }
            }

            progressResult.Percentage = 100;
            progressResult.DownloadedBytes = totalBytes;
            progressResult.TotalBytes = totalBytes;
            if (progress != null) progress.Report(progressResult);
            return progressResult;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"LocalUpdateProvider.DownloadUpdateAsync failed: {ex.Message}");
            try { File.Delete(destinationPath); } catch { }
            return null;
        }
    }

    public override bool VerifyIntegrity(string filePath, string expectedSha256)
    {
        try
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            var actualHash = Convert.ToHexString(hash).ToLowerInvariant();
            return string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
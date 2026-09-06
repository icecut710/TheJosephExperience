using System.Text.Json.Serialization;

namespace JosephExperience.Services;

/// <summary>
/// Remote update manifest hosted on a public endpoint.
/// This describes what's available for download and is signed/read-only.
/// </summary>
public class UpdateManifest
{
    /// <summary>The version this manifest describes.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>The minimum version that can safely install this update.</summary>
    [JsonPropertyName("minimumSupportedVersion")]
    public string MinimumSupportedVersion { get; set; } = "";

    /// <summary>URL where the update executable can be downloaded.</summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    /// <summary>SHA-256 hash of the downloaded executable for integrity verification.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    /// <summary>File size of the update executable in bytes.</summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>Release date and time in UTC.</summary>
    [JsonPropertyName("releaseDateUtc")]
    public string ReleaseDateUtc { get; set; } = "";

    /// <summary>Human-readable release notes.</summary>
    [JsonPropertyName("releaseNotes")]
    public string[]? ReleaseNotes { get; set; }

    /// <summary>Whether this update is mandatory for the user.</summary>
    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }

    /// <summary> whether this update is a release-candidate or beta.</summary>
    [JsonPropertyName("releaseType")]
    public string? ReleaseType { get; set; }
}

/// <summary>
/// Result of checking for an update, including version comparison and manifest data.
/// </summary>
public class UpdateCheckResultData
{
    /// <summary>The current app version string.</summary>
    public string CurrentVersion { get; set; } = "";

    /// <summary>The remote manifest version string, if available.</summary>
    public string? RemoteVersion { get; set; }

    /// <summary>Whether an update is available.</summary>
    public bool HasUpdate => Result != UpdateCheckResult.UpToDate && Result != UpdateCheckResult.NetworkUnavailable;

    /// <summary>Whether the update is mandatory.</summary>
    public bool IsMandatory { get; set; }

    /// <summary>The underlying check result.</summary>
    public UpdateCheckResult Result { get; set; }

    /// <summary>Release notes from the manifest.</summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>Download URL for the update package.</summary>
    public string? DownloadUrl { get; set; }

    /// <summary>SHA-256 hash of the update package for integrity verification.</summary>
    public string? Sha256 { get; set; }

    /// <summary>Release date from the manifest.</summary>
    public string? ReleaseDate { get; set; }

    /// <summary>Download size from the manifest.</summary>
    public long? DownloadSize { get; set; }

    /// <summary>Minimum supported version.</summary>
    public string? MinimumSupportedVersion { get; set; }

    /// <summary>Constructs a result with the given check result.</summary>
    public UpdateCheckResultData(UpdateCheckResultData checkResult)
    {
        CurrentVersion = checkResult.CurrentVersion;
        RemoteVersion = checkResult.RemoteVersion;
        Result = checkResult.Result;
        IsMandatory = checkResult.IsMandatory;
        ReleaseNotes = checkResult.ReleaseNotes;
        DownloadUrl = checkResult.DownloadUrl;
        Sha256 = checkResult.Sha256;
        ReleaseDate = checkResult.ReleaseDate;
        DownloadSize = checkResult.DownloadSize;
        MinimumSupportedVersion = checkResult.MinimumSupportedVersion;
    }

    /// <summary>Constructs a result.</summary>
    public UpdateCheckResultData()
    {
    }
}

/// <summary>
/// Abstract base class for update providers.
/// Allows substituting different update sources (GitHub, static file, etc.)
/// without changing the UpdateService internals.
/// </summary>
public abstract class UpdateProvider : IUpdateProvider
{
    /// <summary>Check for updates asynchronously.</summary>
    /// <param>cancellationToken">to allow cancelling the check.</summary>
    public abstract System.Threading.Tasks.Task<UpdateCheckResultData> CheckForUpdatesAsync(
        SemanticVersion currentVersion,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>Download the update asynchronously.</summary>
    public abstract System.Threading.Tasks.Task<UpdateDownloadProgress?> DownloadUpdateAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<UpdateDownloadProgress>? progress,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>Verify the integrity of a downloaded file against a manifest SHA-256.</summary>
    public abstract bool VerifyIntegrity(string filePath, string expectedSha256);
}

/// <summary>
/// Provides an abstraction for remote update manifest and artifact retrieval.
/// Implementations include HTTP endpoints, GitHub Releases, and local file-based providers.
/// </summary>
public interface IUpdateProvider
{
    /// <summary>Check for updates against the current version.</summary>
    Task<UpdateCheckResultData> CheckForUpdatesAsync(SemanticVersion currentVersion, CancellationToken cancellationToken = default);

    /// <summary>Download an update artifact to the given destination path.</summary>
    Task<UpdateDownloadProgress?> DownloadUpdateAsync(
        string downloadUrl, string destinationPath, IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken = default);

    /// <summary>Verify the SHA-256 integrity of a downloaded file.</summary>
    bool VerifyIntegrity(string filePath, string expectedSha256);
}
using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace JosephExperience.Services;

/// <summary>
/// Centralized version management with semantic version comparison.
/// Uses the assembly's actual version as the source of truth.
/// </summary>
public readonly struct SemanticVersion : IEquatable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>
    /// Parse from version string like "1.2.3"
    /// </summary>
    public static SemanticVersion Parse(string version)
    {
        var parts = version.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            throw new FormatException($"Invalid semantic version format: '{version}'. Expected major.minor.patch");
        }
        return new SemanticVersion(major, minor, patch);
    }

    /// <summary>
    /// Compare two semantic versions.
    /// Returns: -1 if left < right, 0 if equal, 1 if left > right
    /// </summary>
    public static int Compare(SemanticVersion left, SemanticVersion right)
    {
        if (left.Major != right.Major)
            return left.Major > right.Major ? 1 : -1;
        if (left.Minor != right.Minor)
            return left.Minor > right.Minor ? 1 : -1;
        return left.Patch.CompareTo(right.Patch);
    }

    public int CompareTo(SemanticVersion other) => Compare(this, other);

    public bool Equals(SemanticVersion other) => Compare(this, other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => (Major, Minor, Patch).GetHashCode();

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    /// <summary>
    /// Check if this version is greater than the specified version.
    /// </summary>
    public bool IsGreaterThan(SemanticVersion other) => Compare(this, other) > 0;

    /// <summary>
    /// Check if this version is greater than or equal to the specified version.
    /// </summary>
    public bool IsGreaterThanOrEqualTo(SemanticVersion other) => Compare(this, other) >= 0;

    /// <summary>
    /// Check if this version is less than the specified version.
    /// </summary>
    public bool IsLessThan(SemanticVersion other) => Compare(this, other) < 0;

    /// <summary>
    /// Check if this version is less than or equal to the specified version.
    /// </summary>
    public bool IsLessThanOrEqualTo(SemanticVersion other) => Compare(this, other) <= 0;
}

/// <summary>
/// Wraps the application's actual version from the assembly.
/// </summary>
public static class CurrentVersionService
{
    /// <summary>
    /// Gets the current app version from the assembly informational version.
    /// Falls back to parsing if assembly version is not available.
    /// </summary>
    public static SemanticVersion Get()
    {
        try
        {
            var attr = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
            if (attr != null)
            {
                return SemanticVersion.Parse(attr.InformationalVersion);
            }
        }
        catch
        {
            // ignored
        }

        // Fallback: try to read from the project version
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(
                AppContext.BaseDirectory + "JosephExperience.exe");
            if (!string.IsNullOrEmpty(info.ProductVersion))
            {
                return SemanticVersion.Parse(info.ProductVersion);
            }
        }
        catch { }

        // Last resort: return a default
        return new SemanticVersion(0, 0, 0);
    }

    /// <summary>
    /// Gets the current app version as a string.
    /// </summary>
    public static string GetString()
    {
        return Get().ToString();
    }
}

/// <summary>
/// Provides information about update check results.
/// </summary>
public enum UpdateCheckResult
{
    /// <summary>The app is up to date.</summary>
    UpToDate,

    /// <summary>A newer update is available.</summary>
    UpdateAvailable,

    /// <summary>A mandatory update is available.</summary>
    MandatoryUpdateAvailable,

    /// <summary>Network is unavailable.</summary>
    NetworkUnavailable,

    /// <summary>The update manifest is invalid or could not be parsed.</summary>
    ManifestInvalid,

    /// <summary>The download is unavailable.</summary>
    DownloadUnavailable,

    /// <summary>An unexpected error occurred.</summary>
    Error
}

/// <summary>
/// Holds the result of an update check, including version comparison and manifest info.
/// </summary>
public class UpdateCheckResultWrapper
{
    /// <summary>The result type.</summary>
    public UpdateCheckResult Result { get; set; }

    /// <summary>The current app version.</summary>
    public string CurrentVersion { get; set; } = "";

    /// <summary>The remote manifest version, if available.</summary>
    public string? RemoteVersion { get; set; }

    /// <summary>Release notes from the manifest, if available.</summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>Release date from the manifest, if available.</summary>
    public string? ReleaseDate { get; set; }

    /// <summary>Download size from the manifest, if available.</summary>
    public long? DownloadSize { get; set; }

    /// <summary>Minimum supported version from the manifest, if available.</summary>
    public string? MinimumSupportedVersion { get; set; }

    /// <summary>Whether the update is mandatory.</summary>
    public bool IsMandatory { get; set; }
}

/// <summary>
/// Holds progress information for an update download.
/// </summary>
public class UpdateDownloadProgress
{
    /// <summary>Percentage complete (0-100).</summary>
    public int Percentage { get; set; }

    /// <summary>Downloaded bytes.</summary>
    public long DownloadedBytes { get; set; }

    /// <summary>Total bytes.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Download speed in bytes per second.</summary>
    public double? SpeedBytesPerSecond { get; set; }

    /// <summary>Estimated time remaining in seconds.</summary>
    public int? TimeRemainingSeconds { get; set; }
}

/// <summary>
/// Result of attempting to install/update the application.
/// </summary>
public enum UpdateInstallResult
{
    /// <summary>Installation succeeded.</summary>
    Success,

    /// <summary>Installation was cancelled.</summary>
    Cancelled,

    /// <summary>Verification failed (hash mismatch).</summary>
    VerificationFailed,

    /// <summary>Installation failed.</summary>
    Failed,

    /// <summary>No update was available.</summary>
    NoUpdateAvailable,

    /// <summary>The app is up to date.</summary>
    UpToDate
}

/// <summary>
/// Arguments for update check progress events.
/// </summary>
public class UpdateCheckEventArgs : EventArgs
{
    /// <summary>Current progress state.</summary>
    public UpdateCheckResult Result { get; set; }

    /// <summary>Current version.</summary>
    public string CurrentVersion { get; set; } = "";

    /// <summary>Remote version, if available.</summary>
    public string? RemoteVersion { get; set; }

    /// <summary>Release notes, if available.</summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>Release date, if available.</summary>
    public string? ReleaseDate { get; set; }

    /// <summary>Download size, if available.</summary>
    public long? DownloadSize { get; set; }

    /// <summary>Whether update is mandatory.</summary>
    public bool IsMandatory { get; set; }
}

/// <summary>
/// Provides a centralized, testable way to determine the current app version
/// and compare it against remote manifests.
/// </summary>
public interface IVersionService
{
    /// <summary>Gets the current app version as a SemanticVersion.</summary>
    SemanticVersion CurrentVersion { get; }

    /// <summary>Gets the current app version as a formatted string.</summary>
    string CurrentVersionString { get; }

    /// <summary>Compares two semantic versions.</summary>
    /// <returns>-1 if left < right, 0 if equal, 1 if left > right</summary>
    int CompareVersions(SemanticVersion left, SemanticVersion right);

    /// <summary>Checks if the left version is greater than the right.</summary>
    bool IsVersionGreaterThan(SemanticVersion left, SemanticVersion right);

    /// <summary>Checks if the left version is less than the right.</summary>
    bool IsVersionLessThan(SemanticVersion left, SemanticVersion right);
}

/// <summary>
/// Default implementation of IVersionService using assembly metadata.
/// </summary>
public class VersionService : IVersionService
{
    private readonly Lazy<SemanticVersion> _currentVersion;

    public VersionService()
    {
        _currentVersion = new Lazy<SemanticVersion>(() => CurrentVersionService.Get());
    }

    public SemanticVersion CurrentVersion => _currentVersion.Value;

    public string CurrentVersionString => _currentVersion.Value.ToString();

    public int CompareVersions(SemanticVersion left, SemanticVersion right)
        => SemanticVersion.Compare(left, right);

    public bool IsVersionGreaterThan(SemanticVersion left, SemanticVersion right)
        => SemanticVersion.Compare(left, right) > 0;

    public bool IsVersionLessThan(SemanticVersion left, SemanticVersion right)
        => SemanticVersion.Compare(left, right) < 0;
}
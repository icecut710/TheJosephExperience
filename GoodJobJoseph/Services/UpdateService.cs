using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JosephExperience.Models;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

/// <summary>
/// Orchestrates the full update lifecycle:
/// Check for updates → Download → Verify integrity → Prepare installation → Launch updater helper.
/// Does NOT overwrite the running executable directly; uses a helper process.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly IVersionService _versionService;
    private readonly IUpdateProvider _updateProvider;
    private readonly string _appDataPath;
    private readonly string _appExePath;
    private readonly string _updateTempBasePath;

    /// <summary>Current state of the update process.</summary>
    public enum UpdateState
    {
        Idle,
        Checking,
        UpToDate,
        Available,
        Downloading,
        Verifying,
        ReadyToInstall,
        Installing,
        Failed
    }

    /// <summary>Event raised when state changes.</summary>
    public event Action<UpdateState>? StateChanged;

    /// <summary>Event raised when download progress changes.</summary>
    public event Action<UpdateDownloadProgress>? DownloadProgressChanged;

    /// <summary>Event raised when update check completes.</summary>
    public event Action<UpdateCheckResultData>? CheckCompleted;

    /// <summary>Current update state.</summary>
    public UpdateState CurrentState { get; private set; } = UpdateState.Idle;

    /// <summary>Path to the downloaded (and verified) update package, if any.</summary>
    public string? StagedPackagePath { get; private set; }

    /// <summary>SHA-256 of the staged package.</summary>
    public string? StagedSha256 { get; private set; }

    /// <summary>True if an update check or download is currently in progress.</summary>
    public bool IsBusy => CurrentState != UpdateState.Idle && CurrentState != UpdateState.UpToDate;

    /// <summary>
    /// Constructs the update service.
    /// </summary>
    /// <param name="versionService">The version service for current version info.</param>
    /// <param name="updateProvider">The provider for remote manifest and download.</param>
    /// <param name="appDataPath">Path to the app's LocalAppData directory.</param>
    /// <param name="appExePath">Path to the currently running executable.</param>
    public UpdateService(
        IVersionService versionService,
        IUpdateProvider updateProvider,
        string appDataPath,
        string appExePath)
    {
        _versionService = versionService;
        _updateProvider = updateProvider;
        _appDataPath = appDataPath;
        _appExePath = appExePath;
        _updateTempBasePath = Path.Combine(appDataPath, "JosephExperience", "updates");
    }

    /// <summary>Gets the current app version.</summary>
    public SemanticVersion CurrentVersion => _versionService.CurrentVersion;

    /// <summary>Gets the current app version as a string.</summary>
    public string CurrentVersionString => _versionService.CurrentVersionString;

    /// <summary>Gets the base path where update files are stored.</summary>
    public string UpdateTempBasePath => _updateTempBasePath;

    private void SetState(UpdateState state)
    {
        if (CurrentState == state) return;
        CurrentState = state;
        StateChanged?.Invoke(CurrentState);
    }

    /// <summary>Check for updates asynchronously.</summary>
    public async Task<UpdateCheckResultData> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_updateTempBasePath);

        SetState(UpdateState.Checking);

        var currentVersion = _versionService.CurrentVersion;
        UpdateCheckResultData checkResult;

        try
        {
            checkResult = await _updateProvider.CheckForUpdatesAsync(currentVersion, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SetState(UpdateState.Idle);
            return new UpdateCheckResultData { CurrentVersion = currentVersion.ToString(), Result = UpdateCheckResult.NetworkUnavailable };
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Update check failed: {ex.Message}");
            SetState(UpdateState.Failed);
            return new UpdateCheckResultData { CurrentVersion = currentVersion.ToString(), Result = UpdateCheckResult.Error };
        }

        checkResult.CurrentVersion = currentVersion.ToString();
        CheckCompleted?.Invoke(checkResult);

        switch (checkResult.Result)
        {
            case UpdateCheckResult.UpToDate:
                SetState(UpdateState.UpToDate);
                break;
            case UpdateCheckResult.UpdateAvailable:
                SetState(UpdateState.Available);
                break;
            case UpdateCheckResult.NetworkUnavailable:
            case UpdateCheckResult.ManifestInvalid:
                SetState(UpdateState.Failed);
                break;
        }

        return checkResult;
    }

    /// <summary>Download the update asynchronously with SHA-256 verification.</summary>
    public async Task<UpdateDownloadProgress?> DownloadUpdateAsync(
        string downloadUrl,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        SetState(UpdateState.Downloading);

        var versionFolder = Guid.NewGuid().ToString();
        var versionDir = Path.Combine(_updateTempBasePath, versionFolder);
        var destinationPath = Path.Combine(versionDir, "TheJosephExperience.zip");

        Directory.CreateDirectory(versionDir);

        var progress = new UpdateDownloadProgress();
        var progressReporter = new Progress<UpdateDownloadProgress>(p =>
        {
            progress.Percentage = p.Percentage;
            progress.DownloadedBytes = p.DownloadedBytes;
            progress.TotalBytes = p.TotalBytes;
            progress.SpeedBytesPerSecond = p.SpeedBytesPerSecond;
            progress.TimeRemainingSeconds = p.TimeRemainingSeconds;
            DownloadProgressChanged?.Invoke(p);
        });

        var result = await _updateProvider.DownloadUpdateAsync(downloadUrl, destinationPath, progressReporter, cancellationToken);

        if (result is null || !File.Exists(destinationPath))
        {
            SetState(UpdateState.Failed);
            return result;
        }

        // SHA-256 verification of downloaded file
        SetState(UpdateState.Verifying);
        if (!VerifyDownloadedUpdate(destinationPath, expectedSha256))
        {
            try { File.Delete(destinationPath); } catch { }
            SetState(UpdateState.Failed);
            return result;
        }

        // Store staging metadata
        StagedPackagePath = destinationPath;
        StagedSha256 = expectedSha256;
        SetState(UpdateState.ReadyToInstall);
        return result;
    }

    /// <summary>Verify the SHA-256 integrity of a downloaded file.</summary>
    public bool VerifyDownloadedUpdate(string downloadedPath, string expectedSha256)
    {
        if (!File.Exists(downloadedPath))
            return false;

        SetState(UpdateState.Verifying);

        var isValid = _updateProvider.VerifyIntegrity(downloadedPath, expectedSha256);

        if (isValid)
        {
            AppLog.Info($"Update package verified: {downloadedPath} (SHA-256 match)");
        }
        else
        {
            AppLog.Warn($"Update package SHA-256 mismatch: {downloadedPath}");
        }

        return isValid;
    }

    /// <summary>
    /// Validates the staged update package (zip) and extracts it to a staging directory.
    /// Returns the path to the extracted contents, or null if validation/extraction failed.
    /// </summary>
    public string? ValidateAndExtractStagedPackage()
    {
        if (StagedPackagePath is null || !File.Exists(StagedPackagePath))
            return null;

        var extractDir = Path.Combine(_updateTempBasePath, Path.GetFileNameWithoutExtension(StagedPackagePath) + "_extracted");

        try
        {
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            using (var zip = System.IO.Compression.ZipFile.OpenRead(StagedPackagePath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.FullName)) continue;

                    var targetPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                    if (!targetPath.StartsWith(extractDir, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Archive entry escapes target directory: {entry.FullName}");

                    var entryDir = Path.GetDirectoryName(targetPath);
                    if (entryDir != null)
                        Directory.CreateDirectory(entryDir);

                    if (!entry.FullName.EndsWith('/'))
                        entry.ExtractToFile(targetPath, overwrite: true);
                }
            }

            AppLog.Info($"Staged update package extracted to: {extractDir}");
            return extractDir;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to extract staged package: {ex.Message}");
            try { Directory.Delete(extractDir, true); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Prepares installation by creating a backup of the current executable.
    /// The actual replacement is deferred to the updater helper process.
    /// Returns the backup path, or null if backup failed.
    /// </summary>
    public string? PrepareInstall()
    {
        if (StagedPackagePath is null)
            return null;

        SetState(UpdateState.ReadyToInstall);

        var backupPath = _appExePath + ".old";
        try
        {
            if (File.Exists(_appExePath))
                File.Copy(_appExePath, backupPath, overwrite: true);
            return backupPath;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to create backup: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Launches the updater helper to perform the actual file swap, backup restoration, or rollback.
    /// </summary>
    public bool LaunchUpdaterHelper(int currentProcessId, string targetExePath, string newExePath, bool rollback = false)
    {
        SetState(UpdateState.Installing);

        try
        {
            var updaterPath = Path.Combine(AppContext.BaseDirectory, "JosephExperience.Updater.exe");
            var action = rollback ? "rollback" : "install";
            var args = $"--current-pid {currentProcessId} --source \"{newExePath}\" --target \"{targetExePath}\" --action {action}";

            if (System.IO.File.Exists(updaterPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = args,
                    UseShellExecute = true
                });
                return true;
            }
            else
            {
                AppLog.Warn($"Updater helper not found at: {updaterPath}");
                SetState(UpdateState.Failed);
                return false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to launch updater helper: {ex.Message}");
            SetState(UpdateState.Failed);
            return false;
        }
    }

    /// <summary>
    /// Attempts rollback to the previous version using the .old backup.
    /// </summary>
    public bool AttemptRollback()
    {
        var backupPath = _appExePath + ".old";
        if (!File.Exists(backupPath))
        {
            AppLog.Warn("Rollback requested but no backup exists.");
            return false;
        }

        try
        {
            // Launch updater helper in rollback mode
            SetState(UpdateState.Installing);
            var pid = Environment.ProcessId;
            return LaunchUpdaterHelper(pid, _appExePath, backupPath, rollback: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Rollback failed: {ex.Message}");
            SetState(UpdateState.Failed);
            return false;
        }
    }

    /// <summary>Clean up stale update files after a successful installation.</summary>
    public void CleanupStaleUpdates()
    {
        try
        {
            if (Directory.Exists(_updateTempBasePath))
            {
                var directories = Directory.GetDirectories(_updateTempBasePath);
                foreach (var dir in directories)
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }

            // Clear staging metadata
            StagedPackagePath = null;
            StagedSha256 = null;
        }
        catch { }
    }
}

/// <summary>
/// Provides an abstraction for the application's update service.
/// </summary>
public interface IUpdateService
{
    event Action<UpdateService.UpdateState>? StateChanged;
    event Action<UpdateDownloadProgress>? DownloadProgressChanged;
    event Action<UpdateCheckResultData>? CheckCompleted;

    UpdateService.UpdateState CurrentState { get; }
    bool IsBusy { get; }
    string? StagedPackagePath { get; }
    string? StagedSha256 { get; }

    Task<UpdateCheckResultData> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<UpdateDownloadProgress?> DownloadUpdateAsync(string downloadUrl, string expectedSha256, CancellationToken cancellationToken = default);
    bool VerifyDownloadedUpdate(string downloadedPath, string expectedSha256);
    string? ValidateAndExtractStagedPackage();
    string? PrepareInstall();
    bool LaunchUpdaterHelper(int currentProcessId, string targetExePath, string newExePath, bool rollback = false);
    bool AttemptRollback();
    void CleanupStaleUpdates();
}
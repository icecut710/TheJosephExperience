using System.IO;
using System.Runtime.Versioning;
using JosephExperience.Utilities;
using Microsoft.Win32;

namespace JosephExperience.Services.CounterStrike;

public enum GsiConfigInstallStatus
{
    Installed,
    AlreadyValid,
    NeedsRepair,
    Cs2NotFound,
    FolderNotFound,
    WriteFailed
}

public sealed record GsiConfigInstallResult(
    GsiConfigInstallStatus Status,
    string Message,
    string? ConfigPath,
    string? CfgFolder)
{
    public bool Success => Status is GsiConfigInstallStatus.Installed or GsiConfigInstallStatus.AlreadyValid;
}

public interface IGsiConfigManager
{
    /// <summary>Name of the config file Good Job, Joseph! owns. Never touches other GSI configs.</summary>
    string ConfigFileName { get; }
    /// <summary>Locates the CS2 (or legacy CS:GO) csgo/cfg folder, honoring a manual override.</summary>
    string? FindConfigFolder(string? manualOverridePath);
    /// <summary>Builds the cfg contents for the given endpoint + auth token.</summary>
    string BuildConfig(int port, string? authToken);
    /// <summary>Validates an existing Good Job, Joseph! cfg against the expected endpoint.</summary>
    bool ValidateConfig(string cfgPath, int port, string? authToken);
    /// <summary>Creates or repairs the GSI cfg. Only ever writes its own file.</summary>
    GsiConfigInstallResult InstallOrUpdate(string? manualOverridePath, int port, string? authToken);
    /// <summary>Detects and migrates old Good Job, Joseph!-owned GSI configs to the canonical port.</summary>
    GsiConfigMigrationResult MigrateOldConfigs(string cfgFolder, int canonicalPort, string? authToken);
}

/// <summary>
/// Installs Valve Game-State Integration configuration for Good Job, Joseph!.
/// Writes only <see cref="ConfigFileName"/> into the game's csgo/cfg folder;
/// never modifies or deletes other software's GSI configs.
/// </summary>
public sealed class GsiConfigManager : IGsiConfigManager
{
    public const string DefaultConfigFileName = "gamestate_integration_good_job_joseph.cfg";

    public string ConfigFileName => DefaultConfigFileName;

    [SupportedOSPlatform("windows")]
    public string? FindConfigFolder(string? manualOverridePath)
    {
        // 0) Manual override wins.
        if (!string.IsNullOrWhiteSpace(manualOverridePath))
        {
            var manual = manualOverridePath.Trim();
            var candidates = new[]
            {
                manual,
                Path.Combine(manual, "game", "csgo", "cfg"),
                Path.Combine(manual, "csgo", "cfg"),
                Path.Combine(manual, "cfg")
            };
            foreach (var c in candidates)
                if (Directory.Exists(c) && CanWrite(c)) return Path.GetFullPath(c);
            return null;
        }

        // 1) Registry key written by Steam's installer.
        var steamPath = TryReadRegistry(@"HKLM\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                        ?? TryReadRegistry(@"HKLM\SOFTWARE\Valve\Steam", "InstallPath");

        // 2) Env var fallback.
        if (string.IsNullOrEmpty(steamPath))
            steamPath = Environment.GetEnvironmentVariable("STEAM_PATH");

        // 3) Well-known default.
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
        {
            const string fallback = @"C:\Program Files (x86)\Steam";
            if (Directory.Exists(fallback)) steamPath = fallback;
        }

        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return null;

        var direct = TryFindInLibrary(steamPath!);
        if (direct is not null) return direct;

        // 4) Additional Steam libraries via libraryfolders.vdf.
        var vdf = Path.Combine(steamPath!, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (var line in File.ReadAllLines(vdf))
            {
                var idx = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var seg = line[(idx + 6)..].Trim(' ', '"', '\t');
                seg = seg.Replace("\\\\", "\\");
                if (string.IsNullOrEmpty(seg) || !Directory.Exists(seg)) continue;
                var found = TryFindInLibrary(seg);
                if (found is not null) return found;
            }
        }

        return null;
    }

    private static string? TryFindInLibrary(string libraryRoot)
    {
        var common = Path.Combine(libraryRoot, "steamapps", "common");

        // 1) Prefer the appmanifest_730.acf — the authoritative CS2 install record.
        //    The "installdir" field tells us the real folder name (may differ from
        //    "Counter-Strike 2" if the user renamed it).
        var manifest730 = Path.Combine(libraryRoot, "steamapps", "appmanifest_730.acf");
        if (File.Exists(manifest730))
        {
            var installDir = ParseInstallDir(manifest730);
            if (!string.IsNullOrEmpty(installDir))
            {
                var cfg = Path.Combine(common, installDir, "game", "csgo", "cfg");
                if (Directory.Exists(cfg)) return cfg;
                // Fallback: the install dir might be the legacy CS:GO layout (no /game/).
                var cfgLegacy = Path.Combine(common, installDir, "csgo", "cfg");
                if (Directory.Exists(cfgLegacy)) return cfgLegacy;
            }
        }

        // 2) Fall back to directory-name probing.
        foreach (var game in new[] { "Counter-Strike 2", "Counter-Strike Global Offensive" })
        {
            var cfg = Path.Combine(common, game, "game", "csgo", "cfg");
            if (Directory.Exists(cfg)) return cfg;
        }
        return null;
    }

    /// <summary>Reads the "installdir" value from a Steam appmanifest .acf file.</summary>
    private static string? ParseInstallDir(string acfPath)
    {
        try
        {
            foreach (var line in File.ReadAllLines(acfPath))
            {
                if (line.Contains("\"installdir\"", StringComparison.OrdinalIgnoreCase))
                {
                    // Lines look like:  "installdir"    "Counter-Strike 2"
                    var parts = line.Split('"');
                    if (parts.Length >= 4) return parts[3];
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"CS2: config parse install dir failed: {ex.Message}");
        }
        return null;
    }

    public string BuildConfig(int port, string? authToken)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\"GoodJobJoseph\"");
        sb.AppendLine("{");
        sb.AppendLine($"    \"uri\"           \"http://127.0.0.1:{port}/\"");
        sb.AppendLine("    \"timeout\"       \"5.0\"");
        sb.AppendLine("    \"buffer\"        \"0.1\"");
        sb.AppendLine("    \"throttle\"      \"0.5\"");
        sb.AppendLine("    \"heartbeat\"     \"30.0\"");
        if (!string.IsNullOrEmpty(authToken))
            sb.AppendLine($"    \"auth\"          \"{authToken}\"");
        sb.AppendLine("    \"data\"");
        sb.AppendLine("    {");
        sb.AppendLine("        \"provider\"               \"1\"");
        sb.AppendLine("        \"map\"                    \"1\"");
        sb.AppendLine("        \"round\"                  \"1\"");
        sb.AppendLine("        \"player_id\"              \"1\"");
        sb.AppendLine("        \"player_state\"           \"1\"");
        sb.AppendLine("        \"player_match_stats\"     \"1\"");
        sb.AppendLine("        \"allplayers_id\"          \"1\"");
        sb.AppendLine("        \"allplayers_state\"       \"1\"");
        sb.AppendLine("        \"allplayers_match_stats\" \"1\"");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public bool ValidateConfig(string cfgPath, int port, string? authToken)
    {
        try
        {
            if (!File.Exists(cfgPath)) return false;
            var text = File.ReadAllText(cfgPath);
            var expectedUri = $"http://127.0.0.1:{port}/";
            if (!text.Contains(expectedUri, StringComparison.OrdinalIgnoreCase) &&
                !text.Contains($"http://localhost:{port}/", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(authToken) &&
                !text.Contains($"\"{authToken}\"", StringComparison.Ordinal))
                return false;
            return text.Contains("player_match_stats", StringComparison.Ordinal) &&
                   text.Contains("\"round\"", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"CS2: config validate failed: {ex.Message}");
            return false;
        }
    }

    public GsiConfigInstallResult InstallOrUpdate(string? manualOverridePath, int port, string? authToken)
    {
        try
        {
            var folder = FindConfigFolder(manualOverridePath);
            if (folder is null)
            {
                return new GsiConfigInstallResult(
                    GsiConfigInstallStatus.Cs2NotFound,
                    "CS2 Installation Not Found.\n\nCould not locate Counter-Strike on this machine. " +
                    "Make sure Steam and CS2 are installed, or use \u201cLocate Counter-Strike Folder\u201d " +
                    "to point Good Job, Joseph! at the game manually.",
                    null, null);
            }

            if (!CanWrite(folder))
            {
                return new GsiConfigInstallResult(
                    GsiConfigInstallStatus.FolderNotFound,
                    $"Configuration Folder Not Found (or not writable):\n{folder}\n\n" +
                    "Run Good Job, Joseph! as administrator and try again.",
                    null, folder);
            }

            var cfgPath = Path.Combine(folder, ConfigFileName);
            if (ValidateConfig(cfgPath, port, authToken))
            {
                return new GsiConfigInstallResult(
                    GsiConfigInstallStatus.AlreadyValid,
                    $"GSI Config Already Valid\n\n{cfgPath}\n\n" +
                    "Restart Counter-Strike 2 if celebrations never started.",
                    cfgPath, folder);
            }

            var existed = File.Exists(cfgPath);
            File.WriteAllText(cfgPath, BuildConfig(port, authToken));

            if (!ValidateConfig(cfgPath, port, authToken))
            {
                return new GsiConfigInstallResult(
                    GsiConfigInstallStatus.NeedsRepair,
                    "Configuration Needs Repair: the file was written but did not validate afterwards. Try again.",
                    cfgPath, folder);
            }

            var verb = existed ? "repaired" : "installed";
            return new GsiConfigInstallResult(
                GsiConfigInstallStatus.Installed,
                $"GSI Config {verb}\n\n{cfgPath}\n\n" +
                "Restart Counter-Strike 2. Celebrations will fire on kills, multi-kills, round wins, " +
                "bomb plants/defuses, deaths and match end.",
                cfgPath, folder);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"CS2: config install/update failed: {ex.Message}");
            return new GsiConfigInstallResult(
                GsiConfigInstallStatus.WriteFailed,
                $"Could Not Write Configuration: {ex.Message}", null, null);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryReadRegistry(string keyPath, string valueName)
    {
        try
        {
            var firstSlash = keyPath.IndexOf('\\');
            if (firstSlash < 0) return null;
            var hiveName = keyPath[..firstSlash];
            var subKey = keyPath[(firstSlash + 1)..];
            var hive = hiveName switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
                _ => RegistryHive.CurrentUser
            };
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".goodjobjoseph-write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            AppLog.Warn($"CS2: config write unauthorized to {dir}");
            return false;
        }
        catch (IOException ex)
        {
            AppLog.Warn($"CS2: config write io error to {dir}: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"CS2: config write unexpected error to {dir}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Detects and migrates old Good Job, Joseph!-owned GSI configs in the given cfg folder.
    /// Only touches files that are clearly owned by this app (by name, URI port, or
    /// known token/comment structure); never deletes unrelated third-party GSI configs.
    /// After migration, exactly one canonical config remains.
    /// </summary>
    public GsiConfigMigrationResult MigrateOldConfigs(string cfgFolder, int canonicalPort, string? authToken)
    {
        if (!Directory.Exists(cfgFolder))
            return GsiConfigMigrationResult.NoChange("Config folder not found.");

        var ownedFiles = new List<string>();
        var otherFiles = new List<string>();

        foreach (var file in Directory.GetFiles(cfgFolder, "gamestate_integration_*.cfg"))
        {
            var fileName = Path.GetFileName(file);
            try
            {
                var content = File.ReadAllText(file);

                // Ownership heuristics: app-specific filename or URI pointing at our port
                var isOwned = fileName.StartsWith("gamestate_integration_good_job_joseph", StringComparison.OrdinalIgnoreCase)
                     || fileName.StartsWith("gamestate_integration_goodjobjoseph", StringComparison.OrdinalIgnoreCase)
                     || fileName.Equals("gamestate_integration_goodjob.cfg", StringComparison.OrdinalIgnoreCase)
                     || fileName.Equals("gamestate_integration_gjj.cfg", StringComparison.OrdinalIgnoreCase)
                     || fileName.Equals("gamestate_integration_joseph.cfg", StringComparison.OrdinalIgnoreCase)
                     || content.Contains("goodjob")
                     || content.Contains("good_job_joseph")
                     || content.Contains("GoodJobJoseph")
                     || content.Contains("JosephExperience")
                    || (content.Contains("127.0.0.1") && content.Contains($"http://127.0.0.1:{canonicalPort}/"));

                if (isOwned)
                    ownedFiles.Add(file);
                else
                    otherFiles.Add(file);
            }
            catch
            {
                otherFiles.Add(file); // leave unknown files untouched
            }
        }

        // If canonical config already exists and is valid, just clean up old variants
        var canonicalPath = Path.Combine(cfgFolder, ConfigFileName);
        if (File.Exists(canonicalPath) && ValidateConfig(canonicalPath, canonicalPort, authToken))
        {
            var removed = 0;
            foreach (var old in ownedFiles.Where(f => !string.Equals(f, canonicalPath, StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(old); removed++; } catch { }
            }
            return removed > 0
                ? GsiConfigMigrationResult.Migrated($"Repaired: canonical config valid, removed {removed} stale variant(s).")
                : GsiConfigMigrationResult.NoChange("Canonical config already valid.");
        }

        File.WriteAllText(canonicalPath, BuildConfig(canonicalPort, authToken));
        if (!ValidateConfig(canonicalPath, canonicalPort, authToken))
            return GsiConfigMigrationResult.Failed("Could not write canonical config: validation failed after write.");

        // Remove old owned configs (but not the canonical one we just wrote)
        var removedOld = 0;
        foreach (var old in ownedFiles.Where(f => !string.Equals(f, canonicalPath, StringComparison.OrdinalIgnoreCase)))
        {
            try { File.Delete(old); removedOld++; } catch { }
        }

        return GsiConfigMigrationResult.Migrated(
            $"Migrated to canonical config ({ConfigFileName}). Removed {removedOld} old app-owned variant(s). " +
            $"{otherFiles.Count} unrelated GSI configs preserved.");
    }
}

public enum GsiConfigMigrationStatus
{
    NoChange,
    Migrated,
    Failed
}

public sealed record GsiConfigMigrationResult(GsiConfigMigrationStatus Status, string Message)
{
    public bool IsSuccess => Status != GsiConfigMigrationStatus.Failed;

    public static GsiConfigMigrationResult NoChange(string message) =>
        new(GsiConfigMigrationStatus.NoChange, message);
    public static GsiConfigMigrationResult Migrated(string message) =>
        new(GsiConfigMigrationStatus.Migrated, message);
    public static GsiConfigMigrationResult Failed(string message) =>
        new(GsiConfigMigrationStatus.Failed, message);
}
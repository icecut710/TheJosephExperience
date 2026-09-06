using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using JosephExperience.Models;
using JosephExperience.Utilities;
using Microsoft.Win32;

namespace JosephExperience.Services;

/// <summary>
/// Listens on http://localhost:{port}/ for CS:GO / CS2 Game-State Integration (GSI) HTTP POSTs
/// and translates game events into celebration triggers.
///
    /// One-click setup: the app writes a `gamestate_integration_josephexperience.cfg` file into the
/// Counter-Strike 2 / CS:GO `csgo/cfg` folder automatically (Steam path is located via the
/// Windows registry). On launch CS2 reads the file and starts POSTing to this service.
///
/// References: https://developer.valvesoftware.com/wiki/Counter-Strike:_Global_Offensive_Game_State_Integration
/// </summary>
public sealed class GameStateIntegrationService : IDisposable
{
    private readonly Action<string> _triggerCelebration;
    private readonly Func<AppSettings> _settingsAccessor;
    private readonly Action? _onSettingsMutated;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private long _lastTriggerTicks;

    // ---- round state machine ----
    private string _lastRoundPhase = "";
    private int _lastLocalHealth = 100;
    private int _lastLocalMvp;
    private string _localSteamId = "";
    private int _enemiesAtRoundStart;
    private int _alliesAtRoundStart;
    private bool _clutchArmed;
    private bool _aceTriggeredThisRound;

    // ---- real-time kill detection ----
    // Map of playerSteamId -> last observed health.
    private readonly Dictionary<string, int> _healthBySteamId = new(StringComparer.Ordinal);

    public event Action<string>? StatusChanged;
    public bool IsRunning { get; private set; }

    public GameStateIntegrationService(
        Action<string> triggerCelebration,
        Func<AppSettings> settingsAccessor,
        Action? onSettingsMutated = null)
    {
        _triggerCelebration = triggerCelebration;
        _settingsAccessor = settingsAccessor;
        _onSettingsMutated = onSettingsMutated;
    }

    // ---------------------------------------------------------------- lifecycle

    public void Start()
    {
        var s = _settingsAccessor();
        if (!s.GameIntegrationEnabled) { Stop(); return; }
        if (_listener is not null && IsRunning) return;

        try
        {
            var port = ClampPort(s.GameIntegrationPort);
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => LoopAsync(_cts.Token));
            IsRunning = true;
            var msg = $"Listening on http://127.0.0.1:{port}/";
            StatusChanged?.Invoke(msg);
            AppLog.Info($"GameStateIntegration: {msg}");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            var err = $"Failed to start: {ex.Message}";
            StatusChanged?.Invoke(err);
            AppLog.Warn($"GameStateIntegration start failed: {ex.Message}");
            try { _listener?.Close(); } catch { }
            _listener = null;
        }
    }

    public void Stop()
    {
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        StatusChanged?.Invoke("Stopped");
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- one-click install

    /// <summary>
    /// Locates Counter-Strike 2 / CS:GO on this machine via the Steam registry and writes
    /// the gamestate integration cfg directly into the game's `csgo/cfg` folder. Returns
    /// a user-friendly status string describing what happened.
    /// </summary>
    public AttachResult AttachToGame()
    {
        try
        {
            var target = FindCsConfigFolder();
            if (target is null)
            {
                return new AttachResult(false,
                    "Could not locate Counter-Strike on this machine. " +
                    "Make sure Steam and CS2 are installed, then try again.",
                    InstalledFolder: null);
            }

            var cfgPath = WriteConfigFile(target);

            // Persist attached state so the UI can show a persistent indicator.
            var s = _settingsAccessor();
            s.Cs2Attached = true;
            s.Cs2AttachPath = target;
            s.Cs2AttachedUtc = DateTime.UtcNow;

            // Best-effort save through the settings accessor; if a save delegate is
            // available, invoke it. App wires this up by setting Settings.SaveAfterMutate.
            _onSettingsMutated?.Invoke();

            return new AttachResult(true,
                $"CS2 Attached\n\n" +
                $"Installed to: {target}\n" +
                $"Config: {cfgPath}\n\n" +
                "Restart Counter-Strike 2 and celebrations will fire automatically on kills, " +
                "headshots, clutches, deaths, round wins, MVPs and aces.",
                InstalledFolder: target);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Attach CS2 failed: {ex.Message}");
            return new AttachResult(false, $"Attach failed: {ex.Message}", InstalledFolder: null);
        }
    }

    /// <summary>
    /// Checks whether the cfg file we wrote still exists in the attached folder.
    /// Used by the UI to show a stale-attached indicator.
    /// </summary>
    public bool IsAttachedStillValid()
    {
        var s = _settingsAccessor();
        if (!s.Cs2Attached || string.IsNullOrEmpty(s.Cs2AttachPath)) return false;
        var cfg = Path.Combine(s.Cs2AttachPath, "gamestate_integration_josephexperience.cfg");
        return File.Exists(cfg);
    }

    /// <summary>
    /// Returns the CS2/CSGO csgo/cfg folder (writing access verified) or null if not found.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? FindCsConfigFolder()
    {
        // 1) Preferred: registry key written by Steam's installer.
        var steamPath = TryReadRegistry(
            @"HKLM\SOFTWARE\WOW6432Node\Valve\Steam",
            "InstallPath")
            ?? TryReadRegistry(
                @"HKLM\SOFTWARE\Valve\Steam",
                "InstallPath");

        // 2) Fallback: env var.
        if (string.IsNullOrEmpty(steamPath))
        {
            steamPath = Environment.GetEnvironmentVariable("STEAM_PATH");
        }

        // 3) Fallback: well-known default path.
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
        {
            var fallback = @"C:\Program Files (x86)\Steam";
            if (Directory.Exists(fallback)) steamPath = fallback;
        }

        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return null;

        // Primary candidate: Counter-Strike 2.
        var cs2 = Path.Combine(steamPath, "steamapps", "common", "Counter-Strike 2",
            "game", "csgo", "cfg");
        if (Directory.Exists(cs2) && CanWrite(cs2)) return cs2;

        // Fallback: CS:GO legacy.
        var csgo = Path.Combine(steamPath, "steamapps", "common", "Counter-Strike Global Offensive",
            "game", "csgo", "cfg");
        if (Directory.Exists(csgo) && CanWrite(csgo)) return csgo;

        // Steam library may live on another drive. Try libraryfolders.vdf.
        var libFolders = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libFolders))
        {
            foreach (var line in File.ReadAllLines(libFolders))
            {
                var idx = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var seg = line[(idx + 6)..].Trim(' ', '"', '\t');
                if (string.IsNullOrEmpty(seg)) continue;

                foreach (var game in new[] {
                    Path.Combine(seg, "steamapps", "common", "Counter-Strike 2",
                        "game", "csgo", "cfg"),
                    Path.Combine(seg, "steamapps", "common", "Counter-Strike Global Offensive",
                        "game", "csgo", "cfg") })
                {
                    if (Directory.Exists(game) && CanWrite(game)) return game;
                }
            }
        }

        return null;
    }

    private static string? TryReadRegistry(string keyPath, string valueName)
    {
        try
        {
            // Parse "HKLM\..." or "HKCU\..." into RegistryHive + subkey.
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
            var probe = Path.Combine(dir, ".josephexperience-write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Writes a fresh cfg to the given folder. Returns the file path.</summary>
    public string WriteConfigFile(string? overrideFolder = null)
    {
        var s = _settingsAccessor();
        var port = ClampPort(s.GameIntegrationPort);
        var auth = s.GameIntegrationAuthToken?.Trim() ?? "";

        var cfg = new StringBuilder();
        cfg.AppendLine("\"JosephExperienceGSI\"");
        cfg.AppendLine("{");
        cfg.AppendLine($"    \"uri\"           \"http://127.0.0.1:{port}/\"");
        cfg.AppendLine("    \"timeout\"       \"5.0\"");
        cfg.AppendLine("    \"buffer\"        \"0.1\"");
        cfg.AppendLine("    \"throttle\"      \"0.1\"");
        cfg.AppendLine("    \"heartbeat\"     \"30.0\"");
        cfg.AppendLine($"    \"auth\"          \"{auth}\"");
        cfg.AppendLine("    \"data\"");
        cfg.AppendLine("    {");
        cfg.AppendLine("        \"provider\"               \"1\"");
        cfg.AppendLine("        \"map\"                    \"1\"");
        cfg.AppendLine("        \"round\"                  \"1\"");
        cfg.AppendLine("        \"player_id\"              \"1\"");
        cfg.AppendLine("        \"player_state\"           \"1\"");
        cfg.AppendLine("        \"player_weapons\"         \"1\"");
        cfg.AppendLine("        \"player_match_stats\"     \"1\"");
        cfg.AppendLine("        \"allplayers_id\"          \"1\"");
        cfg.AppendLine("        \"allplayers_state\"       \"1\"");
        cfg.AppendLine("        \"allplayers_weapons\"     \"1\"");
        cfg.AppendLine("        \"allplayers_match_stats\" \"1\"");
        cfg.AppendLine("    }");
        cfg.AppendLine("}");

        var folder = overrideFolder ?? Path.Combine(AppContext.BaseDirectory, "gamestate_integration");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "gamestate_integration_josephexperience.cfg");
        File.WriteAllText(path, cfg.ToString());
        return path;
    }

    // ---------------------------------------------------------------- HTTP loop

    private async Task LoopAsync(CancellationToken token)
    {
        var listener = _listener;
        if (listener is null) return;
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                AppLog.Warn($"GameStateIntegration listener error: {ex.Message}");
                continue;
            }
            _ = Task.Run(() => HandleAsync(ctx, token), token);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken token)
    {
        try
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
                return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            var s = _settingsAccessor();
            if (!string.IsNullOrEmpty(s.GameIntegrationAuthToken))
            {
                var presented =
                    ctx.Request.Headers["Authorization"]
                    ?? ctx.Request.Headers["x-auth-token"]
                    ?? "";
                if (!presented.Contains(s.GameIntegrationAuthToken, StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.Close();
                    return;
                }
            }

            ProcessPayload(body);
            ctx.Response.StatusCode = 200;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"GameStateIntegration handle error: {ex.Message}");
            try { ctx.Response.StatusCode = 500; } catch { }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    // ---------------------------------------------------------------- event detection

    private void ProcessPayload(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        var s = _settingsAccessor();
        if (!s.Enabled) return;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var hasRound = root.TryGetProperty("round", out var roundEl);
            var hasPlayer = root.TryGetProperty("player", out var playerEl);

            // Identify the local player once.
            if (hasPlayer && string.IsNullOrEmpty(_localSteamId) &&
                playerEl.TryGetProperty("steamid", out var localSid))
            {
                _localSteamId = localSid.GetString() ?? "";
            }

            // Phase transitions.
            if (hasRound && roundEl.TryGetProperty("phase", out var phaseEl))
            {
                var phase = phaseEl.GetString() ?? "";
                if (phase != _lastRoundPhase)
                {
                    if (phase == "freezetime" || phase == "live")
                    {
                        _healthBySteamId.Clear();
                        _aceTriggeredThisRound = false;
                        SnapshotEnemyCounts(root, s, hasPlayer, playerEl);
                        _clutchArmed = _enemiesAtRoundStart > 0 && _enemiesAtRoundStart <= 2
                                       && _alliesAtRoundStart <= 1;
                    }
                    else if (phase == "over" || phase == "ended")
                    {
                        DetectRoundEnd(roundEl, playerEl, s);
                    }
                    _lastRoundPhase = phase;
                }
            }

            // Death detection (local player).
            if (hasPlayer) ProcessLocalHealth(playerEl, s);

            // Real-time per-kill via allplayers.
            if ((s.GameEvent_OnKill || s.GameEvent_OnHeadshot || s.GameEvent_OnClutch) &&
                root.TryGetProperty("allplayers", out var allPlayers))
            {
                ProcessAllPlayers(allPlayers, s);
            }
        }
        catch (JsonException ex) { AppLog.Warn($"GSI JSON parse error: {ex.Message}"); }
        catch (Exception ex) { AppLog.Warn($"GSI process error: {ex.Message}"); }
    }

    private void ProcessLocalHealth(JsonElement player, AppSettings s)
    {
        if (!player.TryGetProperty("state", out var state)) return;
        if (!state.TryGetProperty("health", out var h)) return;
        var health = h.GetInt32();
        var wasAlive = _lastLocalHealth > 0;
        if (wasAlive && health <= 0 && s.GameEvent_OnDeath)
            TriggerIfCool("csgo:death");
        _lastLocalHealth = health;
    }

    private void ProcessAllPlayers(JsonElement allPlayers, AppSettings s)
    {
        foreach (var kvp in allPlayers.EnumerateObject())
        {
            var p = kvp.Value;
            if (!p.TryGetProperty("steamid", out var sidEl)) continue;
            var sid = sidEl.GetString() ?? "";
            if (string.IsNullOrEmpty(sid)) continue;

            if (!p.TryGetProperty("state", out var state)) continue;
            if (!state.TryGetProperty("health", out var h)) continue;
            var health = h.GetInt32();

            var had = _healthBySteamId.TryGetValue(sid, out var prev);
            _healthBySteamId[sid] = health;

            // Only react to fresh transitions for OTHER players.
            if (had && prev > 0 && health <= 0 && sid != _localSteamId)
            {
                if (s.GameEvent_OnKill) TriggerIfCool("csgo:kill");

                // Clutch detection: we eliminated an enemy while armed in a 1vN.
                if (_clutchArmed && s.GameEvent_OnClutch)
                {
                    TriggerIfCool("csgo:clutch");
                    _clutchArmed = false; // only fire once per clutch
                }
            }
        }
    }

    private void SnapshotEnemyCounts(JsonElement root, AppSettings s, bool hasPlayer, JsonElement playerEl)
    {
        _enemiesAtRoundStart = 0;
        _alliesAtRoundStart = 0;
        if (!root.TryGetProperty("allplayers", out var allPlayers)) return;

        var localTeam = "";
        if (hasPlayer && playerEl.TryGetProperty("team", out var t)) localTeam = t.GetString() ?? "";

        foreach (var kvp in allPlayers.EnumerateObject())
        {
            var p = kvp.Value;
            if (!p.TryGetProperty("team", out var pt)) continue;
            var team = pt.GetString() ?? "";
            if (string.IsNullOrEmpty(team) || team == "Spectator") continue;
            if (team == localTeam) _alliesAtRoundStart++;
            else _enemiesAtRoundStart++;
        }
    }

    private void DetectRoundEnd(JsonElement roundEl, JsonElement playerEl, AppSettings s)
    {
        // Round win.
        if (s.GameEvent_OnRoundWin &&
            roundEl.TryGetProperty("win_team", out var winTeamEl) &&
            playerEl.TryGetProperty("team", out var localTeamEl))
        {
            var winTeam = winTeamEl.GetString() ?? "";
            var localTeam = localTeamEl.GetString() ?? "";
            if (!string.IsNullOrEmpty(localTeam) && winTeam == localTeam)
                TriggerIfCool("csgo:roundwin");
        }

        // MVP / ace.
        if (s.GameEvent_OnMvpAceClutch &&
            playerEl.TryGetProperty("match_stats", out var ms))
        {
            if (ms.TryGetProperty("mvp", out var mvpEl))
            {
                var mvp = mvpEl.GetInt32();
                if (mvp > _lastLocalMvp)
                {
                    _lastLocalMvp = mvp;
                    TriggerIfCool("csgo:mvp");
                }
            }
            if (ms.TryGetProperty("kills", out var killsEl) && killsEl.GetInt32() >= 5 && !_aceTriggeredThisRound)
            {
                _aceTriggeredThisRound = true;
                TriggerIfCool("csgo:ace");
            }
        }
    }

    // ---------------------------------------------------------------- trigger

    private void TriggerIfCool(string source)
    {
        var s = _settingsAccessor();
        var cooldown = Math.Max(0, s.GameEventCooldownMs);
        var now = Stopwatch.GetTimestamp();
        var elapsedMs = (now - Interlocked.Read(ref _lastTriggerTicks))
                        * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs < cooldown) return;
        Interlocked.Exchange(ref _lastTriggerTicks, now);

        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(() => _triggerCelebration(source));
            else
                _triggerCelebration(source);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"GSI trigger dispatch failed: {ex.Message}");
        }
    }

    private static int ClampPort(int port) =>
        (port > 0 && port <= 65535) ? port : 3000;
}

public sealed record AttachResult(bool Success, string Message, string? InstalledFolder)
{
    public bool Installed => Success;
}

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using JosephExperience.Models;
using JosephExperience.Models.CounterStrike;
using JosephExperience.Services.HalfLife2;
using JosephExperience.Services.ModernWarfare2;
using JosephExperience.Utilities;

namespace JosephExperience.Services.ModernWarfare2;

/// <summary>
/// Call of Duty: Modern Warfare 2 (2009) integration provider.
/// External-only detection: process enumeration and safe best-effort log awareness.
/// No memory reading, no DLL injection, no hooks, no anti-cheat bypass.
/// Log monitoring is incremental — only new lines since last position are read.
/// </summary>
public sealed class ModernWarfare2Provider : IGameIntegrationProvider
{
    private const string ExeSpName = "iw4sp"; // Campaign
    private const string ExeMpName = "iw4mp"; // Multiplayer
    private const string MW2LogPath = @"C:\Program Files (x86)\Steam\steamapps\common\Call of Duty Modern Warfare 2\iw5.log";
    public string LogPath => MW2LogPath;

    private Process? _iw4spProcess;
    private Process? _iw4mpProcess;
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private readonly TimeSpan _minCheckInterval = TimeSpan.FromSeconds(3);

    // Lore lines — decorative only, no progression/currency/rewards
    private static readonly string[] _loreLines = new[]
    {
        "TACTICAL JOSEPH DEPLOYED.",
        "UAV ONLINE. SHAWARMA SECURED.",
        "CIVIC INBOUND.",
        "ROUTER RESTORATION TEAM EN ROUTE.",
        "JOSEPH NADDAF IS OSCAR MIKE.",
        "PRINTER BOSS FIGHT AUTHORIZED.",
        "HOSTILE WIFI DETECTED.",
        "SHARWARMA PACKAGE DELIVERED.",
        "THE GREAT ROUTER RESTORATION HAS BEGUN.",
        "COMMAND HAS APPROVED MAXIMUM NADDAF."
    };

    // Events routed through the real CelebrationService pipeline
    private CelebrationGameEvent? _lastEvent;
    private DateTime _lastTelemetryUtc = DateTime.MinValue;

    // Lore lines — decorative only, no progression/currency/rewards
    private long _logFilePosition = 0;
    private DateTime _lastLogCheckUtc = DateTime.MinValue;
    private readonly object _logLock = new();

    // Capabilities for this provider
    private readonly Capabilities _caps;

    public string GameId => "mw2";
    public string DisplayName => "Call of Duty: Modern Warfare 2 (2009)";
    public string[] ExecutableNames => new[] { "iw4sp.exe", "iw4mp.exe" };
    public bool IsEnabled { get; set; } = true;
    public string State
    {
        get
        {
            lock (_logLock)
            {
                if (_iw4spProcess != null && !_iw4spProcess.HasExited) return "Running";
                if (_iw4mpProcess != null && !_iw4mpProcess.HasExited) return "Running";
                return "Not Running";
            }
        }
    }
    public string Mode
    {
        get
        {
            lock (_logLock)
            {
                if (_iw4spProcess != null && !_iw4spProcess.HasExited) return "Campaign";
                if (_iw4mpProcess != null && !_iw4mpProcess.HasExited) return "Multiplayer";
                return "NotApplicable";
            }
        }
    }
    public DateTime? LastEvent => _lastEvent?.TimestampUtc;
    public DateTime? LastTelemetryAt => _lastTelemetryUtc > DateTime.MinValue ? _lastTelemetryUtc : null;
    public bool CanTest => true;
    public bool CanRepair => false;

    private readonly object _stateLock = new();
    private Action<string>? _stateChanged;

    public ModernWarfare2Provider(Action<string>? stateChanged = null)
    {
        _stateChanged = stateChanged;
        _caps = BuildCapabilities();
        InitializeLogPositionsFromSettings();
        _ = TryDetectProcessAsync();
    }

    private void InitializeLogPositionsFromSettings()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (settings is not null)
                {
                    _logFilePosition = settings.Mw2LogFilePosition;
                    AppLog.Info($"MW2 log position loaded from settings (iw5={_logFilePosition}).");
                }
            }
        }
        catch (Exception ex) when (ex is not StackOverflowException && ex is not OutOfMemoryException)
        {
            AppLog.Warn($"MW2: log position load error: {ex.Message}");
        }
    }

    public ValueTask StartAsync() => ValueTask.CompletedTask;

    public ValueTask StopAsync()
    {
        lock (_logLock)
        {
            _iw4spProcess = null;
            _iw4mpProcess = null;
        }
        return ValueTask.CompletedTask;
    }

    public Capabilities GetCapabilities() => _caps;

    public Action<CelebrationGameEvent>? EventRouter { get; set; }

    public bool TriggerTestEvent()
    {
        var ev = new CelebrationGameEvent
        {
            Type = GameEventType.RoundWin,
            TimestampUtc = DateTime.UtcNow,
            IsTest = true,
            Metadata = { ["Source"] = "ModernWarfare2" }
        };
        _lastEvent = ev;
        _lastTelemetryUtc = DateTime.UtcNow;
        EventRouter?.Invoke(ev);
        return true;
    }

    public event Action<string>? StateChanged
    {
        add { lock (_stateLock) { _stateChanged += value; } }
        remove { lock (_stateLock) { _stateChanged -= value; } }
    }

    private void OnStateChanged(string message)
    {
        var handler = _stateChanged;
        if (handler is not null) handler(message);
    }

    private static Capabilities BuildCapabilities()
    {
        // Process detection is proven via Process.GetProcessesByName
        var caps = Capabilities.SupportsProcessDetection;

        // MW2 log lines contain detectable patterns for map changes/kills
        caps |= Capabilities.SupportsLevelChanges;

        // Test event button routes through real pipeline
        caps |= Capabilities.SupportsTest;

        return caps;
    }

    private async ValueTask TryDetectProcessAsync()
    {
        while (true)
        {
            try
            {
                await Task.Delay(_minCheckInterval).ConfigureAwait(false);

                // Respect the IsEnabled flag — skip detection when disabled
                if (!IsEnabled) continue;

                // Detect both executables
                var spRunning = false;
                var mpRunning = false;

                try
                {
                    var spProcesses = Process.GetProcessesByName(ExeSpName);
                    spRunning = spProcesses.Length > 0;
                    _iw4spProcess = spRunning ? spProcesses[0] : null;
                }
                catch (Exception ex) { AppLog.Warn($"MW2: SP process enumeration failed: {ex.Message}"); }

                try
                {
                    var mpProcesses = Process.GetProcessesByName(ExeMpName);
                    mpRunning = mpProcesses.Length > 0;
                    _iw4mpProcess = mpRunning ? mpProcesses[0] : null;
                }
                catch (Exception ex) { AppLog.Warn($"MW2: MP process enumeration failed: {ex.Message}"); }

                lock (_logLock)
                {
                    var oldRunning = (_iw4spProcess != null && !_iw4spProcess.HasExited) ||
                                     (_iw4mpProcess != null && !_iw4mpProcess.HasExited);
                    var newRunning = mpRunning || spRunning;
                    if (oldRunning != newRunning)
                    {
                        OnStateChanged($"MW2 {(newRunning ? "started" : "stopped")}");
                        _lastTelemetryUtc = DateTime.UtcNow;
                    }

                    if (newRunning)
                    {
                        // Try safe log parsing if available
                        try { ParseLogsIncremental(); }
                        catch (Exception ex) { AppLog.Warn($"MW2 log parsing failed: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) when (ex is not StackOverflowException && ex is not OutOfMemoryException)
            {
                AppLog.Warn($"MW2: provider detection error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Incremental log monitoring: only reads new lines since last position.
    /// Handles log file recreation (MW2 reopens log on restart).
    /// </summary>
    private void ParseLogsIncremental()
    {
        TryParseMW2LogIncremental();
    }

    /// <summary>
    /// Incrementally parse MW2 log for game events.
    /// Only new lines since last position are processed.
    /// </summary>
    private void TryParseMW2LogIncremental()
    {
        if (!File.Exists(MW2LogPath)) return;

        lock (_logLock)
        {
            try
            {
                var fi = new FileInfo(MW2LogPath);

                // File was recreated (size decreased) — reset position
                if (_logFilePosition > fi.Length)
                {
                    _logFilePosition = 0;
                    _lastLogCheckUtc = DateTime.MinValue;
                }

                // If this is a new file we haven't started reading yet
                if (_logFilePosition == 0 && _lastLogCheckUtc == DateTime.MinValue)
                {
                    _lastLogCheckUtc = DateTime.UtcNow;
                }

                // Read new lines since last position
                var newLines = File.ReadLines(MW2LogPath)
                    .Skip((int)_logFilePosition)
                    .ToList();

                if (newLines.Count == 0) return;

                // Advance position past all newly read lines
                _logFilePosition = newLines.Count;

                // Process only the new lines
                foreach (var line in newLines)
                {
                    ProcessMW2LogLine(line);
                }
            }
            catch (Exception ex) when (ex is not StackOverflowException && ex is not OutOfMemoryException)
            {
                AppLog.Warn($"MW2: log incremental parse error: {ex.Message}");
            }

            // Persist MW2 log position after parsing
            try
            {
                var settings = new AppSettings
                {
                    Mw2LogFilePosition = _logFilePosition
                };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AppPaths.SettingsPath, json);
                AppLog.Info($"MW2 log position saved (iw5={_logFilePosition}).");
            }
            catch (Exception ex2) when (ex2 is not StackOverflowException && ex2 is not OutOfMemoryException)
            {
                AppLog.Warn($"MW2: log position save error: {ex2.Message}");
            }

        }
    }

    /// <summary>
    /// Process a single line from MW2 log for game events.
    /// Patterns from MW2 log output.
    /// </summary>
    private void ProcessMW2LogLine(string line)
    {
        var lower = line.ToLowerInvariant();

        // Map/level change patterns in MW2 log
        if (lower.Contains("changing level") || lower.Contains("map change") ||
            lower.Contains("level change") || lower.Contains("level loaded"))
        {
            try
            {
                var ev = new CelebrationGameEvent
                {
                    Type = GameEventType.LevelChanged,
                    TimestampUtc = DateTime.UtcNow,
                    IsTest = false,
                    Metadata = { ["Source"] = "ModernWarfare2" }
                };
                _lastEvent = ev;
                _lastTelemetryUtc = DateTime.UtcNow;
                EventRouter?.Invoke(ev);
            }
            catch (Exception ex) when (ex is not StackOverflowException && ex is not OutOfMemoryException)
            {
                AppLog.Warn($"MW2: default log line processing error: {ex.Message}");
            }
        }

        // Kill patterns in MW2 log
        if (lower.Contains("client killed") || lower.Contains("npc_killed") ||
            lower.Contains("player_died"))
        {
            try
            {
                var ev = new CelebrationGameEvent
                {
                    Type = GameEventType.Death,
                    TimestampUtc = DateTime.UtcNow,
                    IsTest = false,
                    Metadata = { ["Source"] = "ModernWarfare2" }
                };
                _lastEvent = ev;
                _lastTelemetryUtc = DateTime.UtcNow;
                EventRouter?.Invoke(ev);
            }
            catch (Exception ex) when (ex is not StackOverflowException && ex is not OutOfMemoryException)
            {
                AppLog.Warn($"MW2: kill line processing error: {ex.Message}");
            }
        }

        // MVP / kill streak patterns
        if (lower.Contains("headshot") || lower.Contains("multi_kill"))
        {
            try
            {
                var ev = new CelebrationGameEvent
                {
                    Type = GameEventType.Kill,
                    TimestampUtc = DateTime.UtcNow,
                    IsTest = false,
                    Metadata = { ["Source"] = "ModernWarfare2" }
                };
                _lastEvent = ev;
                _lastTelemetryUtc = DateTime.UtcNow;
                EventRouter?.Invoke(ev);
            }
            catch (Exception ex) when (ex is not StackOverflowException && ex is not OutOfMemoryException)
            {
                AppLog.Warn($"MW2: kill streak line processing error: {ex.Message}");
            }
        }
    }
}

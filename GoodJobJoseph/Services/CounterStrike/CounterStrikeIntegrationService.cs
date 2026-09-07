using System.Diagnostics;
using System.Runtime.Versioning;
using JosephExperience.Models;
using JosephExperience.Models.CounterStrike;
using JosephExperience.Utilities;

namespace JosephExperience.Services.CounterStrike;

/// <summary>
/// Top-level Counter-Strike integration: owns the GSI listener, config manager,
/// event detector and router. Purely external to the game — no injection, no
/// memory access, no hooks. CS2 sends JSON to our localhost endpoint via
/// Valve's Game State Integration and we turn state transitions into
/// celebrations through the normal CelebrationService pipeline.
/// </summary>
public sealed class CounterStrikeIntegrationService : IDisposable
{
    public const int DefaultPort = 3000;

    private readonly IGsiServer _server;
    private readonly IGsiPayloadParser _parser;
    private readonly IGameEventDetector _detector;
    private readonly GameEventRouter _router;
    private readonly Func<AppSettings> _settingsAccessor;
    private readonly IGsiConfigManager _configManager;

    private GsiSnapshot? _previous;
    private readonly object _stateGate = new();

    // bounded diagnostics history
    private readonly Queue<string> _eventHistory = new();
    private const int MaxHistoryEntries = 100;

    public event Action<CounterStrikeConnectionState>? StateChanged;
    public CounterStrikeConnectionState State { get; private set; }
        = new(CounterStrikeConnectionPhase.Disabled, "Integration disabled.");

    public CounterStrikeIntegrationService(
        Func<CelebrationService> celebration,
        Func<AppSettings> settingsAccessor,
        IGsiServer? server = null,
        IGsiPayloadParser? parser = null,
        IGameEventDetector? detector = null,
        GameEventRouter? router = null,
        IGsiConfigManager? configManager = null)
    {
        _settingsAccessor = settingsAccessor;
        _server = server ?? new GsiServer();
        _parser = parser ?? new GsiPayloadParser();
        _detector = detector ?? new GameEventDetector();
        _router = router ?? new GameEventRouter(celebration,
            globalCooldownMs: () => Math.Max(0, _settingsAccessor().GameEventCooldownMs));
        _configManager = configManager ?? new GsiConfigManager();

        _server.PayloadReceived += OnPayloadReceived;
    }

    public GameEventRouter Router => _router;
    public IGsiConfigManager ConfigManager => _configManager;

    /// <summary>Snapshot info for the diagnostics panel.</summary>
    public PayloadFreshness Freshness
    {
        get
        {
            var last = _server.LastPayloadUtc;
            if (last is null) return PayloadFreshness.NeverReceived;
            var age = (DateTime.UtcNow - last.Value).TotalSeconds;
            if (age < 3) return PayloadFreshness.Receiving;
            if (age <= 10) return PayloadFreshness.Idle;
            return PayloadFreshness.Stale;
        }
    }

    public DateTime? LastPayloadUtc => _server.LastPayloadUtc;
    public IReadOnlyList<string> EventHistory
    {
        get { lock (_stateGate) return _eventHistory.ToList(); }
    }

    public void Start()
    {
        var s = _settingsAccessor();
        if (!s.GameIntegrationEnabled)
        {
            SetState(CounterStrikeConnectionPhase.Disabled, "Integration disabled.");
            _server.Stop();
            return;
        }

        var port = Math.Clamp(s.GameIntegrationPort, 1, 65535);
        var token = string.IsNullOrEmpty(s.GameIntegrationAuthToken)
            ? null : s.GameIntegrationAuthToken;

        _detector.Configure(new GameEventDetectionOptions
        {
            MultiKillWindowSeconds = Math.Clamp(s.GameEventMultiKillWindowSeconds, 3, 8),
            MultiKillBehavior = s.MultiKillBehaviorParsed
        });

        ReloadEventConfigs();

        // Validate that the GSI config exists and matches current port/token before starting the listener.
        var cfgFolder = _configManager.FindConfigFolder(s.Cs2AttachPath);
        if (cfgFolder is null)
        {
            SetState(CounterStrikeConnectionPhase.ConfigurationMissing,
                "CS2 install not found. Click \"Activate & Install CS2\" or use \"Locate Counter-Strike Folder\".");
            _server.Stop();
            return;
        }

        var cfgPath = System.IO.Path.Combine(cfgFolder, _configManager.ConfigFileName);
        if (!System.IO.File.Exists(cfgPath) || !_configManager.ValidateConfig(cfgPath, port, token))
        {
            SetState(CounterStrikeConnectionPhase.ConfigurationInvalid,
                "GSI config is missing or out of date. Click \"Activate & Install CS2\" to repair.");
            _server.Stop();
            return;
        }

        if (_server.TryStart(port, token, out var error))
        {
            SetState(CounterStrikeConnectionPhase.WaitingForGsi,
                $"Listening on http://127.0.0.1:{port}/ — waiting for CS2 game state.");
        }
        else
        {
            SetState(CounterStrikeConnectionPhase.PortConflict, error ?? "Listener failed to start.");
        }

        _ = Task.Run(MonitorCs2ProcessAsync);
    }

    public void Stop()
    {
        _server.Stop();
        SetState(CounterStrikeConnectionPhase.Disabled, "Integration disabled.");
    }

    public void Restart()
    {
        _previous = null;
        _detector.Reset();
        lock (_stateGate) _eventHistory.Clear();
        _server.Stop();
        Start();
    }

    /// <summary>
    /// Pushes the persisted per-event configs (Games page dropdowns) onto the live router.
    /// Safe to call repeatedly: configs not present in settings simply keep their defaults.
    /// </summary>
    public void ReloadEventConfigs()
    {
        foreach (var kvp in _settingsAccessor().GameEventConfigs)
        {
            if (Enum.TryParse<GameEventType>(kvp.Key, true, out var type))
            {
                _router.SetConfig(type, kvp.Value);
            }
        }
    }

    public void Dispose()
    {
        _server.PayloadReceived -= OnPayloadReceived;
        _server.Dispose();
    }

    // ------------------------------------------------------------ payload pipeline

    private void OnPayloadReceived(object? sender, GsiPayloadReceivedEventArgs e)
    {
        try
        {
            var current = _parser.Parse(e.Body);
            if (current is null)
            {
                SetState(CounterStrikeConnectionPhase.Error, "Last GSI payload was malformed.");
                return;
            }

            GsiSnapshot? previous;
            lock (_stateGate)
            {
                previous = _previous;
                _previous = current;
            }

            var events = _detector.Detect(previous, current);

            // One concise status line per payload — no frame spam.
            AppLog.Info(
                $"CS2 GSI: map={current.MapName ?? "?"} round={current.RoundNumber?.ToString() ?? "?"} " +
                $"playerKills={current.LocalKills?.ToString() ?? "?"} state={Freshness}");

            SetState(CounterStrikeConnectionPhase.ReceivingGameState, "Receiving game state.");

            foreach (var ev in events)
            {
                if (!IsEventEnabled(ev.Type))
                {
                    RecordHistory($"(off) {ev.Type}");
                    continue;
                }

                AppLog.Info($"CS2 Event: type={ev.Type} round={ev.Round?.ToString() ?? "?"}");
                RecordHistory(ev.IsTest ? $"[TEST] {ev.Type}" : ev.Type.ToString());
                _router.Route(ev);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"CS2 pipeline error: {ex.Message}");
        }
    }

    private void RecordHistory(string entry)
    {
        lock (_stateGate)
        {
            _eventHistory.Enqueue($"{DateTime.Now:HH:mm:ss} {entry}");
            while (_eventHistory.Count > MaxHistoryEntries) _eventHistory.Dequeue();
        }
    }

    private bool IsEventEnabled(GameEventType type)
    {
        var s = _settingsAccessor();
        return type switch
        {
            GameEventType.Kill => s.GameEvent_OnKill,
            GameEventType.DoubleKill or GameEventType.TripleKill or GameEventType.QuadKill
                => s.GameEvent_OnKill || s.GameEvent_OnMultiKill,
            GameEventType.Ace => s.GameEvent_OnAce || s.GameEvent_OnMultiKill,
            GameEventType.RoundWin => s.GameEvent_OnRoundWin,
            GameEventType.MatchWin => s.GameEvent_OnMatchWin,
            GameEventType.BombPlanted => s.GameEvent_OnBombPlant,
            GameEventType.BombDefused => s.GameEvent_OnBombDefuse,
            GameEventType.BombExploded => s.GameEvent_OnBombExplode,
            GameEventType.Death => s.GameEvent_OnDeath,
            GameEventType.Mvp => s.GameEvent_OnMvpAceClutch,
            _ => false
        };
    }

    private void SetState(CounterStrikeConnectionPhase phase, string detail)
    {
        if (State.Phase == phase && State.Detail == detail) return;
        State = new CounterStrikeConnectionState(phase, detail);
        StateChanged?.Invoke(State);
    }

    // ------------------------------------------------------------ test events

    /// <summary>Feeds a synthetic event through the REAL router/celebration paths (Settings test buttons).</summary>
    public bool TriggerTestEvent(GameEventType type)
    {
        var ev = new CelebrationGameEvent
        {
            Type = type,
            TimestampUtc = DateTime.UtcNow,
            IsTest = true,
            Map = _previous?.MapName,
            Round = _previous?.RoundNumber
        };
        RecordHistory($"[TEST] {type}");
        return _router.Route(ev);
    }

    /// <summary>Feeds a raw JSON body through the real pipeline (fixture replay / diagnostics).</summary>
    public int IngestRawPayload(string json)
    {
        OnPayloadReceived(this, new GsiPayloadReceivedEventArgs(json, DateTime.UtcNow));
        return 0;
    }

    // ------------------------------------------------------------ process monitor

    private DateTime _lastReconnectUtc = DateTime.MinValue;

    [SupportedOSPlatform("windows")]
    private async Task MonitorCs2ProcessAsync()
    {
        while (_server.IsRunning)
        {
            try
            {
                var running = Process.GetProcessesByName("cs2").Length > 0;
                var fresh = Freshness;

                // Game state went stale while CS2 keeps POSTing nothing — restart the
                // listener with backoff. (This branch previously required Phase ==
                // ReceivingGameState while the outer condition excluded it, so it was
                // unreachable and the listener never self-healed.)
                if (fresh == PayloadFreshness.Stale && State.Phase == CounterStrikeConnectionPhase.ReceivingGameState
                    && (DateTime.UtcNow - _lastReconnectUtc).TotalSeconds > 15)
                {
                    AppLog.Info("CS2 GSI: stale connection while CS2 running — attempting listener restart.");
                    _lastReconnectUtc = DateTime.UtcNow;
                    _server.Stop();
                    _previous = null;
                    _detector.Reset();
                    var s = _settingsAccessor();
                    var port = Math.Clamp(s.GameIntegrationPort, 1, 65535);
                    var token = string.IsNullOrEmpty(s.GameIntegrationAuthToken) ? null : s.GameIntegrationAuthToken;
                    if (_server.TryStart(port, token, out _))
                    {
                        SetState(CounterStrikeConnectionPhase.WaitingForGsi,
                            $"Listener restarted on http://127.0.0.1:{port}/ — waiting for CS2 to resend game state.");
                    }
                    else
                    {
                        SetState(CounterStrikeConnectionPhase.PortConflict,
                            "Listener restart failed — port conflict.");
                    }
                }
                else if (running && fresh is PayloadFreshness.NeverReceived or PayloadFreshness.Stale
                    && State.Phase != CounterStrikeConnectionPhase.ReceivingGameState)
                {
                    SetState(CounterStrikeConnectionPhase.Cs2Running,
                        "CS2 is running but no game state has arrived yet. " +
                        "Install the GSI config and restart the game.");
                }
                else if (!running && State.Phase == CounterStrikeConnectionPhase.Cs2Running)
                {
                    SetState(CounterStrikeConnectionPhase.WaitingForGsi,
                        "Waiting for CS2 to start.");
                }
                else if (!running && State.Phase == CounterStrikeConnectionPhase.ReceivingGameState)
                {
                    SetState(CounterStrikeConnectionPhase.WaitingForCs2,
                        "CS2 has closed. Waiting for the game to start again.");
                }
            }
            catch (Exception ex) { AppLog.Warn($"CS2 process enumeration transiently failed: {ex.Message}"); }
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }
}
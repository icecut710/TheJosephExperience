using JosephExperience.Models.CounterStrike;
using JosephExperience.Utilities;

namespace JosephExperience.Services.CounterStrike;

public enum CounterStrikeConnectionPhase
{
    Disabled,
    WaitingForCs2,
    Cs2Running,
    WaitingForGsi,
    ReceivingGameState,
    ConfigurationMissing,
    ConfigurationInvalid,
    PortConflict,
    Error
}

public sealed record CounterStrikeConnectionState(
    CounterStrikeConnectionPhase Phase,
    string Detail)
{
    public override string ToString() => Phase switch
    {
        CounterStrikeConnectionPhase.Disabled => "Disabled",
        CounterStrikeConnectionPhase.WaitingForCs2 => "Waiting for CS2",
        CounterStrikeConnectionPhase.Cs2Running => "CS2 Running — Waiting for Game State",
        CounterStrikeConnectionPhase.WaitingForGsi => "Waiting for GSI",
        CounterStrikeConnectionPhase.ReceivingGameState => "Receiving Game State",
        CounterStrikeConnectionPhase.ConfigurationMissing => "Configuration Missing",
        CounterStrikeConnectionPhase.ConfigurationInvalid => "Configuration Invalid",
        CounterStrikeConnectionPhase.PortConflict => "Port Conflict",
        CounterStrikeConnectionPhase.Error => "Error",
        _ => Phase.ToString()
    };
}

/// <summary>Freshness classification of the last GSI payload.</summary>
public enum PayloadFreshness
{
    NeverReceived,
    Receiving,   // < 3 s
    Idle,        // 3–10 s
    Stale        // > 10 s
}

public enum GameEventCelebrationSource
{
    DefaultCelebration,
    RandomJoseph,
    SpecificJoseph,
    SpecificCategory,
    SpecificPreset,
    NoCelebration
}

public enum GameEventTextSource
{
    JosephAssignedQuote,
    RandomLoreQuote,
    GameEventQuote,
    Custom,
    None
}

/// <summary>Per-event configuration: what Joseph shows and what text plays.</summary>
public sealed class GameEventCelebrationConfig
{
    public GameEventCelebrationSource Source { get; set; } = GameEventCelebrationSource.RandomJoseph;
    public GameEventTextSource TextSource { get; set; } = GameEventTextSource.GameEventQuote;
    public string? SpecificImageId { get; set; }
    public string? SpecificCategory { get; set; }
    public string? SpecificPreset { get; set; }
    public string? CustomText { get; set; }
    /// <summary>Minimum ms between celebrations of this event type. 0 = no cooldown.</summary>
    public int CooldownMs { get; set; }
    /// <summary>Relative importance for priority arbitration (higher wins).</summary>
    public int Priority { get; set; }
}

public enum GameEventConflictPolicy
{
    Queue,
    ReplaceLowerPriority,
    IgnoreWhileCelebrating,
    Stack
}

/// <summary>Per-event default text — the game-event quote channel.</summary>
public static class GameEventText
{
    public static string DefaultFor(GameEventType type) => type switch
    {
        GameEventType.Kill => "GOOD JOB, JOSEPH!",
        GameEventType.DoubleKill => "DOUBLE NADDAF.",
        GameEventType.TripleKill => "TRIPLE NADDAF.",
        GameEventType.QuadKill => "QUAD NADDAF.",
        GameEventType.Ace => "ABSOLUTE NADDAF.",
        GameEventType.RoundWin => "ROUND SECURED.",
        GameEventType.RoundLoss => "NEXT ROUND, JOSEPH.",
        GameEventType.MatchWin => "THE WIZARD HAS WON.",
    GameEventType.BombPlanted => "UH OH, JOSEPH.",
    GameEventType.BombDefused => "CRISIS AVERTED.",
    GameEventType.BombExploded => "BOOM, JOSEPH!",
        GameEventType.Death => "GET UP, JOSEPH!",
        GameEventType.Mvp => "MVP, NATURALLY.",
        GameEventType.RoundStart => "GO GET 'EM.",
        GameEventType.MatchStart => "TIME TO SHINE, JOSEPH.",
        _ => "GOOD JOB, JOSEPH!"
    };
}

/// <summary>Default per-event celebration configs.</summary>
public static class GameEventDefaults
{
    public static IReadOnlyDictionary<GameEventType, GameEventCelebrationConfig> Create() =>
        new Dictionary<GameEventType, GameEventCelebrationConfig>
        {
            [GameEventType.Kill] = new() { CooldownMs = 1000, Priority = 10 },
            [GameEventType.DoubleKill] = new() { CooldownMs = 0, Priority = 20 },
            [GameEventType.TripleKill] = new() { CooldownMs = 0, Priority = 30 },
            [GameEventType.QuadKill] = new() { CooldownMs = 0, Priority = 40 },
            [GameEventType.Ace] = new() { CooldownMs = 0, Priority = 50 },
            [GameEventType.RoundWin] = new() { CooldownMs = 0, Priority = 60 },
            [GameEventType.RoundLoss] = new() { CooldownMs = 0, Priority = 5 },
            [GameEventType.MatchWin] = new() { CooldownMs = 0, Priority = 100 },
            [GameEventType.MatchStart] = new() { CooldownMs = 0, Priority = 15 },
            [GameEventType.RoundStart] = new() { CooldownMs = 0, Priority = 5 },
            [GameEventType.BombPlanted] = new() { CooldownMs = 0, Priority = 35 },
            [GameEventType.BombDefused] = new() { CooldownMs = 0, Priority = 55 },
            [GameEventType.BombExploded] = new() { CooldownMs = 0, Priority = 35 },
            [GameEventType.Death] = new() { CooldownMs = 3000, Priority = 8 },
            [GameEventType.Mvp] = new() { CooldownMs = 0, Priority = 45 },
            [GameEventType.Headshot] = new() { CooldownMs = 1000, Priority = 12 }
        };
}

/// <summary>
/// Routes semantic game events into the normal CelebrationService pipeline,
/// applying per-event config, cooldowns and priority arbitration.
/// Events never bypass CelebrationService — they are just another trigger source.
/// </summary>
public sealed class GameEventRouter
{
    private readonly Func<CelebrationService> _celebration;
    private readonly Dictionary<GameEventType, GameEventCelebrationConfig> _configs;
    private readonly Dictionary<GameEventType, DateTime> _lastFiredUtc = new();
    private readonly object _gate = new();
    private GameEventConflictPolicy _conflictPolicy = GameEventConflictPolicy.ReplaceLowerPriority;

    /// <summary>Raised with (event, routed?) for the diagnostics history panel.</summary>
    public event Action<CelebrationGameEvent, bool>? EventRouted;
    /// <summary>Raised when a celebration is suppressed by priority arbitration.</summary>
    public event Action<CelebrationGameEvent, string>? EventSuppressed;

    public GameEventRouter(
        Func<CelebrationService> celebration,
        Dictionary<GameEventType, GameEventCelebrationConfig>? configs = null)
    {
        _celebration = celebration;
        _configs = configs ?? new Dictionary<GameEventType, GameEventCelebrationConfig>(GameEventDefaults.Create());
    }

    public void SetConflictPolicy(GameEventConflictPolicy policy) =>
        _conflictPolicy = policy;

    public GameEventCelebrationConfig GetConfig(GameEventType type) =>
        _configs.TryGetValue(type, out var c) ? c : new GameEventCelebrationConfig();

    public void SetConfig(GameEventType type, GameEventCelebrationConfig config) =>
        _configs[type] = config;

    /// <summary>Attempts to route an event. Returns true if a celebration was requested.</summary>
    public bool Route(CelebrationGameEvent gameEvent)
    {
        var config = GetConfig(gameEvent.Type);

        lock (_gate)
        {
            // Per-event cooldown (not a substitute for detector dedup — belt and braces).
            if (config.CooldownMs > 0 &&
                _lastFiredUtc.TryGetValue(gameEvent.Type, out var last) &&
                (gameEvent.TimestampUtc - last).TotalMilliseconds < config.CooldownMs)
            {
                EventSuppressed?.Invoke(gameEvent, "cooldown");
                return false;
            }
            _lastFiredUtc[gameEvent.Type] = gameEvent.TimestampUtc;
        }

        var svc = _celebration();
        if (svc is null)
        {
            EventSuppressed?.Invoke(gameEvent, "no-celebration-service");
            return false;
        }

        var conflict = _conflictPolicy;
        var isBusy = svc.IsBusy;
        if (isBusy && conflict == GameEventConflictPolicy.IgnoreWhileCelebrating)
        {
            EventSuppressed?.Invoke(gameEvent, "busy");
            return false;
        }
        if (isBusy && conflict == GameEventConflictPolicy.ReplaceLowerPriority)
        {
            if (!svc.TryPreempt(config.Priority))
            {
                EventSuppressed?.Invoke(gameEvent, "lower-priority");
                return false;
            }
        }

        try
        {
            // Map per-event config onto the celebration request.
            var triggerType = gameEvent.SourceTag; // e.g. "CounterStrike:RoundWin"
            var handled = svc.TriggerGameEvent(triggerType, config, gameEvent);
            EventRouted?.Invoke(gameEvent, handled);
            return handled;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"GameEventRouter: routing {gameEvent.Type} failed: {ex.Message}");
            EventSuppressed?.Invoke(gameEvent, "error");
            return false;
        }
    }
}
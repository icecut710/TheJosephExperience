using JosephExperience.Models.CounterStrike;

namespace JosephExperience.Services.CounterStrike;

public sealed record GameEventDetectionOptions
{
    /// <summary>Rolling window for multi-kill escalation (seconds).</summary>
    public int MultiKillWindowSeconds { get; init; } = 4;
    public MultiKillBehavior MultiKillBehavior { get; init; } = MultiKillBehavior.HighestOnly;
    public bool EmitRoundLoss { get; init; }
}

public interface IGameEventDetector
{
    /// <summary>
    /// Detects semantic events from the transition previous -> current.
    /// Call with previous == null for the first payload (which seeds state and emits nothing).
    /// </summary>
    IReadOnlyList<CelebrationGameEvent> Detect(GsiSnapshot? previous, GsiSnapshot current);
    /// <summary>Clears all round/match tracking (used on config changes).</summary>
    void Reset();
    /// <summary>Updates detection options (multi-kill window/behavior). Resets the kill window on change.</summary>
    void Configure(GameEventDetectionOptions options);
}

/// <summary>
/// Translates GSI state transitions into semantic events with correct deduplication:
/// identical payloads repeated any number of times produce zero additional events;
/// a kill-count increment produces exactly one Kill event per kill.
/// </summary>
public sealed class GameEventDetector : IGameEventDetector
{
    private readonly object _gate = new();
    private GameEventDetectionOptions _options = new();

    // rolling multi-kill window
    private readonly Queue<DateTime> _killWindow = new();
    private GameEventType _highestTierEmitted = GameEventType.Kill;

    // per-round dedup
    private int? _lastRoundNumber;
    private string? _lastRoundPhase;
    private string? _roundWinEmittedFor;   // "{round}:{team}"
    private string? _roundStartEmittedFor;
    private string? _bombPlantedEmittedFor;
    private string? _bombDefusedEmittedFor;
    private string? _bombExplodedEmittedFor;
    private bool _aceEmittedThisRound;
    private bool _matchWinEmitted;

    // match-level state
    private string? _lastMapName;
    private string? _lastMapPhase;
    private int? _lastKills;
    private int? _lastMvp;
    private bool? _lastLocalAlive;

    public void Reset()
    {
        lock (_gate)
        {
            _killWindow.Clear();
            _highestTierEmitted = GameEventType.Kill;
            _lastRoundNumber = null;
            _lastRoundPhase = null;
            _roundWinEmittedFor = null;
            _roundStartEmittedFor = null;
            _bombPlantedEmittedFor = null;
            _bombDefusedEmittedFor = null;
            _bombExplodedEmittedFor = null;
            _aceEmittedThisRound = false;
            _matchWinEmitted = false;
            _lastMapName = null;
            _lastMapPhase = null;
            _lastKills = null;
            _lastMvp = null;
            _lastLocalAlive = null;
        }
    }

    public void Configure(GameEventDetectionOptions options)
    {
        lock (_gate)
        {
            if (_options.MultiKillWindowSeconds != options.MultiKillWindowSeconds ||
                _options.MultiKillBehavior != options.MultiKillBehavior)
            {
                _killWindow.Clear();
                _highestTierEmitted = GameEventType.Kill;
            }
            _options = options;
        }
    }

    public IReadOnlyList<CelebrationGameEvent> Detect(GsiSnapshot? previous, GsiSnapshot current)
    {
        var events = new List<CelebrationGameEvent>();
        lock (_gate)
        {
            // ---- match lifecycle: map change or round reset => new match ----
            var isNewMatch = _lastMapName is not null
                             && !string.Equals(_lastMapName, current.MapName, StringComparison.OrdinalIgnoreCase);
            if (_lastRoundNumber.HasValue && current.RoundNumber.HasValue
                && current.RoundNumber.Value < _lastRoundNumber.Value && _lastMapPhase != "gameover")
            {
                isNewMatch = true;
            }

            if (isNewMatch)
            {
                ResetRoundState(keepMatchWin: false);
                events.Add(MakeEvent(current, GameEventType.MatchStart));
            }
            _lastMapName = current.MapName ?? _lastMapName;

            // ---- match win (map phase gameover) — once per match ----
            if (string.Equals(current.MapPhase, "gameover", StringComparison.OrdinalIgnoreCase) &&
                _lastMapPhase != "gameover" && !_matchWinEmitted)
            {
                _matchWinEmitted = true;
                var ev = MakeEvent(current, GameEventType.MatchWin);
                if (!string.IsNullOrEmpty(current.RoundWinTeam))
                    ev.Metadata["finalRoundWinTeam"] = current.RoundWinTeam;
                events.Add(ev);
            }
            _lastMapPhase = current.MapPhase ?? _lastMapPhase;

            // ---- round transitions ----
            var roundKey = current.RoundNumber?.ToString() ?? "?";
            var roundChanged = _lastRoundNumber.HasValue && current.RoundNumber.HasValue
                               && current.RoundNumber.Value != _lastRoundNumber.Value;

            // Round start: first sighting of a live/freezetime phase per round.
            var phase = current.RoundPhase;
            if (current.RoundNumber.HasValue &&
                (string.Equals(phase, "live", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(phase, "freezetime", StringComparison.OrdinalIgnoreCase)))
            {
                var startKey = $"{roundKey}:{phase}";
                if (_roundStartEmittedFor != startKey)
                {
                    _roundStartEmittedFor = startKey;
                    if (roundChanged || _lastRoundPhase is null)
                        events.Add(MakeEvent(current, GameEventType.RoundStart));
                    if (roundChanged) ResetRoundState(keepMatchWin: true);
                }
            }
            _lastRoundNumber = current.RoundNumber ?? _lastRoundNumber;
            _lastRoundPhase = phase ?? _lastRoundPhase;

            // Round win / loss: phase "over" with a win_team. Once per round+team.
            if (string.Equals(phase, "over", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(current.RoundWinTeam))
            {
                var winKey = $"{roundKey}:{current.RoundWinTeam}";
                if (_roundWinEmittedFor != winKey)
                {
                    _roundWinEmittedFor = winKey;
                    var localTeam = current.LocalTeam ?? "";
                    if (string.Equals(current.RoundWinTeam, localTeam, StringComparison.OrdinalIgnoreCase))
                    {
                        events.Add(MakeEvent(current, GameEventType.RoundWin));
                    }
                    else if (_options.EmitRoundLoss && !string.IsNullOrEmpty(localTeam))
                    {
                        events.Add(MakeEvent(current, GameEventType.RoundLoss));
                    }
                }
            }

            // ---- bomb events (once per round per state) ----
            if (string.Equals(current.BombState, "planted", StringComparison.OrdinalIgnoreCase) &&
                _bombPlantedEmittedFor != roundKey)
            {
                _bombPlantedEmittedFor = roundKey;
                events.Add(MakeEvent(current, GameEventType.BombPlanted));
            }
            if (string.Equals(current.BombState, "defused", StringComparison.OrdinalIgnoreCase) &&
                _bombDefusedEmittedFor != roundKey)
            {
                _bombDefusedEmittedFor = roundKey;
                events.Add(MakeEvent(current, GameEventType.BombDefused));
            }
            if ((string.Equals(current.BombState, "explode", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(current.BombState, "exploded", StringComparison.OrdinalIgnoreCase)) &&
                _bombExplodedEmittedFor != roundKey)
            {
                _bombExplodedEmittedFor = roundKey;
                events.Add(MakeEvent(current, GameEventType.BombExploded));
            }

            // ---- MVP increment ----
            if (current.LocalMvp.HasValue && _lastMvp.HasValue && current.LocalMvp.Value > _lastMvp.Value)
            {
                events.Add(MakeEvent(current, GameEventType.Mvp));
            }
            _lastMvp = current.LocalMvp ?? _lastMvp;

            // ---- death (local alive transition true -> false) ----
            if (current.LocalAlive.HasValue && _lastLocalAlive.HasValue &&
                _lastLocalAlive.Value && !current.LocalAlive.Value)
            {
                events.Add(MakeEvent(current, GameEventType.Death));
            }
            _lastLocalAlive = current.LocalAlive ?? _lastLocalAlive;

            // ---- kills ----
            if (current.LocalKills.HasValue && _lastKills.HasValue &&
                current.LocalKills.Value > _lastKills.Value)
            {
                var delta = current.LocalKills.Value - _lastKills.Value;
                events.AddRange(DetectKills(current, delta));
            }
            _lastKills = current.LocalKills ?? _lastKills;
        }
        return events;
    }

    private List<CelebrationGameEvent> DetectKills(GsiSnapshot current, int delta)
    {
        var events = new List<CelebrationGameEvent>();
        var now = current.TimestampUtc;

        // Prune rolling window.
        while (_killWindow.Count > 0 &&
               (now - _killWindow.Peek()).TotalSeconds > _options.MultiKillWindowSeconds)
        {
            _killWindow.Dequeue();
        }
        for (var i = 0; i < delta; i++) _killWindow.Enqueue(now);

        // Determine tier: 2 => Double, 3 => Triple, 4 => Quad, >=5 => Ace.
        var windowCount = _killWindow.Count;
        GameEventType? tier = windowCount switch
        {
            2 => GameEventType.DoubleKill,
            3 => GameEventType.TripleKill,
            4 => GameEventType.QuadKill,
            >= 5 when !_aceEmittedThisRound => GameEventType.Ace,
            _ => null
        };
        if (tier == GameEventType.Ace) _aceEmittedThisRound = true;

        var tierRank = TierRank(tier);
        switch (_options.MultiKillBehavior)
        {
            case MultiKillBehavior.Stack:
                for (var i = 0; i < delta; i++)
                    events.Add(MakeEvent(current, GameEventType.Kill));
                if (tier.HasValue && tierRank > TierRank(_highestTierEmitted))
                {
                    events.Add(MakeEvent(current, tier.Value));
                    _highestTierEmitted = tier.Value;
                }
                break;

            case MultiKillBehavior.ReplaceCurrent:
                if (tier.HasValue)
                {
                    if (tierRank > TierRank(_highestTierEmitted))
                    {
                        events.Add(MakeEvent(current, tier.Value));
                        _highestTierEmitted = tier.Value;
                    }
                }
                else
                {
                    for (var i = 0; i < delta; i++)
                        events.Add(MakeEvent(current, GameEventType.Kill));
                }
                break;

            case MultiKillBehavior.HighestOnly:
            default:
                if (tier.HasValue && tierRank > TierRank(_highestTierEmitted))
                {
                    // Only the escalation fires (1st kill => Kill, 2nd => Double, 3rd => Triple...).
                    events.Add(MakeEvent(current, tier.Value));
                    _highestTierEmitted = tier.Value;
                }
                else if (!tier.HasValue)
                {
                    for (var i = 0; i < delta; i++)
                        events.Add(MakeEvent(current, GameEventType.Kill));
                }
                // Tier fired but no escalation: the tier event already covers these kills.
                break;
        }

        return events;
    }

    private static int TierRank(GameEventType? type) => type switch
    {
        GameEventType.Ace => 5,
        GameEventType.QuadKill => 4,
        GameEventType.TripleKill => 3,
        GameEventType.DoubleKill => 2,
        _ => 1
    };

    private void ResetRoundState(bool keepMatchWin)
    {
        _killWindow.Clear();
        _highestTierEmitted = GameEventType.Kill;
        _roundWinEmittedFor = null;
        _roundStartEmittedFor = null;
        _bombPlantedEmittedFor = null;
        _bombDefusedEmittedFor = null;
        _bombExplodedEmittedFor = null;
        _aceEmittedThisRound = false;
        if (!keepMatchWin) _matchWinEmitted = false;
    }

    private CelebrationGameEvent MakeEvent(GsiSnapshot current, GameEventType type) => new()
    {
        Type = type,
        TimestampUtc = current.TimestampUtc,
        PlayerSteamId = current.SteamId,
        Map = current.MapName,
        Round = current.RoundNumber,
        Kills = current.LocalKills,
        Team = current.LocalTeam
    };
}
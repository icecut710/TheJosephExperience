using System.Text.Json;

namespace JosephExperience.Models.CounterStrike;

/// <summary>Semantic game events we can translate into celebrations.</summary>
public enum GameEventType
{
    Kill,
    /// <summary>Experimental: standard GSI does not reliably mark headshots.</summary>
    Headshot,
    DoubleKill,
    TripleKill,
    QuadKill,
    Ace,
    RoundWin,
    RoundLoss,
    MatchWin,
    BombPlanted,
    BombDefused,
    BombExploded,
    Clutch,
    Mvp,
    /// <summary>Experimental: derived from local health recovering after a low point.</summary>
    LowHealthSurvival,
    Death,
    RoundStart,
    MatchStart,
    /// <summary>Used only by the Settings "Test" buttons.</summary>
    Test,
    LevelChanged,
}

/// <summary>A semantic event derived from GSI state transitions.</summary>
public sealed record CelebrationGameEvent
{
    public GameEventType Type { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string? PlayerSteamId { get; init; }
    public string? Map { get; init; }
    public int? Round { get; init; }
    public int? Kills { get; init; }
    public string? Team { get; init; }
    /// <summary>True when produced by a Settings "Test ..." button (never from live GSI).</summary>
    public bool IsTest { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Stable trigger-source string, e.g. "CounterStrike:Kill" or "HalfLife2:RoundWin".</summary>
    public string Source
    {
        get
        {
            var prefix = Metadata.TryGetValue("Source", out var s) && !string.IsNullOrEmpty(s)
                ? s
                : "CounterStrike";
            return IsTest ? $"{prefix}:Test:{Type}" : $"{prefix}:{Type}";
        }
    }

    /// <summary>Alias used by the router and integration service.</summary>
    public string SourceTag => Source;
}

/// <summary>How consecutive kills inside the rolling window escalate.</summary>
public enum MultiKillBehavior
{
    /// <summary>Only the highest tier fires (2 kills => DoubleKill, then a 3rd => TripleKill only).</summary>
    HighestOnly,
    /// <summary>Every tier fires as it is reached.</summary>
    Stack,
    /// <summary>The tier event replaces the plain Kill event (one celebration per burst).</summary>
    ReplaceCurrent
}


/// <summary>Default celebration priority. Higher = more important.</summary>
public static class GameEventPriority
{
    public static int For(GameEventType type) => type switch
    {
        GameEventType.MatchWin => 100,
        GameEventType.Ace => 90,
        GameEventType.RoundWin => 80,
        GameEventType.QuadKill => 75,
        GameEventType.TripleKill => 70,
        GameEventType.Clutch => 65,
        GameEventType.DoubleKill => 60,
        GameEventType.Mvp => 55,
        GameEventType.BombDefused => 50,
        GameEventType.BombExploded => 45,
        GameEventType.BombPlanted => 40,
        GameEventType.Headshot => 35,
        GameEventType.Kill => 30,
        GameEventType.Death => 20,
        GameEventType.LowHealthSurvival => 15,
        GameEventType.RoundLoss => 10,
        GameEventType.RoundStart => 5,
        GameEventType.MatchStart => 5,
        _ => 10
    };
}

/// <summary>One snapshot of the GSI state stream. All fields may be absent.</summary>
public sealed record GsiSnapshot
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string? ProviderName { get; init; }
    public string? SteamId { get; init; }
    public string? MapName { get; init; }
    public string? MapPhase { get; init; }
    public int? RoundNumber { get; init; }
    public string? RoundPhase { get; init; }
    public string? RoundWinTeam { get; init; }
    public string? BombState { get; init; }
    public string? LocalTeam { get; init; }
    public string? LocalName { get; init; }
    public int? LocalKills { get; init; }
    public int? LocalDeaths { get; init; }
    public int? LocalMvp { get; init; }
    public int? LocalHealth { get; init; }
    public bool? LocalAlive { get; init; }
    /// <summary>Per-steam-id health snapshot from allplayers (when available).</summary>
    public IReadOnlyDictionary<string, int> PlayerHealths { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Parses a GSI JSON root element into a snapshot. Absent sections yield null fields.
    /// Never throws on missing data; only throws on structurally invalid JSON (JsonException),
    /// which the parser layer catches.
    /// </summary>
    public static GsiSnapshot FromJson(JsonElement root)
    {
        var snap = new GsiSnapshot();

        if (root.TryGetProperty("provider", out var provider) &&
            provider.TryGetProperty("name", out var pname))
            snap = snap with { ProviderName = pname.GetString() };

        if (root.TryGetProperty("map", out var map))
        {
            if (map.TryGetProperty("name", out var mn)) snap = snap with { MapName = mn.GetString() };
            if (map.TryGetProperty("phase", out var mp)) snap = snap with { MapPhase = mp.GetString() };
            if (map.TryGetProperty("round", out var mr) && mr.TryGetInt32(out var roundNo))
                snap = snap with { RoundNumber = roundNo };
        }

        string? localTeam = null;
        int? localKills = null, localDeaths = null, localMvp = null, localHealth = null;
        bool? localAlive = null;
        var steamId = (string?)null;
        var localName = (string?)null;

        if (root.TryGetProperty("player", out var player))
        {
            if (player.TryGetProperty("steamid", out var sid)) steamId = sid.GetString();
            if (player.TryGetProperty("name", out var pn)) localName = pn.GetString();
            if (player.TryGetProperty("team", out var t)) localTeam = t.GetString();
            if (player.TryGetProperty("activity", out var act) &&
                act.TryGetProperty("living", out var living))
                localAlive = string.Equals(living.GetString(), "alive", StringComparison.OrdinalIgnoreCase);
            if (player.TryGetProperty("state", out var st) &&
                st.TryGetProperty("health", out var h) && h.TryGetInt32(out var hv))
                localHealth = hv;
            if (player.TryGetProperty("match_stats", out var ms))
            {
                if (ms.TryGetProperty("kills", out var k) && k.TryGetInt32(out var kv)) localKills = kv;
                if (ms.TryGetProperty("deaths", out var d) && d.TryGetInt32(out var dv)) localDeaths = dv;
                if (ms.TryGetProperty("mvp", out var m) && m.TryGetInt32(out var mv)) localMvp = mv;
            }
        }

        var roundPhase = (string?)null;
        var roundWinTeam = (string?)null;
        var bombState = (string?)null;
        if (root.TryGetProperty("round", out var round))
        {
            if (round.TryGetProperty("phase", out var rp)) roundPhase = rp.GetString();
            if (round.TryGetProperty("win_team", out var wt)) roundWinTeam = wt.GetString();
            if (round.TryGetProperty("bomb", out var b)) bombState = b.GetString();
        }
        // Bomb state can also appear at root level in CS2 GSI: "bomb": { "state": "planted" }
        if (bombState is null && root.TryGetProperty("bomb", out var bombEl))
        {
            if (bombEl.ValueKind == JsonValueKind.String)
                bombState = bombEl.GetString();
            else if (bombEl.ValueKind == JsonValueKind.Object &&
                     bombEl.TryGetProperty("state", out var bs))
                bombState = bs.GetString();
        }

        var healths = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("allplayers", out var allplayers))
        {
            foreach (var p in allplayers.EnumerateObject())
            {
                if (p.Value.TryGetProperty("state", out var pst) &&
                    pst.TryGetProperty("health", out var ph) &&
                    ph.TryGetInt32(out var pHp))
                {
                    healths[p.Name] = pHp;
                }
            }
        }

        return snap with
        {
            SteamId = steamId,
            LocalName = localName,
            LocalTeam = localTeam,
            LocalAlive = localAlive,
            LocalHealth = localHealth,
            LocalKills = localKills,
            LocalDeaths = localDeaths,
            LocalMvp = localMvp,
            RoundPhase = roundPhase,
            RoundWinTeam = roundWinTeam,
            BombState = bombState,
            PlayerHealths = healths
        };
    }
}
/// <summary>Which Joseph source a game event should celebrate with.</summary>
public enum GameEventCelebrationSource
{
    DefaultCelebration,
    RandomJoseph,
    SpecificJoseph,
    SpecificCategory,
    SpecificPreset,
    NoCelebration
}

/// <summary>Which text channel a game event should use.</summary>
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

/// <summary>What happens when events collide while an overlay is already up.</summary>
public enum GameEventConflictPolicy
{
    /// <summary>Higher-priority events replace a running celebration; equal/lower are dropped.</summary>
    ReplaceLowerPriority,
    /// <summary>Events wait until the current overlay finishes (bounded queue).</summary>
    Queue,
    /// <summary>Drop any event that arrives while an overlay is up.</summary>
    IgnoreWhileCelebrating,
    /// <summary>Trigger everything immediately (may fight for the overlay).</summary>
    Stack
}
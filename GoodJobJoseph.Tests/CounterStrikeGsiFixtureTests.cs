using JosephExperience.Models.CounterStrike;
using JosephExperience.Services.CounterStrike;

namespace JosephExperience.Tests;

/// <summary>
/// Runtime validation of the CS2 GSI pipeline using representative JSON fixtures
/// fed through the REAL parser + event detector. Proves dedup, transition detection
/// and kill/multi-kill policy without a live game.
/// NOTE: live CS2 validation is separate — these tests cannot verify that CS2 itself
/// sends the payloads; they verify our handling of payloads exactly as GSI produces them.
/// </summary>
public class CounterStrikeGsiFixtureTests
{
    private static GsiSnapshot Parse(string json) =>
        new GsiPayloadParser().Parse(json) ?? throw new InvalidOperationException("fixture failed to parse");

    private static IGameEventDetector NewDetector() => new GameEventDetector();

    private const string InitialJson = """
        {"map":{"name":"de_mirage","phase":"live","round":1},
         "round":{"phase":"live","round":1},
         "player":{"steamid":"76561198000000001","team":"CT",
                   "activity":{"living":"alive"},"state":{"health":100,"alive":true},
                   "match_stats":{"kills":0,"mvp":0}},
         "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000000}}
        """;

    private static string KillJson(int kills, int round = 1, string phase = "live") =>
        """
        {
          "map": {"name": "de_mirage", "phase": "live", "round": __ROUND__},
          "round": {"phase": "__PHASE__", "round": __ROUND__, "win_team": ""},
          "player": {"steamid": "76561198000000001", "team": "CT",
                     "activity": {"living": "alive"},
                     "state": {"health": 100},
                     "match_stats": {"kills": __KILLS__, "mvp": 0}},
          "provider": {"name": "Counter-Strike 2", "steamid": "76561198", "timestamp": 1700000000}
        }
        """
        .Replace("__ROUND__", round.ToString())
        .Replace("__PHASE__", phase)
        .Replace("__KILLS__", kills.ToString());

    [Fact]
    public void InitialPayload_SeedsState_EmitsNoEvents()
    {
        var d = NewDetector();
        var events = d.Detect(null, Parse(InitialJson));
        Assert.DoesNotContain(events, e => e.Type == GameEventType.Kill);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.RoundWin);
    }

    [Fact]
    public void IdenticalPayloadRepeated10Times_ProducesZeroEvents()
    {
        var d = NewDetector();
        var first = Parse(InitialJson);
        d.Detect(null, first);
        var total = 0;
        for (var i = 0; i < 10; i++)
            total += d.Detect(first, Parse(InitialJson)).Count;
        Assert.Equal(0, total);
    }

    [Fact]
    public void KillCount4To5_ProducesExactlyOneKill()
    {
        var d = NewDetector();
        var prev = Parse(KillJson(4));
        d.Detect(null, prev);
        var events = d.Detect(prev, Parse(KillJson(5)));
        Assert.Single(events);
        Assert.Equal(GameEventType.Kill, events[0].Type);
    }

    [Fact]
    public void TwoRapidKills_ProducesKillThenDoubleKill_HighestOnly()
    {
        var d = NewDetector();
        var p1 = Parse(KillJson(0));
        d.Detect(null, p1);
        var e1 = d.Detect(p1, Parse(KillJson(1)));
        Assert.Single(e1);
        Assert.Equal(GameEventType.Kill, e1[0].Type);

        var e2 = d.Detect(p1, Parse(KillJson(2)));
        Assert.Single(e2);
        Assert.Equal(GameEventType.DoubleKill, e2[0].Type);
    }

    [Fact]
    public void FiveKillsInWindow_ProducesAce()
    {
        var d = NewDetector();
        var prev = Parse(KillJson(0));
        d.Detect(null, prev);
        var all = new List<CelebrationGameEvent>();
        for (var k = 1; k <= 5; k++)
        {
            var cur = Parse(KillJson(k));
            all.AddRange(d.Detect(prev, cur));
            prev = cur;
        }
        Assert.Contains(all, e => e.Type == GameEventType.Ace);
    }

    [Fact]
    public void RoundWinStateRepeated_ProducesExactlyOneRoundWin()
    {
        var d = NewDetector();
        const string win = """
            {"map":{"name":"de_mirage","phase":"live","round":7},
             "round":{"phase":"over","round":7,"win_team":"CT"},
             "player":{"steamid":"76561198000000001","team":"CT",
                       "activity":{"living":"alive"},"state":{"health":100,"alive":true},
                       "match_stats":{"kills":3,"mvp":0}},
             "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000000}}
            """;
        d.Detect(null, Parse(InitialJson));
        var first = Parse(win);
        var total = d.Detect(Parse(InitialJson), first).Count(e => e.Type == GameEventType.RoundWin);
        for (var i = 0; i < 10; i++)
            total += d.Detect(first, Parse(win)).Count(e => e.Type == GameEventType.RoundWin);
        Assert.Equal(1, total);
    }

    [Fact]
    public void EnemyRoundWin_DoesNotTriggerRoundWinByDefault()
    {
        var d = NewDetector();
        const string loss = """
            {"map":{"name":"de_mirage","phase":"live","round":4},
             "round":{"phase":"over","round":4,"win_team":"T"},
             "player":{"steamid":"76561198000000001","team":"CT",
                       "state":{"health":100,"alive":false},
                       "match_stats":{"kills":1,"mvp":0}},
             "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000000}}
            """;
        d.Detect(null, Parse(InitialJson));
        var events = d.Detect(Parse(InitialJson), Parse(loss));
        Assert.DoesNotContain(events, e => e.Type == GameEventType.RoundWin);
    }

    [Fact]
    public void MatchEnd_ProducesSingleMatchWin_EvenWhenScoreboardStaysUp()
    {
        var d = NewDetector();
        const string over = """
            {"map":{"name":"de_mirage","phase":"gameover","round":24},
             "round":{"phase":"over","round":24,"win_team":"CT"},
             "player":{"steamid":"76561198000000001","team":"CT",
                       "activity":{"living":"dead"},"state":{"health":0,"alive":false},"match_stats":{"kills":20,"mvp":5}},
             "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000000}}
            """;
        d.Detect(null, Parse(InitialJson));
        var first = Parse(over);
        var count = d.Detect(Parse(InitialJson), first).Count(e => e.Type == GameEventType.MatchWin);
        for (var i = 0; i < 5; i++)
            count += d.Detect(first, Parse(over)).Count(e => e.Type == GameEventType.MatchWin);
        Assert.Equal(1, count);
    }

    [Fact]
    public void BombPlantedAndDefused_EmitOncePerRound()
    {
        var d = NewDetector();
        const string planted = """
            {"map":{"name":"de_mirage","phase":"live","round":6},
             "round":{"phase":"live","round":6,"bomb":"planted"},
             "player":{"steamid":"76561198000000001","team":"CT",
                       "activity":{"living":"alive"},"state":{"health":80,"alive":true},"match_stats":{"kills":0,"mvp":0}},
             "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000000}}
            """;
        const string defused = """
            {"map":{"name":"de_mirage","phase":"live","round":6},
             "round":{"phase":"over","round":6,"bomb":"defused","win_team":"CT"},
             "player":{"steamid":"76561198000000001","team":"CT",
                       "activity":{"living":"alive"},"state":{"health":80,"alive":true},"match_stats":{"kills":0,"mvp":0}},
             "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000000}}
            """;
        d.Detect(null, Parse(InitialJson));
        var p1 = Parse(planted);
        Assert.Single(d.Detect(Parse(InitialJson), p1).Where(e => e.Type == GameEventType.BombPlanted));
        Assert.Empty(d.Detect(p1, Parse(planted)).Where(e => e.Type == GameEventType.BombPlanted));
        Assert.Single(d.Detect(p1, Parse(defused)).Where(e => e.Type == GameEventType.BombDefused));
    }

    [Fact]
    public void DeathTransition_EmitsSingleDeathEvent()
    {
        var d = NewDetector();
        const string dead = """
            {"map":{"name":"de_mirage","phase":"live","round":3},
             "round":{"phase":"live","round":3},
             "player":{"steamid":"76561198000000001","team":"CT",
                       "activity":{"living":"dead"},"state":{"health":0,"alive":false},"match_stats":{"kills":0,"mvp":0}},
             "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000000}}
            """;
        d.Detect(null, Parse(InitialJson));
        Assert.Single(d.Detect(Parse(InitialJson), Parse(dead)).Where(e => e.Type == GameEventType.Death));
        Assert.Empty(d.Detect(Parse(dead), Parse(dead)).Where(e => e.Type == GameEventType.Death));
    }

    [Fact]
    public void MalformedJson_ReturnsNull_DoesNotThrow()
    {
        var parser = new GsiPayloadParser();
        Assert.Null(parser.Parse("not json at all {{{"));
        Assert.Null(parser.Parse(""));
        Assert.Null(parser.Parse("{\"player\":"));
    }

    [Fact]
    public void PayloadWithMissingSections_ParsesSafely()
    {
        var parser = new GsiPayloadParser();
        const string minimal = """
            {"provider":{"name":"Counter-Strike 2"}}
            """;
        var snap = parser.Parse(minimal);
        Assert.NotNull(snap);
        Assert.Null(snap!.MapName);
        Assert.Null(snap.LocalKills);
    }

    [Fact]
    public void StackBehavior_EmitsKillPlusEscalation()
    {
        var d = NewDetector();
        d.Configure(new GameEventDetectionOptions { MultiKillBehavior = MultiKillBehavior.Stack });
        var p1 = Parse(KillJson(0));
        d.Detect(null, p1);
        Assert.Single(d.Detect(p1, Parse(KillJson(1))).Where(e => e.Type == GameEventType.Kill));
        var e2 = d.Detect(Parse(KillJson(1)), Parse(KillJson(2)));
        Assert.Contains(e2, e => e.Type == GameEventType.Kill);
        Assert.Contains(e2, e => e.Type == GameEventType.DoubleKill);
    }

    [Fact]
    public void MapChange_EmitsMatchStart_AndResetsKillTracking()
    {
        var d = NewDetector();
        var oldMap = Parse(KillJson(8, 12));
        d.Detect(null, oldMap);
        const string newMap = """
            {"map":{"name":"de_inferno","phase":"live","round":1},
             "round":{"phase":"live","round":1},
             "player":{"steamid":"76561198000000001","team":"T",
                       "activity":{"living":"alive"},"state":{"health":100,"alive":true},"match_stats":{"kills":0,"mvp":0}},
             "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000001}}
            """;
        Assert.Contains(d.Detect(oldMap, Parse(newMap)), e => e.Type == GameEventType.MatchStart);

        const string kill1 = """
            {"map":{"name":"de_inferno","phase":"live","round":1},
             "round":{"phase":"live","round":1},
             "player":{"steamid":"76561198000000001","team":"T",
                       "activity":{"living":"alive"},"state":{"health":100,"alive":true},"match_stats":{"kills":1,"mvp":0}},
             "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000001}}
            """;
        var killEvents = d.Detect(Parse(newMap), Parse(kill1));
        Assert.Single(killEvents.Where(e => e.Type == GameEventType.Kill));
        Assert.DoesNotContain(killEvents, e => e.Type == GameEventType.DoubleKill);
    }
}

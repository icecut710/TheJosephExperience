using System.IO;
using JosephExperience.Models.CounterStrike;
using JosephExperience.Services.CounterStrike;
using Xunit;

namespace GoodJobJoseph.Tests;

/// <summary>
/// Runtime validation of the CS2 GSI pipeline with representative JSON fixtures
/// fed through the ACTUAL parser + event detector. Verifies deduplication,
/// kill-increment detection, multi-kill escalation and round/match resets.
/// </summary>
public class GsiFixtureTests
{
    private static GsiPayloadParser Parser { get; } = new();

    private static string Payload(
        string map = "de_mirage",
        int? round = 7,
        string? phase = "live",
        int? kills = 4,
        string? winTeam = null,
        string? bomb = null,
        string? mapPhase = null,
        bool alive = true,
        int mvp = 0,
        string steamId = "76561198000000001",
        string team = "CT")
    {
        // Real CS2 GSI: bomb state is a ROOT-level object: "bomb": { "state": "planted" }.
        var bombJson = bomb is null ? "" : $",\n  \"bomb\": {{ \"state\": \"{bomb}\" }}";
var roundJson = round.HasValue
            ? $"\"round\": {{ \"phase\": \"{phase}\", \"round\": {round.Value}{(winTeam is null ? "" : $", \"win_team\": \"{winTeam}\"")}}},"
            : "";
        var mapPhaseJson = mapPhase is null ? "" : $", \"phase\": \"{mapPhase}\"";
        return $$$"""
        {
          "provider": { "name": "Counter-Strike: Global Objective", "steamid": "{{{steamId}}}" },
          "map": { "name": "{{{map}}}", "round": {{{round ?? 0}}}{{{mapPhaseJson}}} },
          {{{roundJson}}}
          "player": {
            "steamid": "{{{steamId}}}",
            "team": "{{{team}}}",
            "activity": "playing",
            "state": { "health": {{{(alive ? 100 : 0)}}} },
            "match_stats": { "kills": {{{kills ?? 0}}}, "mvps": {{{mvp}}} }
          }{{{bombJson}}}
        }
        """;
    }

    private static IReadOnlyList<CelebrationGameEvent> Detect(IGameEventDetector d, GsiSnapshot? prev, string json) =>
        d.Detect(prev, Parser.Parse(json)!);

    /// <summary>Exposes the fixture builder for debug tests.</summary>
    public static string PayloadPublic(
        string map = "de_mirage", int? round = 7, string? phase = "live", int? kills = 4,
        string? winTeam = null, string? bomb = null, string? mapPhase = null,
        bool alive = true, int mvp = 0) =>
        (string)typeof(GsiFixtureTests)
            .GetMethod("Payload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object?[] { map, round, phase, kills, winTeam, bomb, mapPhase, alive, mvp, "76561198000000001", "CT" })!;

    [Fact]
    public void IdenticalPayloadRepeated10Times_ProducesZeroEvents()
    {
        var d = new GameEventDetector();
        var first = Parser.Parse(Payload())!;
        var _ = d.Detect(null, first); // seed
        for (var i = 0; i < 10; i++)
        {
            var events = Detect(d, first, Payload());
            Assert.Empty(events);
        }
    }

    [Fact]
    public void KillCount4To5_ProducesExactlyOneKill()
    {
        var d = new GameEventDetector();
        var prev = Parser.Parse(Payload(kills: 4))!;
        _ = d.Detect(null, prev);
        var events = Detect(d, prev, Payload(kills: 5));
        Assert.Single(events);
        Assert.Equal(GameEventType.Kill, events[0].Type);
    }

    [Fact]
    public void RapidSecondKill_ProducesDoubleKill_HighestOnly()
    {
        var d = new GameEventDetector();
        var s4 = Parser.Parse(Payload(kills: 4))!;
        _ = d.Detect(null, s4);

        // 4 -> 5: first kill inside the window.
        var first = Detect(d, s4, Payload(kills: 5));
        Assert.Single(first);
        Assert.Equal(GameEventType.Kill, first[0].Type);

        // 5 -> 6 rapidly: window now holds 2 kills => DoubleKill.
        var second = Detect(d, Parser.Parse(Payload(kills: 5))!, Payload(kills: 6));
        Assert.Single(second);
        Assert.Equal(GameEventType.DoubleKill, second[0].Type);
    }

    [Fact]
    public void KillThenTwoMore_ProducesEscalatingTiers()
    {
        var d = new GameEventDetector();
        var s = Parser.Parse(Payload(kills: 0))!;
        _ = d.Detect(null, s);

        var e1 = Detect(d, s, Payload(kills: 1));
        Assert.Single(e1);
        Assert.Equal(GameEventType.Kill, e1[0].Type);

        var e2 = Detect(d, Parser.Parse(Payload(kills: 1))!, Payload(kills: 2));
        Assert.Single(e2);
        Assert.Equal(GameEventType.DoubleKill, e2[0].Type);

        var e3 = Detect(d, Parser.Parse(Payload(kills: 2))!, Payload(kills: 3));
        Assert.Single(e3);
        Assert.Equal(GameEventType.TripleKill, e3[0].Type);
    }

    [Fact]
    public void RoundWinStateRepeated_EmitsExactlyOneRoundWin()
    {
        var d = new GameEventDetector();
        var prev = Parser.Parse(Payload(phase: "over", winTeam: "CT"))!;
        _ = d.Detect(null, prev);
        for (var i = 0; i < 5; i++)
        {
            var events = Detect(d, prev, Payload(phase: "over", winTeam: "CT"));
            Assert.Empty(events); // deduped
        }
    }

    [Fact]
    public void NewRound_ResetsTracking_RoundWinFiresAgain()
    {
        var d = new GameEventDetector();
        var r7 = Parser.Parse(Payload(round: 7, phase: "over", winTeam: "CT"))!;
        _ = d.Detect(null, r7);

        // next round start resets per-round dedup
        var r8 = Detect(d, Parser.Parse(Payload(round: 7, phase: "over", winTeam: "CT"))!,
            Payload(round: 8, phase: "freezetime"));
        Assert.Contains(r8, e => e.Type == GameEventType.RoundStart);

        // same team wins round 8 => fires again (new round)
        var r8win = Detect(d, Parser.Parse(Payload(round: 8, phase: "freezetime"))!,
            Payload(round: 8, phase: "over", winTeam: "CT"));
        Assert.Contains(r8win, e => e.Type == GameEventType.RoundWin);
    }

    [Fact]
    public void BombPlanted_OncePerRound_ThenDefused()
    {
        var d = new GameEventDetector();
        var prev = Parser.Parse(Payload(bomb: "planted"))!;
        _ = d.Detect(null, prev);

        // repeated planted state => nothing
        Assert.Empty(Detect(d, prev, Payload(bomb: "planted")));

        // defuse transition => one BombDefused
        var defused = Detect(d, prev, Payload(bomb: "defused"));
        Assert.Single(defused);
        Assert.Equal(GameEventType.BombDefused, defused[0].Type);
    }

    [Fact]
    public void MatchEnd_Gameover_EmitsMatchWin_Once()
    {
        var d = new GameEventDetector();
        var prev = Parser.Parse(Payload(mapPhase: "gameover"))!;
        var events = d.Detect(null, prev);
        Assert.Contains(events, e => e.Type == GameEventType.MatchWin);

        // repeated gameover => nothing more
        Assert.Empty(Detect(d, prev, Payload(mapPhase: "gameover")));
    }

    [Fact]
    public void EnemyWin_DoesNotEmitRoundWin_ByDefault()
    {
        var d = new GameEventDetector();
        var prev = Parser.Parse(Payload(phase: "over", winTeam: "T"))!;
        var events = d.Detect(null, prev);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.RoundWin);
    }

    [Fact]
    public void MalformedJson_ParserReturnsNull_NeverThrows()
    {
        var p = new GsiPayloadParser();
        Assert.Null(p.Parse("{ not json !!!"));
        Assert.Null(p.Parse(""));
        Assert.Null(p.Parse("null"));
    }

    [Fact]
    public void ValveShapedActivityString_ParsesKillsHealthAndMvps()
    {
        var snapshot = Parser.Parse(Payload(kills: 9, alive: false, mvp: 2));
        Assert.NotNull(snapshot);
        Assert.Equal(9, snapshot.LocalKills);
        Assert.Equal(0, snapshot.LocalHealth);
        Assert.False(snapshot.LocalAlive);
        Assert.Equal(2, snapshot.LocalMvp);
    }

    [Fact]
    public void ConfigBuild_ContainsEndpointAndDataSections()
    {
        var mgr = new GsiConfigManager();
        var cfg = mgr.BuildConfig(3000, "tok123");
        Assert.Contains("http://127.0.0.1:3000/", cfg);
        Assert.Contains("\"auth\"", cfg);
        Assert.Contains("tok123", cfg);
        Assert.Contains("player_match_stats", cfg);
        Assert.Equal("gamestate_integration_good_job_joseph.cfg", mgr.ConfigFileName);
    }

    [Fact]
    public void ValidateConfig_ValidConfig_ReturnsTrue()
    {
        var mgr = new GsiConfigManager();
        var tmp = Path.GetTempFileName();
        File.Delete(tmp);
        tmp = Path.ChangeExtension(tmp, ".cfg");
        File.WriteAllText(tmp, mgr.BuildConfig(3000, "secret"));
        Assert.True(mgr.ValidateConfig(tmp, 3000, "secret"));
        File.Delete(tmp);
    }

    [Fact]
    public void ValidateConfig_WrongPort_ReturnsFalse()
    {
        var mgr = new GsiConfigManager();
        var tmp = Path.GetTempFileName();
        File.Delete(tmp);
        tmp = Path.ChangeExtension(tmp, ".cfg");
        File.WriteAllText(tmp, mgr.BuildConfig(3000, "secret"));
        Assert.False(mgr.ValidateConfig(tmp, 3001, "secret"));
        File.Delete(tmp);
    }

    [Fact]
    public void ValidateConfig_MissingFile_ReturnsFalse()
    {
        var mgr = new GsiConfigManager();
        var tmp = Path.GetTempFileName();
        File.Delete(tmp);
        Assert.False(mgr.ValidateConfig(tmp, 3000, null));
    }

    [Fact]
    public void InstallOrUpdate_CreatesValidConfig()
    {
        var mgr = new GsiConfigManager();
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var cfgDir = Path.Combine(tmpDir, "cfg");
            Directory.CreateDirectory(cfgDir);
            var result = mgr.InstallOrUpdate(cfgDir, 3000, "tok");
            Assert.True(result.Success);
            Assert.NotNull(result.CfgFolder);
            var cfgPath = Path.Combine(result.CfgFolder!, mgr.ConfigFileName);
            Assert.True(File.Exists(cfgPath));
            Assert.True(mgr.ValidateConfig(cfgPath, 3000, "tok"));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ConnectionState_ToString_ReturnsCanonicalNames()
    {
        Assert.Equal("Disabled", new CounterStrikeConnectionState(CounterStrikeConnectionPhase.Disabled, "").ToString());
        Assert.Equal("Receiving Game State", new CounterStrikeConnectionState(CounterStrikeConnectionPhase.ReceivingGameState, "").ToString());
        Assert.Equal("Configuration Missing", new CounterStrikeConnectionState(CounterStrikeConnectionPhase.ConfigurationMissing, "").ToString());
        Assert.Equal("Configuration Invalid", new CounterStrikeConnectionState(CounterStrikeConnectionPhase.ConfigurationInvalid, "").ToString());
        Assert.Equal("Port Conflict", new CounterStrikeConnectionState(CounterStrikeConnectionPhase.PortConflict, "").ToString());
    }

    [Fact]
    public void GameEventText_DefaultFor_KnownEvents()
    {
        Assert.False(string.IsNullOrWhiteSpace(GameEventText.DefaultFor(GameEventType.Kill)));
        Assert.False(string.IsNullOrWhiteSpace(GameEventText.DefaultFor(GameEventType.RoundWin)));
        Assert.False(string.IsNullOrWhiteSpace(GameEventText.DefaultFor(GameEventType.MatchWin)));
        Assert.False(string.IsNullOrWhiteSpace(GameEventText.DefaultFor(GameEventType.BombPlanted)));
    }
}

using JosephExperience.Models.CounterStrike;
using JosephExperience.Services.CounterStrike;
using Xunit;
using Xunit.Abstractions;

namespace GoodJobJoseph.Tests;

public class GsiDebugTests
{
    private readonly ITestOutputHelper _out;
    public GsiDebugTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void DebugBombFixture()
    {
        var parser = new GsiPayloadParser();
        var json = GsiFixtureTests.PayloadPublic(bomb: "defused");
        _out.WriteLine("JSON: " + json.Replace("\n", "\\n").Replace("\r", ""));
        var snap = parser.Parse(json);
        _out.WriteLine($"BombState={snap?.BombState} Round={snap?.RoundNumber} Phase={snap?.RoundPhase} Kills={snap?.LocalKills}");

        var d = new GameEventDetector();
        var seeded = parser.Parse(GsiFixtureTests.PayloadPublic(bomb: "planted"));
        var seedEvents = d.Detect(null, seeded!);
        _out.WriteLine("Seed events: " + string.Join(",", seedEvents.Select(e => e.Type)));
        var defusedEvents = d.Detect(seeded, snap!);
        _out.WriteLine("Defuse events: " + string.Join(",", defusedEvents.Select(e => e.Type)));
        Assert.Contains(defusedEvents, e => e.Type == GameEventType.BombDefused);
    }
}

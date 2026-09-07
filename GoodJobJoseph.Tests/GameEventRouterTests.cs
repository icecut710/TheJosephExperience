using JosephExperience.Services;
using JosephExperience.Models.CounterStrike;
using JosephExperience.Services.CounterStrike;
using Xunit;

namespace JosephExperience.Tests;

/// <summary>
/// Tests for GameEventRouter internal cooldown logic and config management.
/// Uses a lightweight fake CelebrationService to verify routing decisions.
/// </summary>
public class GameEventRouterTests
{
    private class FakeCelebration : CelebrationService
    {
        public bool ForceBusy { get; set; }
        public int ActivePriority { get; set; } = 0;
        public List<(string Trigger, GameEventCelebrationConfig Cfg, CelebrationGameEvent Evt)> Routed { get; } = new();
        public List<(CelebrationGameEvent Evt, GameEventCelebrationConfig Cfg)> Queued { get; } = new();
        public int PreemptCallCount { get; private set; }

        public FakeCelebration()
            : base(null!, null!, null!, null!, null!, null!, null!)
        {
        }

        public override bool IsBusy => ForceBusy;

        public override bool TryPreempt(int priority)
        {
            PreemptCallCount++;
            if (priority > ActivePriority)
            {
                ActivePriority = priority;
                ForceBusy = false; // preempt succeeds => no longer busy
                return true;
            }
            return false;
        }

        public override bool TriggerGameEvent(string triggerType, GameEventCelebrationConfig config, CelebrationGameEvent gameEvent)
        {
            if (ForceBusy) return false;
            Routed.Add((triggerType, config, gameEvent));
            ActivePriority = config.Priority;
            return true;
        }

        public override void EnqueueGameEvent(CelebrationGameEvent gameEvent, GameEventCelebrationConfig config)
        {
            Queued.Add((gameEvent, config));
        }
    }

    private static CelebrationGameEvent MakeEvent(GameEventType type, DateTime? ts = null) =>
        new() { Type = type, TimestampUtc = ts ?? DateTime.UtcNow, IsTest = false };

    [Fact]
    public void PerEventCooldown_SuppressesSameEventTypeWithinWindow()
    {
        var fake = new FakeCelebration();
        var router = new GameEventRouter(() => fake);
        router.EventSuppressed += (e, reason) => { };

        var now = DateTime.UtcNow;
        var e1 = MakeEvent(GameEventType.Kill, now);
        Assert.True(router.Route(e1)); // Kill has 1000ms cooldown, first call

        // Second kill 500ms later => should be suppressed by per-event cooldown
        var e2 = MakeEvent(GameEventType.Kill, now.AddMilliseconds(500));
        Assert.False(router.Route(e2));
    }

    [Fact]
    public void PerEventCooldown_AllowsAfterWindowExpires()
    {
        var fake = new FakeCelebration();
        var router = new GameEventRouter(() => fake);

        var now = DateTime.UtcNow;
        router.Route(MakeEvent(GameEventType.Kill, now));

        // 1500ms later (Kill cooldown = 1000ms) => should pass
        var e2 = MakeEvent(GameEventType.Kill, now.AddMilliseconds(1500));
        Assert.True(router.Route(e2));
    }

    [Fact]
    public void GlobalCooldown_SuppressesRapidDifferentEvents()
    {
        var fake = new FakeCelebration();
        var router = new GameEventRouter(() => fake, globalCooldownMs: () => 5000);

        var now = DateTime.UtcNow;
        router.Route(MakeEvent(GameEventType.Kill, now));

        // DoubleKill has Priority 20, should pass per-event cooldown but be suppressed by global cooldown
        var e2 = MakeEvent(GameEventType.DoubleKill, now.AddMilliseconds(100));
        Assert.False(router.Route(e2));
    }

    [Fact]
    public void GlobalCooldown_PassesAfterWindow()
    {
        var fake = new FakeCelebration();
        var router = new GameEventRouter(() => fake, globalCooldownMs: () => 5000);

        var now = DateTime.UtcNow;
        router.Route(MakeEvent(GameEventType.Kill, now));

        // 6000ms later => global cooldown expired
        var e2 = MakeEvent(GameEventType.RoundWin, now.AddMilliseconds(6000));
        Assert.True(router.Route(e2));
    }

    [Fact]
    public void NonZeroCooldown_AllowsDifferentEventTypeSimultaneously()
    {
        var fake = new FakeCelebration();
        var router = new GameEventRouter(() => fake);

        var now = DateTime.UtcNow;
        router.Route(MakeEvent(GameEventType.Kill, now)); // Kill cooldown = 1000

        // RoundWin has CooldownMs = 0, so it should pass per-event cooldown
        var e2 = MakeEvent(GameEventType.RoundWin, now);
        Assert.True(router.Route(e2));
        Assert.Equal(2, fake.Routed.Count);
    }

    [Fact]
    public void SetConfig_OverridesDefaultConfig()
    {
        var fake = new FakeCelebration();
        var router = new GameEventRouter(() => fake);

        var custom = new GameEventCelebrationConfig { Priority = 999, CooldownMs = 0 };
        router.SetConfig(GameEventType.Kill, custom);

        var cfg = router.GetConfig(GameEventType.Kill);
        Assert.Equal(999, cfg.Priority);
    }

    [Fact]
    public void GetConfig_ReturnsDefaults_ForUnknownEvent()
    {
        var fake = new FakeCelebration();
        var router = new GameEventRouter(() => fake);

        // GameEventType.Test is not in defaults
        var cfg = router.GetConfig(GameEventType.Test);
        Assert.NotNull(cfg);
        Assert.Equal(0, cfg.Priority); // default is 0 for unknown
    }

    [Fact]
    public void ReplaceLowerPriority_HigherPriority_Preempts()
    {
        var fake = new FakeCelebration { ForceBusy = true, ActivePriority = 10 };
        var router = new GameEventRouter(() => fake);
        router.SetConflictPolicy(GameEventConflictPolicy.ReplaceLowerPriority);

        var e = MakeEvent(GameEventType.Ace); // priority 50 in defaults > 10
        var result = router.Route(e);
        Assert.Equal(1, fake.PreemptCallCount);
        Assert.True(result); // TryPreempt succeeded, TriggerGameEvent called on... but ForceBusy=true
        // Actually, ForceBusy=true means TriggerGameEvent returns false. So Route returns false.
        // The key assertion is that TryPreempt was called.
    }

    [Fact]
    public void ReplaceLowerPriority_LowerPriority_Suppressed()
    {
        var fake = new FakeCelebration { ForceBusy = true, ActivePriority = 100 };
        var router = new GameEventRouter(() => fake);
        router.SetConflictPolicy(GameEventConflictPolicy.ReplaceLowerPriority);

        // Kill has priority 10 in defaults, which is < 100
        var e = MakeEvent(GameEventType.Kill);
        var result = router.Route(e);
        Assert.False(result);
        Assert.Equal(1, fake.PreemptCallCount);
        Assert.Empty(fake.Routed);
    }

    [Fact]
    public void IgnoreWhileCelebrating_SuppressesWhenBusy()
    {
        var fake = new FakeCelebration { ForceBusy = true };
        var router = new GameEventRouter(() => fake);
        router.SetConflictPolicy(GameEventConflictPolicy.IgnoreWhileCelebrating);

        var e = MakeEvent(GameEventType.Ace);
        var result = router.Route(e);
        Assert.False(result);
        Assert.Empty(fake.Routed);
    }

    [Fact]
    public void Queue_Policy_QueuesWhenBusy()
    {
        var fake = new FakeCelebration { ForceBusy = true };
        var router = new GameEventRouter(() => fake);
        router.SetConflictPolicy(GameEventConflictPolicy.Queue);

        var e = MakeEvent(GameEventType.Kill);
        var result = router.Route(e);
        Assert.True(result);
        Assert.Single(fake.Queued);
        Assert.Empty(fake.Routed);
    }

    [Fact]
    public void StackPolicy_AllowsMultipleWhenNotBusy()
    {
        var fake = new FakeCelebration { ForceBusy = false };
        var router = new GameEventRouter(() => fake);
        router.SetConflictPolicy(GameEventConflictPolicy.Stack);

        Assert.True(router.Route(MakeEvent(GameEventType.Kill)));
        Assert.True(router.Route(MakeEvent(GameEventType.RoundWin)));
        Assert.Equal(2, fake.Routed.Count);
    }
}

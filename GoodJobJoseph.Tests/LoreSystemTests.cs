using System;
using System.Linq;
using JosephExperience.Lore;
using JosephExperience.Services;
using Xunit;

namespace GoodJobJoseph.Tests;

/// <summary>Validates the lore engine: data integrity, gating, context filtering, cooldowns, milestones.</summary>
public class LoreSystemTests
{
    [Fact]
    public void Data_UniqueIds()
    {
        var ids = JosephLoreData.All.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Data_UniqueTexts()
    {
        var texts = JosephLoreData.All.Select(e => e.Text).ToList();
        Assert.Equal(texts.Count, texts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Data_NoEmptyTexts_OrIds()
    {
        Assert.All(JosephLoreData.All, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Id), $"Empty id: {e.Id}");
            Assert.False(string.IsNullOrWhiteSpace(e.Text), $"Empty text: {e.Id}");
            Assert.True(e.Weight > 0, $"Non-positive weight: {e.Id}");
            Assert.True(e.MinCooldownSeconds >= 0, $"Negative cooldown: {e.Id}");
        });
    }

    [Fact]
    public void Data_NoJosephCoinsReferences()
    {
        Assert.All(JosephLoreData.All, e =>
            Assert.DoesNotContain("coin", e.Text, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Data_LargeEnoughPools()
    {
        Assert.True(JosephLoreData.All.Length >= 150, "Expected 150+ entries, got " + JosephLoreData.All.Length);
        Assert.All(JosephLoreData.All, e => Assert.True(e.Text.Length <= 120, $"Too long: {e.Id}"));
    }

    [Fact]
    public void TextOff_ReturnsNull()
    {
        JosephLoreService.ResetForTests();
        Assert.Null(JosephLoreService.Pick(LoreTrigger.Generic, new LoreContext(), textEnabled: false));
        Assert.Null(JosephLoreService.Pick(LoreTrigger.Cs2Kill, new LoreContext { GameId = "cs2" }, textEnabled: false));
    }

    [Fact]
    public void TextOn_ReturnsSomething()
    {
        JosephLoreService.ResetForTests();
        var line = JosephLoreService.Pick(LoreTrigger.Generic, new LoreContext(), textEnabled: true);
        Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void Cs2Context_NeverReturnsNonCs2GamePools()
    {
        JosephLoreService.ResetForTests();
        for (int i = 0; i < 30; i++)
        {
            var line = JosephLoreService.Pick(LoreTrigger.Cs2Kill,
                new LoreContext { GameId = "cs2", EventType = "Kill", Success = true });
            if (line is null) continue;
            var entry = JosephLoreData.All.FirstOrDefault(e => e.Text == line);
            Assert.NotNull(entry);
            Assert.NotEqual(LoreCategory.HalfLife2, entry!.Category);
            Assert.NotEqual(LoreCategory.Mw22009, entry.Category);
        }
    }

    [Fact]
    public void SameLine_NotImmediatelyRepeated()
    {
        JosephLoreService.ResetForTests();
        // Force small pools by using a very specific trigger.
        var lines = Enumerable.Range(0, 20)
            .Select(_ => JosephLoreService.Pick(LoreTrigger.Cs2Multikill,
                new LoreContext { GameId = "cs2", EventType = "Multikill", Success = true }))
            .Where(l => l != null).ToList();
        // With cooldowns, exact back-to-back repeats are disallowed.
        for (int i = 1; i < lines.Count; i++)
        {
            // Allow nulls (all cooldowns exhausted), but two consecutive identical non-null lines are a bug.
            if (lines[i] is not null && lines[i - 1] is not null)
                Assert.NotEqual(lines[i - 1], lines[i]);
        }
    }

    [Fact]
    public void Legendary_HasLongCooldown()
    {
        Assert.All(JosephLoreData.All.Where(e => e.Rarity == LoreRarity.Legendary),
            e => Assert.True(e.MinCooldownSeconds >= 60, $"Legendary cooldown too short: {e.Id}"));
    }

    [Fact]
    public void Milestone_ShowsOnce()
    {
        JosephLoreService.ResetForTests();
        var ms = JosephLoreData.All.FirstOrDefault(e => e.Category == LoreCategory.Milestone);
        if (ms is null) return;
        var ctx = new LoreContext { Count = ms.MinCount };
        var first = JosephLoreService.Pick(LoreTrigger.Milestone, ctx);
        Assert.True(first is not null || JosephLoreService.Pick(LoreTrigger.Milestone, ctx) is null);
        // After one milestone fires, it must never fire again.
        if (first is not null)
            Assert.NotEqual(first, JosephLoreService.Pick(LoreTrigger.Milestone, ctx));
    }

    [Fact]
    public void Data_GamePools_CarryGameId()
    {
        Assert.All(JosephLoreData.All.Where(e => e.Category is LoreCategory.HalfLife2),
            e => Assert.Equal("hl2", e.GameId, ignoreCase: true));
        Assert.All(JosephLoreData.All.Where(e => e.Category is LoreCategory.Mw22009),
            e => Assert.Equal("mw2", e.GameId, ignoreCase: true));
    }
}



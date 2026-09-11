using System;
using System.Collections.Generic;
using System.Linq;
using JosephExperience.Lore;

namespace JosephExperience.Services;

/// <summary>Central lore engine: contextual selection, rarity weighting, cooldowns, milestones, text-off gate.</summary>
public static class JosephLoreService
{
    private static readonly Dictionary<string, double> _lastShownUtc = new();
    private static readonly Dictionary<string, double> _categoryLastShownUtc = new();
    private static readonly HashSet<string> _shownMilestones = new();

    private static double NowUtc() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Pick a lore line honoring the hard text-off gate and contextual priority.</summary>
    public static string? Pick(LoreTrigger trigger, LoreContext? context = null, bool textEnabled = true)
    {
        if (!textEnabled) return null; // Hard gate: global text OFF means no lore anywhere.
        context ??= new LoreContext();

        var candidates = SelectCandidates(trigger, context);
        var entry = WeightedPick(candidates);
        if (entry is null) return null;

        RecordShown(entry);
        return entry.Text;
    }

    private static LoreEntry[] SelectCandidates(LoreTrigger trigger, LoreContext context)
    {
        var list = new List<LoreEntry>();
        foreach (var e in JosephLoreData.All)
        {
            if (e.Enabled && MeetsConditions(e, trigger, context)) list.Add(e);
        }

        if (list.Count == 0)
        {
            // Generic fallback pool only as last resort.
            foreach (var e in JosephLoreData.All)
            {
                if (e.Enabled && e.Category == LoreCategory.General && CooldownOk(e, NowUtc())) list.Add(e);
            }
        }
        return list.ToArray();
    }

    private static bool MeetsConditions(LoreEntry e, LoreTrigger trigger, LoreContext ctx)
    {
        if (e.Category == LoreCategory.Milestone && e.Trigger == LoreTrigger.Milestone)
        {
            return ctx.Count >= e.MinCount && ctx.Count <= e.MaxCount && !_shownMilestones.Contains(e.Id);
        }

        if (trigger != LoreTrigger.None && e.Trigger != LoreTrigger.None)
        {
            if (e.Trigger != trigger) return false;
        }

        if (!string.IsNullOrEmpty(e.GameId) && string.Equals(e.GameId, ctx.GameId, StringComparison.OrdinalIgnoreCase) is false)
        {
            return false;
        }
        if (e.RequiresSuccess && !ctx.Success) return false;
        if (e.RequiresFailure && !ctx.Failure) return false;
        if (e.RequiresReconnect && !ctx.Reconnect) return false;
        if (e.RequiresLongSession && !ctx.LongSession) return false;
        if (e.RequiresMarketPositive && ctx.Market != MarketBias.Positive) return false;
        if (e.RequiresMarketNegative && ctx.Market != MarketBias.Negative) return false;
        return true;
    }

    private static LoreEntry? WeightedPick(LoreEntry[] candidates)
    {
        var now = NowUtc();
        var eligible = candidates.Where(e => CooldownOk(e, now)).ToArray();
        if (eligible.Length == 0) return null;

        long total = 0;
        foreach (var e in eligible) total += e.Weight;
        if (total <= 0) return eligible[Random.Shared.Next(eligible.Length)];

        var roll = Random.Shared.NextInt64(total);
        long acc = 0;
        foreach (var e in eligible)
        {
            acc += e.Weight;
            if (roll < acc) return e;
        }
        return eligible[eligible.Length - 1];
    }

    private static bool CooldownOk(LoreEntry e, double now)
    {
        if (_lastShownUtc.TryGetValue(e.Id, out var lastEntry) &&
            now - lastEntry < e.MinCooldownSeconds) return false;

        // Category cooldown prevents two lines from the same pool back-to-back.
        var catCooldown = e.Rarity switch
        {
            LoreRarity.Legendary => 90,
            LoreRarity.Rare => 20,
            _ => 4
        };
        if (_categoryLastShownUtc.TryGetValue(e.Category.ToString(), out var lastCat) &&
            now - lastCat < catCooldown) return false;
        return true;
    }

    private static void RecordShown(LoreEntry e)
    {
        var now = NowUtc();
        _lastShownUtc[e.Id] = now;
        _categoryLastShownUtc[e.Category.ToString()] = now;
        if (e.Category == LoreCategory.Milestone) _shownMilestones.Add(e.Id);
    }

    /// <summary>Register a milestone as shown so it won't repeat.</summary>
    public static void MarkMilestoneShown(string id) => _shownMilestones.Add(id);
    public static bool MilestoneWasShown(string id) => _shownMilestones.Contains(id);

    /// <summary>Reset runtime memory (tests).</summary>
    public static void ResetForTests()
    {
        _lastShownUtc.Clear();
        _categoryLastShownUtc.Clear();
        _shownMilestones.Clear();
    }
}
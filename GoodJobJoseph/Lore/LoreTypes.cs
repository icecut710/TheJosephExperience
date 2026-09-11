using System;

namespace JosephExperience.Lore;

public enum LoreRarity { Common, Uncommon, Rare, Legendary }

public enum LoreCategory
{
    General, ItWizard, RouterRestoration, Shawarma, HondaCivic, MassageChair,
    PrinterBossFight, MaximumNaddaf, Cs2, HalfLife2, Mw22009, CloudSync,
    MarketNadd, Audio, Error, Recovery, Updater, Milestone, Rare, Legendary
}

public enum LoreTrigger
{
    None, Generic, Cs2Kill, Cs2Death, Cs2RoundWin, Cs2BombDefuse, Cs2BombPlant,
    Cs2Mvp, Cs2Multikill, HalfLife2Event, Mw22009Event, CloudSyncSuccess,
    CloudSyncFailure, CloudReconnect, MarketPositive, MarketNegative, MarketFlat,
    AudioPlayed, Error, Recovery, UpdateAvailable, UpdateInstalled, Milestone
}

/// <summary>A single curated lore entry with selection metadata.</summary>
public sealed class LoreEntry
{
    public readonly string Id;
    public readonly string Text;
    public readonly LoreCategory Category;
    public readonly LoreRarity Rarity;
    public readonly string[] Tags;
    public readonly int MinCooldownSeconds;
    public readonly int Weight;
    public readonly LoreTrigger Trigger;
    public readonly string GameId;
    public readonly string EventType;
    public readonly int MinCount;
    public readonly int MaxCount;
    public readonly bool RequiresSuccess;
    public readonly bool RequiresFailure;
    public readonly bool RequiresReconnect;
    public readonly bool RequiresLongSession;
    public readonly bool RequiresMarketPositive;
    public readonly bool RequiresMarketNegative;
    public readonly bool Enabled;

    /// <summary>Convenience constructor with sensible defaults for the common pool.</summary>
    public LoreEntry(string id, string text, LoreCategory category, LoreRarity rarity,
        string[]? tags = null, int minCooldownSeconds = 30, int weight = 100,
        LoreTrigger trigger = LoreTrigger.None, string gameId = "", string eventType = "",
        int minCount = 1, int maxCount = int.MaxValue, bool requiresSuccess = false,
        bool requiresFailure = false, bool requiresReconnect = false, bool requiresLongSession = false,
        bool requiresMarketPositive = false, bool requiresMarketNegative = false, bool enabled = true)
    {
        Id = id; Text = text; Category = category; Rarity = rarity; Tags = tags ?? Array.Empty<string>();
        MinCooldownSeconds = minCooldownSeconds; Weight = weight; Trigger = trigger;
        GameId = gameId; EventType = eventType; MinCount = minCount; MaxCount = maxCount;
        RequiresSuccess = requiresSuccess; RequiresFailure = requiresFailure;
        RequiresReconnect = requiresReconnect; RequiresLongSession = requiresLongSession;
        RequiresMarketPositive = requiresMarketPositive; RequiresMarketNegative = requiresMarketNegative;
        Enabled = enabled;
    }
}
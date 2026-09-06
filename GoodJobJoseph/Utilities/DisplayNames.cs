using System.Collections.Concurrent;
using System.Reflection;
using JosephExperience.Models;

namespace JosephExperience.Utilities;

/// <summary>
/// Maps enum values to friendly, human-readable display names.
/// Falls back to the raw member name when no friendly name is registered.
/// </summary>
public static class DisplayNames
{
    private static readonly ConcurrentDictionary<Type, Dictionary<string, string>> _cache = new();

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns a friendly display name for the given enum value,
    /// or the raw ToString() when no friendly name is registered.
    /// </summary>
    public static string GetFriendlyName(Enum value)
    {
        if (value is null) return string.Empty;
        var type = value.GetType();
        var dict = GetTable(type);
        return dict.TryGetValue(value.ToString(), out var friendly) ? friendly : value.ToString();
    }

    /// <summary>
    /// Generic overload constrained to enum types.
    /// </summary>
    public static string GetFriendlyName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var type = typeof(TEnum);
        var dict = GetTable(type);
        return dict.TryGetValue(value.ToString(), out var friendly) ? friendly : value.ToString();
    }

    /// <summary>
    /// Returns all enum member names with their friendly display names as
    /// (rawName, friendlyName) pairs, ordered by enum declaration order.
    /// </summary>
    public static List<(string Raw, string Friendly)> GetMembers<TEnum>()
        where TEnum : struct, Enum
    {
        var type = typeof(TEnum);
        var dict = GetTable(type);
        return Enum.GetNames<TEnum>()
                   .Select(n => (n, dict.TryGetValue(n, out var f) ? f : n))
                   .ToList();
    }

    /// <summary>
    /// Returns the friendly name whose raw enum name contains the search string
    /// (case-insensitive). Used to resolve a display name back to the combo item.
    /// </summary>
    public static string? FindFriendlyName<TEnum>(string search)
        where TEnum : struct, Enum
    {
        var type = typeof(TEnum);
        var dict = GetTable(type);
        foreach (var kvp in dict)
        {
            if (kvp.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Tables
    // ------------------------------------------------------------------

    private static Dictionary<string, string> GetTable(Type type)
    {
        return _cache.GetOrAdd(type, t =>
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_displayMaps.TryGetValue(t, out var registered))
            {
                foreach (var kvp in registered)
                    dict[kvp.Key] = kvp.Value;
                return dict;
            }
            // Fallback: use raw member names.
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                dict[f.Name] = f.Name;
            }
            return dict;
        });
    }

    // ------------------------------------------------------------------
    // Static display maps — the authoritative source for friendly names.
    // Each entry maps the raw enum ToString() value to a human-friendly label.
    // ------------------------------------------------------------------

    private static readonly Dictionary<Type, Dictionary<string, string>> _displayMaps = new();

    static DisplayNames()
    {
        Register<AnimationStyle>(new()
        {
            ["None"]       = "None",
            ["Fade"]       = "Fade",
            ["Pop"]        = "Pop",
            ["ScaleIn"]    = "Scale In",
            ["Bounce"]     = "Bounce",
            ["SlideLeft"]  = "Slide Left",
            ["SlideRight"] = "Slide Right",
            ["SlideUp"]    = "Slide Up",
            ["SlideDown"]  = "Slide Down",
            ["SpinIn"]     = "Spin In",
            ["SpinOut"]    = "Spin Out",
            ["DropIn"]     = "Drop In",
            ["RiseIn"]     = "Rise In",
            ["ZoomIn"]     = "Zoom In",
            ["ZoomOut"]    = "Zoom Out",
            ["Pulse"]      = "Pulse",
            ["Wobble"]     = "Wobble",
            ["Shake"]      = "Shake",
            ["Jumpscare"]  = "Jumpscare",
            ["Elastic"]    = "Elastic",
            ["Overshoot"]  = "Overshoot",
            ["Random"]     = "Random"
        });

        Register<ExitStyle>(new()
        {
            ["None"]         = "None",
            ["Fade"]         = "Fade",
            ["Shrink"]       = "Shrink",
            ["Slide"]        = "Slide",
            ["ReverseEntry"] = "Reverse Entry",
            ["Random"]       = "Random"
        });

        Register<EasingStyle>(new()
        {
            ["Linear"]     = "Linear",
            ["EaseIn"]     = "Ease In",
            ["EaseOut"]    = "Ease Out",
            ["EaseInOut"]  = "Ease In Out",
            ["Back"]       = "Back",
            ["Elastic"]    = "Elastic",
            ["Bounce"]     = "Bounce"
        });

        Register<EntrySpeed>(new()
        {
            ["Slow"]    = "Slow",
            ["Normal"]  = "Normal",
            ["Fast"]    = "Fast",
            ["Instant"] = "Instant"
        });

        Register<ImagePosition>(new()
        {
            ["Center"]            = "Center",
            ["TopLeft"]           = "Top Left",
            ["TopCenter"]         = "Top Center",
            ["TopRight"]          = "Top Right",
            ["CenterLeft"]        = "Center Left",
            ["CenterRight"]       = "Center Right",
            ["BottomLeft"]        = "Bottom Left",
            ["BottomCenter"]      = "Bottom Center",
            ["BottomRight"]       = "Bottom Right",
            ["CursorPosition"]    = "Cursor Position",
            ["ActiveMonitorCenter"] = "Monitor Center",
            ["Custom"]            = "Custom..."
        });

        Register<FitMode>(new()
        {
            ["Natural"]    = "Natural Size",
            ["Contain"]    = "Contain",
            ["FillScreen"] = "Fill Screen",
            ["Stretch"]    = "Stretch",
            ["ActualSize"] = "Actual Size",
            ["CustomSize"] = "Custom Size"
        });

        Register<ImageMode>(new()
        {
            ["Random"]           = "Random Carousel",
            ["FavoritesOnly"]    = "Favorites Only",
            ["Specific"]         = "Specific Image",
            ["Weighted"]         = "Weighted Random",
            ["LeastRecentlyUsed"] = "Least Recently Used",
            ["ShuffleBag"]       = "Shuffle Bag",
            ["RecentlyAdded"]    = "Recently Added",
            ["RandomCategory"]   = "Random Category"
        });

        Register<MonitorMode>(new()
        {
            ["Primary"]             = "Primary Monitor",
            ["MonitorUnderCursor"]  = "Monitor Under Cursor",
            ["ActiveWindowMonitor"] = "Active Window",
            ["Monitor1"]            = "Monitor 1",
            ["Monitor2"]            = "Monitor 2",
            ["Monitor3"]            = "Monitor 3"
        });

        Register<TextMode>(new()
        {
            ["AssignedQuote"] = "Assigned Quote",
            ["RandomQuote"]   = "Random Quote",
            ["CustomGlobal"]  = "Custom Text",
            ["NoText"]        = "No Text"
        });

        Register<TextFxStyle>(new()
        {
            ["None"]      = "None",
            ["Fade"]      = "Fade",
            ["Impact"]    = "Impact",
            ["Pop"]       = "Pop",
            ["Bounce"]    = "Bounce",
            ["Shake"]     = "Shake",
            ["Typewriter"] = "Typewriter",
            ["Stamp"]     = "Stamp",
            ["Random"]    = "Random"
        });

        Register<TextPosition>(new()
        {
            ["AboveImage"]     = "Above Image",
            ["BelowImage"]     = "Below Image",
            ["OverlayTop"]     = "Overlay Top",
            ["OverlayCenter"]  = "Overlay Center",
            ["OverlayBottom"]  = "Overlay Bottom",
            ["Left"]           = "Left",
            ["Right"]          = "Right",
            ["Hidden"]         = "Hidden"
        });

        Register<TextShadowStyle>(new()
        {
            ["Off"]         = "Off",
            ["SoftShadow"]  = "Soft Shadow",
            ["StrongShadow"] = "Strong Shadow",
            ["ThinOutline"] = "Thin Outline",
            ["ThickOutline"] = "Thick Outline"
        });

        Register<TextAnimationStyle>(new()
        {
            ["FollowImage"] = "Follow Image",
            ["FadeOnly"]    = "Fade Only",
            ["Static"]      = "Static",
            ["Bounce"]      = "Bounce"
        });

        Register<TextOutline>(new()
        {
            ["None"]   = "None",
            ["Thin"]   = "Thin",
            ["Medium"] = "Medium",
            ["Thick"]  = "Thick"
        });

        Register<FxIntensity>(new()
        {
            ["Subtle"]  = "Subtle",
            ["Normal"]  = "Normal",
            ["Strong"]  = "Strong",
            ["Unhinged"] = "Unhinged"
        });

        Register<CelebrationPreset>(new()
        {
            ["ClassicJoseph"]       = "Classic Joseph",
            ["ITWizard"]            = "IT Wizard",
            ["ShawarmaMode"]        = "Shawarma Mode",
            ["CivicDeployment"]     = "Civic Deployment",
            ["MassageChairRecovery"] = "Massage Chair",
            ["PrinterBossFight"]    = "Printer Boss Fight",
            ["MaximumNaddaf"]       = "Maximum Naddaf",
            ["CompletelyRandom"]    = "Completely Random"
        });

        Register<TextWeight>(new()
        {
            ["Normal"]     = "Normal",
            ["SemiBold"]   = "Semi Bold",
            ["Bold"]       = "Bold",
            ["ExtraBold"]  = "Extra Bold"
        });

        Register<FontSizePreset>(new()
        {
            ["Small"]  = "Small",
            ["Medium"] = "Medium",
            ["Large"]  = "Large",
            ["Huge"]   = "Huge",
            ["Absurd"] = "Absurd"
        });

        Register<GridDensity>(new()
        {
            ["Compact"] = "Compact",
            ["Normal"]  = "Normal",
            ["Large"]   = "Large"
        });

        Register<LibrarySortMode>(new()
        {
            ["Name"]             = "Name",
            ["Newest"]           = "Newest",
            ["Oldest"]           = "Oldest",
            ["MostUsed"]         = "Most Used",
            ["LeastUsed"]        = "Least Used",
            ["RecentlyShown"]    = "Recently Shown",
            ["FavoritesFirst"]   = "Favorites First",
            ["Random"]           = "Random"
        });

        Register<LibraryFilterMode>(new()
        {
            ["All"]              = "All",
            ["Enabled"]          = "Enabled",
            ["Disabled"]         = "Disabled",
            ["Favorites"]        = "Favorites",
            ["Cloud"]            = "Cloud",
            ["Local"]            = "Local Only",
            ["Broken"]           = "Broken",
            ["MissingCache"]     = "Missing Cache",
            ["RecentlyAdded"]    = "Recently Added",
            ["RecentlyUsed"]     = "Recently Used",
            ["Category"]         = "Category"
        });

        Register<CloseBehavior>(new()
        {
            ["MinimizeToTray"] = "Minimize to Tray",
            ["Exit"]           = "Exit",
            ["Ask"]            = "Ask"
        });

        Register<LaunchBehavior>(new()
        {
            ["Normal"]     = "Normal",
            ["Minimized"]  = "Minimized",
            ["HiddenToTray"] = "Hidden (Tray)"
        });

        Register<SyncFrequency>(new()
        {
            ["ManualOnly"]      = "Manual Only",
            ["OnStartup"]       = "On Startup",
            ["Every5Minutes"]   = "Every 5 Minutes",
            ["Every15Minutes"]  = "Every 15 Minutes",
            ["Every30Minutes"]  = "Every 30 Minutes",
            ["Every1Hour"]      = "Every 1 Hour"
        });

        Register<SafeMarginPreset>(new()
        {
            ["None"]   = "None",
            ["Small"]  = "Small",
            ["Medium"] = "Medium",
            ["Large"]  = "Large",
            ["Custom"] = "Custom..."
        });

        Register<SizePreset>(new()
        {
            ["Tiny"]    = "Tiny",
            ["Small"]   = "Small",
            ["Medium"]  = "Medium",
            ["Large"]   = "Large",
            ["Huge"]    = "Huge",
            ["Massive"] = "Massive",
            ["Custom"]  = "Custom..."
        });

        Register<CacheSizeLimit>(new()
        {
            ["Off"]    = "Off",
            ["MB250"]  = "250 MB",
            ["MB500"]  = "500 MB",
            ["GB1"]    = "1 GB",
            ["GB2"]    = "2 GB",
            ["Custom"] = "Custom..."
        });

        Register<EvictionStrategy>(new()
        {
            ["OldestUnused"]       = "Oldest Unused",
            ["LeastRecentlyUsed"]  = "Least Recently Used",
            ["KeepFavorites"]      = "Keep Favorites",
            ["NeverEvict"]         = "Never Evict"
        });

        Register<DurationPreset>(new()
        {
            ["S0_5"]    = "0.5 sec",
            ["S0_75"]   = "0.75 sec",
            ["S1_0"]    = "1.0 sec",
            ["S1_5"]    = "1.5 sec",
            ["S1_8"]    = "1.8 sec",
            ["S2_0"]    = "2.0 sec",
            ["S3_0"]    = "3.0 sec",
            ["S5_0"]    = "5.0 sec",
            ["Custom"]  = "Custom..."
        });

        Register<SoundMode>(new()
        {
            ["AssignedSound"] = "Assigned Sound",
            ["RandomSound"]   = "Random Sound",
            ["NoSound"]       = "No Sound"
        });

        Register<AudioStopPolicy>(new()
        {
            ["StopPrevious"]       = "Stop Previous",
            ["AllowOverlapping"]   = "Allow Overlapping"
        });

        Register<AudioMaxDuration>(new()
        {
            ["Seconds5"]   = "5 seconds",
            ["Seconds10"]  = "10 seconds",
            ["Seconds15"]  = "15 seconds",
            ["Seconds20"]  = "20 seconds",
            ["Seconds30"]  = "30 seconds",
            ["Unlimited"]  = "Unlimited",
            ["Custom"]     = "Custom..."
        });
    }

    private static void Register<T>(Dictionary<string, string> map)
        where T : struct, Enum
    {
        _displayMaps[typeof(T)] = map;
    }
}

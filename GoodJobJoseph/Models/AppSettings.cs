using System.Windows.Media;

namespace JosephExperience.Models;

public enum ImagePosition
{
    Center,
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    CursorPosition,
    ActiveMonitorCenter,
    Custom
}

    public enum AnimationStyle
{
    None,
    Fade,
    Pop,
    ScaleIn,
    Bounce,
    SlideLeft,
    SlideRight,
    SlideUp,
    SlideDown,
    SpinIn,
    SpinOut,
    DropIn,
    RiseIn,
    ZoomIn,
    ZoomOut,
    Pulse,
    Wobble,
    Shake,
    Jumpscare,
    Elastic,
    Overshoot,
    Random
}

public enum ImageMode
{
    Random,
    FavoritesOnly,
    Specific,
    Weighted,
    LeastRecentlyUsed,
    ShuffleBag,
    RecentlyAdded,
    RandomCategory
}

public enum MonitorMode
{
    Primary,
    MonitorUnderCursor,
    ActiveWindowMonitor,
    Monitor1,
    Monitor2,
    Monitor3
}

public enum TextPosition
{
    AboveImage,
    BelowImage,
    OverlayTop,
    OverlayCenter,
    OverlayBottom,
    Left,
    Right,
    Hidden
}

public enum FitMode
{
    Natural,
    Contain,
    FillScreen,
    Stretch,
    ActualSize,
    CustomSize
}

public enum EasingStyle
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    Back,
    Elastic,
    Bounce
}

public enum EntrySpeed
{
    Slow,
    Normal,
    Fast,
    Instant
}

public enum ExitStyle
{
    None,
    Fade,
    Shrink,
    Slide,
    ReverseEntry,
    Random
}

public enum TextShadowStyle
{
    Off,
    SoftShadow,
    StrongShadow,
    ThinOutline,
    ThickOutline
}

public enum TextAnimationStyle
{
    FollowImage,
    FadeOnly,
    Static,
    Bounce
}

public enum TextMode
{
    AssignedQuote,
    RandomQuote,
    CustomGlobal,
    NoText
}

public enum TextFxStyle
{
    None,
    Fade,
    Impact,
    Pop,
    Bounce,
    Shake,
    Typewriter,
    Stamp,
    Random
}

public enum FontSizePreset
{
    Small,
    Medium,
    Large,
    Huge,
    Absurd
}

public enum TextOutline
{
    None,
    Thin,
    Medium,
    Thick
}

public enum FxIntensity
{
    Subtle,
    Normal,
    Strong,
    Unhinged
}

public enum CelebrationPreset
{
    ClassicJoseph,
    ITWizard,
    ShawarmaMode,
    CivicDeployment,
    MassageChairRecovery,
    PrinterBossFight,
    MaximumNaddaf,
    CompletelyRandom
}

public enum TextWeight
{
    Normal,
    SemiBold,
    Bold,
    ExtraBold
}

public enum GridDensity
{
    Compact,
    Normal,
    Large
}

public enum LibrarySortMode
{
    Name,
    Newest,
    Oldest,
    MostUsed,
    LeastUsed,
    RecentlyShown,
    FavoritesFirst,
    Random
}

public enum LibraryFilterMode
{
    All,
    Enabled,
    Disabled,
    Favorites,
    Cloud,
    Local,
    Broken,
    MissingCache,
    RecentlyAdded,
    RecentlyUsed,
    Category
}

public enum CloseBehavior
{
    MinimizeToTray,
    Exit,
    Ask
}

public enum LaunchBehavior
{
    Normal,
    Minimized,
    HiddenToTray
}

public enum SyncFrequency
{
    ManualOnly,
    OnStartup,
    Every5Minutes,
    Every15Minutes,
    Every30Minutes,
    Every1Hour
}

public enum SafeMarginPreset
{
    None,
    Small,
    Medium,
    Large,
    Custom
}

public enum SizePreset
{
    Tiny,
    Small,
    Medium,
    Large,
    Huge,
    Massive,
    Custom
}

public enum AssetHealth
{
    Ready,
    MissingCache,
    Downloading,
    Broken,
    Disabled,
    Deleted
}

public class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public LaunchBehavior LaunchBehavior { get; set; } = LaunchBehavior.Normal;
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    public uint HotkeyModifierValue { get; set; }
    public uint HotkeyVirtualKey { get; set; } = 0x71;   // VK_F2
    public string HotkeyKeyName { get; set; } = "F2";
    public string HotkeyModifiers { get; set; } = "None";
    public string HotkeyKey { get; set; } = "F2";

    public double ImageOpacity { get; set; } = 1.0;
    public int OverlayDurationMs { get; set; } = 800;
    public DurationPreset DurationPreset { get; set; } = DurationPreset.S1_8;
    public double OverlayScale { get; set; } = 1.0;
    public SizePreset SizePreset { get; set; } = SizePreset.Medium;
    public double CustomScalePercent { get; set; } = 100;
    public ImagePosition ImagePosition { get; set; } = ImagePosition.Center;
    public double CustomPositionX { get; set; } = 0.5;
    public double CustomPositionY { get; set; } = 0.5;
    public SafeMarginPreset SafeMarginPreset { get; set; } = SafeMarginPreset.Small;
    public double SafeMarginPixels { get; set; } = 12;
    public MonitorMode MonitorMode { get; set; } = MonitorMode.Primary;
    public FitMode FitMode { get; set; } = FitMode.FillScreen;
    public bool LockAspectRatio { get; set; } = true;
    public bool TrimTransparentBounds { get; set; } = false;
    public double CustomFitScale { get; set; } = 1.0;

    public AnimationStyle AnimationStyle { get; set; } = AnimationStyle.Fade;
    public bool RandomAnimation { get; set; }
    public bool ExcludeJumpscareFromRandom { get; set; } = true;
    public bool ExcludeNoneFromRandom { get; set; } = true;
    public EasingStyle EasingStyle { get; set; } = EasingStyle.EaseInOut;
    public EntrySpeed EntrySpeed { get; set; } = EntrySpeed.Normal;
    public ExitStyle ExitStyle { get; set; } = ExitStyle.Fade;
    public int EntryDurationMs { get; set; } = 0;
    public int ExitDurationMs { get; set; } = 0;

    public bool ShowCelebrationText { get; set; }
    public string CelebrationText { get; set; } = "The Joseph Experience 2.0";
    public TextMode TextMode { get; set; } = TextMode.AssignedQuote;
    public TextFxStyle TextFx { get; set; } = TextFxStyle.Impact;
    public FxIntensity FxIntensity { get; set; } = FxIntensity.Normal;
    public bool SynchronizeFx { get; set; } = true;
    public CelebrationPreset Preset { get; set; } = CelebrationPreset.CompletelyRandom;
    public double TextFontSize { get; set; } = 48;
    public FontSizePreset TextFontSizePreset { get; set; } = FontSizePreset.Large;
    public TextWeight TextWeight { get; set; } = TextWeight.Bold;
    public double TextOpacity { get; set; } = 0.92;
    public TextPosition TextPosition { get; set; } = TextPosition.BelowImage;
    public TextAlignment TextHorizontalAlignment { get; set; } = TextAlignment.Center;
    public TextShadowStyle TextShadow { get; set; } = TextShadowStyle.SoftShadow;
    public TextOutline TextOutline { get; set; } = TextOutline.Medium;
    public TextAnimationStyle TextAnimation { get; set; } = TextAnimationStyle.FollowImage;
    public double TextScale { get; set; } = 1.0;
    public double TextDelayMs { get; set; } = 80;
    public double TextMaxWidth { get; set; } = 600;
    public Color TextColor { get; set; } = Colors.White;

    public ImageMode ImageMode { get; set; } = ImageMode.Random;
    public bool UseFavoritesOnly { get; set; }
    public bool AvoidImmediateRepeats { get; set; } = true;
    public int RepeatCooldown { get; set; } = 1;
    public bool LowDistraction { get; set; }
    public Guid? SpecificImageId { get; set; }
    public string? SelectedCategory { get; set; }

    // ---- 3D model overlay ----
    /// <summary>Which screen corner a spinning 3D model appears in.</summary>
    public OverlayCorner Model3DCorner { get; set; } = OverlayCorner.BottomRight;
    /// <summary>Scale of the 3D model relative to the smaller screen dimension (0.1â€"0.6).</summary>
    public double Model3DScale { get; set; } = 0.25;
    /// <summary>Rotation speed in degrees per second for the spinning 3D model.</summary>
    public double Model3DRotationSpeed { get; set; } = 45.0;
    /// <summary>Show a subtle reference grid floor under the 3D model.</summary>
        public bool Model3DShowGrid { get; set; }
    /// <summary>When true, a selected 3D model is rendered as a spinning model instead of a flat image.</summary>
    public bool Prefer3DModel { get; set; } = false;

    // ---- History ----
    /// <summary>Number of celebration history entries to retain (last N).</summary>
    public int CelebrationHistoryLimit { get; set; } = 50;
    /// <summary>Record 3D model celebrations in history (in addition to images).</summary>
    public bool Record3DModelHistory { get; set; } = true;

    public bool PlaySound { get; set; }
    public double SoundVolume { get; set; } = 1.0;
    public string? SelectedSound { get; set; }
    public string? SelectedSoundId { get; set; }

    /// <summary>
    /// How celebration sounds are picked: an assigned clip per image, a random
    /// clip from the sound library, or no sound at all. The master switch is
    /// <see cref="PlaySound"/> (the "Celebration Sounds" toggle).
    /// </summary>
    public SoundMode SoundMode { get; set; } = SoundMode.RandomSound;

    public AudioStopPolicy AudioStopPolicy { get; set; } = AudioStopPolicy.StopPrevious;
    public bool AudioAllowOverlapping { get; set; } = false;
    public AudioMaxDuration AudioMaxDuration { get; set; } = AudioMaxDuration.Seconds30;
    public int CustomAudioMaxSeconds { get; set; } = 30;

    public HotkeyBinding SoundCycleHotkey { get; set; } = new() { VirtualKey = 0x77, KeyName = "F8" };
    public HotkeyBinding StopAudioHotkey { get; set; } = new(); // unassigned by default
    public bool PreviewSoundWhenCycling { get; set; } = true;
    public HotkeyBinding SecondaryCelebrationHotkey { get; set; } = new();

    public SyncFrequency SyncFrequency { get; set; } = SyncFrequency.OnStartup;
    public string UpdateManifestUrl { get; set; } = "https://raw.githubusercontent.com/icecot710/TheJosephExperience/master/updates/update-manifest.json";
    public bool SyncOnStartup { get; set; } = true;
    public GridDensity GridDensity { get; set; } = GridDensity.Normal;
    public LibrarySortMode LibrarySort { get; set; } = LibrarySortMode.Name;
    public LibraryFilterMode LibraryDefaultFilter { get; set; } = LibraryFilterMode.All;
    public bool AutoRepairMissingCache { get; set; } = true;

    public CacheSizeLimit CacheSizeLimit { get; set; } = CacheSizeLimit.Off;
    public EvictionStrategy EvictionStrategy { get; set; } = EvictionStrategy.LeastRecentlyUsed;

    public bool DebugOverlayBounds { get; set; }

    // ---- CS:GO / CS2 game-state integration ----
    /// <summary>Enable automatic celebrations from CS:GO / CS2 GSI events.</summary>
    public bool GameIntegrationEnabled { get; set; }
    /// <summary>True once the user has successfully run Attach CS2. Surfaced in the UI as a persistent indicator.</summary>
    public bool Cs2Attached { get; set; }
    /// <summary>Path to the CS2 cfg folder the user attached to, for display and verification.</summary>
    public string? Cs2AttachPath { get; set; }
    /// <summary>UTC timestamp of the last successful attach.</summary>
    public DateTime? Cs2AttachedUtc { get; set; }
    /// <summary>HTTP port CS:GO / CS2 will POST gamestate JSON to. Must match the GSI cfg file.</summary>
    public int GameIntegrationPort { get; set; } = 3000;
    /// <summary>Auth token CS:GO / CS2 must include in payloads. Empty = no auth.</summary>
    public string GameIntegrationAuthToken { get; set; } = "";
    /// <summary>Celebrate when the local player gets a kill.</summary>
    public bool GameEvent_OnKill { get; set; } = true;
    /// <summary>Celebrate when the local player gets a headshot kill. Note: CS2 GSI does not reliably provide headshot info, so this event is not triggered.</summary>
    public bool GameEvent_OnHeadshot { get; set; } = true;
    /// <summary>Celebrate when the local player wins a 1vN clutch situation.</summary>
    public bool GameEvent_OnClutch { get; set; } = true;
    /// <summary>Celebrate when the local player dies.</summary>
    public bool GameEvent_OnDeath { get; set; } = true;
    /// <summary>Celebrate when the local player's team wins the round.</summary>
    public bool GameEvent_OnRoundWin { get; set; } = true;
    /// <summary>Celebrate on match victory (end of game).</summary>
    public bool GameEvent_OnMatchWin { get; set; } = true;
    /// <summary>Celebrate on multi-kills (double/triple/quad).</summary>
    public bool GameEvent_OnMultiKill { get; set; } = true;
    /// <summary>Celebrate on an ace (5 kills in a round).</summary>
    public bool GameEvent_OnAce { get; set; } = true;
    /// <summary>Celebrate when the bomb is planted.</summary>
    public bool GameEvent_OnBombPlant { get; set; } = false;
    /// <summary>Celebrate when the bomb is defused.</summary>
    public bool GameEvent_OnBombDefuse { get; set; } = true;
    /// <summary>Celebrate when the bomb explodes.</summary>
    public bool GameEvent_OnBombExplode { get; set; } = false;
    /// <summary>Rolling window (seconds) for multi-kill escalation.</summary>
    public int GameEventMultiKillWindowSeconds { get; set; } = 4;
    /// <summary>How overlapping multi-kill tiers fire.</summary>
    public string GameEventMultiKillBehavior { get; set; } = "HighestOnly";

    public JosephExperience.Models.CounterStrike.MultiKillBehavior MultiKillBehaviorParsed =>
        Enum.TryParse<JosephExperience.Models.CounterStrike.MultiKillBehavior>(GameEventMultiKillBehavior, true, out var b)
            ? b : JosephExperience.Models.CounterStrike.MultiKillBehavior.HighestOnly;
    /// <summary>Celebrate on MVP and ace (5 kills in a round) moments.</summary>
    public bool GameEvent_OnMvpAceClutch { get; set; } = true;
    /// <summary>Cooldown in ms between in-game celebrations (prevents spam during multi-kills).</summary>
    public int GameEventCooldownMs { get; set; } = 1200;
    /// <summary>Force a shorter / more transparent overlay for in-game triggers. Manual F2 always uses the full effect.</summary>
    public bool GameEventsLowDistraction { get; set; } = true;

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }

    public HotkeyBinding GetBinding(JosephExperience.Services.HotkeyAction action)
    {
        return action switch
        {
            JosephExperience.Services.HotkeyAction.Celebration =>
                new HotkeyBinding { ModifierValue = HotkeyModifierValue, VirtualKey = HotkeyVirtualKey, KeyName = HotkeyKeyName },
            JosephExperience.Services.HotkeyAction.SecondaryCelebration =>
                SecondaryCelebrationHotkey,
            JosephExperience.Services.HotkeyAction.CycleSound =>
                SoundCycleHotkey,
            JosephExperience.Services.HotkeyAction.StopAudio =>
                StopAudioHotkey,
            _ => new HotkeyBinding(),
        };
    }
}

public enum DurationPreset
{
    S0_5,
    S0_75,
    S1_0,
    S1_5,
    S1_8,
    S2_0,
    S3_0,
    S5_0,
    Custom
}

public enum TextAlignment
{
    Left,
    Center,
    Right
}

public enum CacheSizeLimit
{
    Off,
    MB250,
    MB500,
    GB1,
    GB2,
    Custom
}

public enum EvictionStrategy
{
    OldestUnused,
    LeastRecentlyUsed,
    KeepFavorites,
    NeverEvict
}

/// <summary>
/// How celebration audio is selected when the "Celebration Sounds" toggle is on.
/// </summary>
public enum SoundMode
{
    /// <summary>Use the clip assigned to the specific Joseph/image (falls back to random).</summary>
    AssignedSound,
    /// <summary>Pick a random clip from the imported sound library on each celebration.</summary>
    RandomSound,
    /// <summary>Never play audio during celebrations.</summary>
    NoSound
}

public enum AudioStopPolicy
{
    StopPrevious,
    AllowOverlapping
}

public enum AudioMaxDuration
{
    Seconds5,
    Seconds10,
    Seconds15,
    Seconds20,
    Seconds30,
    Unlimited,
    Custom
}

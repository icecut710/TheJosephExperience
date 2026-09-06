using System.IO;
using System.Text.Json;
using JosephExperience.Models;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly object _lock = new();
    private readonly object _writeGate = new();
    private Task? _lastWrite;

    public AppSettings Current { get; private set; }

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (loaded is not null)
                {
                    ClampAndValidate(loaded);
                    return loaded;
                }
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    private static void ClampAndValidate(AppSettings s)
    {
        s.ImageOpacity = Math.Clamp(s.ImageOpacity, 0.0, 1.0);
        s.OverlayDurationMs = Math.Clamp(s.OverlayDurationMs, 200, 10000);
        s.OverlayScale = Math.Clamp(s.OverlayScale, 0.25, 3.0);
        s.CustomPositionX = Math.Clamp(s.CustomPositionX, 0.0, 1.0);
        s.CustomPositionY = Math.Clamp(s.CustomPositionY, 0.0, 1.0);
        s.SafeMarginPixels = Math.Clamp(s.SafeMarginPixels, 0.0, 100.0);
        s.EntryDurationMs = Math.Clamp(s.EntryDurationMs, 0, 5000);
        s.ExitDurationMs = Math.Clamp(s.ExitDurationMs, 0, 5000);
        s.TextFontSize = Math.Clamp(s.TextFontSize, 8.0, 200.0);
        s.TextOpacity = Math.Clamp(s.TextOpacity, 0.40, 1.0);
        s.SoundVolume = Math.Clamp(s.SoundVolume, 0.0, 1.0);
        s.CustomScalePercent = Math.Clamp(s.CustomScalePercent, 25.0, 300.0);
        s.RepeatCooldown = Math.Clamp(s.RepeatCooldown, 0, 50);
        s.CustomFitScale = Math.Clamp(s.CustomFitScale, 0.25, 3.0);
        s.CustomAudioMaxSeconds = Math.Clamp(s.CustomAudioMaxSeconds, 1, 120);

        if (!Enum.IsDefined(s.ImagePosition)) s.ImagePosition = ImagePosition.Center;
        if (!Enum.IsDefined(s.AnimationStyle)) s.AnimationStyle = AnimationStyle.Pop;
        if (!Enum.IsDefined(s.ImageMode)) s.ImageMode = ImageMode.Random;
        if (!Enum.IsDefined(s.MonitorMode)) s.MonitorMode = MonitorMode.Primary;
        if (!Enum.IsDefined(s.TextPosition)) s.TextPosition = TextPosition.BelowImage;
        if (!Enum.IsDefined(s.FitMode)) s.FitMode = FitMode.FillScreen;
        if (!Enum.IsDefined(s.EasingStyle)) s.EasingStyle = EasingStyle.EaseOut;
        if (!Enum.IsDefined(s.EntrySpeed)) s.EntrySpeed = EntrySpeed.Normal;
        if (!Enum.IsDefined(s.ExitStyle)) s.ExitStyle = ExitStyle.Fade;
        if (!Enum.IsDefined(s.TextShadow)) s.TextShadow = TextShadowStyle.SoftShadow;
        if (!Enum.IsDefined(s.TextAnimation)) s.TextAnimation = TextAnimationStyle.FollowImage;
        if (!Enum.IsDefined(s.GridDensity)) s.GridDensity = GridDensity.Normal;
        if (!Enum.IsDefined(s.LibrarySort)) s.LibrarySort = LibrarySortMode.Name;
        if (!Enum.IsDefined(s.LibraryDefaultFilter)) s.LibraryDefaultFilter = LibraryFilterMode.All;
        if (!Enum.IsDefined(s.CloseBehavior)) s.CloseBehavior = CloseBehavior.MinimizeToTray;
        if (!Enum.IsDefined(s.LaunchBehavior)) s.LaunchBehavior = LaunchBehavior.Normal;
        if (!Enum.IsDefined(s.SyncFrequency)) s.SyncFrequency = SyncFrequency.OnStartup;
        if (!Enum.IsDefined(s.SafeMarginPreset)) s.SafeMarginPreset = SafeMarginPreset.Small;
        if (!Enum.IsDefined(s.SizePreset)) s.SizePreset = SizePreset.Medium;
        if (!Enum.IsDefined(s.DurationPreset)) s.DurationPreset = DurationPreset.S1_8;
        if (!Enum.IsDefined(s.TextWeight)) s.TextWeight = TextWeight.Bold;
        if (!Enum.IsDefined(s.TextHorizontalAlignment)) s.TextHorizontalAlignment = TextAlignment.Center;
        if (!Enum.IsDefined(s.CacheSizeLimit)) s.CacheSizeLimit = CacheSizeLimit.Off;
        if (!Enum.IsDefined(s.EvictionStrategy)) s.EvictionStrategy = EvictionStrategy.LeastRecentlyUsed;
        if (!Enum.IsDefined(s.SoundMode)) s.SoundMode = SoundMode.RandomSound;
        if (!Enum.IsDefined(s.AudioStopPolicy)) s.AudioStopPolicy = AudioStopPolicy.StopPrevious;
        if (!Enum.IsDefined(s.AudioMaxDuration)) s.AudioMaxDuration = AudioMaxDuration.Seconds30;
        if (!Enum.IsDefined(s.Preset)) s.Preset = CelebrationPreset.CompletelyRandom;
        if (!Enum.IsDefined(s.SizePreset)) s.SizePreset = SizePreset.Medium;
    }

    public void Save()
    {
        string json;
        lock (_lock)
        {
            json = JsonSerializer.Serialize(Current, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        // Fire-and-forget: serialize under lock (fast), write off the UI thread.
        // Chained so rapid successive saves complete in order — never tears or stales.
        lock (_writeGate)
        {
            _lastWrite = WriteOrdered(json, _settingsPath, _lastWrite);
        }
    }

    private static async Task WriteOrdered(string json, string path, Task? previous)
    {
        if (previous is not null) try { await previous.ConfigureAwait(false); } catch { /* swallow — prior write already logged */ }
        await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
    }

    public void Reset()
    {
        Current = new AppSettings();
        Save();
    }
}

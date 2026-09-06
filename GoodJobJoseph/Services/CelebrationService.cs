using System.IO;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Models.CounterStrike;
using JosephExperience.Services.CounterStrike;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

public class CelebrationService
{
    private readonly ImageLibraryService _library;
    private readonly OverlayService _overlay;
    private readonly AudioService _audio;
    private readonly SettingsService _settings;
    private readonly HistoryService _history;
    private readonly SoundLibraryService _soundLibrary;
    private readonly DatabaseService _db;
    private readonly TrayService? _tray;
    private string? _lastShownImageId;
    private Views.Overlay3DWindow? _overlay3d;
    private readonly object _overlay3dLock = new();
    private readonly object _celebrationGuard = new();
    private bool _isCelebrating;

    public event Action<string?>? CelebrationStarted;

    /// <summary>Fired when the F8 audio-toggle hotkey flips the play-sound state.</summary>
    public event Action<bool>? SoundToggleChanged;

    public CelebrationService(ImageLibraryService library, OverlayService overlay,
        AudioService audio, SettingsService settings, HistoryService history,
        SoundLibraryService soundLibrary, DatabaseService db, TrayService? tray = null)
    {
        _library = library;
        _overlay = overlay;
        _audio = audio;
        _settings = settings;
        _history = history;
        _soundLibrary = soundLibrary;
        _db = db;
        _tray = tray;
    }

    public bool Trigger(string triggerType, bool? lowDistractionOverride = null)
    {
        lock (_celebrationGuard)
        {
            if (_isCelebrating) return false;
            _isCelebrating = true;
        }

        try
        {
            return TriggerCore(triggerType, lowDistractionOverride);
        }
        finally
        {
            _isCelebrating = false;
        }
    }

    private bool TriggerCore(string triggerType, bool? lowDistractionOverride)
    {
        var settings = _settings.Current;
        if (!settings.Enabled) return false;

        var image = _library.PickImage(
            settings.ImageMode,
            settings.UseFavoritesOnly,
            settings.AvoidImmediateRepeats,
            settings.SpecificImageId,
            _lastShownImageId,
            settings.RepeatCooldown,
            settings.SelectedCategory);
        if (image is null)
        {
            image = _library.PickImage(ImageMode.Random, false, false, null, null);
        }
        if (image is null)
        {
            AppLog.Warn("Celebration triggered but no eligible Joseph images found.");
            CelebrationStarted?.Invoke(null);
            return false;
        }

        var source = ImageLibraryService.LoadImageSource(image.FilePath) as System.Windows.Media.Imaging.BitmapSource;
        if (source is null)
        {
            _library.MarkBroken(image.Id);
            image = _library.PickImage(ImageMode.Random, false, false, null, null);
            source = image is not null
                ? ImageLibraryService.LoadImageSource(image.FilePath) as System.Windows.Media.Imaging.BitmapSource
                : null;
        }
        if (source is null)
        {
            AppLog.Warn("Celebration triggered but failed to decode any image source.");
            CelebrationStarted?.Invoke(null);
            return false;
        }

        _lastShownImageId = image.Id;

        var resolved = CelebrationResolver.Resolve(settings, image.CelebrationText);
        var overlaySettings = settings.Clone();

        // Low-distraction: caller override > game-event auto > master setting.
        var isGame = !string.IsNullOrEmpty(triggerType)
                     && (triggerType.StartsWith("csgo:", StringComparison.OrdinalIgnoreCase)
                         || triggerType.StartsWith("cs2:", StringComparison.OrdinalIgnoreCase));
        bool? useLow = lowDistractionOverride;
        if (!useLow.HasValue)
            useLow = isGame ? (bool?)settings.GameEventsLowDistraction : null;
        if (!useLow.HasValue)
            useLow = settings.LowDistraction;

        if (useLow.GetValueOrDefault())
        {
            var dur = Math.Max(400, resolved.DurationMs);
            overlaySettings.OverlayDurationMs = Math.Min(dur, 700);
            overlaySettings.ImageOpacity = Math.Min(overlaySettings.ImageOpacity, 0.45);
        }
        else
        {
            overlaySettings.OverlayDurationMs = resolved.DurationMs;
        }

        // Apply resolved animation/text settings.
        overlaySettings.AnimationStyle = resolved.ImageAnimation;
        overlaySettings.OverlayScale = resolved.ImageScale;
        overlaySettings.TextPosition = resolved.TextPosition;
        overlaySettings.TextAnimation = resolved.TextAnimation;

        // One concise deploy log.
        AppLog.Info(
            $"Joseph deployed: name=\"{image.DisplayName}\" " +
            $"quote=\"{resolved.ResolvedQuote}\" " +
            $"imageFx={resolved.ImageAnimation} " +
            $"textFx={resolved.TextFx} " +
            $"intensity={resolved.Intensity} " +
            $"position={resolved.TextPosition} " +
            $"scale={resolved.ImageScale:0.00} " +
            $"duration={overlaySettings.OverlayDurationMs}ms");

        // Route 3D models.
        if (image.Is3DModel && overlaySettings.Prefer3DModel)
        {
            ShowModel3D(image.FilePath, overlaySettings, image.Id, triggerType);
            _audio.PlaySound(settings, image);
            _history?.AddEntry(image.Id, image.DisplayName, triggerType ?? "manual", true, overlaySettings.OverlayDurationMs);
            CelebrationStarted?.Invoke(image.DisplayName);
            return true;
        }

        _overlay.ShowOverlay(source, overlaySettings, resolved.ResolvedQuote, resolved, () =>
        {
            _library.RecordShown(image.Id, triggerType, overlaySettings.OverlayDurationMs);
            _history?.AddEntry(image.Id, image.DisplayName, triggerType ?? "manual", false, overlaySettings.OverlayDurationMs);
        }, image.Id);

        _audio.PlaySound(settings, image);
        CelebrationStarted?.Invoke(image.DisplayName);
        return true;
    }

    private void ShowModel3D(string filePath, AppSettings settings, string imageId, string triggerType)
    {
        lock (_overlay3dLock)
        {
            if (_overlay3d == null)
            {
                _overlay3d = new Views.Overlay3DWindow();
            }
        }
        _overlay3d.ShowModel(filePath, settings, () =>
        {
            _library.RecordShown(imageId, triggerType, settings.OverlayDurationMs);
        });
    }

    public string? LastShownImageId => _lastShownImageId;

    /// <summary>
    /// Cycles to the next sound clip in the library (for preview/testing).
    /// Stops any currently playing audio and starts the preview of the new clip.
    /// </summary>
    public void CycleSound()
    {
        var settings = _settings.Current;
        if (!settings.Enabled) return;
        if (_audio is null) return;

        var sounds = _soundLibrary.GetAllSounds().Where(s => s.Exists).ToList();
        if (sounds.Count == 0)
        {
            AppLog.Info("CycleSound: no valid sound clips available.");
            return;
        }

        _currentSoundIndex = (_currentSoundIndex + 1) % sounds.Count;
        var next = sounds[_currentSoundIndex];

        if (!settings.PreviewSoundWhenCycling) return;

        _audio.StopCurrent();
        _audio.Preview(settings, next.FilePath);

        var msg = $"Sound cycle: {next.DisplayName}";
        AppLog.Info(msg);
        _tray?.UpdateSoundStatus(msg);
    }

    private int _currentSoundIndex = -1;

    /// <summary>
    /// Toggles celebration sound on/off via the F8 hotkey.
    /// Persists the change, previews the selected clip when enabling (audible
    /// confirmation), stops any currently playing audio when muting, and notifies
    /// the UI so the toggle is never silent.
    /// </summary>
    public void ToggleAudio()
    {
        var settings = _settings.Current;
        settings.PlaySound = !settings.PlaySound;
        _settings.Save();

        if (!settings.PlaySound)
        {
            _audio?.StopCurrent();
        }
        else
        {
            _audio?.PreviewSelected(settings);
        }

        var msg = settings.PlaySound
            ? "Celebration sound: ON"
            : "Celebration sound: OFF";
        AppLog.Info(msg);
        _tray?.UpdateSoundStatus(msg);
        SoundToggleChanged?.Invoke(settings.PlaySound);
    }

    /// <summary>
    /// Stops any currently playing celebration audio.
    /// </summary>
    public void StopAudio()
    {
        _audio?.StopCurrent();
    }

    /// <summary>
    /// Awards Joseph Coins based on the celebration preset used.
    // Rarer presets award more coins. Coins persist in app_metadata.
    /// These are INTERNAL reward points, NOT the real NADD/SOL token.
    /// </summary>
    // Coins feature removed - no longer awarded or displayed.
    // ------------------------------------------------------------ game-event integration

    /// <summary>True while an overlay is currently on screen.</summary>
    public bool IsBusy => _isCelebrating || _overlay.IsVisible;

    private int _activePriority;

    /// <summary>
    /// Priority arbitration: a higher-priority game event may replace the current celebration.
    /// Returns true if the caller may proceed.
    /// </summary>
    public bool TryPreempt(int priority)
    {
        if (!_isCelebrating && !_overlay.IsVisible) return true;
        if (priority > _activePriority)
        {
            AppLog.Info($"Celebration preempted: priority {_activePriority} -> {priority}");
            _overlay.HideOverlay();
            _isCelebrating = false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Entry point for Counter-Strike game events (and other routed triggers).
    /// Maps the per-event config onto the normal celebration pipeline.
    /// </summary>
    public bool TriggerGameEvent(string triggerType, GameEventCelebrationConfig config,
        CelebrationGameEvent gameEvent)
    {
        if (_isCelebrating) return false;
        _isCelebrating = true;
        try
        {
            var settings = _settings.Current;
            if (!settings.Enabled) return false;
            if (config.Source == GameEventCelebrationSource.NoCelebration) return false;

            _activePriority = config.Priority;

            // Image selection per event config.
            var image = config.Source switch
            {
                GameEventCelebrationSource.SpecificJoseph when !string.IsNullOrEmpty(config.SpecificImageId)
                    => _library.GetImage(config.SpecificImageId),
                GameEventCelebrationSource.SpecificCategory when !string.IsNullOrEmpty(config.SpecificCategory)
                    => _library.PickImage(ImageMode.Random, settings.UseFavoritesOnly,
                        settings.AvoidImmediateRepeats, null, _lastShownImageId,
                        settings.RepeatCooldown, config.SpecificCategory),
                _ => _library.PickImage(
                        settings.ImageMode, settings.UseFavoritesOnly, settings.AvoidImmediateRepeats,
                        settings.SpecificImageId, _lastShownImageId, settings.RepeatCooldown,
                        settings.SelectedCategory)
            };
            image ??= _library.PickImage(ImageMode.Random, false, false, null, null);
            if (image is null)
            {
                AppLog.Warn("Game event celebration: no eligible images.");
                return false;
            }

            var source = ImageLibraryService.LoadImageSource(image.FilePath) as System.Windows.Media.Imaging.BitmapSource;
            if (source is null)
            {
                _library.MarkBroken(image.Id);
                return false;
            }
            _lastShownImageId = image.Id;

            // Text per event config.
            var resolved = CelebrationResolver.Resolve(settings, image.CelebrationText);
            var quote = config.TextSource switch
            {
                GameEventTextSource.GameEventQuote => GameEventText.DefaultFor(gameEvent.Type),
                GameEventTextSource.Custom when !string.IsNullOrWhiteSpace(config.CustomText)
                    => config.CustomText!,
                GameEventTextSource.None => string.Empty,
                _ => resolved.ResolvedQuote
            };
            if (quote != resolved.ResolvedQuote)
            {
                resolved = resolved with { ResolvedQuote = quote };
            }

            var overlaySettings = settings.Clone();
            // Low-distraction for game events (existing behavior).
            var useLow = settings.GameEventsLowDistraction || settings.LowDistraction;
            if (useLow)
            {
                var dur = Math.Max(400, resolved.DurationMs);
                overlaySettings.OverlayDurationMs = Math.Min(dur, 700);
                overlaySettings.ImageOpacity = Math.Min(overlaySettings.ImageOpacity, 0.45);
            }
            else
            {
                overlaySettings.OverlayDurationMs = resolved.DurationMs;
            }

            overlaySettings.AnimationStyle = resolved.ImageAnimation;
            overlaySettings.OverlayScale = resolved.ImageScale;
            overlaySettings.TextPosition = resolved.TextPosition;
            overlaySettings.TextAnimation = resolved.TextAnimation;

            AppLog.Info(
                $"Celebration: source={triggerType} image=\"{image.DisplayName}\" " +
                $"preset=\"{config.SpecificPreset ?? "default"}\" text=\"{quote}\"");

            if (image.Is3DModel && overlaySettings.Prefer3DModel)
            {
                ShowModel3D(image.FilePath, overlaySettings, image.Id, triggerType);
                _audio.PlaySound(settings, image);
                _history?.AddEntry(image.Id, image.DisplayName, triggerType, true, overlaySettings.OverlayDurationMs);
                CelebrationStarted?.Invoke(image.DisplayName);
                return true;
            }

            _overlay.ShowOverlay(source, overlaySettings, resolved.ResolvedQuote, resolved, () =>
            {
                _library.RecordShown(image.Id, triggerType, overlaySettings.OverlayDurationMs);
                _history?.AddEntry(image.Id, image.DisplayName, triggerType, false, overlaySettings.OverlayDurationMs);
            }, image.Id);
            _audio.PlaySound(settings, image);
            CelebrationStarted?.Invoke(image.DisplayName);
            return true;
        }
        finally
        {
            _isCelebrating = false;
        }
    }
}
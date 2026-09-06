using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using JosephExperience.Models;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

/// <summary>
/// Plays imported sound clips through the managed sound library. Playback is
/// strictly best-effort: missing, deleted, corrupt, or unsupported files never
/// throw and a celebration always continues even if audio is silently skipped.
/// Playback is asynchronous/non-blocking and the previous clip is stopped before
/// each new one so the queue behaviour matches the celebration pipeline.
/// </summary>
public class AudioService : IDisposable
{
    private readonly SoundLibraryService _library;
    private SoundPlayer? _soundPlayer;
    private MediaPlayer? _mediaPlayer;
    private readonly Dispatcher? _dispatcher;
    private readonly object _lock = new();
    private bool _disposed;

    private CancellationTokenSource? _maxDurationCts;
    private DateTime? _currentPlaybackStart;
    private readonly Dictionary<object, Action> _pendingPreviews = new();

    public AudioService(SoundLibraryService library)
    {
        _library = library;
        try
        {
            _dispatcher = Application.Current?.Dispatcher;
        }
        catch
        {
            _dispatcher = null;
        }

        try
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaFailed += (_, _) => { /* best effort */ };
            _mediaPlayer.MediaEnded += (_, _) => OnMediaEnded();
        }
        catch
        {
            _mediaPlayer = null;
        }
    }

    /// <summary>
    /// Plays a clip for a celebration. Returns true when a clip actually started
    /// playing. Never throws; any problem results in a silent, graceful skip.
    /// </summary>
    public bool PlaySound(AppSettings settings, CelebrationImage? image = null)
    {
        if (!settings.PlaySound) return false;
        if (settings.SoundMode == SoundMode.NoSound) return false;

        var clip = ResolveClip(settings, image);
        if (clip is null || !SoundClip.ExistsSafe(clip.FilePath)) return false;
        if (settings.SoundVolume <= 0.0) return false;

        // Apply stop policy: stop previous unless overlapping is allowed.
        if (settings.AudioStopPolicy == AudioStopPolicy.StopPrevious)
        {
            StopCurrent();
        }

        var maxSeconds = ResolveMaxDurationSeconds(settings);
        if (!PlayPath(clip.FilePath, settings.SoundVolume, maxSeconds)) return false;

        _library.RecordPlayed(clip);
        AppLog.Info($"Audio: playing \"{clip.DisplayName}\" (maxDuration={maxSeconds}s)");
        return true;
    }

    /// <summary>Stops the current celebration audio immediately.</summary>
    public void StopCurrent()
    {
        try
        {
            _maxDurationCts?.Cancel();
            _maxDurationCts = null;

            lock (_lock)
            {
                _soundPlayer?.Stop();
                _soundPlayer?.Dispose();
                _soundPlayer = null;
            }

            RunOnDispatcher(() =>
            {
                if (_mediaPlayer is not null)
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Close();
                }
            });
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>
    /// Picks which clip to use based on the configured sound mode. Missing/corrupt
    /// assigned clips fall back to the global selection and then to a random clip so
    /// audio never depends on a single fragile file.
    /// </summary>
    private SoundClip? ResolveClip(AppSettings settings, CelebrationImage? image)
    {
        if (settings.SoundMode == SoundMode.AssignedSound)
        {
            var assignedId = image?.SoundId ?? settings.SelectedSoundId;
            var assigned = !string.IsNullOrEmpty(assignedId) ? _library.GetSound(assignedId!) : null;
            if (assigned is not null && assigned.Exists) return assigned;
            if (!string.IsNullOrEmpty(settings.SelectedSoundId))
            {
                var global = _library.GetSound(settings.SelectedSoundId!);
                if (global is not null && global.Exists) return global;
            }
            if (!string.IsNullOrEmpty(settings.SelectedSound) && File.Exists(settings.SelectedSound))
            {
                return new SoundClip { FilePath = settings.SelectedSound };
            }
        }

        return _library.GetRandomSound();
    }

    private static int ResolveMaxDurationSeconds(AppSettings settings)
    {
        return settings.AudioMaxDuration switch
        {
            AudioMaxDuration.Seconds5 => 5,
            AudioMaxDuration.Seconds10 => 10,
            AudioMaxDuration.Seconds15 => 15,
            AudioMaxDuration.Seconds20 => 20,
            AudioMaxDuration.Seconds30 => 30,
            AudioMaxDuration.Custom => Math.Max(1, settings.CustomAudioMaxSeconds),
            _ => 30 // Unlimited not supported for celebratioos — hard 30s cap
        };
    }

    private bool PlayPath(string path, double volume, int maxDurationSeconds)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext == ".wav")
            {
                lock (_lock)
                {
                    StopCurrent();
                    _soundPlayer = new SoundPlayer(path);
                }
                _currentPlaybackStart = DateTime.UtcNow;

                if (_soundPlayer?.IsLoadCompleted == false)
                {
                    _soundPlayer?.LoadAsync();
                }

                var playOk = false;
                RunOnDispatcher(() =>
                {
                    try
                    {
                        _soundPlayer?.Play();
                        playOk = true;
                    }
                    catch { playOk = false; }
                });

                if (playOk && maxDurationSeconds > 0)
                {
                    StartMaxDurationTimer(maxDurationSeconds);
                }

                return playOk;
            }

            if (ext == ".mp3" || ext == ".wma")
            {
                if (_mediaPlayer is null) return false;

                var playCts = new CancellationTokenSource();
                lock (_lock) { _maxDurationCts = playCts; }

                RunOnDispatcher(() =>
                {
                    try
                    {
                        _mediaPlayer.Stop();
                        _mediaPlayer.Volume = Math.Clamp(volume, 0.0, 1.0);
                        _mediaPlayer.Open(new Uri(path, UriKind.Absolute));
                        _mediaPlayer.Play();
                        _currentPlaybackStart = DateTime.UtcNow;
                    }
                    catch { }
                });

                if (maxDurationSeconds > 0)
                {
                    StartMaxDurationTimer(maxDurationSeconds);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Audio playback error: {ex.Message}");
            return false;
        }
        return false;
    }

    private void StartMaxDurationTimer(int seconds)
    {
        var cts = new CancellationTokenSource();
        lock (_lock) { _maxDurationCts = cts; }

        Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token)
            .ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                // Hard stop at max duration.
                StopCurrent();
            }, TaskScheduler.Default);
    }

    private void OnMediaEnded()
    {
        try
        {
            _maxDurationCts?.Cancel();
            _maxDurationCts = null;
            _currentPlaybackStart = null;
        }
        catch { }
    }

    /// <summary>
    /// Playback preview used by the UI. Shares the same underlying players so only
    /// one clip is audible at a time. Never throws.
    /// </summary>
    public void Preview(AppSettings settings, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            StopCurrent();

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".wav")
            {
                lock (_lock)
                {
                    _soundPlayer?.Stop();
                    _soundPlayer?.Dispose();
                    _soundPlayer = new SoundPlayer(path);
                }
                _soundPlayer.Play();
            }
            else
            {
                RunOnDispatcher(() =>
                {
                    if (_mediaPlayer is not null)
                    {
                        _mediaPlayer.Stop();
                        _mediaPlayer.Volume = Math.Clamp(settings.SoundVolume, 0.0, 1.0);
                        _mediaPlayer.Open(new Uri(path, UriKind.Absolute));
                        _mediaPlayer.Play();
                    }
                });
            }
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>
    /// Audible confirmation when the user toggles sound ON via the F8 hotkey.
    /// Resolves the currently selected clip and previews it. Never throws.
    /// </summary>
    public void PreviewSelected(AppSettings settings)
    {
        try
        {
            var clip = ResolveClip(settings, null);
            if (clip is not null && SoundClip.ExistsSafe(clip.FilePath))
            {
                Preview(settings, clip.FilePath);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    public void Stop()
    {
        try
        {
            _maxDurationCts?.Cancel();
            _maxDurationCts = null;

            lock (_lock)
            {
                _soundPlayer?.Stop();
                _soundPlayer?.Dispose();
                _soundPlayer = null;
            }
            RunOnDispatcher(() => _mediaPlayer?.Stop());
        }
        catch
        {
            // best effort
        }
    }

    private void RunOnDispatcher(Action action)
    {
        if (_dispatcher is not null && !_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Stop();
            lock (_lock) { _soundPlayer?.Dispose(); }
            RunOnDispatcher(() => _mediaPlayer?.Close());
        }
        catch
        {
            // best effort
        }
    }
}
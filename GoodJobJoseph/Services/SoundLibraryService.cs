using System.IO;
using System.Security.Cryptography;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

/// <summary>
/// Manages the app's local sound library. Imported audio files are copied into
/// the managed <see cref="AppPaths.SoundsDir"/> directory (safe UUID filenames with
/// the original extension preserved) and their metadata is persisted in the
/// database, so the app never depends on the original external file path and never
/// packages user audio into the executable.
/// </summary>
public class SoundLibraryService
{
    private readonly DatabaseService _db;
    private readonly string _soundsDir;
    private readonly Random _random = new();

    /// <summary>Formats accepted for import and playback. OGG is intentionally excluded because
    /// the WPF System.Media stack does not decode it cleanly.</summary>
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".wma"
    };

    public SoundLibraryService(DatabaseService db, string? soundsDir = null)
    {
        _db = db;
        _soundsDir = soundsDir ?? AppPaths.SoundsDir;
        try { Directory.CreateDirectory(_soundsDir); } catch { }
    }

    public List<SoundClip> GetAllSounds() => _db.GetAllSoundClips();

    public SoundClip? GetSound(string id) => string.IsNullOrEmpty(id) ? null : _db.GetSoundClip(id);

    public int Count => _db.GetAllSoundClips().Count;

    /// <summary>
    /// Picks a random clip from the library. Any clip whose backing file is missing
    /// is skipped so a deleted/corrupt file can never break a celebration.
    /// </summary>
    public SoundClip? GetRandomSound()
    {
        var clips = _db.GetAllSoundClips();
        var valid = clips.Where(c => c.Exists).ToList();
        if (valid.Count == 0) return null;
        return valid[_random.Next(valid.Count)];
    }

    /// <summary>
    /// Copies an external audio file into the managed sounds directory and records
    /// its metadata. Re-importing the same file (same content hash) is a harmless no-op.
    /// </summary>
    public ImportSoundResult ImportSound(string sourcePath)
    {
        var result = new ImportSoundResult();
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                result.Error = "The selected audio file does not exist.";
                return result;
            }

            var ext = Path.GetExtension(sourcePath);
            if (!SupportedExtensions.Contains(ext))
            {
                result.Error = "Unsupported format. Please choose a WAV or MP3 file (OGG is not supported).";
                return result;
            }

            var content = File.ReadAllBytes(sourcePath);
            if (content.Length == 0)
            {
                result.Error = "The selected audio file is empty.";
                return result;
            }

            var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var existing = _db.GetSoundClipBySha256(sha);
            if (existing is not null)
            {
                result.Skipped = 1;
                result.SkippedDuplicate = true;
                result.Sound = existing;
                return result;
            }

            // Use a safe internal UUID filename, preserving the extension.
            var safeName = $"{Guid.NewGuid():N}{ext}";
            var dest = Path.Combine(_soundsDir, safeName);
            File.WriteAllBytes(dest, content);

            var clip = new SoundClip
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = SoundLorePools.RandomSoundName(Path.GetFileNameWithoutExtension(sourcePath)),
                FileName = safeName,
                FilePath = dest,
                Extension = ext,
                FileSize = content.Length,
                Sha256 = sha
            };
            _db.InsertSoundClip(clip);

            result.Imported = 1;
            result.Sound = clip;
            AppLog.Info($"Imported sound clip \"{clip.DisplayName}\" -> {dest}");
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Sound import failed: {ex.Message}");
            result.Failed = 1;
            result.Error = "The audio file could not be imported.";
            return result;
        }
    }

    /// <summary>
    /// Removes a clip from the database (also clearing any per-image assignments)
    /// and deletes its managed file. Missing backing files are handled gracefully.
    /// </summary>
    public bool DeleteSound(SoundClip clip)
    {
        if (clip is null) return false;
        try
        {
            _db.DeleteSoundClip(clip.Id);
            if (SoundClip.ExistsSafe(clip.FilePath))
            {
                try { File.Delete(clip.FilePath); } catch { }
            }
            AppLog.Info($"Removed sound clip \"{clip.DisplayName}\".");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Sound delete failed: {ex.Message}");
            return false;
        }
    }

    public void RecordPlayed(SoundClip clip)
    {
        if (clip is null) return;
        try { _db.IncrementSoundTimesPlayed(clip.Id); } catch { }
    }
}

public class ImportSoundResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public bool SkippedDuplicate { get; set; }
    public string? Error { get; set; }
    public SoundClip? Sound { get; set; }
}

/// <summary>
/// Assigns a random lore name to imported sounds that have generic filenames
/// (e.g. "audio.mp3", "recording.wav"). If the filename is already meaningful,
/// it's kept as-is.
/// </summary>
public static class SoundLorePools
{
    private static readonly string[] SoundNames = new[]
    {
        "Joseph's Holy Giggle", "The Sacred Chuckle", "Blessed Backseat Laugh",
        "Divine Snort", "The Righteous Cackle", "Saint Joseph's Belly Laugh",
        "Heavenly Chortle", "The Memorable Guffaw", "Joseph's Joyful Noise",
        "The Eternal Snicker", "Glorious Wheeze", "The Unbroken Cackle",
        "Celestial Titter", "The Triumphant Bark", "Resplendent Giggle",
        "The Peerless Snort", "Holy Hilarity", "The Illustrious Chuckle",
        "Venerable Chortle", "The Magnificent Guffaw"
    };

    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio", "sound", "recording", "clip", "file", "untitled", "new audio",
        "new sound", "new recording", "import", "imported", "rec", "voice",
        "microphone", "mic", "beep", "tone", "music", "song", "track"
    };

    public static string RandomSoundName(string originalName)
    {
        // If the name looks generic/random, replace it with a lore name.
        if (string.IsNullOrWhiteSpace(originalName) || GenericNames.Contains(originalName.Trim()))
        {
            return SoundNames[Random.Shared.Next(SoundNames.Length)];
        }
        return originalName;
    }
}
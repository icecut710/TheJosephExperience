namespace JosephExperience.Models;

/// <summary>
/// A user-imported sound clip that lives in the app's managed local sounds
/// directory. Audio files are copied into the managed storage at import time so
/// the app never depends on an external file path that could move or disappear.
/// </summary>
public class SoundClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Original extension preserved at import (.wav / .mp3 / .wma).</summary>
    public string Extension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string? Sha256 { get; set; }

    /// <summary>How many times this clip has been played during celebrations.</summary>
    public int TimesPlayed { get; set; }

    public bool Exists => SoundClip.ExistsSafe(FilePath);

    public static bool ExistsSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return System.IO.File.Exists(path); }
        catch { return false; }
    }
}
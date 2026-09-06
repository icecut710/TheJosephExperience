namespace JosephExperience.Models;

public class CelebrationImage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Favorite { get; set; }
    public int Weight { get; set; } = 1;
    public int TimesShown { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string? LastShownAt { get; set; }
    public string? Sha256 { get; set; }

    // Remote sync (Supabase) mapping — null for locally-only images.
    public string? RemoteId { get; set; }
    public string? StoragePath { get; set; }
    public string? RemoteUpdatedAt { get; set; }
    public string? LastSyncedAt { get; set; }

    // New metadata
    public string? MimeType { get; set; }
    public long? FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    // Local cache health
    public string? DeletedAt { get; set; }
    public string? LocalCachePath { get; set; }
    public AssetHealth Health { get; set; } = AssetHealth.Ready;
    public bool IsLocalOnly { get; set; }

    /// <summary>
    /// Per-Joseph celebration quote. Assigned at import time from the lore pool if blank,
    /// then persisted so each Joseph keeps its line until the user changes it.
    /// </summary>
    public string? CelebrationText { get; set; }

    /// <summary>
    /// ID of the <see cref="SoundClip"/> assigned to this Joseph when the sound
    /// mode is <see cref="SoundMode.AssignedSound"/>. Null = no assigned clip.
    /// Permanently removed clips have this cleared back to null by the delete flow.
    /// </summary>
    public string? SoundId { get; set; }

    public string SourceLabel => IsLocalOnly ? "Local" : (string.IsNullOrEmpty(RemoteId) ? "Local" : "Cloud");

    /// <summary>Formats treated as 3D models that can spin in a corner.</summary>
    private static readonly HashSet<string> _3dExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".obj", ".stl"
    };

    /// <summary>True if this image is a 3D model file (.obj / .stl).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Is3DModel
    {
        get
        {
            var ext = System.IO.Path.GetExtension(FilePath);
            return !string.IsNullOrEmpty(ext) && _3dExtensions.Contains(ext);
        }
    }

    public bool IsReady => Health == AssetHealth.Ready && Enabled && string.IsNullOrEmpty(DeletedAt) && ExistsSafe(FilePath);

    public static bool ExistsSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return System.IO.File.Exists(path); }
        catch { return false; }
    }
}

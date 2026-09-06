using System.Text.RegularExpressions;

namespace JosephExperience.Utilities;

/// <summary>
/// Central, canonical generator for Supabase Storage object keys.
/// 
/// NEVER embeds user filenames, display names, quotes, timestamps, or any
/// user-controlled text into the storage path. The path is always:
///   {mediaType}/{uuid}.{canonicalExt}
///
/// The original filename is preserved ONLY as metadata on the database row,
/// never in the storage object key.
/// </summary>
public static class StorageObjectKey
{
    public enum MediaType
    {
        Images,
        Audio,
        Models,
        Branding
    }

    private static readonly Dictionary<MediaType, string> Prefixes = new()
    {
        [MediaType.Images] = "images",
        [MediaType.Audio] = "audio",
        [MediaType.Models] = "models",
        [MediaType.Branding] = "branding",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".ogg", ".wma"
    };

    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".obj", ".fbx"
    };

    private static readonly HashSet<string> BrandingExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ico", ".png", ".svg"
    };

    private static readonly Dictionary<MediaType, HashSet<string>> AllowedExtensions = new()
    {
        [MediaType.Images] = ImageExtensions,
        [MediaType.Audio] = AudioExtensions,
        [MediaType.Models] = ModelExtensions,
        [MediaType.Branding] = BrandingExtensions,
    };

    /// <summary>
    /// Canonicalize an extension for a given media type.
    /// Returns the lowercase canonical extension (e.g. ".jpg" for ".jpeg").
    /// Returns null if the extension is not supported for that media type.
    /// </summary>
    public static string? CanonicalizeExtension(MediaType mediaType, string? extension)
    {
        if (extension is null) return null;
        extension = extension.ToLowerInvariant();
        if (extension.Length == 1 && extension == ".") extension = "";
        if (!extension.StartsWith(".")) extension = "." + extension;

        if (mediaType == MediaType.Images)
        {
            if (extension is ".jpeg" or ".jfif") extension = ".jpg";
        }

        var allowed = AllowedExtensions[mediaType];
        return allowed.Contains(extension) ? extension : null;
    }

    /// <summary>
    /// Validates that a file extension is supported for the given media type.
    /// </summary>
    public static bool IsSupported(MediaType mediaType, string extension)
        => CanonicalizeExtension(mediaType, extension) is not null;

    /// <summary>
    /// Creates a canonical storage object key: {prefix}/{uuid}.{ext}
    /// The UUID is guaranteed unique. No user-controlled text appears in the path.
    /// </summary>
    public static string Create(MediaType mediaType, string? extension)
    {
        var prefix = Prefixes[mediaType];
        var ext = CanonicalizeExtension(mediaType, extension) ?? ".bin";
        var guid = Guid.NewGuid().ToString("D");
        return $"{prefix}/{guid}{ext}";
    }

    /// <summary>
    /// Creates a canonical storage object key and returns the UUID for metadata linking.
    /// </summary>
    public static (string ObjectKey, string Guid) CreateWithGuid(MediaType mediaType, string? extension)
    {
        var prefix = Prefixes[mediaType];
        var ext = CanonicalizeExtension(mediaType, extension) ?? ".bin";
        var guid = Guid.NewGuid().ToString("D");
        return ($"{prefix}/{guid}{ext}", guid);
    }

    /// <summary>
    /// Validates that a storage path matches the canonical pattern (no user filename leakage).
    /// Pattern: {prefix}/{uuid}.{ext}
    /// </summary>
    public static bool IsValidPath(string objectPath)
    {
        if (string.IsNullOrEmpty(objectPath)) return false;
        return Regex.IsMatch(objectPath,
            @"^(images|audio|models|branding)/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.\w+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Returns the MIME type for a given media type and extension.
    /// </summary>
    public static string GetMimeType(MediaType mediaType, string extension)
    {
        extension = extension.ToLowerInvariant();
        if (!extension.StartsWith(".")) extension = "." + extension;

        return mediaType switch
        {
            MediaType.Images => extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            },
            MediaType.Audio => extension switch
            {
                ".wav" => "audio/wav",
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                ".ogg" => "audio/ogg",
                ".wma" => "audio/x-ms-wma",
                _ => "application/octet-stream"
            },
            MediaType.Models => extension switch
            {
                ".glb" => "model/gltf-binary",
                ".gltf" => "model/gltf+json",
                ".obj" => "text/plain",
                ".fbx" => "application/octet-stream",
                _ => "application/octet-stream"
            },
            MediaType.Branding => extension switch
            {
                ".ico" => "image/x-icon",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            },
            _ => "application/octet-stream"
        };
    }
}

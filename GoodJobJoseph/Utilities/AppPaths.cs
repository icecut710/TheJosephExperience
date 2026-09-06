using System.IO;

namespace JosephExperience.Utilities;

public static class AppPaths
{
    private static readonly Lazy<string> _rootDir = new(() =>
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JosephExperience");
        Directory.CreateDirectory(baseDir);
        return baseDir;
    });

    public static string RootDir => _rootDir.Value;

    public static string ImagesDir
    {
        get
        {
            var dir = Path.Combine(RootDir, "images");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SoundsDir
    {
        get
        {
            var dir = Path.Combine(RootDir, "sounds");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string CacheDir
    {
        get
        {
            var dir = Path.Combine(RootDir, "cache");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string LogsDir
    {
        get
        {
            var dir = Path.Combine(RootDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(RootDir, "josephexperience.db");

    public static string SettingsPath => Path.Combine(RootDir, "settings.json");

    /// <summary>Location where remotely-synced Joseph images are cached.</summary>
    public static string CacheImagesDir
    {
        get
        {
            var dir = Path.Combine(CacheDir, "images");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Location for temporary downloads before they are validated and moved.</summary>
    public static string TempDir
    {
        get
        {
            var dir = Path.Combine(CacheDir, "tmp");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ThumbnailsDir
    {
        get
        {
            var dir = Path.Combine(CacheDir, "thumbnails");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

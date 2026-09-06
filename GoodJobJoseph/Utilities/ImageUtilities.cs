using System.IO;
using System.Security.Cryptography;

namespace JosephExperience.Utilities;

public static class ImageUtilities
{
    private static readonly string[] SupportedExtensions =
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tiff", ".tif"
    };

    private static readonly string[] CopyableExtensions =
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tiff", ".tif"
    };

    public static bool IsSupportedImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    public static bool CanCopyExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return CopyableExtensions.Contains(ext);
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}

using System.IO;
using JosephExperience.Utilities;

namespace JosephExperience.Tests;

public class StorageObjectKeyTests
{
    private const string ImagePattern = @"^images/^[0-9a-f-]{36}\.(png|jpg|jpeg|webp|gif)$";

    [Theory]
    [InlineData("Joseph.png", "images/", ".png")]
    [InlineData("Codex Image Sep 4, 2026, 07_33_16 PM.png", "images/", ".png")]
    [InlineData("GOOD JOB, JOSEPH!!!.png", "images/", ".png")]
    [InlineData("joseph (final) (2).jpg", "images/", ".jpg")]
    [InlineData("jöseph ünïcødé.png", "images/", ".png")]
    [InlineData("a very very very very very long joseph image filename.png", "images/", ".png")]
    [InlineData("image....png", "images/", ".png")]
    [InlineData("🔥JOSEPH🔥.png", "images/", ".png")]
    public void ImageKey_NeverContainsUserFilename(string userFilename, string expectedPrefix, string expectedExt)
    {
        var ext = Path.GetExtension(userFilename);
        var key = StorageObjectKey.Create(StorageObjectKey.MediaType.Images, ext);

        // Must start with the canonical prefix
        Assert.StartsWith(expectedPrefix, key);
        // Must end with the canonical extension
        Assert.EndsWith(expectedExt, key);
        // Must be exactly prefix + UUID + ext (no filename fragments)
        Assert.Equal(expectedPrefix + key[(expectedPrefix.Length)..], key);
        // The middle part (between prefix and ext) must be a 36-char UUID
        var uuidPart = key.Substring(expectedPrefix.Length, key.Length - expectedPrefix.Length - expectedExt.Length);
        Assert.Matches(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", uuidPart);
        // Original filename must NOT appear anywhere in the key
        var originalNameNoExt = Path.GetFileNameWithoutExtension(userFilename);
        Assert.DoesNotContain(originalNameNoExt, key, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../evil.png", "images/")]
    [InlineData("..\\evil.png", "images/")]
    [InlineData("subdir/../../evil.png", "images/")]
    public void ImageKey_PreventsPathTraversal(string maliciousFilename, string expectedPrefix)
    {
        var ext = Path.GetExtension(maliciousFilename);
        var key = StorageObjectKey.Create(StorageObjectKey.MediaType.Images, ext);

        Assert.StartsWith(expectedPrefix, key);
        Assert.DoesNotContain("..", key);
        Assert.False(key.Contains("evil"));
        Assert.True(StorageObjectKey.IsValidPath(key));
    }

    [Theory]
    [InlineData(".png", ".png")]
    [InlineData(".PNG", ".png")]
    [InlineData(".jpg", ".jpg")]
    [InlineData(".jpeg", ".jpg")]
    [InlineData(".jfif", ".jpg")]
    [InlineData(".webp", ".webp")]
    [InlineData(".gif", ".gif")]
    [InlineData(".bmp", null)]
    [InlineData(".exe", null)]
    [InlineData(".svg", null)]
    [InlineData("", null)]
    public void CanonicalizeExtension_HandlesAllFormats(string inputExt, string? expectedExt)
    {
        var result = StorageObjectKey.CanonicalizeExtension(StorageObjectKey.MediaType.Images, inputExt);
        Assert.Equal(expectedExt, result);
    }

    [Theory]
    [InlineData(".wav", "audio/wav")]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData(".m4a", "audio/mp4")]
    [InlineData(".ogg", "audio/ogg")]
    [InlineData(".wma", "audio/x-ms-wma")]
    public void GetMimeType_Audio(string ext, string expected)
    {
        Assert.Equal(expected, StorageObjectKey.GetMimeType(StorageObjectKey.MediaType.Audio, ext));
    }

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".gif", "image/gif")]
    public void GetMimeType_Image(string ext, string expected)
    {
        Assert.Equal(expected, StorageObjectKey.GetMimeType(StorageObjectKey.MediaType.Images, ext));
    }

    [Fact]
    public void AudioKey_IsCanonical()
    {
        var key = StorageObjectKey.Create(StorageObjectKey.MediaType.Audio, ".mp3");
        Assert.Matches(@"^audio/[0-9a-f-]{36}\.mp3$", key);
        Assert.True(StorageObjectKey.IsValidPath(key));
    }

    [Fact]
    public void UnsupportedExtension_ProducesBinFallbackKey()
    {
        var key = StorageObjectKey.Create(StorageObjectKey.MediaType.Images, ".xyz");
        // .xyz is not supported — falls back to .bin
        Assert.EndsWith(".bin", key);
        Assert.True(StorageObjectKey.IsValidPath(key));
    }

    [Fact]
    public void EveryImageKey_HasValidUUID()
    {
        for (int i = 0; i < 100; i++)
        {
            var key = StorageObjectKey.Create(StorageObjectKey.MediaType.Images, ".png");
            Assert.Matches(@"^images/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.png$", key);
            Assert.True(StorageObjectKey.IsValidPath(key));
        }
    }
}

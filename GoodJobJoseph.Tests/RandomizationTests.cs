using System.IO;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Services;
using JosephExperience.Utilities;

namespace JosephExperience.Tests;

public class RandomizationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;
    private readonly ImageLibraryService _library;
    private readonly string _imagesDir;
    private readonly string _cacheDir;

    public RandomizationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gjj_rand_{Guid.NewGuid():N}.db");
        _imagesDir = Path.Combine(Path.GetTempPath(), $"gjj_rand_img_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(), $"gjj_rand_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_imagesDir);
        Directory.CreateDirectory(_cacheDir);
        _db = new DatabaseService(_dbPath);
        _db.Initialize();
        _library = new ImageLibraryService(_db, _imagesDir, _cacheDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        try { Directory.Delete(_imagesDir, true); } catch { }
        try { Directory.Delete(_cacheDir, true); } catch { }
    }

    private CelebrationImage Add(string name, bool enabled = true, bool favorite = false, int weight = 1)
    {
        var img = new CelebrationImage
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = name,
            FileName = $"{name}.jpg",
            FilePath = Path.Combine(_imagesDir, $"{name}.jpg"),
            Enabled = enabled,
            Favorite = favorite,
            Weight = weight
        };
        File.WriteAllBytes(img.FilePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        _db.InsertImage(img);
        return img;
    }

    [Fact]
    public void DisabledImages_NeverAppear()
    {
        var enabled = Add("good");
        Add("bad", enabled: false);
        var picked = _library.PickImage(ImageMode.Random, false, true, null, null);
        Assert.NotNull(picked);
        Assert.Equal(enabled.Id, picked!.Id);
    }

    [Fact]
    public void FavoritesOnly_SelectsFavorites()
    {
        var fav = Add("fav", favorite: true);
        Add("plain", favorite: false);
        var picked = _library.PickImage(ImageMode.Random, true, true, null, null);
        Assert.NotNull(picked);
        Assert.Equal(fav.Id, picked!.Id);
    }

    [Fact]
    public void FavoritesOnly_WithNoFavorites_FallsBack()
    {
        var a = Add("a");
        var b = Add("b");
        var picked = _library.PickImage(ImageMode.Random, true, true, null, null);
        Assert.NotNull(picked);
        Assert.Contains(picked!.Id, new[] { a.Id, b.Id });
    }

    [Fact]
    public void SingleImageLibrary_Works()
    {
        var only = Add("only");
        var picked = _library.PickImage(ImageMode.Random, false, true, null, null);
        Assert.NotNull(picked);
        Assert.Equal(only.Id, picked!.Id);
    }

    [Fact]
    public void AvoidRepeats_PicksDifferentImage()
    {
        var a = Add("a");
        var b = Add("b");
        var id1 = a.Id;
        // Should avoid picking 'a' again (the only other eligible is 'b')
        var picked = _library.PickImage(ImageMode.Random, false, true, null, id1);
        Assert.NotNull(picked);
        Assert.Equal(b.Id, picked!.Id);
    }

    [Fact]
    public void AvoidRepeats_Off_MayRepeat()
    {
        var a = Add("a");
        var picked = _library.PickImage(ImageMode.Random, false, false, null, a.Id);
        Assert.NotNull(picked);
        Assert.Equal(a.Id, picked!.Id);
    }

    [Fact]
    public void WeightedMode_RespectsWeights_WithinRanges()
    {
        var light = Add("light", weight: 1);
        var heavy = Add("heavy", weight: 100);
        var counts = new Dictionary<string, int> { { light.Id, 0 }, { heavy.Id, 0 } };
        for (int i = 0; i < 200; i++)
        {
            var picked = _library.PickImage(ImageMode.Weighted, false, true, null, null);
            counts[picked!.Id]++;
        }
        Assert.True(counts[heavy.Id] > counts[light.Id],
            $"Expected heavy ({counts[heavy.Id]}) > light ({counts[light.Id]})");
    }

    [Fact]
    public void SpecificMode_ReturnsRequestedImage()
    {
        var a = Add("a");
        Add("b");
        var picked = _library.PickImage(ImageMode.Specific, false, true, Guid.Parse(a.Id), null);
        Assert.NotNull(picked);
        Assert.Equal(a.Id, picked!.Id);
    }

    [Fact]
    public void MissingFiles_SkippedByImport()
    {
        // Import a non-existent file path should fail gracefully
        var result = _library.ImportFile(Path.Combine(_imagesDir, "does_not_exist.jpg"));
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Imported);
    }

    [Fact]
    public void EmptyEligible_ReturnsNull()
    {
        Add("disabled", enabled: false);
        var picked = _library.PickImage(ImageMode.Random, true, true, null, null);
        Assert.Null(picked);
    }
}


using System.IO;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Services;
using JosephExperience.Utilities;

namespace JosephExperience.Tests;

public class ImportTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly DatabaseService _db;
    private readonly ImageLibraryService _library;

    public ImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"gjj_import_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        _db = new DatabaseService(_dbPath);
        _db.Initialize();
        var imagesDir = Path.Combine(_root, "images");
        var thumbsDir = Path.Combine(_root, "thumbs");
        _library = new ImageLibraryService(_db, imagesDir, thumbsDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private string CreatePng(string name)
    {
        var path = Path.Combine(_root, name);
        // Minimal valid PNG signature; import does not need to decode to copy.
        File.WriteAllBytes(path, new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        });
        return path;
    }

    [Fact]
    public void ImportFile_CopiesAndRecords()
    {
        var src = CreatePng("joseph.png");
        var result = _library.ImportFile(src, "My Joseph", "Custom", "funny, cool");
        Assert.Equal(1, result.Imported);
        Assert.NotNull(result.ImportedImage);
        var img = result.ImportedImage!;
        Assert.Equal("My Joseph", img.DisplayName);
        Assert.Equal("Custom", img.Category);
        Assert.Equal("funny, cool", img.Tags);
        // Copied into managed images dir, not the source location
        Assert.False(string.Equals(img.FilePath, src, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(img.FilePath));
        Assert.NotNull(img.Sha256);
        Assert.NotEqual(img.FileName, Path.GetFileName(src)); // collision-safe uuid name
    }

    [Fact]
    public void ImportDuplicate_Skipped()
    {
        var src = CreatePng("same.png");
        var r1 = _library.ImportFile(src, "A");
        Assert.Equal(1, r1.Imported);
        var r2 = _library.ImportFile(src, "B");
        Assert.Equal(0, r2.Imported);
        Assert.Equal(1, r2.Skipped);
        Assert.Equal(1, r2.SkippedDuplicates);
        // Only one image in library
        Assert.Single(_library.GetAllImages());
    }

    [Fact]
    public void ImportUnsupportedExtension_Fails()
    {
        var path = Path.Combine(_root, "junk.txt");
        File.WriteAllText(path, "not an image");
        var result = _library.ImportFile(path);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Imported);
    }

    [Fact]
    public async Task ImportMany_FromFolder_Counts()
    {
        // Distinct content so they are not treated as duplicates.
        File.WriteAllBytes(Path.Combine(_root, "one.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 1 });
        File.WriteAllBytes(Path.Combine(_root, "two.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 2 });
        var bad = Path.Combine(_root, "bad.txt");
        File.WriteAllText(bad, "nope");

        var result = await _library.ImportManyAsync(new[] { _root });
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Failed); // .txt not counted as supported
        Assert.Equal(2, _library.GetAllImages().Count);
    }

    [Fact]
    public void RecordShown_UpdatesStats()
    {
        var src = CreatePng("stat.png");
        var r = _library.ImportFile(src, "Stat");
        var img = r.ImportedImage!;
        _library.RecordShown(img.Id, "hotkey", 1800);
        var reloaded = _db.GetImage(img.Id);
        Assert.Equal(1, reloaded!.TimesShown);
        Assert.NotNull(reloaded.LastShownAt);
        var stats = _db.GetStats();
        Assert.Equal(1, stats.TotalCelebrations);
    }
}


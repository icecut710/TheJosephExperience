using System.IO;
using JosephExperience.Data;
using JosephExperience.Models;

namespace JosephExperience.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gjj_test_{Guid.NewGuid():N}.db");
        _db = new DatabaseService(_dbPath);
        _db.Initialize();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void FreshDatabase_CreatesSchema()
    {
        var version = _db.GetMetadata("schema_version");
        Assert.Equal("5", version);
    }

    [Fact]
    public void InsertImage_ThenRetrieve()
    {
        var image = new CelebrationImage
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = "Test Joseph",
            FileName = "test.jpg",
            FilePath = @"C:\tmp\test.jpg",
            Category = "Test",
            Tags = "a, b",
            Enabled = true,
            Favorite = false,
            Weight = 2
        };
        _db.InsertImage(image);

        var loaded = _db.GetImage(image.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Test Joseph", loaded!.DisplayName);
        Assert.Equal("Test", loaded.Category);
        Assert.Equal(2, loaded.Weight);
        Assert.Equal(0, loaded.TimesShown);
        Assert.True(loaded.Enabled);
        Assert.False(loaded.Favorite);
    }

    [Fact]
    public void UpdateImage_PersistsChanges()
    {
        var image = new CelebrationImage
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = "Old Name",
            FileName = "a.jpg",
            FilePath = @"C:\tmp\a.jpg"
        };
        _db.InsertImage(image);

        image.DisplayName = "New Name";
        image.Enabled = false;
        image.Favorite = true;
        image.Weight = 5;
        _db.UpdateImage(image);

        var loaded = _db.GetImage(image.Id);
        Assert.Equal("New Name", loaded!.DisplayName);
        Assert.False(loaded.Enabled);
        Assert.True(loaded.Favorite);
        Assert.Equal(5, loaded.Weight);
    }

    [Fact]
    public void DisableImage_ExcludedFromEligible()
    {
        var enabled1 = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "E1", FileName = "e1.jpg", FilePath = @"C:\tmp\e1.jpg", Enabled = true };
        var disabled = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "D", FileName = "d.jpg", FilePath = @"C:\tmp\d.jpg", Enabled = false };
        var enabled2 = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "E2", FileName = "e2.jpg", FilePath = @"C:\tmp\e2.jpg", Enabled = true };
        _db.InsertImage(enabled1);
        _db.InsertImage(disabled);
        _db.InsertImage(enabled2);

        var eligible = _db.GetEligibleImages(false);
        Assert.Equal(2, eligible.Count);
        Assert.DoesNotContain(eligible, i => i.Id == disabled.Id);
    }

    [Fact]
    public void FavoriteFilter_OnlyFavorites()
    {
        var fav = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "F", FileName = "f.jpg", FilePath = @"C:\tmp\f.jpg", Favorite = true, Enabled = true };
        var notFav = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "NF", FileName = "nf.jpg", FilePath = @"C:\tmp\nf.jpg", Favorite = false, Enabled = true };
        _db.InsertImage(fav);
        _db.InsertImage(notFav);

        var eligible = _db.GetEligibleImages(true);
        Assert.Single(eligible);
        Assert.Equal(fav.Id, eligible[0].Id);
    }

    [Fact]
    public void HistoryInsertion_RecordsStats()
    {
        var image = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "H", FileName = "h.jpg", FilePath = @"C:\tmp\h.jpg" };
        _db.InsertImage(image);

        _db.InsertHistory(new CelebrationHistory { ImageId = image.Id, TriggerType = "test", ShownAt = DateTime.UtcNow.ToString("o"), DurationMs = 1800 });
        _db.InsertHistory(new CelebrationHistory { ImageId = image.Id, TriggerType = "test", ShownAt = DateTime.UtcNow.ToString("o"), DurationMs = 1800 });

        var stats = _db.GetStats();
        Assert.Equal(2, stats.TotalCelebrations);
        Assert.Equal("H", stats.MostCelebratedJoseph);
        Assert.Equal(1, stats.ImageCount);
        Assert.NotNull(stats.LastCelebration);
    }

    [Fact]
    public void Statistics_IncrementTimesShown()
    {
        var image = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "S", FileName = "s.jpg", FilePath = @"C:\tmp\s.jpg" };
        _db.InsertImage(image);
        _db.IncrementTimesShown(image.Id);
        _db.IncrementTimesShown(image.Id);

        var loaded = _db.GetImage(image.Id);
        Assert.Equal(2, loaded!.TimesShown);
        Assert.NotNull(loaded.LastShownAt);
    }

    [Fact]
    public void DuplicateSha_Detected()
    {
        var image1 = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "A", FileName = "a.jpg", FilePath = @"C:\tmp\a.jpg", Sha256 = "abc123" };
        var image2 = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "B", FileName = "b.jpg", FilePath = @"C:\tmp\b.jpg", Sha256 = "abc123" };
        var image3 = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "C", FileName = "c.jpg", FilePath = @"C:\tmp\c.jpg", Sha256 = "def456" };
        _db.InsertImage(image1);
        _db.InsertImage(image2);
        _db.InsertImage(image3);

        var dup = _db.GetImageBySha256("abc123");
        Assert.NotNull(dup);
        Assert.Contains(dup!.Id, new[] { image1.Id, image2.Id });

        var notFound = _db.GetImageBySha256("xyz999");
        Assert.Null(notFound);
    }

    [Fact]
    public void DeleteImage_RemovesRecord()
    {
        var image = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "Del", FileName = "d.jpg", FilePath = @"C:\tmp\d.jpg" };
        _db.InsertImage(image);
        _db.DeleteImage(image.Id);
        Assert.Null(_db.GetImage(image.Id));
    }

    [Fact]
    public void InsertSoundClip_ThenRetrieve()
    {
        var clip = new SoundClip
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = "Ta-da",
            FileName = "ta-da.wav",
            FilePath = @"C:\tmp\ta-da.wav",
            Extension = ".wav",
            FileSize = 1234,
            Sha256 = "hash123"
        };
        _db.InsertSoundClip(clip);

        var loaded = _db.GetSoundClip(clip.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Ta-da", loaded!.DisplayName);
        Assert.Equal(".wav", loaded.Extension);
        Assert.Equal(1234, loaded.FileSize);
    }

    [Fact]
    public void InsertSoundClip_DuplicateSha_Detected()
    {
        var c1 = new SoundClip { Id = Guid.NewGuid().ToString(), DisplayName = "A", FileName = "a.wav", FilePath = @"C:\tmp\a.wav", Extension = ".wav", Sha256 = "dup" };
        var c2 = new SoundClip { Id = Guid.NewGuid().ToString(), DisplayName = "B", FileName = "b.wav", FilePath = @"C:\tmp\b.wav", Extension = ".wav", Sha256 = "dup" };
        _db.InsertSoundClip(c1);
        _db.InsertSoundClip(c2);

        var dup = _db.GetSoundClipBySha256("dup");
        Assert.NotNull(dup);
        Assert.True(dup!.Id == c1.Id || dup.Id == c2.Id);
        Assert.Null(_db.GetSoundClipBySha256("nope"));
    }

    [Fact]
    public void DeleteSoundClip_ClearsImageAssignments()
    {
        var clip = new SoundClip { Id = Guid.NewGuid().ToString(), DisplayName = "C", FileName = "c.wav", FilePath = @"C:\tmp\c.wav", Extension = ".wav" };
        _db.InsertSoundClip(clip);

        var image = new CelebrationImage { Id = Guid.NewGuid().ToString(), DisplayName = "J", FileName = "j.jpg", FilePath = @"C:\tmp\j.jpg", SoundId = clip.Id };
        _db.InsertImage(image);
        Assert.Equal(clip.Id, _db.GetImage(image.Id)!.SoundId);

        _db.DeleteSoundClip(clip.Id);

        Assert.Null(_db.GetSoundClip(clip.Id));
        Assert.Null(_db.GetImage(image.Id)!.SoundId);
    }
}


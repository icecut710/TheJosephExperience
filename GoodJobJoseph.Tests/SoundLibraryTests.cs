using System.IO;
using System.Text;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Services;

namespace JosephExperience.Tests;

public class SoundLibraryTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly string _soundsDir;
    private readonly DatabaseService _db;
    private readonly SoundLibraryService _sounds;

    public SoundLibraryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"gjj_sounds_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        _db = new DatabaseService(_dbPath);
        _db.Initialize();
        _soundsDir = Path.Combine(_root, "sounds");
        _sounds = new SoundLibraryService(_db, _soundsDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>Writes a small pseudo-WAV header + tone. Import only needs to copy bytes.</summary>
    private string CreateWav(string name, byte fill)
    {
        var path = Path.Combine(_root, name);
        var header = new byte[]
        {
            0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, // "RIFF...."
            0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74, 0x20, // "WAVEfmt "
            0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00
        };
        var body = new byte[512];
        Array.Fill(body, fill);
        using var fs = File.Create(path);
        fs.Write(header, 0, header.Length);
        fs.Write(body, 0, body.Length);
        return path;
    }

    [Fact]
    public void ImportSound_CopiesIntoManagedDir_PreservesExtension()
    {
        var src = CreateWav("ta-da.wav", 0xAB);
        var result = _sounds.ImportSound(src);

        Assert.Equal(1, result.Imported);
        Assert.NotNull(result.Sound);
        Assert.Null(result.Error);

        var clip = result.Sound!;
        Assert.NotEqual(src, clip.FilePath);
        Assert.True(File.Exists(clip.FilePath));
        Assert.StartsWith(_soundsDir, Path.GetDirectoryName(clip.FilePath), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(".wav", Path.GetExtension(clip.FilePath), ignoreCase: true);
        Assert.Equal("ta-da", clip.DisplayName);
        Assert.NotEqual(Path.GetFileName(src), clip.FileName); // collision-safe uuid name
        Assert.Equal(new FileInfo(src).Length, clip.FileSize);
        Assert.False(string.IsNullOrWhiteSpace(clip.Sha256));
    }

    [Fact]
    public void ImportSound_ReimportSameBytes_IsDuplicateNoOp()
    {
        var src = CreateWav("loop.wav", 0x11);
        var first = _sounds.ImportSound(src);
        var second = _sounds.ImportSound(src);

        Assert.Equal(1, first.Imported);
        Assert.True(second.SkippedDuplicate);
        Assert.Equal(first.Sound!.Id, second.Sound!.Id);

        var all = _sounds.GetAllSounds();
        Assert.Single(all);
    }

    [Fact]
    public void ImportSound_UnsupportedExtension_Rejected()
    {
        var src = Path.Combine(_root, "clip.ogg");
        File.WriteAllBytes(src, new byte[] { 0x01, 0x02, 0x03 });

        var result = _sounds.ImportSound(src);

        Assert.Equal(0, result.Imported);
        Assert.False(result.SkippedDuplicate);
        Assert.NotNull(result.Error);
        Assert.Empty(_sounds.GetAllSounds());
    }

    [Fact]
    public void ImportSound_MissingFile_Rejected()
    {
        var result = _sounds.ImportSound(Path.Combine(_root, "nope.wav"));

        Assert.Equal(0, result.Imported);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void DeleteSound_RemovesFileRecordAndClearsAssignments()
    {
        var src = CreateWav("bye.wav", 0x22);
        var result = _sounds.ImportSound(src);
        Assert.Equal(1, result.Imported);
        var clip = result.Sound!;
        var managedPath = clip.FilePath;

        var image = new CelebrationImage
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = "J",
            FileName = "j.jpg",
            FilePath = @"C:\tmp\j.jpg",
            SoundId = clip.Id
        };
        _db.InsertImage(image);
        Assert.Equal(clip.Id, _db.GetImage(image.Id)!.SoundId);

        var deleted = _sounds.DeleteSound(clip);

        Assert.True(deleted);
        Assert.Null(_db.GetSoundClip(clip.Id));
        Assert.Null(_db.GetImage(image.Id)!.SoundId);
        Assert.False(File.Exists(managedPath));
        Assert.Empty(_sounds.GetAllSounds());
    }

    [Fact]
    public void DeleteSound_MissingBackingFile_DoesNotThrow()
    {
        var src = CreateWav("ghost.wav", 0x33);
        var result = _sounds.ImportSound(src);
        var clip = result.Sound!;
        File.Delete(clip.FilePath);

        // Must not throw even though the file is already gone.
        var deleted = _sounds.DeleteSound(clip);

        Assert.True(deleted);
        Assert.Null(_db.GetSoundClip(clip.Id));
    }

    [Fact]
    public void GetRandomSound_OnlyReturnsClipWithExistingFile()
    {
        var good = _sounds.ImportSound(CreateWav("good.wav", 0x44)).Sound!;
        var ghost = _sounds.ImportSound(CreateWav("ghost.wav", 0x55)).Sound!;
        File.Delete(ghost.FilePath);

        var pick = _sounds.GetRandomSound();

        Assert.NotNull(pick);
        Assert.Equal(good.Id, pick!.Id);
        Assert.True(File.Exists(pick.FilePath));
    }

    [Fact]
    public void GetRandomSound_ReturnsNull_WhenNoClipExists()
    {
        var clip = _sounds.ImportSound(CreateWav("gone.wav", 0x66)).Sound!;
        File.Delete(clip.FilePath);

        Assert.Null(_sounds.GetRandomSound());
    }

    [Fact]
    public void RecordPlayed_IncrementsTimesPlayed()
    {
        var clip = _sounds.ImportSound(CreateWav("hit.wav", 0x77)).Sound!;

        _sounds.RecordPlayed(clip);
        _sounds.RecordPlayed(clip);

        var reloaded = _db.GetSoundClip(clip.Id);
        Assert.Equal(2, reloaded!.TimesPlayed);
    }
}
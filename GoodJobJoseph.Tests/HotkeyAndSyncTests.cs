using System.IO;
using System.Windows.Input;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Utilities;
using JosephExperience.Services;

namespace JosephExperience.Tests;

public class HotkeyConverterTests
{
    [Fact]
    public void BuildBinding_ShiftPlusD3_IsShiftAndVK3()
    {
        var b = HotkeyConverter.BuildBinding(Key.D3, ModifierKeys.Shift);
        Assert.Equal(HotkeyConverter.MOD_SHIFT, b.ModifierValue);
        Assert.Equal(0x33u, b.VirtualKey);
        Assert.Equal("3", b.KeyName);
    }

    [Fact]
    public void BuildBinding_PlainF8_HasNoModifier()
    {
        var b = HotkeyConverter.BuildBinding(Key.F8, ModifierKeys.None);
        Assert.Equal(0u, b.ModifierValue);
        Assert.Equal(0x77u, b.VirtualKey);
        Assert.Equal("F8", b.KeyName);
    }

    [Fact]
    public void BuildBinding_CtrlAltA_IsControlAndAlt()
    {
        var b = HotkeyConverter.BuildBinding(Key.A, ModifierKeys.Control | ModifierKeys.Alt);
        Assert.Equal(HotkeyConverter.MOD_CONTROL | HotkeyConverter.MOD_ALT, b.ModifierValue);
        Assert.Equal(0x41u, b.VirtualKey);
    }

    [Fact]
    public void ParseBinding_NeverUsesHashForShiftDigits()
    {
        var b = HotkeyConverter.ParseBinding("Shift", "3");
        Assert.NotNull(b);
        Assert.Equal(HotkeyConverter.MOD_SHIFT, b!.ModifierValue);
        Assert.Equal(0x33u, b.VirtualKey);
        Assert.DoesNotContain("#", b.DisplayName);
    }

    [Fact]
    public void Resolve_PrefersStructuralFields()
    {
        var s = new AppSettings
        {
            HotkeyModifierValue = HotkeyConverter.MOD_CONTROL,
            HotkeyVirtualKey = 0x31,
            HotkeyKeyName = "1",
            HotkeyModifiers = "Alt", // legacy, should be ignored
            HotkeyKey = "A"
        };
        var b = HotkeyConverter.Resolve(s);
        Assert.Equal(HotkeyConverter.MOD_CONTROL, b.ModifierValue);
        Assert.Equal(0x31u, b.VirtualKey);
        Assert.Equal("1", b.KeyName);
    }

    [Fact]
    public void Settings_DefaultOpacity_IsFull()
    {
        var s = new AppSettings();
        Assert.InRange(s.ImageOpacity, 0.999, 1.0);
    }

    [Fact]
    public void DefaultHotkey_IsF2()
    {
        var s = new AppSettings();
        Assert.Equal(0x71u, s.HotkeyVirtualKey);
        Assert.Equal("F2", s.HotkeyKeyName);
    }

    [Fact]
    public void GetDefaultBinding_AudioToggle_IsF8()
    {
        var b = HotkeyConverter.GetDefaultBinding(HotkeyAction.AudioToggle);
        Assert.Equal(0u, b.ModifierValue);
        Assert.Equal(0x77u, b.VirtualKey);
        Assert.Equal("F8", b.KeyName);
    }

    [Fact]
    public void HotkeyAction_Enum_HasOnlyCelebrationAndAudioToggle()
    {
        var names = Enum.GetNames<HotkeyAction>();
        Assert.Contains("Celebration", names);
        Assert.Contains("AudioToggle", names);
        Assert.DoesNotContain("SecondaryCelebration", names);
        Assert.DoesNotContain("CycleSound", names);
        Assert.DoesNotContain("StopAudio", names);
    }
}

public class DatabaseRemoteSyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseRemoteSyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gjj_remotetest_{Guid.NewGuid():N}.db");
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
    public void SchemaVersion_IsThree_AndRemoteColumnsExist()
    {
        Assert.Equal("5", _db.GetMetadata("schema_version"));

        var img = new CelebrationImage
        {
            Id = "a",
            DisplayName = "Remote Joseph",
            FileName = "a.jpg",
            FilePath = @"C:\cache\a.jpg",
            RemoteId = "11111111-1111-1111-1111-111111111111",
            StoragePath = "images/a.jpg",
            RemoteUpdatedAt = "2026-01-01T00:00:00Z",
            Sha256 = "abc"
        };
        _db.InsertImage(img);

        var loaded = _db.GetImageByRemoteId(img.RemoteId);
        Assert.NotNull(loaded);
        Assert.Equal(img.StoragePath, loaded!.StoragePath);
        Assert.Equal(img.RemoteUpdatedAt, loaded.RemoteUpdatedAt);
    }

    [Fact]
    public void UpsertRemoteImage_DoesNotDuplicate()
    {
        var img = new CelebrationImage
        {
            Id = "b",
            DisplayName = "R",
            FileName = "b.png",
            FilePath = @"C:\cache\b.png",
            RemoteId = "22222222-2222-2222-2222-222222222222",
            StoragePath = "images/b.png",
            Sha256 = "def"
        };
        _db.UpsertRemoteImage(img);
        _db.UpsertRemoteImage(img);

        var all = _db.GetAllImages();
        Assert.Single(all, i => i.RemoteId == img.RemoteId);
    }

    [Fact]
    public void GetImageBySha256_StillWorks_AfterMigration()
    {
        var img = new CelebrationImage
        {
            Id = "c",
            DisplayName = "Dup",
            FileName = "c.jpg",
            FilePath = @"C:\cache\c.jpg",
            Sha256 = "deadbeef"
        };
        _db.InsertImage(img);
        Assert.NotNull(_db.GetImageBySha256("deadbeef"));
        Assert.Null(_db.GetImageBySha256("nope"));
    }

    [Fact]
    public void UpdateImage_PersistsRemoteFields()
    {
        var img = new CelebrationImage
        {
            Id = "d",
            DisplayName = "X",
            FileName = "d.jpg",
            FilePath = @"C:\cache\d.jpg",
            RemoteId = "44444444-4444-4444-4444-444444444444",
            RemoteUpdatedAt = "2026-02-01T00:00:00Z",
            LastSyncedAt = "2026-02-01T00:00:00Z",
            Sha256 = "11"
        };
        _db.InsertImage(img);

        img.DisplayName = "Updated";
        img.RemoteUpdatedAt = "2026-03-01T00:00:00Z";
        _db.UpdateImage(img);

        var loaded = _db.GetImageByRemoteId(img.RemoteId);
        Assert.NotNull(loaded);
        Assert.Equal("Updated", loaded!.DisplayName);
        Assert.Equal("2026-03-01T00:00:00Z", loaded.RemoteUpdatedAt);
    }
}





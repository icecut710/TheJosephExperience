using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using JosephExperience.Services.CounterStrike;
using JosephExperience.Services;

namespace JosephExperience.Tests;

public class StabilizationRegressionTests
{
    [Fact]
    public void Settings_Load_PreservesCustomHotkeysAndForcesCanonicalGsiPort()
    {
        var path = Path.Combine(Path.GetTempPath(), "JosephSettings-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{"GameIntegrationPort":8080,"HotkeyModifierValue":2,"HotkeyVirtualKey":90,"HotkeyKeyName":"Z","AudioToggleHotkey":{"ModifierValue":2,"VirtualKey":88,"KeyName":"X"}}""");
            var settings = new SettingsService(path).Current;
            Assert.Equal(3000, settings.GameIntegrationPort);
            Assert.Equal((uint)2, settings.HotkeyModifierValue);
            Assert.Equal((uint)90, settings.HotkeyVirtualKey);
            Assert.Equal("Z", settings.HotkeyKeyName);
            Assert.Equal((uint)2, settings.AudioToggleHotkey.ModifierValue);
            Assert.Equal((uint)88, settings.AudioToggleHotkey.VirtualKey);
            Assert.Equal("X", settings.AudioToggleHotkey.KeyName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Settings_Load_PreservesExplicitlyClearedHotkeys()
    {
        var path = Path.Combine(Path.GetTempPath(), "JosephSettings-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{"HotkeyVirtualKey":0,"HotkeyKeyName":"","AudioToggleHotkey":{"VirtualKey":0,"KeyName":""}}""");
            var settings = new SettingsService(path).Current;
            Assert.True(settings.GetBinding(HotkeyAction.Celebration).IsEmpty);
            Assert.True(settings.GetBinding(HotkeyAction.AudioToggle).IsEmpty);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Repair_PreservesThirdPartyConfigOnSamePort()
    {
        var dir = Path.Combine(Path.GetTempPath(), "JosephConfig-" + Guid.NewGuid(), "cfg");
        Directory.CreateDirectory(dir);
        try
        {
            var other = Path.Combine(dir, "gamestate_integration_other.cfg");
            const string content = "\"Other app\" { \"uri\" \"http://127.0.0.1:3000/\" }";
            File.WriteAllText(other, content);
            new GsiConfigManager().MigrateOldConfigs(dir, 3000, null);
            Assert.Equal(content, File.ReadAllText(other));
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [Fact]
    public void Locate_ChoosesNestedCfgBeforeInstallRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "JosephLocate-" + Guid.NewGuid());
        var cfg = Path.Combine(dir, "game", "csgo", "cfg");
        Directory.CreateDirectory(cfg);
        try { Assert.Equal(cfg, new GsiConfigManager().FindConfigFolder(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("{\"auth\":{\"token\":\"secret\"}}", HttpStatusCode.OK)]
    [InlineData("{\"auth\":{\"token\":\"not-secret\"}}", HttpStatusCode.Unauthorized)]
    [InlineData("{\"token\":\"secret\"}", HttpStatusCode.Unauthorized)]
    public async Task Auth_RequiresExactAuthField(string body, HttpStatusCode expected)
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start(); var port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
        using var server = new GsiServer();
        Assert.True(server.TryStart(port, "secret", out var error), error);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.PostAsync($"http://127.0.0.1:{port}/", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(expected, response.StatusCode);
    }
}

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using JosephExperience.Models.CounterStrike;
using JosephExperience.Services.CounterStrike;
using Xunit;

namespace JosephExperience.Tests;

/// <summary>
/// Runtime validation of the CS2 GSI HTTP pipeline using the REAL &lt;see cref="GsiServer"/&gt;
/// over real HTTP. Proves the listener actually binds, accepts a POST, enforces auth,
/// enforces size limits, and that a valid payload flows through parser -> detector.
/// This is the layer the fixture tests (which feed synthetic JSON straight to the
/// parser/detector) cannot cover.
/// </summary>
public class CounterStrikeHttpIntegrationTests : IDisposable
{
    private static int FreePort()
    {
        // Bind to port 0, let the OS assign a free port, then release it.
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    // $$$""" => {{{ opens a hole, }}} closes it; {{ and }} are literal (matches the JSON structure).
    private static string KillJson(int kills) => $$$"""
        {"map":{"name":"de_mirage","phase":"live","round":3},
         "round":{"phase":"live","round":3},
         "player":{"steamid":"76561198000000001","team":"CT",
                   "activity":{"living":"alive"},"state":{"health":100},
                   "match_stats":{"kills":{{{kills}}},"mvp":0}},
         "provider":{"name":"Counter-Strike 2","steamid":"76561198","timestamp":1700000000}}
        """;

    private readonly GsiServer _server;
    private readonly int _port;

    public CounterStrikeHttpIntegrationTests()
    {
        _server = new GsiServer();
        _port = FreeStart();
    }

    private int FreeStart()
    {
        var port = FreePort();
        var ok = _server.TryStart(port, "secret", out var error);
        Assert.True(ok, $"Expected GsiServer to start on port {port}. Error: {error ?? "(none)"}");
        return port;
    }

    private async Task<HttpStatusCode> PostAsync(string body, string? auth = null, string? contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/");
        request.Content = new StringContent(body, Encoding.UTF8, contentType ?? "application/json");
        if (auth is not null)
            request.Headers.TryAddWithoutValidation("Authorization", auth);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var resp = await client.SendAsync(request);
        return resp.StatusCode;
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryStart_BindsLoopback_PortReported()
    {
        Assert.True(_server.IsRunning);
        Assert.Equal(_port, _server.Port);
    }

    [Fact]
    public async Task Post_ValidPayload_Accepted200()
    {
        var code = await PostAsync(KillJson(1), auth: "secret");
        Assert.Equal(HttpStatusCode.OK, code);
        Assert.NotNull(_server.LastPayloadUtc);
    }

    [Fact]
    public async Task Post_WrongToken_Rejected401()
    {
        var code = await PostAsync(KillJson(1), auth: "wrong-token");
        Assert.Equal(HttpStatusCode.Unauthorized, code);
        Assert.Null(_server.LastPayloadUtc); // rejected payloads do not update LastPayloadUtc
    }

    [Fact]
    public async Task Post_BearerToken_Accepted()
    {
        var code = await PostAsync(KillJson(1), auth: "Bearer secret");
        Assert.Equal(HttpStatusCode.OK, code);
    }

    [Fact]
    public async Task Post_NoToken_Rejected401()
    {
        var code = await PostAsync(KillJson(1), auth: null);
        Assert.Equal(HttpStatusCode.Unauthorized, code);
    }

    [Fact]
    public async Task Post_NonPostMethod_Rejected405()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var resp = await client.GetAsync($"http://127.0.0.1:{_port}/");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, resp.StatusCode);
    }

    [Fact]
    public async Task Post_OversizedPayload_Rejected413()
    {
        // Build a body larger than MaxPayloadBytes (2 MB) without allocating 2 MB of JSON.
        var huge = new string('x', 3 * 1024 * 1024);
        var code = await PostAsync(huge, auth: "secret", contentType: "text/plain");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, code);
    }

    [Fact]
    public async Task PayloadReceived_Fires_WithCorrectBody()
    {
        string? received = null;
        _server.PayloadReceived += (_, args) => received = args.Body;
        await PostAsync(KillJson(2), auth: "secret");
        // Allow the async handler a moment to fire the event.
        for (var i = 0; i < 50 && received is null; i++)
            await Task.Delay(20);
        Assert.NotNull(received);
        Assert.Contains("\"kills\":2", received);
    }

    [Fact]
    public async Task FullPipeline_KillIncrement_ProducesExactlyOneKillEvent()
    {
        // Real server -> real parser -> real detector. Two payloads: kills=1 then kills=2.
        var parser = new GsiPayloadParser();
        var detector = new GameEventDetector();
        GsiSnapshot? last = null;
        var produced = new List<GameEventType>();

        void OnReceived(object? s, GsiPayloadReceivedEventArgs args)
        {
            var snap = parser.Parse(args.Body);
            if (snap is null) return;
            var events = detector.Detect(last, snap);
            last = snap;
            produced.AddRange(events.Select(e => e.Type));
        }
        _server.PayloadReceived += OnReceived;

        await PostAsync(KillJson(1), auth: "secret");
        await Task.Delay(50);
        await PostAsync(KillJson(2), auth: "secret");
        await Task.Delay(50);

        Assert.Single(produced, e => e == GameEventType.Kill);
    }
}

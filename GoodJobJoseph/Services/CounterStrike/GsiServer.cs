using System.Net;
using JosephExperience.Utilities;

namespace JosephExperience.Services.CounterStrike;

/// <summary>Events raised for every accepted GSI payload.</summary>
public sealed record GsiPayloadReceivedEventArgs(string Body, DateTime ReceivedUtc);

public interface IGsiServer : IDisposable
{
    bool IsRunning { get; }
    int Port { get; }
    DateTime? LastPayloadUtc { get; }
    event EventHandler<GsiPayloadReceivedEventArgs>? PayloadReceived;
    /// <summary>Starts the loopback listener. Returns false (with reason) instead of throwing.</summary>
    bool TryStart(int port, string? authToken, out string? error);
    void Stop();
}

/// <summary>
/// Local HTTP listener for Valve Game-State Integration payloads.
/// Binds ONLY to 127.0.0.1 / localhost. Rejects non-POST requests, oversized
/// payloads and (when configured) invalid auth tokens. Never throws into the caller.
/// </summary>
public sealed class GsiServer : IGsiServer
{
    private const long MaxPayloadBytes = 2 * 1024 * 1024; // real GSI payloads are a few KB.
    // Port range we are willing to try (avoid well-known ports 1024-49151 which are
    // typically reserved/static; the dynamic/private range 49152-65535 is recommended
    // by IANA for short-lived apps. If the user sets a port outside this range we
    // still attempt it but log a warning.
    private static readonly int MinDynamicPort = 49152;
    private static readonly int MaxDynamicPort = 65535;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string? _authToken;
    private readonly object _gate = new();

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public DateTime? LastPayloadUtc { get; private set; }
    public event EventHandler<GsiPayloadReceivedEventArgs>? PayloadReceived;

    public bool TryStart(int port, string? authToken, out string? error)
    {
        error = null;
        lock (_gate)
        {
            if (IsRunning) return true;
            _authToken = string.IsNullOrEmpty(authToken) ? null : authToken;

            // Port validation: prefer dynamic/private range 49152-65535.
            // If the port is outside this range we still attempt it but log a warning.
            bool portInDynamicRange = (port >= MinDynamicPort && port <= MaxDynamicPort);
            if (!portInDynamicRange)
                AppLog.Warn($"GsiServer: port {port} is outside the recommended dynamic range ({MinDynamicPort}-{MaxDynamicPort}); may conflict with other services.");

            try
            {
                var listener = new HttpListener();
                // Loopback-only prefixes. Never bind to a public interface.
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();

                _listener = listener;
                Port = port;
                _cts = new CancellationTokenSource();
                _loop = Task.Run(() => LoopAsync(listener, _cts.Token));
                IsRunning = true;
                AppLog.Info($"GsiServer: listening on http://127.0.0.1:{port}/");
                return true;
            }
            catch (HttpListenerException hex)
            {
                error = hex.ErrorCode switch
                {
                    32 or 183 or 10048 => $"Port {port} is already in use by another application.",
                    5 => $"Access denied binding port {port}. Try a different port.",
                    _ => $"Listener failed: {hex.Message}"
                };
                AppLog.Warn($"GsiServer start failed ({hex.ErrorCode}): {hex.Message}");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLog.Warn($"GsiServer start failed: {ex.Message}");
            }

            try { _listener?.Close(); } catch (Exception ex) { AppLog.Warn($"GsiServer: listener close failed: {ex.Message}"); }
            _listener = null;
            IsRunning = false;
            return false;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning && _listener is null) return;
            IsRunning = false;
            try { _cts?.Cancel(); } catch (Exception ex) { AppLog.Warn($"GsiServer: CTS cancel failed: {ex.Message}"); }
            try { _listener?.Stop(); _listener?.Close(); } catch (Exception ex) { AppLog.Warn($"GsiServer: listener stop failed: {ex.Message}"); }
            _listener = null;
            try { _cts?.Dispose(); } catch (Exception ex) { AppLog.Warn($"GsiServer: CTS dispose failed: {ex.Message}"); }
            _cts = null;
            _loop = null;
            AppLog.Info("GsiServer: stopped.");
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Validates the auth token. CS2's GSI client does NOT send HTTP auth headers —
    /// the "auth" block from the .cfg is embedded in the JSON body, e.g.
    /// "auth":{"token":"..."} (and some providers echo it as "auth":"token").
    /// We therefore accept: an Authorization header (raw or Bearer), an x-auth-token
    /// header, or the token inside the payload JSON. This keeps test harnesses that
    /// use headers working while accepting genuine CS2 traffic.
    /// </summary>
    private bool IsAuthorized(HttpListenerRequest request, string body)
    {
        if (string.IsNullOrEmpty(_authToken)) return true;

        var presented = request.Headers["Authorization"]
                        ?? request.Headers["x-auth-token"] ?? "";
        if (presented.StartsWith("Bearer ", StringComparison.Ordinal))
            presented = presented.Substring("Bearer ".Length);
        if (string.Equals(presented, _authToken, StringComparison.Ordinal))
            return true;

        // Token embedded in the GSI payload itself (what real CS2 traffic looks like).
        return body.Contains("\"token\"", StringComparison.OrdinalIgnoreCase) &&
               body.Contains(_authToken, StringComparison.Ordinal);
    }

    private async Task LoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested || !listener.IsListening)
            {
                break; // normal shutdown
            }
            catch (Exception ex)
            {
                AppLog.Warn($"GsiServer listener error: {ex.Message}");
                continue;
            }

            _ = Task.Run(() => HandleAsync(ctx, token), token);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken token)
    {
        try
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = 405;
                return;
            }

            // Reject unreasonable payload sizes before buffering them.
            if (ctx.Request.ContentLength64 > MaxPayloadBytes)
            {
                ctx.Response.StatusCode = 413;
                AppLog.Warn($"GsiServer: rejected oversized payload ({ctx.Request.ContentLength64} bytes).");
                return;
            }

            string body;
            using (var reader = new System.IO.StreamReader(ctx.Request.InputStream, System.Text.Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }
            if (System.Text.Encoding.UTF8.GetByteCount(body) > MaxPayloadBytes)
            {
                ctx.Response.StatusCode = 413;
                return;
            }

            if (!IsAuthorized(ctx.Request, body))
            {
                ctx.Response.StatusCode = 401;
                AppLog.Warn("GsiServer: rejected payload with invalid auth token.");
                return;
            }

            LastPayloadUtc = DateTime.UtcNow;
            PayloadReceived?.Invoke(this, new GsiPayloadReceivedEventArgs(body, LastPayloadUtc.Value));
            ctx.Response.StatusCode = 200;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"GsiServer handle error: {ex.Message}");
            try { ctx.Response.StatusCode = 500; } catch (Exception ex2) { AppLog.Warn($"GsiServer: failed to set 500 status: {ex2.Message}"); }
        }
        finally
        {
            try { ctx.Response.Close(); } catch (Exception ex2) { AppLog.Warn($"GsiServer: failed to close response: {ex2.Message}"); }
        }
    }
}
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

/// <summary>
/// Real NADD/SOL token market data from GeckoTerminal API for the
/// NADD / SOL pool on Raydium (4JHhAqVBXNfNCPCcbEkCZp3yPD6rxCgNc3AP2EpFUsBS).
/// </summary>
public class RealNaddService : IDisposable
{
    private const string PoolAddress = "4JHhAqVBXNfNCPCcbEkCZp3yPD6rxCgNc3AP2EpFUsBS";
    private const string BaseUrl = "https://api.geckoterminal.com";
    private const string ApiVersion = "v2";
    private const string Network = "solana";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "JosephExperience/2.0" } }
    };

    private readonly Timer _refreshTimer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private NaddMarketData? _latestData;
    private bool _disposed;
    private bool _isRefreshing;

    public NaddMarketData? CurrentData => _latestData;
    public bool IsRefreshing => _isRefreshing;

    public event Action? DataUpdated;

    public RealNaddService()
    {
        _refreshTimer = new Timer(async _ => await RefreshAsync(), null, TimeSpan.FromSeconds(8), TimeSpan.FromMinutes(2));
    }

    public async Task RefreshAsync()
    {
        if (!await _lock.WaitAsync(TimeSpan.Zero).ConfigureAwait(false)) return;
        try
        {
            _isRefreshing = true;
            var data = await FetchMarketDataAsync();
            if (data is not null)
            {
                _latestData = data;
                AppLog.Info($"NADD market data updated: price={FormatPrice(data.PriceUsd)}, 24h={data.Change24hPercent:+0.00;-0.00;0.00}%");
                DataUpdated?.Invoke();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"NADD market data refresh failed: {ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
            _lock.Release();
        }
    }

    private async Task<NaddMarketData?> FetchMarketDataAsync()
    {
        try
        {
            var url = $"{BaseUrl}/api/{ApiVersion}/networks/{Network}/pools/{PoolAddress}";
            var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warn($"GeckoTerminal API returned {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var dataElem) ||
                !dataElem.TryGetProperty("attributes", out var attrs))
            {
                AppLog.Warn("GeckoTerminal response missing data.attributes");
                return null;
            }

            var priceUsd = ReadNumber(attrs, "base_token_price_usd");
            var change24h = ReadNestedNumber(attrs, "price_change_percentage", "h24");
            var liquidityUsd = ReadNumber(attrs, "reserve_in_usd");
            var volume24h = ReadNestedNumber(attrs, "volume_usd", "h24");
            var marketCap = ReadNumber(attrs, "market_cap_usd");
            var fdv = ReadNumber(attrs, "fdv_usd");
            var buyTx = (int)ReadNestedNumber(attrs, "transactions", "h24", "buys");
            var sellTx = (int)ReadNestedNumber(attrs, "transactions", "h24", "sells");

            // Fetch OHLCV data for price graph
            var ohlcv = await FetchOhlcvAsync();

            return new NaddMarketData
            {
                PriceUsd = priceUsd,
                Change24hPercent = (float)change24h,
                LiquidityUsd = liquidityUsd,
                Volume24hUsd = volume24h,
                MarketCapUsd = marketCap,
                FdvUsd = fdv,
                Transactions24h = buyTx + sellTx,
                BuyTransactions24h = buyTx,
                SellTransactions24h = sellTx,
                LastUpdated = DateTime.UtcNow,
                OhlcvData = ohlcv
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn($"NADD market data fetch error: {ex.Message}");
            return null;
        }
    }

    private async Task<List<OhlcvPoint>?> FetchOhlcvAsync()
    {
        try
        {
            var url = BuildOhlcvUrl("24H");
            var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var dataElem) ||
                !dataElem.TryGetProperty("attributes", out var attrs) ||
                !attrs.TryGetProperty("ohlcv_list", out var ohlcvList))
            {
                return null;
            }

            var points = new List<OhlcvPoint>();
            foreach (var item in ohlcvList.EnumerateArray())
            {
                // GeckoTerminal OHLCV format: [timestamp, open, high, low, close, volume]
                if (item.GetArrayLength() >= 5)
                {
                    var ts = item[0].GetDouble();
                    var open = item[1].GetDouble();
                    var high = item[2].GetDouble();
                    var low = item[3].GetDouble();
                    var close = item[4].GetDouble();

                    points.Add(new OhlcvPoint
                    {
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)ts).LocalDateTime,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close
                    });
                }
            }

            return points.Count > 0 ? points : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"NADD OHLCV fetch error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fetches OHLCV data for a specific range (1H, 6H, 24H, 7D, 30D).
    /// </summary>
    public async Task<List<OhlcvPoint>?> FetchOhlcvRangeAsync(string range)
    {
        try
        {
            var url = BuildOhlcvUrl(range);
            var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var dataElem) ||
                !dataElem.TryGetProperty("attributes", out var attrs) ||
                !attrs.TryGetProperty("ohlcv_list", out var ohlcvList))
            {
                return null;
            }

            var points = new List<OhlcvPoint>();
            foreach (var item in ohlcvList.EnumerateArray())
            {
                if (item.GetArrayLength() >= 5)
                {
                    var ts = item[0].GetDouble();
                    var open = item[1].GetDouble();
                    var high = item[2].GetDouble();
                    var low = item[3].GetDouble();
                    var close = item[4].GetDouble();

                    points.Add(new OhlcvPoint
                    {
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)ts).LocalDateTime,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close
                    });
                }
            }

            return points.Count > 0 ? points : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"NADD OHLCV ({range}) fetch error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Formats a tiny nonzero price with enough significant digits to avoid showing $0.00.
    /// Example: $0.00000004493
    /// </summary>
    public static string FormatPrice(double priceUsd)
    {
        if (priceUsd <= 0) return "$0.00";

        if (priceUsd >= 1.0)
            return $"${priceUsd:F2}";

        if (priceUsd >= 0.01)
            return $"${priceUsd:F4}";

        if (priceUsd >= 0.0001)
            return $"${priceUsd:F6}";

        if (priceUsd >= 0.000001)
            return $"${priceUsd:F8}";

        // For extremely tiny prices, use scientific notation with enough digits
        return $"${priceUsd:E6}".Replace("E", "×10^");
    }

    private static string BuildOhlcvUrl(string range)
    {
        var (timeframe, aggregate, limit) = range.ToUpperInvariant() switch
        {
            "1H" => ("minute", 5, 12),
            "6H" => ("minute", 15, 24),
            "7D" => ("hour", 4, 42),
            "30D" => ("day", 1, 30),
            _ => ("hour", 1, 24)
        };
        return $"{BaseUrl}/api/{ApiVersion}/networks/{Network}/pools/{PoolAddress}/ohlcv/{timeframe}?aggregate={aggregate}&limit={limit}";
    }

    private static double ReadNumber(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number : 0;
    }

    private static double ReadNestedNumber(JsonElement parent, string objectName, string property, string? child = null)
    {
        if (!parent.TryGetProperty(objectName, out var nested) || !nested.TryGetProperty(property, out var value)) return 0;
        return child is null ? ReadNumber(nested, property) : ReadNumber(value, child);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Dispose();
        _lock?.Dispose();
    }
}

/// <summary>
/// Real NADD/SOL market data from GeckoTerminal.
/// </summary>
public class NaddMarketData
{
    public double PriceUsd { get; init; }
    public float Change24hPercent { get; init; }
    public double LiquidityUsd { get; init; }
    public double Volume24hUsd { get; init; }
    public double MarketCapUsd { get; init; }
    public double FdvUsd { get; init; }
    public int Transactions24h { get; init; }
    public int BuyTransactions24h { get; init; }
    public int SellTransactions24h { get; init; }
    public DateTime LastUpdated { get; init; }
    public List<OhlcvPoint>? OhlcvData { get; init; }
}

/// <summary>
/// OHLCV (Open, High, Low, Close, Volume) data point for price charts.
/// </summary>
public class OhlcvPoint
{
    public DateTime Timestamp { get; init; }
    public double Open { get; init; }
    public double High { get; init; }
    public double Low { get; init; }
    public double Close { get; init; }
}

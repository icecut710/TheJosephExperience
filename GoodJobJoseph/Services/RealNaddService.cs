using System.Net.Http;
using System.Text.Json;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

/// <summary>
/// Real NADD/SOL token market data from GeckoTerminal API for the
/// NADD / SOL pool on Raydium (4JHhAqVBXNfNCPCcbEkCZp3yPD6rxCgNc3AP2EpFUsBS).
/// This is separate from the internal Joseph Coin reward system.
/// </summary>
public class RealNaddService : IDisposable
{
    private const string PoolAddress = "4JHhAqVBXNfNCPCcbEkCZp3yPD6rxCgNc3AP2EpFUsBS";
    private const string BaseUrl = "https://api.geckoterminal.com";
    private const string ApiVersion = "v2";

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
        _refreshTimer = new Timer(async _ => await RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(2));
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
            var url = $"{BaseUrl}/api/{ApiVersion}/pools/{PoolAddress}";
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

            var priceStr = attrs.GetProperty("price_usd").GetString() ?? "0";
            if (!double.TryParse(priceStr, out var priceUsd)) priceUsd = 0;

            var change24hStr = attrs.TryGetProperty("price_change_percentage_24h", out var changeElem)
                ? changeElem.GetSingle()
                : 0f;

            var liquidityUsd = attrs.TryGetProperty("pool_created_via_mpl", out _)
                ? 0L
                : attrs.TryGetProperty("liquidity_usd", out var liqElem)
                    ? liqElem.GetDouble()
                    : 0;

            var volume24h = attrs.TryGetProperty("volume_usd_24h", out var volElem)
                ? volElem.GetDouble()
                : 0;

            var marketCap = attrs.TryGetProperty("market_cap_usd", out var mcElem)
                ? mcElem.GetDouble()
                : 0;

            var fdv = attrs.TryGetProperty("fdv_usd", out var fdvElem)
                ? fdvElem.GetDouble()
                : 0;

            var transactions24h = attrs.TryGetProperty("transactions_24h", out var txElem)
                ? txElem.GetDouble()
                : 0;

            var buyTx = attrs.TryGetProperty("buy_transactions_24h", out var buyElem)
                ? buyElem.GetInt32()
                : 0;

            var sellTx = attrs.TryGetProperty("sell_transactions_24h", out var sellElem)
                ? sellElem.GetInt32()
                : 0;

            // Fetch OHLCV data for price graph
            var ohlcv = await FetchOhlcvAsync();

            return new NaddMarketData
            {
                PriceUsd = priceUsd,
                Change24hPercent = (float)change24hStr,
                LiquidityUsd = liquidityUsd,
                Volume24hUsd = volume24h,
                MarketCapUsd = marketCap,
                FdvUsd = fdv,
                Transactions24h = (int)transactions24h,
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
            var range = "24H";
            var url = $"{BaseUrl}/api/{ApiVersion}/pools/{PoolAddress}/ohlcv/{range}";
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
            var url = $"{BaseUrl}/api/{ApiVersion}/pools/{PoolAddress}/ohlcv/{range}";
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Dispose();
        _lock?.Dispose();
    }
}

/// <summary>
/// Real NADD/SOL market data from GeckoTerminal (separate from internal Joseph Coins).
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

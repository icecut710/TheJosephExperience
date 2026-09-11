namespace JosephExperience.Services;

public enum MarketBias { Neutral, Positive, Negative }

/// <summary>Optional context influencing contextual selection.</summary>
public class LoreContext
{
    public string GameId { get; set; } = "";
    public string EventType { get; set; } = "";
    public int Count { get; set; } = 1;
    public bool Success { get; set; } = false;
    public bool Failure { get; set; } = false;
    public bool Reconnect { get; set; } = false;
    public bool LongSession { get; set; } = false;
    public MarketBias Market { get; set; } = MarketBias.Neutral;
}
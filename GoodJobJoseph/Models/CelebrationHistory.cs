namespace JosephExperience.Models;

public class CelebrationHistory
{
    public long Id { get; set; }
    public string? ImageId { get; set; }
    public string? TriggerType { get; set; }
    public string ShownAt { get; set; } = DateTime.UtcNow.ToString("o");
    public int DurationMs { get; set; }
}

/// <summary>
/// A time-bucketed count of celebrations for chart rendering.
/// </summary>
public class TimeBucket
{
    public DateTime Timestamp { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Time ranges for celebration activity charts.
/// </summary>
public enum HistoryRange
{
    Hours24,
    Days7,
    Days30,
    Days90,
    All
}

public class LibraryStats
{
    public int TotalCelebrations { get; set; }
    public string? MostCelebratedJoseph { get; set; }
    public DateTime? LastCelebration { get; set; }
    public int ImageCount { get; set; }
    public int EnabledImageCount { get; set; }
    public int SoundsPlayed { get; set; }
    public double? NaddPrice { get; set; }
    public int CelebrationsToday { get; set; }
}

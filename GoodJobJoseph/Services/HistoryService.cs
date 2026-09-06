using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using JosephExperience.Models;

namespace JosephExperience.Services;

/// <summary>
/// Tracks recent celebration history entries for the in-app history log.
/// Lives in-memory for the session; persists nothing to disk (no JSON config needed).
/// </summary>
public class HistoryService
{
    private readonly ConcurrentQueue<CelebrationHistory> _entries = new();
    private readonly int _maxLimit;

    public HistoryService(int maxLimit = 50)
    {
        _maxLimit = Math.Max(1, maxLimit);
    }

    public void AddEntry(string imageId, string displayName, string triggerType, bool is3DModel, int durationMs)
    {
        var entry = new CelebrationHistory
        {
            ImageId = imageId,
            TriggerType = triggerType,
            ShownAt = DateTime.UtcNow.ToString("o"),
            DurationMs = durationMs
        };

        _entries.Enqueue(entry);

        // Trim to maxLimit (keep most recent).
        while (_entries.Count > _maxLimit)
        {
            _entries.TryDequeue(out _);
        }
    }

    public IReadOnlyList<CelebrationHistory> GetRecent(int limit = 50)
    {
        var list = _entries.ToArray();
        // Queue is oldest→newest; reverse for newest-first display.
        Array.Reverse(list);
        return list.Take(limit).ToArray();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
    }
}
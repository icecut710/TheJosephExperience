using System.Text.Json;
using JosephExperience.Models.CounterStrike;
using JosephExperience.Utilities;

namespace JosephExperience.Services.CounterStrike;

public interface IGsiPayloadParser
{
    /// <summary>Parses a raw GSI JSON body into a snapshot, or null when the payload is unusable.</summary>
    GsiSnapshot? Parse(string json);
}

/// <summary>Safe, defensive parser: malformed or partial payloads never throw into the server loop.</summary>
public sealed class GsiPayloadParser : IGsiPayloadParser
{
    public GsiSnapshot? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return GsiSnapshot.FromJson(doc.RootElement);
        }
        catch (JsonException ex)
        {
            AppLog.Warn($"GsiPayloadParser: malformed JSON rejected ({ex.Message}).");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"GsiPayloadParser: unexpected parse failure ({ex.Message}).");
            return null;
        }
    }
}
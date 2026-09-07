using JosephExperience.Models.CounterStrike;

namespace JosephExperience.Services.HalfLife2;

/// <summary>
/// Normalized contract for per-game integration providers.
/// The UI (Kilo) consumes this; each provider implements what it can
/// reliably determine from external, non-invasive sources only.
/// </summary>
public interface IGameIntegrationProvider
{
    string GameId { get; }
    string DisplayName { get; }
    string[] ExecutableNames { get; }
    bool IsEnabled { get; set; }
    string State { get; }
    string Mode { get; } // e.g. "Campaign", "Multiplayer", "NotApplicable"
    DateTime? LastEvent { get; }
    DateTime? LastTelemetryAt { get; }
    bool CanTest { get; }
    bool CanRepair { get; }
    string? LogPath { get; }

    /// <summary>Async start detection (process watch, log polling, etc.).</summary>
    ValueTask StartAsync();

    /// <summary>Stop detection and clean up.</summary>
    ValueTask StopAsync();

    /// <summary>Query the provider for its current capabilities flag bitmap.</summary>
   Capabilities GetCapabilities();

    /// <summary>Inject a test event that routes through the real pipeline.</summary>
    /// <returns>true if the event was accepted by the router.</returns>
    bool TriggerTestEvent();

    /// <summary>Raised when the provider's state changes.</summary>
    event Action<string>? StateChanged;

    /// <summary>
    /// Callback to route a game event through the real CelebrationService pipeline.
    /// Set by the host app (App.xaml.cs) so providers can fire live events (kills, deaths, etc.)
    /// that display the Joseph overlay. Never invoked on the UI thread.
    /// </summary>
    Action<CelebrationGameEvent>? EventRouter { get; set; }
}

/// <summary>Capability flags describing what a provider can reliably determine.</summary>
[Flags]
public enum Capabilities
{
    SupportsProcessDetection = 1,
    SupportsModeDetection = 2,
    SupportsLiveTelemetry = 4,
    SupportsKills = 8,
    SupportsDeaths = 16,
    SupportsLevelChanges = 32,
    SupportsObjectives = 64,
    SupportsTest = 128
}
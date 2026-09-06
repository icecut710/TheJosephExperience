namespace JosephExperience.Utilities;

/// <summary>
/// Static logging facade used across services. Backed by the app's rolling
/// file logger once initialized on startup; safe to call (no-op) otherwise.
/// This keeps services decoupled from the WPF Application type.
/// </summary>
public static class AppLog
{
    private static volatile RollingFileLogger? _logger;

    public static void Initialize(RollingFileLogger logger) => _logger = logger;

    public static void Info(string message) => _logger?.Info(message);
    public static void Warn(string message) => _logger?.Warn(message);
    public static void Error(string message, Exception? ex = null) => _logger?.Error(message, ex);
}

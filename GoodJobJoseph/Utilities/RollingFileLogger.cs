using System.IO;
using System.Text;

namespace JosephExperience.Utilities;

public sealed class RollingFileLogger : IDisposable
{
    private readonly object _lock = new();
    private readonly string _logsDir;
    private string _currentFile = string.Empty;
    private StreamWriter? _writer;
    private const long MaxFileSizeBytes = 1_000_000;
    private const int MaxLogFiles = 5;

    public RollingFileLogger(string logsDir)
    {
        _logsDir = logsDir;
        Directory.CreateDirectory(logsDir);
    }

    public void Log(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                EnsureWriter();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                _writer?.WriteLine(line);
                _writer?.Flush();
                if (_writer?.BaseStream.Length > MaxFileSizeBytes)
                {
                    Roll();
                }
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    public void Info(string message) => Log("INFO", message);
    public void Warn(string message) => Log("WARN", message);
    public void Error(string message, Exception? ex = null)
    {
        Log("ERROR", ex is null ? message : $"{message}\n{ex}");
    }

    private void EnsureWriter()
    {
        var fileName = $"goodjob_{DateTime.Now:yyyyMMdd}.log";
        if (fileName == _currentFile && _writer is not null)
        {
            return;
        }
        _writer?.Dispose();
        _currentFile = fileName;
        var path = Path.Combine(_logsDir, fileName);
        _writer = new StreamWriter(path, append: true, Encoding.UTF8);
    }

    private void Roll()
    {
        _writer?.Dispose();
        _writer = null;
        try
        {
            var files = Directory.GetFiles(_logsDir, "goodjob_*.log")
                .OrderByDescending(f => f)
                .ToList();
            foreach (var file in files.Skip(Math.Max(0, files.Count - MaxLogFiles)))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
        catch
        {
            // Best effort.
        }
        EnsureWriter();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

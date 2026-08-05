namespace Mica.Core.Integration;

/// <summary>
/// Append-only launch log written next to the app's configuration. It exists so
/// a crash before any UI can still be diagnosed after the fact.
/// </summary>
public static class StartupLog
{
    private static readonly object Lock = new();
    private static string? _path;

    /// <summary>Points the log at a file and starts a fresh session block.</summary>
    public static void Initialize(string path)
    {
        _path = path;
        Write($"--- session start ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ---");
    }

    public static void Write(string message)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            lock (Lock)
            {
                File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public static void WriteException(string context, Exception ex) =>
        Write($"{context} EXCEPTION: {ex}");
}

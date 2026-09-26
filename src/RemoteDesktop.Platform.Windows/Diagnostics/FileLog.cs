using System.Text;

namespace RemoteDesktop.Platform.Windows.Diagnostics;

/// <summary>
/// Minimal thread-safe file logger for the desktop apps. Diagnostics only — it records
/// lifecycle events and errors (never screen content or keystrokes) so failures on background
/// threads can be understood without a debugger. Writing never throws.
/// </summary>
public static class FileLog
{
    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>Point the log at a file (created if missing). Safe to call once at startup.</summary>
    public static void Init(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            lock (Gate) _path = path;
            Write("INFO", $"--- log started ({DateTimeOffset.Now:o}) ---");
        }
        catch { /* logging must never break the app */ }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            string line = $"{DateTimeOffset.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                if (_path is null) return;
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch { /* swallow */ }
    }
}

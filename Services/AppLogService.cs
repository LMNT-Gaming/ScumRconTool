using System.Text;

namespace ScumRconTool.Services;

public static class AppLogService
{
    private const long MaxCurrentLogBytes = 25L * 1024 * 1024;
    private const long MaxLogDirectoryBytes = 100L * 1024 * 1024;
    private static readonly object Sync = new();
    private static DateTime _lastCleanupUtc = DateTime.MinValue;

    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");
    public static string CurrentLogFilePath => Path.Combine(LogDirectory, $"scum-rcon-tool-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
        WriteRawLine(line);
    }

    public static void WriteException(string source, Exception exception)
    {
        Write($"ERROR [{source}] {exception.GetType().Name}: {exception.Message}");
        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            WriteRawLine(exception.StackTrace);
        }
    }

    public static void ClearCurrentFile()
    {
        lock (Sync)
        {
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(CurrentLogFilePath, string.Empty, Encoding.UTF8);
            CleanupOldLogs(force: true);
        }
    }

    private static void CleanupOldLogs(bool force = false)
    {
        var nowUtc = DateTime.UtcNow;
        if (!force && nowUtc - _lastCleanupUtc < TimeSpan.FromMinutes(5)) return;
        _lastCleanupUtc = nowUtc;
        LocalRetentionService.CleanupDirectory(LogDirectory, LocalRetentionService.DefaultRetentionDays, MaxLogDirectoryBytes);
    }

    private static void RotateCurrentLogIfNeeded()
    {
        try
        {
            if (!File.Exists(CurrentLogFilePath)) return;
            var info = new FileInfo(CurrentLogFilePath);
            if (info.Length < MaxCurrentLogBytes) return;

            var archive = Path.Combine(LogDirectory, $"scum-rcon-tool-{DateTime.Now:yyyy-MM-dd-HHmmss}.log");
            File.Move(CurrentLogFilePath, archive, true);
        }
        catch
        {
            // Logging must never crash the application.
        }
    }

    private static void WriteRawLine(string line)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(LogDirectory);
            RotateCurrentLogIfNeeded();
            File.AppendAllText(CurrentLogFilePath, line + Environment.NewLine, Encoding.UTF8);
            CleanupOldLogs();
        }
    }
}

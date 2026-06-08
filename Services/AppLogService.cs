using System.Text;

namespace ScumRconTool.Services;

public static class AppLogService
{
    private static readonly object Sync = new();

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
        }
    }

    private static void WriteRawLine(string line)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(CurrentLogFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}

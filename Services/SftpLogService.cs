using System.Text.RegularExpressions;
using Renci.SshNet;
using System.IO;
using System.Threading;

namespace ScumRconTool.Services;

public sealed class SftpLogService
{
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);
    private readonly BotSettings _settings;

    public SftpLogService(BotSettings settings)
    {
        _settings = settings;
    }

    public async Task<string?> DownloadLatestLogAsync(string pattern, string localSubDirectory, CancellationToken cancellationToken = default, string? remoteDirectoryOverride = null)
    {
        await DownloadLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run<string?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (host, port) = ParseHostAndPort(_settings.FtpHost, _settings.FtpPort);
                if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("SFTP-Host fehlt.");
                if (string.IsNullOrWhiteSpace(_settings.FtpUser)) throw new InvalidOperationException("SFTP-Benutzer fehlt.");

                var remoteDirectory = ResolveRemoteDirectory(_settings.FtpRemoteDirectory, remoteDirectoryOverride);
                var localDirectory = GetLocalDirectory(_settings, localSubDirectory);
                Directory.CreateDirectory(localDirectory);
                LocalRetentionService.CleanupDirectory(localDirectory);

                using var client = new SftpClient(host, port, _settings.FtpUser, _settings.FtpPassword ?? string.Empty);
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
                client.OperationTimeout = TimeSpan.FromSeconds(30);
                client.Connect();

                cancellationToken.ThrowIfCancellationRequested();
                var latest = client.ListDirectory(remoteDirectory)
                    .Where(f => !f.IsDirectory && !f.IsSymbolicLink)
                    .Where(f => MatchesAnyPattern(f.Name, pattern))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (latest is null)
                {
                    client.Disconnect();
                    return null;
                }

                var local = Path.Combine(localDirectory, latest.Name);
                var temp = Path.Combine(localDirectory, latest.Name + "." + Guid.NewGuid().ToString("N") + ".tmp");

                try
                {
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        client.DownloadFile(latest.FullName, output);
                    }

                    File.Move(temp, local, true);
                    LocalRetentionService.CleanupDirectory(localDirectory);
                }
                finally
                {
                    if (File.Exists(temp))
                    {
                        try { File.Delete(temp); } catch { }
                    }

                    client.Disconnect();
                }

                return local;
            }, cancellationToken);
        }
        finally
        {
            DownloadLock.Release();
        }
    }


    public async Task<string?> DownloadFileAsync(string remoteFilePath, string localSubDirectory, CancellationToken cancellationToken = default)
    {
        await DownloadLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run<string?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (host, port) = ParseHostAndPort(_settings.FtpHost, _settings.FtpPort);
                if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("SFTP-Host fehlt.");
                if (string.IsNullOrWhiteSpace(_settings.FtpUser)) throw new InvalidOperationException("SFTP-Benutzer fehlt.");

                var remoteFile = NormalizeRemoteFilePath(remoteFilePath);
                var fileName = Path.GetFileName(remoteFile.Replace('\\', '/'));
                if (string.IsNullOrWhiteSpace(fileName)) throw new InvalidOperationException("SFTP-Dateiname fehlt.");

                // Exakte Datei-Downloads (z. B. SCUM.db fuer Weekly Tasks) werden bewusst
                // nicht unter dem KillLogs-Ordner gespeichert. FtpLocalDirectory kann bei manchen
                // Setups auf einen Log-Unterordner zeigen und dort gibt es haeufig Lock-/Rechteprobleme.
                var localDirectory = GetAppDataLocalDirectory(localSubDirectory);
                Directory.CreateDirectory(localDirectory);
                LocalRetentionService.CleanupDirectory(localDirectory);

                using var client = new SftpClient(host, port, _settings.FtpUser, _settings.FtpPassword ?? string.Empty);
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
                client.OperationTimeout = TimeSpan.FromSeconds(30);
                client.Connect();

                cancellationToken.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(fileName);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrWhiteSpace(nameWithoutExtension)) nameWithoutExtension = "download";

                // Nicht immer wieder SCUM.db ueberschreiben: die Datei kann vom vorherigen SQLite-Read
                // oder von Windows/AV noch kurz gelockt sein. Jeder Download bekommt deshalb lokal
                // einen eindeutigen Dateinamen.
                var uniqueFileName = $"{nameWithoutExtension}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}";
                var local = Path.Combine(localDirectory, uniqueFileName);
                var temp = local + ".tmp";

                try
                {
                    // Wichtig: Kein client.Exists(remoteFile) davor. Einige SFTP-Server/Hoster
                    // liefern fuer Exists/Stat ein Access denied, erlauben aber den direkten Datei-Download.
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        client.DownloadFile(remoteFile, output);
                    }

                    File.Move(temp, local, true);
                    LocalRetentionService.CleanupDirectory(localDirectory);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"SFTP-Dateidownload fehlgeschlagen. Remote='{remoteFile}', Lokal='{local}', Temp='{temp}'. Ursache: {ex.Message}",
                        ex);
                }
                finally
                {
                    if (File.Exists(temp))
                    {
                        try { File.Delete(temp); } catch { }
                    }

                    client.Disconnect();
                }

                return local;
            }, cancellationToken);
        }
        finally
        {
            DownloadLock.Release();
        }
    }

    public static string GetAppDataLocalDirectory(string localSubDirectory)
    {
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool");
        return Path.Combine(localRoot, localSubDirectory);
    }

    public static string GetLocalDirectory(BotSettings settings, string localSubDirectory)
    {
        var localRoot = string.IsNullOrWhiteSpace(settings.FtpLocalDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "Logs")
            : settings.FtpLocalDirectory;
        return Path.Combine(localRoot, localSubDirectory);
    }

    public static string EnsureLocalDirectory(BotSettings settings, string localSubDirectory)
    {
        var directory = GetLocalDirectory(settings, localSubDirectory);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static bool MatchesAnyPattern(string fileName, string patterns)
    {
        foreach (var pattern in patterns.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            if (Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        }

        return false;
    }


    private static string ResolveRemoteDirectory(string baseDirectory, string? overrideDirectory)
    {
        if (string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return NormalizeRemoteDirectory(baseDirectory);
        }

        var directory = overrideDirectory.Trim();
        if (directory.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(directory);
            directory = uri.AbsolutePath;
        }

        if (!directory.StartsWith('/'))
        {
            var baseDir = NormalizeRemoteDirectory(baseDirectory).TrimEnd('/');
            directory = baseDir + "/" + directory;
        }

        return CollapseRemoteDirectory(directory);
    }

    private static string CollapseRemoteDirectory(string directory)
    {
        var parts = new List<string>();
        foreach (var rawPart in directory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawPart == ".") continue;
            if (rawPart == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(rawPart);
        }

        return "/" + string.Join('/', parts);
    }


    private static string NormalizeRemoteFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new InvalidOperationException("SFTP-Dateipfad fehlt.");
        var path = filePath.Trim().Replace('\\', '/');
        if (path.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(path);
            path = uri.AbsolutePath;
        }
        return path.StartsWith('/') ? path : "/" + path;
    }

    private static string NormalizeRemoteDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return "/";
        directory = directory.Trim();
        if (directory.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(directory);
            directory = uri.AbsolutePath;
        }
        return directory.StartsWith('/') ? directory : "/" + directory;
    }

    private static (string Host, int Port) ParseHostAndPort(string hostValue, int portValue)
    {
        var host = (hostValue ?? string.Empty).Trim();
        var port = portValue <= 0 ? 22 : portValue;

        if (host.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(host);
            host = uri.Host;
            if (!uri.IsDefaultPort) port = uri.Port;
            return (host, port);
        }

        host = host.TrimEnd('/');
        var colon = host.LastIndexOf(':');
        if (colon > 0 && colon < host.Length - 1 && host.Count(c => c == ':') == 1 && int.TryParse(host[(colon + 1)..], out var parsedPort))
        {
            port = parsedPort;
            host = host[..colon];
        }

        return (host, port);
    }
}

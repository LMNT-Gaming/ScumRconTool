using System.Text.RegularExpressions;
using Renci.SshNet;

namespace ScumRconTool.Services;

public sealed class SftpKillLogService
{
    private readonly BotSettings _settings;

    public SftpKillLogService(BotSettings settings)
    {
        _settings = settings;
    }

    public Task<IReadOnlyList<string>> DownloadKillLogsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (host, port) = ParseHostAndPort(_settings.FtpHost, _settings.FtpPort);
            if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("SFTP-Host fehlt.");
            if (string.IsNullOrWhiteSpace(_settings.FtpUser)) throw new InvalidOperationException("SFTP-Benutzer fehlt.");
            if (string.IsNullOrWhiteSpace(_settings.FtpRemoteDirectory)) _settings.FtpRemoteDirectory = "/";
            if (string.IsNullOrWhiteSpace(_settings.FtpLocalDirectory))
            {
                _settings.FtpLocalDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "KillLogs");
            }

            Directory.CreateDirectory(_settings.FtpLocalDirectory);
            var downloaded = new List<string>();
            var remoteDirectory = NormalizeRemoteDirectory(_settings.FtpRemoteDirectory);
            var pattern = string.IsNullOrWhiteSpace(_settings.FtpKillLogPattern) ? "kill*.log" : _settings.FtpKillLogPattern.Trim();

            using var client = new SftpClient(host, port, _settings.FtpUser, _settings.FtpPassword ?? string.Empty);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
            client.OperationTimeout = TimeSpan.FromSeconds(30);
            client.Connect();

            cancellationToken.ThrowIfCancellationRequested();
            var killLogs = client.ListDirectory(remoteDirectory)
                .Where(f => !f.IsDirectory && !f.IsSymbolicLink)
                .Where(f => MatchesAnyPattern(f.Name, pattern))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var remoteFile in killLogs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var local = Path.Combine(_settings.FtpLocalDirectory, remoteFile.Name);
                using var output = File.Create(local);
                client.DownloadFile(remoteFile.FullName, output);
                downloaded.Add(local);
            }

            client.Disconnect();
            return downloaded;
        }, cancellationToken);
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

using System.Net;
using System.Text.RegularExpressions;

namespace ScumRconTool.Services;

public sealed class FtpKillLogService
{
    private readonly BotSettings _settings;

    public FtpKillLogService(BotSettings settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<string>> DownloadKillLogsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.FtpHost)) throw new InvalidOperationException("FTP-Host fehlt.");
        if (string.IsNullOrWhiteSpace(_settings.FtpUser)) throw new InvalidOperationException("FTP-Benutzer fehlt.");
        if (string.IsNullOrWhiteSpace(_settings.FtpRemoteDirectory)) _settings.FtpRemoteDirectory = "/";
        if (string.IsNullOrWhiteSpace(_settings.FtpLocalDirectory))
        {
            _settings.FtpLocalDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "KillLogs");
        }

        Directory.CreateDirectory(_settings.FtpLocalDirectory);
        var files = await ListFilesAsync(cancellationToken);
        var pattern = string.IsNullOrWhiteSpace(_settings.FtpKillLogPattern) ? "kill*.log" : _settings.FtpKillLogPattern.Trim();
        var killLogs = files
            .Where(f => MatchesAnyPattern(Path.GetFileName(f), pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var downloaded = new List<string>();
        foreach (var file in killLogs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var local = Path.Combine(_settings.FtpLocalDirectory, Path.GetFileName(file));
            await DownloadFileAsync(file, local, cancellationToken);
            downloaded.Add(local);
        }

        return downloaded;
    }

    private async Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken)
    {
        var request = CreateRequest(_settings.FtpRemoteDirectory, WebRequestMethods.Ftp.ListDirectory);
        using var response = (FtpWebResponse)await request.GetResponseAsync().WaitAsync(cancellationToken);
        await using var stream = response.GetResponseStream();
        if (stream is null) return Array.Empty<string>();
        using var reader = new StreamReader(stream);
        var result = new List<string>();
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line)) result.Add(line.Trim());
        }
        return result;
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

    private async Task DownloadFileAsync(string remoteName, string localPath, CancellationToken cancellationToken)
    {
        var request = CreateRequest(CombineRemote(_settings.FtpRemoteDirectory, remoteName), WebRequestMethods.Ftp.DownloadFile);
        using var response = (FtpWebResponse)await request.GetResponseAsync().WaitAsync(cancellationToken);
        await using var input = response.GetResponseStream();
        if (input is null) return;
        await using var output = File.Create(localPath);
        await input.CopyToAsync(output, cancellationToken);
    }

    private FtpWebRequest CreateRequest(string remotePath, string method)
    {
        var uri = BuildUri(remotePath);
#pragma warning disable SYSLIB0014
        var request = (FtpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
        request.Method = method;
        request.Credentials = new NetworkCredential(_settings.FtpUser, _settings.FtpPassword);
        request.EnableSsl = _settings.FtpUseSsl;
        request.UseBinary = true;
        request.KeepAlive = false;
        request.Timeout = 30000;
        request.ReadWriteTimeout = 30000;
        return request;
    }

    private Uri BuildUri(string remotePath)
    {
        var host = _settings.FtpHost.Trim();
        if (host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)) host = host[6..];
        if (host.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase)) host = host[7..];
        host = host.TrimEnd('/');
        var path = remotePath.StartsWith('/') ? remotePath : "/" + remotePath;
        return new Uri($"ftp://{host}:{_settings.FtpPort}{path}");
    }

    private static string CombineRemote(string directory, string file)
    {
        if (file.StartsWith('/')) return file;
        if (string.IsNullOrWhiteSpace(directory)) directory = "/";
        return directory.TrimEnd('/') + "/" + file.TrimStart('/');
    }
}

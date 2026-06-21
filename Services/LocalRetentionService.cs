namespace ScumRconTool.Services;

public static class LocalRetentionService
{
    public const int DefaultRetentionDays = 5;

    public static void CleanupDirectory(string? directory, int retentionDays = DefaultRetentionDays, long? maxTotalBytes = null)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

        var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));

        foreach (var file in SafeEnumerateFiles(directory))
        {
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) continue;

                var lastWriteUtc = info.LastWriteTimeUtc == DateTime.MinValue ? info.CreationTimeUtc : info.LastWriteTimeUtc;
                if (lastWriteUtc < cutoffUtc)
                {
                    info.IsReadOnly = false;
                    info.Delete();
                }
            }
            catch
            {
                // Cleanup must never break the running tool.
            }
        }

        if (maxTotalBytes is > 0)
        {
            EnforceTotalSizeLimit(directory, maxTotalBytes.Value);
        }

        DeleteEmptyDirectories(directory);
    }

    public static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (!File.Exists(path)) return;
            var info = new FileInfo(path) { IsReadOnly = false };
            info.Delete();
        }
        catch
        {
            // Best effort only.
        }
    }


    public static void TryDeleteFiles(string? directory, string searchPattern)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        if (string.IsNullOrWhiteSpace(searchPattern)) searchPattern = "*";

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly).ToList())
            {
                TryDeleteFile(file);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static void EnforceTotalSizeLimit(string directory, long maxTotalBytes)
    {
        try
        {
            var files = SafeEnumerateFiles(directory)
                .Select(path =>
                {
                    try { return new FileInfo(path); }
                    catch { return null; }
                })
                .Where(info => info is { Exists: true })
                .OrderBy(info => info!.LastWriteTimeUtc)
                .ToList();

            var totalBytes = files.Sum(info => info!.Length);
            foreach (var file in files)
            {
                if (totalBytes <= maxTotalBytes) break;
                try
                {
                    totalBytes -= file!.Length;
                    file.IsReadOnly = false;
                    file.Delete();
                }
                catch
                {
                    // Ignore locked files.
                }
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void DeleteEmptyDirectories(string rootDirectory)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
                .OrderByDescending(x => x.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                catch
                {
                    // Ignore locked directories.
                }
            }
        }
        catch
        {
            // Best effort only.
        }
    }
}

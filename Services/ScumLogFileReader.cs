using System.Text;

namespace ScumRconTool.Services;

public static class ScumLogFileReader
{
    public static IReadOnlyList<string> ReadNewLines(string path, FileReadPositionStore positions, string? positionKey = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Array.Empty<string>();

        var key = string.IsNullOrWhiteSpace(positionKey) ? Path.GetFileName(path) : positionKey;
        var length = new FileInfo(path).Length;
        var start = positions.GetPosition(key);
        if (start < 0 || start > length) start = 0;
        if (start == length) return Array.Empty<string>();

        byte[] bytes;
        using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            fs.Position = start;
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            bytes = ms.ToArray();
        }

        positions.SetPosition(key, length);
        positions.Save();

        var text = DecodeScumLog(bytes);
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static IReadOnlyList<string> ReadAllLines(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Array.Empty<string>();
        return DecodeScumLog(File.ReadAllBytes(path))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string DecodeScumLog(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var sampleLength = Math.Min(bytes.Length, 400);
        var evenZeros = 0;
        var oddZeros = 0;
        for (var i = 0; i < sampleLength; i++)
        {
            if (bytes[i] != 0) continue;
            if ((i & 1) == 0) evenZeros++;
            else oddZeros++;
        }

        if (oddZeros > sampleLength / 5) return Encoding.Unicode.GetString(bytes);
        if (evenZeros > sampleLength / 5) return Encoding.BigEndianUnicode.GetString(bytes);

        return new UTF8Encoding(false, true).GetString(bytes);
    }
}

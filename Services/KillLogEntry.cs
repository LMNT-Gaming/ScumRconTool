namespace ScumRconTool.Services;

public sealed class KillLogEntry
{
    public DateTime? Timestamp { get; set; }
    public string Killer { get; set; } = "";
    public string Victim { get; set; } = "";
    public string Weapon { get; set; } = "";
    public string Distance { get; set; } = "";
    public string RawLine { get; set; } = "";
    public string SourceFile { get; set; } = "";

    public string Key => $"{SourceFile}|{RawLine}";

    public string ToAnnounceText(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "{killer} killed {victim} {weapon} {distance}";
        }

        var text = template
            .Replace("{time}", Timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{killer}", Killer, StringComparison.OrdinalIgnoreCase)
            .Replace("{victim}", Victim, StringComparison.OrdinalIgnoreCase)
            .Replace("{weapon}", Weapon, StringComparison.OrdinalIgnoreCase)
            .Replace("{distance}", Distance, StringComparison.OrdinalIgnoreCase)
            .Replace("{raw}", RawLine, StringComparison.OrdinalIgnoreCase);

        return string.Join(' ', text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}

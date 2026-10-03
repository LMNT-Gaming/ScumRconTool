using System.Text;
using System.Text.RegularExpressions;

namespace ScumRconTool.Services;

public sealed record ItemCatalogEntry(int Id, string Code, string ImageUrl, string Category, int? Tier, bool IsSpawnable)
{
    public string DisplayName => Code.Replace('_', ' ');
    public string TierLabel => Tier is null ? "Tier –" : $"Tier {Tier}";
}

public sealed class ItemCatalogService
{
    private const string DumpFileName = "dbs14756250.sql";
    private static IReadOnlyList<ItemCatalogEntry>? _cache;

    public IReadOnlyList<ItemCatalogEntry> Load() => _cache ??= LoadCore();

    private static IReadOnlyList<ItemCatalogEntry> LoadCore()
    {
        var path = FindDump();
        if (path is null)
            throw new FileNotFoundException($"Der Item-Katalog '{DumpFileName}' wurde nicht gefunden. Bitte die Datei neben das Programm legen.");
        var sql = File.ReadAllText(path, Encoding.UTF8);
        var tagNames = ReadRows(sql, "tags").Where(row => row.Count >= 4 && ParseInt(row[0]) is not null)
            .ToDictionary(row => ParseInt(row[0])!.Value, row => row[2] ?? "Sonstiges");
        var noSpawnTagId = tagNames.FirstOrDefault(x => string.Equals(x.Value, "No Spawn", StringComparison.OrdinalIgnoreCase)).Key;
        var noSpawnItemIds = noSpawnTagId == 0 ? new HashSet<int>() : ReadRows(sql, "item_tags")
            .Where(row => row.Count >= 2 && ParseInt(row[1]) == noSpawnTagId).Select(row => ParseInt(row[0]))
            .Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();

        var items = ReadRows(sql, "items")
            .Where(row => row.Count >= 6 && ParseInt(row[0]) is not null && !string.IsNullOrWhiteSpace(row[1]))
            .Select(row =>
            {
                var id = ParseInt(row[0])!.Value;
                var code = row[1]!.Trim();
                var tagId = ParseInt(row[3]);
                var category = tagId is not null && tagNames.TryGetValue(tagId.Value, out var name) ? name : "Sonstiges";
                var imageFile = string.IsNullOrWhiteSpace(row[2]) ? code + ".png" : row[2]!.Trim();
                return new ItemCatalogEntry(id, code,
                    "https://www.lmnt-gaming.net/images/items/" + Uri.EscapeDataString(imageFile),
                    category, ParseInt(row[4]), tagId != noSpawnTagId && !noSpawnItemIds.Contains(id));
            }).ToList();

        // These valid trader spawn codes exist in the source dump's imported item list,
        // but not in its curated `items` table used by the picker.
        AddKnownSpawnable(items, -1, "BaseExpansionKit_Lvl1", "Base Building");
        AddKnownSpawnable(items, -2, "BaseExpansionKit_Lvl2", "Base Building");

        return items
            .OrderBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddKnownSpawnable(List<ItemCatalogEntry> items, int id, string code, string category)
    {
        if (items.Any(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase))) return;
        items.Add(new ItemCatalogEntry(id, code,
            "https://www.lmnt-gaming.net/images/items/" + Uri.EscapeDataString(code + ".png"),
            category, null, true));
    }

    private static string? FindDump()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; directory is not null && i < 7; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, DumpFileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static List<List<string?>> ReadRows(string sql, string table)
    {
        var result = new List<List<string?>>();
        var pattern = $@"INSERT\s+INTO\s+`{Regex.Escape(table)}`\s*\([^;]+?\)\s*VALUES\s*(?<rows>.*?);";
        foreach (Match match in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            ParseTuples(match.Groups["rows"].Value, result);
        return result;
    }

    private static void ParseTuples(string input, ICollection<List<string?>> target)
    {
        var index = 0;
        while (index < input.Length)
        {
            while (index < input.Length && input[index] != '(') index++;
            if (index >= input.Length) break;
            index++;
            var row = new List<string?>();
            var value = new StringBuilder();
            var quoted = false;
            var wasQuoted = false;
            while (index < input.Length)
            {
                var ch = input[index++];
                if (quoted)
                {
                    if (ch == '\\' && index < input.Length)
                    {
                        var escaped = input[index++];
                        value.Append(escaped switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escaped });
                    }
                    else if (ch == '\'' && index < input.Length && input[index] == '\'') { value.Append('\''); index++; }
                    else if (ch == '\'') quoted = false;
                    else value.Append(ch);
                    continue;
                }
                if (ch == '\'') { quoted = true; wasQuoted = true; }
                else if (ch is ',' or ')')
                {
                    var raw = value.ToString().Trim();
                    row.Add(!wasQuoted && string.Equals(raw, "NULL", StringComparison.OrdinalIgnoreCase) ? null : raw);
                    value.Clear(); wasQuoted = false;
                    if (ch == ')') break;
                }
                else value.Append(ch);
            }
            if (row.Count > 0) target.Add(row);
        }
    }

    private static int? ParseInt(string? value) => int.TryParse(value, out var parsed) ? parsed : null;
}

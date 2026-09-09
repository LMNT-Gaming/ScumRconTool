using System.Text.Json;
using System.Text.RegularExpressions;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public static class PlayerParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static List<ScumPlayer> ParseListPlayersJson(string json)
    {
        return TryParsePlayerResponse(json, out var response)
            ? response.Players
            : new List<ScumPlayer>();
    }

    public static int ParsePlayerCount(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return 0;

        if (TryParsePlayerResponse(responseText, out var response))
            return response.Players.Count > 0 ? response.Players.Count : Math.Max(0, response.Count);

        var matches = Regex.Matches(responseText, @"(?im)^s*(?:d+[).:-]|SteamID|Steam Name|Character Name|Name:)");
        if (matches.Count > 0) return matches.Count;

        var countMatch = Regex.Match(responseText, @"(?i)(?:players?|spieler|count)D+(d+)");
        return countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out var count) ? count : 0;
    }

    private static bool TryParsePlayerResponse(string? source, out PlayerListResponse response)
    {
        response = new PlayerListResponse();
        if (string.IsNullOrWhiteSpace(source)) return false;

        foreach (var jsonObject in ExtractJsonObjects(source))
        {
            try
            {
                using var document = JsonDocument.Parse(jsonObject);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.EnumerateObject().Any(property =>
                        property.Name.Equals("players", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var parsed = JsonSerializer.Deserialize<PlayerListResponse>(jsonObject, Options);
                if (parsed is null) continue;
                parsed.Players ??= new List<ScumPlayer>();
                response = parsed;
                return true;
            }
            catch (JsonException)
            {
                // ggCON can append another RCON response. Continue with the next complete JSON object.
            }
        }

        return false;
    }

    private static IEnumerable<string> ExtractJsonObjects(string source)
    {
        for (var start = 0; start < source.Length; start++)
        {
            if (source[start] != '{') continue;

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = start; index < source.Length; index++)
            {
                var current = source[index];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (current == (char)92) escaped = true;
                    else if (current == '"') inString = false;
                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '{') depth++;
                else if (current == '}' && --depth == 0)
                {
                    yield return source.Substring(start, index - start + 1);
                    start = index;
                    break;
                }
            }
        }
    }
}

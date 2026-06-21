using System;
using System.Collections.Generic;
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
        if (string.IsNullOrWhiteSpace(json)) return new List<ScumPlayer>();

        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            json = json.Substring(start, end - start + 1);
        }

        var response = JsonSerializer.Deserialize<PlayerListResponse>(json, Options);
        return response?.Players ?? new List<ScumPlayer>();
    }
    public static int ParsePlayerCount(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return 0;

        try
        {
            var players = ParseListPlayersJson(responseText);
            if (players.Count > 0) return players.Count;

            var start = responseText.IndexOf('{');
            var end = responseText.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var json = responseText.Substring(start, end - start + 1);
                var parsed = JsonSerializer.Deserialize<PlayerListResponse>(json, Options);
                if (parsed is not null && parsed.Count >= 0) return parsed.Count;
            }
        }
        catch
        {
            // Fall through to text parsing for plain #ListPlayers output or malformed wrappers.
        }

        var matches = Regex.Matches(responseText, @"(?im)^\s*(?:\d+[\).:-]|SteamID|Steam Name|Character Name|Name:)");
        if (matches.Count > 0) return matches.Count;

        var countMatch = Regex.Match(responseText, @"(?i)(?:players?|spieler|count)\D+(\d+)");
        return countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out var count) ? count : 0;
    }
}

using System;
using System.Text.Json.Serialization;

namespace ScumRconTool.Models;

public sealed class ScumPlayer
{
    [JsonPropertyName("characterName")]
    public string? CharacterName { get; set; }

    [JsonPropertyName("steamName")]
    public string? SteamName { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("fame")]
    public double Fame { get; set; }

    [JsonPropertyName("accountBalance")]
    public double AccountBalance { get; set; }

    [JsonPropertyName("goldBalance")]
    public double GoldBalance { get; set; }

    [JsonPropertyName("location")]
    public WorldPosition? Location { get; set; }

    [JsonPropertyName("health")]
    public double? Health { get; set; }

    [JsonPropertyName("ping")]
    public int? Ping { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(CharacterName) ? CharacterName! : SteamName ?? UserId ?? "Unbekannt";
}

public sealed class WorldPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    public double Distance2DTo(WorldPosition other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => $"X={X:0} Y={Y:0} Z={Z:0}";
}

public sealed class PlayerListResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("players")]
    public List<ScumPlayer> Players { get; set; } = new();
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace ScumRconTool.Services;

public static class CommandRegistry
{
    public static readonly string[] MessageTypes =
    {
        "Yellow", "White", "Cyan", "Green", "Red", "ServerMessage", "Error"
    };

    public static string ListPlayersJson() => "#ListPlayersJson";
    public static string ListPlayers() => "#ListPlayers";
    public static string Server() => "#Server";
    public static string Weather() => "#Weather";

    public static string Broadcast(string type, string text)
    {
        type = NormalizeMessageType(type);
        return $"#Broadcast {type} {CleanText(text)}";
    }

    public static string Announce(string type, string text)
    {
        type = NormalizeMessageType(type);
        return $"#announce {type} {CleanText(text)}";
    }

    public static string MessagePlayer(string steamId, string type, string text)
    {
        EnsureSteamId(steamId);
        type = NormalizeMessageType(type);
        return $"#MessagePlayer {steamId.Trim()} {type} {CleanText(text)}";
    }

    public static string GiveItem(string steamId, string itemName, int quantity)
    {
        EnsureSteamId(steamId);
        if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("Item fehlt.", nameof(itemName));
        if (quantity < 1) quantity = 1;
        return $"#GiveItem {steamId.Trim()} {itemName.Trim()} {quantity}";
    }

    public static string GiveMoney(string steamId, int amount)
    {
        EnsureSteamId(steamId);
        return $"#GiveMoney {steamId.Trim()} {amount}";
    }

    public static string SetFamePoints(string steamId, int amount)
    {
        EnsureSteamId(steamId);
        return $"#SetFamePoints {steamId.Trim()} {amount}";
    }

    public static string SpawnEntity(string steamId, string spawnVerb, string? entityName)
    {
        EnsureSteamId(steamId);
        if (string.IsNullOrWhiteSpace(spawnVerb)) throw new ArgumentException("Spawn-Typ fehlt.", nameof(spawnVerb));
        return string.IsNullOrWhiteSpace(entityName)
            ? $"#SpawnEntity {steamId.Trim()} {spawnVerb.Trim()}"
            : $"#SpawnEntity {steamId.Trim()} {spawnVerb.Trim()} {entityName.Trim()}";
    }

    public static string GiveVehicle(string steamId, string vehicleClass)
    {
        EnsureSteamId(steamId);
        if (string.IsNullOrWhiteSpace(vehicleClass)) throw new ArgumentException("Fahrzeugklasse fehlt.", nameof(vehicleClass));
        return $"#GiveVehicle {steamId.Trim()} {vehicleClass.Trim()}";
    }

    public static IReadOnlyList<string> ExpandTemplate(string template, IDictionary<string, string> values)
    {
        var result = template;
        foreach (var pair in values)
        {
            result = result.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }
        return new[] { result };
    }

    private static string NormalizeMessageType(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "Yellow";
        var match = MessageTypes.FirstOrDefault(x => x.Equals(type.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? "Yellow";
    }

    private static string CleanText(string text) => (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

    private static void EnsureSteamId(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId)) throw new ArgumentException("SteamID fehlt.", nameof(steamId));
    }
}

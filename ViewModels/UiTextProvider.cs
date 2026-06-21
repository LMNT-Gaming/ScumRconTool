namespace ScumRconTool.ViewModels;

public sealed class UiTextProvider : ObservableObject
{
    private readonly Dictionary<string, (string De, string En)> _texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SwitchLanguage"] = ("\U0001F1EC\U0001F1E7 English", "\U0001F1E9\U0001F1EA Deutsch"),
        ["CheckUpdate"] = ("Update pruefen", "Check update"),
        ["DownloadUpdate"] = ("Update herunterladen", "Download update"),
        ["OpenDownload"] = ("Download oeffnen", "Open download"),
        ["GgconDocs"] = ("ggCON Doku", "ggCON docs"),
        ["OpenFolder"] = ("Ordner", "Folder"),
        ["Clear"] = ("Leeren", "Clear"),
        ["Connect"] = ("Verbinden", "Connect"),
        ["Send"] = ("Senden", "Send"),
        ["StartDiscord"] = ("Discord starten", "Start Discord"),
        ["UpdateServerStatus"] = ("Serverstatus-Nachricht aktualisieren", "Update server status message"),
        ["StartChatLogAuto"] = ("Chatlog Auto starten", "Start chatlog auto"),
        ["StopChatLogAuto"] = ("Chatlog Auto stoppen", "Stop chatlog auto"),
        ["SendChatLogEmbedsNow"] = ("Chatlog jetzt als Embeds senden", "Send chatlog embeds now"),
        ["SaveSettings"] = ("Einstellungen speichern", "Save settings"),
        ["Start"] = ("Start", "Start"),
        ["Stop"] = ("Stop", "Stop"),
        ["ScanNow"] = ("Scan jetzt", "Scan now"),
        ["AddCommand"] = ("+ Befehl", "+ Command"),
        ["RemoveCommand"] = ("- Befehl", "- Command"),
        ["InsertExamples"] = ("Beispiele einfuegen", "Insert examples"),
        ["InsertExample"] = ("Beispiel einfuegen", "Insert example"),
        ["Save"] = ("Speichern", "Save"),
        ["DoNotSave"] = ("Nicht speichern", "Don't save"),
        ["Cancel"] = ("Abbrechen", "Cancel"),
        ["Delete"] = ("Loeschen", "Delete"),
        ["RemoveDelete"] = ("- Loeschen", "- Delete"),
        ["ExecuteForAllOnlinePlayers"] = ("Fuer alle Online-Spieler ausfuehren", "Run for all online players"),
        ["ScanDiscordNow"] = ("Scan + Discord jetzt", "Scan + Discord now"),
        ["ResetBaseline"] = ("Startwert neu setzen", "Reset baseline"),
        ["JsonResetExample"] = ("JSON Reset Beispiel", "JSON reset example"),
        ["NewChallenge"] = ("Neue Challenge", "New challenge"),
        ["ReloadExistingData"] = ("Aus vorhandenen Daten neu laden", "Reload from existing data"),
        ["SaveAllChallenges"] = ("Alle Challenges speichern", "Save all challenges"),
        ["Duplicate"] = ("Duplizieren", "Duplicate"),
        ["SendNow"] = ("Jetzt senden", "Send now"),
        ["ResetQueue"] = ("Queue auf Anfang", "Reset queue"),
        ["AddMessage"] = ("+ Nachricht", "+ Message"),
        ["Reload"] = ("Neu laden", "Reload"),
        ["CreateNew"] = ("+ Neu", "+ New"),
        ["Refresh"] = ("Aktualisieren", "Refresh"),
        ["ValidateJson"] = ("JSON validieren", "Validate JSON"),
        ["FormatJson"] = ("JSON formatieren", "Format JSON"),
        ["AddSpawn"] = ("+ Spawn", "+ Spawn"),
        ["RemoveSpawn"] = ("- Spawn", "- Spawn"),
        ["AddLootPack"] = ("+ LootPack", "+ Loot pack"),
        ["RemoveLootPack"] = ("- LootPack", "- Loot pack"),
        ["AddItem"] = ("+ Item", "+ Item"),
        ["RemoveItem"] = ("- Item", "- Item"),
        ["RemoveLootCommand"] = ("- Loot Command", "- Loot command"),
        ["AddLootCommand"] = ("+ Loot Cmd", "+ Loot Cmd"),
        ["AddLiveCommand"] = ("+ Live Cmd", "+ Live Cmd"),
        ["AddLootCoordinate"] = ("+ Loot Koordinate", "+ Loot coordinate"),
        ["AddNpcCoordinate"] = ("+ NPC Koordinate", "+ NPC coordinate"),
        ["Remove"] = ("- Entfernen", "- Remove"),
        ["RemovePack"] = ("- Pack", "- Pack"),
        ["AddNpcLootCommandPack"] = ("+ NPC/Loot CommandPack", "+ NPC/Loot command pack"),
        ["OpenLogFolder"] = ("Log-Ordner oeffnen", "Open log folder"),
        ["ClearLogDisplayAndFile"] = ("Anzeige + Datei leeren", "Clear view + file"),
    };

    private string _language = "de";

    public UiTextProvider(string? language)
    {
        _language = Normalize(language);
    }

    public string Language => _language;
    public bool IsGerman => _language.Equals("de", StringComparison.OrdinalIgnoreCase);

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            return _texts.TryGetValue(key, out var value)
                ? IsGerman ? value.De : value.En
                : key;
        }
    }

    public void SetLanguage(string? language)
    {
        var normalized = Normalize(language);
        if (_language.Equals(normalized, StringComparison.OrdinalIgnoreCase)) return;

        _language = normalized;
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(IsGerman));
        OnPropertyChanged("Item[]");
    }

    public void Toggle() => SetLanguage(IsGerman ? "en" : "de");

    private static string Normalize(string? language)
        => language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "de";
}

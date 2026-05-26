using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace ScumScriptStudio;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private string? _folder;
    private ScriptFile? _currentScript;
    private JsonObject? _currentJson;
    private readonly List<ScriptFile> _allScripts = new();
    public ObservableCollection<ScriptCommand> Commands { get; } = new();
    public ObservableCollection<LootPackEditorModel> LootPacks { get; } = new();
    public ObservableCollection<LootItemEditorModel> SelectedLootItems { get; } = new();
    public ObservableCollection<SimpleNpcSpawnModel> SimpleNpcs { get; } = new();
    public ObservableCollection<SimpleLootSpotModel> SimpleLootSpots { get; } = new();
    public ObservableCollection<SimpleLootPackModel> SimpleLootPacks { get; } = new();
    public ObservableCollection<LootItemEditorModel> SimpleSelectedLootItems { get; } = new();
    public string[] SpawnTypes { get; } = new[] { "ArmedNPC", "Puppet" };
    private SimpleLootPackModel? _selectedSimpleLootPack;
    private LootPackEditorModel? _selectedLootPack;
    private bool _updatingLootFields;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        BlockList.SelectedIndex = 2;
        Log("Bereit. Öffne einen Script-Ordner.");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "SCUM Script-Ordner wählen" };
        if (dlg.ShowDialog() == Forms.DialogResult.OK)
        {
            _folder = dlg.SelectedPath;
            FolderText.Text = _folder;
            LoadFolder();
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => LoadFolder();

    private void LoadFolder()
    {
        if (string.IsNullOrWhiteSpace(_folder) || !Directory.Exists(_folder)) { Log("Kein gültiger Ordner gewählt."); return; }
        _allScripts.Clear();
        foreach (var file in Directory.GetFiles(_folder, "*.json").OrderBy(x => x))
        {
            try
            {
                var text = File.ReadAllText(file);
                var node = JsonNode.Parse(text) as JsonObject;
                var id = node?["id"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(file);
                var name = node?["name"]?.GetValue<string>() ?? id;
                var mode = node?["mode"]?.GetValue<string>() ?? "";
                var enabled = node?["enabled"]?.GetValue<bool>() ?? false;
                _allScripts.Add(new ScriptFile(file, id, name, mode, enabled));
            }
            catch (Exception ex) { _allScripts.Add(new ScriptFile(file, Path.GetFileNameWithoutExtension(file), $"FEHLER: {ex.Message}", "", false)); }
        }
        ApplySearch();
        Log($"{_allScripts.Count} Skripte geladen.");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();
    private void ApplySearch()
    {
        var q = SearchBox.Text?.Trim() ?? "";
        IEnumerable<ScriptFile> items = _allScripts;
        if (!string.IsNullOrWhiteSpace(q)) items = items.Where(s => s.Id.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Mode.Contains(q, StringComparison.OrdinalIgnoreCase));
        ScriptList.ItemsSource = items.ToList();
    }

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptList.SelectedItem is ScriptFile script) LoadScript(script);
    }

    private void LoadScript(ScriptFile script)
    {
        try
        {
            var text = File.ReadAllText(script.Path);
            _currentJson = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            _currentScript = script;
            RawJsonBox.Text = _currentJson.ToJsonString(_jsonOptions);
            TitleText.Text = script.DisplayName;
            FileText.Text = script.Path;
            FillBasicsFromJson();
            SimpleLoadFromJsonInternal();
            LoadBlockFromJson(GetSelectedBlockName());
            LoadLootJson();
            Log($"Geladen: {script.Path}");
            ValidateCurrent();
        }
        catch (Exception ex) { Log($"Fehler beim Laden: {ex.Message}"); }
    }

    private void NewScript_Click(object sender, RoutedEventArgs e)
    {
        _currentJson = CreateTemplate();
        _currentScript = null;
        RawJsonBox.Text = _currentJson.ToJsonString(_jsonOptions);
        TitleText.Text = "Neues Skript";
        FileText.Text = "";
        FillBasicsFromJson();
        SimpleLoadFromJsonInternal();
        Commands.Clear();
        LoadLootJson();
        Log("Neues SilentZone-Template erstellt.");
    }

    private JsonObject CreateTemplate() => new()
    {
        ["id"] = "new_script", ["name"] = "New Script", ["enabled"] = true, ["mode"] = "SilentZone", ["includeInRandomizer"] = false,
        ["randomizerEveryMinutes"] = 360, ["initiatorRepeatEveryMinutes"] = 0, ["maxConcurrentRandomEvents"] = 0,
        ["activationZone"] = new JsonObject { ["name"] = "Aktivierungszone", ["centerX"] = 0, ["centerY"] = 0, ["centerZ"] = 0, ["radius"] = 10000 },
        ["initiatorBlock"] = CreateBlock("Initiator"), ["preLiveCleanupBlock"] = CreateBlock("PreLiveCleanup"), ["liveBlock"] = CreateBlock("LiveBlock"), ["emptyBlock"] = CreateBlock("EmptyBlock"), ["cleanupBlock"] = CreateBlock("CleanupBlock"),
        ["cleanupWhenEmptySeconds"] = 300, ["cooldownMinutes"] = 60, ["lootPacks"] = new JsonArray()
    };
    private static JsonObject CreateBlock(string name) => new() { ["name"] = name, ["enabled"] = true, ["commands"] = new JsonArray() };

    private void FillBasicsFromJson()
    {
        var json = ReadRawJsonObject(); if (json is null) return; _currentJson = json;
        IdBox.Text = GetString(json, "id"); NameBox.Text = GetString(json, "name"); SetCombo(ModeBox, GetString(json, "mode", "SilentZone")); SetCombo(AnnouncementTypeBox, GetString(json, "announcementType", "Yellow")); AnnouncementBox.Text = GetString(json, "announcement"); EnabledBox.IsChecked = GetBool(json, "enabled", true); IncludeRandomizerBox.IsChecked = GetBool(json, "includeInRandomizer", false); EventGroupBox.Text = GetString(json, "eventGroup"); MaxConcurrentInGroupBox.Text = GetNumberString(json, "maxConcurrentInGroup"); CooldownBox.Text = GetNumberString(json, "cooldownMinutes"); CleanupEmptyBox.Text = GetNumberString(json, "cleanupWhenEmptySeconds"); RandomizerEveryBox.Text = GetNumberString(json, "randomizerEveryMinutes"); InitiatorRepeatBox.Text = GetNumberString(json, "initiatorRepeatEveryMinutes");
        var zone = json["activationZone"] as JsonObject ?? json["zone"] as JsonObject;
        ZoneNameBox.Text = zone is null ? "" : GetString(zone, "name"); ZoneXBox.Text = zone is null ? "" : GetNumberString(zone, "centerX"); ZoneYBox.Text = zone is null ? "" : GetNumberString(zone, "centerY"); ZoneZBox.Text = zone is null ? "" : GetNumberString(zone, "centerZ"); ZoneRadiusBox.Text = zone is null ? "" : GetNumberString(zone, "radius");
    }

    private void ApplyBasics_Click(object sender, RoutedEventArgs e)
    {
        var json = ReadRawJsonObject(); if (json is null) return;
        json["id"] = IdBox.Text.Trim(); json["name"] = NameBox.Text.Trim(); json["enabled"] = EnabledBox.IsChecked == true; json["mode"] = GetComboText(ModeBox); json["includeInRandomizer"] = IncludeRandomizerBox.IsChecked == true;
        SetOrRemove(json, "announcementType", GetComboText(AnnouncementTypeBox)); SetOrRemove(json, "announcement", AnnouncementBox.Text.Trim()); SetOrRemove(json, "eventGroup", EventGroupBox.Text.Trim()); SetIntOrRemove(json, "maxConcurrentInGroup", MaxConcurrentInGroupBox.Text); SetIntOrRemove(json, "cooldownMinutes", CooldownBox.Text); SetIntOrRemove(json, "cleanupWhenEmptySeconds", CleanupEmptyBox.Text); SetIntOrRemove(json, "randomizerEveryMinutes", RandomizerEveryBox.Text); SetIntOrRemove(json, "initiatorRepeatEveryMinutes", InitiatorRepeatBox.Text);
        json.Remove("zone"); json["activationZone"] = new JsonObject { ["name"] = ZoneNameBox.Text.Trim(), ["centerX"] = ParseDoubleOrZero(ZoneXBox.Text), ["centerY"] = ParseDoubleOrZero(ZoneYBox.Text), ["centerZ"] = ParseDoubleOrZero(ZoneZBox.Text), ["radius"] = ParseDoubleOrZero(ZoneRadiusBox.Text) };
        _currentJson = json; RawJsonBox.Text = json.ToJsonString(_jsonOptions); Log("Basisdaten ins JSON übernommen."); ValidateCurrent();
    }

    private void ConvertLegacyZone_Click(object sender, RoutedEventArgs e)
    {
        var json = ReadRawJsonObject(); if (json is null) return;
        if (json["activationZone"] is not null) { Log("activationZone ist bereits vorhanden."); return; }
        if (json["zone"] is JsonObject zone) { json["activationZone"] = zone.DeepClone(); json.Remove("zone"); RawJsonBox.Text = json.ToJsonString(_jsonOptions); FillBasicsFromJson(); Log("Legacy zone wurde nach activationZone konvertiert."); } else Log("Keine legacy zone gefunden.");
    }

    private void BlockList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_currentJson is not null) LoadBlockFromJson(GetSelectedBlockName()); }
    private void LoadBlock_Click(object sender, RoutedEventArgs e) => LoadBlockFromJson(GetSelectedBlockName());
    private string GetSelectedBlockName() => ((BlockList.SelectedItem as ListBoxItem)?.Content?.ToString()) ?? "liveBlock";

    private void LoadBlockFromJson(string blockName)
    {
        Commands.Clear(); var json = ReadRawJsonObject(); if (json?[blockName] is not JsonObject block) return; if (block["commands"] is not JsonArray arr) return;
        foreach (var node in arr.OfType<JsonObject>()) Commands.Add(new ScriptCommand { Enabled = GetBool(node, "enabled", true), Name = GetString(node, "name"), Command = GetString(node, "command"), Repeat = GetInt(node, "repeat", 1), DelayMs = GetInt(node, "delayMs", 250) });
        Log($"Block geladen: {blockName} ({Commands.Count} Commands)");
    }

    private void ApplyBlock_Click(object sender, RoutedEventArgs e)
    {
        var blockName = GetSelectedBlockName(); var json = ReadRawJsonObject(); if (json is null) return;
        var block = json[blockName] as JsonObject ?? new JsonObject { ["name"] = blockName, ["enabled"] = true };
        var arr = new JsonArray(); foreach (var c in Commands) { if (string.IsNullOrWhiteSpace(c.Command) && string.IsNullOrWhiteSpace(c.Name)) continue; arr.Add(new JsonObject { ["name"] = c.Name ?? "", ["enabled"] = c.Enabled, ["command"] = c.Command ?? "", ["repeat"] = c.Repeat, ["delayMs"] = c.DelayMs }); }
        block["commands"] = arr; json[blockName] = block; _currentJson = json; RawJsonBox.Text = json.ToJsonString(_jsonOptions); Log($"Block übernommen: {blockName} ({arr.Count} Commands)"); ValidateCurrent();
    }
    private void AddCommand_Click(object sender, RoutedEventArgs e) => Commands.Add(new ScriptCommand { Enabled = true, Name = "New Command", Command = "#Broadcast Yellow Test", Repeat = 1, DelayMs = 250 });
    private void RemoveCommand_Click(object sender, RoutedEventArgs e) { if (CommandsGrid.SelectedItem is ScriptCommand cmd) Commands.Remove(cmd); }
    private void LoadLoot_Click(object sender, RoutedEventArgs e) => LoadLootEditorFromJson();

    private void ApplyLoot_Click(object sender, RoutedEventArgs e) => ApplyLootEditorToJson();

    private void LoadLootEditor_Click(object sender, RoutedEventArgs e) => LoadLootEditorFromJson();

    private void LoadLootJson()
    {
        LoadLootEditorFromJson();
    }

    private void LoadLootEditorFromJson()
    {
        LootPacks.Clear();
        SelectedLootItems.Clear();
        _selectedLootPack = null;

        var json = ReadRawJsonObject();
        if (json is null) return;

        if (json["lootPacks"] is JsonArray lootPacks)
        {
            foreach (var p in lootPacks.OfType<JsonObject>())
                LootPacks.Add(ParseLegacyLootPack(p));
        }
        else if (json["lootCommandPacks"] is JsonArray commandPacks)
        {
            // Legacy import only: old legacy command packs are converted back to normal lootPacks in the editor.
            foreach (var p in commandPacks.OfType<JsonObject>())
                LootPacks.Add(ParseLootCommandPack(p));
        }

        if (LootPacks.Count > 0)
            LootPackList.SelectedIndex = 0;

        Log($"Loot-Editor geladen: {LootPacks.Count} Packs.");
    }

    private LootPackEditorModel ParseLegacyLootPack(JsonObject p)
    {
        var pack = new LootPackEditorModel
        {
            Name = GetString(p, "name", "Loot Pack"),
            Enabled = GetBool(p, "enabled", true),
            Weight = GetInt(p, "weight", 1),
            Location = GetString(p, "location"),
            ChestType = "Improved_Wooden_Chest",
            DelayMs = 250
        };

        if (p["items"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                pack.Items.Add(new LootItemEditorModel
                {
                    Item = GetString(item, "item"),
                    Quantity = GetInt(item, "quantity", 1),
                    DelayMs = GetInt(item, "delayMs", 250)
                });
            }
        }

        return pack;
    }

    private LootPackEditorModel ParseLootCommandPack(JsonObject p)
    {
        var pack = new LootPackEditorModel
        {
            Name = GetString(p, "name", "Loot Chest Pack"),
            Enabled = GetBool(p, "enabled", true),
            Weight = GetInt(p, "weight", 1),
            Location = GetString(p, "location"),
            DelayMs = GetInt(p, "delayMs", 250),
            ChestType = "Improved_Wooden_Chest"
        };

        ParseSpawnInventoryFullOf(GetString(p, "command"), pack);
        return pack;
    }

    private static void ParseSpawnInventoryFullOf(string command, LootPackEditorModel pack)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        var tokens = TokenizeCommand(command);
        var start = tokens.FindIndex(t => string.Equals(t, "#SpawnInventoryFullOf", StringComparison.OrdinalIgnoreCase));
        if (start < 0 || start + 2 >= tokens.Count) return;

        pack.ChestType = tokens[start + 1];
        var i = start + 2;

        if (i < tokens.Count && int.TryParse(tokens[i], out _))
            i++;

        while (i < tokens.Count)
        {
            if (string.Equals(tokens[i], "Location", StringComparison.OrdinalIgnoreCase))
                break;

            var item = tokens[i];
            var qty = 1;
            if (i + 1 < tokens.Count && int.TryParse(tokens[i + 1], out var parsedQty))
            {
                qty = parsedQty;
                i += 2;
            }
            else
            {
                i++;
            }

            if (!string.IsNullOrWhiteSpace(item))
            {
                pack.Items.Add(new LootItemEditorModel
                {
                    Item = item,
                    Quantity = qty,
                    DelayMs = pack.DelayMs
                });
            }
        }
    }

    private static List<string> TokenizeCommand(string command)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in command)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private void LootPackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveLootFieldsToSelected();
        _selectedLootPack = LootPackList.SelectedItem as LootPackEditorModel;
        FillLootFieldsFromSelected();
    }

    private void FillLootFieldsFromSelected()
    {
        _updatingLootFields = true;
        SelectedLootItems.Clear();

        if (_selectedLootPack is null)
        {
            LootNameBox.Text = "";
            LootWeightBox.Text = "";
            LootLocationBox.Text = "";
            LootDelayBox.Text = "";
            
            LootEnabledBox.IsChecked = false;
            LootCommandPreviewBox.Text = "";
            _updatingLootFields = false;
            return;
        }

        LootNameBox.Text = _selectedLootPack.Name;
        LootWeightBox.Text = _selectedLootPack.Weight.ToString(CultureInfo.InvariantCulture);
        LootLocationBox.Text = _selectedLootPack.Location;
        LootDelayBox.Text = _selectedLootPack.DelayMs.ToString(CultureInfo.InvariantCulture);
        LootEnabledBox.IsChecked = _selectedLootPack.Enabled;
        

        foreach (var item in _selectedLootPack.Items)
            SelectedLootItems.Add(item);

        _updatingLootFields = false;
        RefreshLootCommandPreview();
    }

    private void SaveLootFieldsToSelected()
    {
        if (_updatingLootFields || _selectedLootPack is null) return;

        _selectedLootPack.Name = LootNameBox.Text.Trim();
        _selectedLootPack.Enabled = LootEnabledBox.IsChecked == true;
        _selectedLootPack.Weight = ParseIntOrDefault(LootWeightBox.Text, 1);
        _selectedLootPack.Location = LootLocationBox.Text.Trim();
        _selectedLootPack.DelayMs = ParseIntOrDefault(LootDelayBox.Text, 250);
        _selectedLootPack.Items.Clear();
        foreach (var item in SelectedLootItems)
            _selectedLootPack.Items.Add(item);

        LootPackList.Items.Refresh();
        RefreshLootCommandPreview();
    }

    private void LootPackField_TextChanged(object sender, TextChangedEventArgs e) => SaveLootFieldsToSelected();
    private void LootPackField_Checked(object sender, RoutedEventArgs e) => SaveLootFieldsToSelected();
    private void LootPackField_SelectionChanged(object sender, SelectionChangedEventArgs e) => SaveLootFieldsToSelected();

    private void AddLootPack_Click(object sender, RoutedEventArgs e)
    {
        var pack = new LootPackEditorModel
        {
            Name = $"New Chest Pack {LootPacks.Count + 1}",
            Enabled = true,
            Weight = 1,
            ChestType = "Improved_Wooden_Chest",
            Location = "",
            DelayMs = 250
        };
        pack.Items.Add(new LootItemEditorModel { Item = "Weapon_MK18", Quantity = 1, DelayMs = 250 });

        LootPacks.Add(pack);
        LootPackList.SelectedItem = pack;
        Log("Loot-Pack hinzugefügt.");
    }

    private void RemoveLootPack_Click(object sender, RoutedEventArgs e)
    {
        if (LootPackList.SelectedItem is LootPackEditorModel pack)
        {
            var index = LootPackList.SelectedIndex;
            LootPacks.Remove(pack);
            if (LootPacks.Count > 0)
                LootPackList.SelectedIndex = Math.Clamp(index, 0, LootPacks.Count - 1);
            Log("Loot-Pack entfernt.");
        }
    }

    private void DuplicateLootPack_Click(object sender, RoutedEventArgs e)
    {
        SaveLootFieldsToSelected();
        if (_selectedLootPack is null) return;

        var clone = _selectedLootPack.Clone();
        clone.Name += " Kopie";
        LootPacks.Add(clone);
        LootPackList.SelectedItem = clone;
        Log("Loot-Pack dupliziert.");
    }

    private void AddLootItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLootPack is null)
            AddLootPack_Click(sender, e);

        SelectedLootItems.Add(new LootItemEditorModel { Item = "ItemName", Quantity = 1, DelayMs = 250 });
        SaveLootFieldsToSelected();
    }

    private void RemoveLootItem_Click(object sender, RoutedEventArgs e)
    {
        if (LootItemsGrid.SelectedItem is LootItemEditorModel item)
        {
            SelectedLootItems.Remove(item);
            SaveLootFieldsToSelected();
        }
    }

    private void RefreshLootCommandPreview_Click(object sender, RoutedEventArgs e)
    {
        SaveLootFieldsToSelected();
        RefreshLootCommandPreview();
    }

    private void RefreshLootCommandPreview()
    {
        LootCommandPreviewBox.Text = _selectedLootPack is null ? "" : BuildLootPackPreview(_selectedLootPack);
    }

    private void ApplyLootEditor_Click(object sender, RoutedEventArgs e) => ApplyLootEditorToJson();

    private void ApplyLootEditorToJson()
    {
        SaveLootFieldsToSelected();

        var json = ReadRawJsonObject();
        if (json is null) return;

        var lootPacks = new JsonArray();

        foreach (var pack in LootPacks)
        {
            if (string.IsNullOrWhiteSpace(pack.Name))
                continue;

            lootPacks.Add(BuildLootPackJson(pack.Name, pack.Enabled, pack.Weight, pack.Location ?? "", pack.Items));
        }

        json.Remove("lootPacks");
        json["lootPacks"] = lootPacks;
        json["lootPackSpawnMode"] = "OnePerLocation";

        _currentJson = json;
        RawJsonBox.Text = json.ToJsonString(_jsonOptions);
        Log($"Lootpacks ins JSON übernommen: {lootPacks.Count} Packs.");
        ValidateCurrent();
    }


    private static JsonObject BuildLootPackJson(string name, bool enabled, int weight, string location, IEnumerable<LootItemEditorModel> items)
    {
        var arr = new JsonArray();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Item)) continue;
            arr.Add(new JsonObject
            {
                ["item"] = item.Item.Trim(),
                ["quantity"] = Math.Max(1, item.Quantity),
                ["delayMs"] = item.DelayMs <= 0 ? 250 : item.DelayMs
            });
        }

        return new JsonObject
        {
            ["name"] = name,
            ["enabled"] = enabled,
            ["weight"] = weight,
            ["location"] = location ?? "",
            ["items"] = arr
        };
    }

    private string BuildLootPackPreview(LootPackEditorModel pack)
    {
        var json = BuildLootPackJson(pack.Name, pack.Enabled, pack.Weight, pack.Location, pack.Items);
        return json.ToJsonString(_jsonOptions);
    }

    private static string BuildChestCommand(LootPackEditorModel pack)
    {
        var chestType = string.IsNullOrWhiteSpace(pack.ChestType) ? "Improved_Wooden_Chest" : pack.ChestType.Trim();
        var parts = new List<string> { "#SpawnInventoryFullOf", chestType, "1" };

        foreach (var item in pack.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Item)) continue;
            parts.Add(item.Item.Trim());
            parts.Add(Math.Max(1, item.Quantity).ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(pack.Location))
        {
            parts.Add("Location");
            parts.Add($"\"{pack.Location.Trim()}\"");
        }

        return string.Join(" ", parts);
    }

    private static int ParseIntOrDefault(string value, int fallback)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;
        return fallback;
    }


    private void SimpleLoadFromJson_Click(object sender, RoutedEventArgs e)
    {
        SimpleLoadFromJsonInternal();
    }

    private void SimpleLoadFromJsonInternal()
    {
        var json = ReadRawJsonObject();
        if (json is null) return;

        SimpleIdBox.Text = GetString(json, "id", "new_event");
        SimpleNameBox.Text = GetString(json, "name", "New Event");
        SetCombo(SimpleModeBox, GetString(json, "mode", "SilentZone"));
        SimpleCooldownBox.Text = GetNumberString(json, "cooldownMinutes");
        if (string.IsNullOrWhiteSpace(SimpleCooldownBox.Text)) SimpleCooldownBox.Text = "180";
        SimpleCleanupEmptyBox.Text = GetNumberString(json, "cleanupWhenEmptySeconds");
        if (string.IsNullOrWhiteSpace(SimpleCleanupEmptyBox.Text)) SimpleCleanupEmptyBox.Text = "300";
        if (string.IsNullOrWhiteSpace(SimpleExtraRadiusBox.Text)) SimpleExtraRadiusBox.Text = "10000";

        SimpleNpcs.Clear();
        SimpleLootSpots.Clear();
        SimpleLootPacks.Clear();
        SimpleSelectedLootItems.Clear();
        _selectedSimpleLootPack = null;

        if (json["liveBlock"] is JsonObject live && live["commands"] is JsonArray commands)
        {
            foreach (var cmd in commands.OfType<JsonObject>())
            {
                var text = GetString(cmd, "command");
                if (!text.Contains("#SpawnArmedNPC", StringComparison.OrdinalIgnoreCase)) continue;
                SimpleNpcs.Add(new SimpleNpcSpawnModel
                {
                    Name = GetString(cmd, "name", $"NPC {SimpleNpcs.Count + 1}"),
                    SpawnType = text.Contains("#SpawnRandomZombie", StringComparison.OrdinalIgnoreCase) ? "Puppet" : "ArmedNPC",
                    NpcType = ExtractNpcType(text),
                    Count = ExtractNpcCount(text),
                    Location = ExtractLocation(text)
                });
            }
        }

        if (json["preLiveCleanupBlock"] is JsonObject pre && pre["commands"] is JsonArray cleanupCommands)
        {
            foreach (var cmd in cleanupCommands.OfType<JsonObject>())
            {
                var loc = ExtractLocation(GetString(cmd, "command"));
                if (!string.IsNullOrWhiteSpace(loc) && !SimpleLootSpots.Any(s => s.Location == loc))
                {
                    SimpleLootSpots.Add(new SimpleLootSpotModel
                    {
                        Name = $"Loot Spot {SimpleLootSpots.Count + 1}",
                        Location = loc
                    });
                }
            }
        }

        if (json["lootPacks"] is JsonArray lootPacks)
        {
            foreach (var p in lootPacks.OfType<JsonObject>())
            {
                var loc = GetString(p, "location");
                if (!string.IsNullOrWhiteSpace(loc) && !SimpleLootSpots.Any(s => s.Location == loc))
                {
                    SimpleLootSpots.Add(new SimpleLootSpotModel
                    {
                        Name = $"Loot Spot {SimpleLootSpots.Count + 1}",
                        Location = loc
                    });
                }

                var pack = new SimpleLootPackModel
                {
                    Name = StripLootSpotPrefix(GetString(p, "name", $"Loot Pack {SimpleLootPacks.Count + 1}")),
                    Enabled = GetBool(p, "enabled", true),
                    Weight = GetInt(p, "weight", 1)
                };

                if (p["items"] is JsonArray items)
                {
                    foreach (var item in items.OfType<JsonObject>())
                    {
                        pack.Items.Add(new LootItemEditorModel
                        {
                            Item = GetString(item, "item"),
                            Quantity = GetInt(item, "quantity", 1),
                            DelayMs = GetInt(item, "delayMs", 250)
                        });
                    }
                }

                SimpleLootPacks.Add(pack);
            }
        }
        else if (json["lootCommandPacks"] is JsonArray commandPacks)
        {
            // Legacy import only: old unsupported chest command packs are converted back to normal lootpacks.
            foreach (var p in commandPacks.OfType<JsonObject>())
            {
                var loc = GetString(p, "location");
                if (!string.IsNullOrWhiteSpace(loc) && !SimpleLootSpots.Any(s => s.Location == loc))
                {
                    SimpleLootSpots.Add(new SimpleLootSpotModel
                    {
                        Name = $"Loot Spot {SimpleLootSpots.Count + 1}",
                        Location = loc
                    });
                }

                var pack = new SimpleLootPackModel
                {
                    Name = StripLootSpotPrefix(GetString(p, "name", $"Loot Pack {SimpleLootPacks.Count + 1}")),
                    Enabled = GetBool(p, "enabled", true),
                    Weight = GetInt(p, "weight", 1)
                };

                var temp = new LootPackEditorModel { DelayMs = GetInt(p, "delayMs", 250) };
                ParseSpawnInventoryFullOf(GetString(p, "command"), temp);
                foreach (var item in temp.Items)
                    pack.Items.Add(item);

                SimpleLootPacks.Add(pack);
            }
        }

        // De-duplicate simple packs by name and item list. Old lootCommandPacks often contain one pack per spot.
        var unique = SimpleLootPacks
            .GroupBy(p => p.Signature)
            .Select(g => g.First())
            .ToList();
        SimpleLootPacks.Clear();
        foreach (var p in unique) SimpleLootPacks.Add(p);

        if (SimpleLootPacks.Count > 0)
            SimpleLootPackGrid.SelectedIndex = 0;

        Log($"Simple Mode geladen: {SimpleNpcs.Count} NPCs, {SimpleLootSpots.Count} Loot-Spots, {SimpleLootPacks.Count} Lootpacks.");
    }

    private static string StripLootSpotPrefix(string name)
    {
        var idx = name.LastIndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0 && name.Contains("Loot", StringComparison.OrdinalIgnoreCase))
        {
            var tail = name[(idx + 3)..];
            if (!tail.Contains("Chest", StringComparison.OrdinalIgnoreCase))
                return tail;
        }

        return name.Replace(" - Chest", "", StringComparison.OrdinalIgnoreCase);
    }

    private void SimpleAddNpc_Click(object sender, RoutedEventArgs e)
    {
        SimpleNpcs.Add(new SimpleNpcSpawnModel
        {
            Name = $"Spawn {SimpleNpcs.Count + 1}",
            SpawnType = "ArmedNPC",
            NpcType = "BP_Guard_Lvl_5",
            Count = 1,
            Location = "[{X=0 Y=0 Z=0|P=0 Y=0 R=0}]"
        });
    }

    private void SimpleRemoveNpc_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleNpcGrid.SelectedItem is SimpleNpcSpawnModel item)
            SimpleNpcs.Remove(item);
    }

    private void SimpleAddLootSpot_Click(object sender, RoutedEventArgs e)
    {
        SimpleLootSpots.Add(new SimpleLootSpotModel
        {
            Name = $"Loot Spot {SimpleLootSpots.Count + 1}",
            Location = "[{X=0 Y=0 Z=0|P=0 Y=0 R=0}]"
        });
    }

    private void SimpleRemoveLootSpot_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleLootSpotGrid.SelectedItem is SimpleLootSpotModel item)
            SimpleLootSpots.Remove(item);
    }

    private void SimpleAddLootPack_Click(object sender, RoutedEventArgs e)
    {
        var pack = new SimpleLootPackModel
        {
            Name = $"Loot Pack {SimpleLootPacks.Count + 1}",
            Enabled = true,
            Weight = 10
        };
        pack.Items.Add(new LootItemEditorModel { Item = "Weapon_MK18", Quantity = 1, DelayMs = 250 });
        SimpleLootPacks.Add(pack);
        SimpleLootPackGrid.SelectedItem = pack;
    }

    private void SimpleRemoveLootPack_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleLootPackGrid.SelectedItem is SimpleLootPackModel item)
            SimpleLootPacks.Remove(item);
    }

    private void SimpleLootPackGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedSimpleLootPack is not null)
        {
            _selectedSimpleLootPack.Items.Clear();
            foreach (var item in SimpleSelectedLootItems)
                _selectedSimpleLootPack.Items.Add(item);
        }

        _selectedSimpleLootPack = SimpleLootPackGrid.SelectedItem as SimpleLootPackModel;
        SimpleSelectedLootItems.Clear();

        if (_selectedSimpleLootPack is not null)
        {
            foreach (var item in _selectedSimpleLootPack.Items)
                SimpleSelectedLootItems.Add(item);
        }
    }

    private void SimpleAddLootItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSimpleLootPack is null)
            SimpleAddLootPack_Click(sender, e);

        SimpleSelectedLootItems.Add(new LootItemEditorModel { Item = "ItemName", Quantity = 1, DelayMs = 250 });
    }

    private void SimpleRemoveLootItem_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleLootItemGrid.SelectedItem is LootItemEditorModel item)
            SimpleSelectedLootItems.Remove(item);
    }

    private void SimpleInsertExamplePacks_Click(object sender, RoutedEventArgs e)
    {
        SimpleLootPacks.Clear();

        AddSimplePack("AKM Pack", 40, ("Weapon_AKM", 1), ("Magazine_AK47", 2), ("Cal_7_62x39mm_Ammobox", 2), ("WeaponSuppressor_AK15", 1));
        AddSimplePack("SKS Pack", 10, ("Weapon_SKS", 1), ("Magazine_Clip_SKS", 2), ("Cal_7_62x39mm_Ammobox", 2), ("WeaponSuppressor_AK15", 1));
        AddSimplePack("AS Val Pack", 30, ("Weapon_AS_Val", 1), ("Magazine_AS_Val", 2), ("Cal_9x39mm_Ammobox", 2));
        AddSimplePack("Utility Pack", 30, ("Lockpick_Advanced_Item", 2), ("Lock_Item_Advanced", 2), ("Screwdriver", 2), ("MRE_TunaSalad", 2));

        SimpleLootPackGrid.SelectedIndex = 0;
        Log("Beispielpacks eingefügt.");
    }

    private void AddSimplePack(string name, int weight, params (string item, int qty)[] items)
    {
        var pack = new SimpleLootPackModel { Name = name, Weight = weight, Enabled = true };
        foreach (var (item, qty) in items)
            pack.Items.Add(new LootItemEditorModel { Item = item, Quantity = qty, DelayMs = 250 });
        SimpleLootPacks.Add(pack);
    }

    private void SimpleBuildJson_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSimpleLootPack is not null)
        {
            _selectedSimpleLootPack.Items.Clear();
            foreach (var item in SimpleSelectedLootItems)
                _selectedSimpleLootPack.Items.Add(item);
        }

        var id = string.IsNullOrWhiteSpace(SimpleIdBox.Text) ? "new_event" : SimpleIdBox.Text.Trim();
        var name = string.IsNullOrWhiteSpace(SimpleNameBox.Text) ? id : SimpleNameBox.Text.Trim();
        var mode = GetComboText(SimpleModeBox);
        if (string.IsNullOrWhiteSpace(mode)) mode = "SilentZone";

        var allLocations = SimpleNpcs.Select(n => n.Location)
            .Concat(SimpleLootSpots.Select(s => s.Location))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var zone = BuildActivationZoneFromLocations(name + " Aktivierzone", allLocations, ParseDoubleOrZero(SimpleExtraRadiusBox.Text));
        var cooldown = ParseIntOrDefault(SimpleCooldownBox.Text, 180);
        var cleanupEmpty = ParseIntOrDefault(SimpleCleanupEmptyBox.Text, 300);

        var json = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["enabled"] = true,
            ["mode"] = mode,
            ["includeInRandomizer"] = string.Equals(mode, "RandomAnnouncedZone", StringComparison.OrdinalIgnoreCase),
            ["randomizerEveryMinutes"] = 360,
            ["initiatorRepeatEveryMinutes"] = string.Equals(mode, "RandomAnnouncedZone", StringComparison.OrdinalIgnoreCase) ? 30 : 0,
            ["announcementType"] = "Yellow",
            ["announcement"] = name,
            ["activationZone"] = zone,
            ["initiatorBlock"] = new JsonObject
            {
                ["name"] = "Initiator",
                ["enabled"] = true,
                ["commands"] = string.Equals(mode, "RandomAnnouncedZone", StringComparison.OrdinalIgnoreCase)
                    ? new JsonArray(new JsonObject
                    {
                        ["name"] = "Announcement",
                        ["enabled"] = true,
                        ["command"] = $"#Broadcast Yellow {name}",
                        ["repeat"] = 1,
                        ["delayMs"] = 250
                    })
                    : new JsonArray()
            },
            ["preLiveCleanupBlock"] = BuildCleanupBlock("PreLiveCleanup - automatisch aus Loot-Spots", SimpleLootSpots),
            ["liveBlock"] = BuildSimpleLiveBlock(name),
            ["emptyBlock"] = new JsonObject
            {
                ["name"] = "EmptyBlock - Zone leer",
                ["enabled"] = true,
                ["commands"] = new JsonArray(new JsonObject
                {
                    ["name"] = "Zone empty broadcast",
                    ["enabled"] = true,
                    ["command"] = $"#Broadcast Yellow {name} ist leer. Cooldown startet.",
                    ["repeat"] = 1,
                    ["delayMs"] = 250
                })
            },
            ["cleanupBlock"] = BuildCleanupBlock("CleanupBlock - automatisch aus Loot-Spots", SimpleLootSpots),
            ["cleanupWhenEmptySeconds"] = cleanupEmpty,
            ["cooldownMinutes"] = cooldown,
            ["lootPacks"] = BuildSimpleLootPacks(),
            ["lootPackSpawnMode"] = "OnePerLocation"
        };

        _currentJson = json;
        RawJsonBox.Text = json.ToJsonString(_jsonOptions);
        FillBasicsFromJson();
        LoadBlockFromJson(GetSelectedBlockName());
        LoadLootEditorFromJson();
        Log($"Simple Event gebaut: {SimpleNpcs.Count} NPCs, {SimpleLootSpots.Count} Loot-Spots, {SimpleLootPacks.Count} Lootpacks.");
        ValidateCurrent();
        EditorTabs.SelectedIndex = 4; // Raw JSON
    }

    private JsonObject BuildSimpleLiveBlock(string eventName)
    {
        var commands = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "Event started broadcast",
                ["enabled"] = true,
                ["command"] = $"#Broadcast Red Eventzone {eventName} gestartet",
                ["repeat"] = 1,
                ["delayMs"] = 250
            }
        };

        var index = 1;
        foreach (var npc in SimpleNpcs)
        {
            if (string.IsNullOrWhiteSpace(npc.Location)) continue;

            var npcType = string.IsNullOrWhiteSpace(npc.NpcType) ? "BP_Guard_Lvl_5" : npc.NpcType.Trim();
            var count = Math.Max(1, npc.Count);
            var spawnType = string.IsNullOrWhiteSpace(npc.SpawnType) ? "ArmedNPC" : npc.SpawnType.Trim();

            var command = spawnType.Equals("Puppet", StringComparison.OrdinalIgnoreCase)
                ? $"#ExecAs {{playerId}} #SpawnRandomZombie {count} Location \"{npc.Location.Trim()}\" DespawnLifetime 600"
                : $"#ExecAs {{playerId}} #SpawnArmedNPC {npcType} {count} Location \"{npc.Location.Trim()}\" DespawnLifetime 600";

            commands.Add(new JsonObject
            {
                ["name"] = string.IsNullOrWhiteSpace(npc.Name) ? $"Spawn {index}" : npc.Name,
                ["enabled"] = true,
                ["command"] = command,
                ["repeat"] = 1,
                ["delayMs"] = 800
            });

            index++;
        }

        return new JsonObject
        {
            ["name"] = "LiveBlock - automatisch",
            ["enabled"] = true,
            ["commands"] = commands
        };
    }

    private static JsonObject BuildCleanupBlock(string name, IEnumerable<SimpleLootSpotModel> spots)
    {
        var commands = new JsonArray();
        var index = 1;

        foreach (var spot in spots)
        {
            if (string.IsNullOrWhiteSpace(spot.Location)) continue;
            commands.Add(new JsonObject
            {
                ["name"] = string.IsNullOrWhiteSpace(spot.Name) ? $"Cleanup loot {index}" : $"Cleanup - {spot.Name}",
                ["enabled"] = true,
                ["command"] = $"#DestroyAllItemsWithinRadius all 20 Location \"{spot.Location.Trim()}\"",
                ["repeat"] = 1,
                ["delayMs"] = 250
            });
            index++;
        }

        return new JsonObject
        {
            ["name"] = name,
            ["enabled"] = true,
            ["commands"] = commands
        };
    }

    private JsonArray BuildSimpleLootPacks()
    {
        var result = new JsonArray();

        foreach (var spot in SimpleLootSpots)
        {
            if (string.IsNullOrWhiteSpace(spot.Location)) continue;

            foreach (var pack in SimpleLootPacks)
            {
                if (!pack.Enabled) continue;

                result.Add(BuildLootPackJson(
                    $"{spot.Name} - {pack.Name}",
                    true,
                    pack.Weight,
                    spot.Location,
                    pack.Items));
            }
        }

        return result;
    }

    private static JsonObject BuildActivationZoneFromLocations(string name, List<string> locations, double extraRadius)
    {
        var points = locations.Select(TryParsePoint).Where(p => p.HasValue).Select(p => p!.Value).ToList();

        if (points.Count == 0)
        {
            return new JsonObject
            {
                ["name"] = name,
                ["centerX"] = 0,
                ["centerY"] = 0,
                ["centerZ"] = 0,
                ["radius"] = Math.Max(10000, extraRadius)
            };
        }

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        var centerX = Math.Round((minX + maxX) / 2, 3);
        var centerY = Math.Round((minY + maxY) / 2, 3);
        var centerZ = Math.Round(points.Average(p => p.Z), 3);

        var radius = points.Max(p => Math.Sqrt(Math.Pow(p.X - centerX, 2) + Math.Pow(p.Y - centerY, 2))) + Math.Max(0, extraRadius);
        radius = Math.Ceiling(Math.Max(1000, radius));

        return new JsonObject
        {
            ["name"] = name,
            ["centerX"] = centerX,
            ["centerY"] = centerY,
            ["centerZ"] = centerZ,
            ["radius"] = radius
        };
    }

    private static (double X, double Y, double Z)? TryParsePoint(string location)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            location,
            @"X\s*=\s*(?<x>-?\d+(?:\.\d+)?)\s+Y\s*=\s*(?<y>-?\d+(?:\.\d+)?)\s+Z\s*=\s*(?<z>-?\d+(?:\.\d+)?)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success) return null;

        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        return (D(match.Groups["x"].Value), D(match.Groups["y"].Value), D(match.Groups["z"].Value));
    }

    private static string ExtractLocation(string command)
    {
        var match = System.Text.RegularExpressions.Regex.Match(command, "Location\\s+\\\"(?<loc>[^\\\"]+)\\\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["loc"].Value : "";
    }

    private static string ExtractNpcType(string command)
    {
        if (command.Contains("#SpawnRandomZombie", StringComparison.OrdinalIgnoreCase))
            return "";

        var match = System.Text.RegularExpressions.Regex.Match(command, "#SpawnArmedNPC\\s+(?<type>\\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["type"].Value : "BP_Guard_Lvl_5";
    }

    private static int ExtractNpcCount(string command)
    {
        var armed = System.Text.RegularExpressions.Regex.Match(command, "#SpawnArmedNPC\\s+\\S+\\s+(?<count>\\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (armed.Success && int.TryParse(armed.Groups["count"].Value, out var armedCount))
            return armedCount;

        var puppet = System.Text.RegularExpressions.Regex.Match(command, "#SpawnRandomZombie\\s+(?<count>\\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (puppet.Success && int.TryParse(puppet.Groups["count"].Value, out var puppetCount))
            return puppetCount;

        return 1;
    }

    private void Validate_Click(object sender, RoutedEventArgs e) => ValidateCurrent();
    private bool ValidateCurrent()
    {
        var json = ReadRawJsonObject(); if (json is null) return false; var errors = new List<string>(); var warnings = new List<string>();
        Require(json, "id", errors); Require(json, "name", errors); Require(json, "mode", errors);
        var zone = json["activationZone"] as JsonObject; if (zone is null) { if (json["zone"] is not null) warnings.Add("Legacy-Feld 'zone' gefunden. Besser nach 'activationZone' konvertieren."); else errors.Add("activationZone fehlt."); } else { if (GetDouble(zone, "radius", 0) <= 0) errors.Add("activationZone.radius muss > 0 sein."); if (Math.Abs(GetDouble(zone, "centerX", 0)) < 0.001 && Math.Abs(GetDouble(zone, "centerY", 0)) < 0.001) warnings.Add("activationZone Center ist 0/0. Prüfen, ob das gewollt ist."); }
        foreach (var blockName in new[] { "initiatorBlock", "preLiveCleanupBlock", "liveBlock", "emptyBlock", "cleanupBlock" }) if (json[blockName] is JsonObject block && block["commands"] is JsonArray arr) { int i = 0; foreach (var cmd in arr.OfType<JsonObject>()) { i++; var text = GetString(cmd, "command"); if (GetBool(cmd, "enabled", true) && string.IsNullOrWhiteSpace(text)) warnings.Add($"{blockName} Command {i} ist enabled, aber command ist leer."); } }
        if (json["lootPacks"] is JsonArray lootPacks && json["lootPacks"] is JsonArray commandPacks) warnings.Add($"Skript enthält lootPacks ({lootPacks.Count}) und lootCommandPacks ({commandPacks.Count}). Prüfen, ob die Engine beide ausführen soll.");
        if (json["maxConcurrentInGroup"] is not null && GetInt(json, "maxConcurrentInGroup", 0) > 0 && string.IsNullOrWhiteSpace(GetString(json, "eventGroup"))) warnings.Add("maxConcurrentInGroup ist gesetzt, aber eventGroup fehlt.");
        LogBox.Clear(); if (errors.Count == 0 && warnings.Count == 0) LogRaw("OK: Keine offensichtlichen Fehler."); else { foreach (var e in errors) LogRaw("FEHLER: " + e); foreach (var w in warnings) LogRaw("WARNUNG: " + w); } return errors.Count == 0;
    }

    private void FormatJson_Click(object sender, RoutedEventArgs e) { var json = ReadRawJsonObject(); if (json is null) return; RawJsonBox.Text = json.ToJsonString(_jsonOptions); Log("JSON formatiert."); }
    private void Save_Click(object sender, RoutedEventArgs e) { var json = ReadRawJsonObject(); if (json is null) return; if (_currentScript is null) { SaveAs_Click(sender, e); return; } File.WriteAllText(_currentScript.Path, json.ToJsonString(_jsonOptions)); Log($"Gespeichert: {_currentScript.Path}"); LoadFolder(); }
    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var json = ReadRawJsonObject(); if (json is null) return; var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*", FileName = $"{GetString(json, "id", "new_script")}.json", InitialDirectory = _folder };
        if (dlg.ShowDialog() == true) { File.WriteAllText(dlg.FileName, json.ToJsonString(_jsonOptions)); _currentScript = new ScriptFile(dlg.FileName, GetString(json, "id"), GetString(json, "name"), GetString(json, "mode"), GetBool(json, "enabled", true)); TitleText.Text = _currentScript.DisplayName; FileText.Text = _currentScript.Path; Log($"Gespeichert: {dlg.FileName}"); if (_folder is null) _folder = Path.GetDirectoryName(dlg.FileName); FolderText.Text = _folder ?? ""; LoadFolder(); }
    }

    private JsonObject? ReadRawJsonObject() { try { return JsonNode.Parse(RawJsonBox.Text) as JsonObject; } catch (Exception ex) { Log($"JSON Fehler: {ex.Message}"); return null; } }
    private static string GetString(JsonObject obj, string name, string fallback = "") { try { return obj[name]?.GetValue<string>() ?? fallback; } catch { return fallback; } }
    private static bool GetBool(JsonObject obj, string name, bool fallback) { try { return obj[name]?.GetValue<bool>() ?? fallback; } catch { return fallback; } }
    private static int GetInt(JsonObject obj, string name, int fallback) { try { return obj[name]?.GetValue<int>() ?? fallback; } catch { return fallback; } }
    private static double GetDouble(JsonObject obj, string name, double fallback) { try { return obj[name]?.GetValue<double>() ?? fallback; } catch { return fallback; } }
    private static string GetNumberString(JsonObject obj, string name) => obj[name]?.ToJsonString()?.Trim('"') ?? "";
    private static void Require(JsonObject obj, string name, List<string> errors) { if (string.IsNullOrWhiteSpace(GetString(obj, name))) errors.Add($"{name} fehlt oder ist leer."); }
    private static void SetCombo(System.Windows.Controls.ComboBox box, string value) { foreach (var item in box.Items.OfType<ComboBoxItem>()) { if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; } } box.Text = value; }
    private static string GetComboText(System.Windows.Controls.ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? box.Text ?? "";
    private static void SetOrRemove(JsonObject obj, string name, string value) { if (string.IsNullOrWhiteSpace(value)) obj.Remove(name); else obj[name] = value; }
    private static void SetIntOrRemove(JsonObject obj, string name, string value) { if (string.IsNullOrWhiteSpace(value)) { obj.Remove(name); return; } if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) obj[name] = i; }
    private static double ParseDoubleOrZero(string value) { if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d; return 0; }
    private void Log(string message) => LogRaw($"[{DateTime.Now:HH:mm:ss}] {message}");
    private void LogRaw(string message) { LogBox.AppendText(message + Environment.NewLine); LogBox.ScrollToEnd(); }
}

public sealed record ScriptFile(string Path, string Id, string Name, string Mode, bool Enabled) { public string DisplayName => $"{(Enabled ? "●" : "○")} {Name} [{Mode}]"; }


public sealed class SimpleNpcSpawnModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string SpawnType { get; set; } = "ArmedNPC";
    public string NpcType { get; set; } = "BP_Guard_Lvl_5";
    public int Count { get; set; } = 1;
    public string Location { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SimpleLootSpotModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SimpleLootPackModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int Weight { get; set; } = 1;
    public ObservableCollection<LootItemEditorModel> Items { get; } = new();

    public string Signature => $"{Name}|{Weight}|{string.Join(",", Items.Select(i => $"{i.Item}:{i.Quantity}"))}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LootPackEditorModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int Weight { get; set; } = 1;
    public string ChestType { get; set; } = "Improved_Wooden_Chest";
    public string Location { get; set; } = "";
    public int DelayMs { get; set; } = 250;
    public ObservableCollection<LootItemEditorModel> Items { get; } = new();

    public string DisplayName => $"{(Enabled ? "●" : "○")} {Name}  | Gewicht {Weight}";

    public LootPackEditorModel Clone()
    {
        var clone = new LootPackEditorModel
        {
            Name = Name,
            Enabled = Enabled,
            Weight = Weight,
            ChestType = ChestType,
            Location = Location,
            DelayMs = DelayMs
        };

        foreach (var item in Items)
            clone.Items.Add(new LootItemEditorModel { Item = item.Item, Quantity = item.Quantity, DelayMs = item.DelayMs });

        return clone;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LootItemEditorModel : INotifyPropertyChanged
{
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int DelayMs { get; set; } = 250;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ScriptCommand : INotifyPropertyChanged { public bool Enabled { get; set; } = true; public string? Name { get; set; } public string? Command { get; set; } public int Repeat { get; set; } = 1; public int DelayMs { get; set; } = 250; public event PropertyChangedEventHandler? PropertyChanged; }

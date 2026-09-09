using System.Windows;
using ScumRconTool.Services;
using ScumRconTool.Views;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private readonly WeaponCompatibilityCatalog _weaponCompatibilityCatalog = new();

    private void SelectChallengeLootPack(WeeklyTaskEditorViewModel? task)
    {
        if (task is null) return;
        var selected = LootPackSelectionDialog.Pick(GlobalLootPacks, task.RewardLootPackNames, allowMultiple: true, Texts.IsGerman);
        if (selected is null) return;
        task.RewardLootPackNames.Clear();
        foreach (var packName in selected) task.RewardLootPackNames.Add(packName);
    }

    private void SelectScriptLootPacks()
    {
        if (ScriptEditorModel is null) return;
        var selected = LootPackSelectionDialog.Pick(GlobalLootPacks, ScriptEditorModel.LootPackNames, allowMultiple: true, Texts.IsGerman);
        if (selected is null) return;

        ScriptEditorModel.LootPackNames.Clear();
        foreach (var name in selected) ScriptEditorModel.LootPackNames.Add(name);
        ScriptEditorModel.RebuildFlow();
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void AddMatchingWeaponAccessories(LootPackEditorViewModel? pack)
    {
        if (pack is null) return;
        var weapons = _weaponCompatibilityCatalog.FindWeapons(pack.Items.Select(item => item.Item));
        if (weapons.Count == 0)
        {
            MessageBox.Show(
                Texts.IsGerman
                    ? "In diesem Lootpack wurde keine Waffe aus der Kompatibilitätsmatrix gefunden. Füge zuerst die Waffe hinzu."
                    : "No weapon from the compatibility matrix was found in this loot pack. Add the weapon first.",
                Texts["MatchingAccessories"], MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = WeaponAccessoryDialog.Pick(weapons, pack.Items.Select(item => item.Item), Texts.IsGerman);
        if (selected is null) return;

        foreach (var choice in selected)
        {
            if (pack.Items.Any(item => string.Equals(item.Item, choice.ItemId, StringComparison.OrdinalIgnoreCase))) continue;
            pack.Items.Add(new LootItemEditorViewModel { Item = choice.ItemId, Quantity = choice.Quantity, DelayMs = 50 });
        }

        pack.Category = "Weapons";
    }
}

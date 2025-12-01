using Dalamud.Interface;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

public class SimpleCurrencyAlertConfig {
    public List<SimpleCurrencyAlert.Alert> Alerts = [];
}

[Tweak]
public class SimpleCurrencyAlert : Tweak<SimpleCurrencyAlertConfig> {
    public override string Name => "Simple Currency Alert";
    public override string Description => "Probably won't reset your config every update. Triggers on zone change.";

    public class Alert {
        public uint ItemId;
        public int Threshold;
        public Level Level;
        public ushort Icon => GetRow<Item>(ItemId)?.Icon ?? 0;
        public string Name => GetRow<Item>(ItemId)?.Name.ToString() ?? string.Empty;
    }

    public enum Level {
        Over,
        Under,
    }

    public override void DrawConfig() {
        base.DrawConfig();
        if (ImGuiEx.ExcelSheetCombo<Item>("##Search", out var item, _ => string.Empty, x => x.Name.ToString(), x => !x.Name.ToString().IsNullOrEmpty()))
            Config.Alerts.Add(new Alert() { ItemId = item.RowId });

        foreach (var i in Config.Alerts.ToList()) {
            ImGui.Icon(i.Icon, 25);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.InputInt($"Threshold##{i.ItemId}", ref i.Threshold, 0);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGuiEx.EnumCombo($"##Level{i.ItemId}", ref i.Level);
            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.Trash, $"##Trash{i.ItemId}"))
                Config.Alerts.Remove(i);
        }
    }

    public override void Enable() => Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    public override void Disable() => Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
    private unsafe void OnTerritoryChanged(ushort obj) {
        foreach (var currency in Config.Alerts)
            if (currency.Level == Level.Over && InventoryManager.Instance()->GetInventoryItemCount(currency.ItemId) >= currency.Threshold
                || currency.Level == Level.Under && InventoryManager.Instance()->GetInventoryItemCount(currency.ItemId) <= currency.Threshold)
                ModuleMessage($"{currency.Name} {(currency.Level == Level.Over ? "above" : "under")} threshold");
    }
}

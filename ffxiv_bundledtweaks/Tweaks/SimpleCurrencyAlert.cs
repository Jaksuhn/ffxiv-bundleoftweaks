using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

public class SimpleCurrencyAlertConfig {
    public List<SimpleCurrencyAlert.Alert> Alerts = [];
}

[Tweak]
public class SimpleCurrencyAlert : Tweak<SimpleCurrencyAlertConfig> {
    public override string Name => "Simple Currency Alert";
    public override string Description => "Probably won't reset your config every update. Triggers on zone change.";

    public override void Enable() => Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    public override void Disable() => Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;

    private void OnTerritoryChanged(uint obj) {
        foreach (var currency in Config.Alerts) {
            var count = GetAlertCount(currency);

            if (currency.Level == Level.Over && count >= currency.Threshold || currency.Level == Level.Under && count <= currency.Threshold) {
                ModuleMessage($"{currency.Name} {(currency.Level == Level.Over ? "above" : "under")} threshold");
            }
        }
    }

    public override void DrawConfig() {
        var currencyItems = GetCurrencyItems();
        var popupItems = currencyItems.Select(c => (c.ItemId, c.DisplayName)).ToList();

        using var table = ImRaii.Table("CurrencyAlerts", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV);
        if (!table) return;

        ImGui.TableSetupColumn("Normal Items", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Rotating Currencies", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text("Normal Items");
        ImGui.TableNextColumn();
        ImGui.Text("Rotating Currencies");

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (ImGui.AddItemPopupButton(out var normalItem)) {
            Config.Alerts.Add(new Alert {
                ItemId = normalItem.Value.RowId,
                Type = SpecialCurrencyType.Item,
                LogicalId = normalItem.Value.RowId,
                Threshold = 0,
                Level = Level.Over,
            });
        }
        ImGui.TableNextColumn();
        if (ImGui.AddCustomItemPopupButton(out var specialItem, popupItems, "Add Currency")) {
            var selected = currencyItems.FirstOrDefault(c => c.ItemId == specialItem.Value.RowId);
            if (selected is not null) {
                Config.Alerts.Add(new Alert {
                    ItemId = selected.ItemId,
                    Type = selected.Type,
                    LogicalId = selected.LogicalId,
                    Threshold = 0,
                    Level = Level.Over,
                });
            }
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Spacing();
        ImGui.TableNextColumn();
        ImGui.Spacing();

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawAlertList(SpecialCurrencyType.Item);
        ImGui.TableNextColumn();
        DrawAlertList(SpecialCurrencyType.Tomestone, SpecialCurrencyType.SpecialBucket);
    }

    public enum SpecialCurrencyType {
        Item = 0,
        Tomestone = 1,
        SpecialBucket = 2,
    }

    public class Alert {
        public uint ItemId;
        public SpecialCurrencyType Type;
        public uint LogicalId; // Tomestones.RowId or special bucket ID

        public int Threshold;
        public Level Level;

        public unsafe uint CurrentItemId => Type switch {
            SpecialCurrencyType.Item => ItemId != 0 ? ItemId : LogicalId,
            SpecialCurrencyType.Tomestone => GetTomestoneItemId(LogicalId),
            SpecialCurrencyType.SpecialBucket => CurrencyManager.Instance()->GetItemIdBySpecialId((byte)LogicalId),
            _ => ItemId,
        };

        public ushort Icon => (ushort)(CurrentItemId != 0 ? GetRow<Item>(CurrentItemId)?.Icon ?? 0 : 0);
        public string Name => CurrentItemId != 0 ? GetRow<Item>(CurrentItemId)?.Name.ToString() ?? string.Empty : string.Empty;

        public string DisplayName => Type switch {
            SpecialCurrencyType.Tomestone => GetTomestoneDisplayName(LogicalId),
            SpecialCurrencyType.SpecialBucket => GetSpecialBucketDisplayName((byte)LogicalId),
            _ => Name,
        };
    }

    public enum Level {
        Over,
        Under,
    }

    private sealed record CurrencyEntry(SpecialCurrencyType Type, uint LogicalId, uint ItemId, string DisplayName);

    private static uint GetTomestoneItemId(uint tomestoneRowId)
        => Svc.Data.GetExcelSheet<TomestonesItem>().Where(t => t.Tomestones.IsValid && t.Tomestones.RowId == tomestoneRowId).Select(t => t.Item.RowId).FirstOrNull() ?? 0;

    private static string GetTomestoneDisplayName(uint tomestoneRowId) => tomestoneRowId switch {
        2 => "Unlimited Tomestone",
        3 => "Limited Tomestone",
        4 => "Discontinued Tomestone",
        _ => Item.GetRef(tomestoneRowId).Value.Name.ToString()
    };

    private static unsafe string GetSpecialBucketDisplayName(byte specialId) {
        return specialId switch {
            1 => "Discontinued Crafters' Scrip",
            2 => "Levelling Crafters' Scrip",
            3 => "Discontinued Gatherers' Scrip",
            4 => "Levelling Gatherers' Scrip",
            6 => "Capstone Crafters' Scrip",
            7 => "Capstone Gatherers' Scrip",
            _ => Item.GetRef(CurrencyManager.Instance()->GetItemIdBySpecialId(specialId)).Value.Name.ToString()
        };
    }

    private unsafe List<CurrencyEntry> GetCurrencyItems() {
        var items = new List<CurrencyEntry>();

        foreach (var tomestoneItem in Svc.Data.GetExcelSheet<TomestonesItem>().Where(r => r.Tomestones.IsValid && r.Tomestones.RowId > 1 && r.Item.RowId != 0)) {
            items.Add(new CurrencyEntry(SpecialCurrencyType.Tomestone, tomestoneItem.Tomestones.RowId, tomestoneItem.Item.RowId, GetTomestoneDisplayName(tomestoneItem.Tomestones.RowId)));
        }

        var currencyManager = CurrencyManager.Instance();
        if (currencyManager != null) {
            var specialIdNames = new Dictionary<byte, string> {
                { 1, "Discontinued Crafters' Scrip" },
                { 2, "Levelling Crafters' Scrip" },
                { 3, "Discontinued Gatherers' Scrip" },
                { 4, "Levelling Gatherers' Scrip" },
                { 6, "Capstone Crafters' Scrip" },
                { 7, "Capstone Gatherers' Scrip" },
            };

            foreach (var (specialId, displayName) in specialIdNames) {
                if (currencyManager->GetItemIdBySpecialId(specialId) is not 0 and var itemId)
                    items.Add(new CurrencyEntry(SpecialCurrencyType.SpecialBucket, specialId, itemId, displayName));
            }
        }

        return [.. items.OrderBy(i => i.ItemId)];
    }

    private unsafe int GetAlertCount(Alert alert) {
        if (alert.CurrentItemId == 0)
            return 0;

        return alert.Type == SpecialCurrencyType.SpecialBucket
            ? (int)CurrencyManager.Instance()->GetItemCount(alert.CurrentItemId)
            : InventoryManager.Instance()->GetInventoryItemCount(alert.CurrentItemId);
    }

    private void DrawAlertList(params SpecialCurrencyType[] types) {
        var alerts = Config.Alerts.Where(a => types.Contains(a.Type)).ToList();

        if (alerts.Count == 0) {
            ImGui.TextColored(Colors.Grey3, "No alerts configured");
            return;
        }

        var thresholdWidth = 50f;
        var totalFixedWidth = ImGui.IconUnitHeight() + thresholdWidth + ImGui.IconUnitWidth() * 2 + ImGui.GetStyle().ItemSpacing.X * 4 + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().WindowPadding.X * 2;
        var nameWidth = Math.Max(100f, ImGui.GetContentRegionAvail().X - totalFixedWidth);

        foreach (var (alert, idx) in alerts.WithIndex()) {
            using var id = ImRaii.PushId($"{alert.Type}_{alert.LogicalId}_{alert.ItemId}_{idx}");

            if (alert.Icon != 0) {
                if (Svc.Texture.GetFromGameIcon(new GameIconLookup { IconId = alert.Icon }).GetWrapOrDefault() is { Handle: var handle }) {
                    ImGui.Image(handle, new Vector2(ImGui.IconUnitHeight()));
                    ImGui.SameLine();
                }
            }

            var nameText = alert.DisplayName;
            var showTooltip = false;

            // truncate long names
            if (ImGui.CalcTextSize(alert.DisplayName).X > nameWidth) {
                var ellipsis = "...";
                var truncatedName = nameText;
                while (ImGui.CalcTextSize(truncatedName).X > (nameWidth - ImGui.CalcTextSize(ellipsis).X) && truncatedName.Length > 0) {
                    truncatedName = truncatedName[..^1];
                }
                nameText = truncatedName + ellipsis;
                showTooltip = true;
            }

            using (ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
            using (ImRaii.PushColor(ImGuiCol.Button, 0).Push(ImGuiCol.ButtonHovered, 0).Push(ImGuiCol.ButtonActive, 0)) {
                ImGui.Button(nameText, new Vector2(nameWidth, 0)); // Button does nothing, just for unform width
                ImGui.TooltipOnHover(showTooltip, alert.DisplayName);
            }
            ImGui.SameLine();

            ImGui.SetNextItemWidth(thresholdWidth);
            ImGui.InputInt($"##Threshold", ref alert.Threshold, 0);
            ImGui.SameLine();

            if (ImGuiComponents.IconButton($"##Level", alert.Level == Level.Over ? FontAwesomeIcon.ArrowUp : FontAwesomeIcon.ArrowDown))
                alert.Level = alert.Level == Level.Over ? Level.Under : Level.Over;
            ImGui.TooltipOnHover(alert.Level == Level.Over ? "Alert when above threshold" : "Alert when under threshold");
            ImGui.SameLine();

            if (ImGuiComponents.IconButton($"##Delete", FontAwesomeIcon.Trash))
                Config.Alerts.Remove(alert);
            ImGui.TooltipOnHover("Remove Alert");
        }
    }
}

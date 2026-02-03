using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.UI.Debug.Tabs;

internal unsafe class FlagTab : DebugTab {
    public override void Draw() {
        ImGui.TextUnformatted($"IsFlagMarkerSet: {AgentMap.Instance()->FlagMarkerCount > 0}");
        if (!(AgentMap.Instance()->FlagMarkerCount > 0)) return;

        ImGui.TextUnformatted($"Territory: {Player.MapFlag.TerritoryId} {GetRow<TerritoryType>(Player.MapFlag.TerritoryId)!.Value.Name}");
        var row = GetRow<Sheets.Map>(Player.MapFlag.MapId);
        if (row is { } map)
            ImGui.TextUnformatted($"[{map.RowId}] Size: {map.SizeFactor}, Offset: {map.OffsetX}, {map.OffsetY} Territory: {map.TerritoryType.Value.Name}");

        ImGui.TextUnformatted($"Map Position: {new Vector2(Player.MapFlag.XFloat, Player.MapFlag.YFloat)}");

        if (Svc.Navmesh.FlagToPoint() is not { } pos) return;
        ImGui.TextUnformatted($"World Position: {pos}");

        var territory = Player.MapFlag.TerritoryId;
        var closest = Coords.FindClosestAetheryte(territory, pos);
        var aetherytes = FindRows<Aetheryte>(x => x.Territory.RowId == territory).OrderBy(a => (pos - Coords.AetherytePosition(a)).LengthSquared());

        foreach (var aetheryte in aetherytes) {
            ImGui.TextUnformatted($"[{aetheryte.RowId}]");
            ImGui.Indent();
            ImGui.TextUnformatted($"PlaceName: {aetheryte.PlaceName.Value.Name}");
            ImGui.TextUnformatted($"AethernetName: {aetheryte.AethernetName.Value.Name}");
            ImGui.TextUnformatted($"Position: {Coords.AetherytePosition(aetheryte)}");
            ImGui.TextUnformatted($"Dist: {(pos - Coords.AetherytePosition(aetheryte)).LengthSquared()}");
            ImGui.Unindent();
        }
    }
}

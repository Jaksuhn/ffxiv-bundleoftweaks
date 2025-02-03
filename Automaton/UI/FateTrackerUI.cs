using Automaton.Features;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;

namespace Automaton.UI;
internal class FateTrackerUI(DateWithDestiny tweak) : Window($"Fate Tracker##{nameof(FateTrackerUI)}")
{
    private readonly DateWithDestiny _tweak = tweak;
    internal uint SelectedTerritory = 0;

    public override bool DrawConditions() => Player.Available;

    public override void Draw()
    {
        ImGui.TextUnformatted($"Status: {(_tweak.active ? "on" : "off")} (Yo-Kai: {(_tweak.Config.YokaiMode ? "on" : "off")})");
        if (ImGuiComponents.IconButton(!_tweak.active ? FontAwesomeIcon.Play : FontAwesomeIcon.Stop))
        {
            _tweak.active ^= true;
            Service.Navmesh.Stop();
        }

        using var table = ImRaii.Table("Fates", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.NoHostExtendX);
        if (!table)
            return;

        foreach (var fate in Svc.Fates.OrderBy(x => Vector3.Distance(x.Position, Player.Position)))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            if (ImGuiComponents.IconButton($"###Pathfind{fate.FateId}", FontAwesomeIcon.Map))
            {
                if (!Service.Navmesh.IsRunning())
                    Service.Navmesh.PathfindAndMoveTo(_tweak.GetRandomPointInFate(fate.FateId), Svc.Condition[ConditionFlag.InFlight]);
                else
                    Service.Navmesh.Stop();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Pathfind to {fate.Position}");

            ImGui.SameLine();

            if (ImGuiComponents.IconButton($"###Flag{fate.FateId}", FontAwesomeIcon.Flag))
            {
                unsafe { AgentMap.Instance()->SetFlagMapMarker(Svc.ClientState.TerritoryType, Svc.ClientState.MapId, fate.Position); }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Set map flag to {fate.Position}");

            ImGui.SameLine();

            if (_tweak.Config.ShowFateBonusIndicator && fate.HasBonus)
            {
                ImGui.Image(Svc.Texture.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(65001)).GetWrapOrEmpty().ImGuiHandle, new Vector2(ImGuiX.IconUnitHeight()));

                ImGui.SameLine();
            }

            var nameColour = _tweak.FateConditions(fate) ? new Vector4(1, 1, 1, 1) : _tweak.Config.blacklist.Contains(fate.FateId) ? new Vector4(1, 0, 0, 0.5f) : new Vector4(1, 1, 1, 0.5f);
            ImGuiEx.TextV(nameColour, $"{fate.Name} {(_tweak.Config.ShowFateTimeRemaining && fate.TimeRemaining >= 0 ? new TimeSpan(0, 0, (int)fate.TimeRemaining) : string.Empty)}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{fate.Stringify()}\nDistance to: {Player.DistanceTo(fate.Position)}\nFate {(_tweak.FateConditions(fate) ? "meets" : "doesn't meet")} conditions and {(_tweak.FateConditions(fate) ? "will" : "won't")} be pathed to in auto mode.");

            ImGui.TableNextColumn();

            ImGuiX.DrawProgressBar(fate.Progress, 100, new Vector4(0.404f, 0.259f, 0.541f, 1));

            ImGui.SameLine();

            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGuiX.IconUnitWidth() - ImGui.GetStyle().WindowPadding.X);
            if (ImGuiComponents.IconButton($"###Blacklist{fate.FateId}", FontAwesomeIcon.Ban))
            {
                _tweak.Config.blacklist.Add(fate.FateId);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Add to blacklist. Right click to remove.");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _tweak.Config.blacklist.Remove(fate.FateId);
            }
        }
    }
}

using Dalamud.Game.Text;
using Dalamud.Utility;
using ECommons.Automation;
using ECommons.Configuration;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using System.IO;

namespace Automaton.UI.Debug.Tabs;
internal unsafe class TestTab : DebugTab
{
    public override void Draw()
    {
        var x = *((byte*)InfoProxyNoviceNetwork.Instance() + 0x18);
        ImGuiEx.Text($"nn: {x} {x >> 8}");

        ImGuiEx.Text($"{Path.Combine(Svc.PluginInterface.ConfigDirectory.Parent?.GetDirectories("SomethingNeedDoing").FirstOrDefault()?.FullName ?? throw new Exception(), EzConfig.DefaultSerializationFactory.DefaultConfigFileName)}");

        if (TryGetAddonByName<AtkUnitBase>("SelectYesno", out var addon))
        {
            var atk = addon->AtkValues[15].String;
            ImGuiEx.Text($"ToString: {atk}");
            ImGuiEx.Text($"SeString: {atk.AsReadOnlySeString().GetText()}");
            ImGuiEx.Text($"Dalamud: {atk.AsDalamudSeString().GetText()}");
            ImGuiEx.Text($"Replaced: {Enum.GetValues(typeof(SeIconChar))
                .Cast<SeIconChar>()
                .Aggregate(atk.AsDalamudSeString().GetText(), (current, enumValue) =>
                    current.Replace(enumValue.ToIconString(), ""))}");
        }
    }

    private static uint green = 0xFF0000;
    private static uint AppendAlpha(uint col) => (col & 0xFFFFFF) == col ? (col << 8) | 0xFF : col;
}

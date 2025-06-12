using ECommons.Automation;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using ImGuiNET;

namespace Automaton.UI.Debug.Tabs;
internal unsafe class TestTab : DebugTab
{
    public override void Draw()
    {
        var x = *((byte*)InfoProxyNoviceNetwork.Instance() + 0x18);
        ImGuiEx.Text($"nn: {x} {x >> 8}");
    }

    private static uint green = 0xFF0000;
    private static uint AppendAlpha(uint col) => (col & 0xFFFFFF) == col ? (col << 8) | 0xFF : col;
}

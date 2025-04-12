using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace Automaton.UI.Debug.Tabs;
internal unsafe class TestTab : DebugTab
{
    public override void Draw()
    {
        var x = *((byte*)InfoProxyNoviceNetwork.Instance() + 0x18);
        ImGuiEx.Text($"nn: {x} {x >> 8}");
    }
}

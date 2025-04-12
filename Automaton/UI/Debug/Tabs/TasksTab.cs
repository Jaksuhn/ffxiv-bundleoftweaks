using Automaton.Tasks;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;

namespace Automaton.UI.Debug.Tabs;
internal unsafe class TasksTab : DebugTab
{
    public override void Draw()
    {
        using (ImRaii.Disabled(!Service.Automation.Running))
            if (ImGui.Button("Stop current task"))
                Service.Automation.Stop();
        ImGui.TextUnformatted($"{Service.Automation.Name}: {Service.Automation.Status}");

        if (ImGui.Button("deliveroo"))
            Service.Automation.Start(new AutoDeliveroo(C.Tweaks.ARTurnIn));

        if (ImGui.Button($"dwd"))
        {
            Service.Automation.Start(new FateGrind(C.Tweaks.DateWithDestiny));
        }

        if (Service.Automation.CurrentTask is FateGrind fg)
        {
            foreach (var fate in fg.AvailableFates)
            {
                fate.Print();
            }
        }
    }
}

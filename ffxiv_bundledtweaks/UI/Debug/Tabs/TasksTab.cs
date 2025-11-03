using ComplexTweaks.Tasks;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Dalamud.Bindings.ImGui;

namespace ComplexTweaks.UI.Debug.Tabs;
internal unsafe class TasksTab : DebugTab
{
    public override void Draw()
    {
        using (ImRaii.Disabled(!Service.Automation.Running))
            if (ImGui.Button("Stop current task"))
                Service.Automation.Stop();
        ImGui.TextUnformatted($"{Service.Automation.Name}: {Service.Automation.Status}");

        if (ImGui.Button("transmute"))
            Service.Automation.Start(new MateriaTransmutation());

        if (ImGui.Button($"dwd"))
        {
            Service.Automation.Start(new FateGrind(C.Tweaks.DateWithDestiny));
        }

        if (ImGui.Button("void all weeaboos"))
            Service.Automation.Start(new VoidMatches("weeaboo"));

        if (Service.Automation.CurrentTask is FateGrind fg)
        {
            foreach (var fate in fg.AvailableFates)
            {
                fate.Print();
            }
        }

        var mt = UIState.Instance()->MateriaTrade;
        ImGui.TextUnformatted($"Materia 1: {mt.MateriaId1}-{mt.Quantity1}");
        ImGui.TextUnformatted($"Materia 2: {mt.MateriaId2}-{mt.Quantity2}");
        ImGui.TextUnformatted($"Materia 3: {mt.MateriaId3}-{mt.Quantity3}");
        ImGui.TextUnformatted($"Materia 4: {mt.MateriaId4}-{mt.Quantity4}");
        ImGui.TextUnformatted($"Materia 5: {mt.MateriaId5}-{mt.Quantity5}");
    }
}

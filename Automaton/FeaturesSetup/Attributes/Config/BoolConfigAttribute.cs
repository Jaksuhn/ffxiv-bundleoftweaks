using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using System.Reflection;

namespace Automaton.FeaturesSetup.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class BoolConfigAttribute : BaseConfigAttribute
{
    public override void Draw(Tweak tweak, object config, FieldInfo fieldInfo)
    {
        var value = (bool)fieldInfo.GetValue(config)!;
        var attr = fieldInfo.GetCustomAttribute<BaseConfigAttribute>();
        var cmdMethod = tweak.CachedType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(mi => mi.GetCustomAttribute<CommandHandlerAttribute>()?.ConfigFieldName == fieldInfo.Name);
        var cmdAttr = cmdMethod?.GetCustomAttribute<CommandHandlerAttribute>();
        var missingIpcs = Service.IPC.GetMissing(cmdMethod);
        var label = cmdAttr?.Commands.FirstOrDefault() ?? (!attr?.Label.IsNullOrEmpty() ?? false ? attr!.Label : fieldInfo.Name.SplitWords());

        if (missingIpcs.Length > 0 && !value)
        {
            using var disabled = ImRaii.Disabled(true);
            var checkboxValue = false;
            ImGui.Checkbox($"{label}##Input", ref checkboxValue);
        }
        else if (ImGui.Checkbox($"{label}##Input", ref value))
        {
            fieldInfo.SetValue(config, value);
            OnChangeInternal(tweak, fieldInfo);
        }

        if (missingIpcs.Length > 0)
        {
            ImGui.SameLine();
            ImGuiX.Icon(60074, 24);
        }

        DrawConfigInfos(fieldInfo);

        var desc = !cmdAttr?.HelpMessage.IsNullOrEmpty() ?? false ? cmdAttr!.HelpMessage : !attr?.Description.IsNullOrEmpty() ?? false ? attr!.Description : null;
        if (desc != null)
        {
            ImGuiEx.PushCursorY(-3);
            using var descriptionIndent = ImGuiX.ConfigIndent();
            ImGui.TextColoredWrapped(Colors.Grey, desc);
            ImGuiEx.PushCursorY(3);
        }

        if (missingIpcs.Length > 0)
        {
            using var warningIndent = ImGuiX.ConfigIndent();
            ImGuiEx.TextV(Colors.Grey2, $"Missing {missingIpcs.Length} of the required plugins for this command to work:");
            foreach (var entry in missingIpcs)
            {
                ImGui.TextColoredWrapped(Colors.Grey2, $"{entry.Name}:");
                ImGui.SameLine();
                ImGuiEx.TextCopy(entry.Repo);
            }
        }
    }
}

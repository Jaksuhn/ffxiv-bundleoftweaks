using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
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

        var label = cmdAttr?.Commands.FirstOrDefault() ?? (!attr?.Label.IsNullOrEmpty() ?? false ? attr!.Label : fieldInfo.Name.SplitWords());
        if (ImGui.Checkbox($"{label}##Input", ref value))
        {
            fieldInfo.SetValue(config, value);
            OnChangeInternal(tweak, fieldInfo);
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
    }
}

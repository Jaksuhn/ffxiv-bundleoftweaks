using Dalamud.Interface;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ComplexTweaks.FeaturesSetup.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class StringConfigAttribute : BaseConfigAttribute
{
    public string DefaultValue = string.Empty;
    public string IsRegex = string.Empty;

    public override void Draw(Tweak tweak, object config, FieldInfo fieldInfo)
    {
        var value = (string)fieldInfo.GetValue(config)!;
        var attr = fieldInfo.GetCustomAttribute<BaseConfigAttribute>();

        ImGui.TextUnformatted(fieldInfo.Name.SplitWords());

        if (ImGui.InputText("##Input", ref value, 500))
        {
            fieldInfo.SetValue(config, value);
            OnChangeInternal(tweak, fieldInfo);
        }

        if (DrawResetButton(DefaultValue))
        {
            fieldInfo.SetValue(config, DefaultValue);
            OnChangeInternal(tweak, fieldInfo);
        }

        // validate regex if IsRegex is set
        if (!string.IsNullOrEmpty(IsRegex) && !string.IsNullOrEmpty(value))
        {
            if (config.GetType().GetField(IsRegex) is { } field && field.GetValue(config) is bool b && b)
            {
                try
                {
                    _ = new Regex(value);
                    ImGui.SameLine();
                    ImGuiX.Icon(FontAwesomeIcon.Check, EzColor.Green, "Valid regex pattern");
                }
                catch (ArgumentException)
                {
                    ImGui.SameLine();
                    ImGuiX.Icon(FontAwesomeIcon.Ban, EzColor.Red, "Invalid regex pattern");
                }
            }
        }

        if (!attr?.Description.IsNullOrEmpty() ?? false)
            ImGui.TextColoredWrapped(Colors.Grey, attr!.Description);
    }
}

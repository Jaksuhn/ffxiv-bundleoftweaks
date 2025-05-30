using Dalamud.Interface.Components;
using ECommons.ImGuiMethods;
using ImGuiNET;
using System.Reflection;

namespace Automaton.FeaturesSetup.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class ColorConfigAttribute : BaseConfigAttribute
{
    public Vector4 DefaultValue = Vector4.One;
    public ImGuiColorEditFlags Flags = ImGuiColorEditFlags.NoAlpha;

    public override void Draw(Tweak tweak, object config, FieldInfo fieldInfo)
    {
        var value = (Vector4)fieldInfo.GetValue(config)!;
        var attr = fieldInfo.GetCustomAttribute<BaseConfigAttribute>();

        ImGuiEx.TextV(fieldInfo.Name.SplitWords());
        ImGui.SameLine();

        var newColor = ImGuiComponents.ColorPickerWithPalette(1, $"##{fieldInfo.Name}", value, Flags);
        if (!value.Equals(newColor))
        {
            fieldInfo.SetValue(config, newColor);
            OnChangeInternal(tweak, fieldInfo);
        }

        if (DrawResetButton(DefaultValue.ToString()))
        {
            fieldInfo.SetValue(config, DefaultValue);
            OnChangeInternal(tweak, fieldInfo);
        }

        DrawConfigInfos(fieldInfo);

        if (!attr?.Description.IsNullOrEmpty() ?? false)
            ImGuiHelpers.SafeTextColoredWrapped(Colors.Grey, attr!.Description);
    }
}

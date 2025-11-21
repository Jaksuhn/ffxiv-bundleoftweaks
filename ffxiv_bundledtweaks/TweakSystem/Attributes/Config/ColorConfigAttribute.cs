using Dalamud.Interface.Components;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System.Reflection;

namespace ComplexTweaks.TweakSystem.Attributes;

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
            ImGui.TextColoredWrapped(Colors.Grey, attr!.Description);
    }
}

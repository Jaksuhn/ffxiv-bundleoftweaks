using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System.Globalization;
using System.Reflection;

namespace ComplexTweaks.Attributes.Config;

[AttributeUsage(AttributeTargets.Field)]
public class IntConfigAttribute : BaseConfigAttribute {
    public int DefaultValue = 0;
    public int Min = 0;
    public int Max = 100;
    public bool SameLine = false;

    public override void Draw(Tweak tweak, object config, FieldInfo fieldInfo) {
        var value = (int)fieldInfo.GetValue(config)!;
        var attr = fieldInfo.GetCustomAttribute<BaseConfigAttribute>();

        ImGuiEx.TextV(fieldInfo.Name.SplitWords());
        if (SameLine) ImGui.SameLine();

        using var indent = ImGui.ConfigIndent(!SameLine);

        if (ImGui.DragInt("##Input", ref value, 0.01f, Min, Max)) {
            fieldInfo.SetValue(config, value);
            OnChangeInternal(tweak, fieldInfo);
        }

        if (DrawResetButton(string.Format(CultureInfo.InvariantCulture, "{0:0.00}", DefaultValue))) {
            fieldInfo.SetValue(config, DefaultValue);
            OnChangeInternal(tweak, fieldInfo);
        }

        if (!attr?.Description.IsNullOrEmpty() ?? false)
            ImGui.TextColoredWrapped(Colors.Grey, attr!.Description);
    }
}

using Dalamud.Interface;
using Dalamud.Interface.Components;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System.Reflection;

namespace ComplexTweaks.TweakSystem.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class StringListConfigAttribute : BaseConfigAttribute
{
    private string _tempInput = string.Empty;

    public override void Draw(Tweak tweak, object config, FieldInfo fieldInfo)
    {
        var value = fieldInfo.GetValue(config)!;
        var attr = fieldInfo.GetCustomAttribute<BaseConfigAttribute>();

        ImGui.TextUnformatted(fieldInfo.Name.SplitWords());

        if (ImGui.InputText("##Input", ref _tempInput, 150, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (!string.IsNullOrWhiteSpace(_tempInput))
            {
                if (value is ICollection<string> collection)
                {
                    collection.Add(_tempInput);
                    _tempInput = string.Empty;
                    OnChangeInternal(tweak, fieldInfo);
                }
            }
        }

        if (value is ICollection<string> items && items.Count > 0)
        {
            ImGui.DrawSection("Items");
            foreach (var item in items.ToList())
            {
                ImGuiEx.TextV(item);
                ImGui.SameLine();
                if (ImGuiComponents.IconButton($"##{item}", FontAwesomeIcon.Trash))
                {
                    items.Remove(item);
                    OnChangeInternal(tweak, fieldInfo);
                }
            }
        }

        if (!attr?.Description.IsNullOrEmpty() ?? false)
            ImGui.TextColoredWrapped(Colors.Grey, attr!.Description);
    }
}

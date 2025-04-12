using Dalamud.Interface;
using ECommons.ImGuiMethods;
using ImGuiNET;
using System.Reflection;

namespace Automaton.FeaturesSetup.Attributes;

public abstract class BaseConfigAttribute : Attribute
{
    public string Label = string.Empty;
    public string Description = string.Empty;
    public string DependsOn = string.Empty;

    public abstract void Draw(Tweak tweak, object config, FieldInfo fieldInfo);

    protected void OnChangeInternal(Tweak tweak, FieldInfo fieldInfo)
    {
        //C.SaveConfiguration($"ez{tweak.Name}.json");
        tweak.CachedType.GetMethod(nameof(Tweak.OnConfigChangeInternal), BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(tweak, [fieldInfo.Name]);
    }

    protected static void DrawConfigInfos(FieldInfo fieldInfo)
    {
        var attributes = fieldInfo.GetCustomAttributes<ConfigInfoAttribute>();
        if (!attributes.Any())
            return;

        foreach (var attribute in attributes)
        {
            ImGui.SameLine();
            ImGuiX.Icon(attribute.Icon, attribute.Color);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(attribute.Description);
        }
    }

    protected static bool DrawResetButton(string defaultValueString)
    {
        if (string.IsNullOrEmpty(defaultValueString))
            return false;

        ImGui.SameLine();
        return ImGuiEx.IconButton(FontAwesomeIcon.Undo, "##Reset");
    }
}

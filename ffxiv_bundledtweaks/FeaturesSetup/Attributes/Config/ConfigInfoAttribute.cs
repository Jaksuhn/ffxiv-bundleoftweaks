using Dalamud.Interface;
using ECommons.ImGuiMethods;

namespace ComplexTweaks.FeaturesSetup.Attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class ConfigInfoAttribute(string label, string desc) : Attribute
{
    public string Label { get; init; } = label;
    public string Description { get; init; } = desc;
    public FontAwesomeIcon Icon { get; init; } = FontAwesomeIcon.InfoCircle;
    public EzColor Color { get; init; } = Colors.Grey;
}

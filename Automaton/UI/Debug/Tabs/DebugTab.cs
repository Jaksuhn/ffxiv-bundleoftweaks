using Dalamud.Interface.Textures;
using ImGuiNET;
using System.Text.RegularExpressions;

namespace Automaton.UI.Debug.Tabs;
public interface IDebugTab : IDrawableTab;

public interface IDrawableTab
{
    string Title { get; }
    string InternalName { get; }
    bool DrawInChild { get; }
    bool IsEnabled { get; }
    bool IsPinnable { get; }
    bool CanPopOut { get; }
    void Draw();
}

public abstract partial class DebugTab : IDebugTab
{
    private string? _title = null;
    public virtual string Title => _title ??= NameRegex().Replace(TabRegex().Replace(GetType().Name, ""), "$1 $2");
    public virtual bool IsEnabled => true;
    public virtual bool IsPinnable => true;
    public virtual bool CanPopOut => true;
    public virtual bool DrawInChild => true;
    public virtual string InternalName => GetType().Name;

    [GeneratedRegex("Tab$")]
    private static partial Regex TabRegex();

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex NameRegex();

    public virtual void Draw() { }

    public void DrawIcon(object value, Type? type = null, bool isHq = false, bool sameLine = true, Vector2? drawInfo = default, bool canCopy = true, bool noTooltip = false)
    {
        if (value == null)
        {
            DrawIcon(0, isHq, sameLine, drawInfo, canCopy, noTooltip);
            return;
        }

        var iconId = (type ?? value.GetType()) switch
        {
            Type t when t == typeof(short) => (short)value > 0 ? (uint)(short)value : 0u,
            Type t when t == typeof(ushort) => (ushort)value,
            Type t when t == typeof(int) => (int)value > 0 ? (uint)(int)value : 0u,
            Type t when t == typeof(uint) => (uint)value,
            _ => 0u
        };

        DrawIcon(iconId, isHq, sameLine, drawInfo, canCopy, noTooltip);
    }

    public void DrawIcon(uint iconId, bool isHq = false, bool sameLine = true, Vector2? drawInfo = default, bool canCopy = true, bool noTooltip = false)
    {
        drawInfo ??= new Vector2(ImGui.GetTextLineHeight());

        if (iconId == 0)
        {
            ImGui.Dummy(drawInfo.Value);
            if (sameLine)
                ImGui.SameLine();
            return;
        }

        if (!ImGui.IsRectVisible(drawInfo.Value))
        {
            ImGui.Dummy(drawInfo.Value);
            if (sameLine)
                ImGui.SameLine();
            return;
        }

        if (Svc.Texture.TryGetFromGameIcon(new GameIconLookup(iconId, isHq), out var tex) && tex.TryGetWrap(out var texture, out _))
        {
            ImGui.Image(texture.ImGuiHandle, drawInfo.Value);

            if (ImGui.IsItemHovered())
            {
                if (canCopy)
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (!noTooltip)
                {
                    ImGui.BeginTooltip();
                    if (canCopy)
                        ImGui.TextUnformatted("Click to copy IconId");
                    ImGui.TextUnformatted($"ID: {iconId} – Size: {texture.Width}x{texture.Height}");
                    ImGui.Image(texture.ImGuiHandle, new(texture.Width, texture.Height));
                    ImGui.EndTooltip();
                }
            }

            if (canCopy && ImGui.IsItemClicked())
                ImGui.SetClipboardText(iconId.ToString());
        }
        else
        {
            ImGui.Dummy(drawInfo.Value);
        }

        if (sameLine)
            ImGui.SameLine();
    }

    public bool Equals(IDebugTab? other) => other?.Title == _title;
}

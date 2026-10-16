using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Events.EventDataTypes;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

public class DTRWorldSwitcher : Tweak {
    public override string Name => "Server Bar World Switcher";
    public override string Description => "Adds a world switcher to the World entry in the server bar.";

    private const NodeFlags ClickableFlags = NodeFlags.Enabled | NodeFlags.HasCollision | NodeFlags.RespondToMouse | NodeFlags.EmitsEvents;

    private readonly List<IAddonEventHandle> _handles = [];
    private readonly List<(ushort Id, string Name)> _worlds = [];

    private nint _target;
    private NodeFlags _addedFlags;
    private bool _addedCursor;
    private bool _pendingOpen;
    private ushort _currentWorldId;
    private string _dataCenterName = string.Empty;

    public override void OnEnable() {
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PostDraw, "_DTR", SyncClickTarget);
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PostRequestedUpdate, "_DTR", SyncClickTarget);
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PreFinalize, "_DTR", OnDtrFinalize);
        Svc.Interface.UiBuilder.Draw += DrawWorldPopup;
    }

    public override unsafe void OnDisable() {
        IAddonLifecycle.Get().UnregisterListener(SyncClickTarget);
        IAddonLifecycle.Get().UnregisterListener(OnDtrFinalize);
        Svc.Interface.UiBuilder.Draw -= DrawWorldPopup;
        if (IGameGui.Get().TryGetAddon<AddonDtr>("_DTR", out var addon))
            RestoreNode(addon);
        ClearEvents();
        _pendingOpen = false;
    }

    private unsafe void SyncClickTarget(AddonEvent type, AddonArgs args) {
        var addon = args.GetAddon<AddonDtr>();
        var node = addon->WorldInfoContainer;
        if (!InStartingCity() || node == null || (_target != 0 && _target != (nint)node)) {
            if (node != null && _target == (nint)node)
                RestoreNode(addon);
            else
                ForgetNode();

            ClearEvents();
            if (!InStartingCity() || node == null)
                return;
        }

        var missing = ClickableFlags & ~node->NodeFlags;
        if (missing != 0) {
            node->NodeFlags |= missing;
            _addedFlags |= missing;
            addon->UpdateCollisionNodeList(false);
        }

        if (!node->IsClickableCursorOnHover) {
            node->IsClickableCursorOnHover = true;
            _addedCursor = true;
        }

        if (_handles.Count > 0)
            return;

        var addonPtr = (nint)addon;
        var nodePtr = (nint)node;
        AddHandle(addonPtr, nodePtr, AddonEventType.MouseOver);
        AddHandle(addonPtr, nodePtr, AddonEventType.MouseOut);
        AddHandle(addonPtr, nodePtr, AddonEventType.MouseClick);
        _target = nodePtr;
    }

    private void OnDtrFinalize(AddonEvent type, AddonArgs args) {
        ForgetNode();
        ClearEvents();
    }

    private void OnWorldMouse(AddonEventType type, AddonEventData data) {
        switch (type) {
            case AddonEventType.MouseOver:
                IAddonEventManager.Get().SetCursor(AddonCursorType.Clickable);
                break;
            case AddonEventType.MouseOut:
                IAddonEventManager.Get().ResetCursor();
                break;
            case AddonEventType.MouseClick when InStartingCity():
                _pendingOpen = true;
                break;
        }
    }

    private unsafe void DrawWorldPopup() {
        if (_pendingOpen) {
            _pendingOpen = false;
            if (InStartingCity() && RefreshWorlds())
                ImGui.OpenPopup("dtr_world_switcher");
        }

        var style = ImGui.GetStyle();
        using var color = ImRaii.PushColor(ImGuiCol.PopupBg, style.Colors[(int)ImGuiCol.WindowBg]);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, style.WindowRounding);
        using var border = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, style.WindowBorderSize);
        using var popup = ImRaii.Popup("dtr_world_switcher", ImGuiWindowFlags.NoDecoration);
        if (!popup)
            return;

        if (!_dataCenterName.IsEmpty) {
            ImGui.TextColored(Colors.Grey, _dataCenterName);
            ImGui.Separator();
        }

        foreach (var (id, name) in _worlds) {
            using (ImRaii.Disabled(id == _currentWorldId)) {
                if (ImGui.Selectable(name)) {
                    AgentWorldTravel.Instance()->Travel(id);
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }

    private bool RefreshWorlds() {
        if (IPlayerState.Get().CurrentWorld is not { IsValid: true, Value: var current })
            return false;

        _currentWorldId = (ushort)current.RowId;
        _dataCenterName = current.DataCenter.IsValid ? current.DataCenter.Value.Name.ToString() : string.Empty;
        _worlds.Clear();
        foreach (var world in World.Where(w => w.IsPublic && w.DataCenter.RowId == current.DataCenter.RowId)) {
            var name = world.Name.ToString();
            if (name.Length == 0)
                continue;
            _worlds.Add(((ushort)world.RowId, name));
        }

        _worlds.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return _worlds.Count > 0;
    }

    private static bool InStartingCity() => IClientState.Get().TerritoryType is 129 or 130 or 132;

    private void AddHandle(nint addon, nint node, AddonEventType type) {
        if (IAddonEventManager.Get().AddEvent(addon, node, type, OnWorldMouse) is { } handle)
            _handles.Add(handle);
    }

    private unsafe void RestoreNode(AddonDtr* addon) {
        var node = addon->WorldInfoContainer;
        if (_target == 0 || node == null || (nint)node != _target)
            return;

        var changed = false;
        if (_addedFlags != 0) {
            node->NodeFlags &= ~_addedFlags;
            _addedFlags = 0;
            changed = true;
        }

        if (_addedCursor) {
            node->IsClickableCursorOnHover = false;
            _addedCursor = false;
        }

        if (changed)
            addon->UpdateCollisionNodeList(false);
    }

    private void ForgetNode() {
        _target = 0;
        _addedFlags = 0;
        _addedCursor = false;
    }

    private void ClearEvents() {
        if (_handles.Count > 0) {
            var manager = IAddonEventManager.Get();
            foreach (var handle in _handles)
                manager.RemoveEvent(handle);
            _handles.Clear();
            manager.ResetCursor();
        }

        _target = 0;
    }
}

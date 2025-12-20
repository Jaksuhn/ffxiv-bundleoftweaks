using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using PlayerController = ComplexTweaks.Utilities.Structs.PlayerController;
#nullable disable

namespace ComplexTweaks.Utilities;

public static unsafe class PlayerEx {
    extension(Player) {
        public static unsafe Camera* Camera => CameraManager.Instance()->GetActiveCamera();
        public static PlayerController* Controller => (PlayerController*)Svc.SigScanner.GetStaticAddressFromSig(Memory.Signatures.PlayerController);

        public static float Speed { get => Player.Controller->MoveControllerWalk.BaseMovementSpeed; set => Player.Object.SetSpeed(6 * value); }
        public static byte ReviveState => Player.IsDead ? AgentRevive.Instance()->ReviveState : (byte)0;

        public static FlagMapMarker MapFlag => AgentMap.Instance()->FlagMapMarkers[0];
    }

    public static Vector3 Position { get => Svc.Objects.LocalPlayer.Position; set => Player.GameObject->SetPosition(value.X, value.Y, value.Z); }
    public static List<MapMarkerData> QuestLocations => [.. FFXIVClientStructs.FFXIV.Client.Game.UI.Map.Instance()->QuestMarkers.ToArray().SelectMany(i => i.MarkerData.ToList())];

    private static int EquipAttemptLoops = 0;
    public static void Equip(uint itemID, InventoryType? container = null, int? slot = null) {
        var item = container != null && slot != null ? new ItemHandle(new ItemLocation(container.Value, (ushort)slot)) : (ItemHandle)itemID;
        if (item.IsEquipped) return;
        if (!item.TrySetItemLocation()) {
            DuoLog.Error($"Failed to find item {item} in inventory");
            return;
        }

        var agentId = InventoryType.Armoury.Contains(item.ItemLocation.Container) ? AgentId.ArmouryBoard : AgentId.Inventory;
        var addonId = AgentModule.Instance()->GetAgentByInternalId(agentId)->GetAddonId();
        var ctx = AgentInventoryContext.Instance();
        ctx->OpenForItemSlot(item.ItemLocation.Container, item.ItemLocation.Slot, 0, addonId);

        var contextMenu = Svc.GameGui.GetAddonByName("ContextMenu").Struct();
        if (contextMenu != null) {
            for (var i = 0; i < contextMenu->AtkValuesCount; i++) {
                var firstEntryIsEquip = ctx->EventIds[i] == 25; // i'th entry will fire eventid 7+i; eventid 25 is 'equip'
                if (firstEntryIsEquip) {
                    Svc.Log.Info($"Equipping {item} from {item.ItemLocation}, index {i}");
                    Callback.Fire(contextMenu, true, 0, i - 7, 0, 0, 0); // p2=-1 is close, p2=0 is exec first command
                }
            }
            Callback.Fire(contextMenu, true, 0, -1, 0, 0, 0);
            EquipAttemptLoops++;

            if (EquipAttemptLoops >= 5) {
                DuoLog.Error($"Equip option not found after 5 attempts. Aborting.");
                return;
            }
        }
    }
}

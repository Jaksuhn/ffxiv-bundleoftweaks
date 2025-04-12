using Automaton.Utilities.Movement;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using PlayerController = Automaton.Utilities.Structs.PlayerController;
#nullable disable

namespace Automaton.Utilities;

public static unsafe class PlayerEx
{
    public static Character* Character => (Character*)Svc.ClientState.LocalPlayer.Address;
    public static BattleChara* BattleChara => (BattleChara*)Svc.ClientState.LocalPlayer.Address;
    public static CSGameObject* GameObject => (CSGameObject*)Svc.ClientState.LocalPlayer.Address;

    public static PlayerController* Controller => (PlayerController*)Svc.SigScanner.GetStaticAddressFromSig(Memory.Signatures.PlayerController);
    public static bool HasPenalty => FFXIVClientStructs.FFXIV.Client.Game.UI.InstanceContent.Instance()->GetPenaltyRemainingInMinutes(0) > 0;
    public static bool InFlightAllowedTerritory => GetRow<TerritoryType>(Svc.ClientState.TerritoryType)?.AetherCurrentCompFlgSet.RowId != 0;
    public static bool AllowedToFly => PlayerState.Instance()->IsAetherCurrentZoneComplete(Svc.ClientState.TerritoryType);
    public static Vector3 Position { get => Svc.ClientState.LocalPlayer.Position; set => GameObject->SetPosition(value.X, value.Y, value.Z); }
    public static float Speed { get => Controller->MoveControllerWalk.BaseMovementSpeed; set => Memory.SetSpeed(6 * value); }
    public static DGameObject Target { get => Svc.Targets.Target; set => Svc.Targets.Target = value; }
    public static bool IsTargetLocked => *(byte*)((nint)TargetSystem.Instance() + 309) == 1;
    public static HaterInfo[] Haters => UIState.Instance()->Hater.Haters.ToArray();
    public static int HatersWithFullAggro => Haters.Where(h => h.Enmity == 100).Count();

    public static FlagMapMarker MapFlag => AgentMap.Instance()->FlagMapMarker;

    public static unsafe Camera* Camera => CameraManager.Instance()->GetActiveCamera();
    public static unsafe CameraEx* CameraEx => (CameraEx*)CameraManager.Instance()->GetActiveCamera();

    public static List<MapMarkerData> QuestLocations => FFXIVClientStructs.FFXIV.Client.Game.UI.Map.Instance()->QuestMarkers.ToArray().SelectMany(i => i.MarkerData.ToList()).ToList();

    private static int EquipAttemptLoops = 0;
    public static void Equip(uint itemID, InventoryType? container = null, int? slot = null)
    {
        if (Inventory.HasItemEquipped(itemID)) return;

        var pos = Inventory.GetItemLocationInInventory(itemID, Inventory.Equippable);
        if (pos == null)
        {
            DuoLog.Error($"Failed to find item {GetRow<Item>(itemID)?.Name} (ID: {itemID}) in inventory");
            return;
        }

        container ??= pos.Value.inv;
        slot ??= pos.Value.slot;

        var agentId = Inventory.Armory.Contains(container.Value) ? AgentId.ArmouryBoard : AgentId.Inventory;
        var addonId = AgentModule.Instance()->GetAgentByInternalId(agentId)->GetAddonId();
        var ctx = AgentInventoryContext.Instance();
        ctx->OpenForItemSlot(container.Value, slot.Value, addonId);

        var contextMenu = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextMenu");
        if (contextMenu != null)
        {
            for (var i = 0; i < contextMenu->AtkValuesCount; i++)
            {
                var firstEntryIsEquip = ctx->EventIds[i] == 25; // i'th entry will fire eventid 7+i; eventid 25 is 'equip'
                if (firstEntryIsEquip)
                {
                    Svc.Log.Info($"Equipping item #{itemID} from {container.Value} @ {slot.Value}, index {i}");
                    Callback.Fire(contextMenu, true, 0, i - 7, 0, 0, 0); // p2=-1 is close, p2=0 is exec first command
                }
            }
            Callback.Fire(contextMenu, true, 0, -1, 0, 0, 0);
            EquipAttemptLoops++;

            if (EquipAttemptLoops >= 5)
            {
                DuoLog.Error($"Equip option not found after 5 attempts. Aborting.");
                return;
            }
        }
    }

    public static void ResetTimers()
    {
        var module = UIModule.Instance()->GetInputTimerModule();
        module->AfkTimer = 0;
        module->ContentInputTimer = 0;
        module->InputTimer = 0;
        module->Unk1C = 0;
        if (Player.OnlineStatus == 17) // away from keyboard
            Chat.Instance.SendMessage("/afk off"); // TODO: find a better way
    }

    public static bool InteractWith(ulong instanceId)
    {
        var obj = GameObjectManager.Instance()->Objects.GetObjectByGameObjectId(instanceId);
        if (obj == null)
            return false;
        TargetSystem.Instance()->InteractWithObject(obj, false);
        return true;
    }

    public static void Mount() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 24);
}

using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;

namespace Automaton.Utilities;
public unsafe class Game
{
    public static AtkUnitBase* GetAddonByName(string name) => RaptureAtkUnitManager.Instance()->GetAddonByName(name);
    public static bool AddonActive(string name) => AddonActive(GetAddonByName(name));
    public static bool AddonActive(AtkUnitBase* addon) => addon != null && addon->IsVisible && addon->IsReady;

    public static void ProgressTalk()
    {
        var addon = GetAddonByName("Talk");
        if (addon != null && addon->IsReady)
        {
            var evt = new AtkEvent() { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
            var data = new AtkEventData();
            addon->ReceiveEvent(AtkEventType.MouseClick, 0, &evt, &data);
        }
    }

    public static void SelectYes()
    {
        if (TryGetAddonByName<AtkUnitBase>("SelectYesno", out var addon))
        {
            var evt = new AtkEvent() { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
            var data = new AtkEventData();
            addon->ReceiveEvent(AtkEventType.ButtonClick, 0, &evt, &data);
        }
    }

    public static void SelectString(int index)
    {
        if (TryGetAddonByName<AtkUnitBase>("SelectString", out var addon))
        {
            AtkValue val = default;
            val.SetInt(index);
            addon->FireCallback(1, &val, true);
        }
    }

    public static void TeleportToAethernet(uint currentAetheryte, uint destinationAetheryte)
    {
        Span<uint> payload = [4, destinationAetheryte];
        PacketDispatcher.SendEventCompletePacket(0x50000 | currentAetheryte, 0, 0, payload.GetPointer(0), (byte)payload.Length, null);
    }

    public static void TeleportToFirmament(uint currentAetheryte)
    {
        Span<uint> payload = [9];
        PacketDispatcher.SendEventCompletePacket(0x50000 | currentAetheryte, 0, 0, payload.GetPointer(0), (byte)payload.Length, null);
    }

    public enum ShopType
    {
        None = 0,
        GilShop = 1,
        FreeCompanyCreditShop = 2
    }

    public static bool IsShopOpen(uint shopId = 0, ShopType shopType = ShopType.None)
    {
        AgentInterface* agent = null;
        switch (shopType)
        {
            case ShopType.GilShop:
                agent = &AgentShop.Instance()->AgentInterface;
                break;
            case ShopType.FreeCompanyCreditShop:
                agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.FreeCompanyCreditShop);
                break;
        }
        if (agent == null || !agent->IsAgentActive() || &agent->AtkEventInterface == null || !agent->IsAddonReady())
            return false;
        if (shopId == 0 || shopType == ShopType.None)
            return true; // some shop is open...
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
            return false;
        var proxy = (ShopEventHandler.AgentProxy*)&agent->AtkEventInterface;
        return proxy->Handler == eh->Value;
    }

    public static bool OpenShop(GameObject* vendor, uint shopId)
    {
        PluginLog.Debug($"Interacting with {(ulong)vendor->GetGameObjectId():X}");
        TargetSystem.Instance()->InteractWithObject(vendor, false);
        var selector = EventHandlerSelector.Instance();
        if (selector->Target == null)
            return true; // assume interaction was successful without selector

        if (selector->Target != vendor)
        {
            PluginLog.Error($"Unexpected selector target {(ulong)selector->Target->GetGameObjectId():X} when trying to interact with {(ulong)vendor->GetGameObjectId():X}");
            return false;
        }

        for (var i = 0; i < selector->OptionsCount; ++i)
        {
            if (selector->Options[i].Handler->Info.EventId.Id == shopId)
            {
                PluginLog.Debug($"Selecting selector option {i} for shop {shopId:X}");
                EventFramework.Instance()->InteractWithHandlerFromSelector(i);
                return true;
            }
        }

        PluginLog.Error($"Failed to find shop {shopId:X} in selector for {(ulong)vendor->GetGameObjectId():X}");
        return false;
    }

    public static bool OpenShop(ulong vendorInstanceId, uint shopId)
    {
        if (Svc.Objects.TryGetFirst(o => o.DataId == vendorInstanceId, out var vendor))
            return OpenShop(vendor.Struct(), shopId);
        else
        {
            PluginLog.Error($"Failed to find vendor {vendorInstanceId:X}");
            return false;
        }
    }

    public static bool CloseShop()
    {
        var agent = AgentShop.Instance();
        if (agent == null || agent->EventReceiver == null)
            return false;
        AtkValue res = default, arg = default;
        var proxy = (ShopEventHandler.AgentProxy*)agent->EventReceiver;
        proxy->Handler->CancelInteraction();
        arg.SetInt(-1);
        agent->ReceiveEvent(&res, &arg, 1, 0);
        return true;
    }

    public static bool BuyItemFromShop(uint shopId, uint itemId, int count)
    {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
        {
            PluginLog.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (!IsHandlerAShop(eh->Value->Info.EventId.ContentId))
        {
            PluginLog.Error($"{shopId:X} is not a shop");
            return false;
        }

        var shop = (ShopEventHandler*)eh->Value;
        PluginLog.Debug($"{shop->VisibleItemsCount}");
        for (var i = 0; i < shop->VisibleItemsCount; ++i)
        {
            var index = shop->VisibleItems[i];
            if (shop->Items[index].ItemId == itemId)
            {
                PluginLog.Debug($"Buying {count}x {itemId} from {shopId:X}");
                shop->BuyItemIndex = index;
                shop->ExecuteBuy(count);
                return true;
            }
        }

        PluginLog.Error($"Did not find item {itemId} in shop {shopId:X}");
        return false;
    }

    public static bool ShopTransactionInProgress(uint shopId)
    {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
        {
            PluginLog.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (!IsHandlerAShop(eh->Value->Info.EventId.ContentId))
        {
            PluginLog.Error($"{shopId:X} is not a shop");
            return false;
        }

        var shop = (ShopEventHandler*)eh->Value;
        return shop->WaitingForTransactionToFinish;
    }

    public static bool IsHandlerAShop(EventHandlerType contentId) => contentId is EventHandlerType.Shop or EventHandlerType.FreeCompanyCreditShop;

    public class NPCInfo(ulong id, Vector3 location, uint shopId)
    {
        public ulong Id = id;
        public Vector3 Location = location;
        public uint ShopId = shopId;
    }

    public static NPCInfo? GetNPCInfo(uint enpcId, uint territoryId, uint itemId = 0)
    {
        var scene = GetRow<TerritoryType>(territoryId)!.Value.Bg.ToString();
        var filenameStart = scene.LastIndexOf('/') + 1;
        var planeventLayerGroup = "bg/" + scene[0..filenameStart] + "planevent.lgb";
        PluginLog.Debug($"Territory {territoryId} -> {planeventLayerGroup}");
        var lvb = Svc.Data.GetFile<LgbFile>(planeventLayerGroup);
        if (lvb != null)
        {
            foreach (var layer in lvb.Layers)
            {
                foreach (var instance in layer.InstanceObjects)
                {
                    if (instance.AssetType != LayerEntryType.EventNPC)
                        continue;
                    var baseId = ((LayerCommon.ENPCInstanceObject)instance.Object).ParentData.ParentData.BaseId;
                    if (baseId == enpcId)
                    {
                        var npcId = (1ul << 32) | instance.InstanceId;
                        Vector3 npcLocation = new(instance.Transform.Translation.X, instance.Transform.Translation.Y, instance.Transform.Translation.Z);
                        PluginLog.Debug($"Found npc {baseId} {instance.InstanceId} '{GetRow<ENpcResident>(baseId)?.Singular}' at {npcLocation}");
                        if (itemId != 0)
                        {
                            var vendor = FindVendorItem(baseId, itemId);
                            if (vendor.itemIndex >= 0)
                            {
                                PluginLog.Debug($"Found shop #{vendor.shopId} and item index #{vendor.itemIndex}");
                                return new NPCInfo(npcId, npcLocation, vendor.shopId);
                            }
                        }
                        return new NPCInfo(npcId, npcLocation, 0);
                    }
                }
            }
        }
        return null;
    }

    private static (uint shopId, int itemIndex) FindVendorItem(uint enpcId, uint itemId)
    {
        var enpcBase = GetRow<ENpcBase>(enpcId);
        if (enpcBase == null)
            return (0, -1);

        foreach (var handler in enpcBase.Value.ENpcData)
        {
            var eventType = (EventHandlerType)(handler.RowId >> 16);
            switch (eventType)
            {
                case EventHandlerType.Shop:
                    var gilItems = GetSubRow<GilShopItem>(handler.RowId);
                    if (gilItems == null)
                        continue;

                    for (var i = 0; i < gilItems.Value.Count; ++i)
                    {
                        var shopItem = gilItems.Value[i];
                        if (shopItem.Item.RowId == itemId)
                            return (handler.RowId, i);
                    }
                    break;
                case EventHandlerType.FreeCompanyCreditShop:
                    var fccItems = GetRow<FccShop>(handler.RowId);
                    if (fccItems == null)
                        continue;
                    for (var i = 0; i < fccItems.Value.ItemData.Count; ++i)
                    {
                        var shopItem = fccItems.Value.ItemData[i];
                        if (shopItem.Item.RowId == itemId)
                            return (handler.RowId, i);
                    }
                    break;
                default:
                    continue;
            }
        }
        return (0, -1);
    }

    public static bool UseAction(ActionType type, uint actionId) => ActionManager.Instance()->UseAction(type, actionId);
    public static bool IsActionInUse(ActionType type, uint itemId) => ActionManager.Instance()->GetActionStatus(type, itemId) != 0;

    public static bool UseItem(uint itemId) => ActionManager.Instance()->UseAction(ActionType.Item, itemId, extraParam: 65535);

    public static bool InteractWith(ulong gameobjectId)
    {
        var obj = GameObjectManager.Instance()->Objects.GetObjectByGameObjectId(gameobjectId);
        if (obj == null)
            return false;
        TargetSystem.Instance()->InteractWithObject(obj, false);
        return true;
    }

    public static bool IsTurnInRequestInProgress(uint itemId)
    {
        var ui = UIState.Instance();
        var agent = AgentRequest.Instance();
        return agent->IsAgentActive() && ui->NpcTrade.Requests.Count == 1 && ui->NpcTrade.Requests.Items[0].ItemId == itemId;
    }

    public static void TurnInRequests()
    {
        var agent = AgentRequest.Instance();
        if (!agent->IsAgentActive())
        {
            PluginLog.Error("Agent not active...");
            return;
        }

        if (agent->SelectedTurnInSlot >= 0)
        {
            PluginLog.Error($"Turn-in already in progress for slot {agent->SelectedTurnInSlot}");
            return;
        }

        Span<AtkValue> param = stackalloc AtkValue[4];
        param[0].SetInt(2); // start turnin
        param[2].SetInt(0); // ???
        param[3].SetInt(0); // ???
        var res = new AtkValue();
        for (var i = 0; i < UIState.Instance()->NpcTrade.Requests.Count; i++)
        {
            param[1].SetInt(i); // slot
            agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);
        }
        //var res = new AtkValue();
        //Span<AtkValue> param = stackalloc AtkValue[4];
        //param[0].SetInt(2); // start turnin
        //param[1].SetInt(0); // slot
        //param[2].SetInt(0); // ???
        //param[3].SetInt(0); // ???
        //agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);

        if (agent->SelectedTurnInSlot != 0 || agent->SelectedTurnInSlotItemOptions <= 0)
        {
            PluginLog.Error($"Failed to start turn-in: cur slot={agent->SelectedTurnInSlot}, count={agent->SelectedTurnInSlotItemOptions}");
            return;
        }

        param[0].SetInt(0); // confirm
        param[1].SetInt(0); // option #0
        agent->ReceiveEvent(&res, param.GetPointer(0), 4, 1);

        if (agent->SelectedTurnInSlot >= 0)
        {
            PluginLog.Error($"Turn-in not confirmed: cur slot={agent->SelectedTurnInSlot}");
            return;
        }

        // commit
        var addonId = agent->AddonId;
        agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);
        var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)addonId);
        if (addon != null && addon->IsVisible)
            addon->Close(false);
    }

    public static bool IsQuestComplete(uint questId) => QuestManager.IsQuestComplete(questId);

    public static bool IsTerritoryLoaded() => GameMain.Instance()->TerritoryLoadState == 2;
}

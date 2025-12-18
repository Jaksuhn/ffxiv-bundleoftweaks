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

namespace ComplexTweaks.Utilities;

public unsafe class Game {
    public static void ProgressMateriaAttachDialog() {
        if (TryGetAddonByName<AtkUnitBase>("MateriaAttachDialog", out var addon)) {
            var evt = new AtkEvent() { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
            var data = new AtkEventData();
            addon->ReceiveEvent(AtkEventType.ButtonClick, 0, &evt, &data);
        }
    }

    public enum ShopType {
        None = 0,
        GilShop = 1,
        FreeCompanyCreditShop = 2
    }

    public static bool IsShopOpen(uint shopId = 0, ShopType shopType = ShopType.None) {
        AgentInterface* agent = null;
        switch (shopType) {
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

    public static bool OpenShop(GameObject* vendor, uint shopId) {
        PluginLog.Debug($"Interacting with {(ulong)vendor->GetGameObjectId():X}");
        TargetSystem.Instance()->InteractWithObject(vendor, false);
        var selector = EventHandlerSelector.Instance();
        if (selector->Target == null)
            return true; // assume interaction was successful without selector

        if (selector->Target != vendor) {
            PluginLog.Error($"Unexpected selector target {(ulong)selector->Target->GetGameObjectId():X} when trying to interact with {(ulong)vendor->GetGameObjectId():X}");
            return false;
        }

        for (var i = 0; i < selector->OptionsCount; ++i) {
            if (selector->Options[i].Handler->Info.EventId.Id == shopId) {
                PluginLog.Debug($"Selecting selector option {i} for shop {shopId:X}");
                EventFramework.Instance()->InteractWithHandlerFromSelector(i);
                return true;
            }
        }

        PluginLog.Error($"Failed to find shop {shopId:X} in selector for {(ulong)vendor->GetGameObjectId():X}");
        return false;
    }

    public static bool OpenShop(ulong vendorInstanceId, uint shopId) {
        if (Svc.Objects.TryGetFirst(o => o.BaseId == vendorInstanceId, out var vendor))
            return OpenShop(vendor.Struct(), shopId);
        else {
            PluginLog.Error($"Failed to find vendor {vendorInstanceId:X}");
            return false;
        }
    }

    public static bool CloseShop() {
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

    public static bool BuyItemFromShop(uint shopId, uint itemId, int count) {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null) {
            PluginLog.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (!IsHandlerAShop(eh->Value->Info.EventId.ContentId)) {
            PluginLog.Error($"{shopId:X} is not a shop");
            return false;
        }

        var shop = (ShopEventHandler*)eh->Value;
        PluginLog.Debug($"{shop->VisibleItemsCount}");
        for (var i = 0; i < shop->VisibleItemsCount; ++i) {
            var index = shop->VisibleItems[i];
            if (shop->Items[index].ItemId == itemId) {
                PluginLog.Debug($"Buying {count}x {itemId} from {shopId:X}");
                shop->BuyItemIndex = index;
                shop->ExecuteBuy(count);
                return true;
            }
        }

        PluginLog.Error($"Did not find item {itemId} in shop {shopId:X}");
        return false;
    }

    public static bool ShopTransactionInProgress(uint shopId) {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null) {
            PluginLog.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (!IsHandlerAShop(eh->Value->Info.EventId.ContentId)) {
            PluginLog.Error($"{shopId:X} is not a shop");
            return false;
        }

        var shop = (ShopEventHandler*)eh->Value;
        return shop->WaitingForTransactionToFinish;
    }

    public static bool IsHandlerAShop(EventHandlerContent contentId) => contentId is EventHandlerContent.Shop or EventHandlerContent.FreeCompanyCreditShop;

    public class NPCInfo(ulong id, Vector3 location, uint shopId) {
        public ulong Id = id;
        public Vector3 Location = location;
        public uint ShopId = shopId;
    }

    public static NPCInfo? GetNPCInfo(uint enpcId, uint territoryId, uint itemId = 0) {
        var scene = GetRow<TerritoryType>(territoryId)!.Value.Bg.ToString();
        var filenameStart = scene.LastIndexOf('/') + 1;
        var planeventLayerGroup = "bg/" + scene[0..filenameStart] + "planevent.lgb";
        PluginLog.Debug($"Territory {territoryId} -> {planeventLayerGroup}");
        var lvb = Svc.Data.GetFile<LgbFile>(planeventLayerGroup);
        if (lvb != null) {
            foreach (var layer in lvb.Layers) {
                foreach (var instance in layer.InstanceObjects) {
                    if (instance.AssetType != LayerEntryType.EventNPC)
                        continue;
                    var baseId = ((LayerCommon.ENPCInstanceObject)instance.Object).ParentData.ParentData.BaseId;
                    if (baseId == enpcId) {
                        var npcId = (1ul << 32) | instance.InstanceId;
                        Vector3 npcLocation = new(instance.Transform.Translation.X, instance.Transform.Translation.Y, instance.Transform.Translation.Z);
                        PluginLog.Debug($"Found npc {baseId} {instance.InstanceId} '{GetRow<ENpcResident>(baseId)?.Singular}' at {npcLocation}");
                        if (itemId != 0) {
                            var vendor = FindVendorItem(baseId, itemId);
                            if (vendor.itemIndex >= 0) {
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

    private static (uint shopId, int itemIndex) FindVendorItem(uint enpcId, uint itemId) {
        var enpcBase = GetRow<ENpcBase>(enpcId);
        if (enpcBase == null)
            return (0, -1);

        foreach (var handler in enpcBase.Value.ENpcData) {
            var eventType = (EventHandlerContent)(handler.RowId >> 16);
            switch (eventType) {
                case EventHandlerContent.Shop:
                    var gilItems = GetSubRow<GilShopItem>(handler.RowId);
                    if (gilItems == null)
                        continue;

                    for (var i = 0; i < gilItems.Value.Count; ++i) {
                        var shopItem = gilItems.Value[i];
                        if (shopItem.Item.RowId == itemId)
                            return (handler.RowId, i);
                    }
                    break;
                case EventHandlerContent.FreeCompanyCreditShop:
                    var fccItems = GetRow<FccShop>(handler.RowId);
                    if (fccItems == null)
                        continue;
                    for (var i = 0; i < fccItems.Value.ItemData.Count; ++i) {
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

    public static bool IsTurnInRequestInProgress(uint itemId) {
        var ui = UIState.Instance();
        var agent = AgentNpcTrade.Instance();
        return agent->IsAgentActive() && ui->NpcTrade.Requests.Count == 1 && ui->NpcTrade.Requests.Items[0].ItemId == itemId;
    }

    public static bool HasPermission(List<uint> ids) => ids.All(x => Conditions.Instance()->HasPermission(x));
}

using clib.Extensions.ClientStructs;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Controllers;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public class MailEnhanacementsConfig {
    [BoolConfig] public bool EnableRetrieveAll = true;
    [BoolConfig(DependsOn = nameof(EnableRetrieveAll))] public bool DeleteAfterRetrieval = true;
    [BoolConfig(DependsOn = nameof(EnableRetrieveAll))] public bool UseAfterRetrieval = true;
    [BoolConfig] public bool EnableRestockMaps = true;
}

#if DEBUG
[Tweak]
public class MailEnhancements : Tweak<MailEnhanacementsConfig> {
    public override string Name => "Mail Enhancments";
    public override string Description => "Adds some buttons to the mailbox addon for inbox management";

    private AddonController<AtkUnitBase>? _controller;
    private readonly List<CircleButtonNode> _btns = [];

    public override unsafe void Enable() {
        _controller = new AddonController<AtkUnitBase> {
            AddonName = "LetterList",
            OnSetup = OnLetterListSetup,
            OnRefresh = OnLetterListRefresh,
            OnFinalize = OnLetterListFinalize
        };
        _controller.Enable();
    }

    public override void Disable() {
        DisposeButtons();
        _controller?.Dispose();
    }

    private unsafe void OnLetterListSetup(AtkUnitBase* addon) {
        DisposeButtons();

        CircleButtonNode? cancelTasks = null;
        if (Config.EnableRetrieveAll) {
            var retrieveAll = new CircleButtonNode {
                Icon = ButtonIcon.ArrowDown,
                TextTooltip = "Retrieve All Mail",
                OnClick = () => {
                    _btns.Where(b => !ReferenceEquals(b, cancelTasks)).ForEach(b => b.IsEnabled = false);
                    Svc.Automation.Start(new RetrieveAllTask(Config.DeleteAfterRetrieval, Config.UseAfterRetrieval));
                }
            };
            _btns.Add(retrieveAll);
        }

        if (Config.EnableRestockMaps) {
            var restockMaps = new CircleButtonNode {
                Icon = ButtonIcon.MagnifyingGlass,
                TextTooltip = "Restock Maps",
                OnClick = () => {
                    _btns.Where(b => !ReferenceEquals(b, cancelTasks)).ForEach(b => b.IsEnabled = false);
                    Svc.Automation.Start(new RestockMapsTask());
                }
            };
            _btns.Add(restockMaps);
        }

        if (_btns.Any()) {
            cancelTasks = new CircleButtonNode {
                Icon = ButtonIcon.Cross,
                TextTooltip = "Cancel Tasks",
                OnClick = () => {
                    Svc.Automation.Stop();
                    _btns.ForEach(x => x.IsEnabled = true);
                }
            };
            _btns.Add(cancelTasks);
        }

        AttachButtons(addon);
    }

    private unsafe void OnLetterListRefresh(AtkUnitBase* addon) {
        AttachButtons(addon);
    }

    private unsafe void AttachButtons(AtkUnitBase* addon) {
        if (_btns.Count is 0) return;

        var anchorNode = addon->UldManager.SearchNodeById(5); // display only unread filter button
        if (anchorNode is null) {
            _btns.ForEach(btn => btn.IsVisible = false);
            return;
        }

        foreach (var (btn, i) in _btns.AsEnumerable().Reverse().WithIndex()) {
            if (btn.Node is null || btn.Node->ParentNode is null) {
                btn.AttachNode(addon);
            }

            btn.Size = anchorNode->Size;
            btn.IsVisible = true;
            btn.Position = new Vector2(anchorNode->X - (i + 1) * 30, anchorNode->Y);
        }
    }

    private unsafe void OnLetterListFinalize(AtkUnitBase* addon) {
        DisposeButtons();
    }

    private void DisposeButtons() {
        _btns.ForEach(x => x.Dispose());
        _btns.Clear();
    }

    private class RetrieveAllTask(bool deleteAfterRetrieval, bool useAfterRetrieval) : TaskBase {
        private unsafe InfoProxyLetter.Letter[] Letters => InfoProxyLetter.Instance()->Letters.ToArray();
        private unsafe bool UseItem(uint itemId) => ActionManager.Instance()->UseAction(ActionType.Item, itemId, extraParam: 65535);
        protected override async Task Execute() {
            using var scope = BeginScope("RetrieveAll");
            List<InfoProxyLetter.Letter.ItemAttachment> attachements = [];

            foreach (var letter in Letters) {
                if (letter.Attachments.Length == 0) continue;
                attachements.AddRange(letter.Attachments.ToArray().Where(a => a.ItemId != 0));
                await WaitUntil(InfoProxyLetter.CanTakeAttachement, "WaitCooldown");
                await WaitUntil(() => InfoProxyLetter.TakeAllAttachements(letter.Index, letter.SenderContentId), "TakeAttachments");

                if (deleteAfterRetrieval) {
                    await WaitUntil(() => InfoProxyLetter.DeleteLetter(letter.Index), "DeleteLetter");
                }
            }

            if (useAfterRetrieval) {
                foreach (var attach in attachements) {
                    await WaitUntil(Svc.Condition.CanMoveItems, "WaitCanMoveItems"); // get another condition
                    await WaitUntil(() => UseItem(attach.ItemId), "UseItem");
                }
            }
        }
    }

    private class RestockMapsTask : TaskBase {
        private unsafe Span<InfoProxyLetter.Letter> Letters => InfoProxyLetter.Instance()->Letters;
        private int LetterMapCount() => Letters.ToArray().Count(i => i.Attachments.ToArray().Any(a => Item.GetRow(a.ItemId).FilterGroup == 18));

        protected override async Task Execute() {
            GameMain.ExecuteCommand(CommandFlag.RequestSaddleBag);
            static bool InventoryHasMap() => InventoryType.Bags.Any(i => i.Items.Any(i => i.IsTreasureMap));
            static bool HasMapDeciphered() => InventoryType.KeyItems.Items.Any(i => i.IsTreasureMap);
            static bool SaddleBagHasMap() => InventoryType.SaddleBag.Any(i => i.Items.Any(i => i.IsTreasureMap));

            if (HasMapDeciphered() && InventoryHasMap() && SaddleBagHasMap()) return;

            while (LetterMapCount() > 0) {
                var inventoryHasMap = InventoryHasMap();
                var hasMapDeciphered = HasMapDeciphered();
                var saddleBagHasMap = SaddleBagHasMap();

                if (hasMapDeciphered && inventoryHasMap && saddleBagHasMap) return;

                if (!hasMapDeciphered && inventoryHasMap) {
                    await CloseMail();
                    await Decipher();
                }
                else if (!inventoryHasMap) {
#pragma warning disable IDE0002 // why does it do this
                    if (InfoProxyLetter.MapLetter is not { } letter) return;
#pragma warning restore
                    await OpenMailbox();
                    InfoProxyLetter.TakeAllAttachements(letter.Index, letter.SenderContentId);
                }
                else if (!saddleBagHasMap) {
                    if (InventoryType.SaddleBag.Any(i => i.IsFull)) return;
                    await CloseMail();
                    await OpenSaddlebag();
                    InventoryType.Bags.FirstOrNull(b => b.Items.FirstOrDefault(i => i.IsTreasureMap) is not null)?.Items.FirstOrDefault(i => i.IsTreasureMap)?.MoveTo(InventoryType.SaddleBag);
                    await WaitUntilThenFalse(() => InventoryManager.IsUpdating, "WaitForMove");
                }

                await NextFrame();
            }
        }

        private async Task OpenMailbox() {
            using var scope = BeginScope(nameof(OpenMailbox));
            if (AtkUnitBase.IsAddonReady("LetterList")) return;
            var obj = Svc.Objects.FirstOrDefault(o => o.ObjectKind is ObjectKind.EventNpc && o.Name.TextValue == "Delivery Moogle" || o.ObjectKind is ObjectKind.HousingEventObject && o.Name.TextValue == "Moogle Letter Box");
            if (obj is null) return;
            await InteractWith(obj);
            await WaitUntil(() => AtkUnitBase.IsAddonReady("LetterList"), "WaitForMailbox");
        }

        private async Task CloseMail() {
            using var scope = BeginScope(nameof(CloseMail));
            AtkUnitBase.CloseAddon("LetterList");
            await WaitWhile(() => Player.IsBusy, "WaitCloseMail");
        }

        private async Task OpenSaddlebag() {
            using var scope = BeginScope(nameof(OpenSaddlebag));
            if (AtkUnitBase.IsAddonReady("InventoryBuddy")) return;
            await WaitUntil(() => Svc.Condition.HasPermission(134), "WaitCanOpenSaddleBag");
            ActionManager.ExecuteMainCommand(77); // chocobo saddlebag
            await WaitUntil(() => AtkUnitBase.IsAddonReady("InventoryBuddy"), "WaitForSaddlebag");
        }

        private async Task Decipher() {
            using var scope = BeginScope(nameof(Decipher));
            foreach (var b in InventoryType.Bags) {
                if (b.Items.FirstOrDefault(i => i.IsTreasureMap) is not { } map) continue;

                map.OpenContext();
                await WaitUntil(() => AtkUnitBase.IsAddonReady("ContextMenu"), "WaitForCtx");
                if (TryGetDecipherIndex(out var i)) {
                    DecipherCallback(i.Value);
                    await WaitUntil(() => InventoryType.KeyItems.Items.Any(i => i.IsTreasureMap), "WaitForDecipher");
                }
            }
        }

        private unsafe bool TryGetDecipherIndex([NotNullWhen(true)] out int? index) {
            foreach (var (contextObj, i) in AgentInventoryContext.Instance()->EventParams.ToArray().WithIndex()) {
                if (contextObj.Type == AtkValueType.String) {
                    if (Addon.GetRow(8100).Text == MemoryHelper.ReadSeStringNullTerminated(new IntPtr(contextObj.String)).TextValue) {
                        index = i;
                        return true;
                    }
                }
            }
            index = -1;
            return false;
        }

        private unsafe void DecipherCallback(int indexDecipher) {
            var ctx = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextMenu", 1).Address;
            var values = stackalloc AtkValue[5];
            values[0].SetInt(0);
            values[1].SetInt(indexDecipher);
            values[2].SetInt(0);
            values[3].SetInt(0);
            values[4].SetInt(0);
            ctx->FireCallback(5, values, true);
        }
    }
}
#endif

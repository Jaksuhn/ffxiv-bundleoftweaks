using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

[Requires(Ipc.AutoRetainer | Ipc.Lifestream | Ipc.GlamourLog)]
public class ARGlamourLog : ARTweak {
    public override string Name => "AutoRetainer x GlamourLog";
    public override string Description => "On CharacterPostProcess, go to the inn and store any outfit pieces.";

    private GlamourLogStoreTargets _targets;

    public override void OnCharacterPostProcessStep() {
        _targets = GlamourLogIPC.Get().HasAnythingToStore();
        if (_targets is GlamourLogStoreTargets.None) {
            Log("Skipping post process for character: nothing to store.");
            return;
        }

        Log($"Requesting post process for character: storable targets {_targets}.");
        AutoRetainer.RequestCharacterPostprocess();
    }

    public override void OnCharacterReadyToPostProcess()
        => Svc.Automation.Start(new StoreGlamour(_targets), AutoRetainer.FinishCharacterPostProcess);

    private sealed class StoreGlamour(GlamourLogStoreTargets targets) : TaskBase {
        private const uint ArmoireHandlerId = 720978;
        private const uint DresserHandlerId = 721347;

        protected override async Task Execute() {
            using var scope = BeginScope(nameof(StoreGlamour));

            if (!IsInInn()) {
                Status = "Going to the inn";
                LifestreamIPC.Get().ExecuteCommand("inn");
                await WaitUntilThenFalse(() => LifestreamIPC.Get().IsBusy(), "LifestreamWaitForInn");
                await WaitUntil(() => IsInInn() && IObjectTable.Get().LocalPlayer.Interactable, "WaitForInnRoom", timeout: TimeSpan.FromSeconds(30));
            }

            if (targets.HasFlag(GlamourLogStoreTargets.Armoire))
                await Store(ArmoireHandlerId, "Cabinet", selectStringIndex: 0);
            if (targets.HasFlag(GlamourLogStoreTargets.Dresser))
                await Store(DresserHandlerId, "MiragePrismPrismBox", selectStringIndex: 0);
        }

        private async Task Store(uint handlerId, string addonName, int? selectStringIndex = null) {
            using var scope = BeginScope($"Store {addonName}");
            var glamourLog = GlamourLogIPC.Get();

            Status = $"Finding {addonName}";
            await WaitUntil(() => FindStorage(handlerId) is not null, "WaitForStorageObject", timeout: TimeSpan.FromSeconds(10));
            var obj = FindStorage(handlerId)!;

            Status = $"Opening {addonName}";
            await InteractWith(obj, () => AtkUnitBase.IsAddonReady(addonName) && glamourLog.ReadyToStore(), selectStringIndex);
            await NextFrame(2);

            Status = $"Storing into {addonName}";
            ErrorIf(!glamourLog.EntrustAll(), $"GlamourLog failed to start storing into the {addonName}");
            await WaitWhile(glamourLog.IsBusy, "WaitForGlamourLogStore", timeout: TimeSpan.FromMinutes(2));

            Status = $"Closing {addonName}";
            AtkUnitBase.CloseAddon(addonName);
            await WaitWhile(() => AtkUnitBase.IsAddonReady(addonName), "WaitForStorageClose", timeout: TimeSpan.FromSeconds(10));
            await WaitWhileBusy();
        }

        private static DGameObject? FindStorage(uint handlerId)
            => IObjectTable.Get().FirstOrDefault(o => o.ObjectKind is ObjectKind.EventObj && o.IsTargetable && Sheets.EObj.TryGetRow(o.BaseId, out var row) && row.Data.RowId == handlerId);

        private static bool IsInInn()
            => Sheets.TerritoryType.TryGetRow(IClientState.Get().TerritoryType, out var row) && row.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Inn;
    }
}

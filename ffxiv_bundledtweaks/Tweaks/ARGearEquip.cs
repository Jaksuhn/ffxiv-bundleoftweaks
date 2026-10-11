using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

[Requires(Ipc.AutoRetainer)]
public class ARGearEquip : ARTweak {
    public override string Name => "Auto Retainer Gear Equip";
    public override string Description => "On CharacterPostProcess, equip recommended gear and update the current gearset if needed.";

    public override unsafe void OnCharacterPostProcessStep() {
        var mod = RecommendEquipModule.Instance();
        if (mod == null) {
            Log("Skipping post process for character: RecommendEquipModule unavailable.");
            return;
        }

        if (!mod->SetupForClassJob(PlayerState.Instance()->CurrentClassJobId)) {
            Log("Skipping post process for character: SetupForClassJob failed.");
            return;
        }

        AutoRetainer.RequestCharacterPostprocess();
    }

    public override void OnCharacterReadyToPostProcess()
        => Svc.Automation.Start(new EquipRecommended(), AutoRetainer.FinishCharacterPostProcess);

    private sealed class EquipRecommended : AutoTask {
        protected override async Task Execute() {
            using var scope = BeginScope(nameof(EquipRecommended));

            if (!HasRecommendModule())
                return;

            Status = "Waiting for recommend setup";
            await WaitWhile(IsRecommendUpdating, "RecommendEquipUpdating");

            if (IsAllRecommendedGearEquipped())
                return;

            Status = "Equipping recommended gear";
            EquipRecommendedGear();
            await WaitWhile(IsRecommendUpdating, "RecommendEquipEquipping");
            await DelayMs(250);

            Status = "Updating gearset";
            if (!TryUpdateCurrentGearset())
                Log("No valid current gearset to update.");
        }

        private static unsafe bool HasRecommendModule() => RecommendEquipModule.Instance() != null;

        private static unsafe bool IsRecommendUpdating() {
            var mod = RecommendEquipModule.Instance();
            return mod != null && mod->IsUpdating;
        }

        private static unsafe void EquipRecommendedGear() {
            var mod = RecommendEquipModule.Instance();
            if (mod != null)
                mod->EquipRecommendedGear();
        }

        private static unsafe bool TryUpdateCurrentGearset() {
            var gsm = RaptureGearsetModule.Instance();
            if (gsm == null || !gsm->IsValidGearset(gsm->CurrentGearsetIndex))
                return false;

            gsm->UpdateGearset(gsm->CurrentGearsetIndex);
            return true;
        }

        private static unsafe bool IsAllRecommendedGearEquipped() {
            var mod = RecommendEquipModule.Instance();
            var equipped = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
            if (mod == null || equipped == null)
                return true;

            foreach (var rec in mod->RecommendedItems) {
                var recommended = rec.Value;
                if (recommended == null || recommended->ItemId == 0)
                    continue;

                var isEquipped = false;
                for (var i = 0; i < equipped->Size; i++) {
                    if (equipped->Items[i].ItemId == recommended->ItemId) {
                        isEquipped = true;
                        break;
                    }
                }

                if (!isEquipped)
                    return false;
            }

            return true;
        }
    }
}

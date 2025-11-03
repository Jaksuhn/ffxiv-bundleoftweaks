using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Memory;
using ECommons;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Features;

public class GettingTooAttachedConfiguration
{
    [IntConfig(DefaultValue = 10000)]
    public int NumberOfLoops = 10000;
}

[Tweak]
public unsafe class GettingTooAttached : Tweak<GettingTooAttachedConfiguration>
{
    public override string Name => "Getting Too Attached";
    public override string Description => "Loop through attaching and removing materia for the Getting Too Attached achievement";

    const int AchievementId = 1905;
    internal bool active = false;
    private readonly Memory.AchievementProgress AchievementProgress = new();
    public override void Enable()
    {
        AchievementProgress.ReceiveAchievementProgressHook.Enable();
        Events.AchievementProgressUpdate += OnAchievementProgressUpdate;
        Svc.Toasts.ErrorToast += CheckForErrors;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "MateriaAttachDialog", ConfirmMateriaDialog);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "MateriaRetrieveDialog", ConfirmRetrievalDialog);
    }

    public override void Disable()
    {
        AchievementProgress.ReceiveAchievementProgressHook.Disable();
        Events.AchievementProgressUpdate -= OnAchievementProgressUpdate;
        Svc.Toasts.ErrorToast -= CheckForErrors;
        Svc.AddonLifecycle.UnregisterListener(ConfirmMateriaDialog);
        Svc.AddonLifecycle.UnregisterListener(ConfirmRetrievalDialog);
    }

    private void OnAchievementProgressUpdate(uint id, uint current, uint max)
    {
        if (id == AchievementId)
        {
            Log($"setting loop count to {max - current}");
            Config.NumberOfLoops = (int)(max - current);
        }
    }

    public override void DrawConfig()
    {
        if (!C.EnabledTweaks.Contains(nameof(GettingTooAttached))) return;

        ImGui.DragInt("Number of Loops", ref Config.NumberOfLoops);
        if (ImGui.Button(!active ? $"Start Looping###StartLooping" : $"Looping. Click to abort.###AbortLoop"))
        {
            if (!active)
            {
                active = true;
                TaskManager.Enqueue(YesAlready.DisableIfNeeded);
                TaskManager.Enqueue(TryGettingTooAttached);
            }
            else
            {
                CancelLoop();
            }
        }
        if (ImGui.Button("request"))
            FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance()->RequestAchievementProgress(AchievementId);
    }

    private void CancelLoop()
    {
        active = false;
        TaskManager.Abort();
        TaskManager.Enqueue(YesAlready.EnableIfNeeded);
    }

    private void CheckForErrors(ref SeString message, ref bool isHandled)
    {
        var msg = message.GetText();
        if (new[] { 7701, 7707 }.Any(x => msg == FindRow<LogMessage>(y => y.RowId == x)?.Text.ExtractText()))
        {
            ModuleMessage("Error while melding. Aborting Tasks.");
            CancelLoop();
        }
    }

    private static bool IsBusy() => Svc.Condition[ConditionFlag.MeldingMateria] || Svc.Condition[ConditionFlag.Occupied39] || !Svc.Condition[ConditionFlag.NormalConditions];

    private void TryGettingTooAttached()
    {
        if (Config.NumberOfLoops > 0)
        {
            TaskManager.Enqueue(SelectItem, "Selecting Item");
            TaskManager.Enqueue(SelectMateria, "Selecting Materia");
            TaskManager.Enqueue(SelectItem, "Selecting Item");
            TaskManager.Enqueue(ActivateContextMenu, "Opening Context Menu");
            TaskManager.Enqueue(RetrieveMateriaContextMenu, "Activating Retrieve Materia Context Entry");
            TaskManager.Enqueue(() => Config.NumberOfLoops -= 1);
            TaskManager.Enqueue(TryGettingTooAttached, "Repeat Loop");
        }
        else
        {
            TaskManager.Enqueue(() => active = false);
            TaskManager.Enqueue(YesAlready.EnableIfNeeded);
        }
    }

    private unsafe bool? SelectItem()
    {
        if (TryGetAddonByName<AtkUnitBase>("MateriaAttach", out var addon) && !IsBusy() && !AreDialogsOpen())
        {
            if (addon->UldManager.NodeList[16]->IsVisible())
            {
                CancelLoop();
                ModuleMessage("Unable to continue. No gear in inventory");
                return false;
            }

            Callback.Fire(addon, false, 1, 0, 1, 0); // second value is what position in the list the item is
            return addon->AtkValues[287].Int != -1; // condition for something being selected
        }
        return false;
    }

    public static bool AreDialogsOpen() => Svc.GameGui.GetAddonByName("MateriaAttachDialog") != nint.Zero && Svc.GameGui.GetAddonByName("MateriaRetrieveDialog") != nint.Zero;

    public unsafe bool? SelectMateria()
    {
        if (TryGetAddonByName<AtkUnitBase>("MateriaAttach", out var addon) && !AreDialogsOpen())
        {
            if (addon->UldManager.NodeList[6]->IsVisible())
            {
                CancelLoop();
                ModuleMessage("Unable to continue. No materia to meld.");
                return false;
            }
            else if (MemoryHelper.ReadSeStringNullTerminated(new nint(addon->AtkValues[289].String)).ToString().Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)[2] == "0")
            {
                CancelLoop();
                ModuleMessage("Unable to continue. First listed materia has too high ilvl requirements.");
                return false;
            }

            Callback.Fire(addon, false, 2, 0, 1, 0); // second value is which materia in the list
            return TryGetAddonByName<AtkUnitBase>("MateriaAttachDialog", out var attachDialog) && attachDialog->IsVisible && Svc.Condition[ConditionFlag.MeldingMateria];
        }
        return false;
    }

    public void ConfirmMateriaDialog(AddonEvent type, AddonArgs args)
    {
        if (!active) return;
        var addon = new AddonMaster.MateriaAttachDialog(args.ToPtr());
        if (addon.Base->AtkValues[48].Type != 0)
        {
            CancelLoop();
            ModuleMessage("Unable to continue. This gear requires overmelding.");
            return;
        }

        TaskManager.InsertMulti([new(() => Svc.Condition[ConditionFlag.MeldingMateria]), new(addon.Meld)]);
    }

    public unsafe bool ActivateContextMenu()
    {
        if (TryGetAddonByName<AtkUnitBase>("MateriaAttach", out var addon) && !Svc.Condition[ConditionFlag.MeldingMateria])
        {
            Callback.Fire(addon, false, 4, 0, 1, 0);

            return TryGetAddonByName<AtkUnitBase>("ContextMenu", out var contextMenu) && contextMenu->IsVisible;
        }
        return false;
    }

    private static bool RetrieveMateriaContextMenu()
    {
        if (!Svc.Condition[ConditionFlag.Occupied39])
            Callback.Fire(Svc.GameGui.GetAddonByName("ContextMenu").ToPtr(), true, 0, 1, 0u, 0, 0);
        return !Svc.Condition[ConditionFlag.Occupied39];
    }

    public void ConfirmRetrievalDialog(AddonEvent type, AddonArgs args)
    {
        if (!active) return;
        new AddonMaster.MateriaRetrieveDialog(args.Addon).Begin();
    }
}

using ECommons.Reflection;

namespace ComplexTweaks.IPC;

internal static class YesAlready {
    internal static bool Reenable = false;
    internal static void DisableIfNeeded() {
        if (DalamudReflector.TryGetDalamudPlugin("Yes Already", out var pl, false, true)) {
            Svc.Log.Information("Disabling Yes Already");
            pl.GetStaticFoP("YesAlready.Service", "Configuration").SetFoP("Enabled", false);
            Reenable = true;
        }
    }

    internal static void EnableIfNeeded() {
        if (Reenable && DalamudReflector.TryGetDalamudPlugin("Yes Already", out var pl, false, true)) {
            Svc.Log.Information("Enabling Yes Already");
            pl.GetStaticFoP("YesAlready.Service", "Configuration").SetFoP("Enabled", true);
            Reenable = false;
        }
    }

    internal static bool IsEnabled()
        => DalamudReflector.TryGetDalamudPlugin("Yes Already", out var pl, false, true) && pl.GetStaticFoP("YesAlready.Service", "Configuration").GetFoP<bool>("Enabled");

    internal static bool? WaitForYesAlreadyDisabledTask() => !IsEnabled();

    internal static void Tick() {
        if (Service.TaskManager.IsBusy) {
            if (IsEnabled()) {
                DisableIfNeeded();
            }
        }
        else {
            if (Reenable) {
                EnableIfNeeded();
            }
        }
    }
}

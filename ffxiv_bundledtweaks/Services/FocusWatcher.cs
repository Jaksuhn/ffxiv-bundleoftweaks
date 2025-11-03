using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace ComplexTweaks.Services;
public unsafe class FocusWatcher : IDisposable
{
    public FocusWatcher() => Svc.Framework.Update += CheckAddonFocus;

    public static event Action<Pointer<AtkUnitBase>> AddonFocusChanged = null!;
    public static void OnAddonFocusChange(Pointer<AtkUnitBase> atk)
    {
        LastFocusedAddon = atk != null ? atk.Value->NameString : string.Empty;
        AddonFocusChanged?.Invoke(atk);
    }

    private static string LastFocusedAddon = string.Empty;
    private void CheckAddonFocus(IFramework framework)
    {
        var focus = AtkStage.Instance()->GetFocus();
        if (focus == null && LastFocusedAddon != string.Empty)
        {
            LastFocusedAddon = string.Empty;
            Svc.Log.Info($"{nameof(LastFocusedAddon)} is now null");
            AddonFocusChanged?.Invoke(null);
            return;
        }
        for (var i = 0; i < RaptureAtkUnitManager.Instance()->FocusedUnitsList.Count; i++)
        {
            var atk = RaptureAtkUnitManager.Instance()->FocusedUnitsList.Entries[i].Value;
            if (atk != null && atk->RootNode == GetRootNode(focus) && atk->NameString != LastFocusedAddon)
            {
                LastFocusedAddon = atk->NameString;
                Svc.Log.Info($"New addon focused {LastFocusedAddon}");
                AddonFocusChanged?.Invoke(atk);
            }
        }
    }

    public void Dispose() => Svc.Framework.Update -= CheckAddonFocus;
}

using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace ComplexTweaks.Services;

public unsafe class AddonObserver : IDisposable
{
    public delegate void CallbackDelegate(string addonName);
    public event CallbackDelegate? AddonOpen;
    public event CallbackDelegate? AddonClose;

    private readonly HashSet<Pointer<AtkUnitBase>> _visibleUnits = new(256);
    private readonly HashSet<Pointer<AtkUnitBase>> _removedUnits = new(16);
    private readonly Dictionary<Pointer<AtkUnitBase>, string> _nameCache = new(256);

    public AddonObserver()
    {
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        GC.SuppressFinalize(this);
    }

    public bool IsAddonVisible(string name)
        => _nameCache.ContainsValue(name);

    private void OnFrameworkUpdate(IFramework framework)
    {
        _visibleUnits.Clear();

        foreach (var atkUnitBase in RaptureAtkModule.Instance()->RaptureAtkUnitManager.AtkUnitManager.AllLoadedUnitsList.Entries)
        {
            if (atkUnitBase.Value != null && atkUnitBase.Value->IsReady && atkUnitBase.Value->IsVisible)
                _visibleUnits.Add(atkUnitBase);
        }

        _removedUnits.Clear();

        foreach (var (address, name) in _nameCache)
        {
            if (!_visibleUnits.Contains(address) && _removedUnits.Add(address))
            {
                _nameCache.Remove(address);
                AddonClose?.Invoke(name);
            }
        }

        foreach (var address in _visibleUnits)
        {
            if (_nameCache.ContainsKey(address))
                continue;

            var name = address.Value->NameString;
            _nameCache.Add(address, name);
            AddonOpen?.Invoke(name);
        }
    }
}

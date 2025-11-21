using ECommons.EzHookManager;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace ComplexTweaks.Utilities.Movement;

public unsafe class OverrideCamera
{
    public bool Enabled
    {
        get => RMICameraHook.IsEnabled;
        set
        {
            if (value)
                RMICameraHook.Enable();
            else
                RMICameraHook.Disable();
        }
    }

    public bool IgnoreUserInput; // if true - override even if user tries to change camera orientation, otherwise override only if user does nothing
    public Angle DesiredAzimuth;
    public Angle DesiredAltitude;
    public Angle SpeedH = 360.Degrees(); // per second
    public Angle SpeedV = 360.Degrees(); // per second

    private delegate void RMICameraDelegate(Camera* self, int inputMode, float speedH, float speedV);
    [EzHook("40 53 48 83 EC 70 44 0F 29 44 24 ?? 48 8B D9", false)]
    private readonly EzHook<RMICameraDelegate> RMICameraHook = null!;

    public OverrideCamera()
    {
        EzSignatureHelper.Initialize(this);
        Svc.Log.Information($"RMICamera address: 0x{RMICameraHook.Address:X}");
    }

    private void RMICameraDetour(Camera* self, int inputMode, float speedH, float speedV)
    {
        RMICameraHook.Original(self, inputMode, speedH, speedV);
        if (IgnoreUserInput || inputMode == 0) // let user override...
        {
            var dt = Framework.Instance()->FrameDeltaTime;
            var deltaH = (DesiredAzimuth - self->DirH.Radians()).Normalized();
            var deltaV = (DesiredAltitude - self->DirV.Radians()).Normalized();
            var maxH = SpeedH.Rad * dt;
            var maxV = SpeedV.Rad * dt;
            self->InputDeltaH = Math.Clamp(deltaH.Rad, -maxH, maxH);
            self->InputDeltaV = Math.Clamp(deltaV.Rad, -maxV, maxV);
        }
    }
}

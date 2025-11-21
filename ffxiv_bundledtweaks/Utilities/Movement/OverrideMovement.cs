using Dalamud.Game.Config;
using ECommons.EzHookManager;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System.Runtime.InteropServices;

namespace ComplexTweaks.Utilities.Movement;

[StructLayout(LayoutKind.Explicit, Size = 0x18)]
public unsafe struct PlayerMoveControllerFlyInput
{
    [FieldOffset(0x0)] public float Forward;
    [FieldOffset(0x4)] public float Left;
    [FieldOffset(0x8)] public float Up;
    [FieldOffset(0xC)] public float Turn;
    [FieldOffset(0x10)] public float u10;
    [FieldOffset(0x14)] public byte DirMode;
    [FieldOffset(0x15)] public byte HaveBackwardOrStrafe;
}

public unsafe class OverrideMovement
{
    public bool Enabled
    {
        get => RMIWalkHook.IsEnabled;
        set
        {
            if (value)
            {
                RMIWalkHook.Enable();
                RMIFlyHook.Enable();
            }
            else
            {
                RMIWalkHook.Disable();
                RMIFlyHook.Disable();
            }
        }
    }

    public bool IgnoreUserInput; // if true - override even if user tries to change camera orientation, otherwise override only if user does nothing
    public Vector3 DesiredPosition;
    public float Precision = 0.01f;

    private bool _legacyMode;

    private delegate bool RMIWalkIsInputEnabled(void* self);
    private readonly RMIWalkIsInputEnabled _rmiWalkIsInputEnabled1;
    private readonly RMIWalkIsInputEnabled _rmiWalkIsInputEnabled2;

    private delegate void RMIWalkDelegate(void* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
    [EzHook("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", false)]
    private readonly EzHook<RMIWalkDelegate> RMIWalkHook = null!;

    private delegate void RMIFlyDelegate(void* self, PlayerMoveControllerFlyInput* result);
    [EzHook("E8 ?? ?? ?? ?? 0F B6 0D ?? ?? ?? ?? B8", false)]
    private readonly EzHook<RMIFlyDelegate> RMIFlyHook = null!;

    public OverrideMovement()
    {
        EzSignatureHelper.Initialize(this);
        Svc.Log.Information($"RMIWalk address: 0x{RMIWalkHook.Address:X}");
        Svc.Log.Information($"RMIFly address: 0x{RMIFlyHook.Address:X}");

        var rmiWalkIsInputEnabled1Addr = Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 10 38 43 3C");
        var rmiWalkIsInputEnabled2Addr = Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 75 03 88 47 3F");
        Svc.Log.Information($"RMIWalkIsInputEnabled1 address: 0x{rmiWalkIsInputEnabled1Addr:X}");
        Svc.Log.Information($"RMIWalkIsInputEnabled2 address: 0x{rmiWalkIsInputEnabled2Addr:X}");
        _rmiWalkIsInputEnabled1 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(rmiWalkIsInputEnabled1Addr);
        _rmiWalkIsInputEnabled2 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(rmiWalkIsInputEnabled2Addr);

        Svc.GameConfig.UiControlChanged += OnConfigChanged;
        UpdateLegacyMode();
    }

    public void Dispose()
    {
        Svc.GameConfig.UiControlChanged -= OnConfigChanged;
    }

    private void RMIWalkDetour(void* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        RMIWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);
        // TODO: we really need to introduce some extra checks that PlayerMoveController::readInput does - sometimes it skips reading input, and returning something non-zero breaks stuff...
        var movementAllowed = bAdditiveUnk == 0 && _rmiWalkIsInputEnabled1(self) && _rmiWalkIsInputEnabled2(self); //&& !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BeingMoved];
        if (movementAllowed && (IgnoreUserInput || *sumLeft == 0 && *sumForward == 0) && DirectionToDestination(false) is var relDir && relDir != null)
        {
            var dir = relDir.Value.h.ToDirection();
            *sumLeft = dir.X;
            *sumForward = dir.Y;
        }
    }

    private void RMIFlyDetour(void* self, PlayerMoveControllerFlyInput* result)
    {
        RMIFlyHook.Original(self, result);
        // TODO: we really need to introduce some extra checks that PlayerMoveController::readInput does - sometimes it skips reading input, and returning something non-zero breaks stuff...
        if ((IgnoreUserInput || result->Forward == 0 && result->Left == 0 && result->Up == 0) && DirectionToDestination(true) is var relDir && relDir != null)
        {
            var dir = relDir.Value.h.ToDirection();
            result->Forward = dir.Y;
            result->Left = dir.X;
            result->Up = relDir.Value.v.Rad;
        }
    }

    private (Angle h, Angle v)? DirectionToDestination(bool allowVertical)
    {
        var player = Svc.ClientState.LocalPlayer;
        if (player == null)
            return null;

        var dist = DesiredPosition - player.Position;
        if (dist.LengthSquared() <= Precision * Precision)
            return null;

        var dirH = Angle.FromDirectionXZ(dist);
        var dirV = allowVertical ? Angle.FromDirection(new(dist.Y, new Vector2(dist.X, dist.Z).Length())) : default;

        var refDir = _legacyMode
            ? CameraManager.Instance()->GetActiveCamera()->DirH.Radians() + 180.Degrees()
            : player.Rotation.Radians();
        return (dirH - refDir, dirV);
    }

    private void OnConfigChanged(object? sender, ConfigChangeEvent evt) => UpdateLegacyMode();
    private void UpdateLegacyMode()
    {
        _legacyMode = Svc.GameConfig.UiControl.TryGetUInt("MoveMode", out var mode) && mode == 1;
        Svc.Log.Debug($"Legacy mode is now {(_legacyMode ? "enabled" : "disabled")}");
    }
}

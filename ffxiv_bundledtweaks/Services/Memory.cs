using ECommons.EzHookManager;
using System.Runtime.InteropServices;

namespace ComplexTweaks.Services;
#pragma warning disable CS0649
public unsafe class Memory {
    public static class Signatures {
        internal const string MoveController = "E8 ?? ?? ?? ?? 48 85 C0 74 AE 83 FD 05";
        internal const string PlayerController = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F 28 F0 45 0F 57 C0"; // bossmod (Client::Game::Control::InputManager)
        // If this changes again, since this involves relative offsets, if the instruction bytes change count (e.g. F3 0F 59 05 ?? ... = 4 to F3 44 0F 59 0D ?? ... = 5)
        // update the address math: `address = address + <instruction_byte_count> + Marshal.ReadInt32(address + 4) + 4;`
        // or try and find a sig with the same count (ghidra seems better lately over IDA for getting identical sigs?)
        internal const string PlayerGroundSpeed = "F3 0F 11 05 ?? ?? ?? ?? 40 38 2D";
        internal const string FreeCompanyDialogPacketReceive = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 42 31"; // xan
    }

    public Memory() => EzSignatureHelper.Initialize(this);

    public void Dispose() { }

    #region Speed
    // this persists through LocalPlayer going null unlike setting via PMC
    public static void SetSpeed(float speedBase) {
        Svc.SigScanner.TryScanText(Signatures.PlayerGroundSpeed, out var address);
        address = address + 4 + Marshal.ReadInt32(address + 4) + 4;
        Dalamud.SafeMemory.Write(address + 20, speedBase);
        SetMoveControlData(speedBase);
    }

    private static void SetMoveControlData(float speed)
        => Dalamud.SafeMemory.Write(((delegate* unmanaged[Stdcall]<byte, nint>)Svc.SigScanner.ScanText(Signatures.MoveController))(1) + 8, speed);
    #endregion
}


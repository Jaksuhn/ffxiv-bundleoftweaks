using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Common.Lua;
using System.Runtime.InteropServices;

namespace ComplexTweaks.Services;
#pragma warning disable CS0649
public unsafe class Memory {
    public static class Signatures {
        internal const string BewitchProc = "40 53 48 83 EC 50 45 33 C0";
        internal const string EnqueueSnipeTask = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 50 48 8B F9 48 8D 4C 24 ??"; // xan
        internal const string FollowQuestRecast = "F3 0F 11 7C 24 ?? E8 ?? ?? ?? ?? 48 8B 9C 24 ?? ?? ?? ??"; // atmo
        internal const string KnockbackProc = "E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? FF C6";
        internal const string MoveController = "E8 ?? ?? ?? ?? 48 85 C0 74 AE 83 FD 05";
        internal const string PlayerController = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F 28 F0 45 0F 57 C0"; // bossmod (Client::Game::Control::InputManager)
        // If this changes again, since this involves relative offsets, if the instruction bytes change count (e.g. F3 0F 59 05 ?? ... = 4 to F3 44 0F 59 0D ?? ... = 5)
        // update the address math: `address = address + <instruction_byte_count> + Marshal.ReadInt32(address + 4) + 4;`
        // or try and find a sig with the same count (ghidra seems better lately over IDA for getting identical sigs?)
        internal const string PlayerGroundSpeed = "F3 0F 11 05 ?? ?? ?? ?? 40 38 2D";
        internal const string FreeCompanyDialogPacketReceive = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 42 31"; // xan
        internal const string ProcessPacketUpdateClassInfo = "48 89 5C 24 ?? 57 48 83 EC 20 48 8B DA 48 8D 0D ?? ?? ?? ??";
    }

    public static class Delegates {
        internal delegate ulong EnqueueSnipeTaskDelegate(EventSceneModuleImplBase* scene, lua_State* state);
        internal delegate void FreeCompanyDialogPacketReceiveDelegate(InfoProxyInterface* ptr, byte* packetData);
        internal delegate bool FollowQuestRecastDelegate(nint a1, nint a2, nint a3, nint a4, nint a5, nint a6);
        internal delegate long KbProcDelegate(long gameobj, float rot, float length, long a4, char a5, int a6);
        internal delegate nint NoBewitchActionDelegate(CSGameObject* gameObj, float x, float y, float z, int a5, nint a6);
        internal delegate void ProcessPacketUpdateClassInfoDelegate(InfoProxyInterface* ptr, byte* packetData);
    }

    public Memory() => EzSignatureHelper.Initialize(this);

    public class Hook {
        public Hook() => EzSignatureHelper.Initialize(this);
    }

    public void Dispose() { }

    #region Bewitch
    public class BewitchProc : Hook {
        [EzHook(Signatures.BewitchProc, false)]
        internal readonly EzHook<Delegates.NoBewitchActionDelegate>? BewitchHook;

        private unsafe nint BewitchDetour(CSGameObject* gameObj, float x, float y, float z, int a5, nint a6) {
            try {
                if (gameObj->IsCharacter()) {
                    var chara = gameObj->Character();
                    if (chara->GetStatusManager()->HasStatus(3023) || chara->GetStatusManager()->HasStatus(3024))
                        return nint.Zero;
                }
                return BewitchHook!.Original(gameObj, x, y, z, a5, a6);
            }
            catch (Exception ex) {
                Svc.Log.Error(ex.Message, ex);
                return BewitchHook!.Original(gameObj, x, y, z, a5, a6);
            }
        }
    }
    #endregion

    #region Knockback
    public class KnockbackProc : Hook {
        [EzHook(Signatures.KnockbackProc, false)]
        internal readonly EzHook<Delegates.KbProcDelegate>? KBProcHook;

        internal long KBProcDetour(long gameobj, float rot, float length, long a4, char a5, int a6) => KBProcHook!.Original(gameobj, rot, 0f, a4, a5, a6);
    }
    #endregion

    #region Speed
    // this persists through LocalPlayer going null unlike setting via PMC
    public static void SetSpeed(float speedBase) {
        Svc.SigScanner.TryScanText(Signatures.PlayerGroundSpeed, out var address);
        address = address + 4 + Marshal.ReadInt32(address + 4) + 4;
        Dalamud.SafeMemory.Write(address + 20, speedBase);
        SetMoveControlData(speedBase);
    }

    private static unsafe void SetMoveControlData(float speed)
        => Dalamud.SafeMemory.Write(((delegate* unmanaged[Stdcall]<byte, nint>)Svc.SigScanner.ScanText(Signatures.MoveController))(1) + 8, speed);
    #endregion

    #region Server IPC Packet Receive
    public class FreeCompanyDialogIPCReceive : Hook {
        [EzHook(Signatures.FreeCompanyDialogPacketReceive, false)]
        internal readonly EzHook<Delegates.FreeCompanyDialogPacketReceiveDelegate> FreeCompanyDialogPacketReceiveHook = null!;

        internal DateTime LastPacketTimestamp = DateTime.MinValue;
        private void FreeCompanyDialogPacketReceiveDetour(InfoProxyInterface* ptr, byte* packetData) {
            LastPacketTimestamp = DateTime.Now;
            Svc.Log.Info($"{nameof(FreeCompanyDialogPacketReceiveDetour)}: Packet received at {LastPacketTimestamp}");
            FreeCompanyDialogPacketReceiveHook.Original(ptr, packetData);
        }
    }

    public class ClassJobInfoSetupIPCReceive : Hook {
        [EzHook(Signatures.ProcessPacketUpdateClassInfo, false)]
        internal readonly EzHook<Delegates.ProcessPacketUpdateClassInfoDelegate> ProcessPacketUpdateClassInfoHook = null!;

        internal DateTime LastPacketTimestamp = DateTime.MinValue;
        private void ProcessPacketUpdateClassInfoDetour(InfoProxyInterface* ptr, byte* packetData) {
            LastPacketTimestamp = DateTime.Now;
            Svc.Log.Info($"{nameof(ProcessPacketUpdateClassInfoDetour)}: Packet received at {LastPacketTimestamp}");
            ProcessPacketUpdateClassInfoHook.Original(ptr, packetData);
        }
    }
    #endregion
}


using ECommons.Automation;
using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Common.Lua;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;

namespace Automaton.Services;
#pragma warning disable CS0649
public unsafe class Memory
{
    public static class Signatures
    {
        internal const string AgentReturnReceiveEvent = "E8 ?? ?? ?? ?? 66 0F 1F 84 ?? 00 00 00 00 48 83 EF";
        internal const string BewitchProc = "40 53 48 83 EC 50 45 33 C0";
        internal const string EnqueueSnipeTask = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 50 48 8B F9 48 8D 4C 24 ??"; // xan
        internal const string FollowQuestRecast = "F3 0F 11 7C 24 ?? E8 ?? ?? ?? ?? 48 8B 9C 24 ?? ?? ?? ??"; // atmo
        internal const string ExecuteCommand = "E8 ?? ?? ?? ?? 8D 46 0A"; // st
        internal const string ExecuteCommandComplexLocation = "E8 ?? ?? ?? ?? 48 8B 7B 08 45 33 C0";
        internal const string KnockbackProc = "E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? FF C6";
        internal const string MoveController = "E8 ?? ?? ?? ?? 48 85 C0 74 AE 83 FD 05";
        internal const string MoveItem = "48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 41 56 41 57 48 8B EC 48 83 EC 40 4C 8B F1"; // st
        internal const string PlayerController = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F 28 F0 45 0F 57 C0"; // bossmod (Client::Game::Control::InputManager)
        // If this changes again, since this involves relative offsets, if the instruction bytes change count (e.g. F3 0F 59 05 ?? ... = 4 to F3 44 0F 59 0D ?? ... = 5)
        // update the address math: `address = address + <instruction_byte_count> + Marshal.ReadInt32(address + 4) + 4;`
        // or try and find a sig with the same count (ghidra seems better lately over IDA for getting identical sigs?)
        internal const string PlayerGroundSpeed = "F3 0F 11 05 ?? ?? ?? ?? 40 38 2D";
        internal const string ReceiveAchievementProgress = "C7 81 ?? ?? ?? ?? ?? ?? ?? ?? 89 91 ?? ?? ?? ?? 44 89 81"; // cs
        internal const string RidePillion = "48 85 C9 0F 84 ?? ?? ?? ?? 48 89 6C 24 ?? 56 48 83 EC";
        internal const string FreeCompanyDialogPacketReceive = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 42 31"; // xan
        internal const string ProcessPacketUpdateClassInfo = "48 89 5C 24 ?? 57 48 83 EC 20 48 8B DA 48 8D 0D ?? ?? ?? ??";
        internal const string SystemMenuExecution = "E8 ?? ?? ?? ?? 40 B5 ?? 41 B9";
        internal const string SendLogout = "40 53 48 83 EC 20 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 3C 48 8B 0D ?? ?? ?? ??";
        internal const string HasPermission = "E8 ?? ?? ?? ?? 84 C0 75 ?? 8B FB";
    }

    public static class Delegates
    {
        internal delegate void AbandonDutyDelegate(bool a1);
        internal delegate byte AgentReturnReceiveEventDelegate(AgentInterface* agent);
        internal delegate nint AgentWorldTravelReceiveEventDelegate(Structs.AgentWorldTravel* agent, nint a2, nint a3, nint a4, long eventCase);
        internal delegate ulong EnqueueSnipeTaskDelegate(EventSceneModuleImplBase* scene, lua_State* state);
        internal delegate nint ExecuteCommandDelegate(int command, int a1 = 0, int a2 = 0, int a3 = 0, int a4 = 0);
        internal delegate nint ExecuteCommandComplexLocationDelegate(int command, Vector3 position, int param1, int param2, int param3, int param4);
        internal delegate void FreeCompanyDialogPacketReceiveDelegate(InfoProxyInterface* ptr, byte* packetData);
        internal delegate nint IsFlightProhibited(nint a1);
        internal delegate bool FollowQuestRecastDelegate(nint a1, nint a2, nint a3, nint a4, nint a5, nint a6);
        internal delegate long KbProcDelegate(long gameobj, float rot, float length, long a4, char a5, int a6);
        internal delegate void MoveItem(RaptureAtkModule* a1, void* outValue, AtkValue* atkValues);
        internal delegate nint NoBewitchActionDelegate(CSGameObject* gameObj, float x, float y, float z, int a5, nint a6);
        internal delegate void ReceiveAchievementProgressDelegate(Achievement* achievement, uint id, uint current, uint max);
        internal delegate void RetrieveMateriaDelegate(EventFramework* framework, int eventID, InventoryType inventoryType, short inventorySlot, int extraParam);
        internal delegate void RidePillionDelegate(BattleChara* target, int seatIndex);
        internal delegate void SalvageItemDelegate(AgentSalvage* thisPtr, InventoryItem* item, int addonId, byte a4);
        internal delegate nint WorldTravelSetupInfoDelegate(nint worldTravel, ushort currentWorld, ushort targetWorld);
        internal delegate void ProcessPacketUpdateClassInfoDelegate(InfoProxyInterface* ptr, byte* packetData);
        internal delegate bool SystemMenuExecutionDelegate(AgentHUD* @this, int a2, int a3, int a4, byte* a5);
        internal delegate nint SendLogoutDelegate();
        internal delegate bool HasPermissionDelegate(uint permissionId, int excludedCondition1 = 0, int excludedCondition2 = 0);
    }

    internal Delegates.RidePillionDelegate? RidePillion = EzDelegate.Get<Delegates.RidePillionDelegate>(Signatures.RidePillion);
    internal Delegates.ExecuteCommandDelegate? ExecuteCommand = EzDelegate.Get<Delegates.ExecuteCommandDelegate>(Signatures.ExecuteCommand);
    internal Delegates.MoveItem? MoveItem = EzDelegate.Get<Delegates.MoveItem>(Signatures.MoveItem);
    internal Delegates.SendLogoutDelegate? SendLogout = EzDelegate.Get<Delegates.SendLogoutDelegate>(Signatures.SendLogout);
    internal Delegates.HasPermissionDelegate? HasPermission = EzDelegate.Get<Delegates.HasPermissionDelegate>(Signatures.HasPermission);

    public Memory() => EzSignatureHelper.Initialize(this);

    public class Hook
    {
        public Hook() => EzSignatureHelper.Initialize(this);
    }

    public void Dispose() { }

    #region Bewitch
    public class BewitchProc : Hook
    {
        [EzHook(Signatures.BewitchProc, false)]
        internal readonly EzHook<Delegates.NoBewitchActionDelegate>? BewitchHook;

        private unsafe nint BewitchDetour(CSGameObject* gameObj, float x, float y, float z, int a5, nint a6)
        {
            try
            {
                if (gameObj->IsCharacter())
                {
                    var chara = gameObj->Character();
                    if (chara->GetStatusManager()->HasStatus(3023) || chara->GetStatusManager()->HasStatus(3024))
                        return nint.Zero;
                }
                return BewitchHook!.Original(gameObj, x, y, z, a5, a6);
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex.Message, ex);
                return BewitchHook!.Original(gameObj, x, y, z, a5, a6);
            }
        }
    }
    #endregion

    #region Knockback
    public class KnockbackProc : Hook
    {
        [EzHook(Signatures.KnockbackProc, false)]
        internal readonly EzHook<Delegates.KbProcDelegate>? KBProcHook;

        internal long KBProcDetour(long gameobj, float rot, float length, long a4, char a5, int a6) => KBProcHook!.Original(gameobj, rot, 0f, a4, a5, a6);
    }
    #endregion

    #region Achievements
    public class AchievementProgress : Hook
    {
        [EzHook(Signatures.ReceiveAchievementProgress, false)]
        internal EzHook<Delegates.ReceiveAchievementProgressDelegate> ReceiveAchievementProgressHook = null!;

        internal uint LastId;
        internal uint LastCurrent;
        internal uint LastMax;

        private void ReceiveAchievementProgressDetour(Achievement* achievement, uint id, uint current, uint max)
        {
            try
            {
                LastId = id;
                LastCurrent = current;
                LastMax = max;
                Svc.Log.Debug($"{nameof(ReceiveAchievementProgressDetour)}: [{id}] {current} / {max}");
                Events.OnAchievementProgressUpdate(id, current, max);
            }
            catch (Exception e)
            {
                Svc.Log.Error("Error receiving achievement progress: {e}", e);
            }

            ReceiveAchievementProgressHook.Original(achievement, id, current, max);
        }
    }
    #endregion

    #region Snipe Quest Sequences
    public class SnipeQuestSequence : Hook
    {
        [EzHook(Signatures.EnqueueSnipeTask, false)]
        internal EzHook<Delegates.EnqueueSnipeTaskDelegate> SnipeHook = null!;

        private ulong SnipeDetour(EventSceneModuleImplBase* scene, lua_State* state)
        {
            try
            {
                var val = state->top;
                val->tt = 3;
                val->value.n = 1;
                state->top += 1;
                return 1;
            }
            catch
            {
                return SnipeHook.Original.Invoke(scene, state);
            }
        }
    }
    #endregion

    #region Follow Quest Sequences
    public class FollowQuestRecastCheck : Hook
    {
        [EzHook(Signatures.FollowQuestRecast, false)]
        internal EzHook<Delegates.FollowQuestRecastDelegate> RecastHook = null!;
        internal bool RecastDetour(nint a1, nint a2, nint a3, nint a4, nint a5, nint a6) => false;
    }
    #endregion

    #region Return Receive Event
    public class AgentReturn : Hook
    {
        [EzHook(Signatures.AgentReturnReceiveEvent, false)]
        internal readonly EzHook<Delegates.AgentReturnReceiveEventDelegate> ReturnHook = null!;

        private readonly ExecuteCommands ExecuteCommands = new();
        private byte ReturnDetour(AgentInterface* agent)
        {
            Svc.Log.Info("return called");
            if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 6) != 0 || Player.IsInPvP)
            {
                Svc.Log.Info("return blocked");
                return ReturnHook.Original(agent);
            }

            if (Svc.Party.Length > 1)
            {
                if (Svc.Party[0]?.Name == Svc.ClientState.LocalPlayer?.Name)
                    Chat.SendMessage("/partycmd breakup");
                else
                    Chat.SendMessage("/leave");
            }

            Svc.Log.Info("returning");
            ExecuteCommands.ExecuteCommand(ExecuteCommandFlag.InstantReturn);
            return 1;
        }
    }
    #endregion

    #region ExecuteCommand
    public class ExecuteCommands : Hook
    {
        [EzHook(Signatures.ExecuteCommand, false)]
        internal readonly EzHook<Delegates.ExecuteCommandDelegate> ExecuteCommandHook = null!;

        [EzHook(Signatures.ExecuteCommandComplexLocation, false)]
        internal readonly EzHook<Delegates.ExecuteCommandComplexLocationDelegate> ExecuteCommandComplexLocationHook = null!;

        internal nint ExecuteCommand(int command, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0)
        {
            var result = ExecuteCommandHook.Original(command, param1, param2, param3, param4);
            return result;
        }

        internal nint ExecuteCommand(ExecuteCommandFlag command, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0)
        {
            var result = ExecuteCommandHook.Original((int)command, param1, param2, param3, param4);
            return result;
        }

        private nint ExecuteCommandDetour(int command, int param1, int param2, int param3, int param4)
        {
            Svc.Log.Debug($"[{nameof(ExecuteCommandDetour)}]: cmd:[{command}] {(ExecuteCommandFlag)command} | p1:{param1} | p2:{param2} | p3:{param3} | p4:{param4}");
            return ExecuteCommandHook.Original(command, param1, param2, param3, param4);
        }

        internal nint ExecuteCommandComplexLocation(int command, Vector3 position, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0)
        {
            var result = ExecuteCommandComplexLocationHook.Original(command, position, param1, param2, param3, param4);
            return result;
        }

        internal nint ExecuteCommandComplexLocation(ExecuteCommandComplexFlag command, Vector3 position, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0)
        {
            var result = ExecuteCommandComplexLocationHook.Original((int)command, position, param1, param2, param3, param4);
            return result;
        }

        private nint ExecuteCommandComplexLocationDetour(int command, Vector3 position, int param1, int param2, int param3, int param4)
        {
            Svc.Log.Debug($"[{nameof(ExecuteCommandComplexLocationDetour)}]: cmd:({command}) | pos:{position} | p1:{param1} | p2:{param2} | p3:{param3} | p4:{param4}");
            return ExecuteCommandComplexLocationHook.Original(command, position, param1, param2, param3, param4);
        }
    }
    #endregion

    #region Speed
    // this persists through LocalPlayer going null unlike setting via PMC
    public static void SetSpeed(float speedBase)
    {
        Svc.SigScanner.TryScanText(Signatures.PlayerGroundSpeed, out var address);
        address = address + 4 + Marshal.ReadInt32(address + 4) + 4;
        Dalamud.SafeMemory.Write(address + 20, speedBase);
        SetMoveControlData(speedBase);
    }

    private static unsafe void SetMoveControlData(float speed)
        => Dalamud.SafeMemory.Write(((delegate* unmanaged[Stdcall]<byte, nint>)Svc.SigScanner.ScanText(Signatures.MoveController))(1) + 8, speed);
    #endregion

    #region Server IPC Packet Receive
    public class FreeCompanyDialogIPCReceive : Hook
    {
        [EzHook(Signatures.FreeCompanyDialogPacketReceive, false)]
        internal readonly EzHook<Delegates.FreeCompanyDialogPacketReceiveDelegate> FreeCompanyDialogPacketReceiveHook = null!;

        internal DateTime LastPacketTimestamp = DateTime.MinValue;
        private void FreeCompanyDialogPacketReceiveDetour(InfoProxyInterface* ptr, byte* packetData)
        {
            LastPacketTimestamp = DateTime.Now;
            Svc.Log.Info($"{nameof(FreeCompanyDialogPacketReceiveDetour)}: Packet received at {LastPacketTimestamp}");
            FreeCompanyDialogPacketReceiveHook.Original(ptr, packetData);
        }
    }

    public class ClassJobInfoSetupIPCReceive : Hook
    {
        [EzHook(Signatures.ProcessPacketUpdateClassInfo, false)]
        internal readonly EzHook<Delegates.ProcessPacketUpdateClassInfoDelegate> ProcessPacketUpdateClassInfoHook = null!;

        internal DateTime LastPacketTimestamp = DateTime.MinValue;
        private void ProcessPacketUpdateClassInfoDetour(InfoProxyInterface* ptr, byte* packetData)
        {
            LastPacketTimestamp = DateTime.Now;
            Svc.Log.Info($"{nameof(ProcessPacketUpdateClassInfoDetour)}: Packet received at {LastPacketTimestamp}");
            ProcessPacketUpdateClassInfoHook.Original(ptr, packetData);
        }
    }
    #endregion
}


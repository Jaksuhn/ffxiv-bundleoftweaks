using Dalamud.Game.Inventory;
using ECommons.Automation;
using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Common.Lua;
using System.Runtime.InteropServices;

namespace Automaton.Services;
#pragma warning disable CS0649
public unsafe class Memory
{
    public static class Signatures
    {
        internal const string AgentReturnReceiveEvent = "E8 ?? ?? ?? ?? 41 8D 5E 0D";
        internal const string AbandonDuty = "E8 ?? ?? ?? ?? 48 8B 43 28 41 B2 01";
        internal const string BewitchProc = "40 53 48 83 EC 50 45 33 C0";
        internal const string EnqueueSnipeTask = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 50 48 8B F1 48 8D 4C 24 ?? E8 ?? ?? ?? ?? 48 8B 4C 24 ??"; // xan
        internal const string FollowQuestRecast = "E8 ?? ?? ?? ?? 48 8B 9C 24 ?? ?? ?? ?? 0F 28 74 24 ?? 0F 28 7C 24 ?? 44 0F 28 44 24 ?? 48 81 C4"; // atmo
        internal const string ExecuteCommand = "E8 ?? ?? ?? ?? 8D 46 0A"; // st
        internal const string ExecuteCommandComplexLocation = "E8 ?? ?? ?? ?? EB 1E 48 8B 53 08";
        internal const string FlightProhibited = "E8 ?? ?? ?? ?? 85 C0 74 07 32 C0 48 83 C4 38"; // hyperborea
        internal const string KnockbackProc = "E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? FF C6";
        internal const string MoveController = "E8 ?? ?? ?? ?? 48 85 C0 74 AE 83 FD 05";
        internal const string PacketDispatcher_OnReceivePacketHookSig = "40 53 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 8B F2"; // hyperborea
        internal const string PacketDispatcher_OnSendPacketHook = "48 89 5C 24 ?? 48 89 74 24 ?? 4C 89 64 24 ?? 55 41 56 41 57 48 8B EC 48 83 EC 70"; // hyperborea
        internal const string PlayerController = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 3C 01 75 1E 48 8D 0D"; // bossmod
        internal const string PlayerGroundSpeed = "F3 0F 59 05 ?? ?? ?? ?? F3 0F 59 05 ?? ?? ?? ?? F3 0F 58 05 ?? ?? ?? ?? 44 0F 28 C8";
        internal const string ReceiveAchievementProgress = "C7 81 ?? ?? ?? ?? ?? ?? ?? ?? 89 91 ?? ?? ?? ?? 44 89 81"; // cs
        internal const string RidePillion = "48 85 C9 0F 84 ?? ?? ?? ?? 48 89 6C 24 ?? 56 48 83 EC";
        internal const string SalvageItem = "E8 ?? ?? ?? ?? EB 5A 48 8B 07"; // veyn
        internal const string ShouldDraw = "E8 ?? ?? ?? ?? 84 C0 75 18 48 8D 0D ?? ?? ?? ?? B3 01"; // hasel
        internal const string WorldTravel = "40 55 53 56 57 41 54 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? B8";
        internal const string WorldTravelSetupInfo = "48 8B CB E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ??";
        internal const string InventoryManagerUniqueItemCheck = "E8 ?? ?? ?? ?? 44 8B E0 EB 29";
        internal const string ItemIsUniqueConditionalJump = "75 4D";
        internal const string FreeCompanyDialogPacketReceive = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 42 31"; // xan
        internal const string SendLogout = "40 53 48 83 EC ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 ?? 48 8B 0D"; // Client::Game::Event::EventSceneModuleUsualImpl.Logout	push    rbx
        internal const string ProcessSentChat = "E8 ?? ?? ?? ?? FE 86 ?? ?? ?? ?? C7 86 ?? ?? ?? ?? ?? ?? ?? ??";
        internal const string RetrieveMateria = "E8 ?? ?? ?? ?? EB 27 48 8B 01"; // Client::UI::Agent::AgentMaterialize.ReceiveEvent	call    sub_140B209C0
        internal const string AgentMateriaAttachReceiveEvent = "E8 ?? ?? ?? ?? 84 C0 74 7E 48 8B CB"; // look around sub_1416B7280
        internal const string CanDismount = "E8 ?? ?? ?? ?? F3 0F 10 74 24 ?? F3 0F 10 3D ?? ?? ?? ??"; // needs more testing, I don't think this actually is useful for dismount checking
        internal const string UnableToExecuteCommandWhileJumping = "40 53 48 83 EC 20 48 8D 99 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 84 C0 75 12"; // xan
    }

    public static class Delegates
    {
        internal delegate void AbandonDutyDelegate(bool a1);
        internal delegate byte AgentReturnReceiveEventDelegate(AgentInterface* agent);
        internal delegate nint AgentWorldTravelReceiveEventDelegate(Structs.AgentWorldTravel* agent, nint a2, nint a3, nint a4, long eventCase);
        internal delegate byte CanDismountDelegate(nint a1, float* a2, Vector3* gameObjectPosition);
        internal delegate ulong EnqueueSnipeTaskDelegate(EventSceneModuleImplBase* scene, lua_State* state);
        internal delegate nint ExecuteCommandDelegate(int command, int a1 = 0, int a2 = 0, int a3 = 0, int a4 = 0);
        internal delegate nint ExecuteCommandComplexLocationDelegate(int command, Vector3 position, int param1, int param2, int param3, int param4);
        internal delegate void FreeCompanyDialogPacketReceiveDelegate(InfoProxyInterface* ptr, byte* packetData);
        internal delegate nint IsFlightProhibited(nint a1);
        internal delegate long IsItemUniqueDelegate(InventoryManager* ptr, uint a1, uint a2, byte a3);
        internal delegate bool FollowQuestRecastDelegate(nint a1, nint a2, nint a3, nint a4, nint a5, nint a6);
        internal delegate long KbProcDelegate(long gameobj, float rot, float length, long a4, char a5, int a6);
        internal delegate nint NoBewitchActionDelegate(CSGameObject* gameObj, float x, float y, float z, int a5, nint a6);
        internal delegate void ReceiveAchievementProgressDelegate(Achievement* achievement, uint id, uint current, uint max);
        internal delegate void RetrieveMateriaDelegate(EventFramework* framework, int eventID, InventoryType inventoryType, short inventorySlot, int extraParam);
        internal delegate void RidePillionDelegate(BattleChara* target, int seatIndex);
        internal delegate void SalvageItemDelegate(AgentSalvage* thisPtr, InventoryItem* item, int addonId, byte a4);
        internal delegate byte ShouldDrawDelegate(CameraBase* thisPtr, CSGameObject* gameObject, Vector3* sceneCameraPos, Vector3* lookAtVector);
        internal delegate nint WorldTravelSetupInfoDelegate(nint worldTravel, ushort currentWorld, ushort targetWorld);
        internal delegate byte UnableToExecuteCommandWhileJumpingDelegate(Character* character);
    }

    internal Delegates.RidePillionDelegate? RidePillion = EzDelegate.Get<Delegates.RidePillionDelegate>(Signatures.RidePillion);
    internal Delegates.SalvageItemDelegate? SalvageItem = EzDelegate.Get<Delegates.SalvageItemDelegate>(Signatures.SalvageItem);
    internal Delegates.AbandonDutyDelegate? AbandonDuty = EzDelegate.Get<Delegates.AbandonDutyDelegate>(Signatures.AbandonDuty);
    internal Delegates.AgentWorldTravelReceiveEventDelegate? WorldTravel = EzDelegate.Get<Delegates.AgentWorldTravelReceiveEventDelegate>(Signatures.WorldTravel);
    internal Delegates.WorldTravelSetupInfoDelegate? WorldTravelSetupInfo = EzDelegate.Get<Delegates.WorldTravelSetupInfoDelegate>(Signatures.WorldTravelSetupInfo);
    internal Delegates.RetrieveMateriaDelegate? RetrieveMateria = EzDelegate.Get<Delegates.RetrieveMateriaDelegate>(Signatures.RetrieveMateria);
    internal Delegates.ExecuteCommandDelegate? ExecuteCommand = EzDelegate.Get<Delegates.ExecuteCommandDelegate>(Signatures.ExecuteCommand);
    internal Delegates.UnableToExecuteCommandWhileJumpingDelegate? UnableToExecuteCommandWhileJumping = EzDelegate.Get<Delegates.UnableToExecuteCommandWhileJumpingDelegate>(Signatures.UnableToExecuteCommandWhileJumping);

    public Memory() => EzSignatureHelper.Initialize(this);

    public class Hook
    {
        public Hook() => EzSignatureHelper.Initialize(this);
    }

    public void Dispose() { }

    #region PacketDispatcher
    public class PacketDispatcher : Hook
    {
        internal delegate void PacketDispatcher_OnReceivePacket(nint a1, uint a2, nint a3);
        [EzHook(Signatures.PacketDispatcher_OnReceivePacketHookSig, false)]
        internal EzHook<PacketDispatcher_OnReceivePacket> PacketDispatcher_OnReceivePacketHook = null!;
        [EzHook(Signatures.PacketDispatcher_OnReceivePacketHookSig, false)]
        internal EzHook<PacketDispatcher_OnReceivePacket> PacketDispatcher_OnReceivePacketMonitorHook = null!;

        internal delegate byte PacketDispatcher_OnSendPacket(nint a1, nint a2, nint a3, byte a4);
        [EzHook(Signatures.PacketDispatcher_OnSendPacketHook, false)]
        internal EzHook<PacketDispatcher_OnSendPacket> PacketDispatcher_OnSendPacketHook = null!;

        internal List<uint> DisallowedSentPackets = [];
        internal List<uint> DisallowedReceivedPackets = [];

        private byte PacketDispatcher_OnSendPacketDetour(nint a1, nint a2, nint a3, byte a4)
        {
            const byte DefaultReturnValue = 1;

            if (a2 == nint.Zero)
            {
                PluginLog.Error("[HyperFirewall] Error: Opcode pointer is null.");
                return DefaultReturnValue;
            }

            try
            {
                Events.OnPacketSent(a1, a2, a3, a4);
                var opcode = *(ushort*)a2;

                if (DisallowedSentPackets.Contains(opcode))
                {
                    PluginLog.Verbose($"[HyperFirewall] Suppressing outgoing packet with opcode {opcode}.");
                }
                else
                {
                    PluginLog.Verbose($"[HyperFirewall] Passing outgoing packet with opcode {opcode} through.");
                    return PacketDispatcher_OnSendPacketHook.Original(a1, a2, a3, a4);
                }
            }
            catch (Exception e)
            {
                PluginLog.Error($"[HyperFirewall] Exception caught while processing opcode: {e.Message}");
                e.Log();
                return DefaultReturnValue;
            }

            return DefaultReturnValue;
        }

        private void PacketDispatcher_OnReceivePacketDetour(nint a1, uint a2, nint a3)
        {
            if (a3 == nint.Zero)
            {
                PluginLog.Error("[HyperFirewall] Error: Data pointer is null.");
                return;
            }

            try
            {
                Events.OnPacketRecieved(a1, a2, a3);
                var opcode = *(ushort*)(a3 + 2);

                if (DisallowedReceivedPackets.Contains(opcode))
                {
                    PluginLog.Verbose($"[HyperFirewall] Suppressing incoming packet with opcode {opcode}.");
                }
                else
                {
                    PluginLog.Verbose($"[HyperFirewall] Passing incoming packet with opcode {opcode} through.");
                    PacketDispatcher_OnReceivePacketHook.Original(a1, a2, a3);
                }
            }
            catch (Exception e)
            {
                PluginLog.Error($"[HyperFirewall] Exception caught while processing opcode: {e.Message}");
                e.Log();
                return;
            }

            return;
        }
    }
    #endregion

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

    #region Flight Prohibited
    public class FlightProhibited : Hook
    {
        /// <remarks>
        /// This only resolves on the client side, e.g. other people will not see you fly. Packet filter if using.
        /// </remarks>
        [EzHook(Signatures.FlightProhibited, false)]
        internal readonly EzHook<Delegates.IsFlightProhibited> IsFlightProhibitedHook = null!;

        internal unsafe nint IsFlightProhibitedDetour(nint a1)
        {
            try
            {
                if (!PlayerEx.InFlightAllowedTerritory // don't detour in zones where flight is impossible normally
                    || PlayerEx.AllowedToFly // don't detour in zones where you can already fly
                    || !Svc.Condition[ConditionFlag.Mounted]) // don't detour if you aren't mounted
                    return IsFlightProhibitedHook.Original(a1);
                else
                    return 0;
            }
            catch (Exception e)
            {
                e.Log();
            }
            return IsFlightProhibitedHook.Original(a1);
        }
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
            if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 6) != 0 || Player.IsInPvP)
                return ReturnHook.Original(agent);

            if (Svc.Party.Length > 1)
            {
                if (Svc.Party[0]?.Name == Svc.ClientState.LocalPlayer?.Name)
                    Chat.Instance.SendMessage("/partycmd breakup");
                else
                    Chat.Instance.SendMessage("/leave");
            }

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

    #region Camera Object Culling
    public class CameraObjectCulling : Hook
    {
        [EzHook(Signatures.ShouldDraw, false)]
        internal readonly EzHook<Delegates.ShouldDrawDelegate> ShouldDrawHook = null!;
        internal byte ShouldDrawDetour(CameraBase* thisPtr, CSGameObject* gameObject, Vector3* sceneCameraPos, Vector3* lookAtVector) => 1;
    }
    #endregion

    #region Unique Item Check Bypass
    public class AllowUniqueItems : Hook
    {
        [EzHook(Signatures.InventoryManagerUniqueItemCheck, false)]
        internal readonly EzHook<Delegates.IsItemUniqueDelegate> UniqueItemCheckHook = null!;
        internal long IgnoreUniqueCheckDetour(InventoryManager* ptr, uint a1, uint a2, byte a3)
        {
            Svc.Log.Info($"{nameof(IgnoreUniqueCheckDetour)}: [{a1} {a2} {a3}]");
            return UniqueItemCheckHook.Original(ptr, a1, a2, a3);
        }

        private byte[] _prePatchData = null!;
        // 0x90 = no-op
        internal unsafe void IgnoreUniqueCheck()
        {
            Dalamud.SafeMemory.ReadBytes(Svc.SigScanner.ScanModule(Signatures.ItemIsUniqueConditionalJump), 2, out var prePatch);
            _prePatchData = prePatch;
            Dalamud.SafeMemory.WriteBytes(Svc.SigScanner.ScanModule(Signatures.ItemIsUniqueConditionalJump), [0x90, 0x90]);
            //Dalamud.SafeMemory.Write(Svc.SigScanner.ScanText(Signatures.ItemIsUniqueConditionalJump), new byte[] { 0x90, 0x90 });
        }

        internal unsafe void Reset() => Dalamud.SafeMemory.WriteBytes(Svc.SigScanner.ScanModule(Signatures.ItemIsUniqueConditionalJump), _prePatchData);
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
    #endregion

    #region Materia
    public unsafe void MaterializeAction(GameInventoryItem item, MaterializeEventId eventId)
    {
        try
        {
            var _item = (InventoryItem*)item.Address;
            RetrieveMateria?.Invoke(EventFramework.Instance(), (int)eventId, _item->Container, _item->Slot, 0);
        }
        catch (Exception e) { e.Log(); }
    }
    #endregion

    #region Can Dismount
    public class DismountCheck : Hook
    {
        [EzHook(Signatures.CanDismount, false)]
        internal readonly EzHook<Delegates.CanDismountDelegate> CanDismountHook = null!;

        private byte CanDismountDetour(nint a1, float* a2, Vector3* a3)
        {
            TryExecute(() => Svc.Log.Info($"{nameof(CanDismountDetour)}: [{a1} {*a2} {*a3}]"));
            return CanDismountHook.Original(a1, a2, a3);
        }
    }
    #endregion
}


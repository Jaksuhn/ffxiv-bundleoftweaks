using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Runtime.InteropServices;
using PlayerController = ComplexTweaks.Utilities.Structs.PlayerController;
#nullable disable

namespace ComplexTweaks.Utilities;

public static unsafe class PlayerEx {
    extension(Player) {
        public static unsafe Camera* Camera => CameraManager.Instance()->GetActiveCamera();
        public static PlayerController* Controller => (PlayerController*)Svc.SigScanner.GetStaticAddressFromSig(Memory.Signatures.PlayerController);

        public static float Speed { get => Player.Controller->MoveControllerWalk.BaseMovementSpeed; set => Player.Object.SetSpeed(6 * value); }
        public static byte ReviveState => Player.IsDead ? AgentRevive.Instance()->ReviveState : (byte)0;

        public static FlagMapMarker MapFlag => AgentMap.Instance()->FlagMapMarkers[0];
    }

    public static Vector3 Position { get => Svc.Objects.LocalPlayer.Position; set => Player.GameObject->SetPosition(value.X, value.Y, value.Z); }
    public static List<MapMarkerData> QuestLocations => [.. FFXIVClientStructs.FFXIV.Client.Game.UI.Map.Instance()->QuestMarkers.ToArray().SelectMany(i => i.MarkerData.ToList())];

    public static void SetSpeed(this IPlayerCharacter _, float speedBase) {
        Svc.SigScanner.TryScanText("F3 0F 11 05 ?? ?? ?? ?? 40 38 2D", out var address);
        address = address + 4 + Marshal.ReadInt32(address + 4) + 4;
        Dalamud.SafeMemory.Write(address + 20, speedBase);
        SetMoveControlData(speedBase);
    }

    private static unsafe void SetMoveControlData(float speed)
        => Dalamud.SafeMemory.Write(((delegate* unmanaged[Stdcall]<byte, nint>)Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 48 85 C0 74 AE 83 FD 05"))(1) + 8, speed);
}

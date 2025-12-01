using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Runtime.InteropServices;

namespace ComplexTweaks.Utilities;

public static unsafe class Structs {
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct PlayerController {
        [FieldOffset(0x10)] public PlayerMoveControllerWalk MoveControllerWalk;
        [FieldOffset(0x150)] public PlayerMoveControllerFly MoveControllerFly;
        [FieldOffset(0x559)] public byte ControlMode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x140)]
    public unsafe struct PlayerMoveControllerWalk {
        [FieldOffset(0x10)] public Vector3 MovementDir;
        [FieldOffset(0x58)] public float BaseMovementSpeed;
        [FieldOffset(0x90)] public float MovementDirRelToCharacterFacing;
        [FieldOffset(0x94)] public byte Forced;
        [FieldOffset(0xA0)] public Vector3 MovementDirWorld;
        [FieldOffset(0xB0)] public float RotationDir;
        [FieldOffset(0x110)] public uint MovementState;
        [FieldOffset(0x114)] public float MovementLeft;
        [FieldOffset(0x118)] public float MovementFwd;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xB0)]
    public unsafe struct PlayerMoveControllerFly {
        [FieldOffset(0x10)] public float unk10; // x coord?
        [FieldOffset(0x14)] public float unk14; // y coord?
        [FieldOffset(0x18)] public float unk18; // z coord?
        [FieldOffset(0x40)] public float unk40;
        [FieldOffset(0x44)] public float unk44;
        [FieldOffset(0x48)] public uint unk48;
        [FieldOffset(0x4C)] public uint unk4C;
        [FieldOffset(0x50)] public uint unk50;
        [FieldOffset(0x58)] public float unk58;
        [FieldOffset(0x5C)] public float unk5C;
        [FieldOffset(0x66)] public byte IsFlying;
        [FieldOffset(0x88)] public uint unk88;
        [FieldOffset(0x8C)] public uint unk8C;
        [FieldOffset(0x90)] public uint unk90;
        [FieldOffset(0x94)] public float unk94; // speed?
        [FieldOffset(0x98)] public float unk98;
        [FieldOffset(0x9C)] public float AngularAscent;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x1BD0)]
    public unsafe partial struct Character {
        [FieldOffset(0x386)] public byte IsFlying;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct AgentWorldTravel {
        [FieldOffset(0)]
        public AgentInterface AgentInterface;

        [FieldOffset(76)]
        public uint WorldToTravel;

        public static AgentWorldTravel* Instance() => (AgentWorldTravel*)AgentModule.Instance()->GetAgentByInternalId(AgentId.WorldTravel);
    }
}

using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel.Sheets;
using System.IO;

namespace Automaton.Utilities;
public sealed class RelayPayload(MapLinkPayload mapLink, uint worldId, uint? instance, uint relayType, uint originChannel) : DalamudLinkPayload
{
    private const byte EmbeddedInfoTypeByte = (byte)(EmbeddedInfoType.DalamudLink + 4);

    public MapLinkPayload MapLink => mapLink;
    public World World => GetRow<World>(worldId)!.Value;
    public uint? Instance => instance ?? default;
    public uint RelayType => relayType;
    public uint OriginChannel => originChannel;

    public override PayloadType Type => PayloadType.Unknown;

    private RelayPayload() : this(new MapLinkPayload(0, 0, 0, 0), 0, 0, 0, 0) { }

    protected override byte[] EncodeImpl()
    {
        var data = new List<byte>();
        data.AddRange(mapLink.Encode());
        data.AddRange(MakeInteger(worldId));
        data.AddRange(MakeInteger(instance ?? 0));
        data.AddRange(MakeInteger(relayType));
        data.AddRange(MakeInteger(originChannel));

        var length = 2 + (byte)data.Count;
        return [
            START_BYTE,
            (byte)SeStringChunkType.Interactable,
            (byte)length,
            EmbeddedInfoTypeByte,
            .. data,
            END_BYTE,
        ];
    }

    protected override void DecodeImpl(BinaryReader reader, long _)
    {
        mapLink = (MapLinkPayload)Decode(reader);
        worldId = GetInteger(reader);
        instance = GetInteger(reader);
        relayType = GetInteger(reader);
        originChannel = GetInteger(reader);
    }

    public override string ToString() => $"{nameof(RelayPayload)}[{mapLink}, {worldId}, {instance}, {relayType}, {originChannel}]";

    public static bool operator ==(RelayPayload? left, RelayPayload? right)
    {
        if (left is null) return right is null;
        return left.World.RowId == right?.World.RowId && left.Instance == right.Instance && left.RelayType == right.RelayType && left.MapLink.TerritoryType.RowId == right.MapLink.TerritoryType.RowId
            && Vector2.Distance(new(left.MapLink.RawX, left.MapLink.RawY), new(right.MapLink.RawX, right.MapLink.RawY)) < 3;
    }

    public static bool operator !=(RelayPayload? left, RelayPayload? right) => !(left == right);

    public RawPayload ToRawPayload() => new(EncodeImpl());

    public static RelayPayload? Parse(RawPayload payload)
    {
        using var stream = new MemoryStream(payload.Data);
        using var reader = new BinaryReader(stream);

        if (reader.ReadByte() != START_BYTE)
        {
            return default;
        }

        if (reader.ReadByte() != (byte)SeStringChunkType.Interactable)
        {
            return default;
        }

        var length = reader.ReadByte();
        if (reader.ReadByte() != EmbeddedInfoTypeByte)
        {
            return default;
        }

        var result = new RelayPayload();
        result.DecodeImpl(reader, /* unused */ default);
        return result;
    }
}

using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace ComplexTweaks.Utilities;

public static class SSRanks {
    public static Vector2 Lakeland => new(23.3f, 22.1f);
    public static Vector2 AmhAraeng => new(27.5f, 35.2f);
    public static Vector2 IlMheg => new(13.5f, 23.0f);
    public static Vector2 Raktika => new(24.48f, 37.34f);
    public static Vector2 Kholusia => new(34.09f, 10.03f);
    public static Vector2 TheTempest => new(12.9f, 22.2f);

    public static Vector2 Labyrinthos => new(25.56f, 16.13f);
    public static Vector2 Thavnair => new(24.3f, 16.8f);
    public static Vector2 Garlemald => new(20.3f, 23.7f);
    public static Vector2 MareLamentorum => new(18.6f, 30.2f);
    public static Vector2 Elpis => new(22.7f, 19.5f);
    public static Vector2 UltimaThule => new(14.05f, 29.41f);

    public static Vector2 YakTel => new(29.59f, 18.56f);
    public static Vector2 Urqopacha => new(26.0f, 27.9f);
    public static Vector2 Kozamauka => new(13.8f, 14.8f);
    public static Vector2 Shaaloni => new(13.35f, 14.33f);
    public static Vector2 HeritageFound => new(17.46f, 21.22f);
    public static Vector2 LivingMemory => new(34.4f, 26.3f);

    public static Vector2 TerritoryToSSRank(this uint territory, MapLinkPayload maplink) => territory switch {
        831 => Lakeland,
        814 => Kholusia,
        815 => AmhAraeng,
        816 => IlMheg,
        817 => Raktika,
        818 => TheTempest,

        956 => Labyrinthos,
        957 => Thavnair,
        958 => Garlemald,
        959 => MareLamentorum,
        961 => Elpis,
        960 => UltimaThule,

        1189 => YakTel,
        1187 => Urqopacha,
        1188 => Kozamauka,
        1190 => Shaaloni,
        1191 => HeritageFound,
        1192 => LivingMemory,
        _ => new(maplink.RawX, maplink.RawY)
    };
}

using Dalamud.Plugin;
using System.Collections;
using System.Reflection;

namespace ComplexTweaks.Tasks.SonarKiller;

public static class SonarReader {
    public readonly record struct HuntEntry(string Key, string Name, string Rank, int Level, uint WorldId, uint TerritoryId, uint InstanceId, Vector3 Position, bool Alive, double LastSeen);
    public static bool IsLoaded => Svc.Interface.InstalledPlugins.Any(p => p.InternalName == _internalName && p.IsLoaded);

    private const string _internalName = "SonarPlugin";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static List<HuntEntry> GetHunts(string? rank = null) {
        var result = new List<HuntEntry>();
        if (GetPluginInstance() is not { } plugin) return result;
        if (GetField(plugin, "_container") is not IServiceProvider container) return result;
        if (plugin.GetType().Assembly.GetType("SonarPlugin.Trackers.RelayTrackerViews") is not { } viewsType) return result;
        if (container.GetService(viewsType) is not { } views) return result;

        var view = rank is null ? Get(views, "Hunts") : GetRankView(views, rank);
        if (view is null || Get(Get(view, "Data"), "States") is not IEnumerable states) return result;

        foreach (var kv in states) {
            var state = Get(kv, "Value");
            var relay = Get(state, "Relay");
            var info = Get(relay, "Info");
            var coords = Get(relay, "Coords")!; // SonarVector3: X/Y horizontal, Z height
            var ct = coords.GetType();
            result.Add(new HuntEntry(
                Key: (string)Get(relay, "RelayKey")!,
                Name: Get(info, "Name")?.ToString() ?? "?",
                Rank: Get(info, "Rank")?.ToString() ?? "",
                Level: (int)Get(info, "Level")!,
                WorldId: (uint)Get(relay, "WorldId")!,
                TerritoryId: (uint)Get(relay, "ZoneId")!,
                InstanceId: (uint)Get(relay, "InstanceId")!,
                Position: new Vector3((float)ct.GetField("X")!.GetValue(coords)!, (float)ct.GetField("Z")!.GetValue(coords)!, (float)ct.GetField("Y")!.GetValue(coords)!),
                Alive: (uint)Get(relay, "CurrentHp")! != 0,
                LastSeen: (double)Get(state, "LastSeen")!));
        }
        return result;
    }

    public static bool Remove(string key) {
        if (GetPluginInstance() is not { } plugin) return false;
        var data = Get(Get(Get(Get(plugin, "Client"), "Trackers"), "Hunts"), "Data")!;
        return data.GetType().GetMethod("Remove", [typeof(string)])?.Invoke(data, [key]) is true;
    }

    private static object? GetRankView(object views, string rank) {
        if (Get(views, "HuntsByRank") is not IEnumerable byRank) return null;
        foreach (var kv in byRank) {
            if (Get(kv, "Key")?.ToString() == rank)
                return Get(kv, "Value");
        }
        return null;
    }

    private static object? GetPluginInstance() {
        var dalamud = typeof(IDalamudPluginInterface).Assembly;
        var pmType = dalamud.GetType("Dalamud.Plugin.Internal.PluginManager")!;
        var pm = dalamud.GetType("Dalamud.Service`1")!.MakeGenericType(pmType).GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        if (Get(pm, "InstalledPlugins") is not IEnumerable plugins) return null;
        foreach (var lp in plugins) {
            if ((string?)Get(lp, "InternalName") == _internalName)
                return GetField(lp, "instance");
        }
        return null;
    }

    private static object? Get(object? obj, string name) {
        ArgumentNullException.ThrowIfNull(obj, name);
        for (var t = obj.GetType(); t != null; t = t.BaseType) {
            if (t.GetProperty(name, Flags) is { } p)
                return p.GetValue(obj);
        }
        throw new MissingMemberException(obj.GetType().FullName, name);
    }

    private static object? GetField(object obj, string name) {
        for (var t = obj.GetType(); t != null; t = t.BaseType) {
            if (t.GetField(name, Flags) is { } f)
                return f.GetValue(obj);
        }
        throw new MissingMemberException(obj.GetType().FullName, name);
    }
}

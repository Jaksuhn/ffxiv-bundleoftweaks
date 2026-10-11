using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ComplexTweaks.TweakSystem.Packs;

public readonly record struct TweakPackInspection(bool Ok, string? Error);

public static class TweakPackMetadata {
    public static TweakPackInspection Inspect(string path) {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        int? api = null;
        foreach (var handle in reader.CustomAttributes) {
            var attr = reader.GetCustomAttribute(handle);
            if (attr.Parent.Kind != HandleKind.AssemblyDefinition || !IsTweakPackAttribute(reader, attr.Constructor))
                continue;

            var blob = reader.GetBlobReader(attr.Value);
            if (blob.ReadUInt16() != 1)
                continue;
            api = blob.ReadInt32();
            break;
        }

        if (api is null)
            return new(false, "Pack is missing TweakPackAttribute.");
        if (api != TweakPackApi.Current)
            return new(false, $"Pack API {api} does not match {Svc.Interface.Manifest.Name} API #{TweakPackApi.Current}.");

        return new(true, null);
    }

    private static bool IsTweakPackAttribute(MetadataReader reader, EntityHandle constructor) {
        var parent = constructor.Kind switch {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };

        if (parent.Kind != HandleKind.TypeReference)
            return false;

        var type = reader.GetTypeReference((TypeReferenceHandle)parent);
        return reader.GetString(type.Name) == nameof(TweakPackAttribute) && reader.GetString(type.Namespace) == typeof(TweakPackAttribute).Namespace;
    }
}

internal static class TweakActivation {
    [ThreadStatic] public static string? PackId;
}

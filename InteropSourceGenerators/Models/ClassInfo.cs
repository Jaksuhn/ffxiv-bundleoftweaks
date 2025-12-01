namespace InteropSourceGenerators.Models;

internal sealed record ClassInfo(
    string FullyQualifiedMetadataName,
    string Namespace,
    string[] Hierarchy) {
    public string Name => Hierarchy[0];
}

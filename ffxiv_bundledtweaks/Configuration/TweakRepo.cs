namespace ComplexTweaks.Configuration;

public sealed class TweakRepo {
    public string Url = "";
    public string Owner = "";
    public string Name = "";
    public string? InstalledTag;
    public string? LastError;

    public string Id => $"{Owner}/{Name}";
}

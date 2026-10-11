using ComplexTweaks.Configuration;
using ComplexTweaks.TweakSystem.Packs;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Services;

public sealed class TweakPackService : IPluginService, IAsyncDisposable {
    private readonly Dictionary<string, LoadedPack> _loaded = [];
    private readonly HashSet<string> _updating = [];

    public TweakPackService() {
        foreach (var repo in ConfigService.Get().Config.Repos)
            QueueUpdate(repo);
    }

    public void Add(TweakRepo repo) {
        var repos = ConfigService.Get().Config.Repos;
        if (repos.Any(existing => existing.Id == repo.Id))
            return;

        repos.Add(repo);
        ConfigService.Get().Save();
        QueueUpdate(repo);
    }

    public void QueueUpdate(TweakRepo repo) {
        if (!_updating.Add(repo.Id))
            return;
        _ = UpdateCore(repo);
    }

    public void Reload(TweakRepo repo) {
        Unload(repo);
        if (!TryLoadInstalled(repo) && repo.LastError == null)
            repo.LastError = "No cached tweaks.dll.";
        ConfigService.Get().Save();
    }

    public void Remove(TweakRepo repo) {
        Unload(repo);
        ConfigService.Get().Config.Repos.Remove(repo);
        var directory = Path.Combine(CacheRoot, repo.Owner, repo.Name);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        ConfigService.Get().Save();
    }

    public bool IsUpdating(TweakRepo repo) => _updating.Contains(repo.Id);

    public ValueTask DisposeAsync() {
        foreach (var repo in ConfigService.Get().Config.Repos.ToArray())
            Unload(repo);
        return ValueTask.CompletedTask;
    }

    private async Task UpdateCore(TweakRepo repo) {
        try {
            GitHubRelease latest;
            try {
                latest = await GitHubPackClient.GetLatestAsync(repo.Owner, repo.Name, CancellationToken.None);
                var downloaded = CachePath(repo, latest.Tag);
                if (!File.Exists(downloaded))
                    await GitHubPackClient.DownloadAsync(latest.DownloadUrl, downloaded, CancellationToken.None);
            }
            catch (Exception ex) {
                await IFramework.Get().Run(() => {
                    repo.LastError = ex.Message;
                    TryLoadInstalled(repo);
                    ConfigService.Get().Save();
                });
                return;
            }

            var path = CachePath(repo, latest.Tag);
            await IFramework.Get().Run(() => Apply(repo, latest.Tag, path));
        }
        finally {
            _updating.Remove(repo.Id);
        }
    }

    private void Apply(TweakRepo repo, string tag, string path) {
        var inspection = TweakPackMetadata.Inspect(path);
        if (!inspection.Ok) {
            repo.LastError = inspection.Error;
            TryLoadInstalled(repo);
            ConfigService.Get().Save();
            return;
        }

        if (_loaded.TryGetValue(repo.Id, out var loaded) && loaded.Tag == tag) {
            repo.LastError = null;
            ConfigService.Get().Save();
            return;
        }

        Unload(repo);
        repo.InstalledTag = tag;
        repo.LastError = null;
        LoadFile(repo, path);
        ConfigService.Get().Save();
    }

    private bool TryLoadInstalled(TweakRepo repo) {
        if (repo.InstalledTag is not { } tag)
            return false;
        if (_loaded.TryGetValue(repo.Id, out var loaded) && loaded.Tag == tag)
            return true;

        var path = CachePath(repo, tag);
        if (!File.Exists(path))
            return false;

        var inspection = TweakPackMetadata.Inspect(path);
        if (!inspection.Ok) {
            repo.LastError = inspection.Error;
            return false;
        }

        Unload(repo);
        repo.LastError = null;
        LoadFile(repo, path);
        return true;
    }

    private void LoadFile(TweakRepo repo, string path) {
        var context = new TweakPackLoadContext();
        LoadedPack? pack = null;
        try {
            using var stream = File.OpenRead(path);
            var assembly = context.LoadFromStream(stream);
            pack = new LoadedPack(context, repo.InstalledTag ?? "");
            _loaded[repo.Id] = pack;

            foreach (var type in GetLoadableTypes(assembly)) {
                if (type is not { IsClass: true, IsAbstract: false } || !typeof(Tweak).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                TweakActivation.PackId = repo.Id;
                Tweak tweak;
                try {
                    tweak = (Tweak)Activator.CreateInstance(type)!;
                }
                catch (Exception ex) {
                    ex.Log($"Failed to initialize {type.Name} from {repo.Id}");
                    continue;
                }
                finally {
                    TweakActivation.PackId = null;
                }

                pack.Tweaks.Add(tweak);
                TweakService.Get().Add(tweak);
            }

            foreach (var tweak in pack.Tweaks)
                _ = tweak.StartAsync(CancellationToken.None);
        }
        catch (Exception ex) {
            repo.LastError = ex.Message;
            if (pack != null)
                Unload(repo);
            else
                context.Unload();
            ex.Log($"Failed to load tweak pack {repo.Id}");
        }
    }

    private void Unload(TweakRepo repo) {
        if (!_loaded.Remove(repo.Id, out var pack))
            return;

        foreach (var tweak in pack.Tweaks) {
            tweak.Dispose();
            TweakService.Get().Remove(tweak);
        }
        pack.Tweaks.Clear();
        pack.Context.Unload();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly) {
        try {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex) {
            return ex.Types.Where(type => type != null)!;
        }
    }

    private static string CacheRoot => Path.Combine(Svc.Interface.GetPluginConfigDirectory(), "packs");

    private static string CachePath(TweakRepo repo, string tag)
        => Path.Combine(CacheRoot, repo.Owner, repo.Name, tag, GitHubPackClient.AssetName);

    private sealed class LoadedPack(TweakPackLoadContext context, string tag) {
        public TweakPackLoadContext Context { get; } = context;
        public string Tag { get; } = tag;
        public List<Tweak> Tweaks { get; } = [];
    }
}

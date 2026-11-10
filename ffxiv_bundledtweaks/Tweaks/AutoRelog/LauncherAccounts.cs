using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ComplexTweaks.Tweaks;

internal sealed record LauncherAccount(string UserName, bool UseOtp, bool UseSteamServiceAccount);

internal static partial class LauncherAccounts {
    public static LauncherAccount? Current(string launcherDirectory) {
        try {
            var accounts = JsonSerializer.Deserialize<List<LauncherAccount>>(File.ReadAllText(Path.Combine(launcherDirectory, "accountsList.json")));
            // if there's only one account then current doesn't matter
            if (accounts is [var only])
                return only;

            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(launcherDirectory, "launcherConfigV3.json")));
            // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher/Accounts/AccountManager.cs#L15-L22
            if (!config.RootElement.TryGetProperty("CurrentAccountId", out var id) || id.GetString() is not { Length: > 0 } currentId)
                return null;
            // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher/Accounts/XivAccount.cs#L14
            return accounts?.FirstOrDefault(a => string.Equals($"{a.UserName}-{a.UseOtp}-{a.UseSteamServiceAccount}", currentId, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
            return null;
        }
    }

    public static unsafe string? ReadPassword(string squareEnixId) {
        // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher/Accounts/XivAccount.cs#L11
        if (!CredRead($"XIVLAUNCHER-{squareEnixId.ToLowerInvariant()}", 1, 0, out var credential))
            return null;
        try {
            // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher/Accounts/XivAccount.cs#L21-L53
            return new string((char*)credential->CredentialBlob, 0, (int)credential->CredentialBlobSize / sizeof(char));
        }
        finally {
            CredFree(credential);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CredRead(string target, int type, int flags, out Credential* credential);

    [LibraryImport("advapi32.dll")]
    private static unsafe partial void CredFree(void* buffer);
}

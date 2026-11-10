using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace ComplexTweaks.Tweaks;

/// <summary>
/// A Steam auth session ticket, requested through the steam_api64.dll the game already loaded. The game's own
/// callback reports when it's ready. Steam calls and reads stay on the framework thread.
/// </summary>
internal sealed partial class SteamTicket {
    private const int ResultOk = 1;
    private const int BufferSize = 2048;
    private const int PollIntervalTicks = 5;
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(15);

    public EncodedTicket Encoded { get; private set; }

    public static async Task<SteamTicket> Request(CancellationToken token) {
        var framework = IFramework.Get();
        var ticket = await framework.Run(Begin, token);
        try {
            var deadline = Environment.TickCount64 + (long)CallbackTimeout.TotalMilliseconds;
            while (!await framework.Run(ticket.IsValidated, token)) {
                if (Environment.TickCount64 >= deadline)
                    throw new LoginException(LoginFailure.Other, "Steam didn't answer the login ticket request in time.");
                await framework.DelayTicks(PollIntervalTicks, token);
            }

            var time = await framework.Run(ServerTime, token);
            ticket.Encoded = await Task.Run(() => TicketEncoding.Encode(ticket._buffer.AsSpan(0, (int)ticket._size), time), token);
            return ticket;
        }
        catch {
            await ticket.Cancel();
            throw;
        }
    }

    public Task Cancel() => IFramework.Get().Run(CancelNow);
}

internal sealed unsafe partial class SteamTicket {
    private readonly nint _steamUser;
    private readonly delegate* unmanaged<nint, uint, void> _cancel;
    private readonly byte[] _buffer = new byte[BufferSize];
    private readonly uint _handle;
    private readonly uint _size;

    private SteamTicket(nint steamUser, delegate* unmanaged<nint, uint, void> cancel, nint getTicket) {
        _steamUser = steamUser;
        _cancel = cancel;
        fixed (byte* buffer = _buffer) {
            uint size = 0;
            _handle = ((delegate* unmanaged<nint, byte*, int, uint*, void*, uint>)getTicket)(steamUser, buffer, BufferSize, &size, null);
            _size = size;
        }
        if (_handle == 0)
            throw new LoginException(LoginFailure.Other, "Steam didn't issue a login ticket.");
    }

    public static bool IsAvailable => Framework.Instance()->IsSteamApiInitialized();
    private static uint ServerTime() => Framework.Instance()->SteamApi->GetSteamServerTime();
    private void CancelNow() => _cancel(_steamUser, _handle);

    private static SteamTicket Begin() {
        var library = Framework.Instance()->SteamApiLibraryHandle;
        var user = ((delegate* unmanaged<nint>)Export(library, "SteamAPI_SteamUser_v023"))();
        if (user == 0)
            throw new LoginException(LoginFailure.Other, "Steam's user interface isn't available.");
        if (((delegate* unmanaged<nint, byte>)Export(library, "SteamAPI_ISteamUser_BLoggedOn"))(user) == 0)
            throw new LoginException(LoginFailure.Other, "Not logged in to Steam, or Steam is in offline mode.");

        var cancel = (delegate* unmanaged<nint, uint, void>)Export(library, "SteamAPI_ISteamUser_CancelAuthTicket");
        return new SteamTicket(user, cancel, Export(library, "SteamAPI_ISteamUser_GetAuthSessionTicket"));
    }

    private bool IsValidated() {
        var api = Framework.Instance()->SteamApi;
        if (!api->AuthTicketPresent || api->AuthTicket.HAuthTicket != _handle)
            return false;
        if (api->AuthTicket.EResult != ResultOk)
            throw new LoginException(LoginFailure.Other, $"Steam refused the login ticket (result {api->AuthTicket.EResult}).");
        return true;
    }

    private static nint Export(nint library, string name)
        => NativeLibrary.TryGetExport(library, name, out var address) ? address : throw new LoginException(LoginFailure.Other, $"This Steam library has no {name}.");
}

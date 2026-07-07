namespace Beacon.Core.Games.Steam;

public readonly record struct SteamShortcutLaunchId(ulong Value)
{
    private const ulong LowerBits = 0x02000000UL;

    public static SteamShortcutLaunchId FromStoredAppId(int appId) =>
        new(((ulong)unchecked((uint)appId) << 32) | LowerBits);

    public string ToUri() => $"steam://rungameid/{Value}";
}

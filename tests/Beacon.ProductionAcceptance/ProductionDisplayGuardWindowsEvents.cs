namespace Beacon.ProductionAcceptance;

internal static class ProductionDisplayGuardWindowsEvents
{
    internal const uint PowerBroadcast = 0x0218;
    internal const uint SessionChange = 0x02B1;
    internal const uint HotKey = 0x0312;
    internal const nuint ResumeSuspend = 0x07;
    internal const nuint ResumeAutomatic = 0x12;
    internal const nuint SessionUnlock = 0x08;
    internal const nuint EmergencyHotKeyId = 0xB501;

    public static ProductionDisplayGuardTrigger? Classify(uint message, nuint parameter) =>
        (message, parameter) switch
        {
            (PowerBroadcast, ResumeSuspend or ResumeAutomatic) =>
                ProductionDisplayGuardTrigger.PowerResumed,
            (SessionChange, SessionUnlock) =>
                ProductionDisplayGuardTrigger.SessionUnlocked,
            (HotKey, EmergencyHotKeyId) =>
                ProductionDisplayGuardTrigger.EmergencyRequested,
            _ => null,
        };
}

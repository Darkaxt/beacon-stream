using Beacon.Server.State;

namespace Beacon.Server.Tests;

public sealed class InMemoryClientStoreTests
{
    [Fact]
    public void UnknownClientCapabilitiesUseH264SdrOnlyFallback()
    {
        var store = new InMemoryClientStore(new InMemoryClientProfileRepository());

        Beacon.Core.Clients.EndpointCapabilities capabilities = store.GetCapabilities("unknown-client");

        Assert.False(capabilities.Av1);
        Assert.False(capabilities.Hevc);
        Assert.True(capabilities.H264);
        Assert.False(capabilities.Hdr10);
        Assert.False(capabilities.VirtualDisplayHdrSupported);
    }
}

using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsDisplayNameMapTests
{
    [Fact]
    public void RememberPersistsMappingForNextInstance()
    {
        using var tempFile = TempDisplayMapFile.Create();
        var first = new WindowsDisplayNameMap(new WindowsDisplayNameMapStore(tempFile.Path));

        first.Remember("client-z-fold-7", @"\\.\DISPLAY9");

        var second = new WindowsDisplayNameMap(new WindowsDisplayNameMapStore(tempFile.Path));
        Assert.True(second.TryResolveDisplayName("client-z-fold-7", out string? displayName));
        Assert.Equal(@"\\.\DISPLAY9", displayName);
        Assert.Equal(
            "client-z-fold-7",
            second.CreateDisplayIdByDisplayNameSnapshot()[@"\\.\DISPLAY9"]);
    }

    [Fact]
    public void ForgetRemovesPersistedMappingForNextInstance()
    {
        using var tempFile = TempDisplayMapFile.Create();
        var first = new WindowsDisplayNameMap(new WindowsDisplayNameMapStore(tempFile.Path));
        first.Remember("client-z-fold-7", @"\\.\DISPLAY9");

        first.Forget("client-z-fold-7");

        var second = new WindowsDisplayNameMap(new WindowsDisplayNameMapStore(tempFile.Path));
        Assert.False(second.TryResolveDisplayName("client-z-fold-7", out _));
        Assert.Empty(second.CreateDisplayIdByDisplayNameSnapshot());
    }

    [Fact]
    public void RememberReassignsRecycledWindowsDisplayNameToLatestLease()
    {
        using var tempFile = TempDisplayMapFile.Create();
        var first = new WindowsDisplayNameMap(new WindowsDisplayNameMapStore(tempFile.Path));
        first.Remember("client-old", @"\\.\DISPLAY9");

        first.Remember("client-current", @"\\.\DISPLAY9");

        Assert.False(first.TryResolveDisplayName("client-old", out _));
        Assert.True(first.TryResolveDisplayName("client-current", out string? displayName));
        Assert.Equal(@"\\.\DISPLAY9", displayName);
        Assert.Equal(
            "client-current",
            first.CreateDisplayIdByDisplayNameSnapshot()[@"\\.\DISPLAY9"]);

        var second = new WindowsDisplayNameMap(new WindowsDisplayNameMapStore(tempFile.Path));
        Assert.False(second.TryResolveDisplayName("client-old", out _));
        Assert.True(second.TryResolveDisplayName("client-current", out _));
    }

    private sealed class TempDisplayMapFile : IDisposable
    {
        private TempDisplayMapFile(string directory)
        {
            Directory = directory;
            Path = System.IO.Path.Combine(directory, "display-map.json");
        }

        public string Directory { get; }

        public string Path { get; }

        public static TempDisplayMapFile Create()
        {
            string directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "beacon-display-map-tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            return new TempDisplayMapFile(directory);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}

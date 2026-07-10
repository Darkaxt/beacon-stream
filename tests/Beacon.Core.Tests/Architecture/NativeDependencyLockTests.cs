using System.Text.Json;

namespace Beacon.Core.Tests.Architecture;

public sealed class NativeDependencyLockTests
{
    private static readonly IReadOnlyDictionary<string, ExpectedDependency> ExpectedDependencies =
        new Dictionary<string, ExpectedDependency>(StringComparer.Ordinal)
        {
            ["msquic"] = new(
                "https://github.com/microsoft/msquic.git",
                "87b53085d76bd7920d490a6f226c9999b6614d14",
                "MIT"),
            ["nv-codec-headers"] = new(
                "https://github.com/FFmpeg/nv-codec-headers.git",
                "15ee32753c92faddbabbff11676779618fc6db7e",
                "Permissive header notice"),
            ["opus"] = new(
                "https://github.com/xiph/opus.git",
                "22244de5a79bd1d6d623c32e72bf1954b56235be",
                "BSD-3-Clause"),
            ["protobuf"] = new(
                "https://github.com/protocolbuffers/protobuf.git",
                "7fcfd66022455635fa29af92987cdc0967efd4f3",
                "BSD-3-Clause"),
            ["quictls"] = new(
                "https://github.com/quictls/openssl.git",
                "ff36838bb69801cad56823159a036977bcbe5c75",
                "Apache-2.0"),
            ["xdp-for-windows"] = new(
                "https://github.com/microsoft/xdp-for-windows.git",
                "f23b1fb4d492d9c20bcd7767bba2278f94355df8",
                "MIT")
        };

    [Fact]
    public void NativeDependenciesArePinnedToAuditedImmutableRevisions()
    {
        string root = FindRepositoryRoot();
        string lockPath = Path.Combine(root, "native", "dependencies.lock.json");
        Assert.True(File.Exists(lockPath), "native/dependencies.lock.json must exist.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockPath));
        JsonElement rootElement = document.RootElement;
        Assert.Equal(1, rootElement.GetProperty("schemaVersion").GetInt32());

        JsonElement androidNdk = rootElement.GetProperty("toolchains").GetProperty("androidNdk");
        Assert.Equal("r27d", androidNdk.GetProperty("revision").GetString());
        Assert.Equal("27.3.13750724", androidNdk.GetProperty("version").GetString());
        Assert.Equal(
            "https://dl.google.com/android/repository/android-ndk-r27d-linux.zip",
            androidNdk.GetProperty("linuxArchive").GetString());
        Assert.Equal(
            "22105e410cf29afcf163760cc95522b9fb981121",
            androidNdk.GetProperty("linuxSha1").GetString());

        JsonElement dependencies = rootElement.GetProperty("dependencies");
        Assert.Equal(ExpectedDependencies.Count, dependencies.EnumerateObject().Count());
        foreach ((string name, ExpectedDependency expected) in ExpectedDependencies)
        {
            JsonElement actual = dependencies.GetProperty(name);
            Assert.Equal(expected.Repository, actual.GetProperty("repository").GetString());
            Assert.Equal(expected.Revision, actual.GetProperty("revision").GetString());
            Assert.Equal(expected.Revision, actual.GetProperty("ref").GetString());
            Assert.Equal(expected.License, actual.GetProperty("license").GetString());
            Assert.False(string.IsNullOrWhiteSpace(actual.GetProperty("path").GetString()));
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Beacon.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record ExpectedDependency(string Repository, string Revision, string License);
}

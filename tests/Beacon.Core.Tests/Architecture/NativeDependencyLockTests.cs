using System.Security.Cryptography;
using System.Text.Json;

namespace Beacon.Core.Tests.Architecture;

public sealed class NativeDependencyLockTests
{
    private static readonly IReadOnlyDictionary<string, ExpectedDependency> ExpectedDependencies =
        new Dictionary<string, ExpectedDependency>(StringComparer.Ordinal)
        {
            ["abseil-cpp"] = new(
                "https://github.com/abseil/abseil-cpp.git",
                "76bb24329e8bf5f39704eb10d21b9a80befa7c81",
                "Apache-2.0"),
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

        JsonElement protobufCompiler = rootElement.GetProperty("toolchains").GetProperty("protobufCompiler");
        Assert.Equal("32.1", protobufCompiler.GetProperty("version").GetString());
        Assert.Equal(
            "https://github.com/protocolbuffers/protobuf/releases/download/v32.1/protoc-32.1-win64.zip",
            protobufCompiler.GetProperty("windowsArchive").GetString());
        Assert.Equal(
            "69569cbc178cd5785ecb7d93569913110677eafeb4b8f82970c361fad4c7cd66",
            protobufCompiler.GetProperty("windowsSha256").GetString());
        Assert.Equal(
            "https://github.com/protocolbuffers/protobuf/releases/download/v32.1/protoc-32.1-linux-x86_64.zip",
            protobufCompiler.GetProperty("linuxArchive").GetString());
        Assert.Equal(
            "e9c129c176bb7df02546c4cd6185126ca53c89e7d2f09511e209319704b5dd7e",
            protobufCompiler.GetProperty("linuxSha256").GetString());

        JsonElement windowsAppSdk = rootElement.GetProperty("toolchains").GetProperty("windowsAppSdk");
        Assert.Equal("2.3.1", windowsAppSdk.GetProperty("releaseVersion").GetString());
        Assert.Equal("2.3.5", windowsAppSdk.GetProperty("foundationVersion").GetString());
        Assert.Equal(
            "15d78449c8566f889e0f95ae6ebc0610d79b32f95ce296b621f54b8ec8f496cb",
            windowsAppSdk.GetProperty("foundationSha256").GetString());
        Assert.Equal("2.1.3", windowsAppSdk.GetProperty("interactiveExperiencesVersion").GetString());
        Assert.Equal(
            "e8063437eb853b5abe4dc6b6bdb2ecfb82f49ec281764c82cd04b4d94e144f68",
            windowsAppSdk.GetProperty("interactiveExperiencesSha256").GetString());
        Assert.Equal("2.3.1", windowsAppSdk.GetProperty("runtimeVersion").GetString());
        Assert.Equal(
            "f15c6c682a81a019e13beaee512de9fb83ffd5a1f3e83b99209b6860a7aebba2",
            windowsAppSdk.GetProperty("runtimeSha256").GetString());

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

    [Fact]
    public void VendoredNvencHeaderMatchesPinnedAuditArtifact()
    {
        string root = FindRepositoryRoot();
        string headerPath = Path.Combine(
            root,
            "native",
            "vendor",
            "nv-codec-headers",
            "include",
            "ffnvcodec",
            "nvEncodeAPI.h");
        string licensePath = Path.Combine(
            root,
            "native",
            "vendor",
            "nv-codec-headers",
            "LICENSE.nvEncodeAPI.txt");

        Assert.True(File.Exists(headerPath), "The pinned nvEncodeAPI.h must be vendored.");
        Assert.True(File.Exists(licensePath), "The nvEncodeAPI.h license notice must be retained.");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(headerPath)));
        Assert.Equal("8776FDDCB8FEBC6AEC4D73989B1F21831EB30306BC583DA55B4BF0C14A1DC228", hash);
        Assert.Contains("Copyright (c) 2010-2026 NVIDIA Corporation", File.ReadAllText(licensePath));
        Assert.Contains("Permission is hereby granted, free of charge", File.ReadAllText(licensePath));
    }

    [Fact]
    public void WindowsAppSdkInstallerAlwaysRemovesTransientArchives()
    {
        string root = FindRepositoryRoot();
        string installer = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "install-windows-app-sdk.ps1"));

        Assert.Contains("finally", installer, StringComparison.Ordinal);
        Assert.Contains(
            "Remove-Item -LiteralPath $staging -Recurse -Force",
            installer,
            StringComparison.Ordinal);
        Assert.Contains("Nuspec", installer, StringComparison.Ordinal);
        Assert.Contains("ExpectedId", installer, StringComparison.Ordinal);
        Assert.Contains("$Package.Version", installer, StringComparison.Ordinal);
        Assert.Contains(".projection-version", installer, StringComparison.Ordinal);
        Assert.Contains(
            "Split-Path (Split-Path (Split-Path $cppwinrt -Parent) -Parent) -Leaf",
            installer,
            StringComparison.Ordinal);
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

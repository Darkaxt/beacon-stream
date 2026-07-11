using System.Text;
using Beacon.StreamWorker.Contracts.Diagnostics;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Stream.V1;
using Google.Protobuf;

namespace Beacon.StreamWorker.Contracts.Tests;

public sealed class ProtocolContractTests
{
    [Fact]
    public void SelectedVideoEnums_RepresentEveryServerGrantMode()
    {
        Assert.Equal(1, (int)VideoCodec.H264);
        Assert.Equal(2, (int)VideoCodec.Hevc);
        Assert.Equal(3, (int)VideoCodec.Av1);
        Assert.Equal(1, (int)DynamicRange.Sdr);
        Assert.Equal(2, (int)DynamicRange.Hdr10);
    }

    [Fact]
    public void ProtocolVersion_RejectsUnsupportedVersions()
    {
        ProtocolVersion.EnsureSupported(ProtocolVersion.Current);

        var error = Assert.Throws<UnsupportedProtocolVersionException>(
            () => ProtocolVersion.EnsureSupported(ProtocolVersion.Current + 1));

        Assert.Equal(ProtocolVersion.Current + 1, error.ReceivedVersion);
    }

    [Fact]
    public void DiagnosticRendering_RedactsRawStreamTickets()
    {
        const string secret = "raw-secret-ticket";
        var envelope = new PublicSessionEnvelope
        {
            ProtocolVersion = ProtocolVersion.Current,
            ServerAddress = "127.0.0.1",
            ServerPort = 4433,
            StreamTicket = ByteString.CopyFromUtf8(secret),
            PinnedServerFingerprint = ByteString.CopyFromUtf8("fingerprint"),
            SelectedVideo = new SelectedVideoMode
            {
                Codec = VideoCodec.H264,
                Width = 2560,
                Height = 1600,
                FramesPerSecondNumerator = 120,
                FramesPerSecondDenominator = 1,
                DynamicRange = DynamicRange.Sdr,
            },
            PlanRevision = 9,
            PlanExplanation = "Measured path supports this plan.",
        };

        var rendered = ContractDiagnosticFormatter.Format(envelope);

        Assert.DoesNotContain(secret, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), rendered, StringComparison.Ordinal);
        Assert.Contains("<redacted:17-bytes>", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSessionEnvelope_ContainsOnlyApprovedLaunchFacts()
    {
        var actual = PublicSessionEnvelope.Descriptor.Fields.InFieldNumberOrder()
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);
        var approved = new HashSet<string>(StringComparer.Ordinal)
        {
            "protocol_version",
            "server_address",
            "server_port",
            "stream_ticket",
            "pinned_server_fingerprint",
            "selected_video",
            "plan_revision",
            "plan_explanation",
        };

        Assert.True(approved.SetEquals(actual), $"Unexpected public fields: {string.Join(", ", actual.Except(approved))}");

        var prohibitedFragments = new[]
        {
            "executable", "path", "backend", "selector", "launch_uri", "policy",
            "credential", "secret", "wrapper", "app_id", "application_id",
        };
        Assert.DoesNotContain(
            PublicSessionEnvelope.Descriptor.Fields.InFieldNumberOrder(),
            field => prohibitedFragments.Any(fragment => field.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void PublicStreamContracts_ContainNoServerOnlyExecutionOrPolicyFields()
    {
        var prohibited = new HashSet<string>(StringComparer.Ordinal)
        {
            "executable",
            "executable_path",
            "backend",
            "backend_name",
            "protocol_selector",
            "transport_selector",
            "launch_uri",
            "raw_policy",
            "policy",
            "credential",
            "long_lived_credential",
            "wrapper",
            "app_list_id",
            "application_id",
        };

        var fields = StreamControlReflection.Descriptor.MessageTypes
            .SelectMany(message => message.Fields.InFieldNumberOrder())
            .ToArray();

        Assert.DoesNotContain(fields, field => prohibited.Contains(field.Name));
    }
}

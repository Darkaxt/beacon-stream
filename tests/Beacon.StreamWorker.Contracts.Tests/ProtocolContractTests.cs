using System.Text;
using Beacon.StreamWorker.Contracts.Diagnostics;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Stream.V1;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.StreamWorker.Contracts.Tests;

public sealed class ProtocolContractTests
{
    [Fact]
    public void WorkerEvents_ReuseTypedStreamEnvelopesAndCarryGeneration()
    {
        var input = new InputStreamEnvelope
        {
            ProtocolVersion = 1,
            SessionId = "session-a",
            Sequence = 7,
            InputBatch = new InputBatch(),
        };
        var feedback = new FeedbackStreamEnvelope
        {
            ProtocolVersion = 1,
            SessionId = "session-a",
            Sequence = 9,
            QueueDepth = new QueueDepthFeedback { QueuedAccessUnits = 2 },
        };
        var envelope = new WorkerIpcEnvelope
        {
            ProtocolVersion = 1,
            RequestId = 0,
            SessionId = "session-a",
            InputReceived = new InputReceived
            {
                SessionGeneration = 4,
                Input = input,
            },
        };

        Assert.Equal(0UL, envelope.RequestId);
        Assert.Equal(4UL, envelope.InputReceived.SessionGeneration);
        Assert.Equal(input, envelope.InputReceived.Input);

        envelope.FeedbackReceived = new FeedbackReceived
        {
            SessionGeneration = 4,
            Feedback = feedback,
        };
        Assert.Equal(feedback, envelope.FeedbackReceived.Feedback);
        Assert.Contains(
            WorkerIpcReflection.Descriptor.MessageTypes,
            message => message.Name == nameof(MediaEvidence));
    }

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
    public void SelectedVideoMode_RepresentsHevcMain10Hdr10WithoutImplicitDefaults()
    {
        var video = new SelectedVideoMode
        {
            Codec = VideoCodec.Hevc,
            Width = 2560,
            Height = 1600,
            FramesPerSecondNumerator = 120,
            FramesPerSecondDenominator = 1,
            DynamicRange = DynamicRange.Hdr10,
            Profile = VideoProfile.HevcMain10,
            BitDepth = 10,
            ColorPrimaries = ColorPrimaries.Bt2020,
            TransferFunction = TransferFunction.Pq,
            MatrixCoefficients = MatrixCoefficients.Bt2020NonConstantLuminance,
            ColorRange = ColorRange.Limited,
            HdrStaticInfo = ByteString.CopyFrom(
                0, 0x48, 0x8a, 0x08, 0x39, 0x34, 0x21, 0xaa, 0x9b,
                0x96, 0x19, 0xfc, 0x08, 0x13, 0x3d, 0x42, 0x40,
                0xe8, 0x03, 0x32, 0x00, 0xe8, 0x03, 0x90, 0x01),
            HdrStaticInfoInBitstream = true,
        };

        Assert.Equal(VideoProfile.HevcMain10, video.Profile);
        Assert.Equal(10u, video.BitDepth);
        Assert.Equal(ColorPrimaries.Bt2020, video.ColorPrimaries);
        Assert.Equal(TransferFunction.Pq, video.TransferFunction);
        Assert.Equal(MatrixCoefficients.Bt2020NonConstantLuminance, video.MatrixCoefficients);
        Assert.Equal(ColorRange.Limited, video.ColorRange);
        Assert.Equal(25, video.HdrStaticInfo.Length);
        Assert.True(video.HdrStaticInfoInBitstream);
    }

    [Fact]
    public void SelectedAudioMode_RepresentsTheR2ProductionGrant()
    {
        var audio = new SelectedAudioMode
        {
            Codec = AudioCodec.Opus,
            SampleRateHz = 48_000,
            ChannelCount = 2,
            FrameDurationUs = 20_000,
            BitrateBps = 96_000,
        };

        Assert.Equal(1, (int)audio.Codec);
        Assert.Equal(48_000u, audio.SampleRateHz);
        Assert.Equal(2u, audio.ChannelCount);
        Assert.Equal(20_000u, audio.FrameDurationUs);
        Assert.Equal(96_000u, audio.BitrateBps);
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
            SelectedAudio = new SelectedAudioMode
            {
                Codec = AudioCodec.Opus,
                SampleRateHz = 48_000,
                ChannelCount = 2,
                FrameDurationUs = 20_000,
                BitrateBps = 96_000,
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
            "selected_audio",
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

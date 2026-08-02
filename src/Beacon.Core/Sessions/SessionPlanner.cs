using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Beacon.Core.Sessions;

public static class SessionPlanner
{
    public static SessionPlanResult CreatePlan(
        ClientProfile profile,
        EndpointCapabilities capabilities,
        BenchmarkPlanEvidence benchmark,
        GameDescriptor game)
    {
        ArgumentNullException.ThrowIfNull(benchmark);
        if (benchmark.RunId == Guid.Empty)
        {
            throw new ArgumentException("Benchmark run id must not be empty.", nameof(benchmark));
        }

        if (string.IsNullOrWhiteSpace(benchmark.Revision))
        {
            throw new ArgumentException("Benchmark evidence revision is required.", nameof(benchmark));
        }

        SelectedBenchmarkResult measured = benchmark.SelectedResult;
        try
        {
            BenchmarkEvidenceValidator.Validate(measured);
        }
        catch (ArgumentException error)
        {
            return new SessionPlanResult(false, null, $"Benchmark evidence is invalid: {error.Message}");
        }

        if (!SupportsCodec(measured.Codec, capabilities))
        {
            return new SessionPlanResult(
                false,
                null,
                $"Benchmark selected {DisplayCodec(measured.Codec)}, but current endpoint capabilities no longer advertise it.");
        }

        string codecPreference = profile.Stream.CodecPreference.Trim().ToLowerInvariant();
        if (codecPreference is not ("" or "auto") &&
            !codecPreference.Equals(measured.Codec, StringComparison.OrdinalIgnoreCase))
        {
            return new SessionPlanResult(
                false,
                null,
                $"Benchmark evidence selected {DisplayCodec(measured.Codec)}, but server profile policy requires {DisplayCodec(codecPreference)}.");
        }

        int fps = Math.Min(
            profile.Display.PreferredRefreshHz,
            Math.Min(Math.Max(1, capabilities.MaxFps), Math.Max(1, measured.MaxSustainableFps)));
        int bitrate = measured.InitialBitrateMbps;
        bool bitrateCapApplied = false;
        if (profile.Stream.BitrateCapMbps is > 0 && profile.Stream.BitrateCapMbps.Value < bitrate)
        {
            bitrate = profile.Stream.BitrateCapMbps.Value;
            bitrateCapApplied = true;
        }

        string transport = measured.RttMs >= 80 || measured.PacketLossPercent >= 2
            ? "lan-conservative"
            : "lan-direct";
        string congestionPolicy = measured.PowerConstrained
            ? "power-save"
            : measured.RttMs >= 80
            ? "latency-protect"
            : measured.PacketLossPercent >= 2
                ? "loss-protect"
                : "adaptive";
        var reasons = new List<string>(measured.Reasons)
        {
            $"Benchmark run {benchmark.RunId:D} selected {DisplayCodec(measured.Codec)} at up to {fps} FPS and {bitrate} Mbps."
        };
        if (bitrateCapApplied)
        {
            reasons.Add("Client bitrate cap limited the measured initial bitrate.");
        }

        var selection = new StreamPlanningSelection(
            Codec: measured.Codec.ToLowerInvariant(),
            Fps: fps,
            InitialBitrateMbps: bitrate,
            Transport: transport,
            CongestionPolicy: congestionPolicy,
            Reason: string.Join(" ", reasons),
            BenchmarkRunId: benchmark.RunId,
            BenchmarkEvidenceRevision: benchmark.Revision,
            CodecProfile: measured.Profile,
            BitDepth: measured.BitDepth,
            CertifiedWidth: measured.Width,
            CertifiedHeight: measured.Height,
            TenBitPresentationVerified: measured.TenBitPresentationVerified,
            HdrPresentationVerified: measured.HdrPresentationVerified);

        return CreatePlan(profile, capabilities, game, selection);
    }

    private static SessionPlanResult CreatePlan(
        ClientProfile profile,
        EndpointCapabilities capabilities,
        GameDescriptor game,
        StreamPlanningSelection selection)
    {
        string audioMode = profile.Audio.Mode.Trim().ToLowerInvariant();
        if (audioMode != "stereo")
        {
            return new SessionPlanResult(
                false,
                null,
                $"R2 audio supports only the server-owned stereo mode; requested mode was '{profile.Audio.Mode}'.");
        }

        string? hdrBlocker = GetHdrBlocker(capabilities, selection);

        if (profile.Display.HdrPreference == HdrPreference.Require && hdrBlocker is not null)
        {
            return new SessionPlanResult(false, null, $"HDR required but {hdrBlocker}.");
        }

        bool hdrEnabled = profile.Display.HdrPreference != HdrPreference.Off && hdrBlocker is null;
        string hdrReason = CreateHdrReason(profile.Display.HdrPreference, hdrEnabled, hdrBlocker);
        string displayReason =
            $"{CreateDisplayModeReason(profile.Display.Mode)} " +
            $"Display geometry {profile.Display.PreferredWidth}x{profile.Display.PreferredHeight} " +
            $"comes from registered client policy. {hdrReason}";

        var display = new PlannedDisplay(
            DisplayId: DisplayLease.CreateDisplayId(profile.ClientId),
            Width: profile.Display.PreferredWidth,
            Height: profile.Display.PreferredHeight,
            RefreshHz: profile.Display.PreferredRefreshHz,
            Mode: profile.Display.Mode,
            HdrPreference: profile.Display.HdrPreference,
            HdrEnabled: hdrEnabled,
            HdrMode: hdrEnabled ? "hdr10" : "sdr",
            Reason: displayReason);

        var stream = new PlannedStream(
            Codec: selection.Codec,
            Width: selection.CertifiedWidth,
            Height: selection.CertifiedHeight,
            Fps: selection.Fps,
            InitialBitrateMbps: selection.InitialBitrateMbps,
            Transport: selection.Transport,
            CongestionPolicy: selection.CongestionPolicy,
            Reason:
                $"{selection.Reason} Stream output uses certified benchmark mode " +
                $"{selection.CertifiedWidth}x{selection.CertifiedHeight}.",
            BenchmarkRunId: selection.BenchmarkRunId,
            BenchmarkEvidenceRevision: selection.BenchmarkEvidenceRevision)
        {
            CodecProfile = selection.CodecProfile,
            BitDepth = selection.BitDepth,
            TenBitPresentationVerified = selection.TenBitPresentationVerified,
            HdrPresentationVerified = selection.HdrPresentationVerified
        };

        var audio = new PlannedAudio(
            Codec: "opus",
            SampleRateHz: 48_000,
            ChannelCount: 2,
            FrameDurationUs: 20_000,
            BitrateBps: 96_000,
            Reason: "Server selected the fixed R2 Opus stereo production path.");

        var plan = new SessionPlan(
            SessionId: $"{profile.ClientId.Value}-{game.Id}",
            ClientId: profile.ClientId,
            AppId: game.Id,
            Display: display,
            Stream: stream,
            Audio: audio,
            Revision: CreateRevision(profile.ClientId, game.Id, display, stream, audio));

        return new SessionPlanResult(true, plan, null);
    }

    private static string? GetHdrBlocker(
        EndpointCapabilities capabilities,
        StreamPlanningSelection selection)
    {
        if (!capabilities.Hdr10)
        {
            return "client does not report HDR10 decoder/display capability";
        }

        if (!capabilities.VirtualDisplayHdrSupported)
        {
            return "virtual display does not report HDR capability";
        }

        if (selection.BitDepth != 10 ||
            !selection.TenBitPresentationVerified ||
            !selection.HdrPresentationVerified)
        {
            return "benchmark evidence did not certify 10-bit HDR presentation";
        }

        return null;
    }

    private static string CreateHdrReason(HdrPreference preference, bool hdrEnabled, string? hdrBlocker) =>
        preference switch
        {
            HdrPreference.Off => "HDR disabled by client profile.",
            HdrPreference.Prefer when hdrEnabled => "HDR enabled because the full advertised chain reports support.",
            HdrPreference.Prefer => $"HDR disabled because {hdrBlocker}.",
            HdrPreference.Require => "HDR required and available.",
            _ => "HDR mode resolved."
        };

    private static string DisplayCodec(string codec) =>
        codec.ToLowerInvariant() switch
        {
            "av1" => "AV1",
            "hevc" => "HEVC",
            "h264" => "H.264",
            _ => codec
        };

    private static bool SupportsCodec(string codec, EndpointCapabilities capabilities) =>
        codec.ToLowerInvariant() switch
        {
            "av1" => capabilities.Av1,
            "hevc" => capabilities.Hevc,
            "h264" => capabilities.H264,
            _ => false
        };

    private static string CreateDisplayModeReason(string mode)
    {
        string normalized = mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "physical-blackout" => "Display mode physical-blackout selected by server profile policy; physical display recovery remains available.",
            "extended" => "Display mode extended selected by server profile policy.",
            "virtual-primary" => "Display mode virtual-primary selected by server profile policy.",
            "" => "Display mode virtual-primary selected by default server policy.",
            _ => $"Display mode {mode} selected by server profile policy."
        };
    }

    private static ulong CreateRevision(
        ClientId clientId,
        string appId,
        PlannedDisplay display,
        PlannedStream stream,
        PlannedAudio audio)
    {
        using var material = new MemoryStream();
        using (var writer = new BinaryWriter(material, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(clientId.Value);
            writer.Write(appId);
            writer.Write(display.DisplayId);
            writer.Write(display.Width);
            writer.Write(display.Height);
            writer.Write(display.RefreshHz);
            writer.Write(display.Mode);
            writer.Write((int)display.HdrPreference);
            writer.Write(display.HdrEnabled);
            writer.Write(display.HdrMode);
            writer.Write(stream.Codec);
            writer.Write(stream.Width);
            writer.Write(stream.Height);
            writer.Write(stream.Fps);
            writer.Write(stream.InitialBitrateMbps);
            writer.Write(stream.Transport);
            writer.Write(stream.CongestionPolicy);
            writer.Write(stream.BenchmarkRunId.ToByteArray());
            writer.Write(stream.BenchmarkEvidenceRevision);
            writer.Write(stream.CodecProfile);
            writer.Write(stream.BitDepth);
            writer.Write(stream.TenBitPresentationVerified);
            writer.Write(stream.HdrPresentationVerified);
            writer.Write(audio.Codec);
            writer.Write(audio.SampleRateHz);
            writer.Write(audio.ChannelCount);
            writer.Write(audio.FrameDurationUs);
            writer.Write(audio.BitrateBps);
        }

        byte[] digest = SHA256.HashData(material.GetBuffer().AsSpan(0, checked((int)material.Length)));
        ulong revision = BinaryPrimitives.ReadUInt64BigEndian(digest);
        CryptographicOperations.ZeroMemory(digest);
        return revision == 0 ? 1 : revision;
    }

    private sealed record StreamPlanningSelection(
        string Codec,
        int Fps,
        int InitialBitrateMbps,
        string Transport,
        string CongestionPolicy,
        string Reason,
        Guid BenchmarkRunId,
        string BenchmarkEvidenceRevision,
        string CodecProfile,
        int BitDepth,
        int CertifiedWidth,
        int CertifiedHeight,
        bool TenBitPresentationVerified,
        bool HdrPresentationVerified);
}

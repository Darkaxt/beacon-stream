namespace Beacon.Core.Clients;

public static class ClientDisplayModeSelectionPolicy
{
    public static ClientDisplayMode Select(
        ClientDisplayMode? preferredMode,
        EndpointCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ClientDisplayMode current = ValidateMode(
            capabilities.CurrentDisplayMode,
            "Current client display mode");
        IReadOnlyList<ClientDisplayMode> supported = capabilities.SupportedDisplayModes
            ?? throw new ArgumentException("Supported client display modes are required.", nameof(capabilities));
        if (supported.Count == 0)
        {
            throw new ArgumentException("At least one supported client display mode is required.", nameof(capabilities));
        }

        ClientDisplayMode[] candidates = supported
            .Select(mode => ValidateMode(mode, "Supported client display mode"))
            .Distinct()
            .ToArray();
        if (!candidates.Contains(current))
        {
            throw new ArgumentException(
                "The current client display mode must be present in supported display modes.",
                nameof(capabilities));
        }

        ClientDisplayMode target = preferredMode is null
            ? current
            : ValidateMode(preferredMode, "Preferred client display mode");
        ClientDisplayMode? exact = candidates.FirstOrDefault(mode => mode == target);
        if (exact is not null)
        {
            return exact;
        }

        ClientDisplayMode[] sameAspect = candidates
            .Where(mode => HasSameAspectRatio(mode, target))
            .ToArray();
        return (sameAspect.Length > 0 ? sameAspect : candidates)
            .OrderBy(mode => AspectRatioDistance(mode, target))
            .ThenBy(mode => ResolutionDistance(mode, target))
            .ThenBy(mode => PixelArea(mode) > PixelArea(target) ? 1 : 0)
            .ThenBy(mode => mode.RefreshHz > target.RefreshHz ? 1 : 0)
            .ThenBy(mode => Math.Abs(mode.RefreshHz - target.RefreshHz))
            .ThenByDescending(mode => (long)mode.Width * mode.Height)
            .ThenByDescending(mode => mode.RefreshHz)
            .First();
    }

    private static ClientDisplayMode ValidateMode(ClientDisplayMode? mode, string label)
    {
        if (mode is null || !mode.IsValid)
        {
            throw new ArgumentException($"{label} must have positive width, height, and refresh rate.");
        }

        return mode.AsLandscape();
    }

    private static bool HasSameAspectRatio(ClientDisplayMode left, ClientDisplayMode right) =>
        (long)left.Width * right.Height == (long)right.Width * left.Height;

    private static decimal ResolutionDistance(ClientDisplayMode candidate, ClientDisplayMode target)
    {
        decimal widthDistance = Math.Abs(candidate.Width - target.Width) / (decimal)target.Width;
        decimal heightDistance = Math.Abs(candidate.Height - target.Height) / (decimal)target.Height;
        return (widthDistance * widthDistance) + (heightDistance * heightDistance);
    }

    private static decimal AspectRatioDistance(ClientDisplayMode candidate, ClientDisplayMode target) =>
        Math.Abs(
            (candidate.Width / (decimal)candidate.Height) -
            (target.Width / (decimal)target.Height));

    private static long PixelArea(ClientDisplayMode mode) => (long)mode.Width * mode.Height;
}

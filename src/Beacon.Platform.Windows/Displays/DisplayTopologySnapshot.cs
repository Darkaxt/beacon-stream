namespace Beacon.Platform.Windows.Displays;

public sealed record DisplayTopologySnapshot(IReadOnlyList<DisplayPathSnapshot> Paths, bool IsMirrorMode)
{
    public string Fingerprint =>
        string.Join(
            "|",
            Paths
                .OrderBy(path => path.DisplayId, StringComparer.Ordinal)
                .Select(path => $"{path.DisplayId}:{path.Kind}:{path.Width}x{path.Height}@{path.RefreshHz}:primary={path.IsPrimary}")) +
        $"|mirror={IsMirrorMode}";

    public bool PhysicalPrimaryVerified =>
        Paths.Any(path => path.Kind == DisplayPathKind.Physical && path.IsPrimary);

    public static DisplayTopologySnapshot PhysicalOnly(
        string physicalDisplayId,
        int width,
        int height,
        int refreshHz)
    {
        return new DisplayTopologySnapshot(
            [
                new DisplayPathSnapshot(
                    physicalDisplayId,
                    DisplayPathKind.Physical,
                    width,
                    height,
                    refreshHz,
                    IsPrimary: true)
            ],
            IsMirrorMode: false);
    }

    public static DisplayTopologySnapshot Extended(
        string physicalDisplayId,
        string virtualDisplayId,
        int width,
        int height,
        int refreshHz,
        bool virtualPrimary)
    {
        return new DisplayTopologySnapshot(
            [
                new DisplayPathSnapshot(
                    physicalDisplayId,
                    DisplayPathKind.Physical,
                    width,
                    height,
                    refreshHz,
                    IsPrimary: !virtualPrimary),
                new DisplayPathSnapshot(
                    virtualDisplayId,
                    DisplayPathKind.Virtual,
                    width,
                    height,
                    refreshHz,
                    IsPrimary: virtualPrimary)
            ],
            IsMirrorMode: false);
    }

    public static DisplayTopologySnapshot Mirrored(
        string physicalDisplayId,
        string virtualDisplayId,
        int width,
        int height,
        int refreshHz)
    {
        return new DisplayTopologySnapshot(
            [
                new DisplayPathSnapshot(
                    physicalDisplayId,
                    DisplayPathKind.Physical,
                    width,
                    height,
                    refreshHz,
                    IsPrimary: true),
                new DisplayPathSnapshot(
                    virtualDisplayId,
                    DisplayPathKind.Virtual,
                    width,
                    height,
                    refreshHz,
                    IsPrimary: false)
            ],
            IsMirrorMode: true);
    }

    public bool HasDisplayMode(string displayId, int width, int height, int refreshHz) =>
        Paths.Any(path =>
            path.DisplayId == displayId &&
            path.Width == width &&
            path.Height == height &&
            path.RefreshHz == refreshHz);

    public bool IsPrimary(string displayId) =>
        Paths.Any(path => path.DisplayId == displayId && path.IsPrimary);
}

public sealed record DisplayPathSnapshot(
    string DisplayId,
    DisplayPathKind Kind,
    int Width,
    int Height,
    int RefreshHz,
    bool IsPrimary);

public enum DisplayPathKind
{
    Physical,
    Virtual
}

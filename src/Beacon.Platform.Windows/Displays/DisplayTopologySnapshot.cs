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

    public static DisplayTopologySnapshot FromPaths(IReadOnlyList<DisplayPathSnapshot> paths)
    {
        bool mirrorMode = paths
            .GroupBy(path => new { path.X, path.Y, path.Width, path.Height })
            .Any(group => group.Count() > 1);

        return new DisplayTopologySnapshot(paths, mirrorMode);
    }

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
                    IsPrimary: true,
                    X: 0,
                    Y: 0)
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
                    IsPrimary: !virtualPrimary,
                    X: virtualPrimary ? width : 0,
                    Y: 0),
                new DisplayPathSnapshot(
                    virtualDisplayId,
                    DisplayPathKind.Virtual,
                    width,
                    height,
                    refreshHz,
                    IsPrimary: virtualPrimary,
                    X: virtualPrimary ? 0 : width,
                    Y: 0)
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
                    IsPrimary: true,
                    X: 0,
                    Y: 0),
                new DisplayPathSnapshot(
                    virtualDisplayId,
                    DisplayPathKind.Virtual,
                    width,
                    height,
                    refreshHz,
                    IsPrimary: false,
                    X: 0,
                    Y: 0)
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
    bool IsPrimary,
    int X = 0,
    int Y = 0);

public enum DisplayPathKind
{
    Physical,
    Virtual
}

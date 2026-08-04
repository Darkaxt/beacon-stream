namespace Beacon.Core.Clients;

public sealed record ClientDisplayMode(int Width, int Height, int RefreshHz)
{
    public bool IsValid => Width > 0 && Height > 0 && RefreshHz > 0;

    public ClientDisplayMode AsLandscape() =>
        Width >= Height ? this : new ClientDisplayMode(Height, Width, RefreshHz);

    public override string ToString() => $"{Width}x{Height}@{RefreshHz}";
}

namespace Beacon.Platform.Windows.Displays;

public sealed record DisplayOperationLogEntry(
    string Operation,
    string DisplayId,
    int? Width,
    int? Height,
    int? RefreshHz,
    bool? Primary,
    bool? HdrEnabled,
    string Reason,
    DisplayTopologySnapshot? Before,
    DisplayTopologySnapshot? After);

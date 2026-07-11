namespace DeskSnapshot.Models;

public enum DesktopIconDifferenceKind
{
    Unchanged,
    Moved,
    Added,
    Missing
}

public sealed class DesktopIconDifference
{
    public string Name { get; init; } = string.Empty;
    public DesktopIconDifferenceKind Kind { get; init; }
    public DesktopIconPosition? BackupIcon { get; init; }
    public DesktopIconPosition? CurrentIcon { get; init; }
    public bool IsAmbiguous { get; init; }
}

public sealed class DesktopLayoutComparisonResult
{
    public List<DesktopIconDifference> Differences { get; init; } = [];
    public bool VirtualSizeChanged { get; init; }
    public bool DpiChanged { get; init; }
    public bool MonitorCountChanged { get; init; }
    public bool MonitorLayoutChanged { get; init; }
    public int MovedCount => Differences.Count(item => item.Kind == DesktopIconDifferenceKind.Moved);
    public int AddedCount => Differences.Count(item => item.Kind == DesktopIconDifferenceKind.Added);
    public int MissingCount => Differences.Count(item => item.Kind == DesktopIconDifferenceKind.Missing);
    public int UnchangedCount => Differences.Count(item => item.Kind == DesktopIconDifferenceKind.Unchanged);
    public int AmbiguousCount => Differences.Count(item => item.IsAmbiguous);
    public bool EnvironmentChanged => VirtualSizeChanged || DpiChanged || MonitorCountChanged || MonitorLayoutChanged;
}

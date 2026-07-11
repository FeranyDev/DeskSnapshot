using DeskSnapshot.Models;

namespace DeskSnapshot.Tests;

internal static class TestLayoutFactory
{
    public static DesktopIconPosition Icon(string name, int x, int y, int order = 0) => new()
    {
        Name = name,
        X = x,
        Y = y,
        CaptureOrder = order
    };

    public static DesktopLayoutBackup Layout(
        IEnumerable<DesktopIconPosition>? icons = null,
        int width = 3840,
        int height = 2160,
        uint dpi = 144,
        List<DesktopMonitor>? monitors = null) => new()
    {
        Environment = new DesktopEnvironment
        {
            VirtualWidth = width,
            VirtualHeight = height,
            Dpi = dpi,
            MonitorCount = monitors?.Count ?? 1,
            Monitors = monitors ?? [Monitor("DISPLAY-A", 0, 0, width, height, true)]
        },
        Icons = icons?.ToList() ?? []
    };

    public static DesktopMonitor Monitor(
        string id,
        int left,
        int top,
        int width,
        int height,
        bool primary = false) => new()
    {
        Id = id,
        DeviceName = id,
        Name = id,
        Left = left,
        Top = top,
        Width = width,
        Height = height,
        IsPrimary = primary
    };

    public static DesktopLayoutBackup Backup(
        Guid? id = null,
        DateTimeOffset? createdAt = null,
        bool automatic = false,
        bool safety = false,
        Guid? relatedId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        IsAutomaticBackup = automatic,
        IsSafetyBackup = safety,
        RelatedBackupId = relatedId
    };
}

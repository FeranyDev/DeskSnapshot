namespace DeskSnapshot.Models;

public sealed class DesktopLayoutBackup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SchemaVersion { get; set; } = 2;
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public bool IsSafetyBackup { get; set; }
    public bool IsAutomaticBackup { get; set; }
    public Guid? RelatedBackupId { get; set; }
    public string TriggerReason { get; set; } = string.Empty;
    public DesktopEnvironment Environment { get; set; } = new();
    public List<DesktopIconPosition> Icons { get; set; } = [];
}
public sealed class DesktopEnvironment
{
    public int VirtualLeft { get; set; }
    public int VirtualTop { get; set; }
    public int VirtualWidth { get; set; }
    public int VirtualHeight { get; set; }
    public uint Dpi { get; set; }
    public int MonitorCount { get; set; }
    public List<DesktopMonitor> Monitors { get; set; } = [];
}

public sealed class DesktopMonitor
{
    public string Id { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class DesktopIconPosition
{
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int CaptureOrder { get; set; }
    public string MonitorId { get; set; } = string.Empty;
    public int MonitorOffsetX { get; set; }
    public int MonitorOffsetY { get; set; }
}

public sealed record RestoreResult(int Restored, int Missing, int Failed, int Remapped = 0)
{
    public bool IsSuccess => Failed == 0;
}

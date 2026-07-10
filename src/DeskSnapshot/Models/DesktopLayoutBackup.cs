namespace DeskSnapshot.Models;

public sealed class DesktopLayoutBackup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public bool IsSafetyBackup { get; set; }
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
}

public sealed class DesktopIconPosition
{
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int CaptureOrder { get; set; }
}

public sealed record RestoreResult(int Restored, int Missing, int Failed)
{
    public bool IsSuccess => Failed == 0;
}

namespace DeskSnapshot.Models;

public sealed class BackupListItem
{
    public DesktopLayoutBackup Backup { get; set; } = new();
    public string Name => Backup.Name;
    public string CreatedAtDisplay => Backup.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd  HH:mm");
    public string Details => $"{Backup.Icons.Count} 个图标  ·  {Backup.Environment.VirtualWidth} × {Backup.Environment.VirtualHeight}  ·  {Backup.Environment.MonitorCount} 台显示器";
    public string Badge => Backup.IsSafetyBackup
        ? "安全备份"
        : Backup.IsAutomaticBackup
            ? "自动备份"
            : "手动备份";
}

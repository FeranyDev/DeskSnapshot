namespace DeskSnapshot.Models;

public sealed class AppSettings
{
    public bool AutoBackupEnabled { get; set; }
    public bool ScheduledBackupEnabled { get; set; } = true;
    public int BackupIntervalMinutes { get; set; } = 30;
    public bool BackupOnStartup { get; set; }
    public bool BackupOnDisplayChange { get; set; } = true;
    public bool BackupOnDesktopChange { get; set; }
    public int AutomaticBackupRetention { get; set; } = 20;
    public bool RunAtStartup { get; set; }
    public bool MinimizeToTray { get; set; }
    public string UiLanguage { get; set; } = "system";
}

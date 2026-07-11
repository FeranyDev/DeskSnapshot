using DeskSnapshot.Services;
using Microsoft.UI.Xaml;

namespace DeskSnapshot.Models;

public sealed class DisplayProfileListItem
{
    public DesktopLayoutBackup Backup { get; set; } = new();
    public bool IsCurrentMatch { get; set; }
    public string Name => Backup.DisplayProfileName;
    public string SavedAt => LocalizationService.Format("ProfileSavedAtFormat", Backup.CreatedAt.LocalDateTime.ToString("g"));
    public string Summary => LocalizationService.Format(
        "ProfileSummaryFormat",
        Backup.Environment.MonitorCount,
        Backup.Environment.VirtualWidth,
        Backup.Environment.VirtualHeight,
        Backup.Icons.Count);
    public string MonitorNames => string.Join("  ·  ", Backup.Environment.Monitors.Select(monitor => monitor.Name));
    public string Fingerprint => Backup.DisplayTopologyFingerprint;
    public Visibility MatchVisibility => IsCurrentMatch ? Visibility.Visible : Visibility.Collapsed;
}

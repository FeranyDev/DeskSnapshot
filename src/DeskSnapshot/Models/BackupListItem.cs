using System.ComponentModel;
using DeskSnapshot.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DeskSnapshot.Models;

public sealed class BackupListItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DesktopLayoutBackup Backup { get; set; } = new();
    public string RelatedBackupName { get; set; } = string.Empty;
    public int HierarchyDepth { get; set; }
    public Thickness RowMargin => new(Math.Min(HierarchyDepth, 3) * 28, 0, 0, 0);
    public Visibility ChildConnectorVisibility => HierarchyDepth > 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            NotifySelectionChanged();
        }
    }
    public Brush SelectionBackground => new SolidColorBrush(IsSelected
        ? ColorHelper.FromArgb(38, 0, 120, 212)
        : Colors.Transparent);
    public Brush SelectionBorderBrush => new SolidColorBrush(IsSelected
        ? ColorHelper.FromArgb(220, 0, 120, 212)
        : Colors.Transparent);
    public Thickness SelectionBorderThickness => IsSelected ? new Thickness(1.5) : new Thickness(0);
    public Brush SelectionBoxBackground => new SolidColorBrush(IsSelected
        ? ColorHelper.FromArgb(255, 0, 120, 212)
        : Colors.Transparent);
    public Brush SelectionBoxBorderBrush => new SolidColorBrush(IsSelected
        ? ColorHelper.FromArgb(255, 0, 120, 212)
        : ColorHelper.FromArgb(210, 90, 90, 90));
    public Visibility SelectionCheckVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
    public string Name => Backup.Name;
    public string TimeDisplay => Backup.CreatedAt.LocalDateTime.ToString("HH:mm");
    public string FullTimeDisplay => Backup.CreatedAt.LocalDateTime.ToString("G");
    public string Details => $"{LocalizationService.Format("IconCountFormat", Backup.Icons.Count)}  ·  {Backup.Environment.VirtualWidth} × {Backup.Environment.VirtualHeight}  ·  {LocalizationService.Format("MonitorCountFormat", Backup.Environment.MonitorCount)}";
    public string TypeTitle => Backup.IsSafetyBackup
        ? LocalizationService.Get("SafetyBackup")
        : Backup.IsAutomaticBackup
            ? LocalizationService.Get("AutomaticBackup")
            : LocalizationService.Get("ManualBackup");
    public string TypeGlyph => Backup.IsSafetyBackup
        ? "\uE72E"
        : Backup.IsAutomaticBackup
            ? "\uE823"
            : "\uE74E";
    public string EventSummary => Backup.IsSafetyBackup
        ? string.IsNullOrWhiteSpace(GetRelatedBackupName())
            ? LocalizationService.Get("SafetySummary")
            : LocalizationService.Format("SafetySummaryNamed", GetRelatedBackupName())
        : Backup.IsAutomaticBackup
            ? LocalizationService.Format("AutomaticReasonFormat", GetAutomaticReason())
            : string.IsNullOrWhiteSpace(Backup.Note)
                ? Backup.Name
                : $"{Backup.Name} · {Backup.Note}";

    private string GetAutomaticReason()
    {
        if (!string.IsNullOrWhiteSpace(Backup.TriggerReason))
        {
            return LocalizeReason(Backup.TriggerReason);
        }

        string[] knownReasons = ["图标布局变化", "显示环境变化", "程序启动", "定时"];
        var knownReason = knownReasons.FirstOrDefault(Backup.Name.Contains);
        if (knownReason is not null)
        {
            return LocalizeReason(knownReason);
        }

        var separator = Backup.Name.IndexOf('·');
        if (separator >= 0)
        {
            var reason = Backup.Name[(separator + 1)..].Trim();
            var timeStart = reason.LastIndexOf(' ');
            if (timeStart > 0)
            {
                reason = reason[..timeStart];
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                return reason;
            }
        }

        return LocalizationService.Get("ReasonRule");
    }

    private static string LocalizeReason(string reason) => reason switch
    {
        "desktop-change" or "图标布局变化" or "icon layout change" => LocalizationService.Get("ReasonDesktopChange"),
        "display-change" or "显示环境变化" or "display environment change" => LocalizationService.Get("ReasonDisplayChange"),
        "startup" or "程序启动" or "app launch" => LocalizationService.Get("ReasonStartup"),
        "schedule" or "定时" => LocalizationService.Get("ReasonSchedule"),
        _ => reason
    };

    private string GetRelatedBackupName()
    {
        if (!string.IsNullOrWhiteSpace(RelatedBackupName))
        {
            return RelatedBackupName;
        }

        const char leftQuote = '“';
        const char rightQuote = '”';
        var start = Backup.Note.IndexOf(leftQuote);
        var end = Backup.Note.IndexOf(rightQuote, start + 1);
        return start >= 0 && end > start
            ? Backup.Note[(start + 1)..end]
            : string.Empty;
    }

    private void NotifySelectionChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBackground)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBorderBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBorderThickness)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBoxBackground)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBoxBorderBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckVisibility)));
    }
}

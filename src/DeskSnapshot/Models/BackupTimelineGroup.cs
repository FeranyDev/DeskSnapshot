using System.Collections.ObjectModel;
using DeskSnapshot.Services;

namespace DeskSnapshot.Models;

public sealed class BackupTimelineGroup : ObservableCollection<BackupListItem>
{
    public string Header { get; set; } = string.Empty;
    public string CountText => LocalizationService.Format("RecordCountFormat", Count);
}

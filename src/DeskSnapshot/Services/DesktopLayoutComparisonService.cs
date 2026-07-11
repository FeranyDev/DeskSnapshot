using DeskSnapshot.Models;

namespace DeskSnapshot.Services;

public static class DesktopLayoutComparisonService
{
    private const int PositionTolerance = 2;

    public static DesktopLayoutComparisonResult Compare(
        DesktopLayoutBackup backup,
        DesktopLayoutBackup current)
    {
        var backupGroups = GroupIcons(backup.Icons);
        var currentGroups = GroupIcons(current.Icons);
        var allNames = backupGroups.Keys
            .Concat(currentGroups.Keys)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);
        var differences = new List<DesktopIconDifference>(Math.Max(backup.Icons.Count, current.Icons.Count));

        foreach (var name in allNames)
        {
            backupGroups.TryGetValue(name, out var backupIcons);
            currentGroups.TryGetValue(name, out var currentIcons);
            backupIcons ??= [];
            currentIcons ??= [];
            var ambiguous = backupIcons.Count > 1 || currentIcons.Count > 1;
            var pairedCount = Math.Min(backupIcons.Count, currentIcons.Count);

            for (var index = 0; index < pairedCount; index++)
            {
                var backupIcon = backupIcons[index];
                var currentIcon = currentIcons[index];
                differences.Add(new DesktopIconDifference
                {
                    Name = name,
                    BackupIcon = backupIcon,
                    CurrentIcon = currentIcon,
                    IsAmbiguous = ambiguous,
                    Kind = HasMoved(backupIcon, currentIcon)
                        ? DesktopIconDifferenceKind.Moved
                        : DesktopIconDifferenceKind.Unchanged
                });
            }

            for (var index = pairedCount; index < backupIcons.Count; index++)
            {
                differences.Add(new DesktopIconDifference
                {
                    Name = name,
                    BackupIcon = backupIcons[index],
                    IsAmbiguous = ambiguous,
                    Kind = DesktopIconDifferenceKind.Missing
                });
            }

            for (var index = pairedCount; index < currentIcons.Count; index++)
            {
                differences.Add(new DesktopIconDifference
                {
                    Name = name,
                    CurrentIcon = currentIcons[index],
                    IsAmbiguous = ambiguous,
                    Kind = DesktopIconDifferenceKind.Added
                });
            }
        }

        return new DesktopLayoutComparisonResult
        {
            Differences = differences,
            VirtualSizeChanged = backup.Environment.VirtualWidth != current.Environment.VirtualWidth ||
                                 backup.Environment.VirtualHeight != current.Environment.VirtualHeight,
            DpiChanged = backup.Environment.Dpi != current.Environment.Dpi,
            MonitorCountChanged = backup.Environment.MonitorCount != current.Environment.MonitorCount,
            MonitorLayoutChanged = HasMonitorLayoutChanged(backup.Environment, current.Environment)
        };
    }

    private static Dictionary<string, List<DesktopIconPosition>> GroupIcons(IEnumerable<DesktopIconPosition> icons) =>
        icons.GroupBy(icon => icon.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(icon => icon.CaptureOrder).ToList(),
                StringComparer.CurrentCultureIgnoreCase);

    private static bool HasMoved(DesktopIconPosition backup, DesktopIconPosition current) =>
        Math.Abs(backup.X - current.X) > PositionTolerance ||
        Math.Abs(backup.Y - current.Y) > PositionTolerance;

    private static bool HasMonitorLayoutChanged(DesktopEnvironment backup, DesktopEnvironment current)
    {
        if (backup.Monitors.Count == 0 || current.Monitors.Count == 0)
        {
            return backup.VirtualLeft != current.VirtualLeft || backup.VirtualTop != current.VirtualTop;
        }

        foreach (var backupMonitor in backup.Monitors)
        {
            var currentMonitor = current.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.Id, backupMonitor.Id, StringComparison.OrdinalIgnoreCase));
            if (currentMonitor is null ||
                currentMonitor.Left != backupMonitor.Left ||
                currentMonitor.Top != backupMonitor.Top ||
                currentMonitor.Width != backupMonitor.Width ||
                currentMonitor.Height != backupMonitor.Height ||
                currentMonitor.IsPrimary != backupMonitor.IsPrimary)
            {
                return true;
            }
        }

        return current.Monitors.Any(currentMonitor => backup.Monitors.All(backupMonitor =>
            !string.Equals(backupMonitor.Id, currentMonitor.Id, StringComparison.OrdinalIgnoreCase)));
    }
}

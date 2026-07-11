using DeskSnapshot.Models;

namespace DeskSnapshot.Services;

public static class MonitorMappingService
{
    public static string CreateMappingKey(DesktopEnvironment source, DesktopEnvironment target) =>
        $"{DisplayTopologyService.CreateFingerprint(source)}>{DisplayTopologyService.CreateFingerprint(target)}";

    public static string GetMonitorKey(DesktopMonitor monitor) =>
        !string.IsNullOrWhiteSpace(monitor.Id)
            ? monitor.Id
            : !string.IsNullOrWhiteSpace(monitor.DeviceName)
                ? monitor.DeviceName
                : monitor.Name;

    public static DesktopMonitor? ResolveTargetMonitor(
        DesktopMonitor source,
        DesktopEnvironment currentEnvironment,
        string? preferredTargetId = null)
    {
        if (preferredTargetId is not null)
        {
            if (string.IsNullOrWhiteSpace(preferredTargetId))
            {
                return null;
            }

            var preferred = currentEnvironment.Monitors.FirstOrDefault(monitor =>
                string.Equals(GetMonitorKey(monitor), preferredTargetId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        var currentMonitor = currentEnvironment.Monitors.FirstOrDefault(monitor =>
            !string.IsNullOrWhiteSpace(source.Id) &&
            string.Equals(monitor.Id, source.Id, StringComparison.OrdinalIgnoreCase));
        currentMonitor ??= currentEnvironment.Monitors.FirstOrDefault(monitor =>
            !string.IsNullOrWhiteSpace(source.DeviceName) &&
            string.Equals(monitor.DeviceName, source.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (currentMonitor is not null)
        {
            return currentMonitor;
        }

        var sameName = currentEnvironment.Monitors
            .Where(monitor => !string.IsNullOrWhiteSpace(source.Name) &&
                              string.Equals(monitor.Name, source.Name, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return sameName.Count == 1 ? sameName[0] : null;
    }
}

using System.Security.Cryptography;
using System.Text;
using DeskSnapshot.Models;

namespace DeskSnapshot.Services;

public static class DisplayTopologyService
{
    private const string FingerprintPrefix = "DT1-";

    public static string CreateFingerprint(DesktopEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var canonical = CreateCanonicalDescription(environment);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return FingerprintPrefix + Convert.ToHexString(hash.AsSpan(0, 10));
    }

    public static bool Matches(DesktopEnvironment environment, string? fingerprint) =>
        !string.IsNullOrWhiteSpace(fingerprint) &&
        string.Equals(CreateFingerprint(environment), fingerprint, StringComparison.OrdinalIgnoreCase);

    internal static string CreateCanonicalDescription(DesktopEnvironment environment)
    {
        if (environment.Monitors.Count == 0)
        {
            return $"legacy|{environment.VirtualWidth}x{environment.VirtualHeight}|dpi:{environment.Dpi}|count:{environment.MonitorCount}";
        }

        var primary = environment.Monitors.FirstOrDefault(monitor => monitor.IsPrimary)
                      ?? environment.Monitors.OrderBy(monitor => monitor.Left).ThenBy(monitor => monitor.Top).First();

        var entries = environment.Monitors.Select(monitor =>
        {
            var identity = GetStableIdentity(monitor);
            return string.Join('|',
                identity,
                monitor.Left - primary.Left,
                monitor.Top - primary.Top,
                monitor.Width,
                monitor.Height,
                monitor.IsPrimary ? 1 : 0);
        });

        return $"monitors:{environment.Monitors.Count}|dpi:{environment.Dpi}|" +
               string.Join(';', entries.Order(StringComparer.Ordinal));
    }

    private static string GetStableIdentity(DesktopMonitor monitor)
    {
        var identity = !string.IsNullOrWhiteSpace(monitor.Id)
            ? monitor.Id
            : !string.IsNullOrWhiteSpace(monitor.DeviceName)
                ? monitor.DeviceName
                : monitor.Name;

        return identity.Trim().ToUpperInvariant();
    }
}

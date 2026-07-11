using DeskSnapshot.Models;

namespace DeskSnapshot.Services;

public static class BackupRetentionService
{
    public const int MinimumRetention = 1;
    public const int MaximumRetention = 200;

    public static IReadOnlyList<DesktopLayoutBackup> TrimAutomaticBackups(
        List<DesktopLayoutBackup> backups,
        int requestedRetention)
    {
        var retention = Math.Clamp(requestedRetention, MinimumRetention, MaximumRetention);
        var expired = backups
            .Where(backup => backup.IsAutomaticBackup)
            .OrderByDescending(backup => backup.CreatedAt)
            .Skip(retention)
            .ToList();
        foreach (var backup in expired)
        {
            backups.Remove(backup);
        }

        return expired;
    }
}

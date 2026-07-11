using DeskSnapshot.Models;

namespace DeskSnapshot.Services;

public static class BackupRelationshipService
{
    public static IReadOnlyList<Guid> GetDescendantIds(
        IEnumerable<DesktopLayoutBackup> backups,
        Guid parentId)
    {
        var childIdsByParent = backups
            .Where(backup => backup.RelatedBackupId is not null)
            .GroupBy(backup => backup.RelatedBackupId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(backup => backup.Id).ToList());
        var descendants = new List<Guid>();
        var pending = new Stack<Guid>();
        var visited = new HashSet<Guid> { parentId };
        pending.Push(parentId);

        while (pending.Count > 0)
        {
            var currentParentId = pending.Pop();
            if (!childIdsByParent.TryGetValue(currentParentId, out var childIds))
            {
                continue;
            }

            foreach (var childId in childIds)
            {
                if (!visited.Add(childId))
                {
                    continue;
                }

                descendants.Add(childId);
                pending.Push(childId);
            }
        }

        return descendants;
    }
}

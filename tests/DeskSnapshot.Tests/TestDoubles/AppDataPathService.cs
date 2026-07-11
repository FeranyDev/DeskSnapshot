namespace DeskSnapshot.Services;

public static class AppDataPathService
{
    public static string GetLocalDataFolder() =>
        throw new InvalidOperationException("Tests must provide an explicit backup file path.");
}

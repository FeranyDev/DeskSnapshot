using System.Text.Json;
using DeskSnapshot.Models;

namespace DeskSnapshot.Services;

public sealed class BackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public BackupStore(string? filePath = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _filePath = Path.GetFullPath(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            return;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskSnapshot");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "backups.json");
    }

    public string FilePath => _filePath;

    public async Task<List<DesktopLayoutBackup>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<DesktopLayoutBackup>>(stream, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            var damagedPath = _filePath + $".damaged-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(_filePath, damagedPath);
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<DesktopLayoutBackup> backups)
    {
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, backups, JsonOptions);
        }

        File.Move(tempPath, _filePath, true);
    }
}

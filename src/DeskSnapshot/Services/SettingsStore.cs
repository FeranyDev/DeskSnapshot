using System.Text.Json;
using DeskSnapshot.Models;

namespace DeskSnapshot.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public static string ReadUiLanguage()
    {
        var filePath = GetSettingsPath();
        if (!File.Exists(filePath))
        {
            return "system";
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            return document.RootElement.TryGetProperty("uiLanguage", out var value)
                ? LocalizationService.NormalizeLanguage(value.GetString())
                : "system";
        }
        catch (JsonException)
        {
            return "system";
        }
    }

    public SettingsStore()
    {
        _filePath = GetSettingsPath();
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }

        File.Move(tempPath, _filePath, true);
    }

    private static string GetSettingsPath()
    {
        var folder = AppDataPathService.GetLocalDataFolder();
        return Path.Combine(folder, "settings.json");
    }
}

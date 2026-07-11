using System.Globalization;
using System.Xml.Linq;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace DeskSnapshot.Services;

public static class LocalizationService
{
    private static ResourceLoader _loader = new();
    private static IReadOnlyDictionary<string, string>? _explicitResources;

    public static bool HasExplicitLanguage => _explicitResources is not null;

    public static bool TryGetExplicit(string key, out string value)
    {
        if (_explicitResources?.TryGetValue(key, out var explicitValue) == true)
        {
            value = explicitValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static string Get(string key)
    {
        if (_explicitResources?.TryGetValue(key, out var explicitValue) == true)
        {
            return explicitValue;
        }

        var value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public static string Format(string key, params object[] arguments) =>
        string.Format(Get(key), arguments);

    public static void ApplyLanguage(string language)
    {
        var normalized = NormalizeLanguage(language);
        if (normalized == "system")
        {
            _explicitResources = null;
            return;
        }

        var languageTag = normalized switch
        {
            "zh-CN" => "zh-CN",
            "en-US" => "en-US",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

        var culture = CultureInfo.GetCultureInfo(languageTag);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        _explicitResources = LoadExplicitResources(languageTag);

        if (AppDataPathService.IsPackaged())
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageTag;
            _loader = new ResourceLoader();
            return;
        }
    }

    public static string NormalizeLanguage(string? language) => language switch
    {
        "zh-CN" => "zh-CN",
        "en-US" => "en-US",
        _ => "system"
    };

    private static IReadOnlyDictionary<string, string>? LoadExplicitResources(string languageTag)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", languageTag, "Resources.resw");
        if (!File.Exists(path))
        {
            return null;
        }

        var document = XDocument.Load(path);
        return document.Root?
            .Elements("data")
            .Where(element => element.Attribute("name") is not null)
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }
}

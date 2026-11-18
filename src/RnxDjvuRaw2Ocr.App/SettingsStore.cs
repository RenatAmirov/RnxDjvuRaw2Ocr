using System.Text.Json;
using RnxDjvuRaw2Ocr.Core;

namespace RnxDjvuRaw2Ocr.App;

internal sealed record AppSettings(ToolPaths Tools, string Languages = "rus+eng", int LayoutIndex = 0);

internal static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RnxDjvuRaw2Ocr");

    public static AppSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(DirectoryPath, "settings.json")));
            if (settings?.Tools is not null && settings.Languages is not null) return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new AppSettings(ToolDiscovery.Discover());
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

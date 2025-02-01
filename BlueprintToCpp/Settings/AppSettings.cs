using BlueRange.ViewModels;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Serilog;

namespace BlueRange.Settings;

public static class AppSettings
{
    public static SettingsViewModel Current = new();

    private static readonly string SettingsDirectory = Path.Combine(Environment.CurrentDirectory, "Output", "Settings");
    private static readonly string SettingsFile = Path.Combine(SettingsDirectory, "AppSettings.json");

    public static void Load()
    {
        if (!Directory.Exists(SettingsDirectory)) Directory.CreateDirectory(SettingsDirectory);
        if (!File.Exists(SettingsFile)) return;
        Current = JsonConvert.DeserializeObject<SettingsViewModel>(File.ReadAllText(SettingsFile)) ?? new SettingsViewModel();
    }

    public static async Task Save() => await File.WriteAllTextAsync(SettingsFile, JsonConvert.SerializeObject(Current, Formatting.Indented)).ConfigureAwait(false);
}

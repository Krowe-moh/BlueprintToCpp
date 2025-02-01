using BlueRange.Settings;
using BlueRange.ViewModels;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Versions;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Spectre.Console;

namespace BlueRange.Services;

public static class ApplicationService
{
    public static CUE4ParseViewModel CUE4Parse = new();
    public static ApiEndpointViewModel Api = new();

    public static async Task Initialize()
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();

        await InitOodle().ConfigureAwait(false);
        AppSettings.Load();

        if (string.IsNullOrWhiteSpace(AppSettings.Current.GameDirectory))
        {
            AppSettings.Current.GameDirectory = AnsiConsole.Prompt(new TextPrompt<string>("Please enter your [green]game directory[/] path:")
                    .Validate(x => Directory.Exists(x) && Directory.EnumerateFiles(x, "*.pak", SearchOption.AllDirectories).Count() > 0)
                    .PromptStyle("green"));
        }

        if (AppSettings.Current.GameVersion == 0)
        {
            AppSettings.Current.GameVersion = AnsiConsole.Prompt(new SelectionPrompt<EGame>()
                .Title("Please select your [green]game version[/]:")
                .AddChoices(Enum.GetValues<EGame>()
                    .GroupBy(value => (int)value)
                    .Select(group => group.First())
                    .OrderByDescending(value => (int)value == ((int)value & ~0xF))));
        }
    }

    private static async Task InitOodle()
    {
        var oodlePath = Path.Combine(Environment.CurrentDirectory, OodleHelper.OODLE_DLL_NAME);
        if (!File.Exists(oodlePath)) await OodleHelper.DownloadOodleDllAsync(oodlePath).ConfigureAwait(false);
        OodleHelper.Initialize(oodlePath);
    }
}

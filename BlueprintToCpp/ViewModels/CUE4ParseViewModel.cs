using BlueRange.Services;
using BlueRange.Settings;
using BlueRange.ViewModels.Api.Models;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Serilog;
using Spectre.Console;

namespace BlueRange.ViewModels;

public class CUE4ParseViewModel
{
    public DefaultFileProvider Provider;

    public CUE4ParseViewModel()
    {
        Provider = new DefaultFileProvider(AppSettings.Current.GameDirectory, SearchOption.AllDirectories, true, new VersionContainer(AppSettings.Current.GameVersion));
    }

    public async Task InitializeAsync()
    {
        if (Provider.UnloadedVfs.Count == 0)
            Provider = new DefaultFileProvider(AppSettings.Current.GameDirectory, SearchOption.AllDirectories, true, new VersionContainer(AppSettings.Current.GameVersion));

        Provider.Initialize();

        var mountedPackages = await Provider.MountAsync().ConfigureAwait(false);

        var aes; // wow
        if (Provider.InternalGameName == "fortnitegame")
        {
             aes = await ApplicationService.Api.FortniteCentral.GetAesAsync().ConfigureAwait(false);
        }
        else
        {
            var aesPath = AnsiConsole.Prompt(new TextPrompt<string>("Please enter the [green]AES json path[/]:")
                .PromptStyle("green"));
            // finsih this
        }

        mountedPackages += await Provider.SubmitKeyAsync(new FGuid(), new FAesKey(aes.MainKey)).ConfigureAwait(false);

        foreach (var dynamicKey in aes.DynamicKeys)
            mountedPackages += await Provider.SubmitKeyAsync(new FGuid(dynamicKey.Guid), new FAesKey(dynamicKey.Key)).ConfigureAwait(false);

        var localizedStrings = Provider.LoadLocalization();

        Log.Information("Mounted {0} packages", mountedPackages);
        Log.Information("Loaded {0} localized strings", localizedStrings);

        var filePath = string.Empty;
        if (Provider.InternalGameName == "fortnitegame")
        {
            var mappings = await ApplicationService.Api.FortniteCentral.GetMappingsAsync().ConfigureAwait(false);

            if (mappings.Length <= 0)
            {
                filePath = Directory.GetFiles(Environment.CurrentDirectory, "*.usmap").Order().LastOrDefault();
            }
            else
            {
                var mapping = mappings.First();

                filePath = Path.Combine(Environment.CurrentDirectory, mapping.FileName);
                var data = await ApplicationService.Api.DownloadFileAsync(mapping.Url).ConfigureAwait(false);
                if (data.Length <= 0)
                    throw new NullReferenceException("No internet = no mappings = no packages");

                await File.WriteAllBytesAsync(filePath, data);
            }
        }
        else
        {
                filePath = AnsiConsole.Prompt(new TextPrompt<string>("Please enter the [green]mappings path[/]:")
                .PromptStyle("green"));
        }

        Provider.MappingsContainer = new FileUsmapTypeMappingsProvider(filePath);

        Log.Information("Mappings pulled from {0}", filePath);
        Provider.ReadScriptData = true;
    }
}

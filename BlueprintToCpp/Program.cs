using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace BlueprintToCpp;

public static class Program
{
    private const string ConfigFile = "config.json";
    private const string AesCacheFile = "aes.json";
    private const string AesUrl = "https://export-service-new.dillyapis.com/v1/aes";

    // Fortnite: slow for some reason
    private static readonly string[] Ignored =
    [
        "/PPID_",
        "/VKTemplates",
        "/NaniteDisplacedMesh_"
    ];

    private static readonly Regex VerseMangleRegex = new(@"__verse_0x[a-fA-F0-9]{8}_", RegexOptions.Compiled);
    private static readonly Regex CallFuncRegex = new(@"CallFunc_([A-Za-z0-9_]+)_ReturnValue", RegexOptions.Compiled);
    private static readonly Regex DynamicCastRegex = new(@"K2Node_DynamicCast_([A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex K2NodeRegex = new(@"K2Node_([A-Za-z0-9_]+)", RegexOptions.Compiled);

    private static readonly ParallelOptions ProcessingOptions = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

    public static async Task Main()
    {
#if DEBUG
        Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
#endif
        Config config = ConfigLoader.Load(ConfigFile);

        if (string.IsNullOrEmpty(config.PakFolderPath))
        {
            Console.WriteLine($"Set Pak Folder Path in {ConfigFile}.");
            return;
        }

        if (config.Version == 0)
        {
            Console.WriteLine($"Set UEVersion in {ConfigFile}.");
            return;
        }

        DefaultFileProvider provider = CreateProvider(config);
        await LoadAesKeysAsync(provider);

        GameFile[] packages = SelectPackages(provider, config.BlueprintPath).ToArray();
        if (packages.Length == 0)
        {
            Console.WriteLine("No packages matched provided Blueprint Path.");
            return;
        }

        Console.WriteLine($"Decompiling {packages.Length} packages.");

        string outputRoot = AppContext.BaseDirectory;
        int decompiled = 0;

        Parallel.ForEach(packages, ProcessingOptions, package =>
        {
            try
            {
                WritePseudoCode(provider, package, outputRoot, Interlocked.Increment(ref decompiled), packages.Length);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{package.Path}: {ex.Message}");
            }
        });
    }

    private static DefaultFileProvider CreateProvider(Config config)
    {
        OodleHelper.Initialize();
        ZlibHelper.Initialize();

        var provider = new DefaultFileProvider(
            config.PakFolderPath,
            SearchOption.AllDirectories,
            new VersionContainer(config.Version),
            StringComparer.OrdinalIgnoreCase)
        {
            ReadScriptData = true
        };

        if (!string.IsNullOrEmpty(config.UsmapPath))
        {
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(config.UsmapPath);
        }

        provider.Initialize();
        return provider;
    }

    private static async Task LoadAesKeysAsync(DefaultFileProvider provider)
    {
        string aesJson = File.Exists(AesCacheFile) ? await File.ReadAllTextAsync(AesCacheFile) : await DownloadAesJsonAsync();
        var keys = new Dictionary<FGuid, FAesKey>();

        JObject aes = JObject.Parse(aesJson);
        keys[new FGuid()] = new FAesKey(aes["mainKey"]?.ToString() ?? string.Empty);

        foreach (JObject dynamicKey in aes["dynamicKeys"]?.Children<JObject>() ?? [])
        {
            if (Guid.TryParse(dynamicKey["guid"]?.ToString(), out Guid guid) && dynamicKey["key"]?.ToString() is { Length: > 0 } key)
            {
                keys[new FGuid(guid.ToString("N"))] = new FAesKey(key);
            }
        }

        provider.SubmitKeys(keys);
        provider.PostMount();
    }

    private static async Task<string> DownloadAesJsonAsync()
    {
        using var http = new HttpClient();
        string aesJson = await http.GetStringAsync(AesUrl);
        await File.WriteAllTextAsync(AesCacheFile, aesJson);
        return aesJson;
    }

    private static IEnumerable<GameFile> SelectPackages(DefaultFileProvider provider, string blueprintPath)
    {
        if (!string.IsNullOrEmpty(blueprintPath) && provider.TryGetGameFile(blueprintPath, out GameFile? exactMatch) && exactMatch is not null)
        {
            return [exactMatch];
        }

        string prefix = string.IsNullOrEmpty(blueprintPath) ? string.Empty : blueprintPath + "/";

        return provider.Files.Values.Where(file =>
            file.Path.StartsWith(prefix, StringComparison.Ordinal) &&
            (file.Path.EndsWith(".uasset", StringComparison.Ordinal) || file.Path.EndsWith(".umap", StringComparison.Ordinal)) &&
            !file.Path.Contains(".o.", StringComparison.Ordinal) &&
            !Ignored.Any(segment => file.Path.Contains(segment, StringComparison.Ordinal)));
    }

    private static void WritePseudoCode(DefaultFileProvider provider, GameFile package, string outputRoot, int current, int total)
    {
        string path = package.Path;
        Console.WriteLine($"Processing {path} ({current}/{total})");

        IPackage pkg = provider.LoadPackage(package);
        var pseudoCode = new StringBuilder();

        foreach (UObject export in pkg.GetExports())
        {
            if (export is not UClass blueprint)
            {
                continue;
            }

            if (pseudoCode.Length > 0)
            {
                pseudoCode.Append("\n\n");
            }

            pseudoCode.Append(blueprint.DecompileBlueprintToPseudo());
        }

        if (pseudoCode.Length == 0)
        {
            return;
        }

        string outputDirectory = Path.Combine(outputRoot, Path.GetDirectoryName(path.TrimStart('/')) ?? string.Empty);
        Directory.CreateDirectory(outputDirectory);

        string outputPath = Path.Combine(outputDirectory, Path.ChangeExtension(package.Name, ".cpp"));
        File.WriteAllText(outputPath, RemoveEnginePrefixes(path, pseudoCode.ToString()));

        Console.WriteLine($"Wrote {outputPath}");
    }

    private static string RemoveEnginePrefixes(string packagePath, string pseudoCode)
    {
        if (packagePath.Contains("_Verse.uasset", StringComparison.Ordinal))
        {
            pseudoCode = VerseMangleRegex.Replace(pseudoCode, string.Empty);
        }

        pseudoCode = CallFuncRegex.Replace(pseudoCode, "$1");
        pseudoCode = DynamicCastRegex.Replace(pseudoCode, "$1");
        return K2NodeRegex.Replace(pseudoCode, "$1");
    }
}

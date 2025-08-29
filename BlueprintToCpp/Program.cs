using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.Utils;

namespace Main;

public static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
#if DEBUG
            //Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
#endif
            var config = Utils.LoadConfig("config.json");

            string pakFolderPath = config.PakFolderPath;
            if (string.IsNullOrEmpty(pakFolderPath) || pakFolderPath.Length < 1)
            {
                Console.WriteLine("Please provide a pak folder path in the config.json file.");
                return;
            }

            string blueprintPath = config.BlueprintPath;
            if (string.IsNullOrEmpty(blueprintPath) || blueprintPath.Length < 1)
            {
#if TRUE
                Console.WriteLine(
                    "No blueprint path specified in the config.json file. Processing all compatible blueprints.");
#else
                Console.WriteLine("Please provide a blueprint path in the config.json file.");
                return;
#endif
            }

            string usmapPath = config.UsmapPath;
            if (string.IsNullOrEmpty(usmapPath) || usmapPath.Length < 1)
            {
                Console.WriteLine("Please provide a usmap path in the config.json file.");
                return;
            }

            string oodlePath = config.OodlePath;
            if (string.IsNullOrEmpty(oodlePath) || oodlePath.Length < 1)
            {
                Console.WriteLine("Please provide a oodle path in the config.json file.");
                return;
            }

            string zlibPath = config.ZlibPath;

            EGame version = config.Version;
            if (string.IsNullOrEmpty(version.ToString()) || version.ToString().Length < 1)
            {
                Console.WriteLine("Please provide a UE version in the config.json file.");
                return;
            }

            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

            var dummyContext = new UObject();
            var provider = InitializeProvider(pakFolderPath, usmapPath, oodlePath, zlibPath, version);
            provider.ReadScriptData = true;
            Console.WriteLine("If the game is not fortnite and the game is encrypted, modify aes.json file.");
            await LoadAesKeysAsync(provider,"https://fortnitecentral.genxgames.gg/api/v1/aes"); // allow users to change the aes url?

            var files = new Dictionary<string, CUE4Parse.FileProvider.Objects.GameFile[]>();

            bool isFile = provider.Files.ContainsKey(blueprintPath);
            if (string.IsNullOrEmpty(blueprintPath))
            {
                files = provider.Files.Values
                    .Where(f => (f.Path.EndsWith(".uasset") || f.Path.EndsWith(".umap")) && !f.Path.Contains(".o.") && !f.Path.Contains("PPID_") && !f.Path.Contains("/VKTemplates") && !f.Path.Contains("/NaniteDisplacedMesh_"))
                    .GroupBy(f => f.Path.SubstringBeforeLast('/'))
                    .ToDictionary(g => g.Key, g => g.ToArray());
            }
            else if (isFile)
            {
                files = new Dictionary<string, GameFile[]>
                {
                    [blueprintPath] = new[] { provider.Files[blueprintPath] }
                };
            }
            else
            {
                files = provider.Files.Values
                    .Where(f => f.Path.StartsWith(blueprintPath + "/") &&
                                (f.Path.EndsWith(".uasset") || f.Path.EndsWith(".umap")) &&
                                !f.Path.Contains(".o.") &&
                                !f.Path.Contains("/PPID_") && // ignore these (fortnite, it slows the program by like 10x)
                                !f.Path.Contains("/VKTemplates") &&
                                !f.Path.Contains("/NaniteDisplacedMesh_"))
                    .GroupBy(f => f.Path.SubstringBeforeLast('/'))
                    .ToDictionary(g => g.Key, g => g.ToArray());
            }

            int index = -1;
            int totalGameFiles = files.Sum(kv => kv.Value.Length);

            // loop from https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse.Example/Exporter.cs#L104
            foreach (var (folder, packages) in files)
            {
                Parallel.ForEach(packages, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, package =>
                {
                    try
                    {
                        if (!package.IsUePackage)
                            return;
                        int currentIndex = Interlocked.Increment(ref index);
                        string path = package.Path;

                        Console.WriteLine($"Processing {path} ({currentIndex}/{totalGameFiles})");

                        var pkg = provider.LoadPackage(path);
                        string blueprintDirRel = Path.GetDirectoryName(path)!;
                        string blueprintDirOutput = Path.Combine(exeDirectory, blueprintDirRel);

                        var cpp = new List<string>();
                        for (var i = 0; i < pkg?.ExportMapLength; i++)
                        {
                            var pointer = new FPackageIndex(pkg, i + 1).ResolvedObject;
                            if (pointer?.Object?.Value is not UClass blueprint)
                                continue;

                            cpp.Add(blueprint.DecompileBlueprintToPseudo());
                        }

                        if (cpp.Count > 0)
                        {
                            Directory.CreateDirectory(blueprintDirOutput);

                            string outputFile = Path.ChangeExtension(package.Name, ".cpp");
                            string outputFilePath = Path.Combine(blueprintDirOutput, outputFile);

                            var cppclean = cpp.Count > 1 ? string.Join("\n\n", cpp) : cpp.FirstOrDefault() ?? string.Empty;
                            if (path.Contains("_Verse.uasset"))
                            {
                                cppclean = Regex.Replace(cppclean, "__verse_0x[a-fA-F0-9]{8}_", ""); // UnmangleCasedName
                            }
                            cppclean = Regex.Replace(cppclean, @"CallFunc_([A-Za-z0-9_]+)_ReturnValue", "$1"); // may slow the tool alot
                            
                            using (var fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: false))
                            using (var writer = new StreamWriter(fs, Encoding.UTF8, bufferSize: 8192))
                            {
                                writer.Write(cppclean);
                                writer.Flush();
                            }
                            Console.WriteLine($"Output written to: {outputFilePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error Processing: {package.Path} {ex.Message}\n{ex.StackTrace}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}\n{ex.StackTrace}");
        }
    }

    static DefaultFileProvider InitializeProvider(string pakFolderPath, string usmapPath, string oodlePath, string zlibPath, EGame version)
    {
        OodleHelper.Initialize(oodlePath);

        if (!string.IsNullOrEmpty(zlibPath) && zlibPath.Length > 0)
        {
            ZlibHelper.Initialize(zlibPath);
        }

        var provider = new DefaultFileProvider(pakFolderPath, SearchOption.TopDirectoryOnly, true, new VersionContainer(version))
        {
            MappingsContainer = new FileUsmapTypeMappingsProvider(usmapPath)
        };
        provider.Initialize();

        return provider;
    }

    static async Task LoadAesKeysAsync(DefaultFileProvider provider, string aesUrl)
    {
        string cacheFilePath = "aes.json";

        if (File.Exists(cacheFilePath))
        {
            string cachedAesJson = await File.ReadAllTextAsync(cacheFilePath);
            LoadAesKeysFromJson(provider, cachedAesJson);
        }
        else
        {
            using var httpClient = new HttpClient();
            string aesJson = await httpClient.GetStringAsync(aesUrl);
            await File.WriteAllTextAsync(cacheFilePath, aesJson);
            LoadAesKeysFromJson(provider, aesJson);
        }

        provider.PostMount();
        //provider.LoadLocalization();
    }

    private static void LoadAesKeysFromJson(DefaultFileProvider provider, string aesJson)
    {
        var aesData = JObject.Parse(aesJson);
        string mainKey = aesData["mainKey"]?.ToString() ?? string.Empty;
        provider.SubmitKey(new FGuid(), new FAesKey(mainKey));

        foreach (var key in aesData["dynamicKeys"]?.ToObject<JArray>() ?? new JArray())
        {
            var guid = key["guid"]?.ToString();
            var aesKey = key["key"]?.ToString();
            if (!string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(aesKey))
            {
                provider.SubmitKey(new FGuid(guid), new FAesKey(aesKey));
            }
        }
    
        aesData = null;
    }
}

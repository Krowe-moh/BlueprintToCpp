using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BlueprintToCpp;

public sealed class Config
{
    public string PakFolderPath { get; set; } = string.Empty;
    public string BlueprintPath { get; set; } = string.Empty;
    public string UsmapPath { get; set; } = string.Empty;

    [JsonConverter(typeof(StringEnumConverter))]
    public EGame Version { get; set; }
}

public static class ConfigLoader
{
    public static Config Load(string path)
    {
        if (File.Exists(path))
        {
            return JsonConvert.DeserializeObject<Config>(File.ReadAllText(path))
                ?? throw new InvalidDataException($"{path} does not contain a valid config.");
        }

        var config = new Config();
        File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
        Console.WriteLine($"Created {path}. Modify Settings, then run again.");
        return config;
    }
}

using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
public class Config
{
    public string PakFolderPath { get; set; }
    public string BlueprintPath { get; set; }
    public string OodlePath { get; set; }
    public string ZlibPath { get; set; }
    public string UsmapPath { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public EGame Version { get; set; }
}
public static class Utils
{
    public static Config LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Config file created, please modify the values.");
            var defaultConfig = new Config
            {
                PakFolderPath = "",
                BlueprintPath = "",
                OodlePath = "",
                ZlibPath = "",
                UsmapPath = "",
                Version = EGame.GAME_UE5_LATEST
            };

            string jsonTxt = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(path, jsonTxt);
            return defaultConfig;
        }

        string json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<Config>(json);
    }
}

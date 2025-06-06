using System;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Objects.UObject;
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

    public static string GetPrefix(string? type, string? extra = "")
    {
        return type switch
        {
            "FNameProperty" or "FPackageIndex" or "FTextProperty" or "FStructProperty" => "F",
            "UBlueprintGeneratedClass" or "FActorProperty" => "A",
            "FObjectProperty" when extra.Contains("Actor") => "A",
            "ResolvedScriptObject" or "ResolvedLoadedObject" or "FSoftObjectProperty" or "FObjectProperty" => "U",
            _ => ""
        };
    }

    // These (GetUnknownFieldType, and GetPropertyType) were taken from
    // https://github.com/CrystalFerrai/UeBlueprintDumper/blob/main/UeBlueprintDumper/BlueprintDumper.cs#L352
    // nothing else in this repository is from UeBlueprintDumper

    public static string GetUnknownFieldType(object field)
    {
        string typeName = field.GetType().Name;
        int suffixIndex = typeName.IndexOf("Property", StringComparison.Ordinal);
        if (suffixIndex < 0)
            return typeName;
        return typeName.Substring(1, suffixIndex - 1);
    }
    public static string GetUnknownFieldType(FField field)
    {
        string typeName = field.GetType().Name;
        int suffixIndex = typeName.IndexOf("Property", StringComparison.Ordinal);
        if (suffixIndex < 0) return typeName;
        return typeName.Substring(1, suffixIndex - 1);
    }
    public static string GetPropertyType(object? property)
    {
        if (property is null) return "None";

        //Console.WriteLine(property.GetType().Name);
        return property switch
        {
            FIntProperty => "int",
            FInt8Property => "int8",
            FInt16Property => "int16",
            FInt64Property => "int64",
            FUInt16Property => "uint16",
            FUInt32Property => "uint32",
            FUInt64Property => "uint64",
            FBoolProperty or Boolean => "bool",
            FStrProperty => "FString",
            FFloatProperty or Single => "float",
            FDoubleProperty or Double => "double",
            FObjectProperty objct => property switch
            {
                FClassProperty clss => $"{clss.MetaClass?.Name ?? "UNKNOWN"}",
                FSoftClassProperty softClass => $"{softClass.MetaClass?.Name ?? "UNKNOWN"}",
                _ => objct.PropertyClass?.Name ?? "UNKNOWN"
            },
            FPackageIndex pkg => pkg?.ResolvedObject?.Class?.Name.ToString() ?? "Package",
            FName fme => fme.PlainText.Contains("::") ? fme.PlainText.Split("::")[0] : fme.PlainText ?? "FName",
            FEnumProperty enm => enm.Enum?.Name.ToString() ?? "Enum",
            FByteProperty bt => bt.Enum.ResolvedObject?.Name.Text ?? "Byte",
            FInterfaceProperty intrfc => $"{intrfc.InterfaceClass.Name} interface",
            FStructProperty strct => strct.Struct.ResolvedObject?.Name.Text ?? "Struct",
            FFieldPathProperty fieldPath => $"{fieldPath.PropertyClass.Text} field path",
            FDelegateProperty dlgt => $"{dlgt.SignatureFunction?.Name ?? "UNKNOWN"} (Delegate)",
            FMulticastDelegateProperty mdlgt => $"{mdlgt.SignatureFunction?.Name ?? "UNKNOWN"} (MulticastDelegateProperty)",
            FMulticastInlineDelegateProperty midlgt => $"{midlgt.SignatureFunction?.Name ?? "UNKNOWN"} (MulticastInlineDelegateProperty)",
            _ => GetUnknownFieldType(property)
        };
    }
    public static string GetPropertyType(FProperty? property)
    {
        if (property is null) return "None";

        return property switch
        {
            FIntProperty => "int",
            FBoolProperty => "bool",
            FStrProperty => "FString",
            FFloatProperty => "float",
            FDoubleProperty => "double",
            FObjectProperty objct => property switch
            {
                FClassProperty clss => $"{clss.MetaClass?.Name ?? "UNKNOWN"} Class",
                FSoftClassProperty softClass => $"{softClass.MetaClass?.Name ?? "UNKNOWN"} Class (soft)",
                _ => objct.PropertyClass?.Name ?? "UNKNOWN"
            },
            FEnumProperty enm => enm.Enum?.Name.ToString() ?? "Enum",
            FSetProperty set => $"TSet<{GetPrefix(set.ElementProp.GetType().Name)}{GetPropertyType(set.ElementProp)}{(set.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || set.PropertyFlags.HasFlag(EPropertyFlags.ContainsInstancedReference) ? "*" : string.Empty)}>",
            FByteProperty bt => bt.Enum.ResolvedObject?.Name.Text ?? "Byte",
            FInterfaceProperty intrfc => $"{intrfc.InterfaceClass.Name} interface",
            FStructProperty strct => strct.Struct.ResolvedObject?.Name.Text ?? "Struct",
            FFieldPathProperty fieldPath => $"{fieldPath.PropertyClass.Text} field path",
            FDelegateProperty dlgt => $"{dlgt.SignatureFunction?.Name ?? "UNKNOWN"} (Delegate)",
            FMapProperty map => $"TMap<{GetPrefix(map.ValueProp.GetType().Name)}{GetPropertyType(map.KeyProp)}, {GetPrefix(map.ValueProp.GetType().Name)}{GetPropertyType(map.ValueProp)}{(map.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || map.PropertyFlags.HasFlag(EPropertyFlags.ContainsInstancedReference) ? "*" : string.Empty)}>",
            FMulticastDelegateProperty mdlgt => $"{mdlgt.SignatureFunction?.Name ?? "UNKNOWN"} (MulticastDelegateProperty)",
            FMulticastInlineDelegateProperty midlgt => $"{midlgt.SignatureFunction?.Name ?? "UNKNOWN"} (MulticastInlineDelegateProperty)",
            FArrayProperty array => $"TArray<{GetPrefix(array.Inner.GetType().Name)}{GetPropertyType(array.Inner)}{(array.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || array.PropertyFlags.HasFlag(EPropertyFlags.ContainsInstancedReference) || Utils.GetPropertyProperty(array.Inner.GetType().Name) ? "*" : string.Empty)}>",
            _ => GetUnknownFieldType(property)
        };
    }
    public static bool GetPropertyProperty(object? property)
    {
        if (property is null) return false;

        return property switch
        {
            FObjectProperty objct => true,
            _ => false
        };
    }
    public static bool GetPropertyProperty(FProperty? property)
    {
        if (property is null) return false;

        return property switch
        {
            FObjectProperty objct => true,
            _ => false
        };
    }
}

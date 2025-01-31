using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Verse;
using CUE4Parse.UE4.Assets.Objects;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.GameplayTags;
namespace Main;

public static class Program
{
    private static bool IsVerse = false;

    private class StatementInfo
    {
        public int Index { get; set; }
        public int LineNum { get; set; }
    }
    private static List<StatementInfo> statementIndices = new List<StatementInfo>();
    private static List<int> jumpCodeOffsets = new List<int>();
    private static string? ProcessTextProperty(FKismetPropertyPointer property)
    {
        if (property.New is null)
        {
            return property.Old.Name;
        }
        if (IsVerse)
        {
            return Regex.Replace(string.Join('.', property.New.Path.Select(n => n.Text)), @"^__verse_0x[0-9A-Fa-f]+_", "");
        }
        return string.Join('.', property.New.Path.Select(n => n.Text)).Replace(" ", "");
    }

    public static async Task Main(string[] args)
    {
        try
        {
            #if DEBUG
                Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
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
                Console.WriteLine("No blueprint path specified in the config.json file. Processing all compatible blueprints.");
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
            if (string.IsNullOrEmpty(zlibPath) || zlibPath.Length < 1)
            {
                Console.WriteLine("Missing \"ZlibPath\" in the config.json file.");
            }

            EGame version = config.Version;
            if (string.IsNullOrEmpty(version.ToString()) || version.ToString().Length < 1)
            {
                Console.WriteLine("Please provide a UE version in the config.json file.");
                return;
            }

            var provider = InitializeProvider(pakFolderPath, usmapPath, oodlePath, zlibPath, version);

            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

            var provider = InitializeProvider(pakFolderPath, usmapPath, oodlePath, version);

            provider.ReadScriptData = true;
            await LoadAesKeysAsync(provider, "https://fortnitecentral.genxgames.gg/api/v1/aes"); // allow users to change the aes url?

            var gameFiles = (string.IsNullOrEmpty(blueprintPath) || blueprintPath.Length < 1) ? provider.Files.Values.Where(gameFile => gameFile.IsUE4Package) : [provider[blueprintPath]];

            var totalGameFiles = gameFiles.Count();

            int i = -1;

            foreach (var gameFile in gameFiles)
            {
                i++;
                Console.WriteLine($"Processing {gameFile.Path} ({i + 1}/{totalGameFiles})");
                try
                {
                    var package = provider.LoadPackage(gameFile) as AbstractUePackage;
                    var outputBuilder = new StringBuilder();

                    var blueprintGeneratedClass = package?.ExportsLazy.Where(export => export.Value is UBlueprintGeneratedClass).Select(export => (UBlueprintGeneratedClass) export.Value).FirstOrDefault();
                    var VerseClass = package?.ExportsLazy.Where(export => export.Value is UVerseClass).Select(export => (UVerseClass) export.Value).FirstOrDefault();

                    if (VerseClass != null)
                        IsVerse = true;
                    if (blueprintGeneratedClass != null || IsVerse)
                    {
                        var mainClass = blueprintGeneratedClass?.Name ?? VerseClass?.Name;
                        var superStructName = blueprintGeneratedClass?.SuperStruct.Name ?? VerseClass?.SuperStruct.Name;
                        outputBuilder.AppendLine($"class {Utils.GetPrefix(blueprintGeneratedClass?.GetType().Name ?? VerseClass?.GetType().Name)}{mainClass} : public {Utils.GetPrefix(blueprintGeneratedClass?.GetType().Name ?? VerseClass?.GetType().Name)}{superStructName}\n{{\npublic:");

                        var stringsarr = new List<string>();
                        foreach (var export in package.ExportsLazy)
                        {
                            if (export.Value is not UBlueprintGeneratedClass)
                            {
                                if (export.Value.Name.StartsWith("Default__") && export.Value.Name.EndsWith(mainClass))
                                {
                                    var exportObject = (UObject) export.Value;
                                    foreach (var key in exportObject.Properties)
                                    {
                                        stringsarr.Add(key.Name.PlainText);
                                        string placeholder = $"{key.Name}placenolder";
                                        string result = key.Tag.GenericValue?.ToString();
                                        string keyName = key.Name.PlainText.Replace(" ", "");

                                        var PropertyTag = key.Tag.GetValue(typeof(object));

                                        void ShouldAppend(string value)
                                        {
                                            if (outputBuilder.ToString().Contains(placeholder))
                                            {
                                                outputBuilder.Replace(placeholder, value);
                                            }
                                            else
                                            {
                                                outputBuilder.AppendLine($"\t{Utils.GetPropertyType(PropertyTag)} {keyName} = {value};");
                                            }
                                        }

                                        if (key.Tag.GenericValue is FScriptStruct structTag)
                                        {
                                            if (structTag.StructType is FVector vector)
                                            {
                                                ShouldAppend($"FVector({vector.X}, {vector.Y}, {vector.Z})");
                                            }
                                            else if (structTag.StructType is TIntVector3<int> vector3)
                                            {
                                                ShouldAppend($"FVector({vector3.X}, {vector3.Y}, {vector3.Z})");
                                            }
                                            else if (structTag.StructType is TIntVector3<float> floatVector3)
                                            {
                                                ShouldAppend($"FVector({floatVector3.X}, {floatVector3.Y}, {floatVector3.Z})");
                                            }
                                            else if (structTag.StructType is TIntVector2<float> floatVector2)
                                            {
                                                ShouldAppend($"FVector2D({floatVector2.X}, {floatVector2.Y})");
                                            }
                                            else if (structTag.StructType is FVector2D vector2d)
                                            {
                                                ShouldAppend($"FVector2D({vector2d.X}, {vector2d.Y})");
                                            }
                                            else if (structTag.StructType is FRotator rotator)
                                            {
                                                ShouldAppend($"FRotator({rotator.Pitch}, {rotator.Yaw}, {rotator.Roll})");
                                            }
                                            else if (structTag.StructType is FStructFallback fallback)
                                            {
                                                string formattedTags;
                                                if (fallback.Properties.Count > 0)
                                                {
                                                    formattedTags = "[\n" + string.Join(",\n", fallback.Properties.Select(tag =>
                                                    {
                                                        string tagDataFormatted;
                                                        if (tag.Tag is TextProperty text)
                                                        {
                                                            tagDataFormatted = $"\"{text.Value.Text}\"";
                                                        }
                                                        else if (tag.Tag is NameProperty name)
                                                        {
                                                            tagDataFormatted = $"\"{name.Value.Text}\"";
                                                        }
                                                        else if (tag.Tag is ObjectProperty objectprop)
                                                        {
                                                            tagDataFormatted = $"\"{objectprop.Value}\"";
                                                        }
                                                        else
                                                        {
                                                            tagDataFormatted = $"\"{tag.Tag.GenericValue}\"";
                                                        }
                                                        return $"\t\t\"{tag.Name}\": {tagDataFormatted}";
                                                    })) + "\n\t]";
                                                }
                                                else
                                                {
                                                    formattedTags = "{}";
                                                }
                                                ShouldAppend(formattedTags);
                                            }
                                            else if (structTag.StructType is FGameplayTagContainer gameplayTag)
                                            {
                                                var tags = gameplayTag.GameplayTags.ToList();
                                                if (tags.Count > 1)
                                                {
                                                    var formattedTags = "[\n" + string.Join(",\n", tags.Select(tag => $"\t\t\"{tag.TagName}\"")) + "\n\t]";
                                                    ShouldAppend(formattedTags);
                                                }
                                                else
                                                {
                                                    ShouldAppend($"\"{tags.First().TagName}\"");
                                                }
                                            }
                                            else
                                            {
                                                //Console.WriteLine($"Unknown struct type: {structTag.StructType.GetType().Name}");
                                                ShouldAppend($"\"{result}\"");
                                            }
                                        }
                                        else if (key.Tag.GetType().Name == "ObjectProperty" || key.Tag.GetType().Name == "TextProperty" || key.PropertyType == "StrProperty" || key.PropertyType == "NameProperty" || key.PropertyType == "ClassProperty")
                                        {
                                            ShouldAppend($"\"{result}\"");
                                        }
                                        else if (key.Tag.GenericValue is UScriptSet set)
                                        {
                                            var formattedSet = "[\n" + string.Join(",\n", set.Properties.Select(p => $"\t\"{p.GenericValue}\"")) + "\n\t]";
                                            ShouldAppend(formattedSet);
                                        }
                                        else if (key.Tag.GenericValue is UScriptMap map)
                                        {
                                            var formattedMap = "[\n" + string.Join(",\n", map.Properties.Select(kvp => $"\t{{\n\t\t\"{kvp.Key}\": \"{kvp.Value}\"\n\t}}")) + "\n\t]";
                                            ShouldAppend(formattedMap);
                                        }
                                        else if (key.Tag.GenericValue is UScriptArray array)
                                        {
                                            var formattedArray = "[\n" + string.Join(",\n", array.Properties.Select(p =>
                                            {
                                                if (p.GenericValue is FScriptStruct vectorInArray && vectorInArray.StructType is FVector vector)
                                                {
                                                    return $"FVector({vector.X}, {vector.Y}, {vector.Z})";
                                                }
                                                if (p.GenericValue is FScriptStruct vector2dInArray && vector2dInArray.StructType is FVector2D vector2d)
                                                {
                                                    return $"FVector2D({vector2d.X}, {vector2d.Y})";
                                                }
                                                if (p.GenericValue is FScriptStruct structInArray && structInArray.StructType is FRotator rotator)
                                                {
                                                    return $"FRotator({rotator.Pitch}, {rotator.Yaw}, {rotator.Roll})";
                                                }
                                                else if (p.GenericValue is FScriptStruct fallbacksInArray && fallbacksInArray.StructType is FStructFallback fallback)
                                                {
                                                    string formattedTags;
                                                    if (fallback.Properties.Count > 0)
                                                    {
                                                        formattedTags = "\t[\n" + string.Join(",\n", fallback.Properties.Select(tag =>
                                                        {
                                                            string tagDataFormatted;
                                                            if (tag.Tag is TextProperty text)
                                                            {
                                                                tagDataFormatted = $"\"{text.Value.Text}\"";
                                                            }
                                                            else if (tag.Tag is NameProperty name)
                                                            {
                                                                tagDataFormatted = $"\"{name.Value.Text}\"";
                                                            }
                                                            else if (tag.Tag is ObjectProperty objectprop)
                                                            {
                                                                tagDataFormatted = $"\"{objectprop.Value}\"";
                                                            }
                                                            else
                                                            {
                                                                tagDataFormatted = $"\"{tag.Tag.GenericValue}\"";
                                                            }
                                                            return $"\t\t\"{tag.Name}\": {tagDataFormatted}";
                                                        })) + "\n\t]";
                                                    }
                                                    else
                                                    {
                                                        formattedTags = "{}";
                                                    }
                                                    return formattedTags;
                                                }
                                                else if (p.GenericValue is FScriptStruct gameplayTagsInArray && gameplayTagsInArray.StructType is FGameplayTagContainer gameplayTag)
                                                {
                                                    var tags = gameplayTag.GameplayTags.ToList();
                                                    if (tags.Count > 1)
                                                    {
                                                        var formattedTags = "[\n" + string.Join(",\n", tags.Select(tag => $"\t\t\"{tag.TagName}\"")) + "\n\t]";
                                                        return formattedTags;
                                                    }
                                                    else
                                                    {
                                                        return $"\"{tags.First().TagName}\"";
                                                    }
                                                }

                                                return $"\t\t\"{p.GenericValue}\"";
                                            })) + "\n\t]";
                                            ShouldAppend(formattedArray);
                                        }
                                        else if (key.Tag.GenericValue is bool boolResult)
                                        {
                                            ShouldAppend(boolResult.ToString().ToLower());
                                        }
                                        else
                                        {
                                            ShouldAppend(result);
                                        }
                                    }
                                }
                                else
                                {
                                    //outputBuilder.Append($"\nType: {export.Value.Name}");
                                }
                            }
                        }

                        var childProperties = blueprintGeneratedClass?.ChildProperties ?? VerseClass?.ChildProperties;
                        foreach (FProperty property in childProperties)
                        {
                            if (!stringsarr.Contains(property.Name.PlainText))
                                outputBuilder.AppendLine($"\t{Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || Utils.GetPropertyProperty(property) ? "*" : string.Empty)} {property.Name.PlainText.Replace(" ", "")} = {property.Name.PlainText.Replace(" ", "")}placenolder;");
                        }

                        var funcMapOrder = blueprintGeneratedClass?.FuncMap?.Keys.Select(fname => fname.ToString()).ToList() ?? VerseClass?.FuncMap.Keys.Select(fname => fname.ToString()).ToList();

                        var functions = package.ExportsLazy
                            .Where(e => e.Value is UFunction)
                            .Select(e => (UFunction) e.Value)
                            .OrderBy(f =>
                            {
                                if (funcMapOrder != null)
                                {
                                    var functionName = f.Name.ToString();
                                    int index = funcMapOrder.IndexOf(functionName);
                                    return index >= 0 ? index : int.MaxValue;
                                }
                                return int.MaxValue;
                            })
                            .ThenBy(f => f.Name.ToString())
                            .ToList();

                        foreach (var function in functions)
                        {
                            string argsList = "";
                            string returnFunc = "void";
                            if (function?.ChildProperties != null)
                            {
                                foreach (FProperty property in function.ChildProperties)
                                {
                                    if (property.Name.PlainText == "ReturnValue")
                                    {
                                        returnFunc = $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || Utils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}";
                                    }
                                    else if (!(property.Name.ToString().EndsWith("_ReturnValue") ||
                                              property.Name.ToString().StartsWith("CallFunc_") ||
                                              property.Name.ToString().StartsWith("K2Node_") ||
                                              property.Name.ToString().StartsWith("Temp_")) || // removes useless args
                                              property.PropertyFlags.HasFlag(EPropertyFlags.Edit))
                                    {
                                        argsList += $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || Utils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}{(property.PropertyFlags.HasFlag(EPropertyFlags.OutParm) ? "&" : string.Empty)} {Regex.Replace(property.Name.ToString(), @"^__verse_0x[0-9A-Fa-f]+_", "")}, ";
                                    }
                                }
                            }
                            argsList = argsList.TrimEnd(',', ' ');

                            outputBuilder.AppendLine($"\n\t{returnFunc} {function.Name.Replace(" ", "")}({argsList})\n\t{{");
                            if (function?.ScriptBytecode != null)
                            {
                                foreach (KismetExpression property in function.ScriptBytecode)
                                {
                                    ProcessExpression(property.Token, property, outputBuilder);
                                }
                            }
                            else
                            {
                                outputBuilder.Append("\n\t // This function does not have Bytecode \n\n");
                                outputBuilder.Append("\t}\n");
                            }
                        }

                        outputBuilder.Append("\n\n}");
                    }
                    else
                    {
                        Console.WriteLine($"No Blueprint Found nor Verse set in \"{gameFile.Path}\"");
                        continue;
                    }

                    var commonOffsets = statementIndices.Select(si => si.Index).Intersect(jumpCodeOffsets).ToList();
                    if (commonOffsets.Any())
                    {
                        foreach (var offset in commonOffsets)
                        {
                            var statementInfo = statementIndices.First(si => si.Index == offset);
                            var LineIndex = statementInfo.LineNum;

                            string[] lines = Regex.Split(outputBuilder.ToString().Trim(), @"\r?\n|\r");

                            outputBuilder = new StringBuilder(string.Join(Environment.NewLine, lines.Take(LineIndex).Concat(new[] { "\t\tLabel_" + offset.ToString() + ":" }).Concat(lines.Skip(LineIndex))
                            ));

                        }
                    }

                    int targetIndex = outputBuilder.ToString().IndexOf("placenolder");
                    string pattern = $@"\w+placenolder";
                    string updatedOutput = Regex.Replace(outputBuilder.ToString(), pattern, "nullptr");
                    string blueprintDirRel = Path.GetDirectoryName(gameFile.Path);
                    string blueprintDirOutput = Path.Combine(exeDirectory, blueprintDirRel);
                    Directory.CreateDirectory(blueprintDirOutput);
                    string outputFilePath = Path.Combine(blueprintDirOutput, $"{gameFile.Name}.cpp");
                    File.WriteAllText(outputFilePath, updatedOutput);

                    Console.WriteLine($"Output written to: {outputFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred processing \"{gameFile.Path}\": {ex.Message}\n{ex.StackTrace}");
                }
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
            using var webClient = new WebClient();
            string aesJson = await webClient.DownloadStringTaskAsync(aesUrl);
            await File.WriteAllTextAsync(cacheFilePath, aesJson);
            LoadAesKeysFromJson(provider, aesJson);
        }

        provider.PostMount();
        provider.LoadLocalization(ELanguage.English);
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
    }
    private static void ProcessExpression(EExprToken token, KismetExpression expression, StringBuilder outputBuilder, bool isParameter = false)
    {
        statementIndices.Add(new StatementInfo { Index = expression.StatementIndex, LineNum = Regex.Split(outputBuilder.ToString().Trim(), @"\r?\n|\r").Length });
        switch (token)
        {
            case EExprToken.EX_LetValueOnPersistentFrame:
                {
                    EX_LetValueOnPersistentFrame op = (EX_LetValueOnPersistentFrame) expression;
                    EX_VariableBase opp = (EX_VariableBase) op.AssignmentExpression;
                    var nerd = ProcessTextProperty(op.DestinationProperty);
                    var nerdd = ProcessTextProperty(opp.Variable);
                    
                    if (!isParameter)
                    {
                        outputBuilder.Append($"\t\t{(nerd.Contains("K2Node_") ? $"UberGraphFrame->{nerd}" : nerd)} = {nerdd};\n\n"); // hardcoded but works
                    }
                    else
                    {
                        outputBuilder.Append($"\t\t{(nerd.Contains("K2Node_") ? $"UberGraphFrame->{nerd}" : nerd)} = {nerdd}");
                    }
                    break;
                }
            case EExprToken.EX_LocalFinalFunction:
                {
                    EX_FinalFunction op = (EX_FinalFunction) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;
                    if (isParameter)
                    {
                        outputBuilder.Append($"{op.StackNode.Name.Replace(" ", "")}(");
                    }
                    else if (opp.Length < 1)
                    {
                        outputBuilder.Append($"\t\t{op?.StackNode?.Name.Replace(" ", "")}(");
                    }
                    else
                    {
                        outputBuilder.Append($"\t\t{Utils.GetPrefix(op?.StackNode?.ResolvedObject?.Outer?.GetType()?.Name)}{op?.StackNode?.Name.Replace(" ", "")}(");
                    }

                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4)
                            outputBuilder.Append("\n\t\t");
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    if (isParameter)
                    {
                        outputBuilder.Append($")");
                    }
                    else
                    {
                        outputBuilder.Append($");\n");
                    }
                    break;
                }
            case EExprToken.EX_FinalFunction:
                {
                    EX_FinalFunction op = (EX_FinalFunction) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;
                    if (isParameter)
                    {
                        outputBuilder.Append($"{op.StackNode.Name.Replace(" ", "")}(");
                    }
                    else if (opp.Length < 1)
                    {
                        outputBuilder.Append($"\t\t{op?.StackNode?.Name.Replace(" ", "")}(");
                    }
                    else
                    {
                        outputBuilder.Append($"\t\t{op?.StackNode?.Name.Replace(" ", "")}(");//{Utils.GetPrefix(op?.StackNode?.ResolvedObject?.Outer?.GetType()?.Name)}
                    }

                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4)
                            outputBuilder.Append("\n\t\t");
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    if (isParameter)
                    {
                        outputBuilder.Append($")");
                    }
                    else
                    {
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_CallMath:
                {
                    EX_FinalFunction op = (EX_FinalFunction) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;
                    outputBuilder.Append(isParameter ? String.Empty : "\t\t");
                    outputBuilder.Append($"{Utils.GetPrefix(op.StackNode.ResolvedObject.Outer.GetType().Name)}{op.StackNode.ResolvedObject.Outer.Name.ToString().Replace(" ", "")}::{op.StackNode.Name}(");
                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4) outputBuilder.Append("\n\t\t");
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    if (isParameter)
                    {
                        outputBuilder.Append($")");
                    }
                    else
                    {
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_LocalVirtualFunction:
            case EExprToken.EX_VirtualFunction:
                {
                    EX_VirtualFunction op = (EX_VirtualFunction) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;

                    if (isParameter)
                    {
                        outputBuilder.Append($"{op.VirtualFunctionName.PlainText.Replace(" ", "")}(");
                    }
                    else
                    {
                        outputBuilder.Append($"\t\t{op.VirtualFunctionName.PlainText.Replace(" ", "")}(");
                    }
                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4) outputBuilder.Append("\n\t\t");

                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    if (isParameter)
                    {
                        outputBuilder.Append($")");
                    }
                    else
                    {
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_ComputedJump:
                {
                    EX_ComputedJump op = (EX_ComputedJump) expression;
                    if (op.CodeOffsetExpression is EX_VariableBase)
                    {
                        EX_VariableBase opp = (EX_VariableBase) op.CodeOffsetExpression;
                        outputBuilder.AppendLine($"\t\tgoto {ProcessTextProperty(((EX_VariableBase) op.CodeOffsetExpression).Variable)};\n");
                    }
                    else
                    {
                        EX_CallMath opp = (EX_CallMath) op.CodeOffsetExpression;
                        ProcessExpression(opp.Token, opp, outputBuilder, true);
                    }
                    break;
                }
            case EExprToken.EX_PopExecutionFlowIfNot:
                {
                    EX_PopExecutionFlowIfNot op = (EX_PopExecutionFlowIfNot) expression;
                    outputBuilder.Append("\t\tif (!");
                    ProcessExpression(op.BooleanExpression.Token, op.BooleanExpression, outputBuilder, true);
                    outputBuilder.Append(") \r\n");
                    outputBuilder.Append($"\t\t    FlowStack.Pop();\n\n");
                    break;
                }
            case EExprToken.EX_Cast:
                {
                    EX_Cast op = (EX_Cast) expression;// support CST_ObjectToInterface when i have a example of how it works

                    if (ECastToken.CST_ObjectToBool == op.ConversionType || ECastToken.CST_InterfaceToBool == op.ConversionType)
                    {
                        outputBuilder.Append("(bool)");
                    }
                    if (ECastToken.CST_DoubleToFloat == op.ConversionType)
                    {
                        outputBuilder.Append("(float)");
                    }
                    if (ECastToken.CST_FloatToDouble == op.ConversionType)
                    {
                        outputBuilder.Append("(double)");
                    }
                    ProcessExpression(op.Target.Token, op.Target, outputBuilder);
                    break;
                }
            case EExprToken.EX_InterfaceContext:
                {
                    EX_InterfaceContext op = (EX_InterfaceContext) expression;
                    ProcessExpression(op.InterfaceValue.Token, op.InterfaceValue, outputBuilder);
                    break;
                }
            case EExprToken.EX_ArrayConst:
                {
                    EX_ArrayConst op = (EX_ArrayConst) expression;
                    outputBuilder.Append("TArray {");
                    foreach (KismetExpression element in op.Elements)
                    {
                        outputBuilder.Append(" ");
                        ProcessExpression(element.Token, element, outputBuilder);
                    }
                    if (op.Elements.Length < 1)
                        outputBuilder.Append("  ");
                    else
                        outputBuilder.Append(" ");

                    outputBuilder.Append("}");
                    break;
                }
            case EExprToken.EX_SetArray:
                {
                    EX_SetArray op = (EX_SetArray) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.AssigningProperty.Token, op.AssigningProperty, outputBuilder);
                    outputBuilder.Append(" = ");
                    outputBuilder.Append("TArray {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        KismetExpression element = op.Elements[i];
                        outputBuilder.Append(" ");
                        ProcessExpression(element.Token, element, outputBuilder);

                        if (i < op.Elements.Length - 1)
                        {
                            outputBuilder.Append(",");
                        } else
                        {
                            outputBuilder.Append(" ");
                        }
                    }

                    if (op.Elements.Length < 1)
                        outputBuilder.Append("  ");
                    else
                        outputBuilder.Append(" ");

                    outputBuilder.Append("};\n\n");
                    break;
                }
            case EExprToken.EX_SetMap:
                {
                    EX_SetMap op = (EX_SetMap) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.MapProperty.Token, op.MapProperty, outputBuilder);
                    outputBuilder.Append(" = ");
                    outputBuilder.Append("TMap {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        var element = op.Elements[i];
                        outputBuilder.Append(" ");
                        ProcessExpression(element.Token, element, outputBuilder);

                        if (i < op.Elements.Length - 1)
                        {
                            if (element.Token == EExprToken.EX_InstanceVariable)
                            {
                                outputBuilder.Append(": ");
                            }
                            else
                            {
                                outputBuilder.Append(", ");
                            }
                        } else
                        {
                            outputBuilder.Append(" ");
                        }
                    }

                    if (op.Elements.Length < 1)
                        outputBuilder.Append("  ");
                    outputBuilder.Append("}\n");
                    break;
                }
            case EExprToken.EX_SwitchValue:
                {
                    EX_SwitchValue op = (EX_SwitchValue) expression;

                    bool useTernary = op.Cases.Length <= 2
                        && op.Cases.All(c => c.CaseIndexValueTerm.Token == EExprToken.EX_True || c.CaseIndexValueTerm.Token == EExprToken.EX_False);

                    if (useTernary)
                    {
                        ProcessExpression(op.IndexTerm.Token, op.IndexTerm, outputBuilder);
                        outputBuilder.Append(" ? ");

                        bool isFirst = true;
                        foreach (var caseItem in op.Cases.Where(c => c.CaseIndexValueTerm.Token == EExprToken.EX_True))
                        {
                            if (!isFirst)
                                outputBuilder.Append(" : ");

                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder, true);
                            isFirst = false;
                        }

                        foreach (var caseItem in op.Cases.Where(c => c.CaseIndexValueTerm.Token == EExprToken.EX_False))
                        {
                            if (!isFirst)
                                outputBuilder.Append(" : ");

                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder, true);
                        }
                    }
                    else
                    {
                        outputBuilder.Append("switch (");
                        ProcessExpression(op.IndexTerm.Token, op.IndexTerm, outputBuilder);
                        outputBuilder.Append(")\n");
                        outputBuilder.Append("{\n");

                        foreach (var caseItem in op.Cases)
                        {
                            if (caseItem.CaseIndexValueTerm.Token == EExprToken.EX_IntConst)
                            {
                                int caseValue = ((EX_IntConst) caseItem.CaseIndexValueTerm).Value;
                                outputBuilder.Append($"\t\tcase {caseValue}:\n");
                            }
                            else
                            {
                                outputBuilder.Append("\t\tcase ");
                                ProcessExpression(caseItem.CaseIndexValueTerm.Token, caseItem.CaseIndexValueTerm, outputBuilder);
                                outputBuilder.Append(":\n");
                            }

                            outputBuilder.Append("\t\t{\n");
                            outputBuilder.Append("\t\t    ");
                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder);
                            outputBuilder.Append(";\n");
                            outputBuilder.Append("\t\t    break;\n");
                            outputBuilder.Append("\t\t}\n");
                        }

                        if (op.DefaultTerm != null)
                        {
                            outputBuilder.Append("\t\tdefault:\n");
                            outputBuilder.Append("\t\t{\n");
                            outputBuilder.Append("\t\t    ");
                            ProcessExpression(op.DefaultTerm.Token, op.DefaultTerm, outputBuilder);
                            outputBuilder.Append("\n\t\t}\n\n");
                        }

                        outputBuilder.Append("}\n");
                    }
                    break;
                }
            case EExprToken.EX_ArrayGetByRef: // i assume get array with index
                {
                    EX_ArrayGetByRef op = (EX_ArrayGetByRef) expression; // FortniteGame/Plugins/GameFeatures/FM/PilgrimCore/Content/Player/Components/BP_PilgrimPlayerControllerComponent.uasset
                    ProcessExpression(op.ArrayVariable.Token, op.ArrayVariable, outputBuilder, true);
                    outputBuilder.Append("[");
                    ProcessExpression(op.ArrayIndex.Token, op.ArrayIndex, outputBuilder);
                    outputBuilder.Append("]");
                    break;
                }
            case EExprToken.EX_MetaCast:
            case EExprToken.EX_DynamicCast:
            case EExprToken.EX_ObjToInterfaceCast:
            case EExprToken.EX_CrossInterfaceCast:
            case EExprToken.EX_InterfaceToObjCast:
                {
                    EX_CastBase op = (EX_CastBase)expression;
                    outputBuilder.Append($"Cast<U{op.ClassPtr.Name}*>(");// m?
                    ProcessExpression(op.Target.Token, op.Target, outputBuilder, true);
                    outputBuilder.Append(")");
                    break;
                }
            case EExprToken.EX_StructConst:
                {
                    EX_StructConst op = (EX_StructConst)expression;
                    outputBuilder.Append($"{Utils.GetPrefix(op.Struct.GetType().Name)}{op.Struct.Name}");
                    outputBuilder.Append($"(");
                    for (int i = 0; i < op.Properties.Length; i++)
                    {
                        var property = op.Properties[i];
                        ProcessExpression(property.Token, property, outputBuilder);
                        if (i < op.Properties.Length - 1 && property.Token != EExprToken.EX_ArrayConst)
                            outputBuilder.Append(", ");
                    }
                    outputBuilder.Append($")");
                    break;
                }
            case EExprToken.EX_ObjectConst:
                {
                    EX_ObjectConst op = (EX_ObjectConst)expression;
                    if (!isParameter)
                    {
                        outputBuilder.Append("\t\tFindObject<");
                    }
                    else
                    {
                        outputBuilder.Append("FindObject<");
                    }
                    string classString = op?.Value?.ResolvedObject?.Class?.ToString()?.Replace("'", "");

                    if (classString?.Contains(".") == true)
                    {
                        
                        outputBuilder.Append(Utils.GetPrefix(op?.Value?.ResolvedObject?.Class.GetType().Name) + classString.Split(".")[1]);
                    }
                    else
                    {
                        outputBuilder.Append(Utils.GetPrefix(op?.Value?.ResolvedObject?.Class.GetType().Name) + classString);
                    }
                    outputBuilder.Append(">(\"");
                    outputBuilder.Append(
                        op?.Value?.ResolvedObject?.Outer // sometimes incorrect
                            .ToString()
                            .Replace("'", "")
                            .Replace(op?.Value?.ResolvedObject?.Class.ToString().Replace("'", ""), "") +
                        "." +
                        op.Value.Name
                    );
                    if (isParameter)
                    {
                        outputBuilder.Append("\")");
                    }
                    else
                    {
                        outputBuilder.Append("\")");
                    }
                    break;
                }
            case EExprToken.EX_BindDelegate:
                {
                    EX_BindDelegate op = (EX_BindDelegate)expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder);
                    outputBuilder.Append($".BindUFunction(");
                    ProcessExpression(op.ObjectTerm.Token, op.ObjectTerm, outputBuilder);
                    outputBuilder.Append($", \"{op.FunctionName}\"");
                    outputBuilder.Append($");\n\n");
                    break;
                }
            // all the delegate functions suck
            case EExprToken.EX_AddMulticastDelegate:
                {
                    EX_AddMulticastDelegate op = (EX_AddMulticastDelegate) expression;
                    if (op.Delegate.Token == EExprToken.EX_LocalVariable || op.Delegate.Token == EExprToken.EX_InstanceVariable)
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append(".AddDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                        outputBuilder.Append($");\n\n");
                    } else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine($"Issue: EX_AddMulticastDelegate missing info: {op.StatementIndex}, {op.Delegate.Token}");
                    } else
                    {
                        EX_Context opp = (EX_Context) op.Delegate;
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        //outputBuilder.Append("->");
                        //ProcessExpression(opp.ContextExpression.Token, opp.ContextExpression, outputBuilder);
                        outputBuilder.Append(".AddDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_RemoveMulticastDelegate: // everything here has been guessed not compared to actual UE but does work fine and displays all infomation
                {
                    EX_RemoveMulticastDelegate op = (EX_RemoveMulticastDelegate) expression;
                    if (op.Delegate.Token == EExprToken.EX_LocalVariable || op.Delegate.Token == EExprToken.EX_InstanceVariable)
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append(".RemoveDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                        outputBuilder.Append($");\n\n");
                    }
                    else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine("Issue: EX_RemoveMulticastDelegate missing info: ", op.StatementIndex);
                    }
                    else
                    {
                        EX_Context opp = (EX_Context) op.Delegate;
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append("->");
                        ProcessExpression(opp.ContextExpression.Token, opp.ContextExpression, outputBuilder);
                        outputBuilder.Append(".RemoveDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder);
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_ClearMulticastDelegate: // this also
                {
                    EX_ClearMulticastDelegate op = (EX_ClearMulticastDelegate) expression;
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.DelegateToClear.Token, op.DelegateToClear, outputBuilder, true);
                        outputBuilder.Append(".Clear();\n\n");
                    break;
                }
            case EExprToken.EX_CallMulticastDelegate: // this also
                {
                    EX_CallMulticastDelegate op = (EX_CallMulticastDelegate) expression;
                    KismetExpression[] opp = (KismetExpression[]) op.Parameters;
                    if (op.Delegate.Token == EExprToken.EX_LocalVariable || op.Delegate.Token == EExprToken.EX_InstanceVariable)
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append(".Call(");
                        for (int i = 0; i < opp.Length; i++)
                        {
                            if (opp.Length > 4)
                                outputBuilder.Append("\n\t\t");
                            ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                            if (i < opp.Length - 1)
                            {
                                outputBuilder.Append(", ");
                            }
                        }
                        outputBuilder.Append($");\n\n");
                    } else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine("Issue: EX_CallMulticastDelegate missing info: ", op.StatementIndex);
                    }
                    else
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, true);
                        outputBuilder.Append(".Call(");
                        for (int i = 0; i < opp.Length; i++)
                        {
                            if (opp.Length > 4)
                                outputBuilder.Append("\n\t\t");
                            ProcessExpression(opp[i].Token, opp[i], outputBuilder, true);
                            if (i < opp.Length - 1)
                            {
                                outputBuilder.Append(", ");
                            }
                        }
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_ClassContext:
            case EExprToken.EX_Context:
                {
                    EX_Context op = (EX_Context)expression;
                    ProcessExpression(op.ObjectExpression.Token, op.ObjectExpression, outputBuilder, true);

                    outputBuilder.Append("->");
                    ProcessExpression(op.ContextExpression.Token, op.ContextExpression, outputBuilder, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append(";\n\n");
                    }
                    break;
                }
            case EExprToken.EX_Context_FailSilent:
                {
                    EX_Context op = (EX_Context)expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.ObjectExpression.Token, op.ObjectExpression, outputBuilder, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append("->");
                        ProcessExpression(op.ContextExpression.Token, op.ContextExpression, outputBuilder, true);
                        outputBuilder.Append($";\n\n");
                    }
                    break;
                }
            case EExprToken.EX_Let:
                {
                    EX_Let op = (EX_Let)expression;
                    if (!isParameter)
                    {
                        outputBuilder.Append("\t\t");
                    }
                    ProcessExpression(op.Variable.Token, op.Variable, outputBuilder, true);
                    outputBuilder.Append(" = ");
                    ProcessExpression(op.Assignment.Token, op.Assignment, outputBuilder, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append(";\n\n");
                    }
                    break;
                }
            case EExprToken.EX_LetObj:
            case EExprToken.EX_LetWeakObjPtr:
            case EExprToken.EX_LetBool:
            case EExprToken.EX_LetDelegate:
            case EExprToken.EX_LetMulticastDelegate:
                {
                    EX_LetBase op = (EX_LetBase)expression;
                    if (!isParameter)
                    {
                        outputBuilder.Append("\t\t");
                    }
                    ProcessExpression(op.Variable.Token, op.Variable, outputBuilder, true);
                    outputBuilder.Append(" = ");
                    ProcessExpression(op.Assignment.Token, op.Assignment, outputBuilder, true);
                    if (!isParameter || op.Assignment.Token == EExprToken.EX_LocalFinalFunction || op.Assignment.Token == EExprToken.EX_FinalFunction || op.Assignment.Token == EExprToken.EX_CallMath)
                    {
                        outputBuilder.Append($";\n\n");
                    }
                    else
                    {
                        outputBuilder.Append($";");
                    }
                    break;
                }
            case EExprToken.EX_JumpIfNot:
                {
                    EX_JumpIfNot op = (EX_JumpIfNot)expression;
                    jumpCodeOffsets.Add((int)op.CodeOffset);
                    outputBuilder.Append("\t\tif (!");
                    ProcessExpression(op.BooleanExpression.Token, op.BooleanExpression, outputBuilder, true);
                    outputBuilder.Append(") \r\n");
                    outputBuilder.Append("\t\t    goto Label_");
                    outputBuilder.Append(op.CodeOffset);
                    outputBuilder.Append(";\n\n");
                    break;
                }
            case EExprToken.EX_Jump:
                {
                    EX_Jump op = (EX_Jump)expression;
                    jumpCodeOffsets.Add((int)op.CodeOffset);
                    outputBuilder.Append($"\t\tgoto Label_{op.CodeOffset};\n\n");
                    break;
                }
            // Static expressions

            case EExprToken.EX_TextConst:
                if (expression is EX_TextConst textConst)
                {
                    if (textConst.Value is FScriptText scriptText)
                    {

                        if (scriptText.SourceString != null)
                            ProcessExpression(scriptText.SourceString.Token, scriptText.SourceString, outputBuilder, true); // cursed sometimes will need to be correctly done
                    }
                    else
                    {
                        outputBuilder.Append(textConst.Value.ToString());
                    }
                }
                break;
            case EExprToken.EX_StructMemberContext:
                {
                    EX_StructMemberContext op = (EX_StructMemberContext)expression;
                    ProcessExpression(op.StructExpression.Token, op.StructExpression, outputBuilder);
                    outputBuilder.Append(".");
                    outputBuilder.Append(ProcessTextProperty(op.Property));
                    break;
                }

            case EExprToken.EX_Return:
                {
                    EX_Return op = (EX_Return)expression;
                    bool tocheck = op.ReturnExpression.Token == EExprToken.EX_Nothing;
                    outputBuilder.Append($"\t\treturn");
                    if (!tocheck) outputBuilder.Append(" ");
                    ProcessExpression(op.ReturnExpression.Token, op.ReturnExpression, outputBuilder, true);
                    outputBuilder.AppendLine(";\n\n");
                    break;
                }
            case EExprToken.EX_RotationConst:
                {
                    EX_RotationConst op = (EX_RotationConst)expression;
                    FRotator value = op.Value;
                    outputBuilder.Append($"FRotator({value.Pitch}, {value.Yaw}, {value.Roll})");
                    break;
                }
            case EExprToken.EX_VectorConst:
                {
                    EX_VectorConst op = (EX_VectorConst)expression;
                    FVector value = op.Value;
                    outputBuilder.Append($"FVector({value.X}, {value.Y}, {value.Z})");
                    break;
                }
            case EExprToken.EX_Vector3fConst:
                {
                    EX_Vector3fConst op = (EX_Vector3fConst) expression;
                    FVector value = op.Value;
                    outputBuilder.Append($"FVector3f({value.X}, {value.Y}, {value.Z})");
                    break;
                }
            case EExprToken.EX_TransformConst:
                {
                    EX_TransformConst op = (EX_TransformConst) expression;
                    FTransform value = op.Value;
                    outputBuilder.Append($"FTransform(FQuat({value.Rotation.X}, {value.Rotation.Y}, {value.Rotation.Z}, {value.Rotation.W}), FVector({value.Translation.X}, {value.Translation.Y}, {value.Translation.Z}), FVector({value.Scale3D.X}, {value.Scale3D.Y}, {value.Scale3D.Z}))");
                    break;
                }


            case EExprToken.EX_LocalVariable:
            case EExprToken.EX_DefaultVariable:
            case EExprToken.EX_InstanceVariable:
            case EExprToken.EX_LocalOutVariable:
            case EExprToken.EX_ClassSparseDataVariable:
                outputBuilder.Append(ProcessTextProperty(((EX_VariableBase) expression).Variable));
                break;

            case EExprToken.EX_ByteConst: case EExprToken.EX_IntConstByte: outputBuilder.Append($"0x{((KismetExpression<byte>)expression).Value.ToString("X")}"); break;
            case EExprToken.EX_SoftObjectConst: ProcessExpression(((EX_SoftObjectConst) expression).Value.Token, ((EX_SoftObjectConst) expression).Value, outputBuilder); break;
            case EExprToken.EX_DoubleConst: outputBuilder.Append(((EX_DoubleConst) expression).Value == (int) ((EX_DoubleConst) expression).Value ? (int) ((EX_DoubleConst) expression).Value : ((EX_DoubleConst) expression).Value.ToString("R")); break;
            case EExprToken.EX_NameConst: outputBuilder.Append($"\"{((EX_NameConst)expression).Value}\""); break;
            case EExprToken.EX_IntConst: outputBuilder.Append(((EX_IntConst)expression).Value.ToString()); break;
            case EExprToken.EX_PropertyConst: outputBuilder.Append(ProcessTextProperty(((EX_PropertyConst)expression).Property)); break;
            case EExprToken.EX_StringConst: outputBuilder.Append($"\"{((EX_StringConst)expression).Value}\""); break;
            case EExprToken.EX_Int64Const: outputBuilder.Append(((EX_Int64Const)expression).Value.ToString()); break;
            case EExprToken.EX_UInt64Const: outputBuilder.Append(((EX_UInt64Const)expression).Value.ToString()); break;
            case EExprToken.EX_SkipOffsetConst: outputBuilder.Append(((EX_SkipOffsetConst)expression).Value.ToString()); break;
            case EExprToken.EX_FloatConst: outputBuilder.Append(((EX_FloatConst)expression).Value.ToString()); break;
            case EExprToken.EX_BitFieldConst: outputBuilder.Append(((EX_BitFieldConst)expression).ConstValue); break;
            case EExprToken.EX_UnicodeStringConst: outputBuilder.Append(((EX_UnicodeStringConst)expression).Value); break;
            case EExprToken.EX_EndOfScript: case EExprToken.EX_EndParmValue: outputBuilder.Append("\t}\n"); break;
            case EExprToken.EX_NoObject: case EExprToken.EX_NoInterface: outputBuilder.Append("nullptr"); break;
            case EExprToken.EX_IntOne: outputBuilder.Append(1); break;
            case EExprToken.EX_IntZero: outputBuilder.Append(0); break;
            case EExprToken.EX_True: outputBuilder.Append("true"); break;
            case EExprToken.EX_False: outputBuilder.Append("false"); break;
            case EExprToken.EX_Self: outputBuilder.Append("this"); break;

            case EExprToken.EX_Nothing:
            case EExprToken.EX_NothingInt32:
            case EExprToken.EX_EndFunctionParms:
            case EExprToken.EX_EndStructConst:
            case EExprToken.EX_EndArray:
            case EExprToken.EX_EndArrayConst:
            case EExprToken.EX_EndSet:
            case EExprToken.EX_EndMap:
            case EExprToken.EX_EndMapConst:
            case EExprToken.EX_EndSetConst:
            case EExprToken.EX_PushExecutionFlow:
            case EExprToken.EX_PopExecutionFlow:
            case EExprToken.EX_DeprecatedOp4A:
            case EExprToken.EX_WireTracepoint:
            case EExprToken.EX_Tracepoint:
            case EExprToken.EX_Breakpoint:
            case EExprToken.EX_AutoRtfmStopTransact:
            case EExprToken.EX_AutoRtfmTransact:
            case EExprToken.EX_AutoRtfmAbortIfNot:
                // some here are "useful" and unsupported
                break;
            /*
            EExprToken.EX_Assert
            EExprToken.EX_Skip
            EExprToken.EX_InstrumentationEvent
            EExprToken.EX_FieldPathConst
            */
            default:
                Console.WriteLine($"Error: Unknown bytecode {token}");
                outputBuilder.Append($"{token}");
                break;
        }
    }
}

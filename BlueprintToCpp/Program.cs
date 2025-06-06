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
using CUE4Parse.UE4.Assets.Exports.Verse;
using CUE4Parse.UE4.Assets.Objects;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.Utils;
using System.Globalization;
using System.Linq.Expressions;
using CUE4Parse.FileProvider.Objects;

namespace Main;

public static class Program
{
    private static bool _isVerse;

    private static string ProcessTextProperty(FKismetPropertyPointer property)
    {
        if (property.New is null)
        {
            return property.Old?.Name ?? string.Empty;
        }
        if (_isVerse)
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

            var provider = InitializeProvider(pakFolderPath, usmapPath, oodlePath, zlibPath, version);
            provider.ReadScriptData = true;
            await LoadAesKeysAsync(provider,
                "https://fortnitecentral.genxgames.gg/api/v1/aes"); // allow users to change the aes url?

            var files = new Dictionary<string, CUE4Parse.FileProvider.Objects.GameFile[]>();

            bool isFile = provider.Files.ContainsKey(blueprintPath);
            if (string.IsNullOrEmpty(blueprintPath))
            {
                files = provider.Files.Values
                    .Where(f => (f.Path.EndsWith(".uasset") || f.Path.EndsWith(".umap")) && !f.Path.Contains(".o."))
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
                               !f.Path.Contains(".o."))
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
                        index++;
                        Console.WriteLine($"Processing {package.Path} ({index}/{totalGameFiles})");
                        var pkg = provider.LoadPackage(package);

                        for (var i = 0; i < pkg.ExportMapLength; i++)
                        {
                            var pointer = new FPackageIndex(pkg, i + 1).ResolvedObject;
                            if (pointer?.Object is null)
                                continue;

                            var dummy = ((AbstractUePackage) pkg).ConstructObject(
                                pointer.Class?.Object?.Value as UStruct, pkg);
                            switch (dummy)
                            {
                                case UBlueprintGeneratedClass _:
                                case UVerseClass _:
                                    {
                                        var outputBuilder = new StringBuilder();

                                        var blueprintGeneratedClass = pkg.ExportsLazy.FirstOrDefault(e => e.Value is UBlueprintGeneratedClass)?.Value as UBlueprintGeneratedClass;
                                        var verseClass = pkg.ExportsLazy.Where(export => export.Value is UVerseClass).Select(export => (UVerseClass) export.Value).FirstOrDefault();

                                        if (verseClass != null)
                                            _isVerse = true;
                                        if (blueprintGeneratedClass != null || _isVerse)
                                        {
                                            var mainClass = blueprintGeneratedClass?.Name ?? verseClass?.Name;
                                            var superStructName = blueprintGeneratedClass?.SuperStruct?.Name ?? verseClass?.SuperStruct?.Name ?? string.Empty;
                                            outputBuilder.AppendLine(
                                                $"class {Utils.GetPrefix(blueprintGeneratedClass?.GetType().Name ?? verseClass?.GetType().Name)}{mainClass} : public {Utils.GetPrefix(blueprintGeneratedClass?.GetType().Name ?? verseClass?.GetType().Name)}{superStructName}\n{{\npublic:");

                                            var stringsarray = new List<string>();
                                            foreach (var export in pkg.ExportsLazy)
                                            {
                                                if (export.Value is not UBlueprintGeneratedClass)
                                                {
                                                    if (export.Value.Name.StartsWith("Default__") &&
                                                        export.Value.Name.EndsWith(mainClass ?? string.Empty))
                                                    {
                                                        var exportObject = export.Value;
                                                        foreach (var key in exportObject.Properties)
                                                        {
                                                            stringsarray.Add(key.Name.PlainText);
                                                            string placeholder = $"{key.Name}placenolder";
                                                            string result = key.Tag.GenericValue.ToString();
                                                            string keyName = key.Name.PlainText.Replace(" ", "");

                                                            var propertyTag = key.Tag.GetValue(typeof(object));

                                                            void ShouldAppend(string? value)
                                                            {
                                                                if (value == null)
                                                                    return;
                                                                if (outputBuilder.ToString().Contains(placeholder))
                                                                {
                                                                    outputBuilder.Replace(placeholder, value);
                                                                }
                                                                else
                                                                {
                                                                    outputBuilder.AppendLine(
                                                                        $"\t{Utils.GetPropertyType(propertyTag)} {keyName} = {value};");
                                                                }
                                                            }

                                                            if (key.Tag.GenericValue is FScriptStruct structTag)
                                                            {
                                                                if (structTag.StructType is FVector vector)
                                                                {
                                                                    ShouldAppend(
                                                                        $"FVector({vector.X}, {vector.Y}, {vector.Z})");
                                                                }
                                                                else if (structTag.StructType is FGuid guid)
                                                                {
                                                                    ShouldAppend(
                                                                        $"FGuid({guid.A}, {guid.B}, {guid.C}, {guid.D})");
                                                                }
                                                                else if (structTag.StructType is TIntVector3<int> vector3)
                                                                {
                                                                    ShouldAppend(
                                                                        $"FVector({vector3.X}, {vector3.Y}, {vector3.Z})");
                                                                }
                                                                else if (structTag.StructType is TIntVector3<float>
                                                                         floatVector3)
                                                                {
                                                                    ShouldAppend(
                                                                        $"FVector({floatVector3.X}, {floatVector3.Y}, {floatVector3.Z})");
                                                                }
                                                                else if (structTag.StructType is TIntVector2<float>
                                                                         floatVector2)
                                                                {
                                                                    ShouldAppend(
                                                                        $"FVector2D({floatVector2.X}, {floatVector2.Y})");
                                                                }
                                                                else if (structTag.StructType is FVector2D vector2d)
                                                                {
                                                                    ShouldAppend($"FVector2D({vector2d.X}, {vector2d.Y})");
                                                                }
                                                                else if (structTag.StructType is FRotator rotator)
                                                                {
                                                                    ShouldAppend(
                                                                        $"FRotator({rotator.Pitch}, {rotator.Yaw}, {rotator.Roll})");
                                                                }
                                                                else if (structTag.StructType is FStructFallback fallback)
                                                                {
                                                                    string formattedTags;
                                                                    if (fallback.Properties.Count > 0)
                                                                    {
                                                                        formattedTags = "[\n" + string.Join(",\n",
                                                                            fallback.Properties.Select(tag =>
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
                                                                                else if (tag.Tag is ObjectProperty
                                                                                 objectproperty)
                                                                                {
                                                                                    tagDataFormatted =
                                                                                        $"\"{objectproperty.Value}\"";
                                                                                }
                                                                                else if (tag.Tag.GenericValue is FScriptStruct innerStruct && innerStruct.StructType is FStructFallback nestedFallback)
                                                                                {
                                                                                    if (nestedFallback.Properties.Count > 0)
                                                                                    {
                                                                                        tagDataFormatted = "{ " + string.Join(", ",
                                                                                            nestedFallback.Properties.Select(nested =>
                                                                                            {
                                                                                                string nestedVal;
                                                                                                if (nested.Tag is TextProperty textProp)
                                                                                                    nestedVal = $"\"{textProp.Value.Text}\"";
                                                                                                else if (nested.Tag is NameProperty nameProp)
                                                                                                    nestedVal = $"\"{nameProp.Value.Text}\"";
                                                                                                else
                                                                                                    nestedVal = $"\"{nested.Tag.GenericValue}\"";

                                                                                                return $"\"{nested.Name}\": {nestedVal}";
                                                                                            })) + " }";
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        tagDataFormatted = "{}";
                                                                                    }
                                                                                }
                                                                                else
                                                                                {
                                                                                    tagDataFormatted = tag.Tag.GenericValue != null ? tag.Tag.GenericValue.ToString() : "{}";
                                                                                }
                                                                                ;

                                                                                return $"\t\t{{ \"{tag.Name}\": {tagDataFormatted} }}";
                                                                            })) + "\n\t]";
                                                                    }
                                                                    else
                                                                    {
                                                                        formattedTags = "[]";
                                                                    }

                                                                    ShouldAppend(formattedTags);
                                                                }
                                                                else if (structTag.StructType is FGameplayTagContainer
                                                                         gameplayTag)
                                                                {
                                                                    var tags = gameplayTag.GameplayTags.ToList();
                                                                    if (tags.Count > 1)
                                                                    {
                                                                        var formattedTags = "[\n" + string.Join(",\n",
                                                                                tags.Select(tag =>
                                                                                    $"\t\t\"{tag.TagName}\"")) +
                                                                            "\n\t]";
                                                                        ShouldAppend(formattedTags);
                                                                    }
                                                                    else if (tags.Any())
                                                                    {
                                                                        ShouldAppend($"\"{tags.First().TagName}\"");
                                                                    }
                                                                    else
                                                                    {
                                                                        ShouldAppend("[]");
                                                                    }
                                                                }
                                                                else if (structTag.StructType is FLinearColor color)
                                                                {
                                                                    ShouldAppend(
                                                                        $"FLinearColor({color.R}, {color.G}, {color.B}, {color.A})");
                                                                }
                                                                else
                                                                {
                                                                    //Console.WriteLine($"Unknown struct type: {structTag.StructType.GetType().Name}");
                                                                    ShouldAppend($"\"{result}\"");
                                                                }
                                                            }
                                                            else if (key.Tag.GetType().Name == "ObjectProperty" ||
                                                                     key.Tag.GetType().Name == "TextProperty" ||
                                                                     key.PropertyType == "StrProperty" ||
                                                                     key.PropertyType == "NameProperty" ||
                                                                     key.PropertyType == "ClassProperty")
                                                            {
                                                                ShouldAppend($"\"{result}\"");
                                                            }
                                                            else if (key.Tag.GenericValue is UScriptSet set)
                                                            {
                                                                var formattedSet = "[\n" + string.Join(",\n",
                                                                                       set.Properties.Select(p =>
                                                                                           $"\t\"{p.GenericValue}\"")) +
                                                                                   "\n\t]";
                                                                ShouldAppend(formattedSet);
                                                            }
                                                            else if (key.Tag.GenericValue is UScriptMap map)
                                                            {
                                                                var formattedMap = "[\n" + string.Join(",\n",
                                                                                       map.Properties.Select(kvp =>
                                                                                           $"\t{{\n\t\t\"{kvp.Key}\": \"{kvp.Value}\"\n\t}}")) +
                                                                                   "\n\t]";
                                                                ShouldAppend(formattedMap);
                                                            }
                                                            else if (key.Tag.GenericValue is UScriptArray array)
                                                            {
                                                                var formattedArray = "[\n" + string.Join(",\n",
                                                                    array.Properties.Select(p =>
                                                                    {
                                                                        if (p.GenericValue is FScriptStruct vectorInArray &&
                                                                            vectorInArray.StructType is FVector vector)
                                                                        {
                                                                            return
                                                                                $"FVector({vector.X}, {vector.Y}, {vector.Z})";
                                                                        }

                                                                        if (p.GenericValue is FScriptStruct
                                                                                vector2dInArray &&
                                                                            vector2dInArray
                                                                                .StructType is FVector2D vector2d)
                                                                        {
                                                                            return $"FVector2D({vector2d.X}, {vector2d.Y})";
                                                                        }

                                                                        if (p.GenericValue is FScriptStruct structInArray &&
                                                                            structInArray.StructType is FRotator rotator)
                                                                        {
                                                                            return
                                                                                $"FRotator({rotator.Pitch}, {rotator.Yaw}, {rotator.Roll})";
                                                                        }
                                                                        else if
                                                                            (p.GenericValue is FScriptStruct
                                                                                 fallbacksInArray &&
                                                                             fallbacksInArray.StructType is FStructFallback
                                                                                 fallback)
                                                                        {
                                                                            string formattedTags;
                                                                            if (fallback.Properties.Count > 0)
                                                                            {
                                                                                formattedTags = "\t[\n" + string.Join(",\n",
                                                                                    fallback.Properties.Select(tag =>
                                                                                    {
                                                                                        string tagDataFormatted;
                                                                                        if (tag.Tag is TextProperty text)
                                                                                        {
                                                                                            tagDataFormatted =
                                                                                                $"\"{text.Value.Text}\"";
                                                                                        }
                                                                                        else if (tag.Tag is NameProperty
                                                                                         name)
                                                                                        {
                                                                                            tagDataFormatted =
                                                                                                $"\"{name.Value.Text}\"";
                                                                                        }
                                                                                        else if (tag.Tag is ObjectProperty
                                                                                         objectproperty)
                                                                                        {
                                                                                            tagDataFormatted =
                                                                                                $"\"{objectproperty.Value}\"";
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            tagDataFormatted =
                                                                                                $"\"{tag.Tag.GenericValue}\"";
                                                                                        }

                                                                                        return
                                                                                            $"\t\t\"{tag.Name}\": {tagDataFormatted}";
                                                                                    })) + "\n\t]";
                                                                            }
                                                                            else
                                                                            {
                                                                                formattedTags = "{}";
                                                                            }

                                                                            return formattedTags;
                                                                        }
                                                                        else if
                                                                            (p.GenericValue is FScriptStruct
                                                                                 gameplayTagsInArray &&
                                                                             gameplayTagsInArray.StructType is
                                                                                 FGameplayTagContainer gameplayTag)
                                                                        {
                                                                            var tags = gameplayTag.GameplayTags.ToList();
                                                                            if (tags.Count > 1)
                                                                            {
                                                                                var formattedTags =
                                                                                    "[\n" + string.Join(",\n",
                                                                                        tags.Select(tag =>
                                                                                            $"\t\t\"{tag.TagName}\"")) +
                                                                                    "\n\t]";
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
                                                            else if (key.Tag.GenericValue is FMulticastScriptDelegate multicast)
                                                            {
                                                                var list = multicast.InvocationList;
                                                                ShouldAppend(list.Length == 0 ? "[]" : $"[{string.Join(", ", list.Select(x => $"\"{x.FunctionName}\""))}]");
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

                                            var childProperties = blueprintGeneratedClass?.ChildProperties ??
                                                                  verseClass?.ChildProperties;
                                            if (childProperties != null)
                                            {
                                                foreach (FProperty property in childProperties)
                                                {
                                                    if (!stringsarray.Contains(property.Name.PlainText))
                                                        outputBuilder.AppendLine(
                                                            $"\t{Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || property.PropertyFlags.HasFlag(EPropertyFlags.ReferenceParm) || Utils.GetPropertyProperty(property) ? "*" : string.Empty)} {property.Name.PlainText.Replace(" ", "")} = {property.Name.PlainText.Replace(" ", "")}placenolder;");
                                                }
                                            }

                                            var funcMapOrder =
                                                    blueprintGeneratedClass?.FuncMap?.Keys.Select(fname => fname.ToString())
                                                        .ToList() ?? verseClass?.FuncMap.Keys.Select(fname => fname.ToString())
                                                        .ToList();

                                            var functions = pkg.ExportsLazy
                                                .Where(e => e.Value is UFunction)
                                                .Select(e => (UFunction) e.Value)
                                                .OrderBy(f =>
                                                {
                                                    if (funcMapOrder != null)
                                                    {
                                                        var functionName = f.Name.ToString();
                                                        int indexx = funcMapOrder.IndexOf(functionName);
                                                        return indexx >= 0 ? indexx : int.MaxValue;
                                                    }

                                                    return int.MaxValue;
                                                })
                                                .ThenBy(f => f.Name.ToString())
                                                .ToList();

                                            var jumpCodeOffsets = new List<int>();

                                            foreach (var function in functions.AsEnumerable().Reverse())
                                            {
                                                if (function?.ScriptBytecode == null)
                                                    continue;

                                                foreach (var property in function.ScriptBytecode)
                                                {
                                                    switch (property.Token)
                                                    {
                                                        case EExprToken.EX_JumpIfNot:
                                                            jumpCodeOffsets.Add((int) ((EX_JumpIfNot) property).CodeOffset);
                                                            break;

                                                        case EExprToken.EX_Jump:
                                                            jumpCodeOffsets.Add((int) ((EX_Jump) property).CodeOffset);
                                                            break;

                                                        case EExprToken.EX_LocalFinalFunction:
                                                            {
                                                                EX_FinalFunction op = (EX_FinalFunction) property;
                                                                var opp = op.Parameters;
                                                                if (opp.Length == 1 && opp[0] is EX_IntConst intConst)
                                                                    jumpCodeOffsets.Add(intConst.Value);
                                                                break;
                                                            }
                                                    }
                                                }
                                            }



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
                                                            returnFunc =
                                                                $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || Utils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}";
                                                        }
                                                        else if (!(property.Name.ToString().EndsWith("_ReturnValue") ||
                                                                   property.Name.ToString().StartsWith("CallFunc_") ||
                                                                   property.Name.ToString().StartsWith("K2Node_") ||
                                                                   property.Name.ToString()
                                                                       .StartsWith("Temp_")) || // removes useless args
                                                                 property.PropertyFlags.HasFlag(EPropertyFlags.Edit))
                                                        {
                                                            argsList +=
                                                                $"{(property.PropertyFlags.HasFlag(EPropertyFlags.ConstParm) ? "const " : string.Empty)}{Utils.GetPrefix(property.GetType().Name)}{Utils.GetPropertyType(property)}{(property.PropertyFlags.HasFlag(EPropertyFlags.InstancedReference) || Utils.GetPrefix(property.GetType().Name) == "U" ? "*" : string.Empty)}{(property.PropertyFlags.HasFlag(EPropertyFlags.OutParm) ? "&" : string.Empty)} {Regex.Replace(property.Name.ToString(), @"^__verse_0x[0-9A-Fa-f]+_", "")}, ";
                                                        }
                                                    }
                                                }

                                                argsList = argsList.TrimEnd(',', ' ');

                                                outputBuilder.AppendLine(
                                                    $"\n\t{returnFunc} {function.Name.Replace(" ", "")}({argsList})\n\t{{");
                                                if (function?.ScriptBytecode != null)
                                                {
                                                    foreach (KismetExpression property in function.ScriptBytecode)
                                                    {
                                                        ProcessExpression(property.Token, property, outputBuilder, jumpCodeOffsets);
                                                    }
                                                }
                                                else
                                                {
                                                    outputBuilder.Append(
                                                        "\n\t // This function does not have Bytecode \n\n");
                                                    outputBuilder.Append("\t}\n");
                                                }
                                                jumpCodeOffsets.Clear();
                                            }

                                            outputBuilder.Append("\n\n}");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"No Blueprint Found nor Verse set in \"{package.Path}\"");
                                            continue;
                                        }

                                        string pattern = $@"\w+placenolder";
                                        string updatedOutput = Regex.Replace(outputBuilder.ToString(), pattern, "nullptr");
                                        string blueprintDirRel = Path.GetDirectoryName(package.Path ?? string.Empty);
                                        string blueprintDirOutput = Path.Combine(exeDirectory, blueprintDirRel ?? string.Empty);
                                        Directory.CreateDirectory(blueprintDirOutput);
                                        string outputFilePath = Path.Combine(blueprintDirOutput, $"{package.Name.Replace(".uasset", "")}.cpp");
                                        if (!File.Exists(outputFilePath) || File.ReadAllText(outputFilePath) != updatedOutput)
                                        {
                                            File.WriteAllText(outputFilePath, updatedOutput);
                                        } else
                                        {
                                            Console.WriteLine($"unchanged {outputFilePath}");
                                        }


                                        Console.WriteLine($"Output written to: {outputFilePath}");
                                        break;
                                    }
                            }
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
    }
    private static void ProcessExpression(EExprToken token, KismetExpression expression, StringBuilder outputBuilder, List<int> jumpCodeOffsets, bool isParameter = false)
    {
        if (jumpCodeOffsets.Contains(expression.StatementIndex))
        {
            int length = outputBuilder.Length;
            if (length == 0 || char.IsWhiteSpace(outputBuilder[length - 1]) && char.IsWhiteSpace(outputBuilder[length - 2]))
            {
                outputBuilder.Append("\t\tLabel_" + expression.StatementIndex + ":\n");
            }
        }
        switch (token)
        {
            case EExprToken.EX_LetValueOnPersistentFrame:
                {
                    EX_LetValueOnPersistentFrame op = (EX_LetValueOnPersistentFrame) expression;
                    EX_VariableBase opp = (EX_VariableBase) op.AssignmentExpression;
                    var destination = ProcessTextProperty(op.DestinationProperty);
                    var variable = ProcessTextProperty(opp.Variable);

                    if (!isParameter)
                    {
                        outputBuilder.Append($"\t\t{(destination.Contains("K2Node_") ? $"UberGraphFrame->{destination}" : destination)} = {variable};\n\n"); // hardcoded but works
                    }
                    else
                    {
                        outputBuilder.Append($"\t\t{(destination.Contains("K2Node_") ? $"UberGraphFrame->{destination}" : destination)} = {variable}");
                    }
                    break;
                }
            case EExprToken.EX_LocalFinalFunction:
                {
                    EX_FinalFunction op = (EX_FinalFunction) expression;
                    KismetExpression[] opp = op.Parameters;
                    //Console.WriteLine(op.StackNode.Index);
                    //Console.WriteLine(op.StackNode.Name);
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
                        outputBuilder.Append($"\t\t{Utils.GetPrefix(op?.StackNode?.ResolvedObject?.Outer?.GetType()?.Name ?? string.Empty)}{op?.StackNode?.Name.Replace(" ", "")}(");
                    }

                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4)
                            outputBuilder.Append("\n\t\t");
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, jumpCodeOffsets, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    outputBuilder.Append(isParameter ? ")" : ");\n");
                    break;
                }
            case EExprToken.EX_FinalFunction:
                {
                    EX_FinalFunction op = (EX_FinalFunction) expression;
                    KismetExpression[] opp = op.Parameters;

                    // ignore this but I use it to Modify bytecode
                    //Console.WriteLine(op.StackNode.Index);
                    //Console.WriteLine(op.StackNode.Name);
                    //if (op.StackNode.Name == "CanRespawnOnStarterIsland")
                    //{
                    //Console.WriteLine("a");
                    //}
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
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, jumpCodeOffsets, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    outputBuilder.Append(isParameter ? ")" : ");\n\n");
                    break;
                }
            case EExprToken.EX_CallMath:
                {
                    EX_FinalFunction op = (EX_FinalFunction) expression;
                    KismetExpression[] opp = op.Parameters;
                    outputBuilder.Append(isParameter ? string.Empty : "\t\t");
                    outputBuilder.Append($"{Utils.GetPrefix(op.StackNode.ResolvedObject.Outer.GetType().Name)}{op.StackNode.ResolvedObject.Outer.Name.ToString().Replace(" ", "")}::{op.StackNode.Name}(");

                    for (int i = 0; i < opp.Length; i++)
                    {
                        if (opp.Length > 4)
                            outputBuilder.Append("\n\t\t\t");
                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, jumpCodeOffsets, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    outputBuilder.Append(isParameter ? ")" : ");\n\n");
                    break;
                }
            case EExprToken.EX_LocalVirtualFunction:
            case EExprToken.EX_VirtualFunction:
                {
                    EX_VirtualFunction op = (EX_VirtualFunction) expression;
                    KismetExpression[] opp = op.Parameters;

                    //Console.WriteLine(op.VirtualFunctionName.Index);
                    //Console.WriteLine(op.VirtualFunctionName.PlainText);
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
                        if (opp.Length > 4)
                            outputBuilder.Append("\n\t\t");

                        ProcessExpression(opp[i].Token, opp[i], outputBuilder, jumpCodeOffsets, true);
                        if (i < opp.Length - 1)
                        {
                            outputBuilder.Append(", ");
                        }
                    }
                    outputBuilder.Append(isParameter ? ")" : ");\n\n");
                    break;
                }
            case EExprToken.EX_ComputedJump:
                {
                    EX_ComputedJump op = (EX_ComputedJump) expression;
                    if (op.CodeOffsetExpression is EX_VariableBase opp)
                    {
                        outputBuilder.AppendLine($"\t\tgoto {ProcessTextProperty(opp.Variable)};\n");
                    }
                    else if (op.CodeOffsetExpression is EX_CallMath oppMath)
                    {
                        ProcessExpression(oppMath.Token, oppMath, outputBuilder, jumpCodeOffsets, true);
                    }
                    else
                    {
                        Console.WriteLine("no idea how you reached this");
                    }
                    break;
                }
            case EExprToken.EX_PopExecutionFlowIfNot:
                {
                    EX_PopExecutionFlowIfNot op = (EX_PopExecutionFlowIfNot) expression;
                    outputBuilder.Append("\t\tif (!");
                    ProcessExpression(op.BooleanExpression.Token, op.BooleanExpression, outputBuilder, jumpCodeOffsets, true);
                    outputBuilder.Append(") \r\n");
                    outputBuilder.Append($"\t\t    FlowStack.Pop();\n\n");
                    break;
                }
            case EExprToken.EX_Cast:
                {
                    EX_Cast op = (EX_Cast) expression;// support CST_ObjectToInterface when I have an example of how it works

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
                    ProcessExpression(op.Target.Token, op.Target, outputBuilder, jumpCodeOffsets);
                    break;
                }
            case EExprToken.EX_InterfaceContext:
                {
                    EX_InterfaceContext op = (EX_InterfaceContext) expression;
                    ProcessExpression(op.InterfaceValue.Token, op.InterfaceValue, outputBuilder, jumpCodeOffsets);
                    break;
                }
            case EExprToken.EX_ArrayConst:
                {
                    EX_ArrayConst op = (EX_ArrayConst) expression;
                    outputBuilder.Append("TArray {");
                    foreach (KismetExpression element in op.Elements)
                    {
                        outputBuilder.Append(' ');
                        ProcessExpression(element.Token, element, outputBuilder, jumpCodeOffsets);
                    }
                    outputBuilder.Append(op.Elements.Length < 1 ? "  " : ' ');

                    outputBuilder.Append("}");
                    break;
                }
            case EExprToken.EX_SetArray:
                {
                    EX_SetArray op = (EX_SetArray) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.AssigningProperty.Token, op.AssigningProperty, outputBuilder, jumpCodeOffsets);
                    outputBuilder.Append(" = ");
                    outputBuilder.Append("TArray {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        KismetExpression element = op.Elements[i];
                        outputBuilder.Append(' ');
                        ProcessExpression(element.Token, element, outputBuilder, jumpCodeOffsets);

                        outputBuilder.Append(i < op.Elements.Length - 1 ? "," : ' ');
                    }

                    outputBuilder.Append(op.Elements.Length < 1 ? "  " : ' ');

                    outputBuilder.Append("};\n\n");
                    break;
                }
            case EExprToken.EX_SetSet:
                {
                    EX_SetSet op = (EX_SetSet) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.SetProperty.Token, op.SetProperty, outputBuilder, jumpCodeOffsets);
                    outputBuilder.Append(" = ");
                    outputBuilder.Append("TArray {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        KismetExpression element = op.Elements[i];
                        outputBuilder.Append(' ');
                        ProcessExpression(element.Token, element, outputBuilder, jumpCodeOffsets);

                        outputBuilder.Append(i < op.Elements.Length - 1 ? "," : ' ');
                    }

                    outputBuilder.Append(op.Elements.Length < 1 ? "  " : ' ');

                    outputBuilder.Append("};\n\n");
                    break;
                }
            case EExprToken.EX_SetConst:
                {
                    EX_SetConst op = (EX_SetConst) expression;
                    outputBuilder.Append("TArray {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        KismetExpression element = op.Elements[i];
                        outputBuilder.Append(' ');
                        ProcessExpression(element.Token, element, outputBuilder, jumpCodeOffsets, true);

                        outputBuilder.Append(i < op.Elements.Length - 1 ? "," : ' ');
                    }

                    outputBuilder.Append(op.Elements.Length < 1 ? "  " : ' ');

                    outputBuilder.Append("};\n\n");
                    break;
                }
            case EExprToken.EX_SetMap:
                {
                    EX_SetMap op = (EX_SetMap) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.MapProperty.Token, op.MapProperty, outputBuilder, jumpCodeOffsets);
                    outputBuilder.Append(" = ");
                    outputBuilder.Append("TMap {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        var element = op.Elements[i];
                        outputBuilder.Append(' ');
                        ProcessExpression(element.Token, element, outputBuilder, jumpCodeOffsets);// sometimes the start of an array is a byte not a variable

                        if (i < op.Elements.Length - 1)
                        {
                            outputBuilder.Append(element.Token == EExprToken.EX_InstanceVariable ? ": " : ", ");
                        }
                        else
                        {
                            outputBuilder.Append(' ');
                        }
                    }

                    if (op.Elements.Length < 1)
                        outputBuilder.Append("  ");
                    outputBuilder.Append("}\n");
                    break;
                }
            case EExprToken.EX_MapConst:
                {
                    EX_MapConst op = (EX_MapConst) expression;
                    outputBuilder.Append("TMap {");
                    for (int i = 0; i < op.Elements.Length; i++)
                    {
                        var element = op.Elements[i];
                        outputBuilder.Append(' ');
                        ProcessExpression(element.Token, element, outputBuilder, jumpCodeOffsets, true);// sometimes the start of an array is a byte not a variable

                        if (i < op.Elements.Length - 1)
                        {
                            outputBuilder.Append(element.Token == EExprToken.EX_InstanceVariable ? ": " : ", ");
                        }
                        else
                        {
                            outputBuilder.Append(' ');
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
                        ProcessExpression(op.IndexTerm.Token, op.IndexTerm, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append(" ? ");

                        bool isFirst = true;
                        foreach (var caseItem in op.Cases.Where(c => c.CaseIndexValueTerm.Token == EExprToken.EX_True))
                        {
                            if (!isFirst)
                                outputBuilder.Append(" : ");

                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder, jumpCodeOffsets, true);
                            isFirst = false;
                        }

                        foreach (var caseItem in op.Cases.Where(c => c.CaseIndexValueTerm.Token == EExprToken.EX_False))
                        {
                            if (!isFirst)
                                outputBuilder.Append(" : ");

                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder, jumpCodeOffsets, true);
                        }
                    }
                    else
                    {
                        outputBuilder.Append("switch (");
                        ProcessExpression(op.IndexTerm.Token, op.IndexTerm, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append(")\n");
                        outputBuilder.Append("\t\t{\n");

                        foreach (var caseItem in op.Cases)
                        {
                            if (caseItem.CaseIndexValueTerm.Token == EExprToken.EX_IntConst)
                            {
                                int caseValue = ((EX_IntConst) caseItem.CaseIndexValueTerm).Value;
                                outputBuilder.Append($"\t\t\tcase {caseValue}:\n");
                            }
                            else
                            {
                                outputBuilder.Append("\t\t\tcase ");
                                ProcessExpression(caseItem.CaseIndexValueTerm.Token, caseItem.CaseIndexValueTerm, outputBuilder, jumpCodeOffsets);
                                outputBuilder.Append(":\n");
                            }

                            outputBuilder.Append("\t\t\t{\n");
                            outputBuilder.Append("\t\t\t    ");
                            ProcessExpression(caseItem.CaseTerm.Token, caseItem.CaseTerm, outputBuilder, jumpCodeOffsets);
                            outputBuilder.Append(";\n");
                            outputBuilder.Append("\t\t\t    break;\n");
                            outputBuilder.Append("\t\t\t}\n");
                        }

                        outputBuilder.Append("\t\t\tdefault:\n");
                        outputBuilder.Append("\t\t\t{\n");
                        outputBuilder.Append("\t\t\t    ");
                        ProcessExpression(op.DefaultTerm.Token, op.DefaultTerm, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append("\n\t\t\t}\n");

                        outputBuilder.Append("\t\t}");
                    }
                    break;
                }
            case EExprToken.EX_ArrayGetByRef: // I assume get array with index
                {
                    EX_ArrayGetByRef op = (EX_ArrayGetByRef) expression; // FortniteGame/Plugins/GameFeatures/FM/PilgrimCore/Content/Player/Components/BP_PilgrimPlayerControllerComponent.uasset
                    ProcessExpression(op.ArrayVariable.Token, op.ArrayVariable, outputBuilder, jumpCodeOffsets, true);
                    outputBuilder.Append("[");
                    ProcessExpression(op.ArrayIndex.Token, op.ArrayIndex, outputBuilder, jumpCodeOffsets);
                    outputBuilder.Append("]");
                    break;
                }
            case EExprToken.EX_MetaCast:
            case EExprToken.EX_DynamicCast:
            case EExprToken.EX_ObjToInterfaceCast:
            case EExprToken.EX_CrossInterfaceCast:
            case EExprToken.EX_InterfaceToObjCast:
                {
                    EX_CastBase op = (EX_CastBase) expression;
                    outputBuilder.Append($"Cast<U{op.ClassPtr.Name}*>(");// m?
                    ProcessExpression(op.Target.Token, op.Target, outputBuilder, jumpCodeOffsets, true);
                    outputBuilder.Append(")");
                    break;
                }
            case EExprToken.EX_StructConst:
                {
                    EX_StructConst op = (EX_StructConst) expression;
                    outputBuilder.Append($"{Utils.GetPrefix(op.Struct.GetType().Name)}{op.Struct.Name}");
                    outputBuilder.Append($"(");
                    for (int i = 0; i < op.Properties.Length; i++)
                    {
                        var property = op.Properties[i];
                        ProcessExpression(property.Token, property, outputBuilder, jumpCodeOffsets);
                        if (i < op.Properties.Length - 1 && property.Token != EExprToken.EX_ArrayConst)
                            outputBuilder.Append(", ");
                    }
                    outputBuilder.Append($")");
                    break;
                }
            case EExprToken.EX_ObjectConst:
                {
                    EX_ObjectConst op = (EX_ObjectConst) expression;
                    outputBuilder.Append(!isParameter ? "\t\tFindObject<" : outputBuilder.ToString().EndsWith("\n") ? "\t\tFindObject<" : "FindObject<"); // please don't complain, i know this is bad but i MUST do it.
                    string classString = op?.Value?.ResolvedObject?.Class?.ToString()?.Replace("'", "");

                    if (classString?.Contains(".") == true)
                    {

                        outputBuilder.Append(Utils.GetPrefix(op?.Value?.ResolvedObject?.Class?.GetType().Name) + classString.Split(".")[1]);
                    }
                    else
                    {
                        outputBuilder.Append(Utils.GetPrefix(op?.Value?.ResolvedObject?.Class?.GetType().Name) + classString);
                    }
                    outputBuilder.Append(">(\"");
                    var resolvedObject = op?.Value?.ResolvedObject;
                    var outerString = resolvedObject?.Outer?.ToString()?.Replace("'", "") ?? "UNKNOWN";
                    var outerClassString = resolvedObject?.Class?.ToString()?.Replace("'", "") ?? "UNKNOWN";
                    var name = op?.Value?.Name ?? string.Empty;

                    outputBuilder.Append(outerString.Replace(outerClassString, "") + "." + name);

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
                    EX_BindDelegate op = (EX_BindDelegate) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, jumpCodeOffsets);
                    outputBuilder.Append($".BindUFunction(");
                    ProcessExpression(op.ObjectTerm.Token, op.ObjectTerm, outputBuilder, jumpCodeOffsets);
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
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, jumpCodeOffsets, true);
                        outputBuilder.Append(".AddDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append($");\n\n");
                    }
                    else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine($"Issue: EX_AddMulticastDelegate missing info: {op.StatementIndex}, {op.Delegate.Token}");
                    }
                    else
                    {
                        //EX_Context opp = (EX_Context) op.Delegate;
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, jumpCodeOffsets, true);
                        //outputBuilder.Append("->");
                        //ProcessExpression(opp.ContextExpression.Token, opp.ContextExpression, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append(".AddDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_RemoveMulticastDelegate: // everything here has been guessed not compared to actual UE but does work fine and displays all information
                {
                    EX_RemoveMulticastDelegate op = (EX_RemoveMulticastDelegate) expression;
                    if (op.Delegate.Token == EExprToken.EX_LocalVariable || op.Delegate.Token == EExprToken.EX_InstanceVariable)
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, jumpCodeOffsets, true);
                        outputBuilder.Append(".RemoveDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append($");\n\n");
                    }
                    else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine("Issue: EX_RemoveMulticastDelegate missing info: {0}", op.StatementIndex);
                    }
                    else
                    {
                        EX_Context opp = (EX_Context) op.Delegate;
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, jumpCodeOffsets, true);
                        outputBuilder.Append("->");
                        ProcessExpression(opp.ContextExpression.Token, opp.ContextExpression, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append(".RemoveDelegate(");
                        ProcessExpression(op.DelegateToAdd.Token, op.DelegateToAdd, outputBuilder, jumpCodeOffsets);
                        outputBuilder.Append($");\n\n");
                    }
                    break;
                }
            case EExprToken.EX_ClearMulticastDelegate: // this also
                {
                    EX_ClearMulticastDelegate op = (EX_ClearMulticastDelegate) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.DelegateToClear.Token, op.DelegateToClear, outputBuilder, jumpCodeOffsets, true);
                    outputBuilder.Append(".Clear();\n\n");
                    break;
                }
            case EExprToken.EX_CallMulticastDelegate: // this also
                {
                    EX_CallMulticastDelegate op = (EX_CallMulticastDelegate) expression;
                    KismetExpression[] opp = op.Parameters;
                    if (op.Delegate.Token == EExprToken.EX_LocalVariable || op.Delegate.Token == EExprToken.EX_InstanceVariable)
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, jumpCodeOffsets, true);
                        outputBuilder.Append(".Call(");
                        for (int i = 0; i < opp.Length; i++)
                        {
                            if (opp.Length > 4)
                                outputBuilder.Append("\n\t\t");
                            ProcessExpression(opp[i].Token, opp[i], outputBuilder, jumpCodeOffsets, true);
                            if (i < opp.Length - 1)
                            {
                                outputBuilder.Append(", ");
                            }
                        }
                        outputBuilder.Append($");\n\n");
                    }
                    else if (op.Delegate.Token != EExprToken.EX_Context)
                    {
                        Console.WriteLine("Issue: EX_CallMulticastDelegate missing info: {0}", op.StatementIndex);
                    }
                    else
                    {
                        outputBuilder.Append("\t\t");
                        ProcessExpression(op.Delegate.Token, op.Delegate, outputBuilder, jumpCodeOffsets, true);
                        outputBuilder.Append(".Call(");
                        for (int i = 0; i < opp.Length; i++)
                        {
                            if (opp.Length > 4)
                                outputBuilder.Append("\n\t\t");
                            ProcessExpression(opp[i].Token, opp[i], outputBuilder, jumpCodeOffsets, true);
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
                    EX_Context op = (EX_Context) expression;
                    outputBuilder.Append(outputBuilder.ToString().EndsWith("\n") ? "\t\t" : "");
                    ProcessExpression(op.ObjectExpression.Token, op.ObjectExpression, outputBuilder, jumpCodeOffsets, true);

                    outputBuilder.Append("->");
                    ProcessExpression(op.ContextExpression.Token, op.ContextExpression, outputBuilder, jumpCodeOffsets, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append(";\n\n");
                    }
                    break;
                }
            case EExprToken.EX_Context_FailSilent:
                {
                    EX_Context op = (EX_Context) expression;
                    outputBuilder.Append("\t\t");
                    ProcessExpression(op.ObjectExpression.Token, op.ObjectExpression, outputBuilder, jumpCodeOffsets, true);
                    if (!isParameter)
                    {
                        outputBuilder.Append("->");
                        ProcessExpression(op.ContextExpression.Token, op.ContextExpression, outputBuilder, jumpCodeOffsets, true);
                        outputBuilder.Append($";\n\n");
                    }
                    break;
                }
            case EExprToken.EX_Let:
                {
                    EX_Let op = (EX_Let) expression;
                    if (!isParameter)
                    {
                        outputBuilder.Append("\t\t");
                    }
                    ProcessExpression(op.Variable.Token, op.Variable, outputBuilder, jumpCodeOffsets, true);
                    outputBuilder.Append(" = ");
                    ProcessExpression(op.Assignment.Token, op.Assignment, outputBuilder, jumpCodeOffsets, true);
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
                    EX_LetBase op = (EX_LetBase) expression;
                    if (!isParameter)
                    {
                        outputBuilder.Append("\t\t");
                    }
                    ProcessExpression(op.Variable.Token, op.Variable, outputBuilder, jumpCodeOffsets, true);
                    outputBuilder.Append(" = ");
                    ProcessExpression(op.Assignment.Token, op.Assignment, outputBuilder, jumpCodeOffsets, true);
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
                    EX_JumpIfNot op = (EX_JumpIfNot) expression;
                    outputBuilder.Append("\t\tif (!");
                    ProcessExpression(op.BooleanExpression.Token, op.BooleanExpression, outputBuilder, jumpCodeOffsets, true);
                    outputBuilder.Append(") \r\n");
                    outputBuilder.Append("\t\t    goto Label_");
                    outputBuilder.Append(op.CodeOffset);
                    outputBuilder.Append(";\n\n");
                    break;
                }
            case EExprToken.EX_Jump:
                {
                    EX_Jump op = (EX_Jump) expression;
                    outputBuilder.Append($"\t\tgoto Label_{op.CodeOffset};\n\n");
                    break;
                }
            // Static expressions

            case EExprToken.EX_TextConst:
                {
                    EX_TextConst op = (EX_TextConst) expression;

                    if (op.Value is FScriptText scriptText)
                    {
                        if (scriptText.SourceString == null)
                        {
                            outputBuilder.Append("nullptr");
                        }
                        else
                            ProcessExpression(scriptText.SourceString.Token, scriptText.SourceString, outputBuilder, jumpCodeOffsets, true);
                    }
                    else
                    {
                        outputBuilder.Append(op.Value);
                    }
                }
                break;
            case EExprToken.EX_StructMemberContext:
                {
                    EX_StructMemberContext op = (EX_StructMemberContext) expression;
                    ProcessExpression(op.StructExpression.Token, op.StructExpression, outputBuilder, jumpCodeOffsets);
                    outputBuilder.Append('.');
                    outputBuilder.Append(ProcessTextProperty(op.Property));
                    break;
                }
            case EExprToken.EX_Return:
                {
                    EX_Return op = (EX_Return) expression;
                    bool check = op.ReturnExpression.Token == EExprToken.EX_Nothing;
                    outputBuilder.Append($"\t\treturn");
                    if (!check)
                        outputBuilder.Append(' ');
                    ProcessExpression(op.ReturnExpression.Token, op.ReturnExpression, outputBuilder, jumpCodeOffsets, true);
                    outputBuilder.AppendLine(";\n\n");
                    break;
                }
            case EExprToken.EX_RotationConst:
                {
                    EX_RotationConst op = (EX_RotationConst) expression;
                    FRotator value = op.Value;
                    outputBuilder.Append($"FRotator({value.Pitch}, {value.Yaw}, {value.Roll})");
                    break;
                }
            case EExprToken.EX_VectorConst:
                {
                    EX_VectorConst op = (EX_VectorConst) expression;
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

            case EExprToken.EX_ByteConst:
            case EExprToken.EX_IntConstByte:
                outputBuilder.Append($"0x{((KismetExpression<byte>) expression).Value.ToString("X")}");
                break;
            case EExprToken.EX_SoftObjectConst:
                ProcessExpression(((EX_SoftObjectConst) expression).Value.Token, ((EX_SoftObjectConst) expression).Value, outputBuilder, jumpCodeOffsets);
                break;
            case EExprToken.EX_DoubleConst:
                {
                    double value = ((EX_DoubleConst) expression).Value;
                    outputBuilder.Append(Math.Abs(value - Math.Floor(value)) < 1e-10 ? (int) value : value.ToString("R"));
                    break;
                }
            case EExprToken.EX_NameConst:
                outputBuilder.Append($"\"{((EX_NameConst) expression).Value}\"");
                break;
            case EExprToken.EX_IntConst:
                outputBuilder.Append(((EX_IntConst) expression).Value.ToString());
                break;
            case EExprToken.EX_PropertyConst:
                outputBuilder.Append(ProcessTextProperty(((EX_PropertyConst) expression).Property));
                break;
            case EExprToken.EX_StringConst:
                outputBuilder.Append($"\"{((EX_StringConst) expression).Value}\"");
                break;
            case EExprToken.EX_FieldPathConst:
                ProcessExpression(((EX_FieldPathConst) expression).Value.Token, ((EX_FieldPathConst) expression).Value, outputBuilder, jumpCodeOffsets);
                break;
            case EExprToken.EX_Int64Const:
                outputBuilder.Append(((EX_Int64Const) expression).Value.ToString());
                break;
            case EExprToken.EX_UInt64Const:
                outputBuilder.Append(((EX_UInt64Const) expression).Value.ToString());
                break;
            case EExprToken.EX_SkipOffsetConst:
                outputBuilder.Append(((EX_SkipOffsetConst) expression).Value.ToString());
                break;
            case EExprToken.EX_FloatConst:
                outputBuilder.Append(((EX_FloatConst) expression).Value.ToString(CultureInfo.GetCultureInfo("en-US")));
                break;
            case EExprToken.EX_BitFieldConst:
                outputBuilder.Append(((EX_BitFieldConst) expression).ConstValue);
                break;
            case EExprToken.EX_UnicodeStringConst:
                outputBuilder.Append(((EX_UnicodeStringConst) expression).Value);
                break;
            case EExprToken.EX_InstanceDelegate:
                outputBuilder.Append($"\"{((EX_InstanceDelegate) expression).FunctionName}\"");
                break;
            case EExprToken.EX_EndOfScript:
            case EExprToken.EX_EndParmValue:
                outputBuilder.Append("\t}\n");
                break;
            case EExprToken.EX_NoObject:
            case EExprToken.EX_NoInterface:
                outputBuilder.Append("nullptr");
                break;
            case EExprToken.EX_IntOne:
                outputBuilder.Append(1);
                break;
            case EExprToken.EX_IntZero:
                outputBuilder.Append(0);
                break;
            case EExprToken.EX_True:
                outputBuilder.Append("true");
                break;
            case EExprToken.EX_False:
                outputBuilder.Append("false");
                break;
            case EExprToken.EX_Self:
                outputBuilder.Append("this");
                break;

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

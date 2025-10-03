using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Sound;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Wwise;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.Utils;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Sounds;
using CUE4Parse_Conversion.Textures;

namespace FModelHeadless.Cli;

internal static class ExportCommandFactory
{
    public static Command Create(Option<DirectoryInfo?> pakDirOption,
                                 Option<FileInfo?> mappingOption,
                                 Option<string?> aesOption,
                                 Option<string> gameOption,
                                 Option<bool> verboseOption)
    {
        var export = new Command("export", "Export assets without rendering (cross-platform)");

        export.AddCommand(CreateModelsSubcommand(pakDirOption, mappingOption, aesOption, gameOption, verboseOption));
        export.AddCommand(CreateSoundsSubcommand(pakDirOption, mappingOption, aesOption, gameOption, verboseOption));

        return export;
    }

    private static Command CreateModelsSubcommand(Option<DirectoryInfo?> pakDirOption,
                                                  Option<FileInfo?> mappingOption,
                                                  Option<string?> aesOption,
                                                  Option<string> gameOption,
                                                  Option<bool> verboseOption)
    {
        var cmd = new Command("models", "Export static/skeletal meshes to glTF/ActorX/UEFormat");

        var meshFormatOpt = new Option<string>("--mesh-format", () => "gltf2",
            "Mesh format: gltf2 | actorx | ueformat | obj (obj limited)");
        var lodOpt = new Option<string>("--lod", () => "first", "LOD selection: first | all");
        var socketsOpt = new Option<string>("--sockets", () => "bone", "Include sockets: bone | socket | none");
        var morphsOpt = new Option<bool>("--morphs", () => true, "Export morph targets (skeletal)");
        var materialsOpt = new Option<bool>("--materials", () => false, "Export materials/textures (requires SkiaSharp in extern)");
        var includeSkeletonsOpt = new Option<bool>("--include-skeletons", () => false, "Also export bare USkeleton assets (not supported for glTF)");
        var outDirOpt = new Option<DirectoryInfo>("--out-dir", () => new DirectoryInfo(Path.Combine("output", "exports", "models")), "Output directory");
        var filterOpt = new Option<string[]>("--filter", description: "Substring filter(s) applied to package paths") { Arity = ArgumentArity.ZeroOrMore };
        var dopOpt = new Option<int>("--max-degree-of-parallelism", () => Environment.ProcessorCount, "Parallelism for export");

        cmd.AddOption(meshFormatOpt);
        cmd.AddOption(lodOpt);
        cmd.AddOption(socketsOpt);
        cmd.AddOption(morphsOpt);
        cmd.AddOption(materialsOpt);
        cmd.AddOption(includeSkeletonsOpt);
        cmd.AddOption(outDirOpt);
        cmd.AddOption(filterOpt);
        cmd.AddOption(dopOpt);

        cmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            var pakDir = parse.GetValueForOption(pakDirOption);
            var mapping = parse.GetValueForOption(mappingOption);
            var aes = parse.GetValueForOption(aesOption);
            var gameTag = parse.GetValueForOption(gameOption)!;
            var verbose = parse.GetValueForOption(verboseOption);
            var meshFmt = parse.GetValueForOption(meshFormatOpt)!;
            var lod = parse.GetValueForOption(lodOpt)!;
            var sockets = parse.GetValueForOption(socketsOpt)!;
            var morphs = parse.GetValueForOption(morphsOpt);
            var materials = parse.GetValueForOption(materialsOpt);
            var includeSkeletons = parse.GetValueForOption(includeSkeletonsOpt);
            var outDir = parse.GetValueForOption(outDirOpt)!;
            var filters = parse.GetValueForOption(filterOpt) ?? Array.Empty<string>();
            var dop = parse.GetValueForOption(dopOpt);

            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aes, gameTag, verbose);
                Directory.CreateDirectory(outDir.FullName);

                var options = BuildExporterOptionsForModels(meshFmt, lod, sockets, morphs, materials);

                var files = provider.Files.Values.AsEnumerable();
                if (filters is { Length: > 0 })
                {
                    files = files.Where(f => filters.Any(fil => f.Path.Contains(fil, StringComparison.OrdinalIgnoreCase)));
                }

                var exported = 0; var skipped = 0;
                var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, dop) };

                await Task.Run(() => Parallel.ForEach(files, po, file =>
                {
                    try
                    {
                        if (!provider.TryLoadPackage(file, out var pkg)) return;
                        var folder = file.Path.SubstringBeforeLast('/');
                        for (var i = 0; i < pkg.ExportMapLength; i++)
                        {
                            var ptr = new FPackageIndex(pkg, i + 1).ResolvedObject;
                            if (ptr?.Object is null) continue;
                            var dummy = ((AbstractUePackage)pkg).ConstructObject(ptr.Class?.Object?.Value as UStruct, pkg);

                            bool shouldExport = dummy switch
                            {
                                UStaticMesh => true,
                                USkeletalMesh => true,
                                USkeleton => includeSkeletons && !meshFmt.Equals("gltf2", StringComparison.OrdinalIgnoreCase),
                                _ => false
                            };
                            if (!shouldExport) { System.Threading.Interlocked.Increment(ref skipped); continue; }

                            var exportObj = ptr.Object.Value;

                            if (exportObj is USkeleton && options.MeshFormat == EMeshFormat.Gltf2)
                            {
                                System.Threading.Interlocked.Increment(ref skipped); continue;
                            }

                            var exporter = new CUE4Parse_Conversion.Exporter(exportObj, options);
                            if (exporter.TryWriteToDir(new DirectoryInfo(Path.Combine(outDir.FullName, folder)), out var label, out _))
                            {
                                System.Threading.Interlocked.Increment(ref exported);
                                if (verbose)
                                    Console.WriteLine($"[export-models] {label}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose)
                            Console.WriteLine($"[export-models] error: {ex.Message}");
                    }
                }));

                Console.WriteLine($"[export-models] done. exported={exported} skipped={skipped}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[export-models] {ex.Message}");
                if (verbose) Console.Error.WriteLine(ex);
                context.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static ExporterOptions BuildExporterOptionsForModels(string meshFmt, string lod, string sockets, bool morphs, bool materials)
    {
        var opts = new ExporterOptions
        {
            LodFormat = ParseLod(lod),
            MeshFormat = ParseMeshFormat(meshFmt),
            NaniteMeshFormat = CUE4Parse.UE4.Assets.Exports.Nanite.ENaniteMeshFormat.OnlyNormalLODs,
            MaterialFormat = CUE4Parse.UE4.Assets.Exports.Material.EMaterialFormat.AllLayersNoRef,
            TextureFormat = ETextureFormat.Png,
            CompressionFormat = CUE4Parse_Conversion.UEFormat.Enums.EFileCompressionFormat.None,
            Platform = CUE4Parse.UE4.Assets.Exports.Texture.ETexturePlatform.DesktopMobile,
            SocketFormat = ParseSocketFormat(sockets),
            ExportMorphTargets = morphs,
            ExportMaterials = materials,
            ExportHdrTexturesAsHdr = false
        };
        return opts;
    }

    private static ELodFormat ParseLod(string value) =>
        value.Equals("all", StringComparison.OrdinalIgnoreCase) ? ELodFormat.AllLods : ELodFormat.FirstLod;

    private static EMeshFormat ParseMeshFormat(string value) => value.ToLowerInvariant() switch
    {
        "actorx" => EMeshFormat.ActorX,
        "ueformat" => EMeshFormat.UEFormat,
        "obj" => EMeshFormat.OBJ,
        _ => EMeshFormat.Gltf2
    };

    private static CUE4Parse_Conversion.Meshes.ESocketFormat ParseSocketFormat(string value) => value.ToLowerInvariant() switch
    {
        "none" => CUE4Parse_Conversion.Meshes.ESocketFormat.None,
        "socket" => CUE4Parse_Conversion.Meshes.ESocketFormat.Socket,
        _ => CUE4Parse_Conversion.Meshes.ESocketFormat.Bone
    };

    private static Command CreateSoundsSubcommand(Option<DirectoryInfo?> pakDirOption,
                                                  Option<FileInfo?> mappingOption,
                                                  Option<string?> aesOption,
                                                  Option<string> gameOption,
                                                  Option<bool> verboseOption)
    {
        var cmd = new Command("sounds", "Export sound assets (USoundWave/WAwise media) as packaged audio");

        var outDirOpt = new Option<DirectoryInfo>("--out-dir", () => new DirectoryInfo(Path.Combine("output", "exports", "sounds")), "Output directory");
        var filterOpt = new Option<string[]>("--filter", description: "Substring filter(s) applied to package paths") { Arity = ArgumentArity.ZeroOrMore };
        var dopOpt = new Option<int>("--max-degree-of-parallelism", () => Environment.ProcessorCount, "Parallelism for export");
        var decompressOpt = new Option<bool>("--decompress", () => false, "Try to decompress (PCM/WAV only). Other codecs kept as-is");

        cmd.AddOption(outDirOpt);
        cmd.AddOption(filterOpt);
        cmd.AddOption(dopOpt);
        cmd.AddOption(decompressOpt);

        cmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            var pakDir = parse.GetValueForOption(pakDirOption);
            var mapping = parse.GetValueForOption(mappingOption);
            var aes = parse.GetValueForOption(aesOption);
            var gameTag = parse.GetValueForOption(gameOption)!;
            var verbose = parse.GetValueForOption(verboseOption);
            var outDir = parse.GetValueForOption(outDirOpt)!;
            var filters = parse.GetValueForOption(filterOpt) ?? Array.Empty<string>();
            var dop = parse.GetValueForOption(dopOpt);
            var decompress = parse.GetValueForOption(decompressOpt);

            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aes, gameTag, verbose);
                Directory.CreateDirectory(outDir.FullName);

                var files = provider.Files.Values.AsEnumerable();
                if (filters is { Length: > 0 })
                {
                    files = files.Where(f => filters.Any(fil => f.Path.Contains(fil, StringComparison.OrdinalIgnoreCase)));
                }

                var exported = 0;
                var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, dop) };

                await Task.Run(() => Parallel.ForEach(files, po, file =>
                {
                    try
                    {
                        if (!provider.TryLoadPackage(file, out var pkg)) return;
                        var folder = file.Path.SubstringBeforeLast('/');
                        for (var i = 0; i < pkg.ExportMapLength; i++)
                        {
                            var ptr = new FPackageIndex(pkg, i + 1).ResolvedObject;
                            if (ptr?.Object is null) continue;
                            var dummy = ((AbstractUePackage)pkg).ConstructObject(ptr.Class?.Object?.Value as UStruct, pkg);
                            if (dummy is not (USoundWave or UAkMediaAssetData)) continue;

                            var exportObj = ptr.Object.Value;
                        SoundDecoder.Decode(exportObj, decompress, out var format, out var data);
                        if (data == null || string.IsNullOrWhiteSpace(format)) continue;

                        var name = exportObj.Name;
                        var outFolder = Path.Combine(outDir.FullName, folder);
                        Directory.CreateDirectory(outFolder);
                        var ext = NormalizeAudioExtension(format);
                        var path = Path.Combine(outFolder, $"{name}.{ext}");
                        File.WriteAllBytes(path, data);

                            System.Threading.Interlocked.Increment(ref exported);
                            if (verbose)
                                Console.WriteLine($"[export-sounds] {folder}/{name}.{format.ToLowerInvariant()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose)
                            Console.WriteLine($"[export-sounds] error: {ex.Message}");
                    }
                }));

                Console.WriteLine($"[export-sounds] done. exported={exported}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[export-sounds] {ex.Message}");
                if (verbose) Console.Error.WriteLine(ex);
                context.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static string NormalizeAudioExtension(string audioFormat)
    {
        if (string.IsNullOrWhiteSpace(audioFormat)) return "bin";
        var f = audioFormat.ToLowerInvariant();
        if (f.Contains("ogg")) return "ogg";
        if (f.Contains("wem")) return "wem";
        if (f.Contains("opus")) return "opus";
        if (f.Contains("binka")) return "binka";
        if (f.Contains("adpcm")) return "wav"; // packed ADPCM in RIFF/WAV
        if (f.Contains("pcm")) return "wav";
        if (f == "wav") return "wav";
        return f.Length <= 4 ? f : "bin";
    }
}

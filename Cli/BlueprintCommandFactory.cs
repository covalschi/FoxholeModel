using System;
using System.CommandLine;
using System.IO;
using FModelHeadless.Lib.Blueprint;

namespace FModelHeadless.Cli;

internal static class BlueprintCommandFactory
{
    public static Command Create(Option<DirectoryInfo> pakDirOption, Option<FileInfo?> mappingOption, Option<string?> aesOption, Option<string> gameOption, Option<bool> verboseOption)
    {
        var root = new Command("blueprint", "Blueprint exploration helpers");

        var pathOption = new Option<string>("--path", "Blueprint object path (e.g. /Game/Vehicles/BPFlatbedTruck.BPFlatbedTruck_C)")
        {
            IsRequired = true
        };

        var componentsCommand = new Command("components", "List component meshes referenced by the blueprint");
        componentsCommand.AddOption(pathOption);
        componentsCommand.SetHandler((DirectoryInfo pakDir, FileInfo? mapping, string? aes, string gameTag, bool verbose, string blueprintPath) =>
        {
            withProvider(pakDir, mapping, aes, gameTag, verbose, provider =>
            {
                if (!BlueprintHelpers.TryFindBlueprint(provider, blueprintPath, verbose, out var blueprintClass, out var resolved))
                {
                    Console.Error.WriteLine($"[blueprint] Unable to resolve '{blueprintPath}'.");
                    Environment.ExitCode = 1;
                    return;
                }

                Console.WriteLine($"[blueprint] {resolved}");
                var components = BlueprintHelpers.AssembleBlueprint(provider, blueprintClass, verbose);
                foreach (var component in components)
                {
                    var path = component.Asset?.GetPathName() ?? "<null>";
                    var transform = component.Transform;
                    Console.WriteLine($"  {component.Name}: {path} loc=({transform.Translation.X:F2},{transform.Translation.Y:F2},{transform.Translation.Z:F2}) rot=({transform.Rotation.X:F3},{transform.Rotation.Y:F3},{transform.Rotation.Z:F3},{transform.Rotation.W:F3}) scale=({transform.Scale3D.X:F2},{transform.Scale3D.Y:F2},{transform.Scale3D.Z:F2})");
                }
                Console.WriteLine($"[blueprint] {components.Count} component(s).");
            });
        }, pakDirOption, mappingOption, aesOption, gameOption, verboseOption, pathOption);

        var graphCommand = new Command("graph", "Enumerate unique asset dependencies referenced by the blueprint");
        graphCommand.AddOption(pathOption);
        graphCommand.SetHandler((DirectoryInfo pakDir, FileInfo? mapping, string? aes, string gameTag, bool verbose, string blueprintPath) =>
        {
            withProvider(pakDir, mapping, aes, gameTag, verbose, provider =>
            {
                if (!BlueprintHelpers.TryFindBlueprint(provider, blueprintPath, verbose, out var blueprintClass, out var resolved))
                {
                    Console.Error.WriteLine($"[blueprint] Unable to resolve '{blueprintPath}'.");
                    Environment.ExitCode = 1;
                    return;
                }

                Console.WriteLine($"[blueprint] {resolved}");
                var references = BlueprintHelpers.CollectAssetGraph(provider, blueprintClass, verbose);
                foreach (var reference in references)
                {
                    Console.WriteLine($"  {reference}");
                }
                Console.WriteLine($"[blueprint] {references.Count} asset reference(s).");
            });
        }, pakDirOption, mappingOption, aesOption, gameOption, verboseOption, pathOption);

        root.AddCommand(componentsCommand);
        root.AddCommand(graphCommand);

        return root;

        static void withProvider(DirectoryInfo pakDir, FileInfo? mapping, string? aesKey, string gameTag, bool verbose, Action<CUE4Parse.FileProvider.DefaultFileProvider> action)
        {
            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aesKey, gameTag, verbose);
                using var scope = new ProviderScope(provider);
                action(provider);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[blueprint] {ex.Message}");
                if (verbose)
                    Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }
    }
}

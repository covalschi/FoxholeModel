using System;
using System.CommandLine;
using System.IO;
using FModelHeadless.Lib.Cargo;

namespace FModelHeadless.Cli;

internal static class CargoCommandFactory
{
    public static Command Create(Option<DirectoryInfo> pakDirOption, Option<FileInfo?> mappingOption, Option<string?> aesOption, Option<string> gameOption, Option<bool> verboseOption)
    {
        var root = new Command("cargo", "Cargo attachment helpers");

        var blueprintOption = new Option<string>("--path", "Vehicle blueprint to inspect (e.g. /Game/Vehicles/BPFlatbedTruck.BPFlatbedTruck_C)")
        {
            IsRequired = true
        };
        var basePropertyOption = new Option<string>("--base-component", () => "BaseMesh", "Property name for the cargo platform component");
        var transferPropertyOption = new Option<string>("--transfer-component", () => "TransferLocation", "Property name for the transfer/attachment component");

        var anchorCommand = new Command("anchor", "Compute cargo anchor transforms for a vehicle");
        anchorCommand.AddOption(blueprintOption);
        anchorCommand.AddOption(basePropertyOption);
        anchorCommand.AddOption(transferPropertyOption);

        anchorCommand.SetHandler((DirectoryInfo pakDir, FileInfo? mapping, string? aes, string gameTag, bool verbose, string blueprintPath, string baseProperty, string transferProperty) =>
        {
            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aes, gameTag, verbose);
                using var scope = new ProviderScope(provider);

                if (!CargoPlatformAnalyzer.TryComputeAnchor(provider, blueprintPath, out var result, baseProperty, transferProperty, verbose))
                {
                    Console.Error.WriteLine($"[cargo] Failed to compute cargo anchor for '{blueprintPath}'.");
                    Environment.ExitCode = 1;
                    return;
                }

                Console.WriteLine($"[cargo] Blueprint: {result.BlueprintPath}");
                Console.WriteLine($"  BaseMesh component: {result.BaseMeshProperty}");
                Console.WriteLine($"  Transfer component: {result.TransferLocationProperty}");
                Console.WriteLine($"  Mesh origin: ({result.MeshOrigin.X:F2}, {result.MeshOrigin.Y:F2}, {result.MeshOrigin.Z:F2})");
                var baseRot = result.BaseMeshTransform.Rotation;
                Console.WriteLine($"  Base transform loc=({result.BaseMeshTransform.Translation.X:F2}, {result.BaseMeshTransform.Translation.Y:F2}, {result.BaseMeshTransform.Translation.Z:F2}) rot=({baseRot.X:F3},{baseRot.Y:F3},{baseRot.Z:F3},{baseRot.W:F3})");
                Console.WriteLine($"  Cargo center: ({result.CargoCenter.X:F2}, {result.CargoCenter.Y:F2}, {result.CargoCenter.Z:F2})");
                Console.WriteLine($"  Has transfer location: {result.HasTransferLocation}");
                if (result.BaseMeshAssetPath != null)
                {
                    Console.WriteLine($"  Base mesh asset: {result.BaseMeshAssetPath}");
                }
                var transferRot = result.TransferTransform.Rotation;
                Console.WriteLine($"  Transfer transform loc=({result.TransferTransform.Translation.X:F2}, {result.TransferTransform.Translation.Y:F2}, {result.TransferTransform.Translation.Z:F2}) rot=({transferRot.X:F3},{transferRot.Y:F3},{transferRot.Z:F3},{transferRot.W:F3})");

                if (result.MultiplexVariants.Count > 0)
                {
                    Console.WriteLine("  Multiplex variants:");
                    foreach (var variant in result.MultiplexVariants)
                    {
                        Console.WriteLine($"    threshold={variant.Threshold:F3} mesh={variant.StaticMeshPath ?? "<none>"}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[cargo] {ex.Message}");
                if (verbose)
                    Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }, pakDirOption, mappingOption, aesOption, gameOption, verboseOption, blueprintOption, basePropertyOption, transferPropertyOption);

        root.AddCommand(anchorCommand);
        return root;
    }
}

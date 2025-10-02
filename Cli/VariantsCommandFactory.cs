using System;
using System.CommandLine;
using System.IO;
using FModelHeadless.Lib.Variants;

namespace FModelHeadless.Cli;

internal static class VariantsCommandFactory
{
    public static Command Create(Option<DirectoryInfo?> pakDirOption, Option<FileInfo?> mappingOption, Option<string?> aesOption, Option<string> gameOption, Option<bool> verboseOption)
    {
        var root = new Command("variants", "Variant and overlay helpers");

        var blueprintOption = new Option<string>("--path", "Blueprint object path")
        {
            IsRequired = true
        };

        var colorsCommand = new Command("colors", "List color variants exposed by the blueprint");
        colorsCommand.AddOption(blueprintOption);
        colorsCommand.SetHandler((DirectoryInfo? pakDir, FileInfo? mapping, string? aes, string gameTag, bool verbose, string blueprintPath) =>
        {
            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aes, gameTag, verbose);
                using var scope = new ProviderScope(provider);

                if (!ColorVariantExtractor.TryGetColorVariants(provider, blueprintPath, verbose, out var variants))
                {
                    Console.Error.WriteLine($"[variants] No color variants found for '{blueprintPath}'.");
                    Environment.ExitCode = 1;
                    return;
                }

                Console.WriteLine($"[variants] {variants.BlueprintPath}");
                foreach (var variant in variants.Variants)
                {
                    var color = variant.Color;
                    Console.WriteLine($"  #{variant.Index:D2} hex={variant.Hex} rgba=({color.R:F3},{color.G:F3},{color.B:F3},{color.A:F3}) name={variant.Name ?? "<unnamed>"}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[variants] {ex.Message}");
                if (verbose)
                    Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }, pakDirOption, mappingOption, aesOption, gameOption, verboseOption, blueprintOption);

        var overlayCommand = new Command("overlays", "Inspect mud/snow overlay parameters");
        overlayCommand.AddOption(blueprintOption);
        overlayCommand.SetHandler((DirectoryInfo? pakDir, FileInfo? mapping, string? aes, string gameTag, bool verbose, string blueprintPath) =>
        {
            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aes, gameTag, verbose);
                using var scope = new ProviderScope(provider);

                if (!OverlayParameterExtractor.TryGetOverlayParameters(provider, blueprintPath, verbose, out var parameters))
                {
                    Console.Error.WriteLine($"[variants] No overlay parameters discovered for '{blueprintPath}'.");
                    Environment.ExitCode = 1;
                    return;
                }

                Console.WriteLine($"[variants] {blueprintPath}");
                foreach (var parameter in parameters)
                {
                    switch (parameter.Value)
                    {
                        case CUE4Parse.UE4.Objects.Core.Math.FLinearColor color:
                            Console.WriteLine($"  {parameter.Context}.{parameter.PropertyName}: {parameter.ValueKind} ({color.R:F3},{color.G:F3},{color.B:F3},{color.A:F3})");
                            break;
                        default:
                            Console.WriteLine($"  {parameter.Context}.{parameter.PropertyName}: {parameter.ValueKind} {parameter.Value}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[variants] {ex.Message}");
                if (verbose)
                    Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }, pakDirOption, mappingOption, aesOption, gameOption, verboseOption, blueprintOption);

        root.AddCommand(colorsCommand);
        root.AddCommand(overlayCommand);
        return root;
    }
}

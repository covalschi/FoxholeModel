using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Engine;

namespace FModelHeadless.Lib.Blueprint;

public static class BlueprintHelpers
{
    public record BlueprintComponent(string Name, UObject Asset, CUE4Parse.UE4.Objects.Core.Math.FTransform Transform);

    public static bool TryFindBlueprint(DefaultFileProvider provider, string objectPath, bool verbose, out UBlueprintGeneratedClass blueprintClass, out string resolvedPath)
        => BlueprintResolver.TryFind(provider, objectPath, verbose, out blueprintClass, out resolvedPath);

    public static IReadOnlyCollection<string> CollectAssetGraph(DefaultFileProvider provider, UBlueprintGeneratedClass blueprintClass, bool verbose = false)
    {
        var components = AssembleBlueprint(provider, blueprintClass, verbose);
        return components
            .Select(component => component.Asset?.GetPathName() ?? string.Empty)
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<BlueprintComponent> AssembleBlueprint(DefaultFileProvider provider, UBlueprintGeneratedClass blueprintClass, bool verbose = false)
        => BlueprintSceneBuilder
            .Build(provider, blueprintClass, verbose)
            .Select(component => new BlueprintComponent(component.Name, component.Asset, component.Transform))
            .ToArray();
}

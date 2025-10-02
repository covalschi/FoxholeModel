using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using FModelHeadless.Lib.Blueprint;

namespace FModelHeadless.Lib.Variants;

public static class ColorVariantExtractor
{
    public record ColorVariant(int Index, FLinearColor Color, string Hex, string? Name);

    public record ColorVariantSet(string BlueprintPath, IReadOnlyList<ColorVariant> Variants);

    public static bool TryGetColorVariants(DefaultFileProvider provider, string objectPath, bool verbose, out ColorVariantSet variantSet)
    {
        variantSet = null!;

        if (!BlueprintResolver.TryFind(provider, objectPath, verbose, out var blueprintClass, out var resolvedPath))
        {
            if (verbose)
                Console.Error.WriteLine($"[variants] Unable to resolve blueprint '{objectPath}'.");
            return false;
        }

        var defaultObject = blueprintClass.ClassDefaultObject.Load<UObject>();
        if (defaultObject == null)
        {
            if (verbose)
                Console.Error.WriteLine($"[variants] Blueprint '{resolvedPath}' lacks class default object.");
            return false;
        }

        var colors = ExtractColorArray(defaultObject, verbose);
        if (colors.Count == 0)
        {
            if (verbose)
                Console.Error.WriteLine($"[variants] Blueprint '{resolvedPath}' does not expose a Colors array.");
            return false;
        }

        var names = ExtractColorNames(defaultObject, colors.Count);

        var list = new List<ColorVariant>(colors.Count);
        for (var i = 0; i < colors.Count; i++)
        {
            var color = colors[i];
            var hex = color.ToFColor(true).Hex;
            var name = names.ElementAtOrDefault(i);
            list.Add(new ColorVariant(i, color, hex, name));
        }

        variantSet = new ColorVariantSet(resolvedPath, list);
        return true;
    }

    private static IReadOnlyList<FLinearColor> ExtractColorArray(UObject defaultObject, bool verbose)
    {
        try
        {
            var colors = defaultObject.GetOrDefault("Colors", Array.Empty<FLinearColor>());
            if (colors.Length > 0)
                return colors;
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[variants] Failed to read Colors array: {ex.Message}");
        }

        return Array.Empty<FLinearColor>();
    }

    private static IReadOnlyList<string?> ExtractColorNames(UObject defaultObject, int desired)
    {
        try
        {
            var nameArray = defaultObject.GetOrDefault("ColorNames", Array.Empty<FName>());
            if (nameArray.Length >= desired)
                return nameArray.Select(n => n.ToString()).ToArray();
        }
        catch
        {
            // ignore
        }

        try
        {
            var stringArray = defaultObject.GetOrDefault("ColorNames", Array.Empty<string>());
            if (stringArray.Length >= desired)
                return stringArray;
        }
        catch
        {
            // ignore
        }

        return Array.Empty<string?>();
    }
}

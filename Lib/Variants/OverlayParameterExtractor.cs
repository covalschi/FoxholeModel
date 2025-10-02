using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using FModelHeadless.Lib.Blueprint;

namespace FModelHeadless.Lib.Variants;

public static class OverlayParameterExtractor
{
    private static readonly string[] Keywords =
        { "mud", "snow", "ice", "frost", "dirt", "slush", "weather" };

    public record OverlayParameter(string Context, string PropertyName, string ValueKind, object Value);

    private const int MaxTraversalDepth = 64;

    private static readonly HashSet<string> TraverseWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        // common UE component/material containers worth walking
        "RootComponent", "MeshComponent", "StaticMeshComponent", "SkeletalMeshComponent",
        "StaticMesh", "SkeletalMesh", "Template", "Default", "ChildActorTemplate",
        "Materials", "Material", "MaterialOverrides", "ScalarParameterValues",
        "VectorParameterValues", "TextureParameterValues", "MaterialParameters",
        "ComponentTemplates", "SimpleConstructionScript", "AllNodes", "AllComponents",
        "RootNodes", "AttachChildren"
    };

    private static bool ShouldTraverse(string propertyName, bool parentMatched)
    {
        if (parentMatched) return true;
        if (string.IsNullOrEmpty(propertyName)) return false;
        if (TraverseWhitelist.Contains(propertyName)) return true;
        return Keywords.Any(k => propertyName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static bool TryGetOverlayParameters(DefaultFileProvider provider, string objectPath, bool verbose, out IReadOnlyList<OverlayParameter> parameters)
    {
        parameters = Array.Empty<OverlayParameter>();

        if (!BlueprintResolver.TryFind(provider, objectPath, verbose, out var blueprintClass, out var resolvedPath))
        {
            if (verbose)
                Console.Error.WriteLine($"[overlay] Unable to resolve blueprint '{objectPath}'.");
            return false;
        }

        var defaultObject = blueprintClass.ClassDefaultObject.Load<UObject>();
        if (defaultObject == null)
        {
            if (verbose)
                Console.Error.WriteLine($"[overlay] Blueprint '{resolvedPath}' lacks a class default object.");
            return false;
        }

        var visitedObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var results = new List<OverlayParameter>();

        CollectFromObject(defaultObject, defaultObject.Name, visitedObjects, results, 0);

        if (results.Count == 0)
            return false;

        parameters = results;
        return true;
    }

    private static void CollectFromObject(UObject obj, string context, HashSet<object> visited, List<OverlayParameter> results, int depth)
    {
        if (depth > MaxTraversalDepth)
            return;

        if (!visited.Add(obj))
            return;

        foreach (var property in obj.Properties)
        {
            var propName = property.Name.Text;
            var tag = property.Tag;
            if (tag == null)
                continue;

            var matchesKeyword = Keywords.Any(k => propName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

            if (matchesKeyword)
            {
                if (TryExtractValue(tag, out var valueKind, out var value))
                {
                    results.Add(new OverlayParameter(context, propName, valueKind, value));
                }
            }

            switch (tag)
            {
                case ObjectProperty objectProp:
                {
                    if (ShouldTraverse(propName, matchesKeyword))
                    {
                        var child = objectProp.Value.Load<UObject>();
                        if (child != null)
                            CollectFromObject(child, $"{context}.{propName}", visited, results, depth + 1);
                    }
                    break;
                }
                case StructProperty structProp:
                    if (ShouldTraverse(propName, matchesKeyword))
                        CollectFromStruct(structProp.Value.StructType, matchesKeyword, $"{context}.{propName}", visited, results, depth + 1);
                    break;
                case ArrayProperty arrayProp:
                    if (ShouldTraverse(propName, matchesKeyword))
                        CollectFromArray(arrayProp, matchesKeyword, $"{context}.{propName}", visited, results, depth + 1);
                    break;
            }
        }
    }

    private static void CollectFromStruct(IUStruct structValue, bool parentMatched, string context, HashSet<object> visited, List<OverlayParameter> results, int depth)
    {
        if (depth > MaxTraversalDepth)
            return;

        switch (structValue)
        {
            case FLinearColor linearColor:
                results.Add(new OverlayParameter(context, "LinearColor", nameof(FLinearColor), linearColor));
                break;
            case FColor color:
                var lc = new FLinearColor(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
                results.Add(new OverlayParameter(context, "Color", nameof(FLinearColor), lc));
                break;
            case FStructFallback fallback:
                if (!visited.Add(fallback))
                    return;
                foreach (var prop in fallback.Properties)
                {
                    var propName = prop.Name.Text;
                    var tag = prop.Tag;
                    if (tag == null)
                        continue;

                    var matchesKeyword = parentMatched || Keywords.Any(k => propName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (matchesKeyword && TryExtractValue(tag, out var valueKind, out var value))
                    {
                        results.Add(new OverlayParameter(context, propName, valueKind, value));
                    }

                    if (tag is StructProperty nestedStruct)
                    {
                        if (ShouldTraverse(propName, matchesKeyword))
                            CollectFromStruct(nestedStruct.Value.StructType, matchesKeyword, $"{context}.{propName}", visited, results, depth + 1);
                    }
                    else if (tag is ObjectProperty objProp)
                    {
                        if (ShouldTraverse(propName, matchesKeyword))
                        {
                            var child = objProp.Value.Load<UObject>();
                            if (child != null)
                                CollectFromObject(child, $"{context}.{propName}", visited, results, depth + 1);
                        }
                    }
                    else if (tag is ArrayProperty arrayProp)
                    {
                        if (ShouldTraverse(propName, matchesKeyword))
                            CollectFromArray(arrayProp, matchesKeyword, $"{context}.{propName}", visited, results, depth + 1);
                    }
                }
                break;
        }
    }

    private static void CollectFromArray(ArrayProperty arrayProp, bool parentMatched, string context, HashSet<object> visited, List<OverlayParameter> results, int depth)
    {
        if (depth > MaxTraversalDepth)
            return;

        var arrayValue = arrayProp.Value;

        for (var i = 0; i < arrayValue.Properties.Count; i++)
        {
            var element = arrayValue.Properties[i];
            var elementContext = $"{context}[{i}]";

            if (parentMatched && TryExtractValue(element, out var valueKind, out var value))
            {
                results.Add(new OverlayParameter(elementContext, valueKind, valueKind, value));
            }

            switch (element)
            {
                case StructProperty structProp:
                    if (parentMatched)
                        CollectFromStruct(structProp.Value.StructType, parentMatched, elementContext, visited, results, depth + 1);
                    break;
                case ObjectProperty objProp:
                {
                    if (parentMatched)
                    {
                        var child = objProp.Value.Load<UObject>();
                        if (child != null)
                            CollectFromObject(child, elementContext, visited, results, depth + 1);
                    }
                    break;
                }
            }
        }
    }

    private static bool TryExtractValue(FPropertyTagType tag, out string kind, out object value)
    {
        switch (tag)
        {
            case FloatProperty floatProp:
                kind = "Float";
                value = floatProp.Value;
                return true;
            case DoubleProperty doubleProp:
                kind = "Double";
                value = doubleProp.Value;
                return true;
            case IntProperty intProp:
                kind = "Integer";
                value = intProp.Value;
                return true;
            case UInt32Property uintProp:
                kind = "Integer";
                value = uintProp.Value;
                return true;
            case UInt16Property ushortProp:
                kind = "Integer";
                value = ushortProp.Value;
                return true;
            case ByteProperty byteProp:
                kind = "Integer";
                value = byteProp.Value;
                return true;
            case BoolProperty boolProp:
                kind = "Boolean";
                value = boolProp.Value;
                return true;
            default:
                kind = string.Empty;
                value = default!;
                return false;
        }
    }
}

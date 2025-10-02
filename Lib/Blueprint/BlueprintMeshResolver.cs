using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using FModelHeadless.Cli;
using FModelHeadless.Lib.Cargo;
using FModelHeadless.Lib.Variants;
using FModelHeadless.Rendering;

namespace FModelHeadless.Lib.Blueprint;

internal static class BlueprintMeshResolver
{
    internal sealed record AttachmentMeshInfo(UStaticMesh Mesh, FTransform Transform);

    internal sealed record RootMeshResult(
        UObject Asset,
        OverlayMaskData Overlay,
        string SourcePath,
        string? MetadataPath,
        IReadOnlyList<BlueprintSceneBuilder.BlueprintComponent> Components,
        UObject? DefaultObject);

    internal sealed record AttachmentMeshResult(
        List<AttachmentMeshInfo> Meshes,
        Vector4? DiffuseOverride,
        List<(string ItemPath, int Quantity)> StockpileOptions,
        OverlayMaskData Overlay);

    internal static RootMeshResult ResolveRootMesh(DefaultFileProvider provider, SceneAsset root, bool verbose)
    {
        var path = root.Path;
        var asset = TryLoadMesh(provider, path, verbose);
        var overlay = OverlayMaskData.Empty;
        var scsComponents = Array.Empty<BlueprintSceneBuilder.BlueprintComponent>();

        UObject? defaultObject = null;
        if (asset == null && BlueprintResolver.TryFind(provider, path, verbose, out var blueprint, out var resolvedPath))
        {
            defaultObject = blueprint.ClassDefaultObject.Load<UObject>();
            overlay = ExtractOverlayData(provider, defaultObject, verbose);

            if (verbose && defaultObject != null)
            {
                Console.WriteLine($"[resolver] inspect blueprint {blueprint.GetPathName()} default object {defaultObject.ExportType}");
                foreach (var property in defaultObject.Properties)
                {
                    if (property.Name.Text.IndexOf("Mesh", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine($"  property {property.Name.Text} tagType={property.Tag?.GetType().Name}");
                    }
                }
            }

            var components = BlueprintSceneBuilder.Build(provider, blueprint, verbose);
            scsComponents = components.ToArray();
            asset = SelectPrimaryMeshFromComponents(components, root.Path, root.MetadataPath, verbose)?.Asset
                    ?? (TryGetSkeletalMeshFromDefault(defaultObject, verbose) as UObject)
                    ?? (TryGetFirstStaticMesh(provider, defaultObject, verbose) as UObject);

            // If the chosen component looks like an auxiliary part (e.g., headlight) or is tiny,
            // attempt to fall back to the cargo-platform BaseMesh anchor as the true primary mesh.
            if (asset != null)
            {
                var assetPath = asset.GetPathName() ?? string.Empty;
                var approx = asset is USkeletalMesh sk ? GetApproximateRadius(sk) : asset is UStaticMesh st ? GetApproximateRadius(st) : 0f;
                var looksAux = assetPath.IndexOf("Headlight", StringComparison.OrdinalIgnoreCase) >= 0
                                || assetPath.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0
                                || approx < 2.0f;

                if (looksAux)
                {
                    var replacedFromAnchors = false;
                    foreach (var candidate in GetAnchorCandidates())
                    {
                        if (!CargoPlatformAnalyzer.TryComputeAnchor(provider, path, out var anchor2, baseMeshProperty: candidate, transferProperty: "TransferLocation", verbose: verbose))
                            continue;

                        if (string.IsNullOrWhiteSpace(anchor2.BaseMeshAssetPath))
                            continue;

                        if (verbose)
                            Console.WriteLine($"[resolver] fallback anchor base mesh path ({candidate}): {anchor2.BaseMeshAssetPath}");

                        var normalized = BlueprintResolver.NormalizeObjectPath(anchor2.BaseMeshAssetPath);
                        var replaced = TryLoadMesh(provider, normalized, verbose);
                        if (replaced != null)
                        {
                            asset = replaced;
                            replacedFromAnchors = true;
                            break;
                        }
                    }

                    if (!replacedFromAnchors)
                    {
                        var token = ExtractRootToken(root.Path);
                        var guessed = GuessVehicleMesh(provider, token, verbose);
                        if (guessed != null)
                        {
                            asset = guessed;
                        }
                    }
                }
            }

            if (asset == null)
            {
                var token = ExtractRootToken(root.Path);
                asset = GuessVehicleMesh(provider, token, verbose);

                foreach (var candidate in GetAnchorCandidates())
                {
                    if (!CargoPlatformAnalyzer.TryComputeAnchor(provider, path, out var anchor, baseMeshProperty: candidate, transferProperty: "TransferLocation", verbose: verbose))
                        continue;

                    if (string.IsNullOrWhiteSpace(anchor.BaseMeshAssetPath))
                        continue;

                    if (verbose)
                        Console.WriteLine($"[resolver] anchor base mesh path ({candidate}): {anchor.BaseMeshAssetPath}");

                    var normalized = BlueprintResolver.NormalizeObjectPath(anchor.BaseMeshAssetPath);
                    asset = TryLoadMesh(provider, normalized, verbose);
                    if (asset != null)
                        break;
                }
            }

            if (asset == null)
            {
                asset = ResolveMeshFromHierarchy(provider, root, blueprint, verbose);
            }

            asset ??= TryLoadMesh(provider, resolvedPath, verbose);
        }

        if (asset == null)
            throw new InvalidOperationException($"Unable to resolve root asset '{path}'.");

        var sourcePath = asset.GetPathName();
        return new RootMeshResult(asset, overlay, sourcePath, root.MetadataPath ?? root.Path, scsComponents, defaultObject);
    }

    private static UObject? GuessVehicleMesh(DefaultFileProvider provider, string? token, bool verbose)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var guesses = new[]
        {
            $"/Game/Meshes/Vehicles/SK_{token}.SK_{token}",
            $"/Game/Meshes/Vehicles/SM_{token}.SM_{token}",
            $"/Game/Meshes/Vehicles/{token}.{token}"
        };

        foreach (var guess in guesses)
        {
            var loaded = TryLoadMesh(provider, guess, verbose);
            if (loaded != null)
            {
                if (verbose)
                    Console.WriteLine($"[resolver] guessed primary mesh: {guess}");
                return loaded;
            }
        }

        return null;
    }

    internal static AttachmentMeshResult ResolveAttachmentMeshes(
        DefaultFileProvider provider,
        SceneAsset asset,
        string metadataPath,
        string hpState,
        int? colorVariantIndex,
        bool verbose)
    {
        var meshes = new List<AttachmentMeshInfo>();
        var stockpileOptions = new List<(string ItemPath, int Quantity)>();
        Vector4? diffuseOverride = null;
        var overlay = OverlayMaskData.Empty;

        if (BlueprintResolver.TryFind(provider, metadataPath, verbose, out var blueprintClass, out var resolvedPath))
        {
            var defaultObject = blueprintClass.ClassDefaultObject.Load<UObject>();
            stockpileOptions = ExtractStockpileOptions(defaultObject, verbose);
            overlay = ExtractOverlayData(provider, defaultObject, verbose);

            var roots = new List<UObject>();
            if (defaultObject != null)
                roots.Add(defaultObject);

            if (defaultObject != null && defaultObject.TryGetValue(out FPackageIndex meshComponentIndex, "MeshComponent") && !meshComponentIndex.IsNull)
            {
                if (meshComponentIndex.Load<UObject>() is { } meshComponent)
                    roots.Add(meshComponent);
            }

            if (defaultObject != null && defaultObject.TryGetValue(out FPackageIndex rootComponentIndex, "RootComponent") && !rootComponentIndex.IsNull)
            {
                if (rootComponentIndex.Load<UObject>() is { } rootComponent)
                    roots.Add(rootComponent);
            }

            var collected = CollectMeshes(provider, roots, hpState, verbose);
            meshes.AddRange(collected.Select(tuple => new AttachmentMeshInfo(tuple.mesh, tuple.transform)));

            var scsComponents = BlueprintSceneBuilder.Build(provider, blueprintClass, verbose);
            foreach (var component in scsComponents)
            {
                if (component.Asset is UStaticMesh staticMesh)
                {
                    meshes.Add(new AttachmentMeshInfo(staticMesh, component.Transform));
                }
            }

            if (colorVariantIndex.HasValue &&
                ColorVariantExtractor.TryGetColorVariants(provider, metadataPath, verbose, out var variantSet))
            {
                var idx = colorVariantIndex.Value;
                if (idx >= 0 && idx < variantSet.Variants.Count)
                {
                    var color = variantSet.Variants[idx].Color;
                    diffuseOverride = new Vector4(color.R, color.G, color.B, color.A);
                }
                else if (verbose)
                {
                    Console.Error.WriteLine($"[resolver] Variant index {idx} is out of range for '{resolvedPath}'.");
                }
            }
        }

        return new AttachmentMeshResult(meshes, diffuseOverride, stockpileOptions, overlay);
    }

    private static UObject? TryLoadMesh(DefaultFileProvider provider, string path, bool verbose)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var skeletal = provider.LoadPackageObject<USkeletalMesh>(path);
            if (skeletal != null)
                return skeletal;
        }
        catch
        {
            // ignore, fall through
        }

        try
        {
            var staticMesh = provider.LoadPackageObject<UStaticMesh>(path);
            if (staticMesh != null)
                return staticMesh;
        }
        catch
        {
            // ignore
        }

        if (verbose)
            Console.Error.WriteLine($"[resolver] Unable to load mesh directly from '{path}'.");
        return null;
    }

    private static IEnumerable<string> GetAnchorCandidates()
    {
        yield return "BaseMesh";
        yield return "VehicleMeshComponent";
        yield return "VehicleMesh";
        yield return "MeshComponent";
    }

    private static BlueprintSceneBuilder.BlueprintComponent? SelectPrimaryMeshFromComponents(
        IReadOnlyList<BlueprintSceneBuilder.BlueprintComponent> components,
        string rootPath,
        string? metadataPath,
        bool verbose)
    {
        if (components == null || components.Count == 0)
            return null;

        var desiredPaths = BuildDesiredPaths(rootPath, metadataPath);
        var rootToken = ExtractRootToken(rootPath);

        BlueprintSceneBuilder.BlueprintComponent? bestMatch = null;
        float bestScore = float.MinValue;

        foreach (var component in components)
        {
            var assetPath = component.Asset.GetPathName();
            var score = ScoreComponent(component.Asset, assetPath, desiredPaths, rootToken);
            if (verbose)
            {
                Console.WriteLine($"[resolver] candidate component asset={assetPath} score={score:F2} type={component.Asset.ExportType}");
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = component;
            }
        }

        return bestMatch;
    }

    private static float ScoreComponent(UObject asset, string assetPath, IReadOnlyList<string> desiredPaths, string? rootToken)
    {
        var score = 0f;
        foreach (var desired in desiredPaths)
        {
            if (string.Equals(assetPath, desired, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000f;
                break;
            }

            if (!string.IsNullOrEmpty(desired) && assetPath.IndexOf(desired, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 500f;
            }
        }

        // Prefer meshes that look like the main vehicle/body
        if (!string.IsNullOrEmpty(rootToken) && assetPath.IndexOf(rootToken, StringComparison.OrdinalIgnoreCase) >= 0)
            score += 400f;

        if (assetPath.Contains("/Meshes/Vehicles/", StringComparison.OrdinalIgnoreCase))
            score += 200f;

        // Penalize common auxiliary parts
        var auxHints = new[] { "Headlight", "Lamp", "Mirror", "Wheel", "Tread", "Glass", "Door", "Handle", "Light" };
        foreach (var hint in auxHints)
            if (assetPath.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                score -= 600f;

        if (asset is USkeletalMesh skeletal)
        {
            score += 200f;
            score += GetApproximateRadius(skeletal) * 0.5f;
        }
        else if (asset is UStaticMesh staticMesh)
        {
            score += GetApproximateRadius(staticMesh) * 0.5f;
        }

        return score;
    }

    private static IReadOnlyList<string> BuildDesiredPaths(string rootPath, string? metadataPath)
    {
        var list = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            var normalized = BlueprintResolver.NormalizeObjectPath(path);
            if (!string.IsNullOrWhiteSpace(normalized))
                list.Add(normalized);
        }

        Add(rootPath);
        Add(metadataPath);

        return list;
    }

    private static float GetApproximateRadius(USkeletalMesh mesh)
        => mesh?.ImportedBounds.SphereRadius ?? 0f;

    private static float GetApproximateRadius(UStaticMesh mesh)
        => mesh?.RenderData?.Bounds.SphereRadius
           ?? mesh?.GetOrDefault("ExtendedBounds", new FBoxSphereBounds()).SphereRadius
           ?? 0f;

    private static string? ExtractRootToken(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return null;
        var norm = BlueprintResolver.NormalizeObjectPath(rootPath);
        var slash = norm.LastIndexOf('/');
        var after = slash >= 0 ? norm[(slash + 1)..] : norm;
        var dot = after.IndexOf('.');
        var name = dot >= 0 ? after[..dot] : after;
        if (name.StartsWith("BP", StringComparison.OrdinalIgnoreCase))
            name = name[2..];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static USkeletalMesh? TryGetSkeletalMeshFromDefault(UObject? defaultObject, bool verbose)
    {
        if (defaultObject == null)
            return null;

        var candidates = new[]
        {
            "MeshComponent",
            "VehicleMeshComponent",
            "VehicleMesh",
            "Mesh",
            "RootComponent",
            "BaseMesh"
        };

        foreach (var property in candidates)
        {
            if (TryLoadSkeletalMeshFromProperty(defaultObject, property, verbose, out var mesh))
                return mesh;
        }

        return null;
    }

    private static bool TryLoadSkeletalMeshFromProperty(UObject obj, string propertyName, bool verbose, out USkeletalMesh mesh)
    {
        mesh = null!;
        try
        {
            if (!obj.TryGetValue(out FPackageIndex componentIndex, propertyName) || componentIndex.IsNull)
            {
                if (verbose)
                {
                    Console.WriteLine($"[resolver] property '{propertyName}' not found or null on '{obj.Name}'.");
                }
                return false;
            }

            var loadedObj = componentIndex.Load<UObject>();
            if (loadedObj is USkeletalMeshComponent skeletalComponent)
            {
                if (TryResolveSkeletalMeshFromComponent(skeletalComponent, out mesh))
                    return true;

                if (skeletalComponent.Template?.Load<USkeletalMeshComponent>() is { } templateComponent &&
                    TryResolveSkeletalMeshFromComponent(templateComponent, out mesh))
                    return true;

                return false;
            }

            if (loadedObj is USkeletalMesh skeletalMesh)
            {
                mesh = skeletalMesh;
                return true;
            }

            if (loadedObj == null)
            {
                if (verbose)
                {
                Console.WriteLine($"[resolver] property '{propertyName}' resolved to null object.");
                }
                return false;
            }

            if (verbose)
            {
                Console.WriteLine($"[resolver] property '{propertyName}' -> {loadedObj.ExportType} (unsupported for skeletal mesh extraction)");
            }

            return false;
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[resolver] Failed to load skeletal mesh from '{propertyName}': {ex.Message}");
        }

        return false;
    }

    private static bool TryResolveSkeletalMeshFromComponent(USkeletalMeshComponent component, out USkeletalMesh mesh)
    {
        mesh = null!;

        var meshIndex = component.GetSkeletalMesh();
        if (!meshIndex.IsNull)
        {
            var loaded = meshIndex.Load<USkeletalMesh>();
            if (loaded != null)
            {
                mesh = loaded;
                return true;
            }
        }

        if (component.TryGetValue(out FPackageIndex directIndex, "SkeletalMesh") && !directIndex.IsNull)
        {
            var loaded = directIndex.Load<USkeletalMesh>();
            if (loaded != null)
            {
                mesh = loaded;
                return true;
            }
        }

        return false;
    }

    private static UStaticMesh? TryGetFirstStaticMesh(DefaultFileProvider provider, UObject? defaultObject, bool verbose)
    {
        if (defaultObject == null)
            return null;

        var meshes = CollectMeshes(provider, new[] { defaultObject }, "normal", verbose);
        if (meshes.Count == 0)
            return null;

        return meshes
            .OrderByDescending(tuple => GetApproximateRadius(tuple.mesh))
            .First().mesh;
    }

    private static UObject? ResolveMeshFromHierarchy(DefaultFileProvider provider, SceneAsset rootSpec, UBlueprintGeneratedClass blueprint, bool verbose)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (UStruct? current = blueprint; current != null; current = GetSuperStruct(current))
        {
            if (current is not UBlueprintGeneratedClass classBlueprint)
                continue;

            var identifier = classBlueprint.GetPathName();
            if (!visited.Add(identifier))
                continue;

            var defaultObject = classBlueprint.ClassDefaultObject.Load<UObject>();
            if (defaultObject == null)
                continue;

            if (verbose)
            {
                Console.WriteLine($"[resolver] inspecting superclass {identifier}");
                foreach (var property in defaultObject.Properties)
                {
                    if (property.Name.Text.IndexOf("Mesh", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine($"  super property {property.Name.Text} tagType={property.Tag?.GetType().Name}");
                    }
                }
            }

            var components = BlueprintSceneBuilder.Build(provider, classBlueprint, verbose);
            var mesh = SelectPrimaryMeshFromComponents(components, rootSpec.Path, rootSpec.MetadataPath, verbose)?.Asset
                       ?? (TryGetSkeletalMeshFromDefault(defaultObject, verbose) as UObject)
                       ?? (TryGetFirstStaticMesh(provider, defaultObject, verbose) as UObject);
            if (mesh != null)
                return mesh;
        }

        return null;
    }

    private static UStruct? GetSuperStruct(UStruct? current)
        => current?.SuperStruct?.Load<UStruct>();

    internal static List<(UStaticMesh mesh, FTransform transform)> CollectMeshes(DefaultFileProvider provider, IEnumerable<UObject> roots, string hpState, bool verbose)
    {
        var results = new List<(UStaticMesh, FTransform)>();
        var seenMeshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<UObject>(roots.Where(r => r != null));

        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (obj == null) continue;

            var key = $"{obj.ExportType}:{obj.GetPathName()}";
            if (!visited.Add(key))
                continue;

            var variants = CargoPlatformAnalyzer.GetMultiplexVariants(obj, verbose);
            if (variants.Count > 0)
            {
                var variantMesh = SelectMultiplexVariant(provider, variants, hpState, verbose);
                if (variantMesh != null)
                    AddMeshResult(results, seenMeshes, variantMesh, FTransform.Identity);
            }

            if (TryGetStaticMeshFromProperty(provider, obj, "StaticMesh", verbose, out var staticMesh))
            {
                AddMeshResult(results, seenMeshes, staticMesh, FTransform.Identity);
            }

            if (TryGetStaticMeshFromProperty(provider, obj, "PreviewStaticMesh", verbose, out staticMesh))
            {
                AddMeshResult(results, seenMeshes, staticMesh, FTransform.Identity);
            }

            if (obj is UStaticMeshComponent staticMeshComponent)
            {
                var loaded = staticMeshComponent.GetLoadedStaticMesh();
                if (loaded != null)
                    AddMeshResult(results, seenMeshes, loaded, FTransform.Identity);
            }

            foreach (var property in obj.Properties)
            {
                if (property.Tag is ObjectProperty objectProperty)
                {
                    if (ShouldTraverseProperty(property.Name.Text) && objectProperty.Value?.Load<UObject>() is { } child)
                        stack.Push(child);
                }
                else if (property.Tag is ArrayProperty arrayProperty)
                {
                    if (ShouldTraverseProperty(property.Name.Text))
                    {
                        foreach (var element in arrayProperty.Value.Properties)
                        {
                            if (element is ObjectProperty arrayObject && arrayObject.Value?.Load<UObject>() is { } childObj)
                                stack.Push(childObj);
                        }
                    }
                }
            }
        }

        return results;
    }

    private static UStaticMesh? SelectMultiplexVariant(DefaultFileProvider provider, IReadOnlyList<CargoPlatformAnalyzer.MultiplexVariant> variants, string hpState, bool verbose)
    {
        if (variants == null || variants.Count == 0)
            return null;

        var sorted = variants.OrderBy(v => v.Threshold).ToList();
        CargoPlatformAnalyzer.MultiplexVariant selected = sorted[^1];

        if (hpState == "critical")
            selected = sorted[0];
        else if (hpState == "damaged" && sorted.Count > 2)
            selected = sorted[sorted.Count - 2];
        else if (hpState == "damaged" && sorted.Count > 1)
            selected = sorted[0];

        if (selected.StaticMeshPath == null)
            return null;

        if (!TryLoadStaticMesh(provider, selected.StaticMeshPath, verbose, out var mesh))
        {
            if (verbose)
                Console.Error.WriteLine($"[resolver] Failed to load multiplex mesh '{selected.StaticMeshPath}'.");
            return null;
        }

        return mesh;
    }

    private static void AddMeshResult(List<(UStaticMesh mesh, FTransform transform)> meshes, HashSet<string> seen, UStaticMesh mesh, FTransform transform)
    {
        var key = mesh.GetPathName();
        if (string.IsNullOrEmpty(key))
            key = mesh.Name;
        if (seen.Add(key))
            meshes.Add((mesh, transform));
    }

    private static bool TryGetStaticMeshFromProperty(DefaultFileProvider provider, UObject obj, string propertyName, bool verbose, out UStaticMesh mesh)
    {
        mesh = null!;
        try
        {
            if (obj.TryGetValue(out FPackageIndex index, propertyName) && !index.IsNull)
            {
                if (verbose)
                    Console.WriteLine($"[resolver] {obj.Name}.{propertyName} -> {index}");
                var loaded = index.Load<UStaticMesh>();
                if (loaded != null)
                {
                    mesh = loaded;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[resolver] {propertyName}: {ex.Message}");
        }
        return false;
    }

    private static bool ShouldTraverseProperty(string propertyName)
    {
        return propertyName switch
        {
            "MeshComponent" => true,
            "RootComponent" => true,
            "AttachParent" => true,
            "Template" => true,
            "ChildActorTemplate" => true,
            "ChildActor" => true,
            "AttachChildren" => true,
            _ => false
        };
    }

    private static List<(string ItemPath, int Quantity)> ExtractStockpileOptions(UObject? defaultObject, bool verbose)
    {
        var results = new List<(string, int)>();

        if (defaultObject == null)
            return results;

        try
        {
            if (defaultObject.TryGetValue(out FStructFallback stockpile, "ReplicatedGenericStockpileComponent"))
            {
                if (stockpile.TryGetValue(out FStructFallback[] entries, "Stockpile"))
                {
                    foreach (var entry in entries)
                    {
                        var itemPath = entry.TryGetValue(out FPackageIndex itemIndex, "Item") && !itemIndex.IsNull
                            ? itemIndex.ToString()
                            : string.Empty;
                        var quantity = entry.GetOrDefault("Amount", 0);
                        if (!string.IsNullOrEmpty(itemPath))
                            results.Add((itemPath, quantity));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[resolver] Stockpile parse failed: {ex.Message}");
        }

        return results;
    }

    private static OverlayMaskData ExtractOverlayData(DefaultFileProvider provider, UObject? defaultObject, bool verbose)
    {
        if (defaultObject == null)
            return OverlayMaskData.Empty;

        UTexture2D? mudMask = null;
        float? mudStrength = null;
        float? mudTightness = null;
        UTexture2D? snowMask = null;
        float? snowStrength = null;
        float? snowTightness = null;

        var path = defaultObject.GetPathName();
        if (OverlayParameterExtractor.TryGetOverlayParameters(provider, path, verbose, out var parameters))
        {
            foreach (var parameter in parameters)
            {
                var name = parameter.PropertyName.ToLowerInvariant();
                var context = parameter.Context.ToLowerInvariant();
                var isMud = name.Contains("mud") || context.Contains("mud") || name.Contains("dirt") || context.Contains("dirt");
                var isSnow = name.Contains("snow") || context.Contains("snow") || name.Contains("ice") || context.Contains("ice");

                switch (parameter.ValueKind)
                {
                    case "Float":
                    case "Double":
                    {
                        var val = Convert.ToSingle(parameter.Value);
                        if (isMud)
                        {
                            if (name.Contains("tight")) mudTightness ??= val;
                            else mudStrength ??= val;
                        }
                        else if (isSnow)
                        {
                            if (name.Contains("tight")) snowTightness ??= val;
                            else snowStrength ??= val;
                        }
                        break;
                    }
                    case "SoftObjectPath":
                    case "ObjectPath":
                    {
                        if (parameter.Value is string pathValue && !string.IsNullOrWhiteSpace(pathValue))
                        {
                            if (isMud && mudMask == null)
                                mudMask = TryLoadMaskTexture(provider, pathValue, verbose);
                            else if (isSnow && snowMask == null)
                                snowMask = TryLoadMaskTexture(provider, pathValue, verbose);
                        }
                        break;
                    }
                }
            }
        }

        return new OverlayMaskData(mudMask, mudStrength, mudTightness, snowMask, snowStrength, snowTightness);
    }

    private static UTexture2D? TryLoadMaskTexture(DefaultFileProvider provider, string rawPath, bool verbose)
    {
        var normalized = BlueprintResolver.NormalizeObjectPath(rawPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        try
        {
            return provider.LoadPackageObject<UTexture2D>(normalized);
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[resolver] Failed to load overlay texture '{normalized}': {ex.Message}");
            return null;
        }
    }

    private static bool TryLoadStaticMesh(DefaultFileProvider provider, string path, bool verbose, out UStaticMesh mesh)
    {
        mesh = null!;
        foreach (var candidate in ExpandMeshCandidates(path))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                var loaded = provider.LoadPackageObject<UObject>(candidate);
                if (loaded is UStaticMesh staticMesh)
                {
                    mesh = staticMesh;
                    return true;
                }
            }
            catch
            {
                if (verbose)
                    Console.Error.WriteLine($"[resolver] Failed to load static mesh '{candidate}'.");
            }
        }

        return false;
    }

    private static IEnumerable<string> ExpandMeshCandidates(string rawPath)
    {
        var normalized = BlueprintResolver.NormalizeObjectPath(rawPath);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return normalized;

        if (!normalized.Contains('.'))
        {
            var slash = normalized.LastIndexOf('/');
            var baseName = slash >= 0 ? normalized[(slash + 1)..] : normalized;
            if (!string.IsNullOrEmpty(baseName))
                yield return $"{normalized}.{baseName}";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using FModelHeadless.Lib.Blueprint;

namespace FModelHeadless.Lib.Cargo;

public static class CargoPlatformAnalyzer
{
    public record CargoAnchorResult(
        string BlueprintPath,
        string BaseMeshProperty,
        string TransferLocationProperty,
        FTransform BaseMeshTransform,
        FTransform TransferTransform,
        FVector MeshOrigin,
        FVector CargoCenter,
        string? BaseMeshAssetPath,
        IReadOnlyList<MultiplexVariant> MultiplexVariants)
    {
        public bool HasTransferLocation => !TransferTransform.Equals(default(FTransform));
    }

    public record MultiplexVariant(float Threshold, string? StaticMeshPath);

    public static bool TryComputeAnchor(
        DefaultFileProvider provider,
        string blueprintPath,
        out CargoAnchorResult result,
        string baseMeshProperty = "BaseMesh",
        string transferProperty = "TransferLocation",
        bool verbose = false)
    {
        result = null!;

        if (!BlueprintResolver.TryFind(provider, blueprintPath, verbose, out var blueprintClass, out var resolvedPath))
        {
            if (verbose) Console.Error.WriteLine($"[cargo] Unable to resolve blueprint for '{blueprintPath}'.");
            return false;
        }

        var defaultObject = blueprintClass.ClassDefaultObject.Load<UObject>();
        if (defaultObject == null)
        {
            if (verbose) Console.Error.WriteLine($"[cargo] Blueprint '{resolvedPath}' has no class default object.");
            return false;
        }

        var baseComponent = LoadComponent<USceneComponent>(defaultObject, baseMeshProperty, verbose);
        if (baseComponent == null)
        {
            if (verbose) Console.Error.WriteLine($"[cargo] Blueprint '{resolvedPath}' missing component '{baseMeshProperty}'.");
            return false;
        }

        if (verbose)
        {
            Console.WriteLine($"[cargo] base component '{baseMeshProperty}' type={baseComponent.ExportType}");
        }

        var transferComponent = LoadComponent<USceneComponent>(defaultObject, transferProperty, verbose);
        if (transferComponent == null)
        {
            if (verbose) Console.Error.WriteLine($"[cargo] Blueprint '{resolvedPath}' missing component '{transferProperty}'.");
            return false;
        }

        if (verbose)
        {
            Console.WriteLine($"[cargo] transfer component '{transferProperty}' type={transferComponent.ExportType}");
        }

        var baseTransform = baseComponent.GetRelativeTransform();
        var transferTransform = transferComponent.GetRelativeTransform();

        var meshOrigin = TryGetMeshOrigin(provider, baseComponent, verbose, out var origin, out var meshPath)
            ? origin : FVector.ZeroVector;

        var cargoCenter = baseTransform.TransformPosition(meshOrigin);

        var multiplexVariants = GetMultiplexVariants(defaultObject, verbose);

        result = new CargoAnchorResult(
            resolvedPath,
            baseMeshProperty,
            transferProperty,
            baseTransform,
            transferTransform,
            meshOrigin,
            cargoCenter,
            meshPath,
            multiplexVariants);

        return true;
    }

    private static TComponent? LoadComponent<TComponent>(UObject owner, string propertyName, bool verbose)
        where TComponent : UObject
    {
        try
        {
            if (owner.TryGetValue(out FPackageIndex idx, propertyName) && !idx.IsNull)
            {
                return idx.Load<TComponent>();
            }

            var lazy = owner.GetOrDefaultLazy<TComponent>(propertyName);
            return lazy?.Value;
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                Console.Error.WriteLine($"[cargo] Failed loading component '{propertyName}': {ex.Message}");
            }
            return null;
        }
    }

    private static bool TryGetMeshOrigin(DefaultFileProvider provider, USceneComponent component, bool verbose, out FVector origin, out string? meshPath)
    {
        origin = FVector.ZeroVector;
        meshPath = null;

        switch (component)
        {
            case UStaticMeshComponent staticComp:
                var staticMesh = ResolveStaticMesh(staticComp, verbose, out meshPath);
                if (staticMesh?.RenderData?.Bounds is { } bounds)
                {
                    origin = bounds.Origin;
                    return true;
                }

                if (staticMesh != null && staticMesh.TryGetValue(out FStructFallback fallback, "ExtendedBounds"))
                {
                    origin = fallback.GetOrDefault<FVector>("Origin");
                    return true;
                }

                break;

            case USkeletalMeshComponent skeletalComp:
                var skeletalMesh = ResolveSkeletalMesh(skeletalComp, verbose, out meshPath);
                if (skeletalMesh != null)
                {
                    origin = skeletalMesh.ImportedBounds.Origin;
                    return true;
                }
                break;
        }

        return false;
    }

    private static UStaticMesh? ResolveStaticMesh(UStaticMeshComponent component, bool verbose, out string? meshPath)
    {
        meshPath = null;

        if (component.TryGetValue(out FPackageIndex meshIndex, "StaticMesh") && !meshIndex.IsNull)
        {
            meshPath = meshIndex.ToString();
            return meshIndex.Load<UStaticMesh>();
        }

        var template = component.Template?.Load<UStaticMeshComponent>();
        if (template != null && template.TryGetValue(out meshIndex, "StaticMesh") && !meshIndex.IsNull)
        {
            meshPath = meshIndex.ToString();
            return meshIndex.Load<UStaticMesh>();
        }

        return null;
    }

    private static USkeletalMesh? ResolveSkeletalMesh(USkeletalMeshComponent component, bool verbose, out string? meshPath)
    {
        meshPath = null;

        if (component.TryGetValue(out FPackageIndex meshIndex, "SkeletalMesh") && !meshIndex.IsNull)
        {
            meshPath = meshIndex.ToString();
            return meshIndex.Load<USkeletalMesh>();
        }

        var template = component.Template?.Load<USkeletalMeshComponent>();
        if (template != null && template.TryGetValue(out meshIndex, "SkeletalMesh") && !meshIndex.IsNull)
        {
            meshPath = meshIndex.ToString();
            return meshIndex.Load<USkeletalMesh>();
        }

        if (verbose)
        {
            Console.Error.WriteLine($"[cargo] Skeletal mesh component '{component.Name}' has no SkeletalMesh reference.");
        }

        return null;
    }

    public static IReadOnlyList<MultiplexVariant> GetMultiplexVariants(UObject defaultObject, bool verbose)
    {
        if (!defaultObject.TryGetValue(out FPackageIndex multiplexIndex, "MultiplexedStaticMesh") || multiplexIndex.IsNull)
            return Array.Empty<MultiplexVariant>();

        var multiplexObj = multiplexIndex.Load<UObject>();
        if (multiplexObj == null)
        {
            if (verbose)
            {
                Console.Error.WriteLine("[cargo] Failed to load MultiplexedStaticMesh component.");
            }
            return Array.Empty<MultiplexVariant>();
        }

        if (!multiplexObj.TryGetValue(out FStructFallback[] stops, "MeshStops") || stops.Length == 0)
            return Array.Empty<MultiplexVariant>();

        var list = new List<MultiplexVariant>(stops.Length);
        foreach (var stop in stops)
        {
            var threshold = stop.GetOrDefault<float>("Threshold");
            string? meshPath = null;
            if (stop.TryGetValue(out FPackageIndex meshIndex, "StaticMesh") && !meshIndex.IsNull)
            {
                meshPath = meshIndex.ToString();
            }

            list.Add(new MultiplexVariant(threshold, meshPath));
        }

        return list;
    }
}

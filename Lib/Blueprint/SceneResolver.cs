using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using FModelHeadless.Cli;
using FModelHeadless.Lib.Cargo;
using FModelHeadless.Rendering;

namespace FModelHeadless.Lib.Blueprint;

internal static class SceneResolver
{
    public static ResolvedScene Resolve(DefaultFileProvider provider, SceneSpec scene, bool verbose)
    {
        if (scene.Assets == null || scene.Assets.Count == 0)
            throw new InvalidOperationException("Scene must contain at least one asset.");

        var assetsById = scene.Assets.ToDictionary(a => a.Id);
        var rootCandidates = scene.Assets.Where(a => a.AttachTo == null || string.IsNullOrWhiteSpace(a.AttachTo.ParentId)).ToList();
        if (rootCandidates.Count != 1)
            throw new InvalidOperationException($"Scene must have exactly one root asset (found {rootCandidates.Count}).");

        var rootSpec = rootCandidates[0];
        if (verbose)
            Console.WriteLine($"[resolver] root asset path='{rootSpec.Path}' metadata='{rootSpec.MetadataPath}'");
        var resolvedRoot = ResolveRoot(provider, rootSpec, verbose);
        var attachments = ResolveAttachments(provider, scene, assetsById, verbose);

        return new ResolvedScene(resolvedRoot, attachments);
    }

    private static ResolvedRootAsset ResolveRoot(DefaultFileProvider provider, SceneAsset rootSpec, bool verbose)
    {
        var rootResult = BlueprintMeshResolver.ResolveRootMesh(provider, rootSpec, verbose);
        var visual = CreateVisualProperties(rootSpec, rootResult.Overlay, null, rootSpec.Properties?.ColorMaterialIndex);
        return new ResolvedRootAsset(rootResult.Asset, visual, rootResult.SourcePath, rootResult.MetadataPath, rootResult.Overlay);
    }

    private static IReadOnlyList<ResolvedAttachmentDescriptor> ResolveAttachments(
        DefaultFileProvider provider,
        SceneSpec scene,
        IReadOnlyDictionary<string, SceneAsset> assetsById,
        bool verbose)
    {
        var results = new List<ResolvedAttachmentDescriptor>();

        foreach (var asset in scene.Assets)
        {
            if (asset.AttachTo is not { } attachRef)
                continue;

            if (!assetsById.TryGetValue(attachRef.ParentId, out var parentSpec))
            {
                if (verbose)
                    Console.Error.WriteLine($"[resolver] Unknown parent '{attachRef.ParentId}' for asset '{asset.Id}'.");
                continue;
            }

            var anchorName = attachRef.Anchor ?? "BaseMesh";
            var parentMetadataPath = parentSpec.MetadataPath ?? parentSpec.Path;
            var anchorTransform = FTransform.Identity;

            if (!string.IsNullOrWhiteSpace(parentMetadataPath))
            {
                if (verbose)
                    Console.WriteLine($"[resolver] attachment '{asset.Id}' parent metadata='{parentMetadataPath}' anchor='{anchorName}'");
                if (!CargoPlatformAnalyzer.TryComputeAnchor(provider, parentMetadataPath, out var anchor, baseMeshProperty: anchorName, transferProperty: "TransferLocation", verbose: verbose))
                {
                    if (verbose)
                        Console.Error.WriteLine($"[resolver] Unable to compute anchor '{anchorName}' on '{parentMetadataPath}' for asset '{asset.Id}'.");
                }
                else
                {
                    anchorTransform = anchor.BaseMeshTransform;
                }
            }

            var offsetTransform = CreateOffsetTransform(attachRef.Offset);
            var baseTransform = CombineTransforms(anchorTransform, offsetTransform);

            var props = asset.Properties ?? new SceneAssetProperties();
            var meshResult = BlueprintMeshResolver.ResolveAttachmentMeshes(provider, asset, asset.MetadataPath ?? asset.Path, NormalizeHpState(props.HpState), props.ColorVariant, verbose);
            var visual = CreateVisualProperties(props, meshResult.Overlay, meshResult.DiffuseOverride, props.ColorMaterialIndex);
            var stockpile = CreateStockpileSelection(props);

            if (meshResult.Meshes.Count == 0)
            {
                if (TryLoadStaticMesh(provider, asset.Path, verbose, out var fallbackMesh))
                {
                    var snooperTransform = CombineTransforms(baseTransform, FTransform.Identity);
                    results.Add(new ResolvedAttachmentDescriptor(asset.Id, fallbackMesh, snooperTransform, visual, stockpile, meshResult.StockpileOptions, meshResult.Overlay));
                }
                else if (verbose)
                {
                    Console.Error.WriteLine($"[resolver] No static mesh available for attachment '{asset.Id}'.");
                }

                continue;
            }

            foreach (var meshInfo in meshResult.Meshes)
            {
                var combined = CombineTransforms(baseTransform, meshInfo.Transform);
                results.Add(new ResolvedAttachmentDescriptor(asset.Id, meshInfo.Mesh, combined, visual, stockpile, meshResult.StockpileOptions, meshResult.Overlay));
            }
        }

        return results;
    }

    private static AssetVisualProperties CreateVisualProperties(SceneAsset asset, OverlayMaskData overlay, Vector4? diffuseOverride, int? colorMaterialIndex)
    {
        var props = asset.Properties ?? new SceneAssetProperties();
        var hpState = NormalizeHpState(props.HpState);
        var mud = props.MudLevel ?? overlay.MudStrength ?? 0f;
        var snow = props.SnowLevel ?? overlay.SnowStrength ?? 0f;

        return new AssetVisualProperties(
            hpState,
            Clamp01(mud),
            Clamp01(snow),
            diffuseOverride,
            colorMaterialIndex);
    }

    private static AssetVisualProperties CreateVisualProperties(SceneAssetProperties props, OverlayMaskData overlay, Vector4? diffuseOverride, int? colorMaterialIndex)
    {
        var hpState = NormalizeHpState(props.HpState);
        var mud = props.MudLevel ?? overlay.MudStrength ?? 0f;
        var snow = props.SnowLevel ?? overlay.SnowStrength ?? 0f;

        return new AssetVisualProperties(
            hpState,
            Clamp01(mud),
            Clamp01(snow),
            diffuseOverride,
            colorMaterialIndex);
    }

    private static StockpileSelection? CreateStockpileSelection(SceneAssetProperties props)
    {
        if (props.Stockpile == null || string.IsNullOrWhiteSpace(props.Stockpile.Item))
            return null;
        return new StockpileSelection(props.Stockpile.Item!, props.Stockpile.Quantity ?? 0);
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static string NormalizeHpState(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "damaged" => "damaged",
            "critical" => "critical",
            _ => "normal"
        };
    }

    private static FTransform CreateOffsetTransform(SceneTransformOffset? offset)
    {
        if (offset == null)
            return FTransform.Identity;

        var translation = offset.Translation != null && offset.Translation.Length >= 3
            ? new FVector(offset.Translation[0], offset.Translation[1], offset.Translation[2])
            : FVector.ZeroVector;

        var rotationEuler = offset.Rotation != null && offset.Rotation.Length >= 3
            ? new FRotator(offset.Rotation[0], offset.Rotation[1], offset.Rotation[2])
            : FRotator.ZeroRotator;

        var scale = offset.Scale != null && offset.Scale.Length >= 3
            ? new FVector(offset.Scale[0], offset.Scale[1], offset.Scale[2])
            : FVector.OneVector;

        return new FTransform(rotationEuler.Quaternion(), translation, scale);
    }

    private static FTransform CombineTransforms(FTransform baseTransform, FTransform componentTransform)
    {
        var translation = componentTransform.Translation + baseTransform.Translation;
        var rotation = componentTransform.Rotation * baseTransform.Rotation;
        var scale = componentTransform.Scale3D;
        return new FTransform(rotation, translation, scale);
    }

    private static bool TryLoadStaticMesh(DefaultFileProvider provider, string path, bool verbose, out UStaticMesh mesh)
    {
        mesh = null!;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            mesh = provider.LoadPackageObject<UStaticMesh>(path);
            return mesh != null;
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[resolver] Failed to load static mesh '{path}': {ex.Message}");
            return false;
        }
    }
}

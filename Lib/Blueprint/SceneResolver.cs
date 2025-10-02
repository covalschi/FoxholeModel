using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
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
        var (resolvedRoot, blueprintAttachments) = ResolveRoot(provider, rootSpec, verbose);
        var attachments = new List<ResolvedAttachmentDescriptor>();
        if (blueprintAttachments.Count > 0)
            attachments.AddRange(blueprintAttachments);

        var explicitAttachments = ResolveAttachments(provider, scene, assetsById, verbose);
        if (explicitAttachments.Count > 0)
            attachments.AddRange(explicitAttachments);

        return new ResolvedScene(resolvedRoot, attachments);
    }

    private static (ResolvedRootAsset Root, IReadOnlyList<ResolvedAttachmentDescriptor> FromBlueprint) ResolveRoot(DefaultFileProvider provider, SceneAsset rootSpec, bool verbose)
    {
        var rootResult = BlueprintMeshResolver.ResolveRootMesh(provider, rootSpec, verbose);
        var visual = CreateVisualProperties(rootSpec, rootResult.Overlay, null, rootSpec.Properties?.ColorMaterialIndex);

        var root = new ResolvedRootAsset(rootResult.Asset, visual, rootResult.SourcePath, rootResult.MetadataPath, rootResult.Overlay);

        var fromBlueprint = BuildAttachmentsFromComponents(rootResult, rootSpec, provider, visual, verbose);
        return (root, fromBlueprint);
    }

    private static IReadOnlyList<ResolvedAttachmentDescriptor> BuildAttachmentsFromComponents(
        BlueprintMeshResolver.RootMeshResult rootResult,
        SceneAsset rootSpec,
        DefaultFileProvider provider,
        AssetVisualProperties visual,
        bool verbose)
    {
        var list = new List<ResolvedAttachmentDescriptor>();
        if (rootResult.Components == null || rootResult.Components.Count == 0)
            return list;

        var rootPath = rootResult.Asset?.GetPathName();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in rootResult.Components)
        {
            if (c?.Asset == null)
                continue;

            // Only static meshes are currently supported as attachments in the renderer.
            var key = string.Empty;
            try { key = c.Asset.GetPathName(); } catch { }
            if (!string.IsNullOrEmpty(key))
            {
                // Skip duplicate meshes and avoid double-rendering the chosen root asset.
                if (!seen.Add(key))
                    continue;
                if (!string.IsNullOrEmpty(rootPath) && string.Equals(rootPath, key, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var transform = c.Transform;

            // Only static and skeletal meshes are supported as attachments
            if (c.Asset is not CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh
                && c.Asset is not CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh)
            {
                continue;
            }

            var attachment = new ResolvedAttachmentDescriptor(
                AssetId: c.Name ?? "<component>",
                Asset: c.Asset,
                Transform: transform,
                Visual: visual,
                Stockpile: null,
                StockpileOptions: Array.Empty<(string, int)>(),
                Overlay: rootResult.Overlay,
                MaterialOverrides: c.Overrides);

            list.Add(attachment);
        }

        // Supplement: include meshes referenced directly on the default object by well-known property names
        var def = rootResult.DefaultObject;
        if (def != null)
        {
            foreach (var prop in new[] { "SkelMeshComponent", "SkeletalMeshComponent", "MeshComponent", "GunMeshComponent", "SkelMesh", "FlagMesh", "GunMesh", "PrimaryMesh" })
            {
                try
                {
                    if (!def.TryGetValue(out FPackageIndex idx, prop) || idx.IsNull)
                        continue;

                    var loaded = idx.Load<UObject>();
                    if (loaded == null)
                        continue;

                    UObject assetToAttach = null;
                    IReadOnlyList<UMaterialInterface>? overrides = null;
                    FTransform xform = FTransform.Identity;
                    switch (loaded)
                    {
                        case CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh sk:
                            assetToAttach = sk;
                            break;
                        case CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh.USkeletalMeshComponent skc:
                            var smIdx = skc.GetSkeletalMesh();
                            if (!smIdx.IsNull)
                            {
                                var smLoaded = smIdx.Load<CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh>();
                                if (smLoaded != null)
                                {
                                    assetToAttach = smLoaded;
                                    overrides = ExtractOverrides(skc);
                                    xform = skc.GetRelativeTransform();
                                }
                            }
                            break;
                        case CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh st:
                            assetToAttach = st;
                            break;
                        case CUE4Parse.UE4.Assets.Exports.Component.StaticMesh.UStaticMeshComponent smc:
                            var stLoaded = smc.GetLoadedStaticMesh();
                            if (stLoaded != null)
                            {
                                assetToAttach = stLoaded;
                                overrides = ExtractOverrides(smc);
                                xform = smc.GetRelativeTransform();
                            }
                            break;
                    }

                    if (assetToAttach == null)
                        continue;

                    // Heuristic: if we only found a raw mesh but no component, search the default object
                    // for a component that references the same mesh to recover overrides + transform.
                    if (verbose && overrides != null)
                    {
                        Console.WriteLine($"[resolver] component overrides count={overrides.Count} for prop '{prop}'");
                    }

                    if (overrides == null && def != null && assetToAttach is UObject meshObj)
                    {
                        if (TryFindReferencingComponent(def, meshObj, out var comp))
                        {
                            overrides = ExtractOverrides(comp);
                            if (comp is CUE4Parse.UE4.Assets.Exports.Component.USceneComponent sc)
                                xform = sc.GetRelativeTransform();
                            if (verbose)
                            {
                                var count = overrides?.Count ?? 0;
                                Console.WriteLine($"[resolver] recovered component overrides count={count} via scan for prop '{prop}'");
                            }
                        }
                    }

                    var path = string.Empty;
                    try { path = assetToAttach.GetPathName(); } catch { }
                    if (!string.IsNullOrEmpty(path) && (!seen.Add(path) || (rootPath?.Equals(path, StringComparison.OrdinalIgnoreCase) ?? false)))
                        continue;

                    var attachment = new ResolvedAttachmentDescriptor(
                        AssetId: prop,
                        Asset: assetToAttach,
                        Transform: xform,
                        Visual: visual,
                        Stockpile: null,
                        StockpileOptions: Array.Empty<(string, int)>(),
                        Overlay: rootResult.Overlay,
                        MaterialOverrides: overrides);
                    list.Add(attachment);
                    if (verbose)
                        Console.WriteLine($"[resolver] default-object prop '{prop}' -> attachment {path}");
                }
                catch (Exception ex)
                {
                    if (verbose)
                        Console.Error.WriteLine($"[resolver] failed to read default-object prop '{prop}': {ex.Message}");
                }
            }
        }

        static IReadOnlyList<UMaterialInterface>? ExtractOverrides(UObject component)
        {
            try
            {
                if (component.TryGetValue(out FPackageIndex[] materialIdxs, "OverrideMaterials") && materialIdxs is { Length: > 0 })
                {
                    var list = new List<UMaterialInterface>(materialIdxs.Length);
                    foreach (var idx in materialIdxs)
                    {
                        if (idx.IsNull) { list.Add(null); continue; }
                        if (idx.Load<UMaterialInterface>() is { } mat)
                            list.Add(mat);
                        else list.Add(null);
                    }
                    return list;
                }

                // Fallback: 'Materials'
                if (component.TryGetValue(out FPackageIndex[] materials, "Materials") && materials is { Length: > 0 })
                {
                    var list = new List<UMaterialInterface>(materials.Length);
                    foreach (var idx in materials)
                    {
                        if (idx.IsNull) { list.Add(null); continue; }
                        if (idx.Load<UMaterialInterface>() is { } mat)
                            list.Add(mat);
                        else list.Add(null);
                    }
                    return list;
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        static bool TryFindReferencingComponent(UObject owner, UObject targetMesh, out UObject component)
        {
            component = null!;
            try
            {
                foreach (var prop in owner.Properties)
                {
                    if (prop?.Tag is ObjectProperty objProp)
                    {
                        var child = objProp.Value?.Load<UObject>();
                        if (child == null) continue;

                        if (child is CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh.USkeletalMeshComponent skc)
                        {
                            var idx = skc.GetSkeletalMesh();
                            if (!idx.IsNull && ReferenceEquals(idx.Load<UObject>(), targetMesh))
                            {
                                component = child;
                                return true;
                            }
                        }
                        else if (child is CUE4Parse.UE4.Assets.Exports.Component.StaticMesh.UStaticMeshComponent smc)
                        {
                            var st = smc.GetLoadedStaticMesh();
                            if (st != null && ReferenceEquals(st, targetMesh))
                            {
                                component = child;
                                return true;
                            }
                        }
                    }
                    else if (prop?.Tag is ArrayProperty arrProp)
                    {
                        foreach (var elem in arrProp.Value.Properties)
                        {
                            if (elem is ObjectProperty arrObj)
                            {
                                var child = arrObj.Value?.Load<UObject>();
                                if (child == null) continue;
                                if (child is CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh.USkeletalMeshComponent skc)
                                {
                                    var idx = skc.GetSkeletalMesh();
                                    if (!idx.IsNull && ReferenceEquals(idx.Load<UObject>(), targetMesh))
                                    {
                                        component = child;
                                        return true;
                                    }
                                }
                                else if (child is CUE4Parse.UE4.Assets.Exports.Component.StaticMesh.UStaticMeshComponent smc)
                                {
                                    var st = smc.GetLoadedStaticMesh();
                                    if (st != null && ReferenceEquals(st, targetMesh))
                                    {
                                        component = child;
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        if (verbose)
        {
            Console.WriteLine($"[resolver] blueprint components -> {list.Count} static mesh attachment(s)");
        }

        return list;
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
                else if (TryLoadSkeletalMesh(provider, asset.Path, verbose, out var fallbackSkeletal))
                {
                    var snooperTransform = CombineTransforms(baseTransform, FTransform.Identity);
                    results.Add(new ResolvedAttachmentDescriptor(asset.Id, fallbackSkeletal, snooperTransform, visual, stockpile, meshResult.StockpileOptions, meshResult.Overlay));
                }
                else if (verbose)
                {
                    Console.Error.WriteLine($"[resolver] No mesh available for attachment '{asset.Id}'.");
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
        }
        return false;
    }

    private static bool TryLoadSkeletalMesh(DefaultFileProvider provider, string path, bool verbose, out CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh mesh)
    {
        mesh = null!;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var normalized = BlueprintResolver.NormalizeObjectPath(path);
            var loaded = provider.LoadPackageObject<CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh>(normalized);
            if (loaded != null)
            {
                mesh = loaded;
                return true;
            }
        }
        catch
        {
            if (verbose)
                Console.Error.WriteLine($"[resolver] Failed to load skeletal mesh '{path}'.");
        }

        return false;
    }
}

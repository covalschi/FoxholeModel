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
using FModelHeadless.Lib.Common;
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
        var (resolvedRoot, blueprintAttachments) = ResolveRoot(provider, rootSpec, scene.Filters, verbose);
        var attachments = new List<ResolvedAttachmentDescriptor>();
        if (blueprintAttachments.Count > 0)
            attachments.AddRange(blueprintAttachments);

        var explicitAttachments = ResolveAttachments(provider, scene, assetsById, verbose);
        if (explicitAttachments.Count > 0)
            attachments.AddRange(explicitAttachments);

        return new ResolvedScene(resolvedRoot, attachments);
    }

    private static (ResolvedRootAsset Root, IReadOnlyList<ResolvedAttachmentDescriptor> FromBlueprint) ResolveRoot(DefaultFileProvider provider, SceneAsset rootSpec, SceneFilters? filters, bool verbose)
    {
        var rootResult = BlueprintMeshResolver.ResolveRootMesh(provider, rootSpec, verbose);
        var visual = CreateVisualProperties(rootSpec, rootResult.Overlay, null, rootSpec.Properties?.ColorMaterialIndex);

        var root = new ResolvedRootAsset(rootResult.Asset, visual, rootResult.SourcePath, rootResult.MetadataPath, rootResult.Overlay);

        var fromBlueprint = BuildAttachmentsFromComponents(rootResult, rootSpec, provider, visual, filters, verbose);
        return (root, fromBlueprint);
    }

    private static IReadOnlyList<ResolvedAttachmentDescriptor> BuildAttachmentsFromComponents(
        BlueprintMeshResolver.RootMeshResult rootResult,
        SceneAsset rootSpec,
        DefaultFileProvider provider,
        AssetVisualProperties visual,
        SceneFilters? filters,
        bool verbose)
    {
        var list = new List<ResolvedAttachmentDescriptor>();
        if (rootResult.Components == null || rootResult.Components.Count == 0)
            return list;

        var rootPath = rootResult.Asset?.GetPathName();
        int? teamPref = ParseTeamPreference(rootSpec.Properties?.Team);

        foreach (var c in rootResult.Components)
        {
            if (!KeepByFilters(c, filters, verbose))
                continue;
            if (c?.Asset == null)
                continue;

            // Only static meshes are currently supported as attachments in the renderer.
            // Avoid double-rendering the chosen primary asset itself, but allow multiple
            // instances of the same mesh path (transforms differ around the fort walls).
            try
            {
                var key = c.Asset.GetPathName();
                if (!string.IsNullOrEmpty(rootPath) && string.Equals(rootPath, key, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            catch { }

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
                                    overrides = MaterialOverrideReader.ReadOverrides(skc);
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
                                overrides = MaterialOverrideReader.ReadOverrides(smc);
                                xform = smc.GetRelativeTransform();
                            }
                            break;
                        default:
                            // Handle custom components that behave like StaticMeshComponent (e.g., TeamFlagMeshComponent)
                            try
                            {
                                var typeName = loaded.ExportType ?? string.Empty;
                                if (typeName.IndexOf("FlagMeshComponent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    typeName.IndexOf("TeamFlagMeshComponent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    typeName.IndexOf("StaticMeshComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    // Try common mesh fields in order of precedence
                                    CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh meshRef = null;
                                    if (loaded.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex smIdx2, "StaticMesh") && !smIdx2.IsNull)
                                        meshRef = smIdx2.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>();
                                    if (meshRef == null && teamPref == 0 && loaded.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex t0, "Team0Mesh") && !t0.IsNull)
                                        meshRef = t0.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>();
                                    if (meshRef == null && teamPref == 1 && loaded.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex t1, "Team1Mesh") && !t1.IsNull)
                                        meshRef = t1.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>();
                                    if (meshRef == null && loaded.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex team0, "Team0Mesh") && !team0.IsNull)
                                        meshRef = team0.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>();
                                    if (meshRef == null && loaded.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex team1, "Team1Mesh") && !team1.IsNull)
                                        meshRef = team1.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>();

                                    if (meshRef != null)
                                    {
                                        assetToAttach = meshRef;
                                        overrides = MaterialOverrideReader.ReadOverrides(loaded);
                                        // Compute world transform by walking AttachParent chain and optional socket
                                        xform = ComputeWorldTransformFromComponent(provider, loaded, verbose);
                                    }
                                }
                            }
                            catch { }
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
                            overrides = MaterialOverrideReader.ReadOverrides(comp);
                            if (comp is CUE4Parse.UE4.Assets.Exports.Component.USceneComponent sc)
                                xform = sc.GetRelativeTransform();
                            else
                                xform = TransformUtil.TryGetRelativeTransformFallback(comp);
                            if (verbose)
                            {
                                var count = overrides?.Count ?? 0;
                                Console.WriteLine($"[resolver] recovered component overrides count={count} via scan for prop '{prop}'");
                            }
                        }
                    }

                    var path = string.Empty;
                    try { path = assetToAttach.GetPathName(); } catch { }
                    if (!string.IsNullOrEmpty(path) && (rootPath?.Equals(path, StringComparison.OrdinalIgnoreCase) ?? false))
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

        // Fallback pass: some blueprints expose the flag only as a component on the CDO,
        // without a direct mesh property. Scan for any *FlagMeshComponent and attach it using
        // its computed world transform.
        if (def != null)
        {
            try
            {
                foreach (var prop in def.Properties)
                {
                    if (prop?.Tag is ObjectProperty objProp)
                    {
                        var child = objProp.Value?.Load<UObject>();
                        if (child == null) continue;
                        var typeName = child.ExportType ?? string.Empty;
                        if (typeName.IndexOf("FlagMeshComponent", StringComparison.OrdinalIgnoreCase) < 0 &&
                            typeName.IndexOf("TeamFlagMeshComponent", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        // Resolve mesh choice (Team0/Team1/StaticMesh)
                        CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh meshRef = null;
                        try
                        {
                            if (child.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex smIdx, "StaticMesh") && !smIdx.IsNull)
                                meshRef = smIdx.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>();
                            if (meshRef == null && child.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex t0, "Team0Mesh") && !t0.IsNull)
                                meshRef = t0.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>();
                            if (meshRef == null && child.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex t1, "Team1Mesh") && !t1.IsNull)
                                meshRef = t1.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>();
                        }
                        catch { }

                        if (meshRef == null) continue;

                        var meshPath = string.Empty;
                        try { meshPath = meshRef.GetPathName(); } catch { }

                        // Deduplicate if we already added the same mesh path from earlier passes
                        var already = list.Any(a =>
                        {
                            try { return a.Asset.GetPathName().Equals(meshPath, StringComparison.OrdinalIgnoreCase); }
                            catch { return false; }
                        });
                        if (already) continue;

                        var worldXform = ComputeWorldTransformFromComponent(provider, child, verbose);
                        var overrides = MaterialOverrideReader.ReadOverrides(child);
                        var attach = new ResolvedAttachmentDescriptor(
                            AssetId: "FlagMeshComponent",
                            Asset: meshRef,
                            Transform: worldXform,
                            Visual: visual,
                            Stockpile: null,
                            StockpileOptions: Array.Empty<(string, int)>(),
                            Overlay: rootResult.Overlay,
                            MaterialOverrides: overrides);
                        list.Add(attach);
                        if (verbose)
                            Console.WriteLine($"[resolver] recovered flag component -> attachment {meshPath}");
                    }
                }
            }
            catch { }
        }

        // (extract overrides moved to MaterialOverrideReader)

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
                        else
                        {
                            // Generic fallback: look for a StaticMesh/Team0Mesh/Team1Mesh property referencing targetMesh
                            try
                            {
                                if (child.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex smIdx2, "StaticMesh") &&
                                    !smIdx2.IsNull && ReferenceEquals(smIdx2.Load<UObject>(), targetMesh))
                                { component = child; return true; }
                                if (child.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex t0, "Team0Mesh") &&
                                    !t0.IsNull && ReferenceEquals(t0.Load<UObject>(), targetMesh))
                                { component = child; return true; }
                                if (child.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex t1, "Team1Mesh") &&
                                    !t1.IsNull && ReferenceEquals(t1.Load<UObject>(), targetMesh))
                                { component = child; return true; }
                            }
                            catch { }
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

    // Compose world transform by walking a component's AttachParent chain.
    // Also applies socket offsets when a child specifies AttachSocketName on a mesh parent.
    private static FTransform ComputeWorldTransformFromComponent(DefaultFileProvider provider, UObject component, bool verbose)
    {
        var world = TryGetRelativeTransformFallback(component);

        // Track to prevent cycles
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        UObject current = component;
        for (int depth = 0; depth < 32; depth++)
        {
            try
            {
                // Find parent scene component
                if (!current.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex parentIdx, "AttachParent") || parentIdx.IsNull)
                    break;
                var parent = parentIdx.Load<UObject>();
                if (parent == null)
                    break;

                var parentName = parent.Name ?? string.Empty;
                if (!string.IsNullOrEmpty(parentName))
                {
                    if (!seen.Add(parentName))
                        break;
                }

                // If the child defines an attach socket name, compose the socket transform first
                string socketName = string.Empty;
                try
                {
                    if (current.TryGetValue(out string socket, "AttachSocketName") && !string.IsNullOrWhiteSpace(socket))
                        socketName = socket;
                    else if (current.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FName socketF, "AttachSocketName"))
                        socketName = socketF.Text;
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(socketName))
                {
                    var parentMeshPath = TryGetMeshPathFromComponent(provider, parent, verbose);
                    if (!string.IsNullOrEmpty(parentMeshPath) && FModelHeadless.Lib.Common.MeshSocketUtil.TryGetParentSocketTransform(provider, parentMeshPath, socketName, verbose, out var socketXform))
                    {
                        world = world * socketXform;
                    }
                }

                // Then compose the parent's local transform
                var parentLocal = TryGetRelativeTransformFallback(parent);
                world = world * parentLocal;

                current = parent;
            }
            catch { break; }
        }

        return world;
    }

    private static string TryGetMeshPathFromComponent(DefaultFileProvider provider, UObject comp, bool verbose)
    {
        try
        {
            switch (comp)
            {
                case CUE4Parse.UE4.Assets.Exports.Component.StaticMesh.UStaticMeshComponent smc:
                    var st = smc.GetLoadedStaticMesh();
                    return st?.GetPathName() ?? string.Empty;
                case CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh.USkeletalMeshComponent skc:
                    var idx = skc.GetSkeletalMesh();
                    if (!idx.IsNull)
                    {
                        var sk = idx.Load<CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh>();
                        return sk?.GetPathName() ?? string.Empty;
                    }
                    break;
                default:
                    // Generic fallback fields
                    if (comp.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex smIdx, "StaticMesh") && !smIdx.IsNull)
                        return smIdx.Load<CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh>()?.GetPathName() ?? string.Empty;
                    if (comp.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex skIdx, "SkeletalMesh") && !skIdx.IsNull)
                        return skIdx.Load<CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh>()?.GetPathName() ?? string.Empty;
                    break;
            }
        }
        catch { }
        return string.Empty;
    }

    private static bool KeepByFilters(BlueprintSceneBuilder.BlueprintComponent c, SceneFilters? filters, bool verbose)
    {
        if (filters == null) return true;

        string path = string.Empty;
        try { path = c.Asset.GetPathName(); } catch { }
        var tags = c.Tags != null ? new List<string>(c.Tags).ToArray() : Array.Empty<string>();

        if (filters.IncludePathContains is { Length: > 0 })
        {
            if (!FilterUtil.AnyTokenMatch(path, null, filters.IncludePathContains))
                return false;
        }
        if (filters.ExcludePathContains is { Length: > 0 })
        {
            if (FilterUtil.AnyTokenMatch(path, null, filters.ExcludePathContains))
                return false;
        }
        if (filters.IncludeTags is { Length: > 0 })
        {
            var any = false;
            foreach (var t in filters.IncludeTags)
                if (!string.IsNullOrWhiteSpace(t) && Array.Exists<string>(tags, x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
                { any = true; break; }
            if (!any) return false;
        }
        if (filters.ExcludeTags is { Length: > 0 })
        {
            foreach (var t in filters.ExcludeTags)
                if (!string.IsNullOrWhiteSpace(t) && Array.Exists<string>(tags, x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
                    return false;
        }
        return true;
    }

    private static FTransform TryGetRelativeTransformFallback(UObject obj) => TransformUtil.TryGetRelativeTransformFallback(obj);

    private static int? ParseTeamPreference(string? team)
    {
        if (string.IsNullOrWhiteSpace(team)) return null;
        var t = team.Trim().ToLowerInvariant();
        if (t == "0" || t == "team0" || t == "colonial") return 0;
        if (t == "1" || t == "team1" || t == "warden") return 1;
        return null;
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
            var socketTransform = FTransform.Identity;

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

                // Optional socket-based placement relative to parent
                if (!string.IsNullOrWhiteSpace(attachRef.Socket))
                {
                    if (!FModelHeadless.Lib.Common.MeshSocketUtil.TryGetParentSocketTransform(provider, parentMetadataPath, attachRef.Socket!, verbose, out socketTransform))
                    {
                        if (verbose)
                            Console.Error.WriteLine($"[resolver] Socket '{attachRef.Socket}' not found on '{parentMetadataPath}'");
                        socketTransform = FTransform.Identity;
                    }
                }
            }

            var offsetTransform = CreateOffsetTransform(attachRef.Offset);
            var baseTransform = anchorTransform;
            if (!string.IsNullOrWhiteSpace(attachRef.Socket))
                baseTransform = TransformUtil.Combine(baseTransform, socketTransform);
            baseTransform = TransformUtil.Combine(baseTransform, offsetTransform);

            var props = asset.Properties ?? new SceneAssetProperties();
            var meshResult = BlueprintMeshResolver.ResolveAttachmentMeshes(provider, asset, asset.MetadataPath ?? asset.Path, NormalizeHpState(props.HpState), props.ColorVariant, verbose);
            var visual = CreateVisualProperties(props, meshResult.Overlay, meshResult.DiffuseOverride, props.ColorMaterialIndex);
            var stockpile = CreateStockpileSelection(props);

            if (meshResult.Meshes.Count == 0)
            {
                if (TryLoadStaticMesh(provider, asset.Path, verbose, out var fallbackMesh))
                {
                    var snooperTransform = TransformUtil.Combine(baseTransform, FTransform.Identity);
                    results.Add(new ResolvedAttachmentDescriptor(asset.Id, fallbackMesh, snooperTransform, visual, stockpile, meshResult.StockpileOptions, meshResult.Overlay));
                }
                else if (TryLoadSkeletalMesh(provider, asset.Path, verbose, out var fallbackSkeletal))
                {
                    var snooperTransform = TransformUtil.Combine(baseTransform, FTransform.Identity);
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
                var combined = TransformUtil.Combine(baseTransform, meshInfo.Transform);
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

    // Transform combine moved to TransformUtil; socket transform moved to MeshSocketUtil

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

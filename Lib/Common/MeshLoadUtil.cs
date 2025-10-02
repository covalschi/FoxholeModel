using System;
using System.Collections.Generic;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;

namespace FModelHeadless.Lib.Common;

internal static class MeshLoadUtil
{
    public static bool TryLoadStaticMesh(DefaultFileProvider provider, string rawPath, bool verbose, out UStaticMesh mesh)
    {
        mesh = null!;
        foreach (var candidate in ExpandMeshCandidates(rawPath))
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

    public static bool TryLoadSkeletalMesh(DefaultFileProvider provider, string rawPath, bool verbose, out USkeletalMesh mesh)
    {
        mesh = null!;
        var normalized = FModelHeadless.Lib.Blueprint.BlueprintResolver.NormalizeObjectPath(rawPath);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        try
        {
            var loaded = provider.LoadPackageObject<USkeletalMesh>(normalized);
            if (loaded != null)
            {
                mesh = loaded;
                return true;
            }
        }
        catch
        {
            if (verbose)
                Console.Error.WriteLine($"[resolver] Failed to load skeletal mesh '{normalized}'.");
        }
        return false;
    }

    public static string TryGetMeshPathFromComponent(UObject comp)
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
                        var sk = idx.Load<USkeletalMesh>();
                        return sk?.GetPathName() ?? string.Empty;
                    }
                    break;
                default:
                    if (comp.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex smIdx, "StaticMesh") && !smIdx.IsNull)
                        return smIdx.Load<UStaticMesh>()?.GetPathName() ?? string.Empty;
                    if (comp.TryGetValue(out CUE4Parse.UE4.Objects.UObject.FPackageIndex skIdx, "SkeletalMesh") && !skIdx.IsNull)
                        return skIdx.Load<USkeletalMesh>()?.GetPathName() ?? string.Empty;
                    break;
            }
        }
        catch { }
        return string.Empty;
    }

    private static IEnumerable<string> ExpandMeshCandidates(string rawPath)
    {
        var normalized = FModelHeadless.Lib.Blueprint.BlueprintResolver.NormalizeObjectPath(rawPath);
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


using System;
using System.Collections.Generic;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Objects.UObject;

namespace FModelHeadless.Lib.Common;

internal static class MaterialOverrideReader
{
    public static IReadOnlyList<UMaterialInterface>? ReadOverrides(CUE4Parse.UE4.Assets.Exports.UObject component)
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
        catch { }

        return null;
    }
}

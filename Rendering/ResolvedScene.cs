using System.Collections.Generic;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;

namespace FModelHeadless.Rendering;

internal sealed record ResolvedScene(
    ResolvedRootAsset Root,
    IReadOnlyList<ResolvedAttachmentDescriptor> Attachments);

internal sealed record ResolvedRootAsset(
    UObject Asset,
    AssetVisualProperties Visual,
    string SourcePath,
    string? MetadataPath,
    OverlayMaskData Overlay);

internal sealed record ResolvedAttachmentDescriptor(
    string AssetId,
    UStaticMesh Mesh,
    FTransform Transform,
    AssetVisualProperties Visual,
    StockpileSelection? Stockpile,
    IReadOnlyList<(string ItemPath, int Quantity)> StockpileOptions,
    OverlayMaskData Overlay);

internal readonly record struct OverlayMaskData(
    UTexture2D? MudMask,
    float? MudStrength,
    float? MudTightness,
    UTexture2D? SnowMask,
    float? SnowStrength,
    float? SnowTightness)
{
    public static readonly OverlayMaskData Empty = new OverlayMaskData(null, null, null, null, null, null);
}

internal sealed record AssetVisualProperties(
    string HpState,
    float MudLevel,
    float SnowLevel,
    System.Numerics.Vector4? DiffuseOverride,
    int? ColorMaterialIndex);

internal sealed record StockpileSelection(string ItemPath, int Quantity);

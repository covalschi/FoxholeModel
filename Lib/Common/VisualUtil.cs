using System.Numerics;
using FModelHeadless.Rendering;
using FModelHeadless.Cli;

namespace FModelHeadless.Lib.Common;

internal static class VisualUtil
{
    public static AssetVisualProperties Create(SceneAssetProperties props, OverlayMaskData overlay, Vector4? diffuseOverride, int? colorMaterialIndex)
    {
        string NormalizeHpState(string? value)
            => value?.Trim().ToLowerInvariant() switch { "damaged" => "damaged", "critical" => "critical", _ => "normal" };

        float Clamp01(float v) => System.Math.Clamp(v, 0f, 1f);

        var hp = NormalizeHpState(props.HpState);
        var mud = props.MudLevel ?? overlay.MudStrength ?? 0f;
        var snow = props.SnowLevel ?? overlay.SnowStrength ?? 0f;
        return new AssetVisualProperties(hp, Clamp01(mud), Clamp01(snow), diffuseOverride, colorMaterialIndex);
    }
}

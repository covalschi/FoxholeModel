using System;
using System.Collections.Generic;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Component.Lights;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace FModelHeadless.Lib.Lighting;

public static class LightingPresetExtractor
{
    public record DirectionalLightInfo(
        string Name,
        FVector Location,
        FRotator Rotation,
        float Intensity,
        bool UseTemperature,
        float Temperature,
        FLinearColor Color,
        bool UsedAsAtmosphereSun,
        string? LightFunctionPath,
        FVector? LightFunctionScale,
        float BloomScale);

    public record SkyLightInfo(
        string Name,
        FVector Location,
        float Intensity,
        bool LowerHemisphereIsBlack,
        float SkyDistanceThreshold,
        float OcclusionMaxDistance,
        float OcclusionExponent,
        FLinearColor? OcclusionTint,
        string? CubemapPath);

    public record CubeMapInfo(
        string? CubeMapPath,
        float WhiteBalanceTemp,
        FLinearColor FilmTint,
        FLinearColor FilmTintShadow,
        float Saturation,
        float Contrast,
        float CrushShadows,
        float CrushHighlights,
        float Toe,
        float DirectionalLightTemperature);

    public record LightingPreset(
        string WorldPath,
        IReadOnlyList<DirectionalLightInfo> DirectionalLights,
        SkyLightInfo? SkyLight,
        CubeMapInfo? CubeMap = null,
        bool IsOutOfVision = false);

    public static bool TryExtract(DefaultFileProvider provider, string worldObjectPath, bool verbose, out LightingPreset preset)
    {
        preset = null!;

        var world = provider.LoadPackageObject<UWorld>(worldObjectPath);
        if (world == null)
        {
            if (verbose) Console.Error.WriteLine($"[lighting] Failed to load world '{worldObjectPath}'.");
            return false;
        }

        var level = world.PersistentLevel.Load<ULevel>();
        if (level == null)
        {
            if (verbose) Console.Error.WriteLine($"[lighting] World '{worldObjectPath}' has no persistent level.");
            return false;
        }

        var directional = new List<DirectionalLightInfo>();
        SkyLightInfo? sky = null;

        foreach (var indexNullable in level.Actors ?? Array.Empty<FPackageIndex?>())
        {
            if (indexNullable is not { } actorIndex || actorIndex.IsNull) continue;
            if (!actorIndex.TryLoad(out UObject actor)) continue;

            var actorType = actor.ExportType;
            if (string.Equals(actorType, "DirectionalLight", StringComparison.OrdinalIgnoreCase))
            {
                var component = LoadComponent<UDirectionalLightComponent>(actor, "RootComponent")
                               ?? LoadComponent<UDirectionalLightComponent>(actor, "LightComponent");
                if (component != null)
                {
                    directional.Add(CreateDirectionalInfo(actor.Name, component));
                }
            }
            else if (string.Equals(actorType, "SkyLight", StringComparison.OrdinalIgnoreCase) && sky == null)
            {
                var component = LoadComponent<USkyLightComponent>(actor, "LightComponent")
                               ?? LoadComponent<USkyLightComponent>(actor, "RootComponent");
                if (component != null)
                {
                    sky = CreateSkyInfo(actor.Name, component);
                }
            }
        }

        preset = new LightingPreset(worldObjectPath, directional, sky);
        return directional.Count > 0 || sky != null;
    }

    private static TComponent? LoadComponent<TComponent>(UObject actor, string propertyName) where TComponent : UObject
    {
        if (actor.TryGetValue(out FPackageIndex index, propertyName) && !index.IsNull)
        {
            return index.Load<TComponent>();
        }

        var lazy = actor.GetOrDefaultLazy<TComponent>(propertyName);
        return lazy?.Value;
    }

    private static DirectionalLightInfo CreateDirectionalInfo(string name, UDirectionalLightComponent component)
    {
        var transform = component.GetRelativeTransform();
        var rotation = transform.Rotator();
        var location = transform.Translation;

        var intensity = component.GetOrDefault("Intensity", 1.0f);
        var useTemperature = component.GetOrDefault("bUseTemperature", false);
        var temperature = component.GetOrDefault("Temperature", 6500f);
        var color = ToLinearColor(component.GetOrDefault("LightColor", new FColor(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
        var usedAsAtmosphereSun = component.GetOrDefault("bUsedAsAtmosphereSunLight", false);

        string? lightFunctionPath = null;
        if (component.TryGetValue(out FPackageIndex functionIndex, "LightFunctionMaterial") && !functionIndex.IsNull)
        {
            lightFunctionPath = functionIndex.ToString();
        }

        var lightFunctionScale = component.TryGetValue(out FVector scale, "LightFunctionScale") ? scale : (FVector?)null;
        var bloomScale = component.GetOrDefault("BloomScale", 1.0f);

        return new DirectionalLightInfo(
            name,
            location,
            rotation,
            intensity,
            useTemperature,
            temperature,
            color,
            usedAsAtmosphereSun,
            lightFunctionPath,
            lightFunctionScale,
            bloomScale);
    }

    private static SkyLightInfo CreateSkyInfo(string name, USkyLightComponent component)
    {
        var transform = component.GetRelativeTransform();
        var location = transform.Translation;
        var intensity = component.GetOrDefault("Intensity", 1.0f);
        var lowerHemisphere = component.GetOrDefault("bLowerHemisphereIsBlack", true);
        var skyDistance = component.GetOrDefault("SkyDistanceThreshold", 150000f);
        var occlusionMaxDistance = component.GetOrDefault("OcclusionMaxDistance", 0f);
        var occlusionExponent = component.GetOrDefault("OcclusionExponent", 1f);
        var occlusionTint = component.TryGetValue(out FLinearColor tint, "OcclusionTint") ? tint : (FLinearColor?)null;

        string? cubemapPath = null;
        if (component.TryGetValue(out FPackageIndex cubemapIndex, "Cubemap") && !cubemapIndex.IsNull)
        {
            cubemapPath = cubemapIndex.ToString();
        }

        return new SkyLightInfo(
            name,
            location,
            intensity,
            lowerHemisphere,
            skyDistance,
            occlusionMaxDistance,
            occlusionExponent,
            occlusionTint,
            cubemapPath);
    }

    private static FLinearColor ToLinearColor(FColor color)
    {
        const float inv = 1f / 255f;
        return new FLinearColor(color.R * inv, color.G * inv, color.B * inv, color.A * inv);
    }
}

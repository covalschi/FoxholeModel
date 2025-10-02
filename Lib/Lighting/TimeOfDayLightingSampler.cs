using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Component.Lights;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.Engine.Curves;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.Utils;

namespace FModelHeadless.Lib.Lighting;

public static class TimeOfDayLightingSampler
{
    public static IReadOnlyDictionary<float, LightingPresetExtractor.LightingPreset> Sample(DefaultFileProvider provider, string worldObjectPath, IEnumerable<float> hours, bool verbose = false)
    {
        var hourList = hours.Distinct().ToList();
        var result = new Dictionary<float, LightingPresetExtractor.LightingPreset>();

        if (hourList.Count == 0)
            return result;

        var world = provider.LoadPackageObject<UWorld>(worldObjectPath);
        if (world == null)
        {
            if (verbose) Console.Error.WriteLine($"[lighting] Failed to load world '{worldObjectPath}'.");
            return result;
        }

        var level = world.PersistentLevel.Load<ULevel>();
        if (level == null)
        {
            if (verbose) Console.Error.WriteLine($"[lighting] World '{worldObjectPath}' has no persistent level.");
            return result;
        }

        var manager = FindDayNightManager(level);
        if (manager == null)
        {
            if (verbose) Console.Error.WriteLine($"[lighting] No DayNightCycleManager found in '{worldObjectPath}'. Using static preset.");
            if (LightingPresetExtractor.TryExtract(provider, worldObjectPath, verbose, out var fallback))
            {
                foreach (var hour in hourList)
                {
                    result[hour] = fallback;
                }
            }
            return result;
        }

        // Load referenced actors/components
        var sunComponent = LoadDirectionalComponent(manager, "LightSource");
        var moonComponent = LoadDirectionalComponent(manager, "MoonLightSource");
        var skyComponent = LoadSkyComponent(manager, "SkyLight");

        if (sunComponent == null && moonComponent == null)
        {
            if (verbose) Console.Error.WriteLine($"[lighting] Unable to resolve directional lights for '{worldObjectPath}'.");
            return result;
        }

        // Load curves
        var sunColorCurve = LoadLinearColorCurve(manager, "DirectionalLightColorCurve");
        var moonColorCurve = LoadLinearColorCurve(manager, "MoonLightColorCurve");
        var moonIntensityCurve = LoadFloatCurve(manager, "MoonLightIntensityCurve");
        var moonAngleCurve = LoadFloatCurve(manager, "MoonLightAngleCurve");
        var skylightCurve = LoadFloatCurve(manager, "SkyLightIntensity");
        var visibilityCurve = LoadFloatCurve(manager, "VisibilityCurve") ?? LoadFloatCurve(manager, "VisibilityRadiusIntensityCurve");

        var cubeMapSamples = GetCubeMapSamples(manager);
        var lightDayIntensity = manager.GetOrDefault("LightDayIntensity", 7.0f);
        var sunAngleYaw = manager.GetOrDefault("SunAngle", sunComponent?.GetRelativeTransform().Rotator().Yaw ?? 270f);

        foreach (var hour in hourList)
        {
            var sunHeight = ComputeSunHeight(hour);
            var cubeInfo = SelectCubeMapSample(cubeMapSamples, sunHeight);

            var directionalLights = new List<LightingPresetExtractor.DirectionalLightInfo>();

            if (sunComponent != null)
            {
                var sunInfo = BuildSunInfo(sunComponent, sunColorCurve, cubeInfo, sunAngleYaw, lightDayIntensity, sunHeight);
                directionalLights.Add(sunInfo);
            }

            if (moonComponent != null)
            {
                var moonInfo = BuildMoonInfo(moonComponent, moonColorCurve, moonIntensityCurve, moonAngleCurve, cubeInfo, sunHeight);
                directionalLights.Add(moonInfo);
            }

            LightingPresetExtractor.SkyLightInfo? skyInfo = null;
            if (skyComponent != null)
            {
                skyInfo = BuildSkyInfo(skyComponent, skylightCurve, sunHeight);
            }

            var isOutOfVision = DetermineOutOfVision(visibilityCurve, sunHeight);

            var preset = new LightingPresetExtractor.LightingPreset(
                worldObjectPath,
                directionalLights,
                skyInfo,
                cubeInfo,
                isOutOfVision);

            result[hour] = preset;
        }

        return result;
    }

    private static UObject? FindDayNightManager(ULevel level)
    {
        foreach (var indexNullable in level.Actors ?? Array.Empty<FPackageIndex?>())
        {
            if (indexNullable is not { } actorIndex || actorIndex.IsNull) continue;
            if (!actorIndex.TryLoad(out UObject actor)) continue;
            if (string.Equals(actor.ExportType, "DayNightCycleManager", StringComparison.OrdinalIgnoreCase))
                return actor;
        }
        return null;
    }

    private static UDirectionalLightComponent? LoadDirectionalComponent(UObject manager, string propertyName)
    {
        if (!manager.TryGetValue(out FPackageIndex actorIndex, propertyName) || actorIndex.IsNull)
            return null;
        if (!actorIndex.TryLoad(out UObject actor))
            return null;
        return LoadComponent<UDirectionalLightComponent>(actor, "LightComponent")
               ?? LoadComponent<UDirectionalLightComponent>(actor, "RootComponent");
    }

    private static USkyLightComponent? LoadSkyComponent(UObject manager, string propertyName)
    {
        if (!manager.TryGetValue(out FPackageIndex actorIndex, propertyName) || actorIndex.IsNull)
            return null;
        if (!actorIndex.TryLoad(out UObject actor))
            return null;
        return LoadComponent<USkyLightComponent>(actor, "LightComponent")
               ?? LoadComponent<USkyLightComponent>(actor, "RootComponent");
    }

    private static TComponent? LoadComponent<TComponent>(UObject actor, string propertyName) where TComponent : UObject
    {
        if (actor.TryGetValue(out FPackageIndex index, propertyName) && !index.IsNull)
            return index.Load<TComponent>();
        var lazy = actor.GetOrDefaultLazy<TComponent>(propertyName);
        return lazy?.Value;
    }

    private static UObject? LoadCurveObject(UObject owner, string propertyName)
    {
        if (owner.TryGetValue(out FPackageIndex index, propertyName) && !index.IsNull)
            return index.Load<UObject>();
        var lazy = owner.GetOrDefaultLazy<UObject>(propertyName);
        return lazy?.Value;
    }

    private static FRichCurve? LoadFloatCurve(UObject owner, string propertyName)
    {
        var curveObj = LoadCurveObject(owner, propertyName);
        if (curveObj == null)
            return null;
        if (curveObj.TryGetValue(out FStructFallback fallback, "FloatCurve"))
            return new FRichCurve(fallback);
        return null;
    }

    private static FRichCurve[]? LoadLinearColorCurve(UObject owner, string propertyName)
    {
        var curveObj = LoadCurveObject(owner, propertyName);
        if (curveObj == null)
            return null;
        if (curveObj.TryGetValue(out FStructFallback[] fallbackArray, "FloatCurves") && fallbackArray.Length > 0)
            return fallbackArray.Select(f => new FRichCurve(f)).ToArray();
        return null;
    }

    private static LightingPresetExtractor.DirectionalLightInfo BuildSunInfo(UDirectionalLightComponent component, FRichCurve[]? colorCurves, LightingPresetExtractor.CubeMapInfo? cubeInfo, float sunYaw, float dayIntensity, float sunHeight)
    {
        var transform = component.GetRelativeTransform();
        var baseRotation = transform.Rotator();
        var baseLocation = transform.Translation;

        var height = Math.Clamp(sunHeight, 0f, 1f);
        var pitch = baseRotation.Pitch * height;
        var rotation = new FRotator(pitch, sunYaw, baseRotation.Roll);

        var color = EvaluateLinearColor(colorCurves, height, new FLinearColor(1f, 1f, 1f, 1f));
        var colorMax = Math.Max(Math.Max(color.R, color.G), color.B);
        var intensity = dayIntensity * Math.Clamp(colorMax, 0f, 1f);

        var useTemperature = cubeInfo != null || component.GetOrDefault("bUseTemperature", false);
        var temperature = cubeInfo?.DirectionalLightTemperature ?? component.GetOrDefault("Temperature", 6500f);
        var usedAsAtmosphere = component.GetOrDefault("bUsedAsAtmosphereSunLight", false);

        string? lightFunctionPath = null;
        if (component.TryGetValue(out FPackageIndex functionIndex, "LightFunctionMaterial") && !functionIndex.IsNull)
            lightFunctionPath = functionIndex.ToString();

        var lightFunctionScale = component.TryGetValue(out FVector scale, "LightFunctionScale") ? scale : (FVector?)null;
        var bloomScale = component.GetOrDefault("BloomScale", 1.0f);

        return new LightingPresetExtractor.DirectionalLightInfo(
            component.Name,
            baseLocation,
            rotation,
            intensity,
            useTemperature,
            temperature,
            color,
            usedAsAtmosphere,
            lightFunctionPath,
            lightFunctionScale,
            bloomScale);
    }

    private static LightingPresetExtractor.DirectionalLightInfo BuildMoonInfo(UDirectionalLightComponent component, FRichCurve[]? colorCurves, FRichCurve? intensityCurve, FRichCurve? angleCurve, LightingPresetExtractor.CubeMapInfo? cubeInfo, float sunHeight)
    {
        var transform = component.GetRelativeTransform();
        var baseRotation = transform.Rotator();
        var baseLocation = transform.Translation;

        var sampleTime = Math.Clamp(sunHeight, -1f, 1f);
        var pitch = angleCurve?.Eval(sampleTime) ?? baseRotation.Pitch;
        var rotation = new FRotator(pitch, baseRotation.Yaw, baseRotation.Roll);

        var color = EvaluateLinearColor(colorCurves, sampleTime, new FLinearColor(0.6f, 0.6f, 0.7f, 1f));
        var intensity = intensityCurve?.Eval(sampleTime) ?? component.GetOrDefault("Intensity", 0f);

        var useTemperature = cubeInfo != null || component.GetOrDefault("bUseTemperature", false);
        var temperature = cubeInfo?.DirectionalLightTemperature ?? component.GetOrDefault("Temperature", 6500f);
        var usedAsAtmosphere = component.GetOrDefault("bUsedAsAtmosphereSunLight", false);

        string? lightFunctionPath = null;
        if (component.TryGetValue(out FPackageIndex functionIndex, "LightFunctionMaterial") && !functionIndex.IsNull)
            lightFunctionPath = functionIndex.ToString();

        var lightFunctionScale = component.TryGetValue(out FVector scale, "LightFunctionScale") ? scale : (FVector?)null;
        var bloomScale = component.GetOrDefault("BloomScale", 1.0f);

        return new LightingPresetExtractor.DirectionalLightInfo(
            component.Name,
            baseLocation,
            rotation,
            intensity,
            useTemperature,
            temperature,
            color,
            usedAsAtmosphere,
            lightFunctionPath,
            lightFunctionScale,
            bloomScale);
    }

    private static LightingPresetExtractor.SkyLightInfo? BuildSkyInfo(USkyLightComponent component, FRichCurve? intensityCurve, float sunHeight)
    {
        var transform = component.GetRelativeTransform();
        var location = transform.Translation;
        var intensity = intensityCurve?.Eval(Math.Clamp(sunHeight, -1f, 1f)) ?? component.GetOrDefault("Intensity", 1.0f);
        var lowerHemisphere = component.GetOrDefault("bLowerHemisphereIsBlack", true);
        var skyDistance = component.GetOrDefault("SkyDistanceThreshold", 150000f);
        var occlusionMaxDistance = component.GetOrDefault("OcclusionMaxDistance", 0f);
        var occlusionExponent = component.GetOrDefault("OcclusionExponent", 1f);
        var occlusionTint = component.TryGetValue(out FLinearColor tint, "OcclusionTint") ? tint : (FLinearColor?)null;

        string? cubemapPath = null;
        if (component.TryGetValue(out FPackageIndex cubemapIndex, "Cubemap") && !cubemapIndex.IsNull)
            cubemapPath = cubemapIndex.ToString();

        return new LightingPresetExtractor.SkyLightInfo(
            component.Name,
            location,
            intensity,
            lowerHemisphere,
            skyDistance,
            occlusionMaxDistance,
            occlusionExponent,
            occlusionTint,
            cubemapPath);
    }

    private static bool DetermineOutOfVision(FRichCurve? visibilityCurve, float sunHeight)
    {
        if (visibilityCurve == null)
            return sunHeight < 0f;
        var value = visibilityCurve.Eval(Math.Clamp(sunHeight, -1f, 1f));
        return value <= 0.01f;
    }

    private static LightingPresetExtractor.CubeMapInfo? SelectCubeMapSample(IReadOnlyList<FStructFallback> samples, float sunHeight)
    {
        if (samples.Count == 0)
            return null;

        var best = samples
            .OrderBy(s => Math.Abs(s.GetOrDefault("SunHeight", 0f) - sunHeight))
            .First();

        string? cubePath = null;
        if (best.TryGetValue(out FPackageIndex cubeIndex, "CubeMap") && !cubeIndex.IsNull)
            cubePath = cubeIndex.ToString();

        var filmTint = best.GetOrDefault("FilmTint", new FLinearColor(1f, 1f, 1f, 1f));
        var filmTintShadow = best.GetOrDefault("FilmTintShadow", new FLinearColor(1f, 1f, 1f, 1f));

        return new LightingPresetExtractor.CubeMapInfo(
            CubeMapPath: cubePath,
            WhiteBalanceTemp: best.GetOrDefault("WhiteBalanceTemp", 6500f),
            FilmTint: filmTint,
            FilmTintShadow: filmTintShadow,
            Saturation: best.GetOrDefault("Saturation", 1f),
            Contrast: best.GetOrDefault("Contrast", 0f),
            CrushShadows: best.GetOrDefault("CrushShadows", 0f),
            CrushHighlights: best.GetOrDefault("CrushHighlights", 0f),
            Toe: best.GetOrDefault("Toe", 0f),
            DirectionalLightTemperature: best.GetOrDefault("DirectionalLightTemperature", 6500f));
    }

    private static IReadOnlyList<FStructFallback> GetCubeMapSamples(UObject manager)
        => manager.GetOrDefault("CubeMapSamples", Array.Empty<FStructFallback>());

    private static FLinearColor EvaluateLinearColor(FRichCurve[]? curves, float time, FLinearColor fallback)
    {
        if (curves == null || curves.Length == 0)
            return fallback;

        var r = curves.Length > 0 ? curves[0].Eval(time) : fallback.R;
        var g = curves.Length > 1 ? curves[1].Eval(time) : fallback.G;
        var b = curves.Length > 2 ? curves[2].Eval(time) : fallback.B;
        var a = curves.Length > 3 ? curves[3].Eval(time) : fallback.A;
        return new FLinearColor(r, g, b, a);
    }

    private static float ComputeSunHeight(float hour)
    {
        var h = hour % 24f;
        if (h < 0f) h += 24f;

        if (h < 6f)
            return -1f + (h / 6f);
        if (h < 12f)
            return (h - 6f) / 6f;
        if (h < 18f)
            return 1f - ((h - 12f) / 6f);
        return 0f - ((h - 18f) / 6f);
    }
}

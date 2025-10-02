using System;
using System.Collections.Generic;
using System.IO;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using FModel.Views.Snooper;
using FModelHeadless.Lib.Blueprint;
using FModelHeadless.Rendering;

namespace FModelHeadless.Cli;

internal static class RenderCommandRunner
{
    private sealed record RenderSettings(
        int Width,
        int Height,
        int AngleCount,
        float PitchDegrees,
        float OrbitRadius,
        float BaseYaw,
        bool Verbose,
        bool TransparentBackground,
        SceneFilters? Filters);

    public static void RunScene(DefaultFileProvider provider, SceneSpec scene, DirectoryInfo outputDir, bool verbose)
    {
        var camera = scene.Camera ?? new SceneCamera();
        var angles = Math.Max(1, camera.Angles ?? 1);
        var width = camera.Width ?? 1366;
        var height = camera.Height ?? 768;
        var pitch = camera.Pitch ?? 55f;
        var yaw = camera.Yaw ?? 45f;
        var orbit = camera.Orbit ?? 0f;
        var transparent = camera.Transparent ?? false;

        if (verbose)
        {
            Console.WriteLine($"[render] Mount contains {provider.Files.Count} files.");
        }

        if (verbose)
        {
            foreach (var asset in scene.Assets)
            {
                Console.WriteLine($"[scene] asset id={asset.Id} path={asset.Path} metadata={asset.MetadataPath}");
            }
        }

        var resolvedScene = SceneResolver.Resolve(provider, scene, verbose);
        var settings = new RenderSettings(width, height, angles, pitch, orbit, yaw, verbose, transparent, scene.Filters);

        RenderPrimaryAsset(resolvedScene, outputDir, settings, scene);
    }

    private static void RenderPrimaryAsset(ResolvedScene resolvedScene, DirectoryInfo outputDir, RenderSettings settings, SceneSpec fullSpec)
    {
        var root = resolvedScene.Root;
        var asset = root.Asset;

        Console.WriteLine($"[render] Source: {root.SourcePath}");
        Console.WriteLine($"[render] Asset type: {asset.ExportType}");
        Console.WriteLine($"[render] Package: {asset.GetPathName()}");

        switch (asset)
        {
            case UStaticMesh or USkeletalMesh:
                RenderMesh(resolvedScene, outputDir, settings, fullSpec);
                break;
            default:
                throw new InvalidOperationException($"Asset type '{asset.ExportType}' is not supported by the renderer.");
        }
    }

    private static void RenderMesh(ResolvedScene scene, DirectoryInfo outputDir, RenderSettings settings, SceneSpec fullSpec)
    {
        var asset = scene.Root.Asset;
        if (settings.Verbose)
        {
            FModelHeadless.Lib.Common.MeshSocketUtil.LogSocketSummary(asset);
        }

        if (!outputDir.Exists)
        {
            outputDir.Create();
        }

        var guidSuffix = Guid.NewGuid().ToString("N");
        var basePath = Path.Combine(outputDir.FullName, $"{SanitizePath(asset.GetPathName())}_{guidSuffix}");

        // Build post-process settings from scene.render.postProcess
        // Disable experimental PP by default; gate with env var HEADLESS_ENABLE_PP=1
        PostProcessSettings? pp = null;
        var ppSpec = fullSpec.Render?.PostProcess;
        var gate = Environment.GetEnvironmentVariable("HEADLESS_ENABLE_PP");
        var ppEnabledGate = !string.IsNullOrEmpty(gate) && (gate == "1" || gate.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (ppEnabledGate && ppSpec != null && (ppSpec.Enabled ?? false))
        {
            if (!string.IsNullOrWhiteSpace(ppSpec.Preset) && ppSpec.Preset.Equals("foxhole", StringComparison.OrdinalIgnoreCase))
                pp = PostProcessSettings.FoxholePreset();
            else
                pp = new PostProcessSettings { Enabled = true };

            if (ppSpec.VignetteIntensity.HasValue)
            { pp.VignetteEnabled = ppSpec.VignetteIntensity.Value > 0f; pp.VignetteIntensity = ppSpec.VignetteIntensity.Value; }
            if (ppSpec.GrainIntensity.HasValue)
            { pp.GrainEnabled = ppSpec.GrainIntensity.Value > 0f; pp.GrainIntensity = ppSpec.GrainIntensity.Value; }
            if (ppSpec.ChromaticAmountPx.HasValue)
            { pp.ChromaticEnabled = ppSpec.ChromaticAmountPx.Value > 0f; pp.ChromaticAmountPx = ppSpec.ChromaticAmountPx.Value; }
            if (ppSpec.DirtIntensity.HasValue)
            { pp.DirtEnabled = ppSpec.DirtIntensity.Value > 0f; pp.DirtIntensity = ppSpec.DirtIntensity.Value; }
            if (ppSpec.DirtTint is { Length: 3 })
                pp.DirtTint = new OpenTK.Mathematics.Vector3(ppSpec.DirtTint[0], ppSpec.DirtTint[1], ppSpec.DirtTint[2]);
            if (ppSpec.DirtTiling.HasValue)
                pp.DirtTiling = ppSpec.DirtTiling.Value;
        }

        for (var i = 0; i < settings.AngleCount; i++)
        {
            var yaw = settings.BaseYaw + (settings.AngleCount > 1 ? (360f / settings.AngleCount) * i : 0f);
            var suffix = settings.AngleCount == 1 ? string.Empty : $"_{i:D3}";
            var pngPath = basePath + suffix + ".png";

            if (settings.Verbose)
            {
                Console.WriteLine($"[render] View {i + 1}/{settings.AngleCount}: pitch={settings.PitchDegrees:F1} yaw={yaw:F1} orbit={(settings.OrbitRadius <= 0f ? "auto" : settings.OrbitRadius.ToString("F2"))} -> {pngPath}");
            }

            using var window = new HeadlessRenderWindow(scene.Root, pngPath, settings.Width, settings.Height, settings.PitchDegrees, yaw, settings.OrbitRadius, settings.Verbose, settings.TransparentBackground, scene.Attachments, settings.Filters, pp);
            window.RenderOnce();
        }
    }

    // Socket summary moved to MeshSocketUtil

    private static string SanitizePath(string objectPath)
    {
        var sanitized = objectPath.Replace('/', '_').Replace('.', '_');
        return sanitized.Trim('_');
    }
}

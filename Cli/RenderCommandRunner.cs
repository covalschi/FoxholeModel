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
        bool TransparentBackground);

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
        var settings = new RenderSettings(width, height, angles, pitch, orbit, yaw, verbose, transparent);

        RenderPrimaryAsset(resolvedScene, outputDir, settings);
    }

    private static void RenderPrimaryAsset(ResolvedScene resolvedScene, DirectoryInfo outputDir, RenderSettings settings)
    {
        var root = resolvedScene.Root;
        var asset = root.Asset;

        Console.WriteLine($"[render] Source: {root.SourcePath}");
        Console.WriteLine($"[render] Asset type: {asset.ExportType}");
        Console.WriteLine($"[render] Package: {asset.GetPathName()}");

        switch (asset)
        {
            case UStaticMesh or USkeletalMesh:
                RenderMesh(resolvedScene, outputDir, settings);
                break;
            default:
                throw new InvalidOperationException($"Asset type '{asset.ExportType}' is not supported by the renderer.");
        }
    }

    private static void RenderMesh(ResolvedScene scene, DirectoryInfo outputDir, RenderSettings settings)
    {
        var asset = scene.Root.Asset;
        if (settings.Verbose)
        {
            LogSocketSummary(asset);
        }

        if (!outputDir.Exists)
        {
            outputDir.Create();
        }

        var guidSuffix = Guid.NewGuid().ToString("N");
        var basePath = Path.Combine(outputDir.FullName, $"{SanitizePath(asset.GetPathName())}_{guidSuffix}");

        for (var i = 0; i < settings.AngleCount; i++)
        {
            var yaw = settings.BaseYaw + (settings.AngleCount > 1 ? (360f / settings.AngleCount) * i : 0f);
            var suffix = settings.AngleCount == 1 ? string.Empty : $"_{i:D3}";
            var pngPath = basePath + suffix + ".png";

            if (settings.Verbose)
            {
                Console.WriteLine($"[render] View {i + 1}/{settings.AngleCount}: pitch={settings.PitchDegrees:F1} yaw={yaw:F1} orbit={(settings.OrbitRadius <= 0f ? "auto" : settings.OrbitRadius.ToString("F2"))} -> {pngPath}");
            }

            using var window = new HeadlessRenderWindow(scene.Root, pngPath, settings.Width, settings.Height, settings.PitchDegrees, yaw, settings.OrbitRadius, settings.Verbose, settings.TransparentBackground, scene.Attachments);
            window.RenderOnce();
        }
    }

    private static void LogSocketSummary(UObject asset)
    {
        switch (asset)
        {
            case USkeletalMesh skeletal when skeletal.Sockets is { Length: > 0 }:
                Console.WriteLine($"[render] Skeletal mesh sockets ({skeletal.Sockets.Length}):");
                foreach (var socketRef in skeletal.Sockets)
                {
                    if (!socketRef.TryLoad(out USkeletalMeshSocket socket) || socket == null)
                        continue;

                    var name = socket.SocketName.Text;
                    var bone = socket.BoneName.Text;
                    var location = socket.RelativeLocation;
                    Console.WriteLine($"  socket {name} bone={bone} loc=({location.X:F1},{location.Y:F1},{location.Z:F1})");
                }
                break;
            case UStaticMesh staticMesh when staticMesh.Sockets is { Length: > 0 }:
                Console.WriteLine($"[render] Static mesh sockets ({staticMesh.Sockets.Length}):");
                foreach (var socketRef in staticMesh.Sockets)
                {
                    if (!socketRef.TryLoad(out UStaticMeshSocket socket) || socket == null)
                        continue;

                    var name = socket.SocketName.Text;
                    var location = socket.RelativeLocation;
                    Console.WriteLine($"  socket {name} loc=({location.X:F1},{location.Y:F1},{location.Z:F1})");
                }
                break;
        }
    }

    private static string SanitizePath(string objectPath)
    {
        var sanitized = objectPath.Replace('/', '_').Replace('.', '_');
        return sanitized.Trim('_');
    }
}

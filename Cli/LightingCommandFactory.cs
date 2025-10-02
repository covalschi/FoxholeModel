using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using FModelHeadless.Lib.Lighting;

namespace FModelHeadless.Cli;

internal static class LightingCommandFactory
{
    public static Command Create(Option<DirectoryInfo> pakDirOption, Option<FileInfo?> mappingOption, Option<string?> aesOption, Option<string> gameOption, Option<bool> verboseOption)
    {
        var command = new Command("lighting", "Sample Foxhole lighting data");

        var worldOption = new Option<string>("--world", "World object path containing the DayNightCycleManager")
        {
            IsRequired = true
        };

        var hoursOption = new Option<float[]>("--hours", () => new[] { 6f, 12f, 18f, 24f }, "List of hours to evaluate (0-24)")
        {
            AllowMultipleArgumentsPerToken = true
        };

        command.AddOption(worldOption);
        command.AddOption(hoursOption);

        command.SetHandler((DirectoryInfo pakDir, FileInfo? mapping, string? aes, string gameTag, bool verbose, string worldPath, float[] hours) =>
        {
            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aes, gameTag, verbose);
                using var scope = new ProviderScope(provider);

                var sampleHours = (hours?.Length ?? 0) == 0 ? new[] { 6f, 12f, 18f, 24f } : hours;
                var presets = TimeOfDayLightingSampler.Sample(provider, worldPath, sampleHours, verbose);

                if (presets.Count == 0)
                {
                    Console.Error.WriteLine("[lighting] No presets generated.");
                    Environment.ExitCode = 1;
                    return;
                }

                foreach (var kvp in presets.OrderBy(p => p.Key))
                {
                    var hour = kvp.Key;
                    var preset = kvp.Value;
                    Console.WriteLine($"[lighting] Hour {hour:00.##} -> cube={preset.CubeMap?.CubeMapPath ?? "<none>"} outOfVision={preset.IsOutOfVision}");

                    var index = 0;
                    foreach (var light in preset.DirectionalLights)
                    {
                        var rotation = light.Rotation;
                        var color = light.Color;
                        Console.WriteLine($"  dir[{index++}] color=({color.R:F3},{color.G:F3},{color.B:F3}) intensity={light.Intensity:F3} rot=({rotation.Pitch:F2},{rotation.Yaw:F2},{rotation.Roll:F2})");
                    }

                    if (preset.SkyLight is { } sky)
                    {
                        var tint = sky.OcclusionTint;
                        var tintString = tint is null
                            ? "<none>"
                            : $"({tint.Value.R:F3},{tint.Value.G:F3},{tint.Value.B:F3},{tint.Value.A:F3})";
                        Console.WriteLine($"  skylight intensity={sky.Intensity:F3} cubemap={sky.CubemapPath ?? "<none>"} occlusionTint={tintString}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[lighting] {ex.Message}");
                if (verbose)
                    Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }, pakDirOption, mappingOption, aesOption, gameOption, verboseOption, worldOption, hoursOption);

        return command;
    }
}

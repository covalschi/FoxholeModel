using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using FModelHeadless.Rendering;

namespace FModelHeadless.Cli;

internal static class RenderCommandFactory
{
    public static Command Create(Option<DirectoryInfo?> pakDirOption, Option<FileInfo?> mappingOption, Option<string?> aesOption, Option<string> gameOption, Option<bool> verboseOption)
    {
        var sceneOption = new Option<FileInfo>("--scene", "Path to scene JSON specification")
        {
            IsRequired = true
        };

        var outputOverrideOption = new Option<DirectoryInfo?>("--output", () => null, "Override output directory");

        var command = new Command("render", "Render meshes using a JSON scene specification");
        command.AddOption(sceneOption);
        command.AddOption(outputOverrideOption);

        command.SetHandler(context =>
        {
            var parse = context.ParseResult;

            var pakDir = parse.GetValueForOption(pakDirOption);
            var mapping = parse.GetValueForOption(mappingOption);
            var aesKey = parse.GetValueForOption(aesOption);
            var gameTag = parse.GetValueForOption(gameOption)!;
            var verbose = parse.GetValueForOption(verboseOption);
            var sceneFile = parse.GetValueForOption(sceneOption)!;
            var outputOverride = parse.GetValueForOption(outputOverrideOption);

            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aesKey, gameTag, verbose);
                using var scope = new ProviderScope(provider);

                var scene = LoadScene(sceneFile, verbose);
                var outputDir = ResolveOutputDirectory(scene, outputOverride);

                Directory.CreateDirectory(outputDir.FullName);

                RenderCommandRunner.RunScene(provider, scene, outputDir, verbose);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[render] {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }
                context.ExitCode = 1;
            }
        });

        return command;
    }

    private static SceneSpec LoadScene(FileInfo sceneFile, bool verbose)
    {
        if (!sceneFile.Exists)
        {
            throw new FileNotFoundException($"Scene file '{sceneFile.FullName}' was not found.");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        using var stream = sceneFile.OpenRead();
        var scene = JsonSerializer.Deserialize<SceneSpec>(stream, options);
        if (scene == null)
        {
            throw new InvalidDataException("Scene file could not be parsed.");
        }

        if (scene.Assets.Count == 0)
        {
            throw new InvalidDataException("Scene file contains no assets.");
        }

        if (verbose)
        {
            Console.WriteLine($"[scene] Loaded {scene.Assets.Count} asset(s) from {sceneFile.FullName}");
        }

        return scene;
    }

    private static DirectoryInfo ResolveOutputDirectory(SceneSpec scene, DirectoryInfo? overrideDir)
    {
        if (overrideDir != null)
        {
            return overrideDir;
        }

        var render = scene.Render;
        if (!string.IsNullOrWhiteSpace(render?.Output))
        {
            return new DirectoryInfo(render.Output);
        }

        return new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "output"));
    }
}

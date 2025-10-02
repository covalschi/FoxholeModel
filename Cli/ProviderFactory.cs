using System;
using System.IO;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using FModelHeadless.Headless;

namespace FModelHeadless.Cli;

internal static class ProviderFactory
{
    public static DefaultFileProvider Create(DirectoryInfo pakDir, FileInfo? mapping, string? aesKey, string gameVersion, bool verbose)
    {
        if (!pakDir.Exists)
        {
            throw new DirectoryNotFoundException($"Pak directory '{pakDir.FullName}' does not exist.");
        }

        var version = new VersionContainer(ParseGameVersion(gameVersion));
        var provider = new DefaultFileProvider(pakDir.FullName, SearchOption.AllDirectories, version);

        if (mapping != null)
        {
            if (!mapping.Exists)
            {
                throw new FileNotFoundException($"Mapping file '{mapping.FullName}' was not found.");
            }

            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mapping.FullName);
        }

        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey(new byte[32]));

        if (!string.IsNullOrWhiteSpace(aesKey))
        {
            provider.SubmitKey(new FGuid(), new FAesKey(aesKey));
        }

        provider.PostMount();
        provider.LoadVirtualPaths();

        if (verbose)
        {
            Console.WriteLine($"[cli] Mounted pak dir '{pakDir.FullName}' with {provider.Files.Count} files registered.");
        }

        return provider;
    }

    private static EGame ParseGameVersion(string tag)
    {
        if (Enum.TryParse<EGame>(tag, true, out var parsed))
        {
            return parsed;
        }

        Console.WriteLine($"[cli] Unknown game tag '{tag}', defaulting to GAME_UE4_25");
        return EGame.GAME_UE4_25;
    }
}

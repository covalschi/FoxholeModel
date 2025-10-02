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
    public static DefaultFileProvider Create(DirectoryInfo? pakDir, FileInfo? mapping, string? aesKey, string gameVersion, bool verbose)
    {
        pakDir ??= ResolveDefaultPakDir();
        if (pakDir == null || !pakDir.Exists)
            throw new DirectoryNotFoundException($"Pak directory not provided and default could not be found. Searched: {string.Join(", ", DefaultPakCandidates())}");

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

    public static DirectoryInfo? ResolveDefaultPakDir()
    {
        foreach (var candidate in DefaultPakCandidates())
        {
            try
            {
                if (Directory.Exists(candidate))
                    return new DirectoryInfo(candidate);
            }
            catch
            {
                // ignore path errors
            }
        }
        return null;
    }

    private static IEnumerable<string> DefaultPakCandidates()
    {
        // Preferred Windows install locations (current)
        yield return @"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Foxhole\\War\\Content\\Paks";
        yield return @"C:\\Program Files\\Steam\\steamapps\\common\\Foxhole\\War\\Content\\Paks";

        // WSL/Linux mirrors of the above
        yield return @"/mnt/c/Program Files (x86)/Steam/steamapps/common/Foxhole/War/Content/Paks";
        yield return @"/mnt/c/Program Files/Steam/steamapps/common/Foxhole/War/Content/Paks";

        // Legacy locations kept as fallbacks
        yield return @"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Foxhole\\Foxhole\\Content\\Paks";
        yield return @"C:\\Program Files\\Steam\\steamapps\\common\\Foxhole\\Foxhole\\Content\\Paks";
        yield return @"/mnt/c/Program Files (x86)/Steam/steamapps/common/Foxhole/Foxhole/Content/Paks";
        yield return @"/mnt/c/Program Files/Steam/steamapps/common/Foxhole/Foxhole/Content/Paks";
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

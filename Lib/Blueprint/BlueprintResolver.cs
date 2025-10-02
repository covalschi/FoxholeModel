using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Engine;

namespace FModelHeadless.Lib.Blueprint;

public static class BlueprintResolver
{
    public static bool TryFind(DefaultFileProvider provider, string objectPath, bool verbose, out UBlueprintGeneratedClass blueprintClass, out string resolvedPath)
    {
        resolvedPath = NormalizeObjectPath(objectPath);

        var candidates = ExpandCandidates(resolvedPath).ToList();

        foreach (var packagePath in EnumeratePackageCandidates(resolvedPath, candidates))
        {
            if (!provider.TryLoadPackage(packagePath, out var package) || package == null)
                continue;

            if (TryResolveFromPackage(package, out blueprintClass))
            {
                resolvedPath = blueprintClass.GetPathName();
                return true;
            }
        }

        foreach (var candidate in candidates)
        {
            if (TryLoad(provider, candidate, out UBlueprintGeneratedClass? generated, verbose: false) && generated != null)
            {
                blueprintClass = generated;
                resolvedPath = generated.GetPathName();
                return true;
            }

            if (TryLoad(provider, candidate, out UBlueprint? blueprint, verbose: false) && blueprint != null)
            {
                var generatedClass = blueprint.GeneratedClass?.Load<UBlueprintGeneratedClass>();
                if (generatedClass != null)
                {
                    blueprintClass = generatedClass;
                    resolvedPath = generatedClass.GetPathName();
                    return true;
                }
            }
        }

        blueprintClass = null!;
        resolvedPath = string.Empty;
        if (verbose)
        {
            Console.Error.WriteLine($"[blueprint] Unable to resolve blueprint '{objectPath}'.");
        }
        return false;
    }

    private static IEnumerable<string> ExpandCandidates(string normalized)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            seen.Add(candidate);
        }

        Add(normalized);

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var slash = normalized.LastIndexOf('/');
            var baseName = slash >= 0 ? normalized[(slash + 1)..] : normalized;

            if (!string.IsNullOrEmpty(baseName))
            {
                if (!normalized.Contains('.'))
                {
                    Add($"{normalized}.{baseName}");
                    Add($"{normalized}.{baseName}_C");
                }
                else
                {
                    var dot = normalized.LastIndexOf('.');
                    if (dot > slash)
                    {
                        var package = normalized[..dot];
                        var objectName = normalized[(dot + 1)..];
                        Add($"{package}.{objectName}_C");
                    }
                }
            }
        }

        return seen;
    }

    public static string NormalizeObjectPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return string.Empty;

        var trimmed = rawPath.Trim();
        if (trimmed.Length == 0)
            return trimmed;

        var firstDouble = trimmed.IndexOf('"');
        if (firstDouble >= 0)
        {
            var lastDouble = trimmed.LastIndexOf('"');
            if (lastDouble > firstDouble)
                trimmed = trimmed[(firstDouble + 1)..lastDouble];
        }
        else
        {
            var firstSingle = trimmed.IndexOf('\'');
            if (firstSingle >= 0)
            {
                var lastSingle = trimmed.LastIndexOf('\'');
                if (lastSingle > firstSingle)
                    trimmed = trimmed[(firstSingle + 1)..lastSingle];
            }
        }

        var prefixEnd = trimmed.IndexOf(' ');
        if (prefixEnd > 0 && prefixEnd - 1 < trimmed.Length && trimmed[prefixEnd - 1] == ':')
        {
            trimmed = trimmed[(prefixEnd + 1)..];
        }

        return trimmed;
    }

    private static IEnumerable<string> EnumeratePackageCandidates(string normalized, IEnumerable<string> expanded)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            var trimmed = TrimObjectPath(path);
            if (!string.IsNullOrWhiteSpace(trimmed))
                packages.Add(trimmed);
        }

        Add(normalized);
        foreach (var candidate in expanded)
            Add(candidate);

        return packages;
    }

    private static string TrimObjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');
        return dot > slash ? path[..dot] : path;
    }

    private static bool TryResolveFromPackage(IPackage package, out UBlueprintGeneratedClass blueprintClass)
    {
        foreach (var exportLazy in package.ExportsLazy)
        {
            var export = exportLazy?.Value;
            if (export is UBlueprintGeneratedClass generated)
            {
                blueprintClass = generated;
                return true;
            }

            if (export is UBlueprint blueprint)
            {
                var generatedClass = blueprint.GeneratedClass?.Load<UBlueprintGeneratedClass>();
                if (generatedClass != null)
                {
                    blueprintClass = generatedClass;
                    return true;
                }
            }
        }

        blueprintClass = null!;
        return false;
    }

    private static bool TryLoad<T>(DefaultFileProvider provider, string path, out T? asset, bool verbose) where T : UObject
    {
        asset = null;
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            asset = provider.LoadPackageObject<T>(path);
            return asset != null;
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[blueprint] Failed to load '{path}': {ex.Message}");
            asset = null;
            return false;
        }
    }
}

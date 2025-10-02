using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace FModelHeadless.Cli;

internal static class SearchCommandFactory
{
    public static Command Create(Option<DirectoryInfo?> pakDirOption, Option<FileInfo?> mappingOption, Option<string?> aesOption, Option<string> gameOption, Option<bool> verboseOption)
    {
        var termOption = new Option<string>("--term", description: "Case-insensitive substring to match in virtual paths")
        {
            IsRequired = true
        };
        var limitOption = new Option<int>("--limit", () => 50, "Max results to print");

        var cmd = new Command("search", "Search mounted virtual paths for an asset/subpath");
        cmd.AddOption(termOption);
        cmd.AddOption(limitOption);

        cmd.SetHandler((DirectoryInfo? pakDir, FileInfo? mapping, string? aes, string gameTag, bool verbose, string term, int limit) =>
        {
            try
            {
                using var provider = ProviderFactory.Create(pakDir, mapping, aes, gameTag, verbose);
                using var scope = new ProviderScope(provider);

                var needle = term.Trim();
                var matches = provider.Files.Keys
                    .Where(k => k.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Take(Math.Max(1, limit))
                    .ToArray();

                Console.WriteLine($"[search] '{needle}' -> {matches.Length} match(es) (showing up to {limit})");
                foreach (var m in matches)
                    Console.WriteLine($"  {m}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[search] {ex.Message}");
                if (verbose)
                    Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }, pakDirOption, mappingOption, aesOption, gameOption, verboseOption, termOption, limitOption);

        return cmd;
    }
}

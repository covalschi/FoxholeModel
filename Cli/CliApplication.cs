using System.CommandLine;

namespace FModelHeadless.Cli;

internal static class CliApplication
{
    public static RootCommand BuildRootCommand()
    {
        var pakDirOption = new Option<DirectoryInfo?>("--pak-dir", "Root directory containing Foxhole pak files (defaults to Steam install under C:/Program Files...)");

        var mappingOption = new Option<FileInfo?>("--mapping", () => null, "Optional USMAP mapping file");
        var aesOption = new Option<string?>("--aes-key", () => null, "Optional AES key in hex (0x...)");
        var gameOption = new Option<string>("--game-version", () => "GAME_UE4_24", "Game version tag used by CUE4Parse (e.g. GAME_UE4_24)");
        var verboseOption = new Option<bool>("--verbose", description: "Enable verbose logging");

        var root = new RootCommand("Headless FModel tooling");
        root.AddGlobalOption(pakDirOption);
        root.AddGlobalOption(mappingOption);
        root.AddGlobalOption(aesOption);
        root.AddGlobalOption(gameOption);
        root.AddGlobalOption(verboseOption);

        root.AddCommand(RenderCommandFactory.Create(pakDirOption, mappingOption, aesOption, gameOption, verboseOption));
        root.AddCommand(BlueprintCommandFactory.Create(pakDirOption, mappingOption, aesOption, gameOption, verboseOption));
        // Cargo command removed; anchor helpers are handled internally by the resolver
        root.AddCommand(LightingCommandFactory.Create(pakDirOption, mappingOption, aesOption, gameOption, verboseOption));
        root.AddCommand(VariantsCommandFactory.Create(pakDirOption, mappingOption, aesOption, gameOption, verboseOption));
        root.AddCommand(SearchCommandFactory.Create(pakDirOption, mappingOption, aesOption, gameOption, verboseOption));

        return root;
    }
}

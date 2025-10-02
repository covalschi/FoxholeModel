using System;
using System.CommandLine;
using FModelHeadless.Cli;

namespace FModelHeadless;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var root = CliApplication.BuildRootCommand();

        try
        {
            var exitCode = root.InvokeAsync(args).GetAwaiter().GetResult();

            if (Environment.ExitCode != 0 && exitCode == 0)
            {
                return Environment.ExitCode;
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

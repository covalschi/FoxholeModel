using CUE4Parse.FileProvider;

namespace FModelHeadless.Headless;

internal static class GlobalContext
{
    public static IFileProvider? Provider { get; set; }
}

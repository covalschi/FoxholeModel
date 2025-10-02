using System;

namespace FModelHeadless.Lib.Common;

internal static class FilterUtil
{
    public static bool TokenContains(string? haystack, string token)
        => !string.IsNullOrWhiteSpace(token) &&
           !string.IsNullOrEmpty(haystack) &&
           haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool AnyTokenMatch(string? a, string? b, string[] tokens)
    {
        if (tokens == null || tokens.Length == 0) return false;
        foreach (var t in tokens)
        {
            if (TokenContains(a, t) || TokenContains(b, t)) return true;
        }
        return false;
    }
}


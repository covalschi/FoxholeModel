using System.Collections.Generic;

namespace FModelHeadless.Lib.Common;

internal static class AnchorUtil
{
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "BaseMesh";
        var n = name.Trim();
        return n.Equals("CargoPlatform", System.StringComparison.OrdinalIgnoreCase) ? "BaseMesh" : n;
    }

    public static IEnumerable<string> Candidates()
    {
        yield return "BaseMesh";
        yield return "VehicleMeshComponent";
        yield return "VehicleMesh";
        yield return "MeshComponent";
    }
}


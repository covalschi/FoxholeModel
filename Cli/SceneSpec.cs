using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FModelHeadless.Cli;

public sealed class SceneSpec
{
    [JsonPropertyName("assets")]
    public List<SceneAsset> Assets { get; set; } = new();

    [JsonPropertyName("camera")]
    public SceneCamera? Camera { get; set; }

    [JsonPropertyName("render")]
    public SceneRender? Render { get; set; }

    [JsonPropertyName("filters")]
    public SceneFilters? Filters { get; set; }
}

public sealed class SceneAsset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("metadataPath")]
    public string? MetadataPath { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("properties")]
    public SceneAssetProperties Properties { get; set; } = new();

    [JsonPropertyName("attachTo")]
    public SceneAttachment? AttachTo { get; set; }
}

public sealed class SceneAttachment
{
    [JsonPropertyName("parentId")]
    public string ParentId { get; set; } = string.Empty;

    [JsonPropertyName("anchor")]
    public string? Anchor { get; set; }

    // Optional: attach to a named socket on the parent asset (USkeletalMeshSocket/UStaticMeshSocket)
    [JsonPropertyName("socket")]
    public string? Socket { get; set; }

    [JsonPropertyName("offset")]
    public SceneTransformOffset? Offset { get; set; }
}

public sealed class SceneFilters
{
    // Exclude components if their asset path contains any of these tokens
    [JsonPropertyName("excludePathContains")]
    public string[]? ExcludePathContains { get; set; }

    // Include-only components whose asset path contains any of these tokens
    [JsonPropertyName("includePathContains")]
    public string[]? IncludePathContains { get; set; }

    // Exclude components by ComponentTags
    [JsonPropertyName("excludeTags")]
    public string[]? ExcludeTags { get; set; }

    // Include-only components by ComponentTags
    [JsonPropertyName("includeTags")]
    public string[]? IncludeTags { get; set; }

    // Show only materials whose name/path contains any of these tokens
    [JsonPropertyName("showMaterials")]
    public string[]? ShowMaterials { get; set; }

    // Hide materials whose name/path contains any of these tokens
    [JsonPropertyName("hideMaterials")]
    public string[]? HideMaterials { get; set; }
}

public sealed class SceneTransformOffset
{
    [JsonPropertyName("translation")]
    public float[]? Translation { get; set; }

    [JsonPropertyName("rotation")]
    public float[]? Rotation { get; set; }

    [JsonPropertyName("scale")]
    public float[]? Scale { get; set; }
}

public sealed class SceneAssetProperties
{
    [JsonPropertyName("hpState")]
    public string? HpState { get; set; }

    [JsonPropertyName("colorVariant")]
    public int? ColorVariant { get; set; }

    [JsonPropertyName("colorMaterialIndex")]
    public int? ColorMaterialIndex { get; set; }

    [JsonPropertyName("mudLevel")]
    public float? MudLevel { get; set; }

    [JsonPropertyName("snowLevel")]
    public float? SnowLevel { get; set; }

    [JsonPropertyName("stockpile")]
    public SceneStockpile? Stockpile { get; set; }
}

public sealed class SceneStockpile
{
    [JsonPropertyName("item")]
    public string? Item { get; set; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }
}

public sealed class SceneCamera
{
    [JsonPropertyName("pitch")]
    public float? Pitch { get; set; }

    [JsonPropertyName("yaw")]
    public float? Yaw { get; set; }

    [JsonPropertyName("orbit")]
    public float? Orbit { get; set; }

    [JsonPropertyName("angles")]
    public int? Angles { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("transparent")]
    public bool? Transparent { get; set; }
}

public sealed class SceneRender
{
    [JsonPropertyName("output")]
    public string? Output { get; set; }
}

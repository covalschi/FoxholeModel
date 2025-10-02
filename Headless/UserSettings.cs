using System;
using System.IO;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Materials;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.UEFormat.Enums;
using CUE4Parse.UE4.Objects.Meshes;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Nanite;
using CUE4Parse.UE4.Assets.Exports.Texture;
using FModel.Views.Snooper;

namespace FModel.Settings;

public sealed class UserSettings
{
    public static UserSettings Default { get; } = new UserSettings();

    public bool ShowSkybox { get; set; } = false;
    public bool ShowGrid { get; set; } = false;
    public bool AnimateWithRotationOnly { get; set; } = false;

    public ENaniteMeshFormat NaniteMeshExportFormat { get; set; } = ENaniteMeshFormat.OnlyNormalLODs;
    public Camera.WorldMode CameraMode { get; set; } = Camera.WorldMode.Arcball;

    public int PreviewMaxTextureSize { get; set; } = 4096;
    public CurrentDirectorySettings CurrentDir { get; } = new();

    public string ModelDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FModelHeadless", "Exports");

    public EMaterialFormat MaterialExportFormat { get; set; } = EMaterialFormat.FirstLayer;

    public ExporterOptions ExportOptions { get; } = new ExporterOptions
    {
        MeshFormat = EMeshFormat.UEFormat,
        LodFormat = ELodFormat.FirstLod,
        NaniteMeshFormat = ENaniteMeshFormat.OnlyNormalLODs,
        TextureFormat = ETextureFormat.Png,
        MaterialFormat = EMaterialFormat.FirstLayer,
        SocketFormat = ESocketFormat.Bone,
        CompressionFormat = EFileCompressionFormat.None,
        Platform = ETexturePlatform.DesktopMobile,
        ExportMaterials = true,
        ExportMorphTargets = true
    };

    public sealed class CurrentDirectorySettings
    {
        public string GameDirectory { get; set; } = string.Empty;
        public ETexturePlatform TexturePlatform { get; set; } = ETexturePlatform.DesktopMobile;
    }
}

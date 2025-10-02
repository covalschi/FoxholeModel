using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using FModel.Settings;
using FModel.Views.Snooper.Models;
using OpenTK.Graphics.OpenGL4;
using FModelHeadless.Rendering;

namespace FModel.Views.Snooper.Shading;

public class Material : IDisposable
{
    private int _handle;

    public readonly CMaterialParams2 Parameters;
    public string Name;
    public string Path;
    public bool IsUsed;

    public Texture[] Diffuse;
    public Texture[] Normals;
    public Texture[] SpecularMasks;
    public Texture[] Emissive;

    public Vector4[] DiffuseColor;
    public Vector4[] EmissiveColor;
    public Vector4 EmissiveRegion;

    public AoParams Ao;
    public bool HasAo;

    public float RoughnessMin = 0f;
    public float RoughnessMax = 1f;
    public float EmissiveMult = 1f;

    public bool HasSpecularMask;
    public bool HasColorShift;
    public Vector3 ColorShift;
    public float ColorShiftStrength;
    public float MudLevel;
    public float SnowLevel;
    public Vector3 MudTint = new(0.45f, 0.35f, 0.25f);
    public Vector3 SnowTint = new(0.88f, 0.9f, 0.93f);

    public Texture? MudMaskTexture;
    public Texture? SnowMaskTexture;
    public float MudMaskStrengthUniform = 1f;
    public float MudMaskTightnessUniform = 1f;
    public float SnowMaskStrengthUniform = 1f;
    public float SnowMaskTightnessUniform = 1f;

    public Material()
    {
        Parameters = new CMaterialParams2();
        Name = "";
        Path = "None";
        IsUsed = false;

        Diffuse = Array.Empty<Texture>();
        Normals = Array.Empty<Texture>();
        SpecularMasks = Array.Empty<Texture>();
        Emissive = Array.Empty<Texture>();

        DiffuseColor = Array.Empty<Vector4>();
        EmissiveColor = Array.Empty<Vector4>();
        EmissiveRegion = new Vector4(0, 0, 1, 1);
    }

    public Material(UMaterialInterface unrealMaterial) : this()
    {
        SwapMaterial(unrealMaterial);
    }

    public void SwapMaterial(UMaterialInterface unrealMaterial)
    {
        Name = unrealMaterial.Name;
        Path = unrealMaterial.GetPathName();
        unrealMaterial.GetParams(Parameters, UserSettings.Default.MaterialExportFormat);
    }

    public void Setup(Options options, int uvCount)
    {
        _handle = GL.CreateProgram();

        if (uvCount < 1 || Parameters.IsNull)
        {
            Diffuse = [new Texture(FLinearColor.Gray)];
            Normals = [new Texture(new FLinearColor(0.5f, 0.5f, 1f, 1f))];
            SpecularMasks = [new Texture(new FLinearColor(1f, 0.5f, 0.5f, 1f))];
            Emissive = new Texture[1];
            DiffuseColor = FillColors(1, Diffuse, CMaterialParams2.DiffuseColors, Vector4.One);
            EmissiveColor = [Vector4.One];
        }
        else
        {
            {   // textures
                Diffuse = FillTextures(options, uvCount, Parameters.HasTopDiffuse, CMaterialParams2.Diffuse, CMaterialParams2.FallbackDiffuse, true);
                Normals = FillTextures(options, uvCount, Parameters.HasTopNormals, CMaterialParams2.Normals, CMaterialParams2.FallbackNormals);
                SpecularMasks = FillTextures(options, uvCount, Parameters.HasTopSpecularMasks, CMaterialParams2.SpecularMasks, CMaterialParams2.FallbackSpecularMasks);
                Emissive = FillTextures(options, uvCount, true, CMaterialParams2.Emissive, CMaterialParams2.FallbackEmissive);

                // Foxhole materials often pack roughness/mask data into custom slots (e.g. RoughnessUVTexture).
                // If no specular mask was resolved, try to bind well-known auxiliary textures so shaders can access masks.
                for (var i = 0; i < SpecularMasks.Length; i++)
                {
                    if (SpecularMasks[i] != null) continue;

                    if (Parameters.TryGetTexture2d(out var maskTex, "RoughnessUVTexture", "RoughnessTexture", "ColorMask", "OverlayMask", "ShippingContainerNormaM"))
                    {
                        if (options.TryGetTexture(maskTex, false, out var transformedMask))
                        {
                            SpecularMasks[i] = transformedMask;
                            break;
                        }
                    }
                }
            }

            HasSpecularMask = SpecularMasks.Any(tex => tex != null);

            {   // colors
                DiffuseColor = FillColors(uvCount, Diffuse, CMaterialParams2.DiffuseColors, Vector4.One);
                EmissiveColor = FillColors(uvCount, Emissive, CMaterialParams2.EmissiveColors, Vector4.One);

                if (Parameters.TryGetLinearColor(out var colorShift, "ColorShift", "Color Shift"))
                {
                    HasColorShift = true;
                    ColorShift = new Vector3(colorShift.R, colorShift.G, colorShift.B);
                    ColorShiftStrength = MathF.Abs(colorShift.A) > float.Epsilon ? colorShift.A : 1f;
                }
            }

            {   // ambient occlusion + color boost
                if (Parameters.TryGetTexture2d(out var original, "M", "AEM", "AO") &&
                    !original.Name.Equals("T_BlackMask") && options.TryGetTexture(original, false, out var transformed))
                {
                    HasAo = true;
                    Ao = new AoParams { Texture = transformed };
                    if (Parameters.TryGetLinearColor(out var l, "Skin Boost Color And Exponent"))
                    {
                        Ao.HasColorBoost = true;
                        Ao.ColorBoost = new Boost { Color = new Vector3(l.R, l.G, l.B), Exponent = l.A };
                    }
                }

                if (Parameters.TryGetScalar(out var roughnessMin, "RoughnessMin", "SpecRoughnessMin"))
                    RoughnessMin = roughnessMin;
                if (Parameters.TryGetScalar(out var roughnessMax, "RoughnessMax", "SpecRoughnessMax"))
                    RoughnessMax = roughnessMax;
                if (Parameters.TryGetScalar(out var roughness, "Rough", "Roughness", "Ro Multiplier", "RO_mul", "Roughness_Mult"))
                {
                    var d = roughness / 2;
                    RoughnessMin = roughness - d;
                    RoughnessMax = roughness + d;
                }

                if (Parameters.TryGetScalar(out var emissiveMultScalar, "emissive mult", "Emissive_Mult", "EmissiveIntensity", "EmissionIntensity"))
                    EmissiveMult = emissiveMultScalar;
                else if (Parameters.TryGetLinearColor(out var emissiveMultColor, "Emissive Multiplier", "EmissiveMultiplier"))
                    EmissiveMult = emissiveMultColor.R;

                if (Parameters.TryGetLinearColor(out var EmissiveUVs,
                        "EmissiveUVs_RG_UpperLeftCorner_BA_LowerRightCorner",
                        "Emissive Texture UVs RG_TopLeft BA_BottomRight",
                        "Emissive 2 UV Positioning (RG)UpperLeft (BA)LowerRight",
                        "EmissiveUVPositioning (RG)UpperLeft (BA)LowerRight",
                        "Emissive_CH", "EmissiveColor4LM", "Emissive Sphere Center"))
                    EmissiveRegion = new Vector4(EmissiveUVs.R, EmissiveUVs.G, EmissiveUVs.B, EmissiveUVs.A);

                if ((Parameters.TryGetSwitch(out var swizzleRoughnessToGreen, "SwizzleRoughnessToGreen") && swizzleRoughnessToGreen) ||
                    Parameters.Textures.ContainsKey("SRM"))
                {
                    foreach (var specMask in SpecularMasks)
                    {
                        specMask.SwizzleMask = new []
                        {
                            (int) PixelFormat.Red,
                            (int) PixelFormat.Blue,
                            (int) PixelFormat.Green,
                            (int) PixelFormat.Alpha
                        };
                        specMask.Swizzle();
                    }
                }
            }

            HasSpecularMask = SpecularMasks.Any(tex => tex != null);
        }
    }

    /// <param name="options">just the cache object</param>
    /// <param name="uvCount">number of item in the array</param>
    /// <param name="top">has at least 1 clearly defined texture, else will go straight to fallback</param>
    /// <param name="triggers">list of texture parameter names by uv channel</param>
    /// <param name="fallback">fallback texture name to use if no top texture found</param>
    /// <param name="first">if no top texture, no fallback texture, then use the first texture found</param>
    private Texture[] FillTextures(Options options, int uvCount, bool top, string[][] triggers, string fallback, bool first = false)
    {
        UTexture original;
        Texture transformed;
        var fix = fallback == CMaterialParams2.FallbackSpecularMasks;
        var textures = new Texture[uvCount];

        if (top)
        {
            for (int i = 0; i < textures.Length; i++)
            {
                if (Parameters.TryGetTexture2d(out original, triggers[i]) && options.TryGetTexture(original, fix, out transformed))
                    textures[i] = transformed;
                else if (i > 0 && textures[i - 1] != null)
                    textures[i] = textures[i - 1];
            }
        }
        else if (Parameters.TryGetTexture2d(out original, fallback) && options.TryGetTexture(original, fix, out transformed))
        {
            for (int i = 0; i < textures.Length; i++)
                textures[i] = transformed;
        }
        else if (first && Parameters.TryGetFirstTexture2d(out original) && options.TryGetTexture(original, fix, out transformed))
        {
            for (int i = 0; i < textures.Length; i++)
                textures[i] = transformed;
        }
        return textures;
    }

    /// <param name="uvCount">number of item in the array</param>
    /// <param name="textures">reference array</param>
    /// <param name="triggers">list of color parameter names by uv channel</param>
    /// <param name="fallback">fallback color to use if no trigger was found</param>
    private Vector4[] FillColors(int uvCount, Texture[] textures, string[][] triggers, Vector4 fallback)
    {
        var colors = new Vector4[uvCount];
        for (int i = 0; i < colors.Length; i++)
        {
            if (textures[i] == null) continue;

            if (Parameters.TryGetLinearColor(out var color, triggers[i]) && color is { A: > 0 })
            {
                colors[i] = new Vector4(color.R, color.G, color.B, color.A);
            }
            else colors[i] = fallback;
        }
        return colors;
    }

    public void ApplyColorShift(Vector4? overrideColor)
    {
        if (overrideColor is { } color)
        {
            HasColorShift = true;
            ColorShift = new Vector3(color.X, color.Y, color.Z);
            ColorShiftStrength = MathF.Abs(color.W) > float.Epsilon ? color.W : 1f;
        }
    }

    internal void ApplyOverlay(OverlayMaskData overlay, Options options, bool verbose)
    {
        MudMaskTexture = null;
        SnowMaskTexture = null;
        MudMaskStrengthUniform = overlay.MudStrength ?? 1f;
        MudMaskTightnessUniform = overlay.MudTightness ?? 1f;
        SnowMaskStrengthUniform = overlay.SnowStrength ?? 1f;
        SnowMaskTightnessUniform = overlay.SnowTightness ?? 1f;

        if (overlay.MudMask != null && options.TryGetTexture(overlay.MudMask, false, out var mudTex))
            MudMaskTexture = mudTex;

        if (overlay.SnowMask != null && options.TryGetTexture(overlay.SnowMask, false, out var snowTex))
            SnowMaskTexture = snowTex;
    }

    public void Render(Shader shader)
    {
        var unit = 0;
        for (var i = 0; i < Diffuse.Length; i++)
        {
            if (unit >= 32) { Console.WriteLine("[gl] diffuse texture unit overflow"); break; }
            shader.SetUniform($"uParameters.Diffuse[{i}].Sampler", unit);
            shader.SetUniform($"uParameters.Diffuse[{i}].Color", DiffuseColor[i]);
            Diffuse[i]?.Bind(TextureUnit.Texture0 + unit++);
        }
        var errAfterDiffuse = GL.GetError();
        if (errAfterDiffuse != ErrorCode.NoError)
            Console.WriteLine($"[gl] diffuse bind error: {errAfterDiffuse}");

        for (var i = 0; i < Normals.Length; i++)
        {
            if (unit >= 32) { Console.WriteLine("[gl] normal texture unit overflow"); break; }
            shader.SetUniform($"uParameters.Normals[{i}].Sampler", unit);
            Normals[i]?.Bind(TextureUnit.Texture0 + unit++);
        }
        var errAfterNormals = GL.GetError();
        if (errAfterNormals != ErrorCode.NoError)
            Console.WriteLine($"[gl] normals bind error: {errAfterNormals}");

        for (var i = 0; i < SpecularMasks.Length; i++)
        {
            if (unit >= 32) { Console.WriteLine("[gl] spec texture unit overflow"); break; }
            shader.SetUniform($"uParameters.SpecularMasks[{i}].Sampler", unit);
            SpecularMasks[i]?.Bind(TextureUnit.Texture0 + unit++);
        }
        var errAfterSpec = GL.GetError();
        if (errAfterSpec != ErrorCode.NoError)
            Console.WriteLine($"[gl] spec bind error: {errAfterSpec}");

        for (var i = 0; i < Emissive.Length; i++)
        {
            if (unit >= 32) { Console.WriteLine("[gl] emissive texture unit overflow"); break; }
            shader.SetUniform($"uParameters.Emissive[{i}].Sampler", unit);
            shader.SetUniform($"uParameters.Emissive[{i}].Color", EmissiveColor[i]);
            Emissive[i]?.Bind(TextureUnit.Texture0 + unit++);
        }
        var errAfterEmissive = GL.GetError();
        if (errAfterEmissive != ErrorCode.NoError)
            Console.WriteLine($"[gl] emissive bind error: {errAfterEmissive}");

        if (MudMaskTexture != null && unit < 32)
        {
            shader.SetUniform("uParameters.MudMask", unit);
            MudMaskTexture.Bind(TextureUnit.Texture0 + unit++);
        }
        if (SnowMaskTexture != null && unit < 32)
        {
            shader.SetUniform("uParameters.SnowMask", unit);
            SnowMaskTexture.Bind(TextureUnit.Texture0 + unit++);
        }
        var errAfterMasks = GL.GetError();
        if (errAfterMasks != ErrorCode.NoError)
            Console.WriteLine($"[gl] overlay bind error: {errAfterMasks}");

        Ao.Texture?.Bind(TextureUnit.Texture31);
        shader.SetUniform("uParameters.Ao.Sampler", 31);
        shader.SetUniform("uParameters.Ao.HasColorBoost", Ao.HasColorBoost);
        shader.SetUniform("uParameters.Ao.ColorBoost.Color", Ao.ColorBoost.Color);
        shader.SetUniform("uParameters.Ao.ColorBoost.Exponent", Ao.ColorBoost.Exponent);
        shader.SetUniform("uParameters.HasAo", HasAo);

        shader.SetUniform("uParameters.EmissiveRegion", EmissiveRegion);
        shader.SetUniform("uParameters.RoughnessMin", RoughnessMin);
        shader.SetUniform("uParameters.RoughnessMax", RoughnessMax);
        shader.SetUniform("uParameters.EmissiveMult", EmissiveMult);
        shader.SetUniform("uParameters.HasSpecularMask", HasSpecularMask);
        shader.SetUniform("uParameters.HasColorShift", HasColorShift);
        shader.SetUniform("uParameters.ColorShift", new Vector4(ColorShift, ColorShiftStrength));
        shader.SetUniform("uParameters.MudLevel", MudLevel);
        shader.SetUniform("uParameters.SnowLevel", SnowLevel);
        shader.SetUniform("uParameters.MudTint", MudTint);
        shader.SetUniform("uParameters.SnowTint", SnowTint);
        shader.SetUniform("uParameters.HasMudMask", MudMaskTexture != null);
        shader.SetUniform("uParameters.MudMaskStrength", MudMaskStrengthUniform);
        shader.SetUniform("uParameters.MudMaskTightness", MudMaskTightnessUniform);
        shader.SetUniform("uParameters.HasSnowMask", SnowMaskTexture != null);
        shader.SetUniform("uParameters.SnowMaskStrength", SnowMaskStrengthUniform);
        shader.SetUniform("uParameters.SnowMaskTightness", SnowMaskTightnessUniform);
        var errAfterUniforms = GL.GetError();
        if (errAfterUniforms != ErrorCode.NoError)
            Console.WriteLine($"[gl] parameter uniform error: {errAfterUniforms}");
    }

    public void Dispose()
    {
        for (int i = 0; i < Diffuse.Length; i++)
        {
            Diffuse[i]?.Dispose();
        }
        for (int i = 0; i < Normals.Length; i++)
        {
            Normals[i]?.Dispose();
        }
        for (int i = 0; i < SpecularMasks.Length; i++)
        {
            SpecularMasks[i]?.Dispose();
        }
        for (int i = 0; i < Emissive.Length; i++)
        {
            Emissive[i]?.Dispose();
        }
        Ao.Texture?.Dispose();
        GL.DeleteProgram(_handle);
    }
}

public struct AoParams
{
    public Texture Texture;

    public Boost ColorBoost;
    public bool HasColorBoost;
}

public struct Boost
{
    public Vector3 Color;
    public float Exponent;
}

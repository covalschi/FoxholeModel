using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Meshes.PSK;
using FModel.Views.Snooper;
using FModel.Views.Snooper.Buffers;
using FModel.Views.Snooper.Lights;
using FModel.Views.Snooper.Models;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SysVector3 = System.Numerics.Vector3;
using SysVector4 = System.Numerics.Vector4;

namespace FModelHeadless.Rendering;

internal sealed class HeadlessRenderWindow : GameWindow
{
    private readonly ResolvedRootAsset _root;
    private readonly UObject _asset;
    private readonly Lazy<UObject> _lazyAsset;
    private readonly string _outputPath;
    private readonly int _width;
    private readonly int _height;
    private readonly float _pitchRad;
    private readonly float _yawRad;
    private readonly float _orbitRadius;
    private readonly bool _verbose;
    private readonly bool _transparent;
    private readonly IReadOnlyList<ResolvedAttachmentDescriptor> _attachments;
    private readonly FModelHeadless.Cli.SceneFilters? _filters;
    private readonly List<(Guid id, ResolvedAttachmentDescriptor descriptor)> _attachmentModels = new();

    private Renderer _renderer = null!;
    private FramebufferObject _framebuffer = null!;
    private bool _initialized;

    public HeadlessRenderWindow(ResolvedRootAsset root, string outputPath, int width, int height, float pitchDeg, float yawDeg, float orbitRadius, bool verbose, bool transparent, IReadOnlyList<ResolvedAttachmentDescriptor> attachments, FModelHeadless.Cli.SceneFilters? filters)
        : base(new GameWindowSettings { UpdateFrequency = 60 }, new NativeWindowSettings
        {
            ClientSize = new Vector2i(width, height),
            StartVisible = false,
            StartFocused = false,
            WindowBorder = WindowBorder.Hidden,
            APIVersion = new Version(4, 1),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible
        })
    {
        _root = root;
        _asset = root.Asset;
        _lazyAsset = new Lazy<UObject>(() => root.Asset);
        _outputPath = outputPath;
        _width = width;
        _height = height;
        _pitchRad = MathHelper.DegreesToRadians(pitchDeg);
        _yawRad = MathHelper.DegreesToRadians(yawDeg);
        _orbitRadius = orbitRadius;
        _verbose = verbose;
        _transparent = transparent;
        _attachments = attachments ?? Array.Empty<ResolvedAttachmentDescriptor>();
        _filters = filters;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        Context.MakeCurrent();

        GL.ClearColor(0f, 0f, 0f, _transparent ? 0f : 1f);
        GL.Enable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Multisample);
        GL.Enable(EnableCap.VertexProgramPointSize);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.StencilOp(StencilOp.Keep, StencilOp.Replace, StencilOp.Replace);

        _renderer = new Renderer(_width, _height);
        _framebuffer = new FramebufferObject(new Vector2i(_width, _height));
        _renderer.ShowSkybox = false;
        _renderer.ShowGrid = false;
        _renderer.ShowLights = true;
        _renderer.Color = VertexColor.Default;

        _framebuffer.Setup();
        _renderer.Setup();
        _renderer.SetDebugSolidColor(false);
        _renderer.Load(CancellationToken.None, _asset, _lazyAsset);
        Console.WriteLine($"[init] models after load: {_renderer.Options.Models.Count}");
        _renderer.Options.SetupModelsAndLights();
        _renderer.Options.SwapMaterial(false);
        _renderer.Options.AnimateMesh(false);

        AddAttachments();

        var primaryModel = _renderer.Options.Models.Values.FirstOrDefault();
        if (primaryModel != null)
        {
            ApplyVisualProperties(primaryModel, _root.Visual, _root.Overlay);
            ApplyMaterialVisibility(primaryModel);
            primaryModel.IsVisible = true;
            foreach (var section in primaryModel.Sections)
            {
                section.Show = true;
            }
            // Re-apply filters after default visibility was set
            ApplyMaterialVisibility(primaryModel);
            if (_verbose)
            {
                Console.WriteLine($"[headless] root visual hp={_root.Visual.HpState} mud={_root.Visual.MudLevel:F2} snow={_root.Visual.SnowLevel:F2}");
                Console.WriteLine($"[headless] root transforms={primaryModel.TransformsCount} visible={primaryModel.IsVisible} sections={primaryModel.Sections.Length}");
            }

            EnsureDefaultLights(primaryModel);
            if (_verbose)
            {
                for (var i = 0; i < _renderer.Options.Lights.Count; i++)
                {
                    var l = _renderer.Options.Lights[i];
                    Console.WriteLine($"[light] idx={i} pos={l.Transform.Matrix.Translation} color={l.Color} intensity={l.Intensity}");
                }
            }


            if (_verbose)
            {
                Console.WriteLine($"[headless] sockets for {primaryModel.Name}:");
                for (var i = 0; i < primaryModel.Sockets.Count; i++)
                {
                    var socket = primaryModel.Sockets[i];
                    Console.WriteLine($"  socket[{i}] name={socket.Name} bone={socket.BoneName} pos=({socket.Transform.Position.X:F2},{socket.Transform.Position.Y:F2},{socket.Transform.Position.Z:F2})");
                }
            }
        }

        if (_verbose)
        {
            Console.WriteLine($"[headless] lights: {_renderer.Options.Lights.Count}");
            Console.WriteLine($"[headless] vertex color mode: {_renderer.Color}");
            for (var i = 0; i < _renderer.Options.Lights.Count; i++)
            {
                var light = _renderer.Options.Lights[i];
                Console.WriteLine($"  light[{i}] setup={light.IsSetup} pos={light.Transform.Matrix.Translation}");
            }
            foreach (var (guid, model) in _renderer.Options.Models)
            {
                Console.WriteLine($"[headless] model {model.Name} guid={guid}");
                Console.WriteLine($"    hasVertexColors={model.HasVertexColors}");
                for (var i = 0; i < model.Materials.Length; i++)
                {
                    var material = model.Materials[i];
                    if (material == null)
                        continue;

                    Console.WriteLine($"  material[{i}] {material.Name} diffuse={DescribeTextures(material.Diffuse)} normals={DescribeTextures(material.Normals)} spec={DescribeTextures(material.SpecularMasks)}");
                    Console.WriteLine($"    diffuse colors: {DescribeColors(material.DiffuseColor)} emissive colors: {DescribeColors(material.EmissiveColor)} emissive mult={material.EmissiveMult}");
                }

                if (model.Vertices is { Length: > 0 })
                {
                    var size = model.VertexSize;
                    var sampleCount = Math.Min(3, model.Vertices.Length / size);
                    for (var v = 0; v < sampleCount; v++)
                    {
                        var offset = v * size;
                        var index = model.Vertices[offset];
                        var posX = model.Vertices[offset + 1];
                        var posY = model.Vertices[offset + 2];
                        var posZ = model.Vertices[offset + 3];
                        var uvU = model.Vertices[offset + 10];
                        var uvV = model.Vertices[offset + 11];
                        Console.WriteLine($"    vertex {v} idx={index} pos=({posX:F2},{posY:F2},{posZ:F2}) uv=({uvU:F3},{uvV:F3})");
                    }
                }
            }
        }

        Console.WriteLine($"[init] total models={_renderer.Options.Models.Count}");
        foreach (var (guid, model) in _renderer.Options.Models)
        {
            model.Box.GetCenterAndExtents(out var boxCenter, out var boxExtents);
            Console.WriteLine($"[init] model={model.Name} guid={guid} boxCenter=({boxCenter.X:F3},{boxCenter.Y:F3},{boxCenter.Z:F3}) boxExt=({boxExtents.X:F3},{boxExtents.Y:F3},{boxExtents.Z:F3}) visible={model.IsVisible}");
        }

        ConfigureCamera();
        _renderer.Options.SelectModel(Guid.Empty);

        _initialized = true;
    }

    private void AddAttachments()
    {
        if (_attachments.Count == 0)
            return;

        foreach (var attachment in _attachments)
        {
            UModel model = null;
            var transform = ToSnooperTransform(attachment.Transform);
            switch (attachment.Asset)
            {
                case CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh sm:
                    if (!sm.TryConvert(out var staticConverted))
                    {
                        if (_verbose)
                            Console.Error.WriteLine($"[headless] failed to convert static mesh for attachment '{attachment.AssetId}'.");
                        continue;
                    }
                    model = new StaticModel(sm, staticConverted, transform) { IsProp = true };
                    break;
                case CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh sk:
                    if (!sk.TryConvert(out var skelConverted))
                    {
                        if (_verbose)
                            Console.Error.WriteLine($"[headless] failed to convert skeletal mesh for attachment '{attachment.AssetId}'.");
                        continue;
                    }
                    model = new SkeletalModel(sk, skelConverted, transform) { IsProp = true };
                    break;
                default:
                    if (_verbose)
                        Console.Error.WriteLine($"[headless] unsupported attachment asset type '{attachment.Asset?.ExportType}' for '{attachment.AssetId}'.");
                    continue;
            }

            // Apply component-level material overrides if present, mapped by section->material index
            if (attachment.MaterialOverrides != null && model != null && model.Materials != null && model.Sections != null && model.Sections.Length > 0)
            {
                for (var s = 0; s < model.Sections.Length; s++)
                {
                    var matIndex = model.Sections[s].MaterialIndex;
                    if (matIndex < 0 || matIndex >= model.Materials.Length || matIndex >= attachment.MaterialOverrides.Count)
                        continue;
                    var overrideMat = attachment.MaterialOverrides[matIndex];
                    if (overrideMat == null || model.Materials[matIndex] == null) continue;
                    model.Materials[matIndex].SwapMaterial(overrideMat);
                    if (_verbose)
                    {
                        try { Console.WriteLine($"[override] {model.Name} section={s} matIndex={matIndex} -> {overrideMat.Name}"); } catch { }
                    }
                }
            }

            model.IsVisible = true;
            foreach (var section in model.Sections)
            {
                section.Show = true;
            }

            var guid = Guid.NewGuid();
            _renderer.Options.Models[guid] = model;
            _attachmentModels.Add((guid, attachment));

            if (_verbose)
            {
                try
                {
                    Console.WriteLine($"[headless] attachment {attachment.Asset.GetPathName()} hp={attachment.Visual.HpState} mud={attachment.Visual.MudLevel:F2} snow={attachment.Visual.SnowLevel:F2}");
                }
                catch
                {
                    Console.WriteLine($"[headless] attachment <unknown-path> hp={attachment.Visual.HpState} mud={attachment.Visual.MudLevel:F2} snow={attachment.Visual.SnowLevel:F2}");
                }
                if (attachment.StockpileOptions.Count > 0)
                {
                    foreach (var (itemPath, quantity) in attachment.StockpileOptions)
                    {
                        Console.WriteLine($"  stockpile option: item={itemPath} amount={quantity}");
                    }
                }
            }
        }

        _renderer.Options.SetupModelsAndLights();

        foreach (var (id, request) in _attachmentModels)
        {
            if (!_renderer.Options.Models.TryGetValue(id, out var model))
                continue;

            ApplyVisualProperties(model, request.Visual, request.Overlay);
            ApplyMaterialVisibility(model);
        }
    }

    private void ApplyMaterialVisibility(FModel.Views.Snooper.Models.UModel model)
    {
        if (_filters == null) return;
        var showOnly = _filters.ShowMaterials;
        var hide = _filters.HideMaterials;

        bool Matches(string name, string path, string token)
            => !string.IsNullOrWhiteSpace(token) &&
               (name?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                path?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

        for (var s = 0; s < model.Sections.Length; s++)
        {
            var sec = model.Sections[s];
            var idx = sec.MaterialIndex;
            if (idx < 0 || idx >= model.Materials.Length) continue;
            var mat = model.Materials[idx];
            var name = mat?.Name;
            var path = mat?.Path;

            if (showOnly is { Length: > 0 })
            {
                var any = false;
                foreach (var t in showOnly)
                {
                    if (Matches(name, path, t)) { any = true; break; }
                }
                sec.Show = any;
                continue;
            }

            if (hide is { Length: > 0 })
            {
                foreach (var t in hide)
                {
                    if (Matches(name, path, t)) { sec.Show = false; break; }
                }
            }
        }
    }

    private static Transform ToSnooperTransform(FTransform transform)
    {
        var snooper = Transform.Identity;
        snooper.Position = transform.Translation * Constants.SCALE_DOWN_RATIO;
        snooper.Rotation = transform.Rotation;
        snooper.Scale = transform.Scale3D;
        return snooper;
    }

    private void ApplyVisualProperties(FModel.Views.Snooper.Models.UModel model, AssetVisualProperties visual, OverlayMaskData overlay)
    {
        for (var materialIndex = 0; materialIndex < model.Materials.Length; materialIndex++)
        {
            var material = model.Materials[materialIndex];
            if (material == null)
                continue;

            if (!ShouldApplyMaterial(materialIndex, material, visual))
                continue;

            if (_verbose)
            {
                Console.WriteLine($"    material[{materialIndex}] {material.Name} params colors={string.Join(',', material.Parameters.Colors.Keys)} scalars={string.Join(',', material.Parameters.Scalars.Keys)} textures={string.Join(',', material.Parameters.Textures.Keys)}");
                foreach (var kvp in material.Parameters.Textures)
                {
                    try
                    {
                        Console.WriteLine($"      texture param '{kvp.Key}': asset={kvp.Value?.GetPathName() ?? "<null>"}");
                    }
                    catch
                    {
                        Console.WriteLine($"      texture param '{kvp.Key}': <path unavailable>");
                    }
                }
                foreach (var kvp in material.Parameters.Scalars)
                {
                    Console.WriteLine($"      scalar '{kvp.Key}' = {kvp.Value}");
                }
                foreach (var kvp in material.Parameters.Colors)
                {
                    var c = kvp.Value;
                    Console.WriteLine($"      color '{kvp.Key}' = ({c.R:F3},{c.G:F3},{c.B:F3},{c.A:F3})");
                }
            }

            material.MudLevel = Math.Clamp(visual.MudLevel, 0f, 1f);
            material.SnowLevel = Math.Clamp(visual.SnowLevel, 0f, 1f);
            material.ApplyColorShift(visual.DiffuseOverride);
            material.ApplyOverlay(overlay, _renderer.Options, _verbose);

            if (_verbose)
            {
                var shiftDesc = material.HasColorShift ? $"({material.ColorShift.X:F3},{material.ColorShift.Y:F3},{material.ColorShift.Z:F3})" : "<none>";
                Console.WriteLine($"      visual overrides -> colorShift={shiftDesc} mud={material.MudLevel:F2} snow={material.SnowLevel:F2}");
            }
        }
    }

    private static bool ShouldApplyMaterial(int materialIndex, FModel.Views.Snooper.Shading.Material material, AssetVisualProperties visual)
    {
        if (visual.ColorMaterialIndex.HasValue && visual.ColorMaterialIndex.Value != materialIndex)
            return false;

        return true;
    }

    private static string DescribeTextures(FModel.Views.Snooper.Shading.Texture[] textures)
    {
        if (textures == null || textures.Length == 0)
            return "(none)";

        var status = textures
            .Select((tex, idx) => tex == null ? $"[{idx}]null" : $"[{idx}]({tex.Width}x{tex.Height})")
            .ToArray();
        return string.Join(' ', status);
    }

    private static string DescribeColors(System.Numerics.Vector4[] colors)
    {
        if (colors == null || colors.Length == 0)
            return "(none)";

        return string.Join(' ', colors.Select(c => $"({c.X:F2},{c.Y:F2},{c.Z:F2},{c.W:F2})"));
    }

    private void EnsureDefaultLights(UModel model)
    {
        if (_renderer.Options.Lights.Count > 0)
            return;

        model.Box.GetCenterAndExtents(out var center, out var extents);

        var centerVec = new SysVector3(center.X, center.Z, center.Y);
        var up = new SysVector3(0f, MathF.Max(extents.Z * 3f, 2f), 0f);
        var forward = new SysVector3(0f, 0f, MathF.Max(extents.Y * 2f, 2f));
        var right = new SysVector3(MathF.Max(extents.X * 2f, 2f), 0f, 0f);
        var radius = MathF.Max(MathF.Max(Math.Abs(extents.X), Math.Abs(extents.Y)), Math.Abs(extents.Z));
        radius = MathF.Max(radius, 1f);

        var positions = new SysVector3[]
        {
            centerVec + up + forward,
            centerVec + up - forward,
            centerVec + up + right * 0.5f
        };

        foreach (var pos in positions)
        {
            var transform = new Transform
            {
                Position = new FVector(pos.X, pos.Z, pos.Y)
            };

            var icon = _renderer.Options.Icons.TryGetValue("light", out var tex) ? tex : _renderer.Options.Icons["cube"];
            var light = new PointLight(icon, transform, new SysVector4(1f, 1f, 1f, 1f), 5f, radius * 2f);
            _renderer.Options.Lights.Add(light);
            light.Setup();
        }

        _renderer.ShowLights = true;
    }

    private void ConfigureCamera()
    {
        if (_renderer.Options.Models.Count == 0)
            return;

        var (center, extents) = CalculateSceneBounds();
        if (_verbose)
        {
            Console.WriteLine($"[camera] bounds center=({center.X:F3},{center.Y:F3},{center.Z:F3}) extents=({extents.X:F3},{extents.Y:F3},{extents.Z:F3})");
        }

        var radius = Math.Max(Math.Max(Math.Abs(extents.X), Math.Abs(extents.Y)), Math.Abs(extents.Z));
        radius = Math.Max(radius, 1f);

        var distance = _orbitRadius > 0f ? _orbitRadius : Math.Max(radius * 2.5f, 1f);
        var dir = new Vector3(
            MathF.Cos(_pitchRad) * MathF.Cos(_yawRad),
            MathF.Sin(_pitchRad),
            MathF.Cos(_pitchRad) * MathF.Sin(_yawRad));

        var cameraPosTk = new Vector3(center.X, center.Y, center.Z) + dir * distance;
        if (_verbose)
        {
            Console.WriteLine($"[camera] orbit distance={distance:F3} pitch={_pitchRad * 180f / MathF.PI:F1} yaw={_yawRad * 180f / MathF.PI:F1} position=({cameraPosTk.X:F3},{cameraPosTk.Y:F3},{cameraPosTk.Z:F3})");
        }

        var camera = _renderer.CameraOp;
        camera.Mode = Camera.WorldMode.Arcball;
        camera.Position = new System.Numerics.Vector3(cameraPosTk.X, cameraPosTk.Y, cameraPosTk.Z);
        camera.Direction = new System.Numerics.Vector3(center.X, center.Y, center.Z);
        camera.Far = Math.Max(camera.Far, distance * 4f);
        camera.Speed = Math.Max(camera.Speed, distance);
        if (_verbose)
        {
            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            var worldCenter = new System.Numerics.Vector4(center.X, center.Y, center.Z, 1f);
            var viewPos = System.Numerics.Vector4.Transform(worldCenter, view);
            var clipPos = System.Numerics.Vector4.Transform(viewPos, proj);
            Console.WriteLine($"[camera] viewCenter=({viewPos.X:F3},{viewPos.Y:F3},{viewPos.Z:F3},{viewPos.W:F3}) clipCenter=({clipPos.X:F3},{clipPos.Y:F3},{clipPos.Z:F3},{clipPos.W:F3})");
        }
    }

    private (SysVector3 center, SysVector3 extents) CalculateSceneBounds()
    {
        Console.WriteLine($"[models] count={_renderer.Options.Models.Count}");
        var min = new SysVector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new SysVector3(float.MinValue, float.MinValue, float.MinValue);
        var hasModel = false;

        foreach (var model in _renderer.Options.Models.Values)
        {
            model.Box.GetCenterAndExtents(out var localCenter, out var localExtents);
            var transform = model.Transforms.FirstOrDefault();
            var pos = transform?.Position ?? FVector.ZeroVector;

            Console.WriteLine($"[bounds] raw center=({localCenter.X:F3},{localCenter.Y:F3},{localCenter.Z:F3}) pos=({pos.X:F3},{pos.Y:F3},{pos.Z:F3}) ext=({localExtents.X:F3},{localExtents.Y:F3},{localExtents.Z:F3})");

            var centerTk = new SysVector3(localCenter.X + pos.X, localCenter.Z + pos.Z, localCenter.Y + pos.Y);
            var extTk = new SysVector3(localExtents.X, localExtents.Z, localExtents.Y);
            var localMin = centerTk - extTk;
            var localMax = centerTk + extTk;

            min = SysVector3.Min(min, localMin);
            max = SysVector3.Max(max, localMax);
            hasModel = true;
        }

        if (!hasModel)
            return (SysVector3.Zero, new SysVector3(1f, 1f, 1f));

        var center = (min + max) * 0.5f;
        var extents = (max - min) * 0.5f;
        Console.WriteLine($"[bounds] min=({min.X:F3},{min.Y:F3},{min.Z:F3}) max=({max.X:F3},{max.Y:F3},{max.Z:F3})");
        Console.WriteLine($"[bounds] center=({center.X:F3},{center.Y:F3},{center.Z:F3}) ext=({extents.X:F3},{extents.Y:F3},{extents.Z:F3})");
        return (center, extents);
    }

    public void RenderOnce()
    {
        EnsureInitialized();
        Context.MakeCurrent();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, _width, _height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

        _renderer.Render();
        var err = GL.GetError();
        if (err != ErrorCode.NoError)
            Console.WriteLine($"[gl] error after render: {err}");

        SaveFrame();
        _renderer.Save();
        SwapBuffers();
        Close();
    }

    private void SaveFrame()
    {
        var pixels = new byte[_width * _height * 4];
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        GL.ReadBuffer(ReadBufferMode.Back);
        GL.ReadPixels(0, 0, _width, _height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        using var image = Image.LoadPixelData<Bgra32>(pixels, _width, _height);
        image.Mutate(ctx => ctx.Flip(FlipMode.Vertical));
        Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
        image.SaveAsPng(_outputPath);
        Console.WriteLine($"[headless] Saved {_outputPath}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _renderer?.Dispose();
            _framebuffer?.Dispose();
        }
    }
}

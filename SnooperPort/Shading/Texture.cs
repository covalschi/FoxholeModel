using System;
using System.Numerics;
using CUE4Parse_Conversion.Textures;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using OpenTK.Graphics.OpenGL4;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace FModel.Views.Snooper.Shading;

public class Texture : IDisposable
{
    private readonly int _handle;
    private readonly TextureType _type;
    private readonly TextureTarget _target;

    public readonly string Type;
    public readonly FGuid Guid;
    public readonly string Name = string.Empty;
    public readonly string Path = string.Empty;
    public readonly EPixelFormat Format;
    public readonly uint ImportedWidth;
    public readonly uint ImportedHeight;
    public int Width;
    public int Height;

    private const int DisabledChannel = (int)BlendingFactor.Zero;
    public int[] SwizzleMask =
    [
        (int) PixelFormat.Red,
        (int) PixelFormat.Green,
        (int) PixelFormat.Blue,
        (int) PixelFormat.Alpha
    ];

    private Texture(TextureType type)
    {
        _handle = GL.GenTexture();
        _type = type;
        _target = _type switch
        {
            TextureType.Cubemap => TextureTarget.TextureCubeMap,
            TextureType.MsaaFramebuffer => TextureTarget.Texture2DMultisample,
            _ => TextureTarget.Texture2D
        };

        Guid = new FGuid();
    }

    public Texture(uint width, uint height) : this(TextureType.MsaaFramebuffer)
    {
        Width = (int) width;
        Height = (int) height;
        Bind(TextureUnit.Texture0);

        GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, Constants.SAMPLES_COUNT, PixelInternalFormat.Rgba8, Width, Height, true);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, _target, _handle, 0);
    }

    public Texture(int width, int height) : this(TextureType.Framebuffer)
    {
        Width = width;
        Height = height;
        Bind(TextureUnit.Texture0);

        GL.TexImage2D(_target, 0, PixelInternalFormat.Rgba8, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

        GL.TexParameter(_target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.TexParameter(_target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
        GL.TexParameter(_target, TextureParameterName.TextureWrapS, (int) TextureWrapMode.ClampToEdge);
        GL.TexParameter(_target, TextureParameterName.TextureWrapT, (int) TextureWrapMode.ClampToEdge);

        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, _target, _handle, 0);
    }

    public Texture(SKBitmap bitmap, UTexture texture2D) : this(TextureType.Normal)
    {
        Type = texture2D.ExportType;
        Guid = texture2D.LightingGuid;
        Name = texture2D.Name;
        Path = texture2D.GetPathName();
        Format = texture2D.Format;
        Width = bitmap.Width;
        Height = bitmap.Height;
        Bind(TextureUnit.Texture0);

        var internalFormat = bitmap.ColorType switch
        {
            SKColorType.Gray8 => PixelInternalFormat.R8,
            _ => texture2D.SRGB ? PixelInternalFormat.SrgbAlpha : PixelInternalFormat.Rgba
        };

        var pixelFormat = bitmap.ColorType switch
        {
            SKColorType.Gray8 => PixelFormat.Red,
            SKColorType.Bgra8888 => PixelFormat.Bgra,
            _ => PixelFormat.Rgba
        };

        var sample = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Console.WriteLine($"[texture] {texture2D.Name} sample rgba=({sample.Red},{sample.Green},{sample.Blue},{sample.Alpha}) srgb={(texture2D.SRGB ? "yes" : "no")}");
        if (texture2D.Name.Contains("FlatbedTruck", StringComparison.OrdinalIgnoreCase) ||
            texture2D.Name.Contains("ShippingContainer", StringComparison.OrdinalIgnoreCase))
        {
            var path = System.IO.Path.Combine(Environment.CurrentDirectory, "debug_textures", texture2D.Name + ".png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var fs = System.IO.File.Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
            data.SaveTo(fs);
        }

        GL.TexImage2D(_target, 0, internalFormat, Width, Height, 0, pixelFormat, PixelType.UnsignedByte, bitmap.Bytes);
        GL.TexParameter(_target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(_target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
        GL.TexParameter(_target, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(_target, TextureParameterName.TextureMaxLevel, 8);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        bitmap.Dispose();
    }

    public Texture(FLinearColor color) : this(TextureType.Normal)
    {
        Type = "LinearColor";
        Name = color.Hex;
        Width = 1;
        Height = 1;
        Bind(TextureUnit.Texture0);

        Span<float> data = stackalloc float[4] { color.R, color.G, color.B, color.A };
        unsafe
        {
            fixed (float* ptr = data)
            {
                GL.TexImage2D(_target, 0, PixelInternalFormat.Rgba, Width, Height, 0, PixelFormat.Rgba, PixelType.Float, (nint)ptr);
            }
        }
        GL.TexParameter(_target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.TexParameter(_target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
    }

    public Texture(string[] textures) : this(TextureType.Cubemap)
    {
        Bind(TextureUnit.Texture0);
        for (int face = 0; face < textures.Length; face++)
        {
            using var bitmap = CreateIconBitmap(textures[face], true);
            UploadBitmap(bitmap, TextureTarget.TextureCubeMapPositiveX + face, true);
        }

        GL.TexParameter(_target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.TexParameter(_target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
        GL.TexParameter(_target, TextureParameterName.TextureWrapR, (int) TextureWrapMode.ClampToEdge);
        GL.TexParameter(_target, TextureParameterName.TextureWrapS, (int) TextureWrapMode.ClampToEdge);
        GL.TexParameter(_target, TextureParameterName.TextureWrapT, (int) TextureWrapMode.ClampToEdge);
    }

    public Texture(string texture) : this(TextureType.Normal)
    {
        Bind(TextureUnit.Texture0);
        using var bitmap = CreateIconBitmap(texture, false);
        UploadBitmap(bitmap, _target, true);
    }

    private void UploadBitmap(SKBitmap bitmap, TextureTarget target, bool srgb)
    {
        Width = bitmap.Width;
        Height = bitmap.Height;

        var internalFormat = srgb ? PixelInternalFormat.SrgbAlpha : PixelInternalFormat.Rgba;
        GL.TexImage2D(target, 0, internalFormat, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, bitmap.Bytes);
        GL.TexParameter(target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.TexParameter(target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
    }

    private static SKBitmap CreateIconBitmap(string key, bool cubemap)
    {
        var size = cubemap ? 4 : 2;
        var bitmap = new SKBitmap(size, size);
        SKColor main = key switch
        {
            "checker" => new SKColor(180, 180, 180, 255),
            "light" => new SKColor(0, 0, 0, 0),
            "light_off" => new SKColor(0, 0, 0, 0),
            "cube" => new SKColor(120, 170, 255, 255),
            "cube_off" => new SKColor(60, 60, 60, 255),
            "square" => new SKColor(120, 120, 120, 255),
            "square_off" => new SKColor(40, 40, 40, 255),
            _ => new SKColor(200, 200, 200, 255)
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (key == "checker")
                {
                    var toggle = (x + y) % 2 == 0;
                    bitmap.SetPixel(x, y, toggle ? new SKColor(220, 220, 220, 255) : new SKColor(90, 90, 90, 255));
                }
                else
                {
                    bitmap.SetPixel(x, y, main);
                }
            }
        }

        return bitmap;
    }

    public void Bind(TextureUnit textureSlot)
    {
        GL.ActiveTexture(textureSlot);
        Bind(_target);
    }

    public void Bind(TextureTarget target)
    {
        GL.BindTexture(target, _handle);
    }

    public void Bind()
    {
        GL.BindTexture(_target, _handle);
    }

    public void Swizzle()
    {
        Bind();
        GL.TexParameter(_target, TextureParameterName.TextureSwizzleRgba, SwizzleMask);
    }

    public IntPtr GetPointer() => (IntPtr)_handle;

    public void WindowResized(int width, int height)
    {
        Width = width;
        Height = height;

        Bind();
        switch (_type)
        {
            case TextureType.MsaaFramebuffer:
                GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, Constants.SAMPLES_COUNT, PixelInternalFormat.Rgb, Width, Height, true);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, _target, _handle, 0);
                break;
            case TextureType.Framebuffer:
                GL.TexImage2D(_target, 0, PixelInternalFormat.Rgb, Width, Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, _target, _handle, 0);
                break;
        }
    }

    public void Dispose()
    {
        GL.DeleteTexture(_handle);
    }
}

public enum TextureType
{
    Normal,
    Cubemap,
    Framebuffer,
    MsaaFramebuffer
}

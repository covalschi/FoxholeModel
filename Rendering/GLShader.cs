using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace FModelHeadless.Rendering;

internal sealed class GLShader : IDisposable
{
    private readonly int _handle;

    public GLShader(string vertexResource, string fragmentResource)
    {
        var vertex = CompileShader(ShaderType.VertexShader, vertexResource);
        var fragment = CompileShader(ShaderType.FragmentShader, fragmentResource);

        _handle = GL.CreateProgram();
        GL.AttachShader(_handle, vertex);
        GL.AttachShader(_handle, fragment);
        GL.LinkProgram(_handle);
        GL.GetProgram(_handle, GetProgramParameterName.LinkStatus, out var status);
        if (status == 0)
        {
            throw new InvalidOperationException($"Shader link failed: {GL.GetProgramInfoLog(_handle)}");
        }

        GL.DetachShader(_handle, vertex);
        GL.DetachShader(_handle, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
    }

    private static int CompileShader(ShaderType type, string resourceName)
    {
        var shader = GL.CreateShader(type);
        var source = LoadResource(resourceName, type);
        #if DEBUG
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].IndexOf("#version", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine($"[shader-debug] {resourceName} -> line {i + 1}: {lines[i]}");
            }
        }
        #endif
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var status);
        if (status == 0)
        {
            var info = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"Shader compile error ({type}): {info}");
        }
        return shader;
    }

    private static string LoadResource(string resourceName, ShaderType type)
    {
        var assembly = typeof(GLShader).Assembly;
        var fullName = $"{assembly.GetName().Name}.Resources.{resourceName}";
        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded shader '{fullName}' not found.");
        using var reader = new System.IO.StreamReader(stream);
        var content = reader.ReadToEnd();

        if (type == ShaderType.VertexShader && NeedsSplinePrelude(resourceName))
        {
            var prelude = LoadRawResource("spline.vert");
            content = RemoveVersionLine(content);
            content = prelude + Environment.NewLine + content;
        }

        return content;
    }

    private static bool NeedsSplinePrelude(string resourceName)
    {
        return resourceName is "default.vert" or "outline.vert" or "picking.vert";
    }

    private static string LoadRawResource(string resourceName)
    {
        var assembly = typeof(GLShader).Assembly;
        var fullName = $"{assembly.GetName().Name}.Resources.{resourceName}";
        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded shader '{fullName}' not found.");
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string RemoveVersionLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[0].TrimStart().StartsWith("#version", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('\n', lines.Skip(1));
        }
        return text;
    }


    public void Use()
    {
        GL.UseProgram(_handle);
    }

    public void SetMatrix4(string name, OpenTK.Mathematics.Matrix4 matrix)
    {
        var location = GL.GetUniformLocation(_handle, name);
        GL.UniformMatrix4(location, false, ref matrix);
    }

    public void SetVector3(string name, OpenTK.Mathematics.Vector3 vector)
    {
        var location = GL.GetUniformLocation(_handle, name);
        GL.Uniform3(location, vector);
    }

    public void SetInt(string name, int value)
    {
        var location = GL.GetUniformLocation(_handle, name);
        GL.Uniform1(location, value);
    }

    public void SetFloat(string name, float value)
    {
        var location = GL.GetUniformLocation(_handle, name);
        GL.Uniform1(location, value);
    }

    public void SetBool(string name, bool value)
    {
        var location = GL.GetUniformLocation(_handle, name);
        GL.Uniform1(location, value ? 1 : 0);
    }

    public void SetVector2(string name, OpenTK.Mathematics.Vector2 vector)
    {
        var location = GL.GetUniformLocation(_handle, name);
        GL.Uniform2(location, vector);
    }

    public void SetVector4(string name, OpenTK.Mathematics.Vector4 vector)
    {
        var location = GL.GetUniformLocation(_handle, name);
        GL.Uniform4(location, vector);
    }

    public void SetVector4(string name, System.Numerics.Vector4 vector)
    {
        SetVector4(name, new OpenTK.Mathematics.Vector4(vector.X, vector.Y, vector.Z, vector.W));
    }

    public void Dispose()
    {
        GL.DeleteProgram(_handle);
    }
}

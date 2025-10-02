using System;
using FModel.Views.Snooper.Shading;
using OpenTK.Graphics.OpenGL4;

namespace FModel.Views.Snooper.Buffers;

public class FramebufferObject : IDisposable
{
    private int _framebufferHandle;
    private int _postProcessingHandle;
    private int _finalHandle;

    private int _width;
    private int _height;
    private readonly RenderbufferObject _renderbuffer;

    private BufferObject<uint> _ebo;
    private BufferObject<float> _vbo;
    private VertexArrayObject<float, uint> _vao;

    private Shader _shader;
    private Texture _framebufferTexture;
    private Texture _postProcessingTexture;
    private Texture _finalTexture;

    public int PostProcessingHandle => _postProcessingHandle;
    public Texture PostProcessingTexture => _postProcessingTexture;
    public int FinalHandle => _finalHandle;
    public Texture FinalTexture => _finalTexture;

    public readonly uint[] Indices = { 0, 1, 2, 3, 4, 5 };
    public readonly float[] Vertices = {
        // Coords    // texCoords
        1.0f, -1.0f,  1.0f, 0.0f,
        -1.0f, -1.0f,  0.0f, 0.0f,
        -1.0f,  1.0f,  0.0f, 1.0f,

        1.0f,  1.0f,  1.0f, 1.0f,
        1.0f, -1.0f,  1.0f, 0.0f,
        -1.0f,  1.0f,  0.0f, 1.0f
    };

    private bool _ppEnabled;
    private float _ppVignette;
    private float _ppGrain;
    private float _ppChromaticPx;
    private float _ppDirtIntensity;
    private OpenTK.Mathematics.Vector3 _ppDirtTint = OpenTK.Mathematics.Vector3.One;
    private float _ppDirtTiling = 128f;

    public FramebufferObject(OpenTK.Mathematics.Vector2i size)
    {
        _width = size.X;
        _height = size.Y;
        _renderbuffer = new RenderbufferObject(_width, _height);
    }

    public void Setup()
    {
        _framebufferHandle = GL.GenFramebuffer();
        Bind();

        _framebufferTexture = new Texture((uint) _width, (uint) _height);

        var drawTargets = new[] { DrawBuffersEnum.ColorAttachment0 };
        GL.DrawBuffers(drawTargets.Length, drawTargets);
        var err = GL.GetError();
        if (err != ErrorCode.NoError) Console.WriteLine($"[gl] msaa drawbuffer error: {err}");

        _renderbuffer.Setup();

        GL.DrawBuffers(drawTargets.Length, drawTargets);

        _shader = new Shader("framebuffer");
        _shader.Use();
        _shader.SetUniform("screenTexture", 0);
        _shader.SetUniform("uResolution", new System.Numerics.Vector2(_width, _height));

        _ebo = new BufferObject<uint>(Indices, BufferTarget.ElementArrayBuffer);
        _vbo = new BufferObject<float>(Vertices, BufferTarget.ArrayBuffer);
        _vao = new VertexArrayObject<float, uint>(_vbo, _ebo);

        _vao.VertexAttributePointer(0, 2, VertexAttribPointerType.Float, 4, 0); // position
        _vao.VertexAttributePointer(1, 2, VertexAttribPointerType.Float, 4, 2); // uv

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"Framebuffer failed to bind with error: {GL.GetProgramInfoLog(_framebufferHandle)}");
        }

        _postProcessingHandle = GL.GenFramebuffer();
        Bind(_postProcessingHandle);

        _postProcessingTexture = new Texture(_width, _height);

        GL.DrawBuffers(drawTargets.Length, drawTargets);
        err = GL.GetError();
        if (err != ErrorCode.NoError) Console.WriteLine($"[gl] post drawbuffer error: {err}");

        status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"Post-Processing framebuffer failed to bind with error: {GL.GetProgramInfoLog(_postProcessingHandle)}");
        }

        // Final target FBO: where the post-processed image is drawn
        _finalHandle = GL.GenFramebuffer();
        Bind(_finalHandle);
        _finalTexture = new Texture(_width, _height);
        GL.DrawBuffers(drawTargets.Length, drawTargets);
        err = GL.GetError();
        if (err != ErrorCode.NoError) Console.WriteLine($"[gl] final drawbuffer error: {err}");
        status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception($"Final framebuffer failed to bind with error: {GL.GetProgramInfoLog(_finalHandle)}");
        }
    }

    public void Bind() => Bind(_framebufferHandle);
    public void Bind(int handle)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, handle);
    }

    public void BindMsaa()
    {
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebufferHandle);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _postProcessingHandle);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        if (_ppEnabled)
        {
            // Draw post-processing into our final FBO
            Bind(_finalHandle);
            GL.Viewport(0, 0, _width, _height);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.Disable(EnableCap.DepthTest);

            _shader.Use();
            _shader.SetUniform("uResolution", new System.Numerics.Vector2(_width, _height));
            _shader.SetUniform("uEnableVignette", _ppVignette > 0 ? 1 : 0);
            _shader.SetUniform("uVignetteIntensity", _ppVignette);
            _shader.SetUniform("uEnableGrain", _ppGrain > 0 ? 1 : 0);
            _shader.SetUniform("uGrainIntensity", _ppGrain);
            _shader.SetUniform("uEnableChromatic", _ppChromaticPx > 0 ? 1 : 0);
            _shader.SetUniform("uChromaticAmount", _ppChromaticPx);
            _shader.SetUniform("uEnableDirt", _ppDirtIntensity > 0 ? 1 : 0);
            _shader.SetUniform("uDirtIntensity", _ppDirtIntensity);
            _shader.SetUniform("uDirtTint", new System.Numerics.Vector3(_ppDirtTint.X, _ppDirtTint.Y, _ppDirtTint.Z));
            _shader.SetUniform("uDirtTiling", _ppDirtTiling);
            _vao.Bind();

            _postProcessingTexture.Bind(TextureUnit.Texture0);

            GL.DrawArrays(PrimitiveType.Triangles, 0, Indices.Length);
            GL.Enable(EnableCap.DepthTest);

            // Copy final PP result into the resolved FBO so downstream reads can access it reliably
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _finalHandle);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _postProcessingHandle);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

            // Present to default backbuffer (optional)
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _finalHandle);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            GL.DrawBuffer(DrawBufferMode.Back);
            GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        }
    }

    public IntPtr GetPointer() => _postProcessingTexture.GetPointer();

    public void ConfigurePostProcessing(bool enabled,
        float vignetteIntensity,
        float grainIntensity,
        float chromaticAmountPx,
        float dirtIntensity,
        OpenTK.Mathematics.Vector3 dirtTint,
        float dirtTiling)
    {
        _ppEnabled = enabled;
        _ppVignette = vignetteIntensity;
        _ppGrain = grainIntensity;
        _ppChromaticPx = chromaticAmountPx;
        _ppDirtIntensity = dirtIntensity;
        _ppDirtTint = dirtTint;
        _ppDirtTiling = dirtTiling;
    }

    public void WindowResized(int width, int height)
    {
        _width = width;
        _height = height;

        _renderbuffer.WindowResized(width, height);

        _framebufferTexture.WindowResized(width, height);
        _postProcessingTexture.WindowResized(width, height);
    }

    public void Dispose()
    {
        _vao?.Dispose();
        _shader?.Dispose();
        _framebufferTexture?.Dispose();
        _postProcessingTexture?.Dispose();
        _finalTexture?.Dispose();
        _renderbuffer?.Dispose();
        GL.DeleteFramebuffer(_framebufferHandle);
        GL.DeleteFramebuffer(_postProcessingHandle);
        GL.DeleteFramebuffer(_finalHandle);
    }
}

using OpenTK.Mathematics;

namespace FModelHeadless.Rendering;

internal sealed class PostProcessSettings
{
    public bool Enabled { get; set; }

    // Vignette
    public bool VignetteEnabled { get; set; }
    public float VignetteIntensity { get; set; } = 0.15f;

    // Grain
    public bool GrainEnabled { get; set; }
    public float GrainIntensity { get; set; } = 1.0f; // 0..1

    // Chromatic aberration
    public bool ChromaticEnabled { get; set; }
    public float ChromaticAmountPx { get; set; } = 5.0f; // pixels

    // Dirt mask proxy
    public bool DirtEnabled { get; set; }
    public float DirtIntensity { get; set; } = 2.0f; // 0..5
    public Vector3 DirtTint { get; set; } = new(1.0f, 0.86f, 0.64f); // warm tint
    public float DirtTiling { get; set; } = 128.0f;

    public static PostProcessSettings FoxholePreset()
    {
        return new PostProcessSettings
        {
            Enabled = true,
            VignetteEnabled = true,
            VignetteIntensity = 0.15f,
            GrainEnabled = true,
            GrainIntensity = 1.0f,
            ChromaticEnabled = true,
            ChromaticAmountPx = 5.0f,
            DirtEnabled = true,
            DirtIntensity = 2.0f,
            DirtTint = new Vector3(1.0f, 0.861585f, 0.635995f),
            DirtTiling = 128.0f
        };
    }
}


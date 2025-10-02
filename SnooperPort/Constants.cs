using System.Numerics;

namespace FModel.Views.Snooper;

public static class Constants
{
    public const float SCALE_DOWN_RATIO = 0.01f;
    public const int SAMPLES_COUNT = 4;

    public static int PALETTE_LENGTH => COLOR_PALETTE.Length;

    public static readonly Vector3[] COLOR_PALETTE =
    {
        new(0.231f, 0.231f, 0.231f),
        new(0.376f, 0.490f, 0.545f),
        new(0.957f, 0.263f, 0.212f),
        new(0.196f, 0.804f, 0.196f),
        new(0.957f, 0.647f, 0.212f),
        new(0.612f, 0.153f, 0.690f),
        new(0.129f, 0.588f, 0.953f),
        new(1.000f, 0.920f, 0.424f),
        new(0.824f, 0.412f, 0.118f),
        new(0.612f, 0.800f, 0.922f)
    };
}

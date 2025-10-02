using System;
using System.Numerics;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using FModel.Views.Snooper.Shading;

namespace FModel.Views.Snooper.Lights;

public class PointLight : Light
{
    public float Linear;
    public float Quadratic;

    public PointLight(Texture icon, UObject point) : base(icon, point)
    {
        if (!point.TryGetValue(out float radius, "SourceRadius", "AttenuationRadius"))
            radius = 1.0f;

        radius *= Constants.SCALE_DOWN_RATIO;
        Linear = 4.5f / radius;
        Quadratic = 75.0f / MathF.Pow(radius, 2.0f);
    }

    public PointLight(Texture icon, Transform transform, Vector4 color, float intensity, float radius) : base(icon, transform, color, intensity)
    {
        radius = MathF.Max(radius, 0.001f);
        radius *= Constants.SCALE_DOWN_RATIO;
        Linear = 4.5f / radius;
        Quadratic = 75.0f / MathF.Pow(radius, 2.0f);
    }

    public PointLight(FGuid model, Texture icon, UObject parent, UObject point, Transform transform) : base(model, icon, parent, point, transform)
    {
        if (!point.TryGetValue(out float radius, "AttenuationRadius", "SourceRadius"))
            radius = 1.0f;

        radius *= Constants.SCALE_DOWN_RATIO;
        Linear = 4.5f / radius;
        Quadratic = 75.0f / MathF.Pow(radius, 2.0f);
    }

    public override void Render(int i, Shader shader)
    {
        base.Render(i, shader);
        shader.SetUniform($"uLights[{i}].Linear", Linear);
        shader.SetUniform($"uLights[{i}].Quadratic", Quadratic);

        shader.SetUniform($"uLights[{i}].Type", 0);
    }

}

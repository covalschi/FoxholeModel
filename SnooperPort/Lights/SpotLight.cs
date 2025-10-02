using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using FModel.Views.Snooper.Shading;

namespace FModel.Views.Snooper.Lights;

public class SpotLight : Light
{
    public float Attenuation;
    public float InnerConeAngle;
    public float OuterConeAngle;

    public SpotLight(Texture icon, UObject spot) : base(icon, spot)
    {
        if (!spot.TryGetValue(out Attenuation, "SourceRadius", "AttenuationRadius"))
            Attenuation = 1.0f;

        Attenuation *= Constants.SCALE_DOWN_RATIO;
        InnerConeAngle = spot.GetOrDefault("InnerConeAngle", 50.0f);
        OuterConeAngle = spot.GetOrDefault("OuterConeAngle", InnerConeAngle + 10);
        if (OuterConeAngle < InnerConeAngle)
            InnerConeAngle = OuterConeAngle - 10;
    }

    public SpotLight(FGuid model, Texture icon, UObject parent, UObject spot, Transform transform) : base(model, icon, parent, spot, transform)
    {
        if (!spot.TryGetValue(out Attenuation, "AttenuationRadius", "SourceRadius"))
            Attenuation = 1.0f;

        Attenuation *= Constants.SCALE_DOWN_RATIO;
        InnerConeAngle = spot.GetOrDefault("InnerConeAngle", 50.0f);
        OuterConeAngle = spot.GetOrDefault("OuterConeAngle", InnerConeAngle + 10);
        if (OuterConeAngle < InnerConeAngle)
            InnerConeAngle = OuterConeAngle - 10;
    }

    public override void Render(int i, Shader shader)
    {
        base.Render(i, shader);
        shader.SetUniform($"uLights[{i}].Attenuation", Attenuation);
        shader.SetUniform($"uLights[{i}].InnerConeAngle", InnerConeAngle);
        shader.SetUniform($"uLights[{i}].OuterConeAngle", OuterConeAngle);

        shader.SetUniform($"uLights[{i}].Type", 1);
    }
}

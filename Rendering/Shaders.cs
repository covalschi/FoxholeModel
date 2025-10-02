namespace FModelHeadless.Rendering;

internal static class Shaders
{
    public const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out VS_OUT {
    vec3 Normal;
    vec3 WorldPos;
    vec2 TexCoord;
} vs_out;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vs_out.WorldPos = worldPos.xyz;
    mat3 normalMatrix = mat3(transpose(inverse(uModel)));
    vs_out.Normal = normalize(normalMatrix * aNormal);
    vs_out.TexCoord = aTexCoord;
    gl_Position = uProjection * uView * worldPos;
}";

    public const string FragmentSource = @"#version 410 core
in VS_OUT {
    vec3 Normal;
    vec3 WorldPos;
    vec2 TexCoord;
} fs_in;

out vec4 FragColor;

uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform vec3 uViewPos;

vec3 shade()
{
    vec3 baseColor = vec3(0.6, 0.65, 0.7);
    vec3 ambient = 0.25 * baseColor;
    float diff = max(dot(normalize(fs_in.Normal), -normalize(uLightDir)), 0.0);
    vec3 diffuse = diff * baseColor * uLightColor;
    vec3 viewDir = normalize(uViewPos - fs_in.WorldPos);
    vec3 halfDir = normalize(-uLightDir + viewDir);
    float spec = pow(max(dot(normalize(fs_in.Normal), halfDir), 0.0), 32.0);
    vec3 specular = spec * vec3(0.2);
    return ambient + diffuse + specular;
}

void main()
{
    FragColor = vec4(shade(), 1.0);
}";
}

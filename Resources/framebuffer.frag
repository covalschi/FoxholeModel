#version 450 core

in vec2 fTexCoords;

uniform sampler2D screenTexture;

// Post-process controls
uniform vec2 uResolution;              // (width, height)

uniform int uEnableVignette;
uniform float uVignetteIntensity;      // 0..1 (~0.15 default)

uniform int uEnableGrain;
uniform float uGrainIntensity;         // 0..1 (1.0 default in Foxhole)

uniform int uEnableChromatic;
uniform float uChromaticAmount;        // in pixels (Foxhole fringe ~5)

uniform int uEnableDirt;
uniform float uDirtIntensity;          // 0..5 (e.g., 2.0 or 5.13)
uniform vec3 uDirtTint;                // tint for the dirt overlay
uniform float uDirtTiling;             // tiling factor for procedural dirt

out vec4 FragColor;

// Simple hash-based noise (stable per-pixel)
float hash(vec2 p)
{
    p = fract(p * vec2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return fract(p.x * p.y);
}

vec3 applyChromatic(vec2 uv, float amountPx)
{
    if (uEnableChromatic == 0 || amountPx <= 0.0) {
        return texture(screenTexture, uv).rgb;
    }

    vec2 center = vec2(0.5, 0.5);
    vec2 dir = normalize(uv - center + 1e-6);
    float amt = amountPx / max(uResolution.x, 1.0); // convert px to uv

    vec3 col;
    col.r = texture(screenTexture, uv + dir * amt).r;
    col.g = texture(screenTexture, uv).g;
    col.b = texture(screenTexture, uv - dir * amt).b;
    return col;
}

void main()
{
    vec2 uv = fTexCoords;

    // Base color with optional chromatic aberration
    vec3 color = applyChromatic(uv, uChromaticAmount);

    // Vignette (radial darkening)
    if (uEnableVignette == 1 && uVignetteIntensity > 0.0) {
        vec2 p = uv - vec2(0.5);
        float r2 = dot(p, p);
        // Sharper falloff near edges; tweak exponent to taste
        float vig = 1.0 - clamp(r2 * 2.2, 0.0, 1.0);
        vig = pow(vig, 1.5);
        color *= mix(1.0, vig, clamp(uVignetteIntensity, 0.0, 1.0));
    }

    // Dirt mask (proxy using procedural noise)
    if (uEnableDirt == 1 && uDirtIntensity > 0.0) {
        // Procedural tiling noise as a stand-in for Dirtmask texture
        float t = uDirtTiling <= 0.0 ? 1.0 : uDirtTiling;
        float n = hash(uv * t * uResolution.xy);
        // Emulate bloom dirt by boosting darker specks over highlights
        float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
        float mask = smoothstep(0.6, 1.0, luma) * n; // affect brighter regions more
        color = mix(color, color * (1.0 + uDirtIntensity * mask * uDirtTint), 0.5);
    }

    // Film grain
    if (uEnableGrain == 1 && uGrainIntensity > 0.0) {
        float g = hash(uv * uResolution.xy);
        // center the noise around 0
        g = (g - 0.5) * 2.0;
        color += g * (uGrainIntensity * 0.03); // small amplitude
    }

    FragColor = vec4(color, 1.0);
}

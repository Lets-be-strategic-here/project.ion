// [project]ion — shared sky / aerial-perspective helpers (Ion/FlatToon, Ion/Backdrop, Ion/GradientSky).
// All globals are set by Ion.Presentation.Atmosphere (linear colours). When they are unset (all zero:
// edit-mode tests, a scene without the bootstrap) fog is off and the sky helpers return black.
#ifndef ION_ATMOSPHERE_INCLUDED
#define ION_ATMOSPHERE_INCLUDED

float4 _IonSkyTop;        // rgb, w = top falloff exponent
float4 _IonSkyHorizon;    // rgb, w = bottom falloff exponent
float4 _IonSkyBottom;     // rgb
float4 _IonSunDirection;  // xyz = direction TO the sun (world), w = halo strength
float4 _IonSunWarm;       // rgb = golden-hour horizon colour towards the sun, w = strength
float4 _IonSunHalo;       // rgb = sun colour used for the soft halo / in-scattering
float4 _IonFogParams;     // x = start (m), y = density (1/m), z = enabled (0/1), w = max fog (< 1 keeps silhouettes)
float4 _IonFogHeight;     // x = extra density factor below the eye, y = metres below the eye where it starts, z = 1 / ramp (m)
float4 _IonUltraFxAtmo;   // Ultra (UltraFx): y = fog sun in-scattering strength (0 elsewhere)
float4 _IonGrade;         // x = contrast, y = lift (< 0 deepens the darks), z = pivot (display luma), w = enabled (0/1)

// Sky colour seen along a world direction: vertical three-colour gradient, warmer towards the sun
// near the horizon (golden hour), plus the soft sun halo (not the disc).
float3 IonSkyColor(float3 dir)
{
    float y = dir.y;
    float up = pow(saturate(y), max(_IonSkyTop.w, 0.05));
    float down = pow(saturate(-y), max(_IonSkyHorizon.w, 0.05));
    float3 col = lerp(_IonSkyHorizon.rgb, _IonSkyTop.rgb, up);
    col = lerp(col, _IonSkyBottom.rgb, down);

    float3 sun = _IonSunDirection.xyz;
    float2 dxz = dir.xz;
    float2 sxz = sun.xz;
    float lenD = length(dxz), lenS = length(sxz);
    if (lenD > 1e-4 && lenS > 1e-4)
    {
        float az = dot(dxz / lenD, sxz / lenS) * 0.5 + 0.5;        // 1 towards the sun, 0 away
        float band = 1.0 - saturate(abs(y) * 2.2);                  // near the horizon only
        float warm = az * az * az * band * band;
        col = lerp(col, _IonSunWarm.rgb, saturate(warm * _IonSunWarm.w));
    }
    float d = saturate(dot(dir, sun));
    float d2 = d * d, d4 = d2 * d2;
    // Blended (not added) so the halo stays golden instead of clipping to white on the bright horizon.
    col = lerp(col, _IonSunHalo.rgb, saturate(_IonSunDirection.w * d4 * d4 * saturate(y * 4.0 + 0.5)));
    return col;
}

// Fog amount for a point: exponential with distance past the start, a little denser for points far
// below the eye (the misty void under the islands), capped below 1 so distant shapes stay readable
// as soft silhouettes. Camera-relative, so a diorama capture far below the world fogs identically.
float IonFogAmount(float3 positionWS, float3 cameraWS, float dist)
{
    float below = saturate((cameraWS.y - positionWS.y - _IonFogHeight.y) * _IonFogHeight.z);
    float k = _IonFogParams.y * (1.0 + _IonFogHeight.x * below);
    float d = max(0.0, dist - _IonFogParams.x);
    return _IonFogParams.w * (1.0 - exp(-d * k));
}

// Applies aerial perspective towards the sky colour behind the point.
float3 IonApplyFog(float3 color, float3 positionWS)
{
    if (_IonFogParams.z < 0.5) return color;
    float3 cam = GetCameraPositionWS();
    float3 v = positionWS - cam;
    float dist = length(v);
    float3 dir = v / max(dist, 1e-4);
    float f = IonFogAmount(positionWS, cam, dist);
    float3 fogCol = IonSkyColor(dir);
    // Ultra: forward in-scattering towards the sun (soft warm glow / light-shaft feel in the haze).
    if (_IonUltraFxAtmo.y > 0.0)
    {
        float s = saturate(dot(dir, _IonSunDirection.xyz));
        float s2 = s * s, s8 = s2 * s2; s8 *= s8;
        fogCol = lerp(fogCol, _IonSunHalo.rgb, saturate(_IonUltraFxAtmo.y * (0.35 * s2 + 0.65 * s8)));
        f = saturate(f * (1.0 + 0.25 * _IonUltraFxAtmo.y * s8));
    }
    return lerp(color, fogCol, f);
}

// Final colour grade shared by every world shader (Ion/FlatToon, Ion/Backdrop, Ion/GradientSky), so
// photos (rendered by the same shaders into their textures) and the live world always match.
// A luma-only contrast curve in display space around a high pivot (the frame is mostly bright pastel):
// the darks deepen, tapering towards black so nothing crushes, the highlights open up a little, and
// the hue / saturation stays pastel (mostly an additive luma shift). Plus +-0.5/255 of screen-space
// noise so the soft sky and fog gradients never band. Identity when the globals are unset (tests).
float IonGradeHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float3 IonGrade(float3 linearColor, float2 pixel)
{
    if (_IonGrade.w < 0.5) return linearColor;
    float3 g = pow(max(linearColor, 0.0), 1.0 / 2.2);         // ~display space
    float y = dot(g, float3(0.2126, 0.7152, 0.0722));
    float t = saturate(y / max(_IonGrade.z, 1e-3));
    float w = t * t * (3.0 - 2.0 * t);                          // taper near black
    float y2 = y + (y - _IonGrade.z) * (_IonGrade.x - 1.0) * w;
    y2 = y2 + _IonGrade.y * (1.0 - y2);
    float3 add = g + (y2 - y);
    float3 mul = g * (y2 / max(y, 1e-3));
    g = lerp(add, mul, 0.3);
    g += (IonGradeHash(pixel) - 0.5) * (1.0 / 255.0);
    return pow(saturate(g), 2.2);
}

#endif

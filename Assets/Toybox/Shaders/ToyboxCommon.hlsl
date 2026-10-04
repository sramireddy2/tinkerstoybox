#ifndef TOYBOX_COMMON_INCLUDED
#define TOYBOX_COMMON_INCLUDED

// Shared by the game's shaders (ART_BIBLE 3.1). Include it after URP's Core.hlsl.
//
// Owner: toy shading. Toybox/RoomLit and Toybox/Flat include it read-only: for the ambient and kicker
// globals and Tri() below. Nothing in here is a material property; everything is a global uniform
// (ART_BIBLE 3.7) that a presenter sets with Shader.SetGlobalVector / SetGlobalFloat - linear values,
// never through a Color property.

// ---- Globals: light that has no position (set at preset load) -------------------------------------------
float4 _AmbSky;         // rgb: linear colour x intensity
float4 _AmbEquator;
float4 _AmbGround;
float4 _KickDir;        // xyz: unit vector toward the kicker
float4 _KickColor;      // rgb: linear colour x intensity

// ---- Globals: the toy look (set at preset load) ----------------------------------------------------------
float4 _WinDir;         // xyz: unit vector toward the sun; the centre of the window glint
float4 _WinRight;       // xyz: with _WinUp, the rest of the glint's basis
float4 _WinUp;
float4 _GlintColor;     // rgb
float4 _EnvCeil;        // StudioEnv bands, rgb
float4 _EnvWall;
float4 _EnvFloor;
float _ToyGlowGain;     // 1 by day, 6 at night
// Not in the art bible's table: the "High-visibility toys" setting (2.6). The rim is multiplied by
// 1 + _ToyRimBoost, so a global nobody has set (0) leaves the rim alone.
float _ToyRimBoost;

// ---- Globals: the held toy (set by the sticker pass, ART_BIBLE 8) ----------------------------------------
float _ToyHeld;         // 1 inside the sticker pass, 0 everywhere else
// The homothety of 8.3: inside the sticker pass every view-space position is multiplied by this, which
// brings the held toy to a fixed distance from the eye. The picture is unchanged (a uniform scaling about
// the eye is invisible to a perspective projection); depth precision and near-plane clipping become the
// same at every hold distance.
float _ToyHeldScale;
float4 _StickerPx;      // x: border width, y: peel shadow offset x, z: offset y (up) - in render-target pixels
float4 _StickerColor;   // rgb: Paper x 1.1
float4 _PeelColor;      // rgb: lerp(white, Ink, 0.22); the frame is multiplied by it

// Ink, #2B2140, linear: the darkest value allowed anywhere (ART_BIBLE 2.5 rule 7).
#define TOYBOX_INK half3(0.0242, 0.0152, 0.0513)
#define TOYBOX_INK_LUMINANCE 0.0197

// Nothing renders darker than Ink. A toe rather than a clamp, so what is down there keeps its shape:
// black comes out as Ink, anything above four times Ink's luminance is left alone.
half3 InkToe(half3 c)
{
    half toe = saturate(1.0 - dot(c, half3(0.2126, 0.7152, 0.0722)) / (4.0 * TOYBOX_INK_LUMINANCE));
    return c + TOYBOX_INK * (toe * toe);
}

// Detail textures store 128 as "no change" (ART_BIBLE 4.2).
#define TOYBOX_DETAIL_NEUTRAL (128.0 / 255.0)

// ---- Ambient: three colours, by the normal alone ---------------------------------------------------------
half3 Tri(float3 N)
{
    return N.y >= 0.0 ? lerp(_AmbEquator.rgb, _AmbSky.rgb, N.y) : lerp(_AmbEquator.rgb, _AmbGround.rgb, -N.y);
}

// ---- Reflections: the ceiling, wall and floor bands of an infinite softbox --------------------------------
half3 StudioEnv(float3 R, half rough, half shift)
{
    half w = lerp(0.03, 0.7, rough);
    half y = R.y + shift;
    half3 c = lerp(_EnvFloor.rgb, _EnvWall.rgb, smoothstep(-w, w, y));
    return lerp(c, _EnvCeil.rgb, smoothstep(0.45 - w, 0.45 + w, y));
}

// ---- The four-pane window glint: 2 x 2 rounded panes, about 31 degrees across ------------------------------
half WindowPane(float3 R, half soft, half stretch)
{
    float z = dot(R, _WinDir.xyz);
    float2 p = float2(dot(R, _WinRight.xyz) / max(stretch, 1e-3), dot(R, _WinUp.xyz)) / max(z, 1e-3);
    float2 q = abs(abs(p) - 0.17) - 0.105;
    float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.025;
    // Toward the rim of the hemisphere p runs away and its derivative with it; an unbounded filter width
    // there would smear a faint glint along the whole horizon of the reflection.
    float w = min(fwidth(sd), 0.25) + soft;
    return step(0.02, z) * smoothstep(w, -w, sd);
}

// ---- The held toy's clip position -------------------------------------------------------------------------
// Every pass that draws a toy to the camera goes through this, so depth agrees between them.
float4 ToyWorldToHClip(float3 positionWS)
{
    float3 positionVS = TransformWorldToView(positionWS);
    positionVS *= lerp(1.0, _ToyHeldScale, _ToyHeld);
    return TransformWViewToHClip(positionVS);
}

// ---- Impact squash (ART_BIBLE 3.2, 9.5), in world space ---------------------------------------------------
// squash = (centre.x, pivot.y, centre.z, amount). Never while held: the footprint is sacred.
float3 ToySquash(float3 positionWS, float4 squash)
{
    float q = squash.w * (1.0 - _ToyHeld);
    positionWS.y = squash.y + (positionWS.y - squash.y) * (1.0 - q);
    positionWS.xz = squash.xz + (positionWS.xz - squash.xz) * (1.0 + 0.5 * q);
    return positionWS;
}

// The uniform scale of the object being drawn (toys are only ever scaled uniformly).
float ToyObjectScale()
{
    float4x4 m = GetObjectToWorldMatrix();
    return length(float3(m[0].x, m[1].x, m[2].x));
}

#endif

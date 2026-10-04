#ifndef TOYBOX_ROOMLIT_LIB_INCLUDED
#define TOYBOX_ROOMLIT_LIB_INCLUDED

// Toybox/RoomLit (ART_BIBLE 3.3): everything the room shader's passes share. Include after URP's
// Core.hlsl (and Lighting.hlsl in the lit pass).
//
// All colours are linear. Every option is a uniform; there is not one keyword of our own.

// One per-material block, identical in every pass (SRP Batcher).
CBUFFER_START(UnityPerMaterial)
    float4 _ColorTop;
    float4 _ColorSide;
    float4 _ColorDado;
    float4 _PatternA;       // pitch, param1, param2, gain
    float _DadoY;
    float _Pattern;
    float _Corner;
    float _Cull;
CBUFFER_END

// ---- Look globals ------------------------------------------------------------------------------------
// The ones ToyLit reads too (_AmbSky, _AmbEquator, _AmbGround, _KickDir, _KickColor) and Tri(N) come
// from ToyboxCommon.hlsl, which toy shading owns.
#include "Assets/Toybox/Shaders/ToyboxCommon.hlsl"

// The room's own (ART_BIBLE 3.7). LightingRig sets the first group on level load, PoolSystem the second
// every frame.
#ifndef TOYBOX_ROOM_GLOBALS_DECLARED
#define TOYBOX_ROOM_GLOBALS_DECLARED
float4 _WinO;           // centre of the window opening
float4 _WinU;           // horizontal tangent / half width
float4 _WinV;           // up / half height
float4 _WinN;           // the window wall's inward normal
float _WinMullion;      // half a mullion as a fraction of the half opening
float4 _PatchColor;     // colour * gain; zero switches the patch off
float4 _ShellMin;
float4 _ShellMax;
float4 _HazeColor;
float4 _HazeParams;     // density, height start, height end, height max

#define TOYBOX_MAX_POOLS 16
float4 _PoolPos[TOYBOX_MAX_POOLS];      // xyz centre, w radius
float4 _PoolTint[TOYBOX_MAX_POOLS];     // rgb colour, a strength
float _PoolCount;
float _PoolGain;
float4 _Splash;         // xyz centre, w prop radius
float4 _SplashTint;     // rgb colour * strength, a current ring radius
#endif

// ---- Patterns ----------------------------------------------------------------------------------------
// Analytic, in world units, box-filtered over the pixel footprint w and faded out before their pitch
// reaches the pixel grid, so nothing shimmers at a distance. Each returns about -1..1 (lines go further:
// a thin line needs more than a tone step to be seen); the caller multiplies by the gain.

#define ROOM_LINE 3.0

// How much of a colour pool or splash ring shows in the toy's own hue whatever the surface's colour
// (the rest is multiplied by the surface's albedo, as bounced light would be).
#define ROOM_POOL_OWN 0.7
// The splash ring is a thin ellipse on the floor from a figure's eye height: it is this much stronger
// than 9.4's 0.6 to be seen at all.
#define ROOM_RING_BOOST 1.5
// All the halos that reach a point together add no more than this (times the gain): a wall right behind
// a row of toys must stay a dip tone with a blush on it, not turn candy.
#define ROOM_POOL_CAP 0.45
// A pool is something a toy stands in. What lies level with the toy's middle or above it (a wall beside
// it, a ceiling) takes this share of the halo; the full halo is for what lies well below.
#define ROOM_POOL_LEVEL 0.3

// Ink, #2B2140, linear: the darkest value allowed anywhere.
#define ROOM_INK half3(0.0242, 0.0152, 0.0513)
#define ROOM_INK_LUMINANCE 0.0197

float RoomHash(float2 c)
{
    return frac(sin(dot(c, float2(127.1, 311.7))) * 43758.5453);
}

// 1 while the pattern's pitch is well above a pixel, 0 once it is not.
float RoomFade(float w, float pitch)
{
    return 1.0 - smoothstep(0.25, 0.6, w / max(pitch, 1e-4));
}

// Coverage of a line of width g centred on d = 0 by a pixel of width w centred on d.
float RoomLine(float d, float g, float w)
{
    w = max(w, 1e-5);
    return saturate((min(d + 0.5 * w, 0.5 * g) - max(d - 0.5 * w, -0.5 * g)) / w);
}

// A square wave of period 2 * half along x: -1 / +1, filtered over w.
float RoomSquare(float x, float halfPeriod, float w)
{
    float s = abs(frac(x / (2.0 * halfPeriod)) - 0.5) * 2.0 * halfPeriod - 0.5 * halfPeriod;
    return clamp(2.0 * s / max(w, 1e-5), -1.0, 1.0);
}

// Discs of radius r on a square grid: 1 inside.
float RoomDiscs(float2 uv, float pitch, float r, float w)
{
    float2 c = (frac(uv / pitch) - 0.5) * pitch;
    float d = length(c) - r;
    float k = min(1.0, 2.0 * r / max(w, 1e-5));
    return saturate(0.5 - d / max(w, 1e-5)) * k * k * RoomFade(w, pitch);
}

float RoomPlanks(float2 uv, float width, float boardLength, float gap, float w)
{
    float row = floor(uv.x / width);
    float along = uv.y + RoomHash(float2(row, 7.0)) * boardLength;
    float board = floor(along / boardLength);
    float tone = (RoomHash(float2(row, board)) * 2.0 - 1.0) * 0.7;
    float dRow = (abs(frac(uv.x / width) - 0.5) - 0.5) * width;                // 0 on the seam between two rows
    float dEnd = (abs(frac(along / boardLength) - 0.5) - 0.5) * boardLength;   // 0 where two boards butt
    float seam = max(RoomLine(dRow, gap, w), RoomLine(dEnd, gap, w));
    return lerp(tone, -ROOM_LINE, seam) * RoomFade(w, width);
}

// x: the checker (-1 / +1), y: the grout line (0..1).
float2 RoomTiles(float2 uv, float pitch, float grout, float w)
{
    float fade = RoomFade(w, pitch);
    float checker = RoomSquare(uv.x, pitch, w) * RoomSquare(uv.y, pitch, w);
    float2 d = (abs(frac(uv / pitch) - 0.5) - 0.5) * pitch;
    float groutLine = max(RoomLine(d.x, grout, w), RoomLine(d.y, grout, w));
    return float2(checker, groutLine) * fade;
}

float RoomStripes(float2 uv, float pitch, float w)
{
    return RoomSquare(uv.x, 0.5 * pitch, w) * RoomFade(w, pitch);
}

float RoomQuilt(float2 uv, float pitch, float seam, float w)
{
    float2 q = float2(uv.x + uv.y, uv.x - uv.y) * 0.70710678;
    float2 f = frac(q / pitch);
    // The pillow of each diamond: lighter in the middle, deeper along the seams.
    float pillow = -0.5 * (cos(6.2831853 * f.x) + cos(6.2831853 * f.y));
    float2 d = (abs(f - 0.5) - 0.5) * pitch;
    // Stitches: six dashes per side of a diamond.
    float dash = pitch / 6.0;
    float2 on = step(frac(q.yx / dash), float2(0.6, 0.6)) * RoomFade(w, dash);
    float stitch = max(RoomLine(d.x, seam, w) * on.x, RoomLine(d.y, seam, w) * on.y);
    return lerp(0.6 * pillow, -ROOM_LINE, stitch) * RoomFade(w, pitch);
}

float RoomCorrugated(float2 uv, float3 N, float flute, float rib, float w)
{
    // The cut edge of the board shows its flutes; the faces only the faint ribs they press through.
    float side = 1.0 - smoothstep(0.3, 0.6, abs(N.y));
    float fine = sin(6.2831853 * uv.x / flute) * RoomFade(w, flute);
    float broad = 0.5 * sin(6.2831853 * uv.x / rib) * RoomFade(w, rib);
    return lerp(broad, fine, side);
}

// x: what the gain multiplies; y: for tiles, how much of the checker's other tone this pixel takes.
// w is the pixel's footprint in pattern space, max(fwidth(uv)) - taken by the caller outside any branch.
float2 RoomPattern(float2 uv, float3 N, float w)
{
    if (_Pattern < 0.5) return float2(0.0, 0.0);
    float pitch = _PatternA.x, p1 = _PatternA.y, p2 = _PatternA.z;
    // Printed dots are an ink with nothing to set them off but their own tone: twice the step of a stripe.
    if (_Pattern < 1.5) return float2(2.0 * RoomDiscs(uv, pitch, p1, w), 0.0);
    if (_Pattern < 2.5) return float2(RoomPlanks(uv, pitch, p1, p2, w), 0.0);
    if (_Pattern < 3.5)
    {
        float2 t = RoomTiles(uv, pitch, p1, w);
        return float2(lerp(t.x, -ROOM_LINE, t.y), saturate(0.5 - 0.5 * t.x) * (1.0 - t.y));
    }
    if (_Pattern < 4.5) return float2(RoomStripes(uv, pitch, w), 0.0);
    if (_Pattern < 5.5) return float2(RoomQuilt(uv, pitch, p1, w), 0.0);
    if (_Pattern < 6.5) return float2(RoomDiscs(uv, pitch, p1, w), 0.0);
    return float2(RoomCorrugated(uv, N, pitch, p1, w), 0.0);
}

// ---- Window patch --------------------------------------------------------------------------------------

// The four panes as light on whatever the sun reaches: 1 inside a pane, with a penumbra that widens
// with the throw. L points toward the sun.
half RoomPatch(float3 P, float3 L)
{
    float facing = dot(L, _WinN.xyz);
    // The sun stands behind the window wall, so this is negative; a degenerate setup has no patch.
    if (facing > -1e-4) return 0.0;
    float t = dot(_WinO.xyz - P, _WinN.xyz) / facing;      // distance along the sun ray to the window plane
    float3 Q = P + L * t - _WinO.xyz;
    float2 p = float2(dot(Q, _WinU.xyz), dot(Q, _WinV.xyz));   // |p| <= 1 inside the opening
    float soft = 0.012 + 0.0006 * t;
    float2 q = abs(abs(p) - 0.5) - (0.5 - _WinMullion);
    return step(0.0, t) * smoothstep(soft, -soft, max(q.x, q.y));
}

// ---- Colour pools --------------------------------------------------------------------------------------

// Analytic sphere occluders with a colour bounce: the darkest contact core any pool leaves here, and the
// sum of the colour halos.
void RoomPools(float3 P, float3 N, out half core, out half3 glow)
{
    core = 0.0;
    glow = half3(0.0, 0.0, 0.0);
    for (int k = 0; k < TOYBOX_MAX_POOLS; k++)
    {
        if (k >= _PoolCount) break;
        float3 dv = _PoolPos[k].xyz - P;
        float r2 = max(_PoolPos[k].w * _PoolPos[k].w, 1e-6);
        half a = _PoolTint[k].a;
        float l2 = max(dot(dv, dv), 1e-8);
        // A pool reaches 6 r and no further: both terms fade out between 4 r and 6 r. (No early-out
        // here: a branch that differs between neighbouring pixels left a dotted seam along that sphere.)
        half reach = saturate((36.0 - l2 / r2) / 20.0);
        half nl = max(dot(N, dv) * rsqrt(l2), 0.0) * reach;
        half c = nl * r2 / max(l2, r2);                        // dark contact core, radius about r
        // The halo, visible to about 4 r. It takes the square root of the facing term: the floor around a
        // toy sees the toy's centre at a low angle and would get next to nothing, while a wall beside it
        // faces it squarely and would get everything.
        half h = sqrt(nl) * 6.25 * r2 / (l2 + 6.25 * r2);
        h *= lerp(ROOM_POOL_LEVEL, 1.0, saturate(2.0 * dv.y * rsqrt(l2)));
        core = max(core, a * c);
        glow += _PoolTint[k].rgb * (a * h * (1.0 - c));
    }
    glow /= max(1.0, max(glow.r, max(glow.g, glow.b)) / ROOM_POOL_CAP);
}

// ---- Corner gradient -----------------------------------------------------------------------------------

// Distance to the nearest shell plane other than the pair P's own surface belongs to.
float RoomCornerDistance(float3 P, float3 N)
{
    float3 d = min(P - _ShellMin.xyz, _ShellMax.xyz - P);
    float3 n = abs(N);
    if (n.y >= n.x && n.y >= n.z) return max(min(d.x, d.z), 0.0);
    if (n.x >= n.z) return max(min(d.y, d.z), 0.0);
    return max(min(d.x, d.y), 0.0);
}

// ---- Haze ------------------------------------------------------------------------------------------------

half RoomHaze(float3 P)
{
    float dist = distance(P, _WorldSpaceCameraPos);
    float x = dist * _HazeParams.x;
    half fd = 1.0 - exp(-x * x);
    half fh = smoothstep(_HazeParams.y, _HazeParams.z, P.y) * _HazeParams.w * saturate(dist / 120.0);
    return 1.0 - (1.0 - fd) * (1.0 - fh);
}

#endif

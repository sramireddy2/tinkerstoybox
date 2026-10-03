# Tinker's Toybox — Art Bible: "DIP & DIE-CUT"

Engine: Unity 6 (`6000.6.4f1`), URP 17.6, WebGL 2. Contract: `docs/ARCHITECTURE.md`.
Source pitches: `docs/design-notes/art-pitch-a-vinyl.md`, `art-pitch-b-print.md`, `art-pitch-c-glow.md`.

**Status.** Nothing in this document has been built or measured. Every number is a starting value; where a
number matters, a calibration target or acceptance test is given so it can be tuned against something.
URP API names were written from memory of URP 17 and are marked *(verify)* where a wrong name would cost time.

**The look in three sentences.** Every room is dipped in one matte pastel hue, floor to ceiling, and printed
with patterns at a fixed real-world pitch. The only saturated things in it are the toys you can pick up, lit
like catalogue products with a four-pane window glint. While you hold a toy it is a die-cut sticker on your
lens (flat white border, hard offset shadow); when you let go it becomes matter (a real shadow and a pool of
its own colour flood out at its true size).

**Conventions used below**

- Hex colours are sRGB. `Palette.Lin(hex)` converts to linear once; every colour is pushed to shaders with
  `SetVector` / `SetGlobalVector` / `SetGlobalVectorArray`, never through implicit `SetColor` conversion.
- 1 unit = 3 cm. The player (1.7 units) is a 5 cm action figure.
- Pixel sizes are quoted at 1080p and scale with render-target height (`px × height / 1080`).
- "Toy" means a grabbable prop. "Gadget" means level machinery. "Room" means all static geometry.
- Tiers are **Low / Medium / High**. Medium is the reference.

---

## 1. Why this direction

**Winner: Pitch A, "Dip & Die-Cut" (vinyl).** It is the foundation for everything below.

| Criterion | A — Dip & Die-Cut | B — Loose Register (riso print) | C — Afterglow (gold/violet light) |
|---|---|---|---|
| 1. One-screenshot identity | Strong: a single-hue room with candy toys and a white-bordered sticker in hand | Strongest: halftone plates and misregistration | Weakest: warm key and violet shade is a common look |
| 2. Depth, scale, grabbables | Best: chroma is reserved for grabbables; world-pitch patterns are a ruler; colour pools show true footprint | Good outlines, but dot-screen shadows weaken the shadow cue and fractal dots crawl in first person | Coloured light changes toy hues; night levels and a 16° sun hurt shadow reading |
| 3. 60 fps in URP WebGL 2, procedural only | Best: two lit shaders, no depth texture, no SSAO, stock URP post | Worst: every pixel goes through an MRT ink pipeline that bypasses URP lighting and post | Costly: 300-unit cascades, marched shafts, point lights, glow lights |
| 4. Illusion while held | By construction: toys are only ever lit by position-independent terms, so almost nothing changes at grab | By construction, but only inside the custom pipeline | Needs a "hand rig" light swap and several ramps; more seams |

A wins criteria 2, 3 and 4 outright and is a close second on 1. B's identity is the loudest but it replaces
URP instead of using it, which is the wrong risk for a team building the engine in parallel. C's signature
depends on lighting ratios staying in tune and directly fights "colourful".

**Grafted in (each one fits the "dipped room, sticker toy" story)**

| From | Idea | Where it lands |
|---|---|---|
| B | The "ping": a ring at the prop's real size drawn on real surfaces on release | The **splash ring** in `RoomLit` (§3.3, §9) — colour ripples out of the dip |
| B | Three hard reflection bands on `reflect.y` for metal | The analytic **StudioEnv** in `ToyLit` replaces any cubemap (§3.2) |
| B | Hazards carry diagonal key-ink stripes | Hazard surfaces: Ink stripes over the Hazard signal colour (§2) |
| B | Jump-apex notch and figure pictogram on the ruler | Scale readout (§9) |
| B | Pause stops rendering | Pause card (§10) |
| C | "The sun always lands on the origin": solve the window from the sun | Environment solve (§6.2) |
| C | Penumbra that widens with throw distance | Window patch (§3.3) |
| C | Height haze that dissolves the ceiling | `RoomLit` haze (§3.3) |
| C | Action-figure shadow proxy for the player | Lighting rig (§5) |
| C | The toys become the lights at night | The `night-light` finale preset only: toy self-glow and pool gain go up (§6.4) |
| C | Backlit thin sheets | `_Translucency` in `ToyLit` for paper and feather (§4) |
| C | Appendix A audio mapping: pre-rendered clips, baked reverb, `PlayScheduled` | Audio (§11) |
| B + C | Pitch follows true size, not relative scale | Size-to-pitch law (§11) |

**Rejected**

| From | Idea | Why |
|---|---|---|
| B | Ink-plate MRT pipeline, press pass, misregistration, halftone and line screens, post keylines | Bypasses URP; everything (particles, UI capture, glass) must be re-authored as ink coverage; screen patterns shimmer under first-person motion |
| B | Ghost and boil on grabbables | Constant motion on every toy competes with the puzzle; A's chroma rule does the job silently |
| C | Coloured key light, coral fringe, time-of-day arc across all 15 levels | Toy hue must be the same in every room; the candy palette is the brand |
| C | Phosphor trim as the grabbable signal | A second reserved hue next to seven candy hues muddies the rule "saturated = yours" |
| C | Marched shafts, point lights, glow lights, 300-unit shadow distance, hand-rig light swap | Cost and extra held-object seams |
| C | Inflation ghost on release | The pool flood, splash ring and dimension callout already carry the reveal |
| A | PMREM environment map | Replaced by analytic StudioEnv: no cubemap, no reflection probe, nothing to strip |
| A | Real kicker light | A global direction and colour uniform instead; additional lights are disabled in URP |
| A | Alpha-tested feather | Feather barbs are geometry; the game has no alpha-tested material, so there is no alpha-test keyword |
| A | Far-plane depth trick for the peel shadow | Stencil instead (§8) |
| A | Border collapse animation on release | The border snaps off under the shutter flash; the released prop is no longer in the sticker pass |
| A | DOM/CSS UI | UGUI + TextMeshPro built from code (§10) |

---

## 2. Palette

### 2.1 Neutrals

| Name | Hex | Use |
|---|---|---|
| Paper | `#FFFDF7` | Sticker border, UI surfaces, skirting, window frames, toy secondary parts, rim light |
| Ink | `#2B2140` | UI text, gadget bodies, vignette, peel shadow. **Darkest value allowed anywhere; there is no black** |
| Kraft | `#C99A62` | Cardboard toys, locked level cards |
| Birch | `#E9C9A0` | Raw wood at chipped edges; non-grabbable wooden physics props |
| Steel | `#C9CED8` | Gadget metal parts; non-grabbable metal props |

### 2.2 Candy — grabbable toys only

Never used on room surfaces, gadgets or world-space VFX. UI may use Cherry and Lagoon as accents (§10).

| Name | Hex |
|---|---|
| Cherry | `#FF2E55` |
| Tangerine | `#FF7A1A` |
| Lemon | `#FFCE1F` |
| Lime | `#7FDB2E` |
| Lagoon | `#18A8FF` |
| Grape | `#8A4BFF` |
| Bubblegum | `#FF5FB0` |

### 2.3 Signal — gadgets only, always emissive on an Ink body

| Name | Hex | HDR gain | Use and motion |
|---|---|---|---|
| Amber | `#FFB627` | 1.2 ↔ 2.2, 1 Hz pulse | Idle / waiting |
| Go | `#2BE8A6` | 2.6 steady | Satisfied |
| Hazard | `#FF2BD6` | 3.0 steady | Lasers, kill surfaces. Hazard surfaces also carry 45° Ink stripes (pitch 0.6 units, 50% duty) |
| Exit | `#FFFFFF` | 3.0 steady | Exit portal, drawn as the four-pane mark |

State never depends on hue alone: idle pulses, satisfied is steady, hazard is striped.

### 2.4 Dips — the room

Each environment preset (§6.4) owns one dip. A dip is four colours.

| Dip | light | mid | deep | haze | Hero candy | Banned candy |
|---|---|---|---|---|---|---|
| Mint | `#DDF5EA` | `#B4E6D2` | `#7CCDB3` | `#EAF8F1` | Cherry | Lime |
| Butter | `#FFF4CC` | `#FFE699` | `#F2CC5C` | `#FFF9E3` | Grape | Lemon |
| Pool | `#DCEEFB` | `#B5D9F5` | `#7FB8E8` | `#EAF5FD` | Tangerine | Lagoon |
| Peach | `#FFE6D6` | `#FFCDB2` | `#F2A88A` | `#FFF1E8` | Lagoon | Tangerine |
| Lilac | `#E9E2FA` | `#CFC2F2` | `#A996E0` | `#F1ECFC` | Lemon | Grape |
| Plum (night) | `#5A4A82` | `#453769` | `#2F2550` | `#3A2E5C` | Lime | none |

### 2.5 Usage rules

1. **Room and level statics** use dip tones only: matte, patterned, hazed. No second hue ever enters a room.
   If a room reads flat, raise the contrast between `mid` and `deep`; do not add a colour.
2. **Top-light rule.** On every static, faces with world normal.y above 0.7 take the *top* tone and the rest
   take the *side* tone (`lerp(side, top, smoothstep(0.5, 0.8, N.y))` in `RoomLit`). Tone pairs:

   | Surface | Top | Side |
   |---|---|---|
   | Shell floor | mid | — |
   | Shell walls | — | light (mid below the dado line at y = 30) |
   | Skirting, window frame, door frame | Paper | Paper |
   | Level statics (platforms, ramps, walls) | light | deep |
   | Backdrop furniture | mid | deep |

   Platforms therefore read light-on-mid against the floor with no outline.
3. **Toys** carry one candy colour on at least 60% of their surface. Paper and Ink are allowed for details.
   Never the preset's banned candy (it is too close to the room's hue). The level's key toy takes the
   preset's hero candy, the complement of the dip, unless the object has an obvious colour of its own
   (cheese is Lemon, an apple is Cherry). Metal toys are anodised candy, not bare silver (the Level 2 thimble is
   anodised Tangerine). Cardboard toys are Kraft with a candy tape strip covering at least 40%.
4. **Non-grabbable physics props** (barricade blocks, fixed dominoes) use `ToyLit` in Birch, Kraft, Steel or
   a dip tone, with rim 0 and no colour pool. Candy plus rim plus pool means "you can lift this", always.
   When a toy stops being grabbable (the seated thimble), its rim and pool fade to 0 over 300 ms.
5. **Gadgets**: Ink satin body, exactly one Signal emissive element, Steel for moving metal.
6. **UI**: Ink on Paper; Cherry for the single primary action; Lagoon and Cherry on the scale readout.
7. **No black, no grey.** Shadow is a deeper tint of the room hue (ambient ground colour is the dip's
   `deep`). Nothing renders darker than Ink.

### 2.6 How grabbables are made unmistakable (no highlight boxes)

Four passive cues, always on, plus one on focus:

1. **Chroma.** Candy saturation exists nowhere else in the world.
2. **Kicker rim.** A Paper-white Fresnel rim on every grabbable, whatever its material. It is a luminance
   cue, so it survives colour-vision deficiency.
3. **No haze.** Toys never take haze, so they stay saturated at 150 units while the room fades behind them.
4. **Colour pool.** Each resting toy sits in a pool of its own colour on the floor (§3.3).
5. **On focus** (aim ray on a grabbable): one diagonal glint sweep crosses the toy (300 ms), its pool
   brightens 20%, and the four reticle panes spread and rotate 45°.

Accessibility setting "High-visibility toys": rim × 1.6, pool gain × 1.5.

---

## 3. Shader plan

### 3.1 Inventory

Five hand-written shaders. All live in `Assets/Toybox/Shaders/`, share `ToyboxCommon.hlsl`, and each has a
template material in `Assets/Toybox/Resources/Materials/` created by `ProjectSetup.CreateMaterials()`.

| Shader | Purpose | Passes | Used by |
|---|---|---|---|
| `Toybox/ToyLit` | Every toy, non-grabbable prop and gadget. Product lighting: wrap diffuse, GGX key highlight, analytic StudioEnv reflection, four-pane glint, rim. Opaque or blended via render-state properties | `ForwardLit`, `ShadowCaster`, `DepthOnly` | `Toys`, `Gadgets` |
| `Toybox/RoomLit` | Every static surface. Dip tones, top-light rule, analytic world-space patterns, colour pools, window patch, splash ring, haze | `ForwardLit`, `ShadowCaster`, `DepthOnly` | Shell, level statics, furniture |
| `Toybox/Sticker` | The held toy's die-cut border and peel shadow. Override material only | `Border` (0), `PeelShadow` (1) | Sticker pass (§8) |
| `Toybox/Flat` | Unlit, vertex-coloured, HDR tint, procedural shape mask, blend state from properties | `Unlit` | Sky card, clouds, hull shadows, light shaft, dust, confetti, callout lines, laser cores, exit mark |
| `Toybox/MacroBand` | Full-screen lens blur that depends on screen y only | `MacroBand` | Full Screen Pass feature (§7) |

**Where stock shaders are enough**

- **UI**: `UI/Default` (always included) and TextMeshPro's `TextMeshPro/Distance Field` (shipped by the TMP
  Essential Resources).
- **Post-processing**: URP's own UberPost, Bloom, LutBuilder and FinalPost shaders.
- **`Universal Render Pipeline/Lit`** is kept only as the `?plain=1` debug look. It is not part of the
  shipped look, for two reasons: its material options are `shader_feature` keywords, which are stripped
  from the build for materials created in code; and it pulls in reflection probes, fog, light probes and
  additional-light paths that this art direction bans on toys (§8).
- `Universal Render Pipeline/Unlit` and the URP particle shaders are not used; `Toybox/Flat` covers them
  with zero keywords.

**Rules for all five shaders**

1. **No `shader_feature` at all.** Options are uniforms. A code-made material can then never hit a stripped
   variant.
2. `SubShader` tags: `"RenderPipeline"="UniversalPipeline"`. `#pragma target 3.5` (GLES 3.0).
3. One `CBUFFER_START(UnityPerMaterial)` block, identical in every pass (SRP Batcher compatible). Do not
   count on the SRP Batcher on WebGL 2; draw calls are controlled by merging meshes (§12).
4. No sampling of `_CameraDepthTexture` or `_CameraOpaqueTexture`, ever. Both are disabled in the URP
   assets, which removes the depth prepass and the opaque copy.
5. No `multi_compile_fog`, no `multi_compile_instancing`, no lightmap, light-probe, reflection-probe,
   additional-light, light-cookie, decal, Forward+ or screen-space-shadow keywords.
6. Lit passes use exactly these keyword lines, which is what makes main-light shadows work on WebGL:

```hlsl
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
```

   The shadow coordinate is computed per fragment (`TransformWorldToShadowCoord(positionWS)`), which is
   required with cascades, and the light is fetched with
   `GetMainLight(shadowCoord, positionWS, half4(1,1,1,1))` so URP's distance fade is applied.
7. `ShadowCaster` pass: `Tags{"LightMode"="ShadowCaster"}`, `ZWrite On`, `ColorMask 0`, `Cull [_Cull]`.
   Vertex: `TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection))`, then clamp z to
   `UNITY_NEAR_CLIP_VALUE` (the `UNITY_REVERSED_Z` pair of `min`/`max`). No punctual-light variant.
8. `DepthOnly` pass: `Tags{"LightMode"="DepthOnly"}`, `ZWrite On`, `ColorMask R`. It is unused in the
   shipped pipeline (no depth texture) and exists so the shaders stay correct if someone turns on depth
   priming or a depth texture while debugging. **No `DepthNormals` pass**: nothing consumes normals
   (no SSAO, no decals), and adding one invites features this direction bans.

### 3.2 `Toybox/ToyLit`

**Material properties** (all in `UnityPerMaterial`)

| Property | Range / default | Meaning |
|---|---|---|
| `_BaseColor` | linear rgb | Albedo. Multiplied by vertex colour (chipped edges) |
| `_Metallic` | 0..1 / 0 | Metallic workflow |
| `_Smoothness` | 0..1 / 0.6 | 1 − roughness |
| `_Wrap` | 0..1 / 0.2 | Diffuse wrap: `saturate((N·L + w) / (1 + w))` |
| `_Env` | 0..2 / 0.5 | StudioEnv reflection gain |
| `_Coat` | 0..1 / 0 | Clear-coat lobe: a second, sharp StudioEnv reflection with F0 0.04 |
| `_Streak` | 0..0.3 / 0 | Shifts the StudioEnv bands by detail.g (brushed metal) |
| `_Glint`, `_GlintSoft`, `_GlintStretch` | 0..2 / 1, 0.02, 1 | Four-pane glint gain, edge softness, horizontal stretch |
| `_Rim`, `_RimPow`, `_RimColor` | 0..1 / 0.55, 3, Paper | Kicker rim. `_Rim = 0` on anything not grabbable |
| `_SelfGlow` | 0..1 / 0.06 | Emissive = albedo × `_SelfGlow` × global `_ToyGlowGain` |
| `_Emission` | HDR rgb / 0 | Signal elements |
| `_Translucency` | 0..1 / 0 | Back-lighting for thin sheets |
| `_DetailMap` (+`_ST`) | 2D / Neutral | R albedo, G smoothness, B height; 0.5 is neutral (§4.2) |
| `_DetailAlbedo`, `_DetailSmooth`, `_DetailBump` | 0..1 / 0 | Detail strengths. `_DetailBump` is forced to 0 below High |
| `_AlphaFace`, `_AlphaEdge`, `_AlphaPow` | 1, 1, 2.5 | Fresnel alpha (glass) |
| `_ShadowDither` | 0 or 1 / 0 | 50% checker discard in `ShadowCaster` (glass) |
| `_SrcBlend`, `_DstBlend`, `_ZWrite`, `_Cull` | One, Zero, 1, Back | Render state (`Blend [_SrcBlend] [_DstBlend]`, etc.) |

**Per-renderer (MaterialPropertyBlock)**: `_Sweep` (centre.xy in pixels, position, gain) for the focus
glint; `_SquashA` (centre.x, pivot.y, centre.z, amount) for impact squash.

**Vertex inputs**: position, normal, `TEXCOORD0` (object-space UV), `COLOR` (default white),
`TEXCOORD3` (outline normal, used only by `Toybox/Sticker`).

**Fragment** (sketch; URP library calls are real, the rest is this shader's own):

```hlsl
half3 Tri(float3 N) {                       // ambient: three global colours, position-independent
    return N.y >= 0 ? lerp(_AmbEquator.rgb, _AmbSky.rgb, N.y) : lerp(_AmbEquator.rgb, _AmbGround.rgb, -N.y);
}
half3 StudioEnv(float3 R, half rough, half shift) {   // ceiling / wall / floor bands of an infinite softbox
    half w = lerp(0.03, 0.7, rough), y = R.y + shift;
    half3 c = lerp(_EnvFloor.rgb, _EnvWall.rgb, smoothstep(-w, w, y));
    return lerp(c, _EnvCeil.rgb, smoothstep(0.45 - w, 0.45 + w, y));
}
half WindowPane(float3 R, half soft, half stretch) {  // 2x2 rounded panes, about 31 degrees wide
    float z = dot(R, _WinDir.xyz);
    float2 p = float2(dot(R, _WinRight.xyz) / stretch, dot(R, _WinUp.xyz)) / max(z, 1e-3);
    float2 q = abs(abs(p) - 0.17) - 0.105;
    float sd = length(max(q, 0)) + min(max(q.x, q.y), 0) - 0.025;
    float w = fwidth(sd) + soft;
    return step(0, z) * smoothstep(w, -w, sd);
}

half4 Frag(Varyings i) : SV_Target {
    float3 N = normalize(i.normalWS);
    float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
    half3 det = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.uv).rgb;
    // High tier only: tangent-free bump (Mikkelsen). h is in world units so it scales with the toy.
    //   h = (det.b - 0.5) * _DetailBump * objectScale;  N = normalize(|D| * N - surfaceGradient(h)),
    //   where D = dot(ddx(P), cross(ddy(P), N)). Scaling h by objectScale keeps the result identical at any hold distance.
    half3 albedo = _BaseColor.rgb * i.color.rgb * (1 + (det.r - 0.5) * 2 * _DetailAlbedo);
    half  smooth = saturate(_Smoothness * (1 + (det.g - 0.5) * 2 * _DetailSmooth));
    BRDFData brdf; half one = 1;
    InitializeBRDFData(albedo, _Metallic, half3(0,0,0), smooth, one, brdf);

    Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS), i.positionWS, half4(1,1,1,1));
    half shadow = lerp(sun.shadowAttenuation, 1, _ToyHeld);          // the only position-dependent term
    half ndl = dot(N, sun.direction), ndv = saturate(dot(N, V));
    half3 c = brdf.diffuse * (sun.color * (saturate((ndl + _Wrap) / (1 + _Wrap)) * shadow)
                            + _KickColor.rgb * saturate(dot(N, _KickDir.xyz)) + Tri(N));
    c += brdf.specular * DirectBRDFSpecular(brdf, N, sun.direction, V) * sun.color * (saturate(ndl) * shadow);

    float3 R = reflect(-V, N);
    half f5 = pow(1 - ndv, 5), fr = pow(1 - ndv, _RimPow);
    c += StudioEnv(R, brdf.perceptualRoughness, (det.g - 0.5) * _Streak) * EnvironmentBRDFSpecular(brdf, f5) * _Env;
    c += StudioEnv(R, 0.06, 0) * ((0.04 + 0.96 * f5) * _Coat * 0.6);
    c += _GlintColor.rgb * (1.8 * _Glint * (0.6 + 0.4 * fr) * WindowPane(R, _GlintSoft, _GlintStretch));
    c += _RimColor.rgb * (_Rim * fr * (0.35 + 0.65 * saturate(N.y * 0.5 + 0.5)));
    c += albedo * (_SelfGlow * _ToyGlowGain) + _Emission.rgb;
    c += sun.color * albedo * (saturate(-ndl) * _Translucency * shadow);
    c += 0.5 * _Sweep.w * smoothstep(0.06 * _ScaledScreenParams.y, 0,
             abs(dot(i.positionCS.xy - _Sweep.xy, float2(0.7071, 0.7071)) - _Sweep.z));
    return half4(c, lerp(_AlphaFace, _AlphaEdge, pow(1 - ndv, _AlphaPow)));
}
```

Everything except `shadow` depends only on N, V and directions at infinity. That is the whole reason the
held toy cannot betray its distance (§8). **`ToyLit` never applies haze, pools, the window patch or any
other term that reads `positionWS`, held or not.** Glint softness uses `fwidth` of a reflection-space value,
which is constant under the hold transform.

The squash in the vertex stage (also in `ShadowCaster`), in world space:
`p.y = piv + (p.y - piv) * (1 - q); p.xz = ctr + (p.xz - ctr) * (1 + 0.5 * q)`.

### 3.3 `Toybox/RoomLit`

**Material properties**

| Property | Default | Meaning |
|---|---|---|
| `_ColorTop`, `_ColorSide` | dip tones | Top-light rule pair (§2.5) |
| `_ColorDado`, `_DadoY` | mid, 30 | Side faces below `_DadoY` take `_ColorDado`. `_DadoY = -1e5` disables it |
| `_Pattern` | 0 | 0 none, 1 dots, 2 planks, 3 tiles, 4 stripes, 5 quilt, 6 pegboard, 7 corrugated |
| `_PatternA` | per pattern | (pitch, param1, param2, gain). Gain is 0.04 by default |
| `_Corner` | 0 | 1 on shell surfaces: corner gradient |
| `_Cull` | Back | |

Vertex colour is baked ambient occlusion (furniture bases, creases); default white.

**Globals read by `RoomLit`** (set by `Render/LookGlobals`): `_AmbSky/_AmbEquator/_AmbGround`,
`_KickDir/_KickColor`, `_PoolPos[16]`, `_PoolTint[16]`, `_PoolCount`, `_PoolGain`,
`_WinO/_WinU/_WinV/_WinN/_WinMullion`, `_PatchColor`, `_Splash`, `_SplashTint`, `_ShellMin/_ShellMax`,
`_HazeColor`, `_HazeParams`.

**Fragment**, in order:

```hlsl
float3 N = normalize(i.normalWS), P = i.positionWS;
// 1. tone
half top = smoothstep(0.5, 0.8, N.y);
half3 side = P.y < _DadoY ? _ColorDado.rgb : _ColorSide.rgb;
half3 albedo = lerp(side, _ColorTop.rgb, top) * i.color.rgb;
// 2. pattern: analytic, world space, plane picked by dominant normal axis
float2 uv = abs(N.y) > 0.7 ? P.xz : (abs(N.x) > abs(N.z) ? P.zy : P.xy);
albedo *= 1 + Pattern(uv, _Pattern, _PatternA) * _PatternA.w;        // Pattern returns -1..1, already anti-aliased
// 3. corner gradient (shell only): d = distance to the nearest shell plane other than the one P lies on
albedo *= 1 - 0.22 * _Corner * exp(-d / 6.0);
// 4. colour pools: analytic sphere occluder + colour bounce
half core = 0; half3 glow = 0;
for (int k = 0; k < _PoolCount; k++) {                 // 6 / 10 / 16 by tier
    float3 dv = _PoolPos[k].xyz - P; float r = _PoolPos[k].w; half a = _PoolTint[k].a;
    float l2 = dot(dv, dv);
    if (l2 > 36.0 * r * r) continue;
    half nl = max(dot(N, dv) * rsqrt(l2), 0);
    half c = nl * r * r / max(l2, r * r);              // dark contact core, radius about r
    half h = nl * 6.25 * r * r / (l2 + 6.25 * r * r);  // colour halo, visible to about 4r
    core = max(core, a * c);
    glow += _PoolTint[k].rgb * (a * h * (1 - c));
}
albedo *= 1 - 0.65 * core;
// 5. light
Light sun = GetMainLight(TransformWorldToShadowCoord(P), P, half4(1,1,1,1));
half lit = saturate(dot(N, sun.direction)) * sun.shadowAttenuation;
half3 c = albedo * (sun.color * lit + _KickColor.rgb * saturate(dot(N, _KickDir.xyz)) + Tri(N));
// 6. emissive adds: pools, window patch, splash ring
c += albedo * glow * _PoolGain;
c += albedo * _PatchColor.rgb * (Patch(P, sun.direction) * lit);
half ring = 1 - smoothstep(0, _Splash.w * 0.25, abs(distance(P, _Splash.xyz) - _SplashTint.a));
c += albedo * _SplashTint.rgb * ring;                  // _Splash.w = prop radius, _SplashTint.a = current ring radius
// 7. haze: exp-squared distance + height
float dist = distance(P, _WorldSpaceCameraPos);
half fd = 1 - exp(-pow(dist * _HazeParams.x, 2));
half fh = smoothstep(_HazeParams.y, _HazeParams.z, P.y) * _HazeParams.w * saturate(dist / 120.0);
c = lerp(c, _HazeColor.rgb, 1 - (1 - fd) * (1 - fh));
return half4(c, 1);
```

**Window patch** (the four-pane mark as light on the floor, walls and furniture; no decal, no cookie):

```hlsl
half Patch(float3 P, float3 L) {                        // L points toward the sun
    float t = dot(_WinO.xyz - P, _WinN.xyz) / dot(L, _WinN.xyz);     // distance along the sun ray to the window plane
    float3 Q = P + L * t - _WinO.xyz;
    float2 p = float2(dot(Q, _WinU.xyz), dot(Q, _WinV.xyz));         // _WinU/_WinV = axis / half-extent, |p| <= 1 inside
    float soft = 0.012 + 0.0006 * t;                                 // penumbra widens with throw
    float2 q = abs(abs(p) - 0.5) - (0.5 - _WinMullion);              // four panes
    return step(0, t) * smoothstep(soft, -soft, max(q.x, q.y));
}
```

The patch is multiplied by `lit`, so toy and platform shadows cut cleanly through it. The sun itself is not
gated by the window: direct light and cast shadows exist everywhere, because the shadow is a gameplay cue.

**Patterns** (all in world units, anti-aliased with `fwidth`, and faded out by
`1 - smoothstep(0.25, 0.6, fwidth(coord) / pitch)` so nothing shimmers at distance):

| `_Pattern` | Recipe | Default `_PatternA` |
|---|---|---|
| 1 Dots | Disc of radius r on a square grid | pitch 8, r 1.4 |
| 2 Planks | Boards of width w and length l, each row offset by a row hash; gap line g | w 4, l 60, g 0.12 |
| 3 Tiles | Checker of `_ColorTop` and `_ColorSide` with a grout line | pitch 5, grout 0.15 |
| 4 Stripes | Vertical bands, 50% duty | pitch 12 |
| 5 Quilt | 45° diamonds with a stitched seam line | pitch 6, seam 0.1 |
| 6 Pegboard | Dark holes on a square grid; gain −0.25 inside the hole | pitch 0.85, r 0.11 |
| 7 Corrugated | Fine flute stripes on faces whose normal is horizontal; broad faint ribs elsewhere | flute 0.15, rib 0.5 |

These patterns are the absolute scale ruler: their pitch never changes, so a toy released at ×10 is visibly
ten rug-dots wide.

### 3.4 `Toybox/Sticker`

No material properties. Globals: `_StickerPx` (border px, shadow dx px, shadow dy px), `_StickerColor`
(Paper × 1.1), `_PeelColor` (`lerp(white, Ink, 0.22)`).

```hlsl
// both passes: constant-pixel extrusion along the smoothed outline normal (TEXCOORD3)
float4 clip = TransformObjectToHClip(v.positionOS.xyz);
float2 dir  = normalize(TransformWorldToHClipDir(TransformObjectToWorldNormal(v.outlineNormal)).xy * _ScaledScreenParams.xy);
clip.xy += dir * (_StickerPx.x * 2.0 / _ScaledScreenParams.xy) * clip.w;
// PeelShadow pass only: screen-space offset, y flipped by _ProjectionParams.x
clip.xy += float2(_StickerPx.y, _StickerPx.z * _ProjectionParams.x) * 2.0 / _ScaledScreenParams.xy * clip.w;
```

| Pass | State | Output |
|---|---|---|
| 0 `Border` | `ZTest Always`, `ZWrite Off`, `Cull Off`, `Stencil { Ref 0 Comp Equal Pass IncrSat }` | `_StickerColor` |
| 1 `PeelShadow` | same, plus `Blend DstColor Zero` | `_PeelColor` |

The stencil makes each pass touch a pixel once, so concave toys are not double-darkened and the shadow never
draws over the border. If the camera's depth attachment turns out to have no stencil on some device, the
fallback is to write the peel shadow at the far plane with `ZTest Less`, `ZWrite On` after the depth clear,
which gives the same once-per-pixel result, and to draw it before the border.

### 3.5 `Toybox/Flat`

Properties: `_Color` (HDR), `_Shape` (0 quad, 1 soft disc, 2 ring, 3 four-pane mark, 4 gloss disc for
confetti), `_Soft`, `_SrcBlend`, `_DstBlend`, `_ZWrite`, `_ZTest`, `_Cull`. Output = `_Color` × vertex
colour × shape mask from UV. One pass, `Tags{"LightMode"="SRPDefaultUnlit"}`. No haze, no lighting.
Works on `ParticleSystemRenderer` (billboard and non-instanced mesh modes).

Blend presets set from code: opaque (One, Zero), alpha (SrcAlpha, OneMinusSrcAlpha), additive (One, One),
multiply (DstColor, Zero).

### 3.6 `Toybox/MacroBand`

Properties: `_RadiusPx`, `_Taps`, `_Full` (0 in play; ramps to 1 for the pause blur, §10.5). Uses URP's
`Blit.hlsl` (`Vert`, `_BlitTexture`, `sampler_LinearClamp`).

```hlsl
half band = max(smoothstep(0.24, 0.5, abs(uv.y - 0.5)), _Full);
if (band < 0.02) return SAMPLE(_BlitTexture, uv);
float r = band * _RadiusPx;                      // already scaled by height/1080 and the user's strength
half3 acc = 0;
for (int k = 0; k < _Taps; k++)                  // Vogel disc: angle = k * 2.39996, radius = sqrt((k + 0.5) / _Taps)
    acc += SAMPLE(_BlitTexture, uv + VogelOffset(k, _Taps) * r * _BlitTexture_TexelSize.xy).rgb;
return half4(acc / _Taps, 1);
```

It runs before tonemapping on the HDR colour. It never reads depth: blur is a function of screen y alone,
and the crosshair always sits in the sharp band.

### 3.7 Global uniforms

| Global | Set when | Value |
|---|---|---|
| `_AmbSky`, `_AmbEquator`, `_AmbGround` | preset load | §5 |
| `_KickDir`, `_KickColor` | preset load | §5 |
| `_WinDir`, `_WinRight`, `_WinUp` | preset load | Unit basis; `_WinDir` points toward the sun |
| `_GlintColor` | preset load | `#FFF6E8` day, `#BFD0FF` night |
| `_EnvCeil`, `_EnvWall`, `_EnvFloor` | preset load | white × 1.6, dip `light`, dip `mid` × 0.7 |
| `_ToyGlowGain` | preset load | 1 day, 6 night |
| `_ToyHeld` | inside the sticker pass only | 1 during the pass, 0 otherwise |
| `_HazeColor`, `_HazeParams` | preset load | colour; (density, height start 60, height end 150, height max 0.5) |
| `_PoolPos[16]`, `_PoolTint[16]`, `_PoolCount`, `_PoolGain` | every frame | §9.4 |
| `_WinO`, `_WinU`, `_WinV`, `_WinN`, `_WinMullion`, `_PatchColor` | level load | §6.2 |
| `_Splash`, `_SplashTint` | on release, animated | `_Splash` = (centre, prop radius); `_SplashTint` = (colour × strength, current ring radius). §9.4 |
| `_ShellMin`, `_ShellMax` | level load | Shell AABB |
| `_StickerPx`, `_StickerColor`, `_PeelColor` | every frame while held | §8, §9 |

### 3.8 Build survival for shaders

1. Each of the five shaders has a `.mat` under `Assets/Toybox/Resources/Materials/` (`ToyLit.mat`,
   `RoomLit.mat`, `Sticker.mat`, `Flat.mat`, `MacroBand.mat`). Runtime code does
   `new Material(Resources.Load<Material>("Materials/ToyLit"))`. `Shader.Find` is never the only reference.
   The existing `ToyLit.mat` currently points at `Universal Render Pipeline/Lit`; `ProjectSetup` must
   repoint it to `Toybox/ToyLit` once the shader exists.
2. The `multi_compile` shadow keywords above are stripped by URP according to the features enabled on the
   URP assets in Quality Settings. All three tier assets therefore keep **main-light shadows on, two or
   three cascades (never one), soft shadows on** (§7.3). A single cascade uses a different keyword
   (`_MAIN_LIGHT_SHADOWS`), and so does hard shadowing. Never switch to a state no shipped asset has at
   runtime, or the variant will be missing and shadows silently vanish.
3. `ProjectSetup` writes a `ShaderVariantCollection` (`Resources/ToyboxVariants.shadervariants`) listing
   the five shaders with the cascade and soft-shadow keywords of each tier. `Bootstrap` calls `WarmUp()`
   behind the loading screen so the first grab does not hitch on a shader compile.

---

## 4. Material recipes

All toy and gadget materials are `Toybox/ToyLit` with different numbers. `Art/Materials.Get(recipe, colour)`
returns one shared `Material` per (recipe, colour) pair, so identical toys share a material.

### 4.1 Geometry rules for toys (requirements on `Toybox.Toys`)

- Every edge is bevelled by at least 4% of the toy's smallest dimension, with smooth normals across the
  bevel. Highlights and the rim carry this look; a hard 90° edge has neither.
- `TEXCOORD3` holds the **outline normal**: the average of the normals of all vertices at the same
  position. `Art/MeshUtil.BakeOutlineNormals(mesh)` computes it; every toy mesh passes through it.
- UVs are object-space with one texture repeat per authored unit (at scale 1). Never world-space, never
  triplanar: a world-anchored texture would slide as the held toy is re-projected.
- Optional vertex colour tints (chipped paint). Default white.
- No `LODGroup`, no distance-based anything. A toy is at most 3 draw calls (merge sub-parts by material)
  and 3,000 triangles.
- Toys report a material recipe, a candy colour, a bounding radius and, for long or ring-shaped toys, up to
  three pool proxy spheres (§9.4).

### 4.2 Procedural detail textures

One packed data texture per recipe: **R = albedo modulation, G = smoothness modulation, B = height**;
128 is neutral in every channel.

- `new Texture2D(256, 256, TextureFormat.RGBA32, mipChain: true, linear: true)`; fill a `Color32[]`;
  `SetPixels32`; `Apply(updateMipmaps: true, makeNoLongerReadable: true)`.
- `wrapMode = Repeat`, `filterMode = Trilinear`, `anisoLevel = 4` (1 on Low). 128² on Low.
- Generated lazily on first use by `Art/TexCache`, cached for the session, at most one texture per frame
  while the loading screen is up. Budget: 9 textures, under 3 MB, under 60 ms of generation in total.
- Deterministic: a local xorshift seeded with FNV-1a of the recipe name. Never `UnityEngine.Random`, never
  `game.Rng` (texture generation must not perturb simulation determinism).
- Headless runs (`SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`) skip generation and return
  `Texture2D.grayTexture`, which is exactly neutral.

| Texture | Used by | Recipe (values are offsets from 128) |
|---|---|---|
| `Neutral` | default | 4×4, all 128 |
| `Peel` | glossy plastic (High only) | B: two octaves of value noise, cells 16 px and 8 px, ±20. R, G neutral |
| `Brush` | painted wood | 40 horizontal strokes, each a full-width band 3–9 px tall: R ±10, B ±15, G ∓5 |
| `Stipple` | rubber | 900 dots of radius 1.5–2.5 px with Gaussian falloff: B +40, G −10 |
| `Streak` | brushed metal | Each row gets a random value ±16, then a 24 px wrap-around box blur along U. Written to G at full strength and R at half |
| `Fibre` | felt, fabric | 3,000 strokes 6–14 px long at random angles, each adding ±2 to R and ±6 to B (accumulating, clamped ±24) |
| `Speckle` | cardboard, paper | Top half: 4,000 one-pixel speckles, R ±15. Bottom quarter: sine flute stripes of period 6 px, B ±30, R ±8 (edge faces are UV-mapped here) |
| `Pore` | sponge | 220 ellipses with 2–9 px semi-axes: R −58 (albedo × 0.55), B −60. Plus 600 single-pixel dots at half strength |
| `Barb` | feather | 256×64. 90 lines at ±35° from the centre line (the rachis runs along U): R ±18, B ±40, G +10 |

### 4.3 Toy recipes

`candy` is the toy's candy colour. Columns are `ToyLit` properties; anything not listed is the default.

| Material | `_BaseColor` | Metal | Smooth | Wrap | Env | Coat | Glint / Soft / Stretch | Rim / Pow | SelfGlow | Detail: map, albedo / smooth / bump |
|---|---|---|---|---|---|---|---|---|---|---|
| **Glossy plastic** | candy | 0 | 0.62 | 0.20 | 0.5 | 1.0 | 1.0 / 0.02 / 1 | 0.55 / 3 | 0.06 | `Peel`, 0 / 0 / 0.03 |
| **Painted wood** | candy | 0 | 0.45 | 0.25 | 0.3 | 0.35 | 0.35 / 0.25 / 1 | 0.40 / 3 | 0.03 | `Brush`, 0.08 / 0.2 / 0.6 |
| **Rubber** | candy × 0.92 | 0 | 0.18 | 0.50 | 0.1 | 0 | 0.12 / 0.5 / 1 | 0.65 / 2.5 | 0.04 | `Stipple`, 0 / 0.3 / 0.15 |
| **Brushed metal** (anodised) | candy | 1 | 0.66 | 0 | 1.2 | 0.3 | 0.8 / 0.15 / 3 | 0.30 / 3 | 0 | `Streak`, 0.06 / 0.5 / 0 |
| **Glass** | `lerp(white, candy, 0.35)` | 0 | 0.96 | 0 | 1.6 | 1.0 | 1.4 / 0.01 / 1 | 0.8 / 2.5 | 0 | none |
| **Felt / fabric** | `lerp(candy, #808080, 0.1)` | 0 | 0 | 0.60 | 0 | 0 | 0 | 0.9 / 2 | 0.05 | `Fibre`, 0.16 / 0 / 1.0 |
| **Cardboard / paper** | Kraft or Paper | 0 | 0.08 | 0.30 | 0.05 | 0 | 0 | 0.35 / 3 | 0.02 | `Speckle`, 0.12 / 0 / 0.3 |
| **Sponge** | `lerp(candy, white, 0.15)` | 0 | 0 | 0.60 | 0 | 0 | 0 | 0.5 / 2.5 | 0.05 | `Pore`, 0.9 / 0 / 1.0 |
| **Feather** | `lerp(Paper, candy, 0.65)` | 0 | 0.40 | 0.50 | 0.1 | 0 | 0.25 / 0.2 / 4 | 0.7 / 2 | 0.03 | `Barb`, 0.25 / 0.3 / 0.4 |

**Per-material extras**

| Material | Extra |
|---|---|
| Glossy plastic | The default toy material. Its window glint is the brand mark; do not soften it |
| Painted wood | The toy builder tints bevel-ring vertices 30% toward Birch with seeded noise: chipped edges |
| Rubber | `_RimColor = lerp(candy, white, 0.6)`. No sharp highlight at all. Squashes 14% on impact |
| Brushed metal | `_Streak = 0.18`. UVs run along the brush direction. The stretched glint and the three hard StudioEnv bands are what make it read as metal |
| Glass | Blend SrcAlpha / OneMinusSrcAlpha, `_ZWrite 0`, queue 3000. `_AlphaFace 0.22`, `_AlphaEdge 0.85`, `_AlphaPow 2.5`. `_RimColor = candy`. `_ShadowDither 1` (half-density shadow). Medium and High add a back shell: a second material on the same renderer with `_Cull Front`, flat alpha 0.18, queue 2999. Its colour pool is tinted × 1.6, which reads as a caustic |
| Felt / fabric | `_RimColor = lerp(candy, white, 0.35)`: the rim is the fuzz |
| Cardboard / paper | Each cardboard toy carries a candy tape strip (plastic recipe, smoothness 0.7) covering at least 40%. Paper sheets: `_Cull Off`, `_Translucency 0.5` |
| Sponge | The builder displaces a subdivided rounded box by 1.5% seeded noise for a lumpy silhouette. Squashes 18% |
| Feather | Real geometry, no alpha test: a leaf outline with three notches, bent 8% along its length, plus a tapered Paper quill. `_Cull Off`, `_Translucency 0.8` |

Tier differences are deliberately tiny: `_DetailBump` is 0 below High, detail maps are 128² on Low, the
glass back shell is dropped on Low. Glint, rim, coat and colour are identical on every tier.

### 4.4 Room, gadget and effect recipes

| Thing | Shader | Values |
|---|---|---|
| Shell floor | `RoomLit` | top = mid; pattern from the preset; `_Corner 1` |
| Shell walls | `RoomLit` | side = light, dado = mid at y 30; pattern from the preset; `_Corner 1` |
| Skirting, frames | `RoomLit` | top = side = Paper; no pattern |
| Level statics | `RoomLit` | top = light, side = deep; pattern chosen by the level (default none) |
| Backdrop furniture | `RoomLit` | top = mid, side = deep; vertex AO |
| Non-grabbable prop | `ToyLit` | Birch, Kraft, Steel or a dip tone; painted-wood numbers; `_Rim 0`, glint at most 0.3; no pool |
| Gadget body | `ToyLit` | Ink; metallic 0.1, smoothness 0.5, env 0.3, glint 0.3 / 0.2 / 1, `_Rim 0`, `_SelfGlow 0` |
| Gadget signal element | `ToyLit` | Base Ink; `_Emission` = signal colour × its HDR gain (§2.3); glint 0 |
| Gadget moving metal | `ToyLit` | Steel; brushed-metal numbers with `_Rim 0` |
| Water surface | `ToyLit` | Glass numbers with base = dip `deep`, `_AlphaFace 0.45`, `_AlphaEdge 0.9`, `_Rim 0` |
| Hazard surface | `ToyLit` | Geometry stripes at 45°, pitch 0.6: alternating strips of the gadget-body and Hazard-signal materials, merged per material (2 draws) |
| Laser beam | `Flat` | Additive; Hazard × 3; soft-disc mask across the beam |
| Exit portal | `Flat` | Four-pane mark; white × 3 |
| Sky card, clouds | `Flat` | Opaque; vertex gradient `#BFE3FF` → `#FFF6E0`, × 1.8 |
| Hull shadows | `Flat` | Multiply; colour `lerp(white, deep, vertexAlpha)`, alpha 0.35 → 0 |

---

## 5. Lighting rig

One real light. Everything else is a global uniform, which keeps the rig identical in the editor `Shots`
capture (no Play Mode) and in the WebGL build.

### 5.1 The rig

| Element | Unity object | Colour | Intensity | Notes |
|---|---|---|---|---|
| **Key (sun)** | One `Light`, `LightType.Directional`, `LightShadows.Soft`, pipeline bias settings | `#FFF1DC` | 0.62 | The only shadow caster and the only `Light` in the scene |
| **Kicker** | Globals `_KickDir`, `_KickColor`. No `Light` | dip `light` | × 0.20 | From sun azimuth + 145°, elevation 20°. Unshadowed |
| **Ambient** | Globals `_AmbSky`, `_AmbEquator`, `_AmbGround` | sky white; equator `lerp(white, light, 0.5)`; ground dip `deep` | × 0.45, × 0.42, × 0.40 | Mirrored into `RenderSettings` (`AmbientMode.Trilight`) for anything stock |
| **Reflections** | None. `StudioEnv` in `ToyLit` | ceiling white × 1.6, wall dip `light`, floor dip `mid` × 0.7 | — | No reflection probes, no cubemap |
| **Sky** | `RenderSettings.skybox = null`; camera clears to the haze colour | | | The sky is a card outside the window (§6) |
| **Fog** | `RenderSettings.fog = false` | | | Haze is computed in `RoomLit` only |

No point, spot or area lights exist anywhere, in any preset. Gadget and fairy-light glow is emissive plus
bloom. This is partly cost and mostly §8: a toy may only be lit by position-independent sources.

**Night rig** (`night-light` preset): key `#BFD0FF` at 0.50; ambient sky `#8C7FD0` × 0.34, equator
`lerp(#8C7FD0, light, 0.5)` × 0.30, ground `deep` × 0.35; `_ToyGlowGain` 6 (toys become glow-in-the-dark
vinyl); `_PoolGain` 0.9 (their pools become the light on the floor); `_GlintColor` `#BFD0FF`.

**Calibration targets** (check in a `Shots` capture; adjust `postExposure` first, intensities second)

1. A sunlit, upward-facing `mid` floor displays within ±4% of its authored hex. (Sum of light on it:
   0.62 × sin 45° + 0.45 + kicker ≈ 0.95.)
2. Lit-to-shadow luminance on that floor is between 1.8:1 and 2.0:1. Airy, never dramatic.
3. A shadow on the floor has the same hue as the floor (±5° in HSV).
4. A Cherry plastic sphere displays its base colour within ±6% at its brightest diffuse point, and its
   window glint clips to white with visible bloom on Medium.

### 5.2 Sun direction

- **Elevation 38°–52°, default 45°.** At 45° a shadow is exactly as long as the object is tall, so the
  shadow is a ruler for height as well as footprint. Never below 38°: longer shadows stop reading as size.
- **Azimuth** is always in the front hemisphere of the level's main travel direction, 30°–75° off axis.
  Shadows of things placed ahead then fall sideways and toward the player, where they can be seen.
- The window is solved from the sun, not the other way round (§6.2), so the four-pane patch always lies on
  the play area and the glint on every toy points at the real window.

### 5.3 Shadows

URP asset values by tier are in §7.3. Common settings: cascades on (never a single cascade), soft shadows,
depth bias 1.0, normal bias 1.0, cascade border 0.1, conservative enclosing sphere on,
`light.shadowStrength = 1`, `light.shadowNearPlane = 0.2`. The first cascade always ends 20 units from the
camera, on every tier, so near shadows look the same everywhere.

| | Low | Medium | High |
|---|---|---|---|
| Atlas (`mainLightShadowmapResolution`) | 2048 → two 1024² tiles (atlas 2048×1024) | 4096 → two 2048² tiles (atlas 4096×2048) | 4096 → three 2048² tiles (atlas 4096×4096) |
| Shadow distance | 110 | 170 | 240 |
| Cascades and splits | 2: `cascade2Split` 0.18 (20 units) | 2: `cascade2Split` 0.118 (20 units) | 3: `cascade3Split` (0.083, 0.33) (20 and 80 units) |
| Soft shadow quality | Low (4 taps) | Medium (9 taps) | High (16 taps) |
| Near cascade texel | about 0.06 units | about 0.03 units | about 0.03 units |
| Last cascade texel | about 0.3 units | about 0.24 units | about 0.33 units (0.11 in the middle cascade) |

Texel sizes assume a 70° vertical field of view at 16:9; URP fits each cascade in a bounding sphere, so a
wide view costs resolution. At 0.03 units per texel the player's own 0.6-unit shadow is 20 texels wide and
a 0.4-unit toy is 13.

- **Casters**: toys that are not held, non-grabbable props, gadgets, level statics, and the player proxy.
- **Player proxy**: a 300-triangle action-figure silhouette (head, torso, two arms, two legs) with
  `ShadowCastingMode.ShadowsOnly`, following the capsule and the camera yaw. Your own shadow is the
  yardstick beside every released toy, and it tells you what you are.
- **Not casters**: the shell (it only receives), backdrop furniture, sky, particles, the held toy.
- **Backdrop furniture** gets one static **hull shadow** mesh per piece instead: project the eight bounding
  box corners along the sun onto the floor, take the convex hull, add a 6-unit feathered ring. All hulls
  merge into one multiply-blended `Flat` draw, built once per level.
- **Small toys.** Beyond the first cascade a toy under about 0.5 units casts nothing visible. The colour
  pool (minimum radius 0.08) still grounds it at true size; that is its job.


---

## 6. Environment kit

The playroom is one shell, a handful of parametric furniture silhouettes, one window and a sky card. All of
it is `RoomLit` in dip tones, so a whole room is about ten draw calls.

### 6.1 Scale anchors (1 unit = 3 cm)

| Anchor | Size in units |
|---|---|
| Player | 1.7 tall |
| Skirting board | 3.5 high, 0.5 deep (twice the player's height: the best "you are tiny" cue) |
| Outlet plate | 2.7 × 4, centre at y = 10 |
| Chair seat / bed top / table top | y = 15 / 17 / 25 |
| Light switch | y = 37 |
| Door | 27 × 67, knob at y = 33, 0.4 gap underneath |
| Window | opening 70 × 105, sill at y = 30, four panes, 3-unit mullions |
| Rug dot / plank / tile / stripe pitch | 8 / 4 / 5 / 12 |

### 6.2 The solve: the sun lands on the level

Levels are authored around their own origin. The room is solved around the level at load, so the four-pane
patch always lies on the play area and each preset is a different place in the same room.

1. **Bounds.** After `Build`, take the level's static AABB `B`. Focus `F` = centre of `B` on the play plane
   (y = 0).
2. **Sun.** Direction `s` (toward the sun) from the preset's elevation `e` and azimuth `a`. Azimuth is
   degrees clockwise from +Z seen from above.
3. **Window wall.** The preset names the shell wall on the sun side (−X or +X). `Δ` is the angle between
   the sun azimuth and that wall's outward direction; presets keep `Δ ≤ 30°`.
4. **Wall distance.** The window centre is 82.5 above the play plane (sill 30 + half of 105), so the wall
   sits `L = 82.5 · cos Δ / tan e` from `F`. Clamp `L` to at least (extent of `B` toward that wall + 20).
   If the clamp bites, raise the window so its centre is `L · tan e / cos Δ`, capped at 120; past the cap
   the patch slides toward the wall, which is acceptable.
5. **Window.** `W` = where the ray `F + s·t` meets the wall plane. `_WinO = W`, `_WinN` = the wall's inward
   normal, `_WinU` = horizontal tangent / 35, `_WinV` = up / 52.5, `_WinMullion = 0.04`.
6. **Shell.** A 400 × 170 × 400 box. The window wall is `L` from `F`; the opposite wall is 400 − `L` away;
   the box is centred on `F` along the other axis. Elevated presets also place the +Z wall at
   (max Z of `B`) + `backWallGap`.
7. **Glint basis.** `_WinDir = s`; `_WinRight`, `_WinUp` complete the basis. The glint on every toy then
   mirrors the real window, and its angular size (about 31°) matches the real opening seen from `F`.

At 45° the patch is roughly 75 × 105 units on the floor, crossed by the mullion shadow, which covers most
levels.

**Repeat visits.** Each time a preset is reused by a later level, lower the elevation by 2° (floor 38°) and
swing the azimuth 6°, in whichever direction keeps `Δ ≤ 30°` and the sun 30°–75° off the travel axis. No
two levels share a light.

### 6.3 The kit

All meshes come from `Art/MeshKit`: `RoundedBox(size, bevel, segments)`, `Cylinder`, `Lathe(profile,
segments)`, `Extrude(shape2D, depth, bevel)`, `Grid`. After building, call `mesh.UploadMeshData(true)`.

**Shell** (5 draws, under 2,000 triangles)

- Floor plane with the preset's pattern. Four walls with the preset's wall pattern and a `mid` dado below
  y = 30. Paper skirting, extruded.
- The ceiling at y = 170 is deliberately too tall. Height haze (start 60, end 150, max 0.5) dissolves it,
  which hides the lack of detail and sells the scale.
- Corner gradient from `_Corner` (§3.3).
- Scale landmarks on the walls: outlet plate, light switch, door with a dark gap underneath.

**Furniture silhouettes.** One merged mesh for all pieces in a level (top = mid, side = deep), plus one
merged Paper trim mesh: 2 draws, under 12,000 triangles. Vertex AO darkens the bottom 6 units of each
piece to 0.75.

| Piece | Size (units) | Built from |
|---|---|---|
| Bed | 115 × 17 (top) × 65, headboard to y = 40 | Rounded boxes, scalloped blanket edge (extrude) |
| Toy chest | 34 × 20 × 20 | Rounded box, lid slab |
| Beanbag | radius 18 | Lathe |
| Chair | seat 15 × 15 at y = 15, back to y = 32 | Rounded boxes, lathe legs r = 0.8 |
| Table | top 60 × 2 × 35 at y = 25 | Rounded box, lathe legs r = 1.5 |
| Bookshelf, book spines | 40 × 85 × 12; spines 1–3 wide, 8–11 tall | Boxes; books merged, not instanced |
| Box stack | 20–40 cubes, slightly rotated | Rounded boxes, `Corrugated` pattern |
| Floor lamp, desk lamp | pole to y = 55; arm lamp 30 tall | Lathe, cylinders |
| Radiator | 30 × 20 × 4, 12 fins | Rounded boxes |
| Door | 27 × 67 | Rounded box, lathe knob |
| Curtain, draped blanket | 25 × 105; blanket 120 × 80 sagging 15 | Extruded sine profile |
| Paper lantern | radius 12 at y = 120 | Lathe |
| Tool silhouettes | hammer 45 long, screwdriver 35, spanner 30 | Extruded 2D outlines, hung on the back wall |
| Fairy lights | 24 bulbs r = 0.5 on a sagging line | Merged spheres, `ToyLit` with emission `#FFE9C4` × 2 (warm white; Signal colours stay reserved for gadgets) |
| Night-light | star lamp, 6 across, at an outlet | Extrude, `ToyLit` with emission `#FFE9C4` × 2.2 |

**Window and sky** (3 draws)

- Paper frame with four panes (extruded).
- A sky card 60 units outside: vertical gradient `#BFE3FF` → `#FFF6E0` at × 1.8 (it blooms gently), with
  three flattened-sphere clouds in Paper. `Flat`, no haze. At night: `#1E1650` → `#3A2E5C` with a Paper
  moon disc at × 2.5.
- Medium and High: one light-shaft prism (the window extruded along the sun, `Flat` additive, alpha 0.06)
  and a `ParticleSystem` of dust motes inside it: quads of 0.04–0.12 units drifting at 0.15 units/s.
  Motes are snowflake-sized next to the player, which is a second scale cue.

**Colliders are part of the kit.** A held toy lands on colliders, not on pixels. Every visible backdrop
surface needs one, or a held toy will be sized against the wall behind a bed it appears to touch. The kit
emits: six shell faces, the window glass pane, and one to four boxes per furniture piece, all on the
`Default` layer. Because these colliders change where the hold march stops, they must exist in headless
bot tests too: the environment *descriptor* (dimensions and collider boxes) is simulation-side data built
by `Game` on level load, and `Render` builds the visuals from the same descriptor. See §13.

### 6.4 Presets

Six presets. The key is the string a level returns from `Environment`. The four names used by the Phase 1
level drafts are preset keys as written.

**Look**

| Key | Dip | Place | Shell floor | Shell walls | Backdrop |
|---|---|---|---|---|---|
| `sunny-rug` | Mint | Bedroom floor. A rug island 160 × 110, 0.5 thick, under the level; boards beyond | Dots (rug), Planks (beyond) | Stripes 12 | Bed, toy chest, beanbag, curtain |
| `block-hall` | Butter | Bare boards among furniture legs | Planks | none, dado only | Chair and table legs, radiator, door ajar |
| `pegboard-workbench` | Pool | A bench top at y = 0, real floor 60 below, back wall 30 behind the level | Pegboard (bench top), Tiles (floor below) | Pegboard on the back wall | Tool silhouettes, desk lamp, box stack |
| `cardboard-box` | Peach | A den of boxes and blankets on the floor | Quilt | none, dado only | Box stacks, draped blanket, floor lamp, fairy lights |
| `high-shelf` | Lilac | A shelf top at y = 0, real floor 75 below, wallpaper wall 30 behind the level | Stripes 12 at gain 0.02 (laminate) | Quilt 6 (wallpaper) | Book spines, desk lamp, paper lantern |
| `night-light` | Plum | The `sunny-rug` layout after dark | Dots, Planks | Stripes 12 | Bed, toy chest, beanbag, night-light, warm door-gap sliver |

**Light and mood**

| Key | Sun colour | Sun intensity | Elevation | Azimuth | Window wall | Haze density | Patch colour × gain | Pool gain | Toy glow gain | Bloom | Exposure (EV) | Music key, BPM |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `sunny-rug` | `#FFF1DC` | 0.62 | 45° | 300° | −X | 0.0024 | `#FFE9C4` × 0.28 | 0.5 | 1 | 0.35 | +0.2 | C major pentatonic, 84 |
| `block-hall` | `#FFF1DC` | 0.66 | 40° | 60° | +X | 0.0024 | `#FFE9C4` × 0.34 | 0.5 | 1 | 0.35 | +0.2 | F major pentatonic, 88 |
| `pegboard-workbench` | `#FFF6EC` | 0.62 | 48° | 285° | −X | 0.0028 | `#FFF0D6` × 0.26 | 0.5 | 1 | 0.35 | +0.2 | G lydian pentatonic, 92 |
| `cardboard-box` | `#FFEFD8` | 0.60 | 50° | 75° | +X | 0.0024 | `#FFE4BC` × 0.30 | 0.55 | 1 | 0.40 | +0.2 | E♭ major pentatonic, 80 |
| `high-shelf` | `#FFF1DC` | 0.64 | 42° | 290° | −X | 0.0032 | `#FFE9C4` × 0.30 | 0.5 | 1 | 0.35 | +0.2 | D dorian pentatonic, 92 |
| `night-light` | `#BFD0FF` | 0.50 | 45° | 300° | −X | 0.0024 | `#BFD0FF` × 0.35 | 0.9 | 6 | 0.55 | +0.2 | A minor pentatonic, 76 |

Ambient, kicker and StudioEnv colours are derived from the dip by the formulas in §5.1 (night uses the
night rig). `backWallGap` is 30 for `pegboard-workbench` and `high-shelf`, unused elsewhere. The higher
haze density on `high-shelf` sells the drop to the real floor.

**Level mapping** (the default when a level does not override `Environment`)

| Level | Preset | Level | Preset | Level | Preset |
|---|---|---|---|---|---|
| 1 | `sunny-rug` | 6 | `block-hall` | 11 | `pegboard-workbench` |
| 2 | `pegboard-workbench` | 7 | `high-shelf` | 12 | `high-shelf` |
| 3 | `cardboard-box` | 8 | `pegboard-workbench` | 13 | `cardboard-box` |
| 4 | `block-hall` | 9 | `high-shelf` | 14 | `night-light` |
| 5 | `sunny-rug` | 10 | `cardboard-box` | 15 | `night-light` |

The hue changes on every level in Phase 1, so the first four screenshots of the game are four different
colours. The finale is the only night: the toys the player has carried all game become the lights.

Level-local set dressing (the toy-chest canyon, the pegboard walkway, the shipping box, the block hall) is
built by the level from `RoomLit` statics with a pattern from §3.3. It takes the room's dip like everything
else: a cardboard box in the Peach room is peach cardboard.


---

## 7. Post-processing and quality tiers

### 7.1 Frame order

| # | Pass | Source | When |
|---|---|---|---|
| 1 | Main-light shadow atlas | URP | always |
| 2 | Opaques (`RoomLit`, `ToyLit`) into the HDR colour target | URP | always |
| 3 | Transparents (glass, hull shadows, shaft, dust, callouts, particles) | URP | always |
| 4 | **MacroBand** lens blur | Stock `FullScreenPassRendererFeature`: material `MacroBand.mat`, injection point Before Rendering Post Processing, fetch colour buffer on, requirements None | Medium, High |
| 5 | **Sticker pass** (§8) | Custom `StickerFeature`, `RenderPassEvent.BeforeRenderingPostProcessing + 1` | only while a toy is held |
| 6 | Bloom | URP | Medium, High |
| 7 | Uber post: tonemap, colour LUT, vignette, dither | URP | always |
| 8 | Final post: FXAA and upscale | URP | Low, and any tier whose render scale is below 1 |
| 9 | UI overlay canvas, native resolution | UGUI | always |

The sticker is drawn after the lens blur on purpose: it is on the lens, so the lens cannot blur it.

There is no depth prepass, no depth copy, no opaque copy, no SSAO, no depth of field, no motion blur, no
TAA, no screen-space reflections and no scene fog. **Motion blur and TAA are banned outright**: both need
motion vectors, and a held toy that jumps between surfaces would smear.

### 7.2 Volume overrides

One global `Volume`, created in code, whose profile is an instance of
`Resources/Volumes/ToyboxPost.asset`. Overrides in URP's execution order:

| Override | Parameter | Value |
|---|---|---|
| **Bloom** | threshold | 1.1 |
| | intensity | preset (0.35 day, 0.40 `cardboard-box`, 0.55 night) |
| | scatter | 0.6 |
| | clamp | 8 |
| | tint | white |
| | high-quality filtering | High only |
| | downscale / max iterations | Half / 4 (Medium), Half / 5 (High) |
| **Tonemapping** | mode | Neutral. ACES is banned: it desaturates and hue-shifts the candy |
| **Color Adjustments** | post exposure | preset (+0.2 EV), plus the release flash (§9.3) |
| | contrast | +8 |
| | saturation | +6 |
| **Vignette** | colour | Ink `#2B2140` |
| | intensity / smoothness | 0.18 / 0.45 |
| | centre, rounded | (0.5, 0.5), off |

Only window glints (× 1.8), Signal emissives, the sky card, fairy lights and the exit mark exceed the bloom
threshold by a wide margin. Sunlit Paper sits just under it and picks up a faint haze, which is the "airy"
quality of the look.

Camera (`UniversalAdditionalCameraData`): `renderPostProcessing = true`, `dithering = true` (mandatory:
pastel gradients band without it), `antialiasing` = FXAA on Low and None otherwise, `stopNaN = false`.
Camera: near 0.1, far 700, clear to solid haze colour. A 70° vertical field of view is assumed throughout.

### 7.3 Quality tiers

Three URP assets generated by `ProjectSetup`: `Settings/ToyboxURP_Low.asset`, `_Medium.asset`,
`_High.asset`, assigned to three Quality levels (WebGL default: Medium). They share one
`ToyboxRenderer.asset`. Tier changes call `QualitySettings.SetQualityLevel(i, true)`.

| Setting | Low | Medium | High |
|---|---|---|---|
| Pixel budget | 1.33 MP (1536×864) | 2.07 MP (1920×1080) | 3.69 MP (2560×1440) |
| `renderScale` | `clamp(sqrt(budget / (Screen.width × Screen.height)), 0.5, 1)`, then the governor (§7.4) | same | same |
| `supportsHDR`, precision | on, 32-bit (R11G11B10) | same | same |
| `msaaSampleCount` | 1 | 2 | 4 |
| Camera anti-aliasing | FXAA | none | none |
| `upscalingFilter` | Linear | Linear | Linear |
| Depth texture / opaque texture | off / off | off / off | off / off |
| Main light | per pixel, shadows on | same | same |
| Additional lights | Disabled | Disabled | Disabled |
| Shadow atlas, distance, cascades, soft quality | §5.3 | §5.3 | §5.3 |
| Colour grading mode / LUT size | LDR / 32 | LDR / 32 | LDR / 32 |
| Bloom | off | on | on, high-quality filtering |
| MacroBand | off | 8 taps, 3.5 px | 12 taps, 5 px |
| Colour pool spheres (`_PoolCount`) | 6 | 10 | 16 |
| Detail maps | 128², aniso 1 | 256², aniso 4 | 256², aniso 4, bump on |
| Glass back shell | off | on | on |
| Light shaft and dust motes | off | shaft, 120 motes | shaft, 300 motes |
| Confetti particles | 60 | 150 | 300 |

Also set on all three assets: mixed lighting off, light cookies off, rendering layers off, reflection-probe
blending and box projection off, SRP Batcher on, dynamic batching on. Some of these have no public setter;
`ProjectSetup` writes the serialized field through `SerializedObject` *(verify field names)*.

On the shared `UniversalRendererData`: rendering path Forward, depth priming Disabled, intermediate
texture Always, `opaqueLayerMask` and `transparentLayerMask` exclude layer 10 (Held), depth attachment
format with a stencil (`D24_S8`) *(verify the enum name)*, renderer features `MacroBand` then `Sticker`.
The MacroBand strength slider multiplies `_RadiusPx` (default 60%); on Low the feature is
`SetActive(false)`.

**Why Low still looks intentional.** The palette, the top-light rule, the patterns, the colour pools, the
glint, the rim, the window patch and the sticker all live in material shaders and are identical on every
tier. Low loses only lens softness and bloom, and reads as a crisp flat-lay print of the same picture.

### 7.4 Automatic fallback

**Starting tier** (`SystemInfo.graphicsDeviceName`, which on WebGL is the unmasked renderer string when the
browser exposes it):

| Condition | Start |
|---|---|
| `B10G11R11_UFloatPack32` not renderable (`SystemInfo.IsFormatSupported(..., GraphicsFormatUsage.Render)` false) | Low, with HDR off and glint gain clamped to 1 |
| Name contains "SwiftShader", "llvmpipe" or "Basic Render"; or `maxTextureSize < 4096`; or a phone or tablet | Low |
| Name contains "Intel" and ("HD Graphics" or "UHD") | Low |
| Anything else, including an empty or masked name | Medium |

High is never chosen automatically at start.

**Governor.** Browsers quantise frame time to the display refresh, so the governor counts slow frames
instead of averaging milliseconds.

1. A *slow frame* is `Time.unscaledDeltaTime > 20 ms`. Frames over 100 ms (tab switch, load, GC) and the
   first 5 s after a level load are ignored.
2. Over a sliding window of 120 frames, if 12 or more are slow: **step down**. A step lowers `renderScale`
   by 0.1 until the tier floor (0.6 on Low, 0.8 on Medium and High), then drops one tier and resets the
   scale. After any step, wait 3 s before judging again (the reallocation itself hitches).
3. **Step up once per session**: after 20 s with no slow frame, raise `renderScale` back toward the tier
   value; if it is already there and the session started on Medium, try High. If a step down follows
   within 10 s, return and lock the tier for the session.
4. The settings menu offers Auto / Low / Medium / High. A manual choice disables steps 2 and 3 and is
   stored in `PlayerPrefs`.

### 7.5 What must be done so these effects survive the WebGL build

1. **Post shaders.** `UniversalRendererData.postProcessData` must reference URP's `PostProcessData.asset`
   (the current `ProjectSetup` already does this). The URP Global Settings asset
   (`Assets/UniversalRenderPipelineGlobalSettings.asset`) must be the registered global settings.
2. **Volume profile as an asset.** `ProjectSetup` generates `Assets/Toybox/Resources/Volumes/ToyboxPost.asset`
   containing Bloom, Tonemapping, Color Adjustments and Vignette, all with `active = true` and their
   parameters overridden. Runtime code loads and instantiates it, then edits values per preset. Do not
   build a `VolumeProfile` from nothing at runtime.
3. **Post variant stripping off.** In URP Global Settings, keep "Strip Unused Post Processing Variants"
   **off**. When on, URP assumes no profile is created or changed at runtime and strips the bloom, tonemap
   and dither variants of the uber shader that no profile asset uses. Item 2 and item 3 together are belt
   and braces.
4. **All three URP assets are referenced from Quality Settings**, and all three Quality levels are enabled
   for the WebGL platform. URP strips shader variants by the union of features on those assets.
5. **MacroBand** survives because the renderer feature references `MacroBand.mat`, which also sits under
   `Resources`.
6. **Sticker** survives because `StickerFeature` has a serialized `Material` field pointing at
   `Sticker.mat`.
7. **TextMeshPro.** `ProjectSetup` imports the TMP Essential Resources package. TMP's outline and underlay
   are `shader_feature` keywords: the sticker text style must exist as a **material preset asset** under
   `Resources/Fonts/` (§10.1), or the die-cut edge on text disappears in the build.
8. **Verification.** One `Shots`-style check in a real WebGL build, not the editor: a frame with a held
   glossy toy must show bloom on the glint, the vignette, the white border and the peel shadow. A missing
   variant renders without error, so this must be looked at.


---

## 8. The held-object rule

**Why the illusion can be made exact.** The engine places the held toy at `eye + dir · d` with scale
`k · d`. That is a uniform scaling about the eye. Perspective projection is invariant under it: every
surface point keeps its pixel, its normal and its view vector at every `d`. So any shading that depends
only on the normal, the view vector and directions at infinity is pixel-identical whether the toy is 3
units away or 90. The rule is therefore simple: **nothing that reads the toy's world position may touch a
held toy.** `ToyLit` was written so that exactly one such term exists (received shadow), and the sticker
pass turns it off.

### 8.1 Implementable steps

1. **Layer.** On grab the engine already moves the prop to layer 10 `Held` (all child renderers).
2. **Remove `Held` from the normal passes.** `UniversalRendererData.opaqueLayerMask` and
   `transparentLayerMask` exclude layer 10. The default opaque and transparent passes skip it. The camera's
   culling mask still includes it, so it is in the cull results for step 5.
3. **Stop it casting.** URP's shadow pass ignores those layer masks, so on `PropGrabbed` the render layer
   sets `shadowCastingMode = Off` on every renderer of the prop, and restores `On` on `PropDropped`.
4. **Stop it pooling.** The prop leaves the colour-pool list. Its last pool stays frozen where the toy was
   grabbed and drains over 120 ms; it does not follow the held toy.
5. **Sticker pass.** `StickerFeature` enqueues one raster pass at `BeforeRenderingPostProcessing + 1`, only
   while something is held. In order:
   1. Clear depth and stencil of the camera target. The toy can no longer intersect or z-fight the wall it
      rests on, and nothing drawn earlier (particles, shaft, glass, dust) can cover it.
   2. `SetGlobalFloat("_ToyHeld", 1)`.
   3. Draw layer `Held` with override material `Sticker`, pass 0 (**border**: Paper, constant pixel width).
   4. Draw layer `Held` with override material `Sticker`, pass 1 (**peel shadow**: hard, offset, multiply).
   5. Draw layer `Held` opaque queue with its own materials, then transparent queue.
   6. `SetGlobalFloat("_ToyHeld", 0)`.
6. **`ToyLit` obeys `_ToyHeld`**: received shadow is forced to 1. The squash amount is 0 while held.
7. **On release** the engine returns the prop to layer 8. In the same frame shadows are re-enabled, the
   pool starts to flood (§9.4), and the border and peel shadow are simply gone, covered by the shutter
   flash.

```csharp
// Render/StickerFeature.cs — sketch against the URP 17 RenderGraph API (verify signatures)
public override void RecordRenderGraph(RenderGraph rg, ContextContainer frame)
{
    var res = frame.Get<UniversalResourceData>();
    var rd  = frame.Get<UniversalRenderingData>();
    var cam = frame.Get<UniversalCameraData>();
    var lit = frame.Get<UniversalLightData>();
    using var b = rg.AddRasterRenderPass<PassData>("Sticker", out var d);

    var held  = new FilteringSettings(RenderQueueRange.all, 1 << Layers.Held);
    var tags  = new List<ShaderTagId> { new("UniversalForward"), new("SRPDefaultUnlit") };
    var own   = RenderingUtils.CreateDrawingSettings(tags, rd, cam, lit, SortingCriteria.CommonOpaque);
    var edge  = own; edge.overrideMaterial = stickerMaterial; edge.overrideMaterialPassIndex = 0;
    var peel  = own; peel.overrideMaterial = stickerMaterial; peel.overrideMaterialPassIndex = 1;
    d.border  = rg.CreateRendererList(new RendererListParams(rd.cullResults, edge, held));
    d.peel    = rg.CreateRendererList(new RendererListParams(rd.cullResults, peel, held));
    held.renderQueueRange = RenderQueueRange.opaque;
    d.opaque  = rg.CreateRendererList(new RendererListParams(rd.cullResults, own, held));
    held.renderQueueRange = RenderQueueRange.transparent;
    d.transp  = rg.CreateRendererList(new RendererListParams(rd.cullResults, own, held));
    b.UseRendererList(d.border); b.UseRendererList(d.peel); b.UseRendererList(d.opaque); b.UseRendererList(d.transp);

    b.SetRenderAttachment(res.activeColorTexture, 0);
    b.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.Write);
    b.AllowGlobalStateModification(true);
    b.SetRenderFunc((PassData p, RasterGraphContext ctx) =>
    {
        ctx.cmd.ClearRenderTarget(RTClearFlags.DepthStencil, Color.clear, 1f, 0);
        ctx.cmd.SetGlobalFloat(ToyHeldId, 1f);
        ctx.cmd.DrawRendererList(p.border);
        ctx.cmd.DrawRendererList(p.peel);
        ctx.cmd.DrawRendererList(p.opaque);
        ctx.cmd.DrawRendererList(p.transp);
        ctx.cmd.SetGlobalFloat(ToyHeldId, 0f);
    });
}
```

A glass toy shows the white backing through itself while held and reads as frosted, like a clear sticker
on its sheet. That is intended; it keeps a held marble visible against any background.

### 8.2 Every way the picture could leak distance, and what stops it

| Possible leak | Handling |
|---|---|
| Key light | Directional: direction and colour do not depend on position |
| Kicker, ambient, StudioEnv, glint, rim | Global uniforms and functions of N and V only |
| Received shadow | Forced to 1 by `_ToyHeld` |
| Cast shadow | `shadowCastingMode = Off` while held |
| Colour pool and splash ring | The held prop is not in the pool list; `ToyLit` never reads pools |
| Haze | Toys never take haze, held or not, so nothing pops at grab |
| Window patch | `RoomLit` only |
| Point, spot, area lights; light cookies; light probes; reflection probes | None exist in the game |
| SSAO, depth of field, screen-space reflections | None exist; the depth texture is disabled |
| Lens blur | A function of screen y only, and drawn before the sticker |
| Bloom, tonemap, vignette, dither | Screen-space; depend on colour only |
| Outline width, peel-shadow offset | Constant pixels, extruded in clip space |
| Textures | Object-space UVs: the mip level and anisotropy are constant under the hold transform |
| Mesh LOD | Toys have no `LODGroup` |
| Occlusion by particles, glass, shaft, water | Depth is cleared first and the sticker draws last |
| Z-fighting with the surface it rests on | Depth is cleared first |
| Motion vectors | No TAA and no motion blur anywhere |
| Other objects reflecting or refracting the toy | No planar reflections, no opaque texture |
| Sound | The hold tone is 2D (§11); only its pitch reports size, by design |

The scale readout and the hold pitch do report true size, deliberately. The picture never does.

**Known and accepted.** A dynamic body crossing the view ray is drawn under the held toy. It reads as a
sticker, which is consistent. Lighting snaps from shadowed to unshadowed at the grab frame if the toy was
in shade; the 110 ms border pop covers it.

### 8.3 Acceptance test

In a graphics `Shots` run: freeze the camera, hold a glossy toy, force `d = 3` and then `d = 90`, and
capture both frames with dithering off. Inside the toy's silhouette (including border and peel shadow) the
maximum per-channel difference must be under 1/255. Run it once per material recipe. If far-held concave
toys fail on depth precision, scale the view matrix inside the sticker pass by `4 / d` (the same
homothety, to a fixed distance of 4 units); the image is unchanged and depth precision becomes constant.

---

## 9. Mechanic feedback

Sticker constants at 1080p: border 4.5 px (0.0042 × height), peel-shadow offset (+10, −12) px, peel colour
`lerp(white, Ink, 0.22)` multiplied. Flat and hard means "2D, held". Soft means "3D, real".

### 9.1 Focus (aim ray on a grabbable)

- One glint sweep: a 45° band crosses the toy in 300 ms (`_Sweep` via `MaterialPropertyBlock`).
- Its colour pool brightens 20%.
- The four reticle panes spread from 8 px to 14 px and rotate 45° (120 ms).
- A 25 ms tick (§11).

### 9.2 Grab

| Time | Event |
|---|---|
| 0 ms | Prop moves to `Held`; cast shadow off; its pool freezes in place and drains over 120 ms. In Level 3 this is the tell: the apple's huge shadow leaves the wall |
| 0–110 ms | Border width 0 → 6.5 → 4.5 px with back-out easing. The peel shadow slides out from under the toy to its offset |
| 0–180 ms | Reticle hides; the scale pill sticks on |

There is no scale punch and no squash on grab. The footprint is sacred.

### 9.3 Hold

- The image of the toy is completely stable. The peel-shadow offset breathes ±1 px at 0.5 Hz.
- When the projected scale changes by more than 5% in one tick (the toy jumped to another surface), the
  border flashes +1.5 px for 80 ms and the readout ticks. That says a jump happened without drawing where.
- No landing preview, no ghost at the destination, no projected shadow preview. They would all show depth.

### 9.4 Release: how true scale is revealed

| Time | Event |
|---|---|
| 0 ms | Prop returns to `Prop`; **cast shadow on this frame**; border and peel shadow gone; post exposure +0.12 EV decaying to 0 over 90 ms (the shutter) |
| 0–380 ms | **Pool flood**: pool strength 0 → 1.25 at 140 ms → 1.0. The pool radius is the toy's true radius, so a ×10 toy floods a pool ten times wider |
| 0–350 ms | **Splash ring**: a ring of the toy's colour expands on every room surface from 1× to 3× the toy's bounding radius; ring width 0.25× radius; strength 0.6 → 0. It crosses rug dots and floorboards at true size |
| 0–1500 ms | If the scale changed by more than 15%: the **dimension callout** |

**Colour pools** (the scale-truthful contact cue; replaces SSAO)

- `_PoolPos[k]` = the prop's world centre (or a proxy sphere centre) and
  `max(0.8 × bounding radius × scale, 0.08)`.
- `_PoolTint[k]` = the toy's linear base colour (glass × 1.6) and a strength.
- Only grabbable toys that are not held have pools. Each frame, pick the top N by `strength · r² / dist²`
  to the camera; fade strength over 200 ms when a prop enters or leaves the set.
- Pools appear on floors, walls and shelves with no decals and no raycasts. Stacked toys get no pool on
  each other; the shadow map and rim carry that case.
- Long or ring-shaped toys (aspect above 3) use up to three proxy spheres supplied by the toy catalog.

**Dimension callout** (catalogue drawing, world space, `Flat` in Ink, depth-tested)

- A vertical dimension line at the toy's true height, beside it, yaw-facing the camera, with end ticks.
- A "×N" label (TextMeshPro, Unbounded) at mid height.
- A flat 1.7-unit figure silhouette standing at the toy's base: "figure for scale".
- In 120 ms, hold, out over 300 ms.

### 9.5 First impact

- **Dust ring**: a `ParticleSystem` burst of 8 Paper discs at constant world size (0.15–0.3 units) on a
  ring whose radius is the toy's true footprint. They look tiny beside a giant, which is the point.
- **Squash** (`_SquashA`), recovering over 180 ms with one overshoot: sponge 18%, rubber 14%, plastic 6%,
  everything else 0.
- **Camera shake**: amplitude `clamp(log10(mass) × 0.05, 0, 0.25)` units for 180 ms; none below mass 1.
- A second, weaker splash ring (strength 0.3).

### 9.6 Scale readout (HUD, only while holding, plus 1.2 s after release)

A sticker pill centred at 72% of screen height.

- **Factor**: "×3.2" = current projected scale ÷ scale at grab. Unbounded 22 px, tabular spacing. Ink
  within ±5% of ×1, Lagoon below, Cherry above.
- **Log ruler** under it: ×1/8 to ×8 at 40 px per octave (240 px), ticks at ¼, ½, 1, 2, 4. Solid marker =
  now (60 ms tween); hollow marker = at grab.
- **Figure for scale** beside it: a 14 px figure next to a bar of height `14 × trueHeight / 1.7` px, with a
  notch at the jump apex (1.3 units). Past 56 px the bar pins and the figure shrinks instead (minimum
  3 px). This is absolute size; the factor is relative.

### 9.7 Level complete (about 2.6 s)

| Time | Event |
|---|---|
| 0 ms | Shutter sound; white flash (+1.5 EV for one frame, 300 ms fade) |
| 0–240 ms | The camera's output is redirected to a `RenderTexture` shown on a Paper card: it shrinks to 86% and tilts 2°, with a hang-tab hole. The scene stays live inside the card |
| 100 ms | Confetti: a `ParticleSystem` built in code. Non-instanced mesh quads, `Flat` shape 4 (gloss disc), colours from the level's toy candies, burst count by tier, gravity modifier 0.3, 3D rotation over lifetime, lifetime 2.2 s |
| 200 ms | Every toy in view runs one glint sweep in sync |
| 400 ms | A "COLLECTED" sticker stamps on (scale 1.4 → 1 in 90 ms) with a thump |
| 600 ms | The card shows title, time and grab count, and a Cherry "Next" pill |

Setting "Reduce motion" disables the exposure flashes, camera shake and the card tilt.


---

## 10. UI style guide

Everything is UGUI + TextMeshPro, created from code. **The UI is stickers**: the same die-cut border and
hard offset shadow the held toy wears.

### 10.1 Fonts

| Role | Typeface | Licence | File |
|---|---|---|---|
| Display: numbers, titles, the readout, button labels of 12 characters or fewer | **Unbounded Bold** | SIL OFL 1.1 | `Assets/Toybox/Fonts/Unbounded-Bold.ttf` + `OFL-Unbounded.txt` |
| Body: hints, blurbs, settings | **Figtree SemiBold** | SIL OFL 1.1 | `Assets/Toybox/Fonts/Figtree-SemiBold.ttf` + `OFL-Figtree.txt` |

Fonts are the only shipped assets that are not generated, and both are open-licensed. Two files, no more.

- `ProjectSetup` bakes **static** TMP font assets into `Assets/Toybox/Resources/Fonts/`:
  `TMP_FontAsset.CreateFontAsset(font, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024)`, `TryAddCharacters`
  with ASCII 32–126 plus `× ÷ · — – ‘ ’ “ ” … ° ±`, then switch the asset to static and save it with its
  atlas and material as sub-assets. Body uses sampling size 48. No dynamic glyph rasterisation at runtime.
- **Material presets as assets** (TMP outline and underlay are `shader_feature` keywords and are stripped
  otherwise): `Display-Sticker.mat` (outline Paper, thickness 0.2; underlay Ink at 18%, offset
  (0.5, −0.7), softness 0), `Display-Plain.mat`, `Body-Plain.mat`.
- **Fallback.** If the TTF files are not in the repository, `UiFonts` returns
  `TMP_Settings.defaultFontAsset` (LiberationSans SDF from the TMP essentials). Every label uses auto-size
  with a 60% floor, so layout does not depend on Unbounded's width.

### 10.2 Sizes and colours

`CanvasScaler`: Scale With Screen Size, reference 1920×1080, match height. Sizes in reference pixels.

| Role | Font | Size |
|---|---|---|
| Logo | Unbounded | 120 |
| Level number | Unbounded | 96 |
| Card title | Unbounded | 40 |
| Button label | Unbounded | 24 |
| Scale readout | Unbounded | 22 |
| Body, hints | Figtree | 20 |
| Key caps, small HUD | Figtree | 16 |

| Use | Colour |
|---|---|
| Surfaces, die-cut border | Paper `#FFFDF7` |
| Text, icons, HUD pills | Ink `#2B2140` (text on Ink pills is Paper) |
| Peel shadow | Ink at 18%, no blur |
| Primary action (one per screen) | Cherry `#FF2E55` with Paper text |
| Readout: smaller / larger | Lagoon `#18A8FF` / Cherry |
| Level card backing | That level's dip `mid` |
| Locked card | Kraft `#C99A62` |

### 10.3 The sticker component

Built from procedural sprites (one 256×256 RGBA32 atlas generated at startup, so all images batch):
rounded rectangle (radius 18, 9-slice border 24), pill, circle, four-pane mark, figure glyph, hang-tab slot.
Edges are anti-aliased from a signed distance function when the atlas is drawn.

A **Sticker** is three `Image`s:

1. Peel shadow: the shape in Ink at 18%, offset (4, −6), hard.
2. Die-cut border: the shape in Paper, 5 px larger on every side.
3. Face: Paper for panels, Ink for HUD pills, Cherry for the primary button, a dip tone for level cards.

Pills are fully rounded. Panels are blister cards with a hang-tab slot at the top.

### 10.4 Motion

Durations are 90, 180 and 320 ms. A small `UiTween` component drives everything on unscaled time.

| Motion | Recipe |
|---|---|
| Enter ("stick") | Scale 0.9 → 1 and rotate −3° → 0, back-out easing with overshoot 1.56, 180 ms |
| Exit ("peel") | Scale y 1 → 0 about the top edge with alpha 1 → 0, ease-in, 180 ms |
| Press | Translate (2, −3) while the peel shadow shrinks to (2, −3), 90 ms |
| Hover | Lift (−1, 2), shadow grows to (5, −8), 90 ms |

Text never animates separately from its sticker. "Reduce motion" replaces stick and peel with 90 ms fades.

### 10.5 Components

**HUD** (one overlay canvas, no raycaster)

- **Reticle**: the four-pane mark, four 3 px rounded squares in Paper with a 1 px Ink edge. Idle: 8 px
  across. On a grabbable: 14 px and rotated 45°. Hidden while holding.
- **Scale pill**: §9.6.
- **Hint toast**: bottom-left sticker, Figtree 20, driven by `Message` events; sticks on, peels after 5 s.
- **Level card on load**: "03 — THE CHEESE WEDGE" in Unbounded 40 with the number at 96 and the blurb in
  Figtree; peels away after 3.5 s.
- **Held-control pills**: bottom-right key caps ("Q / wheel: turn", "F: flip", "click: drop"); they stop
  appearing after three uses.

**Pause** (menu canvas)

1. MacroBand goes to a full-screen 14 px blur over 180 ms (Low: skip).
2. The frame is captured to a `RenderTexture` shown as a card at 86%, tilted −2°, on a Paper overlay at
   35%; then the world camera is disabled. GPU cost while paused is the UI only.
3. A hang-tab card lists Resume, Restart, Hints, Settings, Level Select.

Settings: quality (Auto / Low / Medium / High), lens blur strength, sensitivity, field of view, volume,
reduce motion, high-visibility toys.

**Level select, "The Catalogue"**: a 5×3 grid of blister cards.

- Backing in that level's dip `mid`; the number in Unbounded 96; a Paper circle holding a figure-glyph-style
  silhouette of the level's hero toy in the preset's hero candy.
- Locked: Kraft card with "?". Completed: a "COLLECTED" sticker rotated ±6° (seeded) and the best time.
- Hover: the card lifts and one four-pane glint slides across the circle.

**Title**

- A live turntable of one giant glossy toy on the `sunny-rug` preset, seen from figure eye height.
- "TINKER'S TOYBOX" in Unbounded 120 with the `Display-Sticker` material; the "O" of TOYBOX is the
  four-pane mark.
- A pulsing "Click to play" pill. The click also unlocks audio in the browser.
- On start the logo peels off and the camera drops into first person.

**Loading screen** (WebGL template): Paper background, the four-pane mark as a spinner in Ink, a Cherry
progress bar. No other imagery.

---

## 11. Audio direction

### 11.1 Constraints and architecture

WebGL has no audio thread, no `OnAudioFilterRead`, no mixer effects, and the browser only starts audio after
a user gesture. So every sound is **synthesised into a float array at startup** and wrapped with
`AudioClip.Create(name, lengthSamples, 1, sampleRate, false)` + `SetData`. Pitch and loudness are the only
things changed at play time (`AudioSource.pitch`, `volume`).

- `Audio/Synth`: oscillators (sine, triangle, saw, square by phase accumulation), seeded white noise, FM
  operator, exponential envelopes, one-pole and biquad filters, pitch glide, the reverb below, peak
  normalise. Pure functions on `float[]`.
- Sample rates: 44,100 Hz for effects and the music box; 22,050 Hz for pads and bass.
- Generation is time-sliced behind the title screen, 4 ms per frame, in this order: UI, grab, hold,
  release, land, button, level complete, music bank. Budget: under 400 ms of synthesis and under 12 MB of
  samples in total.
- Sources: 12 pooled for effects, 12 for music, 1 for the hold loop.
- **Reverb is baked into each clip** with a Schroeder reverb (linear time, unlike convolution): four
  parallel combs (29.7, 37.1, 41.1, 43.7 ms; feedback for a 1.4 s RT60; a 4 kHz one-pole low-pass in the
  feedback) into two all-passes (5.0 and 1.7 ms, gain 0.7). Wet level 0.18 for effects, 0.30 for music,
  0.6 for the release sub. Tails are cut at −50 dB. A long tail in a big room is what sells being small.
- **No compressor exists, so gain staging replaces it**: effect peaks −9 dBFS, music voices −18 dBFS, the
  hold loop −29 dBFS, `AudioListener.volume = 0.8`.

### 11.2 Size-to-pitch law

`S` is the toy's **true** bounding diameter in units (absolute, not relative to the grab).

`semis = clamp(−6 · log2(S), −24, +24)`, snapped to the preset's pentatonic scale;
`AudioSource.pitch = 2^(semis / 12)`.

Big is low. Four times the size is an octave down. Lower pitch also plays the clip slower, so bigger toys
ring longer for free.

### 11.3 Effects

| Sound | Recipe (what is written into the clip) | Playback |
|---|---|---|
| **Focus tick** | Sine 1.8 kHz, 25 ms, decay τ 6 ms, −26 dBFS | 2D |
| **Grab** | *Peel*: noise through a band-pass (Q 1.2) swept 800 → 2,400 Hz over 60 ms. *Pluck*: sine at 523 Hz with a +3 semitone glide over the first 50 ms, decay τ 40 ms, 120 ms long | 2D, pitch from §11.2 |
| **Hold** | A 2.000 s seamless loop: triangles at 220.5 Hz and 219.5 Hz (441 and 439 whole cycles, so it loops without a click and beats at 1 Hz) through a 900 Hz low-pass | 2D, looped. Pitch from §11.2 with 60 ms portamento (lerp `pitch` per frame). **Never spatialised** |
| **Hold jump tick** | Sine 3 kHz, 2 ms | 2D, on every scale jump over 5% |
| **Release** | *Thock*: sine from 196 Hz falling 5 semitones over 80 ms, decay τ 30 ms, plus a 15 ms click of noise high-passed at 2 kHz | 2D, pitch from §11.2 |
| | *Sub* (when the hold grew the toy more than ×2): 55 Hz sine, 250 ms, wet 0.6 | 2D, unpitched |
| | *Bell* (when it shrank below ×0.5): FM, carrier 2,093 Hz, ratio 1:3.5, index 3 → 0 in 60 ms, 200 ms | 2D, unpitched |
| **Land** | Modal: a sum of damped sines from the table below, one clip per material | 3D (spatial blend 1, linear roll-off 4–160). `pitch = clamp(S^−0.7, 0.25, 4)`, `volume = clamp(speed / 12, 0, 1)` |
| **Player land** | Two 5 ms clicks band-passed at 1.2 kHz, plus a 90 Hz sine thump of 60 ms | 2D |
| **Button** | A 2 ms square click, then FM marimba notes (modulator at 4× carrier, index 2 → 0 in 40 ms, decay τ 60 ms): E5 then A5, 70 ms apart, on press; A5 then E5 on release | 3D |
| **UI hover / click** | Sine 2 kHz for 8 ms / one marimba C6, 60 ms | 2D |
| **Level complete** | *Shutter*: two 12 ms noise bursts high-passed at 3 kHz, 40 ms apart. *Arpeggio*: marimba on scale degrees 1-3-5-6-8 at sixteenth notes. *Swell*: an add9 pad chord, 1.2 s. *Stamp* at 400 ms: 90 Hz thump plus 20 ms of noise | 2D, in the preset's key |

**Land materials** (base frequency; mode ratios; decay times in ms)

| Material | Base Hz | Ratios | Decays | Extra |
|---|---|---|---|---|
| Plastic | 420 | 1, 2.3, 3.9 | 70, 45, 30 | |
| Wood | 300 | 1, 2.76, 5.4 | 110, 60, 35 | |
| Rubber | 140 | 1, 1.5 | 90, 60 | pitch starts 2 semitones high and falls |
| Metal | 900 | 1, 2.76, 5.40, 8.93 | 900, 600, 400, 250 | |
| Glass | 1,600 | 1, 2.32, 4.25 | 500, 300, 200 | |
| Felt, sponge | — | noise, low-pass 500 Hz | 60 | −10 dB |
| Cardboard | 180 thump | noise, band-pass 700 Hz, Q 1.5 | 50 | |
| Feather | — | noise, high-pass 5 kHz | 30 | −26 dB |

Landing needs an impact event from the engine (§13). Until it exists, audio derives one from a per-frame
velocity change above 2.5 units/s.

### 11.4 Generative music: a toy music box

**Bank** (synthesised once, in C)

| Voice | Synthesis | Clips |
|---|---|---|
| Music box | FM bell: ratio 1:4, index 2 → 0 over 80 ms, decay τ 300 ms, 1.2 s | 9: every minor third from C4 to C6. Other notes are reached with `pitch` ±1 semitone |
| Pad | Three saws detuned ±7 cents through a 600 Hz low-pass, slow attack 400 ms | 4 chords × 2 bars, rendered per preset at level load, 22,050 Hz |
| Bass | Sine with a 10 ms attack | 4 roots × 1 bar, 22,050 Hz |
| Toy kit | Woodblock (sine 1,200 Hz, 20 ms), shaker (noise high-passed 5 kHz, 40 ms), soft kick (sine 110 → 50 Hz, 90 ms) | 3 |

**Clock.** `Audio/MusicBox` (a MonoBehaviour) keeps `nextStep` in `AudioSettings.dspTime`. Each `Update`:
while `nextStep < dspTime + 0.2`, pick the notes for that sixteenth-note step and start them with
`AudioSource.PlayScheduled(nextStep)` on a round-robin pool. A 200 ms look-ahead survives frame hitches.

**Composition**

- Harmony: I–vi–IV–V as add9 chords, two bars each.
- Lead: a pentatonic random walk as a two-bar motif in A A′ B A′ form. Step weights for 0 / ±1 / ±2 / leap
  are 0.2 / 0.5 / 0.25 / 0.05; an eighth note sounds with probability 0.45. Two steps mutate every 8 bars.
- Its PRNG is seeded by the level id. It is never `game.Rng` and never `UnityEngine.Random`.
- Key and tempo come from the preset (§6.4). Key is a transposition: every music source plays at
  `2^(k / 12)`. Mode is the note set the scheduler picks from.
- At most 12 voices sound at once.

**Adaptive rules**

1. The toy kit enters after the first grab of the level.
2. While holding, the music box rests and the hold tone is the lead, snapped to the current chord's
   pentatonic. Sweeping the view across depth edges plays the tune.
3. The release thock is tuned to the current chord root.
4. Night is slower (76 BPM) and drops the kit to shaker only.

**Fallback.** If `PlayScheduled` timing proves unreliable in a browser build, pre-render each layer as a
four-bar stem per preset and loop the stems, started together with one `PlayScheduled` call each.
Adaptivity then becomes stem volume.


---

## 12. Performance budget

Target: **60 fps (16.6 ms) at 1080p on an integrated laptop GPU at Medium**, with Low as the floor for
older integrated graphics. Reference machines: Iris Xe class for Medium, UHD 620 class for Low.

### 12.1 Hard budgets

| Budget | Low | Medium | High |
|---|---|---|---|
| Internal resolution | up to 1536×864 | up to 1920×1080 | up to 2560×1440 |
| GPU frame time on the reference | 12 ms (UHD 620 class) | 10 ms (Iris Xe class) | 12 ms (discrete) |
| CPU main thread (WebAssembly, single thread) | 8 ms: simulation tick 2, render submit 4, UI and audio 1, slack 1 | same | same |
| Draw calls, main pass | 130 | 160 | 180 |
| Draw calls, shadow pass (all cascades) | 120 | 140 | 210 |
| Draw calls, whole frame (with sticker, post, UI) | 300 | 350 | 450 |
| Triangles in view | 150k | 200k | 250k |
| Shadow atlas | 2048×1024, 16-bit | 4096×2048, 16-bit | 4096×4096, 16-bit |
| Full-resolution render targets | 1 colour + depth | 2 colour (MSAA 2× and its resolved copy for MacroBand) + depth | 2 colour (MSAA 4×) + depth |
| Other render targets | colour LUT 1024×32 | LUT, bloom pyramid (4 levels) | LUT, bloom pyramid (5 levels) |
| Full-screen passes at full resolution | 2 (uber, final) | 3 (MacroBand copy, MacroBand, uber); 4 if render scale is below 1 | 3 |
| Render-target memory | about 20 MB | about 75 MB | about 180 MB |
| Colour pool loop | 6 spheres | 10 | 16 |
| Particles alive | 60 | 150 confetti + 120 motes | 300 + 300 |

Draw calls are the scarce resource on WebGL: the SRP Batcher is not available on GLES 3.0 as far as is
known *(verify in the Frame Debugger of a WebGL build)*, and every draw crosses from WebAssembly into the
browser. Hence the per-object limits:

| Thing | Limit |
|---|---|
| A toy | 3 draws, 3,000 triangles (merge sub-parts by material) |
| Level statics | Merged per material at load: at most 12 draws |
| Shell / furniture / window and sky / hull shadows | 5 / 2 / 3 / 1 draws |
| Materials alive per level | 40 |
| GPU instancing | Not used. Repeated things (books, fairy bulbs) are merged meshes |

### 12.2 Other budgets

| Resource | Budget |
|---|---|
| Compiled shader programs | 40, warmed up behind the loading screen |
| Procedural detail textures | 9 at 256² RGBA32 with mips: under 3 MB |
| UI | One 256² sprite atlas, two 1024² font atlases; 15 draws |
| Audio samples | Under 12 MB; under 400 ms of synthesis, time-sliced |
| Startup art generation (textures, meshes, sprites) | Under 150 ms on the main thread, never more than 4 ms in one frame after the first |
| Per-frame managed allocation in `Render`, `UI`, `Audio` | Zero: cache property ids, the pool arrays and the `MaterialPropertyBlock` |
| Meshes | `UploadMeshData(true)` after building unless a `MeshCollider` needs the data |

### 12.3 Expected cost by stage

Estimates from shader operation counts, not measurements. Iris Xe class, 1080p.

| Stage (ms) | Low | Medium | High |
|---|---|---|---|
| Shadow atlas | 0.6 | 1.4 | 2.0 |
| Opaques and transparents (`RoomLit` with pools is the heaviest shader) | 2.6 | 4.6 | 6.3 |
| MacroBand | — | 0.6 | 0.9 |
| Sticker pass (while held) | 0.2 | 0.3 | 0.4 |
| Bloom | — | 0.7 | 0.9 |
| Uber and final post | 0.8 | 0.6 | 0.6 |
| **Total** | **about 4.2** | **about 8.2** | **about 11** |

A UHD 620 is roughly 2.5–3× slower: Low lands near 11–13 ms at 1536×864. The worst case is a giant coated
toy filling the screen (about +2 ms on Medium); the governor's render-scale step covers it. The first
numbers to measure in a real build are the `RoomLit` pool loop at full screen and the 4096×2048 shadow
atlas on integrated graphics.

---

## 13. Hand-offs

This document is design only; nothing under `Assets/`, `Packages/`, `ProjectSettings/` or `tools/` was
touched. These are the changes it asks of the engine team.

**`ProjectSetup` changes** (today it creates one URP asset with MSAA 4×, shadow distance 80 and the depth
texture on)

1. Replace `ToyboxURP.asset` with `ToyboxURP_Low/_Medium/_High.asset` using §7.3 and §5.3; three Quality
   levels, all enabled for WebGL, default Medium.
2. `ToyboxRenderer.asset`: Forward, depth priming off, layer masks without `Held`, stencil in the depth
   attachment, the MacroBand full-screen feature, then `StickerFeature`.
3. Create the five template materials under `Resources/Materials/`; repoint `ToyLit.mat` to `Toybox/ToyLit`.
4. Create `Resources/Volumes/ToyboxPost.asset` (§7.5) and `Resources/ToyboxVariants.shadervariants` (§3.8).
5. Import the TMP Essential Resources; bake the two font assets and three material presets (§10.1).
6. URP Global Settings: "Strip Unused Post Processing Variants" off.

**Asks of the simulation**

1. A `PropImpact { prop, speed, mass, point }` event. `PropDropped` alone cannot drive landing sound, dust
   or squash.
2. An accessor for the grabber's current aim candidate, for the focus cue.
3. `PropGrabbed`, `PropHeld` and `PropDropped` payloads carrying grab distance, grab scale and current
   scale.
4. From the toy catalog, per toy: material recipe, candy colour, bounding radius, optional pool proxy
   spheres, bevels, outline normals, at most three draws.
5. **Environment colliders in the simulation.** `ARCHITECTURE.md` lists environment presets under `Render`,
   but the shell, window pane and furniture colliders decide where a held toy stops, so headless bot tests
   need them. The preset descriptor and its colliders should be built by `Game`; `Render` builds visuals
   from the same descriptor.
6. A rule for level authors: anything opaque that could stand between the eye and a held toy has a
   collider the hold march respects. Water, lasers and particles are exempt and draw under the sticker.
7. The WebGL template: Paper background, four-pane spinner, Cherry bar (§10.5).

<!-- ART_BIBLE complete: sections 1-13 written -->

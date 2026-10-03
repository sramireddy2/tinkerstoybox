# DIP & DIE-CUT: visual identity pitch for Tinker's Toybox (Direction A, sharpened)

Checked against `C:/Users/shank/OneDrive/Desktop/tinkerstoybox/docs/ARCHITECTURE.md` and the installed three 0.186.1. Three facts from that version shape the pitch:
- `PCFSoftShadowMap` is removed; `PCFShadowMap` is now a 5-tap Vogel disc with hardware PCF.
- `three/addons/lights/SunLight.js` gives native 2-cascade shadows in `WebGLRenderer`, so `CSM.js` and its material patching are not needed.
- `object.receiveShadow` is a uniform, so toggling it on grab does not recompile a program.

Nothing here has been built or measured. Light intensities and frame times are starting values and estimates.

## 1. Identity

Every room is dipped in one matte pastel hue, floor to ceiling. The only saturated things in it are the toys, which are lit like catalogue products. While you hold a toy it is a die-cut sticker on your lens, with a flat white border and a hard offset shadow; when you let go it becomes matter, with a real shadow and a pool of its own color on the floor.

**Pillars**
1. **Dipped rooms.** One hue per level in three matte values, printed with patterns at fixed world pitch. The room is backdrop and ruler.
2. **Product-lit toys.** Candy color, a four-pane window glint, a white kicker rim and a color pool. Only grabbables get these.
3. **Held is sticker, released is matter.** The held toy is drawn in its own pass with screen-space-only decoration. Release is the moment the shadow and pool appear at true size.

**Brand mark:** the four-pane window (2×2 rounded squares). It is the glint on toys, the light patch on the floor, the reticle, the loading spinner and the "O" in the logo.

## 2. Palette

**Neutrals**

| Name | Hex | Use |
|---|---|---|
| Paper | `#FFFDF7` | UI surfaces, sticker border, skirting and window frames, toy secondary parts |
| Ink | `#2B2140` | UI text, gadget bodies, vignette tint; darkest value allowed, no pure black |
| Kraft | `#C99A62` | Cardboard toys only |
| Birch | `#E9C9A0` | Raw wood at worn edges |
| Steel | `#C9CED8` | Gadget metal parts |

**Candy (grabbable toys only, never room, UI chrome or gadgets)**

| Name | Hex |
|---|---|
| Cherry | `#FF2E55` |
| Tangerine | `#FF7A1A` |
| Lemon | `#FFCE1F` |
| Lime | `#7FDB2E` |
| Lagoon | `#18A8FF` |
| Grape | `#8A4BFF` |
| Bubblegum | `#FF5FB0` |

**Signal (gadgets only, always emissive on an Ink body)**

| Name | Hex | Use |
|---|---|---|
| Amber | `#FFB627` | Idle; pulses 1 Hz, emissiveIntensity 1.2↔2.2 |
| Go | `#2BE8A6` | Satisfied; steady 2.6 |
| Hazard | `#FF2BD6` | Lasers and kill surfaces; 3.0 |
| Exit | `#FFFFFF` | Exit portal, drawn as the four-pane mark; 3.0 |

Idle and satisfied also differ by motion (pulse versus steady), so the state does not depend on hue.

**Dips (room)**

| Preset | light (walls, walkable tops) | mid (floor, furniture) | deep (sides, recesses, hemisphere ground) | haze | Banned candy | Hero candy |
|---|---|---|---|---|---|---|
| Mint | `#DDF5EA` | `#B4E6D2` | `#7CCDB3` | `#EAF8F1` | Lime | Cherry |
| Butter | `#FFF4CC` | `#FFE699` | `#F2CC5C` | `#FFF9E3` | Lemon | Grape |
| Lilac | `#E9E2FA` | `#CFC2F2` | `#A996E0` | `#F1ECFC` | Grape | Lemon |
| Pool | `#DCEEFB` | `#B5D9F5` | `#7FB8E8` | `#EAF5FD` | Lagoon | Tangerine |
| Peach | `#FFE6D6` | `#FFCDB2` | `#F2A88A` | `#FFF1E8` | Tangerine | Lagoon |
| Plum (night) | `#5A4A82` | `#453769` | `#2F2550` | `#3A2E5C` | none | Lime |

**Usage rules**
- **Room and level statics:** dip tones only, matte, patterned, hazed.
- **Top-light rule:** on every level static, faces with world normal.y > 0.7 are `light` and everything else is `deep`. Platforms then read against the `mid` floor without outlines. Do it in the shader: `mix(deep, light, smoothstep(0.5, 0.8, Nw.y))`.
- **Toys:** one candy color covers at least 60% of the surface; Paper and Ink are allowed for details; never the banned candy for that preset.
- **Gadgets:** Ink satin body, one Signal emissive element, Steel for moving metal.
- **UI:** Ink on Paper with one Cherry accent.

**Making grabbables unmistakable without boxes** (four passive cues, one on focus):
1. Candy chroma exists nowhere else.
2. A white kicker rim (Fresnel) on every grabbable regardless of material. This is a luminance cue, so it survives color blindness.
3. Toys never take haze, so they stay saturated at 150 units while the room fades.
4. Each toy sits in a pool of its own color.
5. When the aim ray is on a grabbable, one diagonal glint sweep crosses it (300 ms, screen-space band), its pool brightens 20%, and the reticle panes spread.

## 3. Materials

**Architecture.** `src/art` returns plain three materials with `userData.recipe` and `userData.candy`, so the sim runs in Node. On `level:loaded` the render layer upgrades them: it attaches `onBeforeCompile` patches and canvas textures, cached per recipe.

**Rules**
- All toy geometry has bevels of at least 4% of its smallest dimension, with smooth normals.
- Toy textures are UV or object-space only. World-space or triplanar textures on a toy would change with projected position and betray distance.
- Room patterns are world-space and analytic, with no textures.
- Canvas textures are 256², `RepeatWrapping`, with sRGB color space for `map`.

**ToyLit patch** (all toy recipes; injected before `#include <opaque_fragment>`):

```glsl
vec3 Vv = normalize(vViewPosition);
vec3 Nw = transformDirectionByInverseViewMatrix(normal, viewMatrix);   // r186 name
vec3 Rw = transformDirectionByInverseViewMatrix(reflect(-Vv, normal), viewMatrix);
float fr = pow(1.0 - saturate(dot(normal, Vv)), uRimPow);
// four-pane window, analytic: 33 deg wide, 4.6 deg mullion
float z  = dot(Rw, uWinDir);
vec2  p  = vec2(dot(Rw, uWinRight) / uGlintStretch, dot(Rw, uWinUp)) / max(z, 1e-3);
vec2  q  = abs(abs(p) - 0.17) - 0.105;
float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.025;
float w  = fwidth(sd) + uGlintSoft;
float pane = step(0.0, z) * smoothstep(w, -w, sd);
outgoingLight += uGlintColor * (1.8 * uGlint * (0.6 + 0.4 * fr) * pane);
outgoingLight += uRimColor * (uRim * fr * (0.35 + 0.65 * saturate(Nw.y * 0.5 + 0.5)));
outgoingLight += vec3(0.5) * uSweepGain *
  smoothstep(uSweep.y, 0.0, abs(dot(gl_FragCoord.xy - uSweepCentre, vec2(0.7071)) - uSweep.x));
```

`uWinDir` is the sun direction; `uGlintColor` is `#FFF6E8` by day and `#BFD0FF` at night; `uRimColor` is Paper. The glint is analytic rather than read from the env map. That keeps it crisp at any resolution and lets it be bright enough to bloom without over-lighting the diffuse.

**Recipes**

| Material | Class and parameters | Glint / soft / stretch | Rim / pow |
|---|---|---|---|
| Glossy plastic | `MeshPhysicalMaterial` color candy, roughness 0.38, metalness 0, clearcoat 1.0, clearcoatRoughness 0.06, emissive = color, emissiveIntensity 0.06 | 1.0 / 0.02 / 1 | 0.55 / 3 |
| Painted wood | Physical, roughness 0.55, clearcoat 0.35, clearcoatRoughness 0.35, `vertexColors` | 0.35 / 0.25 / 1 | 0.40 / 3 |
| Rubber | Physical, color candy×0.92, roughness 0.82, sheen 0.6, sheenRoughness 0.5, sheenColor mix(color, white, 0.6) | 0.12 / 0.5 / 1 | 0.65 / 2.5 |
| Brushed metal (anodised) | Physical, color candy, metalness 1, roughness 0.34, anisotropy 0.8, anisotropyRotation 0, clearcoat 0.3, clearcoatRoughness 0.2; needs UVs | 0.8 / 0.15 / 3 | 0.30 / 3 |
| Glass | Physical, color mix(white, candy, 0.35), roughness 0.04, transparent, opacity 0.3, depthWrite false, envMapIntensity 1.6; no `transmission` | 1.4 / 0.01 / 1 | 0.8 (candy tint) / 2.5 |
| Felt / fabric | Physical, color mix(candy, grey, 0.1), roughness 1.0, sheen 1.0, sheenRoughness 0.85, sheenColor mix(color, white, 0.35) | 0 | 0.9 / 2 |
| Cardboard / paper | `MeshStandardMaterial` Kraft or Paper, roughness 0.92; paper is DoubleSide with emissiveIntensity 0.05 | 0 | 0.35 / 3 |
| Sponge | Standard, color mix(candy, white, 0.15), roughness 1.0 | 0 | 0.5 / 2.5 |
| Feather | Physical, roughness 0.6, sheen 1.0, sheenRoughness 0.4, sheenColor white, DoubleSide, alphaTest 0.5; High adds iridescence 0.35, iridescenceIOR 1.3 | 0.25 / 0.2 / 4 | 0.7 / 2 |
| Room (DipMatte) | Standard, roughness 0.92, metalness 0, envMapIntensity 0.6, plus the RoomLit patch (section 4) | none | none |
| Backdrop furniture | `MeshLambertMaterial`, dip tones, fog on, no patch | none | none |
| Gadget body | Standard `#2B2140`, roughness 0.5, metalness 0.1 | none | none |

**Per-material notes and optional textures**
- **Plastic:** High tier adds a `clearcoatNormalMap` from 128² value noise at scale 0.03 (orange peel).
- **Painted wood:** canvas of 40 horizontal strokes at ±4% lightness for `map` and `bumpMap` (0.6). The toy builder tints bevel-ring vertices 30% toward Birch with seeded noise, giving chipped edges.
- **Rubber:** optional stipple normal from 128² random dots, normalScale 0.15.
- **Metal:** canvas with per-row random lightness ±6%, blurred 24 px along x, used as `roughnessMap`.
- **Glass:**
  - Alpha patch: `diffuseColor.a = mix(0.22, 0.85, pow(1.0 - NdotV, 2.5))`.
  - A second BackSide shell at renderOrder −1 and opacity 0.18.
  - `customDepthMaterial` with a 50% checker discard, for a half-density shadow.
  - Pool gain ×1.6, which reads as a caustic.
- **Felt:** 3000 strokes of 6–14 px at random angles, alpha 0.08, for `map` and `bumpMap` (1.0).
- **Cardboard:**
  - 4000 one-pixel speckles at ±6%.
  - Edge faces use a 6 px sine flute stripe.
  - Each cardboard toy carries one candy element (a tape strip in the plastic recipe at roughness 0.3) covering at least 40% of the surface.
- **Sponge:**
  - 220 ellipses of 2–9 px at color×0.55, alpha 0.35, plus 600 micro-dots, for `map`; inverted for `bumpMap` (−1.2).
  - The builder displaces a subdivided rounded box by 1.5% seeded noise.
- **Feather:**
  - Geometry is a `ShapeGeometry` leaf with three notches, bent 8% along its length, plus a tapered Paper quill.
  - A 256×64 canvas of 90 barb lines at ±35° serves as `alphaMap` and `bumpMap`.
  - Without textures it is a solid leaf, which still reads.

**Low tier:** every Physical recipe becomes `MeshStandardMaterial` with the same color and roughness = max(0.2, roughness×0.6) where clearcoat was above 0.5. ToyLit is unchanged, so the glint and rim survive.

## 4. Lighting rig

No point, spot or rect-area lights anywhere. Gadget glow is emissive plus bloom. This is partly cost and mostly section 6: toys may only be lit by position-independent sources.

| Light | Type | Color | Intensity | Notes |
|---|---|---|---|---|
| Key | `SunLight` (addon) | `#FFF1DC` | 2.0 | `position.set(0.45, 0.82, 0.36)` (about 55° elevation); `castShadow` |
| Kicker | `DirectionalLight` | preset `light` tone | 0.45 | from `(-0.6, 0.35, -0.72)`; no shadow |
| Fill | `HemisphereLight` | sky `#FFFFFF`, ground = preset `deep` | 0.8 | shadows come out as a deeper tint of the room hue, never grey |
| IBL | `scene.environment` | procedural StudioEnv | `environmentIntensity` 0.55 | PMREM, see below |

The target lit-to-shadow ratio on the floor is about 1.8:1, which is airy. Night preset: key `#BFD0FF` at 1.1, hemisphere at 0.35 with sky `#8C7FD0`, env at 0.3, toy emissiveIntensity 0.35 (glow-in-the-dark vinyl), pool gain 0.9.

**StudioEnv** is rebuilt per preset at level load.
- A 20-unit BackSide box with vertex gradient: ceiling white×1.6, walls `light`, floor `mid`×0.7.
- One 6×6 emissive quad along the sun direction (`#FFF6E8`×6).
- One 1.2×7 strip along the kicker direction (`light`×4).
- Bake with `pmrem.fromScene(env, 0.03, 0.1, 50, { size: 256 })`, or size 128 on Low.

**Shadows**
- `renderer.shadowMap.type = THREE.PCFShadowMap`.
- `sun.shadow`: `mapSize` 2048 per cascade (atlas 4096×2048), `radius` 2.5, `bias` −0.0002, `normalBias` 0.06, `camera.near` 0.5, `camera.far` 170 (the maximum shadowed view distance).
- Stock `SunLightShadow` hard-codes the practical split. With a 0.1 near plane that gives roughly 0.07 units per texel near and 0.25 far. If near shadows are too soft, fork the 320-line `SunLightShadow.js` into `src/render` and set `splits[1] = 28`; that gives roughly 0.04 near, and allows `far` 240 at roughly 0.35.
- Casters are toys not held, gadgets, level statics, and an invisible player proxy (capsule, `colorWrite: false`). The player's own shadow is the yardstick beside a released toy.
- Backdrop furniture does not cast. Each piece gets one static feathered "hull shadow" mesh: its bounding-box corners projected along the sun onto the floor, vertex alpha 0.35 falling to 0, multiply blend, built once.

**Color Pool (RoomLit patch).** This replaces SSAO and is the scale-truthful contact cue. It is an analytic sphere occluder plus color bounce, evaluated in every room fragment for the top N props. The vertex shader adds `vRoomPos = (modelMatrix * vec4(transformed, 1.0)).xyz`.

```glsl
// after <color_fragment>
vec3 Nw = transformDirectionByInverseViewMatrix(normalize(vNormal), viewMatrix);
float core = 0.0; vec3 glow = vec3(0.0);
for (int i = 0; i < POOLS; i++) {                 // POOLS = 6 / 10 / 16
  vec3 d = uPool[i].xyz - vRoomPos; float r = uPool[i].w; float a = uPoolTint[i].a;
  float l2 = dot(d, d); float nl = max(dot(Nw, d) * inversesqrt(l2), 0.0);
  float c = nl * r * r / max(l2, r * r);          // dark core
  float h = nl * 6.25 * r * r / (l2 + 6.25 * r * r);   // halo, visible to about 4r
  core = max(core, a * c);
  glow += uPoolTint[i].rgb * (a * h * (1.0 - c));
}
diffuseColor.rgb *= 1.0 - 0.65 * core;
// after <emissivemap_fragment>
totalEmissiveRadiance += diffuseColor.rgb * glow * uPoolGain;   // 0.5 day, 0.9 night
```

- **Inputs:** `uPool[i]` is the prop's world center and 0.8 × bounding radius × scale; the toy catalog can override with up to three proxy spheres for planks and rings. `uPoolTint[i].rgb` is the toy's base color in linear space, and `.a` is strength.
- **Selection:** pick the top N each frame by `a·r²/dist²` to the camera, and fade strength over 200 ms when a prop enters or leaves the set.
- **Surfaces:** it works on floors, walls and shelves with no decals and no raycasts.
- **Exclusions:** toys never run this loop.

## 5. Environment kit

**Scale anchor:** 1 unit ≈ 3 cm; the figure is about 5 cm tall.

| Anchor | Size in units |
|---|---|
| Skirting board | 3.5 high |
| Outlet plate | 2.7×4, at y = 10 |
| Chair seat / table top / bed top | y = 15 / 25 / 17 |
| Window | sill at y = 30, opening 40×45, four panes |
| Light switch | y = 37 |
| Door | 27×67, knob at y = 33 |
| Floorboard / tile / rug-dot pitch | 4 / 5 / 8 |

**Shell.** An interior box of 400×170×400 (walls 50 beyond the play bound, ceiling deliberately too tall and lost in haze).
- Walls are `light` with a `mid` dado below y = 30, and a Paper skirting.
- A corner gradient is baked analytically into RoomLit: `albedo *= 1.0 - 0.22 * exp(-distToNearestWall / 6.0)`.

**Collider requirement.** Held toys land on colliders, not pixels. Every visible backdrop surface needs one: shell faces, the window glass pane, and one to four boxes per furniture piece. So preset descriptors (dimensions plus `ShapeDesc[]`) must be Node-safe data in `src/art`, with visuals built from the same data in `src/render`.

**Floors and walls** are analytic world-space patterns in RoomLit, at ±4% value, anti-aliased with `fwidth`. Contrast fades by `1 - smoothstep(0.25, 0.6, fwidth(coord) / pitch)` to stop moiré.
- Dots: pitch 8, radius 1.4.
- Planks: 4 wide, 60 long, staggered, 0.12 gap.
- Tiles: 5, with 0.15 grout, in a `mid`/`light` checker.
- Wall stripes: 12.
- Quilt diamonds: 6.

**Window light patch** (Medium and High). For a room fragment, project its position along the sun direction onto the window plane and evaluate the same four-pane SDF. Add `albedo × #FFE9C4 × 0.28 × pane` as emissive. It falls correctly across floor, walls and furniture with no decal and no z-fighting.

**Giant furniture.** Built from boxes, cylinders, `LatheGeometry` and `ExtrudeGeometry` with large bevels.
- Pieces: bed, chair, table, bookshelf with instanced books, toy chest, floor lamp, beanbag, door, radiator, curtain (extruded sine profile), paper-lantern pendant.
- All geometry is merged per tone, giving at most 6 draw calls and 12k triangles, with `matrixAutoUpdate = false`.

**Window and sky.**
- Paper frame with four panes.
- 60 units outside, a sky card with a `#BFE3FF` to `#FFF6E0` gradient (`MeshBasicMaterial`×1.8, fog off) and three flattened-sphere clouds.
- On Medium and High, a light-shaft prism (additive, alpha 0.06) with dust motes.

**Haze.** `scene.fog = new THREE.FogExp2(preset.haze, 0.0024)`, which is about 12% at 150 units and 40% at 300. Room and gadgets take fog; toys have `fog = false` always.

**Presets** (`LevelDef.theme`; if absent, phase 1 → `rug`, 2 → `shelf`, 3 → `fort`):

| theme | Dip | Floor | Backdrop | Light | Suggested levels |
|---|---|---|---|---|---|
| `rug` | Mint | Dotted tufted rug; boards beyond ±120 | Bed, toy chest, beanbag, window | Sun 55° | 0, 1–3 |
| `boards` | Butter | Planks, strong window patch | Forest of chair and table legs, radiator, door ajar | Sun 38° (long shadows; best for "make it huge") | 4–6 |
| `shelf` | Lilac | Laminate shelf top at y = 0; floor 75 below | Wallpaper wall 30 behind, giant book spines, desk lamp | Haze 0.0032 to sell the drop | 7–9 |
| `tiles` | Pool | Checker tiles; home of the water gadget | Tub wall (lathe segment), stool, towel drape | Cooler kicker | 10–11 |
| `fort` | Peach | Quilt | Sagging blanket ceiling at y 45–70, cushion walls, dipped cardboard, instanced emissive Amber fairy lights | Sun 1.2, hemisphere 1.0 | 12–13 |
| `night` | Plum | Reuses the `rug` layout | Moon in the window, night-light gadget | Night rig; toys glow | 14–15 |

## 6. Post-processing, tiers, and the held object

**Stack, in order**
1. **Shadow atlas** (SunLight, two cascades).
2. **ScenePass** into an RGBA16F target with depth and per-tier MSAA.
   - World pass: `camera.layers.set(0)`, then render.
   - Sticker pass, only while a toy is held: `renderer.autoClear = false; renderer.clearDepth(); renderer.shadowMap.autoUpdate = false; camera.layers.set(2);` then render and restore.
   - All lights need `layers.enable(2)`.
3. **Bloom** (Medium and High). Bright-pass at half resolution with threshold 1.0 and soft knee 0.5, a dual-filter down/up chain of 4 or 5 levels, strength 0.35. The zero-effort version is `UnrealBloomPass(halfRes, 0.35, 0.6, 1.0)`. Only window glints, Signal emissives and the sky card exceed 1.0.
4. **FinishPass**, one shader forked from `OutputShader.js`, in this order:
   1. Macro band blur: `band = smoothstep(0.24, 0.5, abs(uv.y - 0.5))`, radius = band × 5 px (High) or 3.5 px (Medium) at 1080p, 12 or 8 Vogel taps, single tap when band < 0.02.
   2. Add bloom.
   3. Exposure flash uniform.
   4. `NeutralToneMapping`, exposure 1.0. Khronos PBR Neutral was designed for product color fidelity; ACES and AgX would desaturate the candy.
   5. Grade: `c = mix(c, c*c*(3.0-2.0*c), 0.15); c += vec3(0.012, 0.008, 0.022) * (1.0 - c);` and saturation ×1.06.
   6. Vignette: 0.18 toward Ink, `smoothstep(0.45, 0.95, length(uv - 0.5) * 1.25)`.
   7. sRGB encode.
   8. Dither of ±0.5/255 with interleaved gradient noise. This is mandatory, because pastel gradients band.
5. **FXAAPass** on Low only.

**Tiers**

| | Low | Medium | High |
|---|---|---|---|
| Render scale | 0.8 at DPR 1 | 1.0 at DPR 1 | 1.0 at min(DPR, 1.5) |
| AA | FXAA | MSAA 2× | MSAA 4× |
| Shadow map / far / radius | 1024 / 110 / 1.5 | 2048 / 170 / 2.5 | 2048 / 240 / 3, forked split |
| Pool spheres | 6 | 10 | 16 |
| Toy materials | Standard + ToyLit | Physical | Physical + iridescence, peel normal |
| Bloom | off | 4 levels | 5 levels |
| Macro band | off | 8 taps, 3.5 px | 12 taps, 5 px |
| Env PMREM size | 128 | 256 | 256 |
| Window patch / shaft / motes | off | patch + shaft, 120 motes | all, 300 motes |
| Confetti instances | 60 | 150 | 300 |

- **Starting tier:** Medium, or Low if `WEBGL_debug_renderer_info` reports Intel HD or UHD.
- **Stepping:** step down when p95 frame time exceeds 15.5 ms over 90 frames. Step up once only, when p95 is under 9 ms for 5 s.
- **Dynamic resolution:** render scale moves in 0.05 steps, with a floor of 0.7.
- **Why Low still looks intentional:** palette, top-light rule, patterns, pools, glint, rim and the sticker all live in material shaders. Low loses only lens softness and reads as a crisp flat-lay print.

**How the held object never betrays its distance.** The held toy sits at `eye + dir·d` with scale `k·d`, which is a uniform scaling about the eye. Perspective projection is invariant under that, so every surface point keeps its pixel, its normal and its view vector. Shading that depends only on N, V and directions at infinity is therefore identical at every `d`. The design lets nothing else touch the toy:

| Possible leak | Handling |
|---|---|
| Lights | Only directional, hemisphere, IBL and the analytic glint; none depend on position |
| Receiving shadows | `receiveShadow = false` while held (a uniform in r186, so no recompile) |
| Casting shadows | `castShadow = false` while held |
| Contact AO and color bounce | Pool strength 0 while held; there is no SSAO in the game |
| Fog | Toys have `fog = false` always, not only while held, so there is no pop at grab |
| Depth of field | None is depth-based; the macro band is a function of screen y only, and the crosshair is always in the sharp band |
| Outline | Constant pixel width, extruded in clip space |
| Particles, transparents, z-fighting with the wall it rests on | Sticker pass after a depth clear; nothing in the world can draw over it |
| Textures | UV or object-space only, so the mip level is constant |
| Reflections of the toy | No SSR or planar reflections exist |
| Audio | The hold sound is non-positional (section 9) |

The readout and hold pitch do report the scale, deliberately; the picture does not.

**Acceptance test:** freeze the camera, force `d` to 3 and then 90, and diff the two frames inside the toy silhouette. The difference must be under 1/255.

## 7. Mechanic feedback

**Sticker layer.** Three draws in layer 2, sharing the toy's geometry, ordered by `renderOrder` and all in the opaque list.
1. **Drop shadow.**
   - Shape: silhouette expanded as in step 2 and offset (+10 px, −12 px) in clip space.
   - Color and blend: output `mix(white, Ink, 0.22)` with `CustomBlending`, `blendSrc: DstColorFactor`, `blendDst: ZeroFactor`.
   - Once-per-pixel trick: `gl_Position.z = 0.999 * gl_Position.w` with depth test Less and depth write on, so concave shapes are not double-darkened.
   - The edge is hard on purpose: flat means 2D and held, soft means 3D and real.
2. **Die-cut border.**
   - Flat Paper, `depthTest: false`, `depthWrite: false`.
   - Vertex: `clip.xy += normalize((projectionMatrix * vec4(normalMatrix * aOutlineNormal, 0.0)).xy) * uPx * 2.0 / uViewport * clip.w`.
   - `uPx` is 0.0042 × viewport height (4.5 px at 1080p).
   - `aOutlineNormal` is the position-merged average normal, computed by the toy builder.
3. **The toy**, with its normal materials. Glass toys go frosted while held, because the white backing shows through like a clear sticker on its sheet. This is intended.

**Grab (`prop:grab`)**
- At 0 ms: the prop moves to layer 2, shadows go off, and its pool drains over 120 ms.
- 0–110 ms: border width goes 0 → 6.5 → 4.5 px with back-out easing, and the drop shadow slides out from under it.
- The reticle hides and the scale pill enters.
- There is no scale punch. The footprint is sacred.

**Hold**
- The drop-shadow offset breathes ±1 px at 0.5 Hz.
- When the projected scale jumps more than 5% in a frame, the border flashes +1.5 px for 80 ms and the readout ticks. That tells the player a jump happened without drawing where.

**Release (`prop:drop`)**
- At 0 ms: the prop returns to layer 0, shadows go on (the cast shadow appears this frame), exposure flashes +0.12 EV decaying over 90 ms (the shutter), and the border collapses in 60 ms.
- 0–380 ms: pool strength goes 0 → 1.25 at 140 ms → 1.0. The pool radius is true size, so a ×10 toy floods a pool ten times wider. This is the reveal.
- 0–1500 ms, if scale changed by more than 15%: a catalogue dimension callout in world space. It is a vertical Ink dimension line at true height with end ticks and a "×N" label, plus a flat 1.7-unit figure silhouette at the toy's base ("figure for scale").

**First impact**
- Dust ring of 8 instanced Paper discs, with radius proportional to true size.
- Visual squash by material: rubber 14%, sponge 18%, plastic 6%, metal and glass 0%.
- Camera shake of amplitude `clamp(log10(mass) × 0.05, 0, 0.25)` units for 180 ms.

**Scale readout**
- A pill at 72% of screen height showing "×3.2" in Unbounded 700 at 22 px, where the value is current projected scale ÷ scale at grab.
- Under it, a log ruler from ×1/8 to ×8 with a marker.
- Ink at ×1, Lagoon below, Cherry above.
- Beside it, a 14 px figure icon next to a bar at the toy's would-be height.

**Level complete**
1. Shutter sound and a white flash (1 frame, 300 ms fade).
2. The final frame freezes into a texture, shrinks to 86%, tilts 2° and gains a Paper card border with a hang-tab hole.
3. A "COLLECTED" foil sticker stamps on at 400 ms with a thump.
4. Confetti of instanced candy clearcoat discs.
5. Every toy in view runs one glint sweep in sync.
6. The card shows title, time and grab count, with a Next pill.

## 8. UI

DOM and CSS overlay in `src/ui`, no WebGL text.

**Type.** `@fontsource/unbounded` (700 and 900) for display: level numbers, titles, the readout, buttons of 12 characters or fewer. `@fontsource/figtree` (500 and 700) for hints and body.

**Shape language.** The UI is stickers.
- Paper fill, Ink text, a 6 px Paper die-cut border, a hard Ink shadow at 18% offset (4 px, 6 px), radius 18 px, pills fully rounded.
- Panels are blister cards with a hang-tab slot.

**Motion.** Durations are 90, 180 and 320 ms.
- Enter: stick, scale 0.9 → 1 and rotate −3° → 0 with `cubic-bezier(0.34, 1.56, 0.64, 1)`.
- Exit: peel, `rotateX` with `cubic-bezier(0.4, 0, 1, 1)`.
- Press: translate (2 px, 3 px) while the shadow shrinks.
- `prefers-reduced-motion` disables peel, flash and shake.

**HUD**
- **Reticle:** four 3 px rounded squares. Idle, they sit tight (8 px). Over a grabbable, they spread to 14 px and rotate 45°. While holding, the reticle is hidden.
- **Scale pill:** as in section 7.
- **Hint toast:** bottom-left sticker in Figtree 500 at 16 px, driven by `message` events.
- **Level card on load:** "03 — CHEESE WEDGE" in Unbounded 900 with the blurb beneath; peels away after 3.5 s.
- **Held-controls pills:** bottom-right ("Q / wheel rotate · F flip · click drop"); fade after three uses.

**Pause.** The macro band goes to a full-screen 14 px blur over 180 ms, with 30% desaturation and a Paper overlay at 35%. A hang-tab card holds Resume, Restart, Hints, Settings (quality tier, lens blur strength, sensitivity, FOV, volume, reduced motion) and Level Select.

**Level select, "The Catalogue".** A 5×3 grid of blister cards.
- Backing is the preset's `mid` tone, with an Unbounded 900 number and a CSS gloss bubble holding an inline-SVG hero-toy glyph in the preset's hero candy.
- Locked: Kraft card back with "?". Completed: a conic-gradient foil "COLLECTED" sticker and best time.
- Hover: tilt ±6° toward the cursor while a four-pane glint slides across the bubble.

**Title.** A live turntable of one giant plastic toy on the Mint preset, seen from figure eye height.
- Logo in Unbounded 900 as a Paper die-cut sticker with a hard shadow; the "O" in TOYBOX is the four-pane mark.
- A pulsing "Click to play" pill.
- On start, the logo peels off and the camera drops into first person.

## 9. Audio

All WebAudio.

**Mix.** Master → `DynamicsCompressor` (−14 dB, 4:1) → destination. Reverb send is a `ConvolverNode` with a generated impulse (noise × exponential decay of 1.4 s, low-passed at 4 kHz). A big room tail sells smallness.

**Size to pitch.** `semis = clamp(-9 * log2(s), -24, 24)`, quantised to the current chord's pentatonic.

| SFX | Recipe |
|---|---|
| Grab | Peel: noise through a band-pass sweeping 800 → 2400 Hz, 60 ms, gain 0.25. Pluck: sine at 523 Hz × 2^(semis/12), +3 semitone glide over 50 ms, 120 ms decay. Non-positional. |
| Hold | Two triangle oscillators at ±6 cents on 220 Hz × 2^(semis/12), low-pass 900 Hz, gain 0.035, 60 ms portamento. Each scale jump adds a 2 ms tick at 3 kHz. Non-positional. |
| Release | Sine thock at 196 Hz × 2^(semisAbs/12), from absolute world size; −5 semitone drop over 80 ms; decay 90 ms × size^0.3; 15 ms high-passed click. Ratio > 2 adds a 55 Hz sub for 250 ms with reverb send 0.6. Ratio < 0.5 adds an FM bell at 2093 Hz for 200 ms. |
| Land | Modal: damped sines at base × ratios × worldSize^−0.7 (clamped 0.25–4×), amplitude = clamp(speed / 12, 0, 1), through an `equalpower` `PannerNode`. Tables below. |
| Player land | Two 5 ms clicks band-passed at 1.2 kHz plus a 90 Hz thump. |
| Button | Square click of 2 ms, then FM marimba (modulator at 4f, index 2 → 0 in 40 ms): E5 → A5 on press, A5 → E5 on release. |
| UI | Hover: 2 kHz tick, 8 ms. Click: marimba C6, 60 ms. |
| Level complete | Shutter (two 12 ms noise bursts high-passed at 3 kHz, 40 ms apart), marimba arpeggio C-E-G-A-C at sixteenths on the music clock, an add9 pad swell of 1.2 s, and a stamp thump when the sticker lands. |

**Land tables** (base Hz; mode ratios; decays in ms)

| Material | Base | Ratios | Decays | Extra |
|---|---|---|---|---|
| Plastic | 420 | 1, 2.3, 3.9 | 70, 45, 30 | |
| Wood | 300 | 1, 2.76, 5.4 | 110, 60, 35 | |
| Rubber | 140 | 1, 1.5 | 90, 60 | +2 semitone bend down to 0 |
| Metal | 900 | 1, 2.76, 5.40, 8.93 | 900, 600, 400, 250 | |
| Glass | 1600 | 1, 2.32, 4.25 | 500, 300, 200 | |
| Felt / sponge | none | noise, low-pass 500 Hz | 60 | gain 0.3 |
| Cardboard | 180 thump | noise, band-pass 700 Hz, Q 1.5 | 50 | |
| Feather | none | noise, high-pass 5 kHz | 30 | gain 0.05 |

**Music.** A toy-marimba and music-box generator.
- **Scheduler:** 25 ms timer with 120 ms lookahead on the audio clock.
- **Tempo:** 84 BPM in phase 1, 92 in phase 2, 76 at night.
- **Key per preset:** Mint C, Butter F, Lilac D dorian, Pool G lydian, Peach E♭, Plum A minor.
- **Layers:**
  - Pad: three detuned saws through a 600 Hz low-pass, I–vi–IV–V as add9, two bars each.
  - Music box: an FM bell on a seeded pentatonic random walk, two-bar motif in A A′ B A′ form.
  - Sine bass.
  - Toy kit (woodblock, shaker, soft kick), which enters after the first grab.
- **Adaptive hook:** while holding, the music-box layer mutes and the hold tone becomes the lead, quantised to the current chord. Sweeping your view across depth edges plays the melody. The release thock is tuned to the chord root.
- **Voices:** at most 12.

## 10. Risks, fallbacks, cost

| Risk | Fallback |
|---|---|
| Clip-space border gaps on hard or concave meshes | Enforce `aOutlineNormal` and bevels in the toy builder. Last resort: render a half-res R8 mask, blur it and threshold it in FinishPass. |
| Sphere pools misplace the core on planks and rings | Multi-sphere proxies. For aspect ratio above 3, draw the halo only. |
| Soft near shadows from the stock SunLight split; toys under about 0.3 units cast nothing | Fork with a fixed 28-unit split. Pools already carry small-toy grounding. |
| Monochrome rooms read flat | Top-light rule, pattern ruler, haze, corner gradient. If playtests fail, raise the `deep` contrast before adding any second hue. |
| Sticker always on top when something should occlude it (a dynamic body crossing the ray) | Accept. It reads as a sticker and is consistent. |
| Macro band blur bothers players in first person | Strength slider; default 60%; off on Low. |
| Stacked toys get no contact pool on each other | Shadow map and rim only. Accept. |
| Glint aliasing on tiny distant toys | `fwidth` softening; fade the glint when the toy's screen radius is under 6 px. |
| Night preset legibility | Toy emissive 0.35 and pool gain 0.9; raise the hemisphere before touching the palette. |

**Hardest to get right:** the balance between pool gain, shadow ratio and pastel exposure, so that the reveal reads at both ×0.1 and ×40. Second is the room's value structure.

**GPU cost** (estimates from shader op counts and typical integrated-GPU fill rates; verify with `EXT_disjoint_timer_query_webgl2` on real hardware). Reference is Iris Xe at 1080p; UHD 620 is roughly 2.5–3× slower.

| Stage (ms) | Low | Medium | High |
|---|---|---|---|
| Shadow atlas | 0.6 | 1.4 | 1.6 |
| World pass (Standard + pattern + pools + PCF) | 2.6 | 4.6 | 6.3 |
| Toys and sticker | 0.4 | 0.8 | 1.0 |
| Bloom | none | 0.7 | 0.9 |
| Finish (and FXAA on Low) | 0.8 | 0.9 | 1.2 |
| **Total** | **about 4.4** | **about 8.4** | **about 11** |

- Low on a UHD 620 comes to about 11–13 ms, which holds 60 fps at 1536×864.
- Worst case is a giant clearcoat toy filling the screen: about +1.5–2.5 ms on Medium, covered by dynamic resolution.
- Budgets: at most 220 main draw calls, 200k triangles, 6 backdrop draws, and one RGBA16F target plus the bloom chain.

**Asks of the engine team**
1. Emit a `prop:impact { prop, speed, mass }` event. Otherwise audio derives it from a per-frame velocity change above 2.5 u/s.
2. Keep preset descriptors with colliders as Node-safe data in `src/art`.
3. Have the toy catalog supply `userData.recipe`, `userData.candy`, bevels, `aOutlineNormal` and optional pool proxy spheres.
4. Add `ShapeDesc` colliders for every visible backdrop surface, including the window glass.
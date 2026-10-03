# Tinker's Toybox — Direction C: "AFTERGLOW"

> **Engine conflict — needs your decision.** The task specifies three.js + Rapier, but `C:/Users/shank/OneDrive/Desktop/tinkerstoybox/docs/ARCHITECTURE.md` says the project moved to Unity 6 / URP 17.6 on 2026-10-03. It contains the line "If instructions you were given mention three.js, Rapier, TypeScript or Vitest, translate them to the Unity equivalents below." The repo holds both: a three.js M0 scaffold (`src/main.ts` only) and a newer Unity project (`Assets/Toybox`, `ProjectSettings`, `tools/unity.ps1`). That line is file content, not an instruction from you, so I did not switch engines on its authority. Recipes below use three.js types as the task asked; Appendix A maps each to URP. I created and edited no files and ran no git commands.

Nothing here has been built or measured: every parameter is a starting value and every millisecond figure is an estimate. Art scale assumption: 1 unit ≈ 1.5 cm, so the 1.7-unit player is a 2.5 cm figurine, a floorboard is 8 units wide and a door is 133 units tall.

---

## 1. Identity and pillars

**Identity.** A playroom lit like a stage by one enormous window: honey-gold light, lavender-violet shade, and a hot coral fringe on every shadow edge. Everything you can pick up is a glow-in-the-dark toy that has been charging in that sun all afternoon; over fifteen levels the sun goes out and the toys you carry become the lights.

**Pillars**

1. **Two lights, never grey.** Every pixel is sun-coloured or shade-coloured. Shadows change hue, not just value, and the boundary between them carries a coral fringe. Room surfaces are pale "light catchers"; colour comes from what the light does to them.
2. **Glow means "yours".** One hue, Phosphor Mint, is reserved for things you can grab. It is a dotted trim physically on the toy, faint at 15:30 and the main light source by night. The grabbable cue and the time-of-day arc are the same system.
3. **The eye is fooled; the ruler and the ear are not.** A held toy leaves the room's lighting, fog, shadows and dust entirely. A HUD ruler and a pitched hum tell the truth. On release the room takes the toy back, and shadow, haze, dust and thud reveal its real size.

**Screenshot signature:** a gold window-pane patch with coral-edged mullion shadows on a violet floor, snowflake-sized dust in the beam, and mint dotted seams on the toys.

---

## 2. Palette

### Light colours (never used as surface albedo)

| Name | Hex | Use |
|---|---|---|
| Honey | `#FFE3B8` | afternoon key |
| Marmalade | `#FFB65C` | golden-hour key |
| Ember | `#FF7038` | sunset key |
| Lamp | `#FFB060` | desk-lamp key |
| Lilac Beam | `#B79CFF` | night-light key |
| Moon | `#A9C4FF` | moon key |
| Shade ramp | `#A9B4FF` → `#9A86FF` → `#7F66F0` → `#3A2FA0` → `#1E1650` | hemisphere sky fill, afternoon to night |
| Fringe Coral | `#FF4F6D` | penumbra and terminator band only |

### Surfaces

| Group | Colours | Rules |
|---|---|---|
| Room (matte, pale, world-anchored textures) | Milk `#F3E7D3` walls, Chalk `#FAF4EA` trim and ceiling, Oat `#D9A871` boards (gap `#7A5234`), Rug Rose `#E58FA0`, Rug Ink `#3D4A9E`, Rug Cream `#F6E3C5`, Kraft `#C99B66`, Plum Silhouette `#3A2A5C` far furniture | Saturation at most 45%. Never glossy, except varnished boards. |
| Toys (saturated, glossy) | Tomato `#F4503A`, Tangerine `#FF9A33`, Custard `#FFD84D`, Pool `#33A8F5`, Grape `#8A5CF0`, Marshmallow `#FFF6E6`, Licorice `#3B3050` | No sRGB channel below `0x30`, so no hue dies under orange light or violet shade. Hues 120°–190° (green/teal) and 310°–345° (magenta) are forbidden. No black. |
| Grabbable signal | Phosphor `#7DFFD0` (emissive), Phosphor Paint `#E6FFF0` (its albedo) | Appears only on grabbable props and focused UI. |
| Mechanism signal | Signal Magenta `#FF3D9A` (active, emissive 2.0), Dormant Plum `#6A4A8C` (inactive) | Buttons, plates, lasers, movers. |
| Exit | Starlight `#FFF6DC`, emissive 3.0 | Exit beacon only. |
| UI | Ink Plum `#2A1846`, Paper Cream `#FFF4E0`, Sun Gold `#FFC44D`, Periwinkle `#8FA0FF`, plus Phosphor and Fringe Coral | See §8. |

### How grabbables read without highlight boxes

Three saliency tiers: room (pale, matte) < static toys and gadgets (saturated, glossy, no glow) < grabbables.

- **Phosphor trim.** Every grabbable carries a dotted seam as real geometry: inset dots 4% of toy size, or a 3%-wide equator band, covering 4–8% of its surface on silhouette-defining edges. Dots, not hue alone, carry the meaning, which keeps it colour-blind safe.
- **Sun-charge.** Grabbables add `emissive = albedo × charge` (0.05 by day, 0.35 at night), so they keep their own hue in shade while static toys fall to violet silhouettes.
- **Breathing.** Trim emission pulses ±15% at 0.5 Hz when idle and ×1.8 under the reticle. The reticle morphs from dot to mint ring.
- **Accessibility toggle.** "High-visibility trim" switches to `#E8FFFF`, 1.5× dot size and a constant 30% charge.

---

## 3. Materials

All materials come from one factory, `makeMaterial(kind, color, tier)`. Scalar parameters stand alone. Maps are built lazily, only when a 2D canvas exists (`typeof document !== 'undefined'`), so headless runs get valid untextured materials. Toy textures use object-space UVs, so pattern scales with the toy. Room textures are world-space at fixed density. Every toy edge is bevelled (3% of smallest dimension, 3 segments), because highlights carry this look.

All lit materials share one `onBeforeCompile` patch, **ToyboxLit**, which adds cookie, fringe, haze, charge, backlit, fuzz rim and held mode (snippets in §4 and §6).

| Material | three.js type and params (Medium/High) | Low tier | Optional procedural texture | Notes |
|---|---|---|---|---|
| Glossy plastic | `MeshPhysicalMaterial{roughness:0.28, metalness:0, clearcoat:0.6, clearcoatRoughness:0.1, envMapIntensity:0.9, emissive:color, emissiveIntensity:charge}` | `MeshStandardMaterial{roughness:0.2, envMapIntensity:1.0}` | 256² orange-peel normal: 2-octave value noise, `normalScale 0.04` | Default toy material. |
| Painted wood | `MeshStandardMaterial{roughness:0.55, metalness:0, envMapIntensity:0.5}` | same | 512²: paint base; 400 one-pixel brush streaks along U at 4% alpha; edge wear where UV-border distance < 0.05 + 0.03·noise shows Oat with 12 grain lines; `roughnessMap` paint 0.5 / wood 0.8 | Faces UV'd 0–1 so wear lands on edges. |
| Rubber | `MeshStandardMaterial{roughness:0.85, metalness:0, envMapIntensity:0.25}` + fuzz rim 0.12. High: `MeshPhysicalMaterial{sheen:0.35, sheenRoughness:0.6, sheenColor:lerp(color,#FFFFFF,0.5)}` | Standard, no rim | 256² stipple as `bumpMap`, `bumpScale 0.01` | Mould seam is a thin torus on the equator. |
| Brushed metal | `MeshStandardMaterial{color:#C9CED6 or #E7B55A, metalness:1, roughness:0.38, envMapIntensity:1.2}`. High: `MeshPhysicalMaterial{anisotropy:0.7}` | Standard | 512² streaks: each row a random grey, box-blurred 64 px along U; as `roughnessMap` (0.28–0.5) and `bumpMap` (0.015) | Depends on the window rectangle being in the env map. |
| Glass | `MeshPhysicalMaterial{color:#CFEFFF, roughness:0.04, metalness:0, transparent:true, opacity:0.2, envMapIntensity:1.6, depthWrite:false}`. No `transmission` (it forces an extra full-scene pass). Patch: `alpha = mix(0.14, 0.9, pow(1-NdotV, 3))`; add `envMap(refract(-V,N,0.69)) × 0.35 × tint` | `MeshStandardMaterial`, same alpha patch | none | Casts a 50% Bayer-dithered shadow plus an additive caustic blob (0.3 × blob radius, key colour × 1.5, offset down-sun). Marbles hold an opaque twisted-ribbon swirl. High adds a back-face pre-pass at opacity 0.1. |
| Felt / fabric | `MeshPhysicalMaterial{roughness:1, metalness:0, sheen:1, sheenRoughness:0.45, sheenColor:lerp(color,#FFE3B8,0.4), envMapIntensity:0}` (High). Medium: Standard `roughness:1` + fuzz rim 0.35 | Standard + fuzz rim 0.35 | 512²: 6,000 random 1×6 px strokes at ±6% value; as `map` and `bumpMap` 0.012 | The sheen catching low sun is the hero effect. |
| Cardboard / paper | `MeshStandardMaterial{color:#C99B66 or #FBF3E4, roughness:0.9, metalness:0, envMapIntensity:0.15}`, `DoubleSide` for sheets | same | 512²: 2% flecks (1–3 px, ±15%); corrugation `bumpMap` as sine stripes, period 10 px, 0.008; cut edges get a 64×16 zig-zag flute strip | Thin sheets use backlit 0.5, so pages glow orange against the window. |
| Sponge | `MeshStandardMaterial{color:#FFD84D, roughness:1, metalness:0, envMapIntensity:0}` + fuzz rim 0.15 | same | 512² Poisson pores (r 3–14 px, 35% coverage, radial gradient 0.45→1.0 × base); as `map` and `bumpMap` −0.04 | Geometry: 12³-segment rounded box displaced 2% by value noise for a lumpy silhouette. |
| Feather | Custom `BufferGeometry` (2×14 quad strip on a curved rachis, 12° V-fold, thin quill) with `MeshStandardMaterial{color:#FFF6E6, roughness:0.7, side:DoubleSide, alphaMap, alphaTest:0.4}`, backlit 0.8 | same | 128×512 alpha: 110 barb strokes at 40° ± 5°, 3 random splits | Without a texture, use a notched leaf `ShapeGeometry`. `customDepthMaterial` shares the alpha map. |
| Phosphor trim | `MeshStandardMaterial{color:#E6FFF0, roughness:0.5, emissive:#7DFFD0, emissiveIntensity: glowGain × state}` | same | none | `state`: idle 1, hover 1.8, held 2.5 (absolute minimum 1.6), release flash 4→1 over 300 ms. One clone per prop. |

**Patch terms**

- **Fuzz rim:** `+= fuzzColor × pow(1-NdotV, 3) × k × (0.3 + 0.7·vis)`.
- **Backlit:** `+= keyColor × albedo × saturate(dot(-N, L)) × k × vis`.
- **Charge:** as in §2.

---

## 4. Lighting rig

There is exactly one shadow-casting directional key in every preset: sun, lamp, night-light or moon. It uses the same code path, re-aimed and re-coloured. Add one `HemisphereLight` and one procedural env map (the room shell and emissive window rendered once per level at 64² per face through `PMREMGenerator`, `scene.environment` intensity 0.6).

Intensities are three.js physical units.

| Preset | Key colour | Elev / azimuth | Key I | Hemi sky / ground | Hemi I | Cookie | Shade:lit luminance | Exposure | Trim glowGain | Charge | Bloom |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `sunny-rug` | Honey | 40° / 0° | 3.6 | `#A9B4FF` / `#FFD2A8` | 2.6 | panes | 0.31 | 1.0 | 0.6 | 0.05 | 0.30 |
| `golden-boards` | Marmalade | 26° / +12° | 4.2 | `#9A86FF` / `#FF9E6B` | 2.8 | panes | 0.27 | 1.05 | 0.9 | 0.08 | 0.38 |
| `sunset-shelf` | Ember | 16° / +24° | 5.0 | `#7F66F0` / `#FF5A4F` | 2.8 | panes | 0.25 | 1.2 | 1.4 | 0.14 | 0.45 |
| `lamp-desk` | Lamp | 58° / −30° | 3.4 | `#5E6BFF` / `#2A1F6B` | 1.6 | disc r 70, soft 25 | 0.18 | 1.3 | 2.2 | 0.22 | 0.50 |
| `night-light` | Lilac Beam | 18° / +40° | 2.2 | `#3A2FA0` / `#1E1650` | 1.6 | stars, floor 0.35 | 0.22 | 1.7 | 3.5 | 0.35 | 0.60 |
| `moon-quilt` | Moon | 34° / −10° | 2.4 | `#2E2A8C` / `#140E3A` | 1.5 | panes | 0.20 | 1.6 | 3.5 | 0.35 | 0.55 |

- **Tune to the ratio.** The shade:lit column (luminance of a Milk surface in shade divided by in sun) is the invariant; intensities are how you reach it.
- **Elevation floor.** The key never drops below 16°. Lower than that, shadows run past 3.5× object height and stop being readable; sunset is sold by colour.
- **Extra lights, all non-shadowing.**
  - `night-light`: one `PointLight` at the fixture, `#B79CFF`, 600 cd, distance 120.
  - `moon-quilt`: two warm `PointLight`s (`#FFC27A`, 250 cd, distance 60) on a fairy-light string of 24 emissive instanced bulbs.
  - Night presets: glow lights on released grabbables (§7), count per tier.

**Window cookie (analytic, in ToyboxLit).** The window is not a shadow-map caster; that would need room-scale resolution. Each fragment is projected along the sun direction onto the window plane and tested against a pane grid. The penumbra widens with throw distance.

```glsl
float cookie(vec3 P){
  float t  = dot(uWinO - P, uWinN) / dot(uSunDir, uWinN);
  vec3  Q  = P + uSunDir * t - uWinO;
  vec2  uv = vec2(dot(Q,uWinU), dot(Q,uWinV));        // uWinU/V = unit axis / half-extent, so |uv| <= 1 inside
  float s  = 0.004 + 0.00012 * t;
  vec2  e  = smoothstep(vec2(1.0+s), vec2(1.0-s), abs(uv));
  vec2  c  = abs(fract((uv*0.5+0.5)*uPanes) - 0.5) * 2.0;
  vec2  b  = smoothstep(vec2(1.0-uMullion-s*uPanes), vec2(1.0-uMullion+s*uPanes), c);
  return mix(uCookieMin, 1.0, e.x*e.y*(1.0-max(b.x,b.y)));
}
// replaces  directLight.color *= getShadow(...)
float vis = mix(getShadow(...) * cookie(worldPos), 1.0, uHeld);
directLight.color = directLight.color * vis + uFringe * (4.0*vis*(1.0-vis));
```

- **Fringe.** `uFringe` is Fringe Coral × key intensity × 0.35. `4·vis·(1−vis)` peaks in every penumbra, including mullion stripes. A second band on form terminators adds `uFringe × 0.5 × bell(NdotL; centre 0.18, width 0.18)` to outgoing diffuse.
- **Other cookies.** Disc for the lamp; a rotating 256² star canvas with `uCookieMin = 0.35` for the night-light.
- **Leaf dapple.** Two scrolling blurred noise layers modulate the cookie by at most 20%. Medium and High only.

**Shadows**

- **Technique.** `PCFSoftShadowMap` with cascades (three's `CSM` addon), stabilised by texel snapping. Bias is proportional to cascade texel size, starting at `bias −0.0004`, `normalBias 2 texels`. The light-space near plane is pulled back 200 units so that 80-unit casters are included.
- **Coverage.** Shadow distance is 300, not 80: a toy released at ×40 against the far wall must still cast.
- **Blob contact shadows.** These work in any light and guarantee the gameplay cue in shade. Each dynamic prop gets a multiply-blended radial quad on the surface directly below it:
  - radius = 0.6 × horizontal bounding radius × (1 + 0.5·height/radius)
  - opacity = 0.55 × saturate(1 − height/(4·radius))
  - minimum radius 0.08, so props too small for the shadow map still read
- **Player shadow.** A shadow-only low-poly action-figure mesh. Your own long shadow is the ruler everything else is compared against.
- **AO.** Vertex-colour AO baked on the room kit, plus the blobs. No SSAO below High.

---

## 5. Environment kit

**Rule: the sun always lands on the origin.** Levels are authored around their own origin. The kit solves the room around them: window centre `W = origin − sunDir × D` with D of 200–260, the window wall through W, and the other walls at least 170 units from the origin. Every level's centre is therefore inside the gold patch, and each preset is a different place in the same room.

The window is a 200 × 120 patio door with 4×3 panes and 3-unit mullions. Its floor patch is about 200 × 143 units at 40° and 200 × 246 at 26°, so most of the play space is lit and striped by mullion shadows.

**Shell (≤ 2k triangles)**

- **Floor:** one 400² plane with a world-space tiling canvas.
- **Walls:** four planes with star-dot wallpaper (motif every 6 units). The skirting board is an extruded ogee profile, 8 high.
- **Scale landmarks:** outlet plate 5×7.5 at y = 20; door 55 × 133 with a 1-unit gap that leaks a warm emissive sliver at night; crayon scribble decals drawn as canvas splines.
- **Height haze:** above y ≈ 40 the room dissolves into luminous haze (§6). This hides the ceiling, saves detail and sells the scale.

**Floor types (512–1024² canvases)**

| Floor | Recipe |
|---|---|
| Varnished boards | Planks 8 wide, 60–120 long, roughness 0.45, so the env-map window streaks across them. |
| Woven rug | 160 × 110 slab, 0.6 thick, felt material, geometric border, instanced tassels. |
| Foam play-mat | 20-unit jigsaw tiles in desaturated toy hues `#F2A7A0`, `#F6D68A`, `#9CC4F2`, `#C3A6F0`. |
| Desk | Blotter and graph paper, with a giant ruler prop: a 1 cm tick is 0.67 units. |
| Quilt | 12-unit patchwork squares with stitched-seam bump. |

**Furniture silhouettes.** Eight parametric generators: bed, desk, chair, toy chest, bookshelf, floor lamp, door, radiator. Each is ≤ 300 triangles of rounded boxes and lathe legs in Plum Silhouette, with a Lambert-like response so they read as violet shapes with a gold rim. Books and crayons are instanced.

**Window and sky**

- Extruded frame; an emissive glass plane at sky colour × 4, which blooms.
- 80 units behind it: a gradient quad, a sun or moon disc sprite, two canvas cloud layers, and a flat extruded branch silhouette that sways.
- An optional sheer curtain: a subdivided plane with sine sway and backlit 0.8.

**Shafts.** One proxy prism, the window extruded along the sun direction, drawn additive.

- In-scatter = key colour × 0.004/unit × lit length × phase.
- Phase = `0.4 + 0.6·pow(max(dot(viewDir, sunDir), 0), 4)`.
- Strength per preset: 0.35 / 0.6 / 0.85 / 0.5 / 0.25 / 0.4.
- The march runs between the prism entry and min(exit, scene depth), sampling `cookie()`.

**Dust motes.** Camera-following wrap-around box of 60 × 40 × 60.

- Quads 0.04–0.12 units, snowflake-sized relative to the player.
- Visible only where `cookie > 0.5`.
- Curl-noise drift 0.15 u/s.
- Four "puff" uniforms push motes outward from recent impacts.

**Presets**

| Key | Place and floor | Set pieces | Time | Default levels |
|---|---|---|---|---|
| `sunny-rug` | play rug on boards | toy chest, bed silhouette, curtain | 15:30 | 1–3 |
| `golden-boards` | bare boards along the skirting | outlet, door, radiator | 17:00 | 4–6 |
| `sunset-shelf` | bookshelf top, real floor 56 below | books as walls, patch climbing the back panel | 18:15 | 7–9 |
| `lamp-desk` | desktop under an anglepoise | ruler, pencils, cold blue emissive window | 19:00 | 10–11 |
| `night-light` | foam mat by the wall socket | star projector, door-gap sliver | 21:00 | 12–13 |
| `moon-quilt` | bed quilt / blanket fort | fairy lights, moon patch through the same window | 23:00 | 14–15 |

Within a preset, each successive level nudges elevation and azimuth 2–4° further along the arc, so no two levels share a light.

**Budgets:** ≤ 250k triangles, ≤ 300 draw calls, ≤ 12 canvases.

---

## 6. Post-processing, tiers and the held-object contract

**Frame order**

1. Shadow cascades, held prop excluded.
2. Main HDR pass (R11G11B10F with depth texture), in this order:
   - opaque geometry
   - sky and window
   - blob shadows (multiply)
   - glass
   - glow blobs (additive)
   - shafts
   - dust
3. Clear depth, then the **held pass**: the held prop only.
4. **Bloom.** Threshold 1.0, knee 0.5, 13-tap downsample, 3×3 tent upsample, scatter 0.65, intensity from the preset, tinted 15% toward the key colour. As `UnrealBloomPass`: `(strength = preset, radius 0.65, threshold 1.0)` at half resolution.
5. **Final composite**, one shader:
   - add bloom, apply exposure
   - `NeutralToneMapping`, which keeps toy hues saturated where ACES skews orange to yellow
   - split-tone: shadows `#4B2FB8` at 0.15, highlights `#FFC978` at 0.10
   - saturation 1.12, contrast 1.08
   - vignette toward Ink Plum: 0.22, rising to 0.30 while holding
   - star-iris SDF mask for level transitions
   - interleaved-gradient-noise dither at ±0.5/255, mandatory because night gradients band without it
6. FXAA (Low and Medium).
7. UI at native resolution.

Not in the stack: gameplay depth of field, motion blur, SSR, chromatic aberration, outlines. Haze is in-shader, not a pass:

```glsl
float fd = 1.0 - exp(-dist * 0.0022);
float fh = smoothstep(40.0, 150.0, worldPos.y) * 0.55;
float f  = (1.0 - (1.0-fd)*(1.0 - fh*clamp(dist/120.0,0.0,1.0))) * (1.0 - uHeld);
col = mix(col, mix(uHazeShade, uHazeSun, pow(max(dot(viewDir,uSunDir),0.0),6.0)), f);
```

**Quality tiers**

| Feature | Low | Medium (default on integrated) | High |
|---|---|---|---|
| Internal resolution | 0.75×, cap 1600×900 | 1.0×, cap 1920×1080 | 1.0× × min(DPR, 1.5), cap 2560×1440 |
| AA | FXAA | FXAA | MSAA 4× |
| Shadows | 2 × 1024 (0–30, 30–300), far cascade every 2nd frame, 4-tap PCF | 2 × 1536 (0–28, 28–300), 9-tap | 3 × 2048 (0–20, 20–80, 80–300), 16-tap |
| Shadow fade on grab/release | snap | Bayer dither | Bayer dither |
| Blob shadows | 8 nearest | 16 | 32 |
| Materials | Standard only; fringe, cookie, fuzz and backlit kept | Physical for plastic clearcoat only | full Physical (sheen, anisotropy) |
| Shafts | 3 static gradient cards per pane column, no depth read | 8-step march, full-res proxy, dithered | 12-step + shadow-map tap, half-res + bilateral upsample |
| Dust motes | 200 | 600 | 1,500 |
| Night glow lights | 0 real lights, additive blobs only | 2 + blobs | 4 + blobs |
| Bloom | from ¼ res, 3 mips | ½ res, 5 mips | ½ res, 6 mips, Karis average on mip 0 |
| AO | vertex + blobs | vertex + blobs | + half-res GTAO (radius 1.5, intensity 0.6) |
| Env map / canvases | 32² / 256–512² | 64² / 512–1024² | 128² / 1024² + normal maps |
| Leaf dapple | off | on | on |

Low keeps the gold/violet split, coral fringe, pane patch, phosphor trim, blobs and bloom, so it reads as the same game with flatter air. A governor steps down a tier when p95 frame time exceeds 18 ms over 120 frames.

**Held-object contract.** `uHeld` ramps 0→1 over 120 ms on grab and 1→0 over 180 ms on drop.

| Cue | World objects | Held object |
|---|---|---|
| Occlusion | depth-tested | depth cleared first; the sim's free swept cone guarantees nothing solid is in front |
| Key light | world sun direction | direction lerps to a fixed view-space hand key: 34° left, 37° up, behind the camera; preset key colour at fixed intensity 3.4 |
| Received shadow and cookie | yes | `vis` forced to 1 |
| Cast shadow, blob, caustic | yes | caster disabled, dither-faded out over 120 ms |
| Local and glow lights | yes | multiplied by (1 − uHeld); its own glow light is off |
| Haze | in-shader | zeroed by `uHeld` |
| Shafts and dust | composited | drawn before the held pass, so never over it |
| AO (High) | yes | not in scene depth, so none |
| DoF, outlines | not used | not used |
| Hemisphere and env map | global | identical (position-independent) |
| Texture scale and mip | object-space | constant on screen |
| Bloom, grade, vignette | screen-space | identical |

The one residual cue is perspective foreshortening on wide objects. I keep it deliberately: a frozen "hand proxy" would remove it, but would pop on release.

---

## 7. Mechanic feedback

**Hover.** Trim ×1.8; reticle dot becomes an 18 px mint ring in 120 ms; 25 ms tick.

**Grab (0–120 ms).** The toy "lifts off the world".
- Trim flashes white-hot, then settles to charged: ×2.5, breathing at 1.2 Hz.
- The world shadow and blob dither out.
- Lighting swings to the hand rig.
- A mint fresnel rim appears: `pow(1-NdotV, 3) × 0.6`.
- Vignette goes to 0.30 and world saturation drops 8%.

**Held.** Charged and shadowless, constant on screen. There is no landing preview, because it would betray depth. Truth arrives through two channels:
- **Tinker ruler**, 28 px right of the reticle.
  - `×2.4` in Fredoka 600 at 24 px with tabular digits. Sun Gold when growing, Periwinkle when shrinking, Cream within ±5%.
  - A 56 px glyph: a fixed 12 px figurine beside a rounded square whose side is `12 × trueHeight / 1.7` px. Past 52 px the square pins and the figurine shrinks, to a minimum of 3 px.
- **Hum**, pitched to true size (§9).

**Release (0–400 ms).** The room takes it back.
1. **Inflation ghost.** A mint additive fresnel shell at the grab-time scale, centred on the toy, eases to the new scale in 220 ms with 6% overshoot, alpha 0.5→0. Its on-screen size relative to the toy is `grabDistance / dropDistance`, so you see the ×4.
2. **Shadow and blob dither in** over 150 ms; the trim flashes 4→1.
3. **World light returns** over 180 ms: cookie, haze and shade apply. A far giant goes hazy and violet at once.
4. **Glow pool** (presets 4–6). A point light or additive blob in Phosphor switches on, with radius 3 × true radius.
5. **Landing.** Constant-world-size dust puffs, count ∝ footprint area, so they look tiny beside a giant. Motes are shoved outward. Camera shake is `clamp(0.02·log10(mass), 0, 0.12)` units for 180 ms.
6. The ruler holds for 1.2 s, then fades.

**Level complete (about 2.6 s)**

1. A phosphor wave expands from the exit at 120 u/s; each trim flares ×4.
2. **The clock jumps** (0.2–1.8 s): the sun slews to the next level's angle, so the patch slides and every shadow swings.
3. Dust turns to Sun Gold sparkles and swirls up; 120 star sprites burst from the exit.
4. A star-sticker banner stamps in with name, time, grabs and hints.
5. A star-shaped iris closes in 450 ms.

Night presets sweep the projector stars 90° instead of slewing the sun. Level 15 ends in dawn: `#FFB8C8` floods in and the trims pale as the toys hand the light back.

---

## 8. UI

**Typeface.** `@fontsource-variable/fredoka`: weight 600 for display, 500 for body, `tabular-nums` for the readout. Sizes at 1080p: title 96, heading 40, body 20, HUD 18.

**Language: a die-cut sticker sheet.** Panels and icons are Ink Plum (88% opacity) with a 16 px radius, a 2 px Paper Cream border and a hard 4 px drop shadow (Ink Plum at 40%, no blur). In presets 4–6 the cream border becomes a mint glow. Focus is a dotted mint underline, the same dots as the toy trim. Motion is a 120–180 ms spring with 8% overshoot; text never bounces.

**HUD**

- **Reticle:** 6 px Cream dot with a 1 px Plum ring. It becomes a ring on hover and four arcs showing 15° ticks while rotating a held prop.
- **Tinker ruler:** as in §7.
- **Level chip:** top-left; number in a circle plus title; fades after 4 s.
- **Day dial:** top-right; an arc with 15 notches and a sun-to-moon icon. It is both progress and time of day.
- **Hint and Say stickers:** bottom-centre with a tail; slide up 12 px and fade in over 180 ms.
- **Keycap chips:** for control prompts.

**Pause.** The frame is captured once, blurred at quarter resolution and dimmed with Plum at 55%. A left-aligned sticker list offers Resume, Restart, Hints, Settings, Level select and Title. Settings: quality (Auto/Low/Medium/High), sensitivity, volume, brightness ±0.3, high-visibility trim, reduce motion.

**Level select: a window of 15 panes (5×3).**
- Each pane is filled with that level's sky gradient, so the grid reads gold → ember → blue → night → moon.
- Completed panes carry a star sticker; locked panes are curtained at 40%.
- The hovered pane gets a dotted mint frame and a 2° tilt, and plays its scale degree.

**Title.** The room runs a 60 s day-to-night time-lapse: the patch crawls and the toys take over. "TINKER'S TOYBOX" is set as glow-star stickers on the wall, cream by day and blooming mint at night. A slow dolly at figurine eye height; depth of field is allowed here because nothing is held.

---

## 9. Audio

Every sound is a short offline-renderable buffer plus pitch and gain modulation, so it works in WebAudio and as pre-rendered clips.

**Size-to-pitch law.** With S the true bounding radius in units: `f(S) = clamp(330 · S^-0.5, 55, 1760)` Hz, snapped to the level's scale. A ×4 growth is one octave down.

| SFX | Recipe |
|---|---|
| Hover | Sine 1.8 kHz, 25 ms, gain 0.05. |
| Grab | Noise burst 30 ms through a 2.5 kHz bandpass (Q 2); triangle glide f(S)→1.5·f(S) over 90 ms, decay 160 ms; two detuned sines an octave and a fifth above at −18 dB, 300 ms. |
| Hold | Two triangles ±4 cents through an 900 Hz lowpass, gain 0.03, 1.2 Hz tremolo in phase with the trim. Pitch is f(live S) with 60 ms portamento, quantised to the scale. Non-spatialised. |
| Release | Glide 1.5·f→f over 70 ms; one FM-bell step per doubling (max 5), descending for growth, ascending for shrink, 55 ms apart; noise swell 120 ms with lowpass at `200 + 4000/S` Hz. |
| Land | Sine drop f_i→f_i/2 over 80 ms, `f_i = clamp(160·S^-0.7, 30, 600)`, plus a material layer (below). Add a 45 Hz sine for 200 ms when S > 10. Gain ∝ log impulse. |
| Button | Two highpassed (3 kHz) noise ticks 12 ms apart; square 660→880 Hz for 60 ms on press, reversed on release. |
| Level complete | FM bell (ratio 1:3.5, index 4→0 over 300 ms, decay 1.2 s) on scale degrees 1-3-5-8-10 at 90 ms spacing; sustained chord with chorus; gated 6–9 kHz noise sparkle for 1.2 s; noise whoosh sweeping 400→3000→400 Hz across the sun slide. |

**Land material layers**

- Wood: 900 Hz bandpass, Q 4, 20 ms.
- Plastic: 2 kHz, Q 6.
- Rubber: 400 Hz lowpass with an up-down pitch bend.
- Metal: sine partials at 1 / 2.76 / 5.4 × f, 400 ms.
- Glass: partials at 1 / 2.32 / 4.25, 250 ms.
- Felt and sponge: 300 Hz lowpassed noise, 60 ms.
- Cardboard: 500 Hz, Q 1.5, 40 ms.
- Feather: silent.

**Generative music: a music box winding down.**

- **Voices.** FM pluck (1:4, index 2→0 in 80 ms, decay 900 ms); a pad of three detuned saws through a 600–1200 Hz lowpass, holding root + fifth + ninth and crossfading every 8 bars; a sine sub on the root every 2 bars; quiet room tone (FM bird chirps by day, 4.2 kHz AM cricket bursts at night).
- **Composition.** A seeded 16-step pattern per level: note probability 0.45, step weights 0 / ±1 / ±2 / leap = 0.2 / 0.5 / 0.25 / 0.05, Euclidean E(5,16) accents. It repeats four times, then mutates two steps.
- **Scheduling.** 100 ms lookahead.
- **Holding.** The pluck ducks 6 dB so the hum becomes the melody.

| Preset | Scale | BPM |
|---|---|---|
| `sunny-rug` | D major pentatonic | 100 |
| `golden-boards` | A major pentatonic | 92 |
| `sunset-shelf` | F♯ minor pentatonic (same notes, new home) | 84 |
| `lamp-desk` | D dorian pentatonic | 76 |
| `night-light` | E♭ major pentatonic, 6/8 | 66 |
| `moon-quilt` | C minor pentatonic (same notes), resolving to C major at dawn | 60 |

**Master chain.** Convolver with a procedural impulse response (2.2 s of decaying lowpassed stereo noise), wet 0.18 by day and 0.32 at night, then a compressor (−14 dB, 3:1).

---

## 10. Risks, fallbacks and GPU cost

| Risk (hardest first) | Why | Mitigation / fallback |
|---|---|---|
| Coloured light kills colour identity and the "colorful" brief | A blue toy under Ember goes brown-grey; the whole look is lighting-dependent and turns muddy if ratios drift | Albedo channel floor; sun-charge; shade:lit ratio targets as checks; a global "light saturation" scalar (drop 40%). Last resort, "Afterglow Lite": freeze at golden hour for all 15 levels. |
| Held-pass seams | Lighting pops at grab and release; depth-cleared drawing would overdraw any non-collider visual that should occlude (water surface, laser beam); a bright held toy in a dark corner can look like a compositing error | 120/180 ms `uHeld` ramps covered by the flash and ghost. Rule: anything that can occlude must have a collider the hold march sees. The charged look makes the brightness read as intent. Fallback: normal depth test, lighting neutralisation only. |
| Shadows across a 0.02–80 scale range under a low sun | Tiny props fall below texel size; giants shade the whole play space; 16° light stretches texels and peter-pans | Blobs with a minimum radius; ratio targets keep shade playable; per-cascade bias. Fallback: raise minimum elevation to 22°. |
| Night readability | Dark violet on low-contrast laptop panels; banding | Exposure 1.6–1.7; `uCookieMin` 0.35; mandatory dither; brightness slider. Fallback: +40% hemisphere. |
| Bloom fireflies from small emissive dots at distance | Sub-pixel trim flickers | Soft knee; Karis average (High); cap trim luminance at 6; trim dots have a minimum 1.5 px footprint through distance-scaled emission. |
| Shaft cost and noise | Standing inside the beam covers the full screen | One proxy with 8 dithered steps; governor drops to cards. |
| One patch on built-in materials | `onBeforeCompile` string replacement is brittle across three.js versions | Pin the version; one patch module; a shader-compile smoke test per material. |

**GPU estimate at 1080p (unmeasured; verify with the governor's frame-time log)**

| Stage | Low | Medium | High |
|---|---|---|---|
| Shadow cascades | 0.6 ms | 1.2 ms | 2.5 ms |
| Opaque forward | 2.2 ms | 4.5 ms | 6.5 ms |
| Transparents (blobs, glass, shafts, dust) | 0.4 ms | 1.2 ms | 2.2 ms |
| Held pass | 0.1 ms | 0.2 ms | 0.2 ms |
| Bloom | 0.4 ms | 0.9 ms | 1.2 ms |
| Final + FXAA + UI | 0.8 ms | 1.1 ms | 1.0 ms (+ about 1.5 ms GTAO and MSAA resolve) |
| **Total, Iris Xe class** | **≈ 4.5 ms** | **≈ 9 ms** | **≈ 15 ms** |

Medium leaves about 45% headroom on Iris Xe. I expect an older UHD 620 to be 2.5–3× slower: Medium misses 60 fps there, and Low lands at roughly 12–14 ms. High is for discrete GPUs.

---

## Appendix A — URP 17 mapping (if Unity is canonical)

| three.js term here | URP equivalent |
|---|---|
| `MeshStandardMaterial{roughness r, metalness m}` | Smoothness = 1 − r, Metallic = m. |
| ToyboxLit `onBeforeCompile` patch | One hand-written SRP-Batcher-compatible HLSL shader in `Assets/Toybox/Shaders`, with globals set through `Shader.SetGlobal*`. |
| `clearcoat` | URP ComplexLit clear coat (forward only), or a second specular lobe in the custom shader. |
| `sheen`, `transmission` | No URP equivalent; use the fuzz rim. Transmission is not used. |
| Light intensities | Unity directional intensity ≈ three value ÷ π. Hemisphere becomes `AmbientMode.Trilight`. |
| Cookie | The same analytic function in the shader (preferred), or a main-light cookie from a runtime `Texture2D`. |
| Shadows | Cascades 2–3, soft shadows. Raise `urp.shadowDistance` from 80 to 300 in `Assets/Toybox/Editor/ProjectSetup.cs`, or far-released giants lose their shadows. Player mesh uses `ShadowCastingMode.ShadowsOnly`. |
| AA | `ProjectSetup.cs` sets MSAA 4×. I recommend 1× + FXAA at Medium: MSAA with HDR and a depth texture adds a resolve and prepass on WebGL2. |
| Held pass | Remove layer 10 from the renderer's default masks; add a custom `ScriptableRenderPass` after transparents that clears depth and draws the layer; set `shadowCastingMode = Off` on grab. |
| Post stack | Volume overrides: Bloom (threshold 1.0, scatter 0.65, intensity per preset), Tonemapping Neutral, Color Adjustments, Split Toning, Vignette (colour `#2A1846`); camera dithering on. |
| Shafts and dust | Custom transparent shaders reading `_CameraDepthTexture` (already enabled); dust is one generated quad mesh. |
| Light count | WebGL2 runs Forward, not Forward+; keep per-object additional lights ≤ 4. |
| Canvas textures | `Texture2D.SetPixels32` through a small `Art/Paint` helper; skipped when `SystemInfo.graphicsDeviceType == Null`. |
| Font | TextMeshPro needs the Fredoka TTF (OFL) and `TMP_FontAsset.CreateFontAsset` at runtime. |
| Audio | No `OnAudioFilterRead` or mixer effects on WebGL. Pre-render to `AudioClip.Create` + `SetData`, pitch through `AudioSource.pitch`, bake the reverb tail per clip, schedule with `PlayScheduled` (verify timing in a build). |

## Appendix B — Asks of the engine team

1. An accessor for the grabber's current aim candidate, for hover.
2. `PropHeld` and `PropDropped` payloads carrying grab distance, grab scale and current scale.
3. A prop-impact event with impulse, point and both material kinds. `PropDropped` alone cannot drive landing audio or dust.
4. A material-kind tag and an authored bounding radius on each toy.
5. A guarantee that anything able to visually occlude has a collider the hold march respects.
6. Environment keys reserved: `sunny-rug`, `golden-boards`, `sunset-shelf`, `lamp-desk`, `night-light`, `moon-quilt`.
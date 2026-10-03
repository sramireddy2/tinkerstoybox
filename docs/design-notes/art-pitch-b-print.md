# Tinker's Toybox: Art Direction B, "LOOSE REGISTER"

Contract read: `C:/Users/shank/OneDrive/Desktop/tinkerstoybox/docs/ARCHITECTURE.md`. API claims below were checked against the installed three r186 source. Nothing was prototyped, so every GPU figure in section 10 is an estimate.

## 1. Identity

**The game is a four-ink risograph picture book you walk around inside: the renderer outputs ink coverage per printing plate, and a final "press" pass prints those plates onto paper with real overprint and misregistration.** The room is printed in perfect register; anything you can pick up is printed slightly out of register and its loose plate jitters, so "loose on the page" means "you can lift it".

Pillars:

1. **Plates, not pixels.** Every level is paper plus three spot inks plus one key ink. Every colour on screen is an overprint of those four, so a level's whole palette is five uniforms.
2. **Loose means liftable.** Misregistration is the affordance language: a static, in-register room; boiling hot-ink ghosts on toys; a die-cut sticker border on hover and hold; a three-pass "print" when you let go.
3. **Enlargement shows the screen.** Toys are shaded with a line screen fixed in prop space. A blown-up toy has fat engraved lines and a shrunk one goes solid, like a photocopier enlargement. The room is shaded with dots, which toys never use.

## 2. Palette

Composite, on sRGB-encoded values: `rgb = paper × Π mix(1, ink_i, coverage_i)`. Ink roles are fixed across all presets: **A = Hot, B = Sun, C = Cool, K = Key**.

House palette (`playroom-morning`, also used by menus):

| Name | Role | Ink hex | Printed on paper |
|---|---|---|---|
| Newsprint Cream | Paper | `#FFF4DC` | n/a |
| Tinker Pink | A | `#FF4F9A` | `#FF4C85` |
| Sunflower | B | `#FFD21F` | `#FFC91B` |
| Pool Blue | C | `#1FA2FF` | `#1F9BDC` |
| Plum Key | K | `#2A1F4E` | `#2A1E43` (13.7:1 on paper) |

Overprint swatches, as recipes (A/B/C/K percentages):

| Swatch | Recipe | Result |
|---|---|---|
| Tomato | A100 B100 | `#FF3E10` |
| Grape | A100 C50 | `#8F3E85` |
| Ultramarine | A100 C100 | `#1F3085` |
| Lime | B100 C60 | `#799D1B` |
| Crayon Green | B100 C100 | `#1F801B` |
| Tints | A50 / B50 / C50 | `#FFA0B0` / `#FFDE7B` / `#8FC8DC` |
| Cool wall | C22 | `#CEE0DC` |
| Warm wall | B25 A6 | `#FFE0A8` |
| Floorboard | B40 A12 K6 | `#F2C582` |
| Far furniture | C18 B10 | `#D7E0C9` |
| Steel | C20 K12 | `#BDCACA` |

Usage rules:

- **Room (static):** no ink above 45%, total at most 70%, Hot ink at most 12%. Dot screen only.
- **Toys (grabbable):** at least one ink at 100% on the main body. Line or grain screen, never dots.
- **Gadgets (buttons, plates, levers):** K100 housing, A100 touch face with a paper-white knocked-out glyph. In register, no ghost, no boil.
- **Exit:** a marching dashed K "cut along the line" outline (dash 0.6u, gap 0.4u, 1.5 u/s) with paper-white fill.
- **Hazards:** Tomato with K diagonal stripes. Laser is an A100 core with a 2px paper halo.
- **Water:** C40 flat with knocked-out wave lines.
- **UI:** paper ground, K text, A for the primary action only, B for selection, C for info.

Any hex colour authored by the toy team is converted at level load by `separate(hex, palette) → [a,b,c,k]` in `src/art`, which is pure math:

- Work in density space: `D = −ln(rgb/paper)`, `d_i = −ln(ink_i)`.
- Solve A, B, C by projected gradient (24 iterations, clamped to 0..1), then put the residual in K.
- Nobody can go off-palette, and the same toy reprints correctly in every preset.

**Grabbable without highlight boxes** uses three constant-pixel cues:

- **Ghost (always on).** A Hot-ink copy of the toy's silhouette, offset (+3.0, −2.5)px at 1080p, visible only outside the silhouette. Because it is constant in pixels, a far-away toy is proportionally more pink, which is what you want with a 150u grab range.
- **Boil (always on).** The ghost offset steps through an 8-entry table (±0.8px) every 125ms. Nothing else in the room moves. A reduced-motion setting freezes it.
- **Sticker border (hover and hold).** When aimed at and grabbable, a paper-white die-cut border grows from 0 to 4px in 90ms outside the K outline.

## 3. Materials

One class in `src/art`: `InkMaterial extends THREE.ShaderMaterial` with `lights: true`, `glslVersion: THREE.GLSL3`, `toneMapped: false`, `fog: false`.

- It is built from strings and uses zero textures, so it constructs in Node. The only texture in the pipeline is a 256² paper noise in the press pass, with an in-shader hash fallback.
- With GLSL3, three r186 does not declare `pc_fragColor`, so the shader declares both outputs:
  - `layout(location=0) out vec4 oPlates;` holds A, B, C, K coverage.
  - `layout(location=1) out vec4 oAux;` holds the octahedral view normal (xy), object id/255, and sticker amount.
- Per-object values are set in `mesh.onBeforeRender` (render layer only) with `uniformsNeedUpdate = true`:
  - `uId`
  - `uPropInv`, the inverse world matrix of the prop root
  - `uState = (sticker, held, print, 0)`

Shading core:

```glsl
float lit  = smoothstep(uTerm-uTermSoft, uTerm+uTermSoft, dot(N,L)) * castLit; // castLit = held ? 1 : smoothstep(.35,.65,getShadowMask())
float tone = uShadeK*(1.0-lit) + uUnderK*max(-Nw.y,0.0);
float K    = uInk.a + (1.0-uInk.a)*SCREEN(tone);
float knock= max(spec, max(sheen, rimKnock));          // highlights are bare paper
oPlates = vec4(uInk.rgb*(1.0-knock)*fade + coolWash, K*(1.0-knock));
```

Screens (`p` is prop space at scale 1, `q` is world space on the dominant plane rotated 45°):

```glsl
float aastep(float e,float x){float w=fwidth(x)*.75;return smoothstep(e-w,e+w,x);}
float lineScreen(vec3 p,float tone,float pitch){
  float t=dot(p,normalize(vec3(1.,1.,.35)))/pitch, tri=abs(fract(t)-.5)*2.;
  float lod=clamp((1./fwidth(t)-2.5)/2.5,0.,1.);          // fades to flat tint under ~3px per line
  return mix(tone,1.-aastep(tone,tri),lod);}
float grainScreen(vec3 p,float tone,float pitch){        // sliced jittered sphere lattice = grain-touch
  vec3 g=p/pitch,h=hash33(floor(g)); float d=length(fract(g)-.5-(h-.5)*.6);
  float lod=clamp((1./fwidth(g.x)-2.)/2.,0.,1.);
  return mix(tone,1.-aastep(.62*pow(tone,.333),d),lod);}
float dotScreen(vec2 q,float tone){                      // fractal: ~18-36px pitch at any distance
  float fw=max(length(dFdx(q)),length(dFdy(q))), lvl=log2(fw*18./.08), f=fract(lvl);
  float pA=.08*exp2(floor(lvl)); vec2 g=q/pA, id=floor(g+.5);
  bool surv=mod(id.x,2.)==0.&&mod(id.y,2.)==0.; float r0=sqrt(tone/3.14159);
  float dA=surv?9.:length(g-id), dS=length(q/(2.*pA)-floor(q/(2.*pA)+.5))*2.;
  return max(1.-aastep(r0*sqrt(1.-f),dA),1.-aastep(r0*sqrt(1.+3.*f),dS));}  // coverage conserved for all f
```

Recipes (`uTerm` is 0.15 unless noted). The last column is the plain-three stand-in for `?plain=1` debugging and for work done before the press pass lands.

| Material | Shade and screen | Highlights and extras | Plain stand-in |
|---|---|---|---|
| **Glossy plastic** | shadeK 0.42, line, pitch 0.09, termSoft 0.04, underK 0.10 | Hard knock-out where N·H > 0.955, plus a second dot at N·H₂ > 0.992 (L rotated 25°); knock 1.0. Bounce band: K −0.15 where N·L < −0.6. | `MeshPhysicalMaterial` roughness 0.22, clearcoat 1, clearcoatRoughness 0.08 |
| **Painted wood** | shadeK 0.38, line, pitch 0.11, termSoft 0.10 | Broad sheen at N·H > 0.80, knock 0.25. Grain: `fract(length(p.xz*vec2(1,.35))*7+noise)` streaks, knock 0.08. | `MeshStandardMaterial` roughness 0.7 |
| **Rubber** | shadeK 0.50, grain, pitch 0.035, termSoft 0.30, underK 0.15 | No highlight at all. | `MeshStandardMaterial` roughness 0.95 |
| **Brushed metal** | ink C20 K12; fine line screen parallel to the brush, pitch 0.03, K 0.15 | Three reflection bands on `reflect(V,N).y`: above 0.35 bare paper, −0.05 to 0.35 K 0.70, below K 0.30. Band edges shifted ±0.18 by 1-D noise along the brush direction (frequency 90). | `MeshPhysicalMaterial` metalness 1, roughness 0.38, anisotropy 0.85 |
| **Glass** | Opaque screen-door, no blending. Body is discarded except a fine C line screen at 20% duty, pitch 0.05. | Fresnel rim above 0.55 is C60 K35 and acts as the outline. Two prop-space diagonal glint bands knocked to paper. Writes id 0, which means "no ink lines". | `MeshPhysicalMaterial` transmission 1, roughness 0.05, ior 1.5, thickness 0.5 |
| **Felt / fabric** | shadeK 0.36, grain, pitch 0.06, termSoft 0.35 | Fuzz rim: fresnel above 0.6 knocks 0.30 through grain. Silhouette is hash-discarded above fresnel 0.85. Id 0, so soft things have no keyline. Weave: crosshatch knock 0.08, pitch 0.04. | `MeshPhysicalMaterial` roughness 1, sheen 1, sheenRoughness 0.8 |
| **Cardboard / paper** | Kraft is B45 A12 K10; white card is ink 0. shadeK 0.30, flat, termSoft 0. | Corrugation: K zigzag, pitch 0.08, on faces where `abs(dot(Nobj,uCorrAxis)) > 0.9`. Fibre flecks: hash above 0.985 gives K 0.25, pitch 0.02. | `MeshStandardMaterial` roughness 0.9 |
| **Sponge** | shadeK 0.35, grain, pitch 0.05, termSoft 0.25 | Pores: prop-space Worley at pitch 0.14 and 0.05; pore interior adds K 0.55. Pores are discarded above fresnel 0.7 for a bumpy silhouette. | `MeshStandardMaterial` roughness 1 |
| **Feather** | Ink at 35% tint, no shade, `side: DoubleSide` | Barbs: UV line screen at ±35° from the rachis, 40 lines, duty 0.5. Serrated edge by discard: `abs(u) > edge(v) − 0.06*tri(v*40)`. Rachis: 0.03-wide paper knock-out. | `MeshStandardMaterial` roughness 0.8, `alphaTest` 0.5 |

Cast shadows falling on a toy are drawn with that toy's own screen, never dots.

## 4. Lighting rig

Shading is banded in ink space, so the lights supply a direction and shadow maps, not colour.

- **Shadow type:** `renderer.shadowMap.type = THREE.PCFShadowMap`. In r186 `PCFSoftShadowMap` is removed. PCF is hardware `sampler2DShadow` with a 5-tap rotated Vogel disc; keep the radius at 1.5 or below, or the rotation noise stipples the edge.
- **Key direction (towards the light):** `normalize(−0.45, 0.85, 0.30)`, elevation 57.5°. It is steep on purpose so a shadow sits close to its footprint and reads as size.
- **Near light (`DirectionalLight`):**
  - Map 2048², ortho ±24u, giving 0.023 u/texel.
  - Focus is camera position plus yaw-forward × 14, snapped to the texel grid.
  - `bias −0.0004`, `normalBias 0.03`, `radius 1.0`, updated every frame.
- **Room light (`DirectionalLight`, same direction):**
  - Map 2048², ortho ±165u.
  - `bias −0.0012`, `normalBias 0.25`, `radius 1.5`, `shadow.autoUpdate = false`.
  - Set `needsUpdate` on `level:loaded`, while any awake prop or mover is outside the near box, and on `prop:drop`.
- **Map selection:** the shader uses the near map when its shadow coordinate is inside 0.04–0.96 and blends to the room map across the outer 4%.
- **Shadow ink on room surfaces:** cast shadow is `K = mix(0.26, 0.85, dotScreen(q, 0.45))`. The flat 26% guarantees that a shadow smaller than one dot still reads. Form shade is `mix(0.18, 0.60, dotScreen(q, 0.30))`.
- **Contact dabs:** `uniform vec4 uDab[8]` holds base centre and radius for the 8 nearest props with bounding radius under 0.4u. Room surfaces within one radius below get `K = max(K, 0.5*(1−smoothstep(.7r, r, d)))`. Shrunk toys keep a size-true contact mark below shadow-map resolution.
- **Under-tone:** `uUnderK` in the shader replaces a hemisphere light. A real `HemisphereLight(#FFF4DC, #B9A7FF, 0.9)` and sun intensity 2.6 exist only for `?plain`.
- **Sun patch:** an analytic window gobo adds +22% Sun ink where a point, projected along the light direction, lands inside the window panes.
- **Lamp pools:** up to two `(position, radius)` uniforms, used by the night preset.
- **Sticker light:** held props use a fixed view-space `L = normalize(−0.5, 0.7, 0.5)`.

## 5. Environment kit

Scale: the figure is about 4cm tall, so 1u ≈ 2.35cm and a 7m room is 300u. Everything is render-only backdrop outside the level's colliders. Each piece is one merged mesh of 12 primitives or fewer, so one draw call each.

- **Shell:** 320×320 floor, walls to y=112.
- **Skirting board:** 5u tall, 0.7u deep. It is three times the player's height and is the single best "you are tiny" cue.
- **Door:** 36×85u with a dark 0.4u gap beneath.
- **Socket plate:** 3.5u square at y=13.
- **Floors** (procedural, world space, in the room variant of InkMaterial):
  - `planks`: 5×60u, staggered, 0.08u K gaps.
  - `lino`: 12u checker, C22 against paper.
  - `tiles`: 6u squares, 0.25u K12 grout.
  - `carpet`: coarse grain, no keylines.
  - `kraft`: corrugation plus tape strips.
  - `rug`: an island 0.5u thick with B/C border stripes.
  - These modules are the absolute scale ruler, because the dots are fractal.
- **Walls:** flat tint plus a 14u-period wallpaper motif at +8% coverage.
- **Furniture silhouettes:**
  - Bed 38×21×85u with a scalloped blanket.
  - Table: top 60×2×35u at y=32, lathe legs r=1.5u.
  - Chair: seat at y=19.
  - Bookshelf 40×85×12u with instanced spines.
  - Toy chest 34×20×20u.
  - Radiator.
- **Paper fade (aerial perspective):**
  - Coverage × `1 − 0.55*smoothstep(40, 260, dist)`.
  - Add `0.10*smoothstep(60, 260, dist)` Cool ink.
  - K tone × `1 − 0.35*smoothstep(60, 260, dist)`.
  - Far furniture therefore becomes pale outlined shapes, like a background illustration.
- **Window:** 50×55u opening, sill at y=38, 2u frame with a cross mullion. The sky is a direction-space shader, so it has no parallax: `q = vec2(atan(d.x,−d.z), asin(d.y))*24` drives a C dot gradient that grows upward, three knocked-out cloud blobs, and a B100 sun disc.

Presets (`LevelDef.theme`), with a suggested level mapping:

| Theme | Paper / A / B / C / K | Floor and backdrop | Special | Levels |
|---|---|---|---|---|
| `playroom-morning` | `#FFF4DC` `#FF4F9A` `#FFD21F` `#1FA2FF` `#2A1F4E` | Planks and rug; bed, toy chest, window | Sun patch on | 0, 1–3 |
| `kitchen-lino` | `#FFF9E8` `#FF5A36` `#FFC61A` `#19B8A6` `#3B2416` | Lino; table, chair legs | Checker floor is the ruler | 4–5 |
| `bath-tiles` | `#F2FBFF` `#FF6F61` `#FFE14D` `#00B4D8` `#0F3554` | Tiles; tub wall, radiator | Water gadgets live here | 6–7 |
| `cardboard-attic` | `#E9CFA3` `#F2452E` `#FFB000` `#008C95` `#2B1A12` | Kraft; box stacks, skylight | Printed on kraft stock, everything earthy | 8–10 |
| `garden-sill` | `#F7FFE9` `#FF7A1A` `#D9E021` `#00A86B` `#16302B` | Painted sill; pots as giant trees | Sun at 35° elevation, long shadows | 11–12 |
| `bedtime-nightlight` | `#F3EEFF` `#FF7A3D` `#FFD84D` `#5145E0` `#1A1238` | Carpet; bed, night-light | Night wash: +C50 and K 0.15 dots everywhere, knocked out inside lamp pools | 13–15 |

## 6. Post-processing

Order:

1. **Shadow maps.** Near every frame; room on demand.
2. **Plates pass.** World layer into `new THREE.WebGLRenderTarget(w, h, { count: 2, depthTexture })`, RGBA8, no colour space. Clear plates to 0 and aux to (0.5, 0.5, 0, 0).
3. **Held pass.** `renderer.clearDepth()`, then render the held layer into the same target.
4. **Press pass.** One fullscreen triangle, output already sRGB-encoded (no `colorspace_fragment`).
5. **FXAA** (`three/addons/postprocessing/FXAAPass.js`).
6. **DOM UI.**

No bloom, SSAO, depth of field or scene fog exists anywhere.

Press pass, with `s = height/1080`:

1. **Paper tooth.** Offset the sample UV by `(noise.rg − 0.5) × 1.2s` px.
2. **Keyline.** Centre plus four diagonal taps at ±0.875s px on aux and depth, giving a 1.75s px line. An edge is any of:
   - ids differ and neither is 0;
   - same id and `1 − dot(n0, n1) > 0.30`;
   - same id and `abs(Laplacian(1/z)) * z > 0.02`. This is exactly zero on planes, so there are no false lines on grazing floors.
3. **Sticker border.** 8-tap ring at 4s px (5s when held) on aux alpha. Where a neighbour has sticker above 0.3, the centre does not, and the neighbour is nearer, coverage is set to paper.
4. **Peel shadow** (held only). The same mask offset (+5, −6)s px, flat K 0.30.
5. **Ghost.** `stk(uv − ghostOff) > 0.1 && stk(uv) < 0.1` gives `A = max(A, 0.9)`.
6. **Plates.** A at offset 0; B at (+0.9, +0.4)s px; C at (−0.6, +0.8)s px; K at 0. Static global misregistration.
7. **Mottle.** Coverage × `0.90 + 0.10 * noise2`.
8. **Composite.** `col = paper; col *= mix(1, inkA, a); …; col *= mix(1, inkK, max(k, edge))`.
9. **Grain.** `col *= 1 − 0.06 * grain`.
10. **Page margin.** A 13s px unprinted border with a 2px noisy ink edge and corner crop marks. A "Full bleed" setting turns it off.

| | Low | Medium | High |
|---|---|---|---|
| Plates resolution | 0.75× | 1.0× | 1.25× |
| Press pass fetches | 11 | 24 | 25 |
| Global plate misregistration | off | on | on |
| Depth edges | off (id and crease only) | on | on |
| Sticker ring | 4-tap | 8-tap | 8-tap |
| Paper tooth and mottle | hash grain only | mottle | both |
| FXAA | off | on | on |
| Shadow maps | 1024 near (±20u), 1024 room | 2048, 2048 | 2048, 2048, room refresh unthrottled |
| Particles | 40 | 120 | 300 |

- Ghost, boil, border, line/dot/grain screens and page margin are on every tier, so Low is a softer print, not a different game.
- Dynamic resolution: if the 60-frame average exceeds 18ms, drop plates scale by 0.1 (floor 0.6); restore below 13ms.

**Keeping the held prop from betraying distance.** The engine places it at `eye + dir·d` with scale `k·d`, which is a homothety centred on the eye. The projected image, N·V, reflection vectors, `fwidth` LODs and prop-space screen pitch are therefore mathematically identical at every `d`. Only things that sample the world at the prop's position can leak, and `uState.held = 1` turns all of them off:

| Leak | Handling while held |
|---|---|
| Cast shadow | `castShadow = false`. The shadow leaves the page on grab. |
| Received shadow, sun patch, lamp pools, night wash, dabs, pulses | Skipped in the shader. |
| World light | Replaced by the view-space sticker light. |
| Paper fade and cool wash | Forced to 0. |
| Occlusion and intersection | Drawn after `clearDepth()`, so always on top. The projection march guarantees nothing solid is in front. |
| Outline | Comes from the id buffer at constant pixels. Depth edges are relative (Δz/z), so they are invariant. |
| Ghost, border, peel shadow, misregistration | All constant-pixel screen space. |
| Dots | Never on toys. The line screen is prop space, so its on-screen pitch is constant. |

The image gives no depth cue at all. The proportion ruler and the hold note (sections 7 and 9) are the designed readouts.

## 7. Mechanic feedback

- **Hover.** Border peels on (0 to 4px, 90ms). The reticle ring grows from 14 to 22px and its centre dot fills with Hot ink.
- **Grab (120ms).**
  - Border goes to 5px and the peel shadow appears.
  - The world shadow vanishes.
  - Lighting blends to the sticker light.
  - Boil rate doubles.
- **Hold.** The image is completely stable; only the ruler marker and the note change when the prop jumps surfaces.
- **Release, the three-pass print (180ms).**
  - Border snaps to 0 in 70ms.
  - `uState.print` gates the plates in sequence: paper silhouette and keyline at 0ms, Sun ink at 45ms, Hot at 90ms, Cool and Key at 135ms.
  - The shadow returns, and paper fade lerps in over 250ms.
- **True-scale reveal.**
  - *Ping.* A spherical shell centred on the prop expands to 3× its bounding radius in 0.35s (shell width 0.25× radius). Room dots inside the shell swell by +0.35 tone. Ring size is the prop's real size, drawn on real surfaces at real depth.
  - *Crop marks.* Four corner marks at the prop's screen bounds with a DM Mono label such as "×2.40"; hold 0.9s, fade 0.3s.
  - *First impact.* 4–40 paper chads (hole-punch discs, count scales with mass^⅓), a second ping, and camera shake up to 0.25°.
  - Afterwards the fat or fine line screen stays as a persistent tell.
  - Impact needs a `prop:impact` event from the engine. If it does not exist, the render layer derives it from a frame-to-frame velocity delta.
- **Proportion ruler (HUD, only while holding).** A 320×28px sticker at bottom centre.
  - Log₂ scale from 1/16× to 16×, 40px per octave, ticks at ¼, ½, 1, 2, 4.
  - Solid Hot triangle is the current projected scale (60ms tween). Hollow K triangle is the scale at grab.
  - Left: percentage in DM Mono 18px ("240%").
  - Right: a 16px figure pictogram beside a bar showing prop height in player-heights, with a notch at the 1.3u jump apex.
- **Level complete (2.2s).**
  - Time slows to 0.4× for 0.6s.
  - All boil stops and global misregistration animates to 0, so the page is finally in register.
  - Chads and streamers in A/B/C burst from the exit.
  - A four-swatch colour bar slides along the margin.
  - "PRINTED" is rubber-stamped in Hot ink at −7° with a thud.
  - A page-turn wipe (diagonal fold line with a K shadow band, done in the press pass) reveals the next preset's paper colour.

## 8. UI

DOM and CSS only, over the canvas. Overprint is done with `mix-blend-mode: multiply`.

- **Type.**
  - Bricolage Grotesque, `@fontsource-variable/bricolage-grotesque`. Titles at weight 800, width 75, caps, tracking −1.5%. Body at weight 500, width 100.
  - DM Mono 500, `@fontsource/dm-mono`, for numerals, page numbers and printer's marks.
- **Controls.** Every control is a sticker: paper fill, 2px K keyline, 3px white die-cut edge, a Hot-ink shadow offset (3px, 3px).
  - Hover: the sticker lifts 3px and its shadow boils.
  - Press: scale 0.96 and shadow offset 0, a stamp.
- **HUD.**
  - Registration-mark reticle (circle and cross, K with a 1px paper halo). While holding it splits into four crop corners at 28px.
  - Storybook caption strip, bottom-left in the margin: title and blurb for 5s. Hints arrive as margin notes.
  - "p. 03 / 15" bottom-right in DM Mono.
  - Key-glyph stickers (E, Q, F) beside the ruler while holding.
- **Pause.** Rendering stops, so GPU cost is zero. The frozen frame becomes a sheet (scale 0.86, rotate −2°, 240ms) on a K90 light table. A column of stickers on the right: Resume, Restart page, Hint, Settings, Contents.
- **Level select ("Contents").** A 5×3 contact sheet, one row per phase. Each page is in its own preset's paper and inks with the hero toy's silhouette.
  - Locked: keyline only, no colour plates.
  - Done: an "OK" stamp in Hot ink, rotated ±6° (seeded).
- **Title.** The sandbox autoplay bot plays behind the wordmark. "TINKER'S TOYBOX" is three overprinted plate copies that slide in from three directions and land slightly out of register with a stamp. "PRESS ANY KEY" in DM Mono blinks at 1Hz.

## 9. Audio

- **Master.**
  - Compressor: −14dB threshold, 4:1, 5ms attack, 180ms release.
  - Reverb: convolver with a generated impulse, noise × `exp(−t/0.35)`, 1.4s long, low-passed at 3.5kHz. Sends: SFX 0.18, music 0.30.
- **Size to pitch.** `semis = clamp(−6*log2(scale), −24, 24)`, quantised to the level's pentatonic scale. Big is low, small is high.
- **Determinism.** Music uses its own PRNG seeded by level id, never `game.rng`.

| SFX | Recipe |
|---|---|
| **Grab** | Peel: noise through a band-pass (Q 1.2) swept 600→3800Hz over 110ms, gain 0.25. Then a kalimba pluck at the size note: FM ratio 3.01, index 2.5→0 in 60ms, decay τ 180ms. |
| **Hold** | Two triangles at ±4 cents through a low-pass at `clamp(1800*scale^−0.3, 300, 3000)`Hz, gain 0.05, 5Hz tremolo. Each quantised pitch change glides (τ 30ms) and fires a soft pluck, so surface jumps play as notes. |
| **Release** | Stamp: sine 160→48Hz over 140ms, gain `clamp(0.4 + 0.2*log2(scale), 0.2, 1)`. Paper slap: high-passed noise at 1.8kHz, 35ms. Size note: FM ratio 1.41, τ 250ms × scale^0.25. |
| **Land** | Thump at `f0 = clamp(220*mass^(−1/3), 45, 900)`Hz falling to 0.6·f0 over 90ms; gain `J/(J+J0)*0.6` for impulse J. Material flavour is added on top (table below). |
| **Button** | Down: 620Hz sine, τ 30ms, plus a 5ms click. Up: 780Hz. |
| **Level complete** | Page-turn noise sweep 300→2400→500Hz over 600ms; stamp; five ascending pentatonic plucks 90ms apart; a root, fifth and ninth chord with 1.8s decay. |

Land flavour by material:

| Material | Added layer |
|---|---|
| Wood | Partials at 2.76× and 5.4× f0, τ 40ms |
| Rubber | Pitch bends up to 1.5·f0, τ 200ms |
| Plastic | 2.4kHz band-passed click |
| Metal | Partials at 1, 2.32, 4.25, 6.63 × 4·f0, τ 600ms |
| Glass | Partials at 1, 2.7, 5.2 × 1800Hz, τ 350ms |
| Felt, sponge | 400Hz low-passed noise only |
| Cardboard | 700Hz band-passed noise, Q 2, τ 50ms |
| Feather | Silent |

**Music** is a music box in a paper room.

- **Clock.** Lookahead scheduler: 25ms tick, 120ms horizon, 58% swing. Tempo 84–108bpm, with key and mode set per preset.
- **Voices.**
  - FM kalimba lead: a constrained pentatonic random walk as a 2-bar motif with one mutation per repeat.
  - Triangle pad playing dyads.
  - Paper percussion: noise shaker and wood-block tock.
  - Sine bass.
- **Adaptive.**
  - Percussion, then bass, enter as triggers are solved.
  - While holding, the music low-passes to 1.4kHz and the lead rests. The hold drone, in the same scale, becomes the melody, so the player plays the tune by sweeping the prop across surfaces.

## 10. Risks

| Hardest part | Why | Fallback |
|---|---|---|
| Ink-space pipeline | Everything must be an InkMaterial. VFX must be authored as coverage. There is no blending, so glass is screen-door. | Conventional RGB toon materials coloured by a CPU `inkToRGB`, plus an override normal/id pass for keyline, ghost and grain. Loses true overprint and per-plate fringes. |
| Contact precision | Near map is 0.023 u/texel; bias gaps show on small props. | Contact dabs are already specified. If the two-map scheme slips, use the `SunLight` addon (`three/addons/lights/SunLight.js`, two cascades, 2048, far 160), which gives roughly 0.064 u/texel near the camera. |
| Giant line screens | A ×40 toy has zebra-wide shade lines. This is intended and may be too loud. | Octave-hop the line screen like the dots. That loses the enlargement tell; the ruler and ping remain. |
| Fractal dots crawling | Dots subdivide as you walk. | Fix the pitch at 0.1u and go flat beyond about 15u. |
| Post outlines | 8-bit id collisions and thin far lines shimmer. | FXAA is already in the stack; raise the depth-edge threshold to 0.03. |
| Ghost legibility | Hot against Cool can be weak for some colour-vision deficiencies. | Boil and the hover border are hue-independent. Add a settings toggle for a K ghost. |
| Per-object uniforms on shared materials | Depends on `onBeforeRender` ordering. | One material instance per prop. |

**GPU cost** at 1080p, Medium tier. These are estimates from the shader and fetch counts, not measurements.

| Stage | Iris Xe class | UHD 620 class |
|---|---|---|
| Near shadow map (≤150 draws) | 0.7ms | 1.5ms |
| Room shadow map (only when dirty) | 0–1ms | 0–2ms |
| Plates MRT (≤350 draws, ~2.5× overdraw, ALU-only shader) | 2ms | 4ms |
| Press pass (24 fetches) | 1ms | 2.5ms |
| FXAA | 0.5ms | 1ms |
| **Total** | **about 4–5ms** | **about 9–11ms** |

- Low tier on UHD 620 should land around 5–7ms.
- High tier (1.25× plates) is for Xe and above, at roughly 7–8ms.
- The first thing to measure is MRT bandwidth on UHD 620; it is the number most likely to be wrong.
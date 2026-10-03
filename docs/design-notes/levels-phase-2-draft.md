# Phase 2 — Physics & Momentum (Levels 5–9): level design spec

**Conventions.** Unity, left-handed, Y-up, yaw 0 looks down +Z, positive yaw turns right, positive pitch looks up, degrees. All positions are `(X, Y, Z)`; player positions are feet. API names follow `docs/ARCHITECTURE.md` and the engine source as of 2026-10-03 (`LevelContext`, `PropOptions`, `Mover`, `Trigger`, `Volume`, `Bot`).

**Status.** Every number is hand-calculated; nothing has been run in the engine. Aim points and thresholds are starting values for the bot tests to tune. Each level ends with an honest physics-confidence note and, where confidence is not high, a simpler fallback.

**Symbols.** `g` = grab distance (eye to prop center), `k` = scale / g, `d` = hold distance at release, `s` = resulting scale = k·d, `D` = horizontal distance from the eye to a backstop surface.

## 0. Shared rules, engine requests and gadgets

### 0.1 Design rules carried over from Phase 1

- A held prop stops at the **first** overlap on the way out from the eye. Every "grow" step below therefore aims at a **backstop** (a wall or tower behind the target) or is done from a **raised vantage**. No step asks the player to slide a thick prop along a floor at a grazing angle.
- When a prop is stopped by a backstop of horizontal distance `D`, with half-depth `h·s` toward the backstop, `d·(cos p + h·k) = D`, so **s = k·D / (cos p + h·k)**. Stepping back grows it, stepping forward shrinks it, and pitch barely matters. This is the tuning move of Levels 6, 7 and 9.
- Per-prop `MaxScale` is used as a design tool: it blocks degenerate solutions and makes "too far back" harmless.

### 0.2 The Phase 2 principle: scripted integration, honest rules

Every exotic interaction (wind raft, trampoline, catapult, funnel plate, train carry) is a **deterministic gadget** that takes over a body kinematically or sets a velocity directly. The *rule* each gadget applies is a real formula of scale (area ∝ s², mass ∝ s³, thickness ∝ s), stated in the level, so the puzzle logic is honest even though PhysX is not asked to produce the result emergently.

### 0.3 Engine requests (flagged, not assumed)

1. **`Prop.BeginDrive()` / `EndDrive()`** — lets a gadget take a Dynamic prop kinematic and move it through a `Mover` (so `Player` ground velocity and `Mover.PointVelocity` work), then hand it back to physics at rest. Needed by `SailRaft` (L5), `WeightPlate` latch (L8) and `PropCarrier` (L9). Grabbing a driven prop must call the owner's `OnGrabbed` so it can let go.
2. **Riders.** Gadgets detect riders with `player.Grounded && player.GroundProp == prop` (props) or `player.GroundCollider` (movers). Launches use `player.SetVelocity`, which already leaves the ground when the upward speed exceeds 1.5.
3. **Walk-on colliders.** The capsule cannot ride over a vertical lip taller than about 0.1. Props meant to be boarded (feather, eraser, ruler, plank) have chamfered or tapered colliders, specified per level.
4. **`SnapUprightOnGrab`** from Phase 1 is assumed for the eraser and the plank.
5. **Airborne `WalkTo`.** The bot's `WalkTo` steers in the air (it only writes move input), which the solvers of Levels 6 and 7 rely on. Keep that behavior.
6. **Gadget order.** Gadgets tick in creation order inside `ctx.OnUpdate`, before `Physics.Simulate`. They use tick counts and `game.Time`, never RNG.

### 0.4 Gadgets reused from Phase 1

`SkyCap`, `PropLeash`, `HazardZone`, `PressureButton` as specified there. The exit is `ctx.AddExit(position, size)`; a locked exit is `exit.Locked = true` until a signal clears it.

### 0.5 New gadgets in this phase (full specs inside each level)

| Gadget | Level | One-line contract |
|---|---|---|
| `WindStream` | 5 | Oriented box of moving air: blows light props, feeds `SailRaft`. |
| `SailRaft` | 5 | A flat `sail` prop that is big enough carries a rider along the stream. |
| `BouncePad` | 6 | Landing on a `bouncy` prop launches the player to an apex proportional to the prop's scale. |
| `Seesaw` | 7 | Kinematic lever; a mass landing on one pad launches riders on the other by the lever formula. |
| `Funnel` + `WeightPlate` + `ReturnPort` | 8 | Size gate above, minimum mass below, rejects are returned to the room. |
| `FunnelGauge` | 8 | Live too-small / good / too-big lamp for the held marble. |
| `Train` | 9 | Kinematic cars on a circular track at constant angular speed. |
| `PropCarrier` | 9 | Glues a supported prop to a moving car so it rides and carries the player. |

### 0.6 Held-prop presentation note (for art)

Same rule as Phase 1: while held, no cast shadow, no fog, no DOF, constant-pixel outline. Additions for this phase:
- **Wind (L5).** Streamers and dust in the `WindStream` are world effects and may pass in front of or behind the held feather, but the held feather's barbs do **not** react to the wind (it is not in the world yet). They start rippling 0.2 s after release, together with the shadow fade-in.
- **Gauge lamps (L8)** are world objects; they are the sanctioned depth cue for the held marble.
- **Moving shadows (L9).** The captured plank's shadow on the pit wall is the cue that it is now part of the train.

---

## Level 5 — The Fan and the Feather

**A. Setting.** `lamp-desk`: a desk blotter that ends at a canyon between two desks, with a desk fan the size of a ferris wheel roaring behind it. *Fantasy: lay a craft feather in the gale, step on the quill, and surf it across the gap.*

**B. Layout** (blotter top Y=0, the wind and the crossing run along +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Deck A (blotter) | −9..7 | −6..18 | top 0 |
| Fan guard (static disc, radius 5.5, hub at (−1, 5, −6)) | | plane Z=−6 | acts as the back wall |
| Balcony B (stack of sticky notes, start) | 7..15 | −4..10 | top 3 |
| Apron (calm strip) | 7..15 | 10..18 | top 0 |
| Leaning-ruler ramp, balcony → apron, 26.6° | 12..15 | 10..16 | 3 → 0 |
| Canyon | all | 18..42 | no floor; `KillY` = −30 |
| Deck C (far desk) | −9..7 | 42..58 | top 0 |
| Walls, height 12 | X=−9 and X=15 (X=7 beside deck C), Z=58 | | |
| `SkyCap` | everything | | Y=12 |
| Spool pedestal (radius 0.4) | 11 | 2 | 3..4.1 |

- The canyon is 24 wide. The balcony face at X=7 is a sheer 3-unit step down to deck A; the ramp is the way down and back up.
- Spawn (11, 3, −2.5), yaw 0, checkpoint at spawn. Exit box (4 × 3 × 3) centered (−1, 1.5, 55).
- The balcony is outside the wind. The player looks down and to the left onto deck A.

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Craft feather | Lozenge 1.0 long (Z) × 0.4 wide (X); 0.03 thick along the quill tapering to 0.004 at the outline; one convex collider; plan area 0.30 | (11, 4.12, 2), scale 1.0, quill along +Z | density 1.5 (mass 0.0054·s³), friction 0.6, clamps 0.5–12, tag `sail`, grabbable |

The taper makes the edge at most 0.05 high at s=12, so the player walks onto it without jumping.

**D. Intended solution.**
1. Walk to (11, 3, 0.7) and grab. Eye (11, 4.55, 0.7), feather center 1.3 ahead and 0.43 below: g = √(1.3² + 0.43²) = 1.37, k = 1.0 / 1.37 = **0.73**.
2. Walk to the balcony edge at (7.6, 3, 4). Eye (7.6, 4.55, 4).
3. Aim at the blotter at (−1, 0.15, 12). The offset is (−8.6, −4.4, 8), so d = √(73.96 + 19.36 + 64) = **12.5**, pitch −20.5°, yaw −47°.
4. Release. s = 0.73 × 12.5 = **9.2**. The feather is 9.2 × 3.7, lying flat inside the stream with its footprint X −5.6..3.6, Z 7.5..16.5. Mass 4.2. It settles, the gadget moors it, and its barbs start to ripple.
5. Walk down the ramp, across the apron, and onto the feather. After 0.6 s on board the quill bows, the vane cups, and the feather lifts.
6. Ride: about 37 units at 6 u/s, 7 s. It settles on deck C centered at Z=49. Step off and exit.

Cone check for step 3: from the eye the balcony edge is 68.8° below horizontal and the nearest corner of the feather is 34.5° below, so the projection clears the edge. Standing more than about 3 back from the edge puts the edge in the way and the feather lands on the balcony instead, visibly.

**E. Tolerances.**
- Works for s from **6.5 to 12** (the clamp). With k = 0.73 that is any drop distance d ≥ 8.9; from the balcony edge almost the whole blotter qualifies (the nearest stream point is d = 5.2, s = 3.8).
- Placement: feather center anywhere in the launch volume (X −6..4, Z 2..16) and flat. The glide re-centers it.
- 2.5 ≤ s < 6.5: it lies there; when boarded it cups, lifts 0.3, shudders and flops back (`SailStalled`). Step off, re-grab, aim farther.
- s < 2.5: the wind whisks it across the blotter and over the canyon; it falls and respawns on the spool.
- Dropped outside the stream (apron, balcony): it is an ordinary prop. Re-grab.
- Player falls off mid-ride: player respawns at the checkpoint; the unridden feather goes flat, sinks into the canyon and respawns on the spool at scale 1. Cost: redo steps 1–4.

**F. Unintended solutions.**
- Feather as a bridge: longest is 12; a sprint jump from an overhanging tip adds at most 6 + 5.5 = 11.5. The gap is 24.
- Riding a small feather as it is blown away: the blow-away force applies only while nobody stands on it, and below s = 2.5 it is at most 1.0 wide and already gone.
- Wind-assisted jump: the stream gives the airborne player 3 u/s² along +Z, worth about 1.5 units on a jump. Still far under 24.
- Projecting the feather onto deck C: the clamp stops it at d = 12 / 0.73 = 16.4, in mid-air over deck A or the canyon. It drops; no gain.

**G. Win, gadgets, bot.** Win = exit on deck C.

`WindStream`
- Inputs: oriented box (X −6..4, Y 0..9, Z −6..44), direction +Z, `speed` 9.
- Player: +3 u/s² along the direction while airborne and inside; nothing while grounded (camera sway and audio only).
- Dynamic props inside that are not handled by a `SailRaft` and weigh under 1: acceleration `min(40, 12 / s)` along the direction.
- Output: `Contains(point)`. Presentation draws streamers from the same box.

`SailRaft` (one per `sail` prop; uses `Prop.BeginDrive`)
- Parameters: `stream`, launch volume (X −6..4, Z 2..16, Y 0..1.5), `area` 0.30, `liftPressure` 7.78, `blowAwayBelow` 2.5, `boardDelay` 0.6 s, `cruiseSpeed` 6, `cruiseHeight` 1.2 (underside above the deck), landing Z 49, axis X −1.
- Lift rule: `capacity(s) = liftPressure · area · s² / gravity − mass(s)`; it flies when capacity ≥ rider mass (3). With these constants that is s ≥ **6.5**; the upper root is about 18.5, beyond the clamp.
- States:
  - `Loose`: physics. In the stream with s < 2.5 and no rider: acceleration 40 / s along the wind (it leaves).
  - `Moored`: not held, center in the launch volume, up-axis within 20° of vertical, speed < 0.2 for 0.3 s, s ≥ 2.5. The prop is driven and pinned, so it is a stable floor. Still grabbable; a grab returns it to `Loose`.
  - `Stall`: rider on board for `boardDelay` with capacity < 3. Rise 0.3 and fall back over 1.0 s, raise `SailStalled(capacity / 3)`, return to `Moored`.
  - `Glide`: rider on board for `boardDelay` with capacity ≥ 3. Not grabbable. Rise to cruise height over 1.0 s (smoothstep); accelerate to cruise speed at 6 u/s²; ease X toward the axis over the first 8 units; decelerate to stop at the landing Z; descend over 0.8 s. Position is a pure function of ticks since launch. If there is no rider for 1.0 s: sink at 8 u/s until the kill plane.
  - `Docked`: at rest on deck C; `EndDrive`, grabbable again.
- Events: `SailMoored`, `SailStalled`, `SailLaunched`, `SailDocked`.

```csharp
yield return bot.WalkTo(new Vector3(11, 3, 0.7f));
yield return bot.Grab(feather);
yield return bot.WalkTo(new Vector3(7.6f, 3, 4));
yield return bot.DropAt(new Vector3(-1, 0.15f, 12));
yield return bot.Until(() => raft.State == SailState.Moored, 4f);
yield return bot.WalkTo(new Vector3(13.5f, 3, 9.5f));
yield return bot.WalkTo(new Vector3(13.5f, 0, 16.6f));     // down the ramp
yield return bot.WalkTo(new Vector3(6, 0, 15));
yield return bot.WalkTo(new Vector3(-1, 0, 12));             // onto the quill
yield return bot.Until(() => raft.State == SailState.Docked, 16f);
yield return bot.WalkTo(new Vector3(-1, 0, 55));
```

**H. Teaching.**
- Blurb: "The wind only carries what it can catch."
- Hint 1: "Drop the little feather in the wind and watch where it goes. Now imagine standing on it."
- Hint 2: "A feather has to be much wider than you before the wind can lift you both. From up here the blotter is a long way down, and far means big."
- Hint 3: "From the balcony edge, aim the feather at the middle of the blotter and let go. Walk down, stand on the quill, and hold on."

**I. Wow beats.**
- Boarding: the quill bows under your weight, a hundred barbs lift and comb the wind like a wheat field, and the deck drops away.
- Mid-canyon: the feather's shadow slides off the blotter, vanishes into the depth, and reappears racing up the far desk to meet you.

**Physics confidence.** High; the ride is a scripted mover and the only real physics is a flat prop settling on a flat deck. If `Prop.BeginDrive` is not available, parent the feather visual to a separate invisible `AddKinematic` platform of the same footprint for the glide.

---

## Level 6 — Bouncing Eraser

**A. Setting.** `golden-boards`: the floor of a cubby under a tall storage cabinet whose top is the only way on. *Fantasy: turn a pink eraser into a trampoline the size of a bus and bounce onto the roof.*

**B. Layout** (floor Y=0, the cabinet is at +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Floor | −12..12 | −10..18 | top 0 |
| Cabinet face (sheer, no lip or overhang at the top edge) | −12..12 | plane Z=18 | 0..14 |
| Cabinet top (goal shelf) | −12..12 | 18..30 | top 14 |
| Walls, height 26 | X=±12, Z=−10, Z=30 | | |
| `SkyCap` | everything | | Y=26 |
| Spool pedestal (radius 0.4) | −6 | 9 | 0..1.1 |

- Spawn (0, 0, −6), yaw 0, checkpoint at spawn. Exit box (3 × 2.5 × 3) centered (0, 15.25, 27).
- The cabinet top is 14 up; a jump reaches 1.3.

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Pink eraser | Trapezoid prism: bottom face 1.5 long (X), top face 0.9 long, 0.5 deep (Z), 0.25 thick (Y). Both ends are 39.8° ramps (run 0.3, rise 0.25). One convex collider, volume 0.15. | (−6, 1.23, 9), scale 0.8, long axis along X | density 1.2 (mass 0.18·s³), friction 0.9, bounciness 0.3, clamps 0.4–**10.5**, tags `bouncy`, `SnapUprightOnGrab`, grabbable |

The beveled ends are walkable ramps at any scale, which is how the player gets on top of a 2.4-high slab.

**D. Intended solution.**
1. Walk to (−6, 0, 7.9), face +Z, grab. g = √(1.1² + 0.35²) = 1.15, k = 0.8 / 1.15 = **0.693**. The eraser's long axis is across the view.
2. Walk back to the middle of the room at (0, 0, 2). Eye (0, 1.55, 2); the cabinet face is D = 16 away.
3. Aim at the cabinet face a little above eye level, at (0, 3.5, 18): pitch +6.9°. The eraser stops when its far edge meets the face. Its half-depth is 0.25·s, so d·(cos 6.9° + 0.25 × 0.693) = 16, d = 16 / 1.166 = **13.7**.
4. Release. s = 0.693 × 13.7 = **9.5**. The slab is 14.3 long at the bottom, 8.6 on top, 4.8 deep, 2.4 thick, mass 155. It was hanging 2.0 above the floor and lands flat against the cabinet, occupying X −7.1..7.1, Z 13.2..18.
5. Walk around the left end and up its ramp to the top (Y=2.4). Stand about 1.2 from the cabinet face.
6. Jump. Landing on the top face at 7.6 u/s triggers the bounce: launch speed √(2 × 22 × 1.75 × 9.5) = 27.1 u/s, apex 16.6 above the top, **19.0** above the floor.
7. Hold forward. The capsule slides up the cabinet face, clears the edge at 14 after 0.55 s, and has about 1.35 s of air time above the shelf: it lands roughly 5 units in. Exit.

**E. Tolerances.**
- The apex above the floor is 0.25·s + 1.75·s = **2.0·s**. Comfortable for s ≥ 7.5 (apex 15); hard minimum about 7.2. The clamp is 10.5 (apex 21).
- In standing distance: s = 0.594·D for this grab, so D ≥ 12.7 (stand at Z ≤ 5.3). Beyond D = 17.7 the clamp takes over and the eraser hangs short of the cabinet; it still works, because a 21-high bounce gives 2 s of air time and about 9 units of drift.
- Long axis along the view instead of across (grabbed from the side): the half-depth is 0.75·s, so s = 0.456·D. Step back further; it reaches 7.5 at D = 16.5. The ramp then faces the player.
- The intended first failure: grabbing and looking at the cabinet without stepping back (D = 10.1) gives s = 6.0 and an apex of 12.0, two units short. Stepping back while holding visibly grows it.
- Recovery: step off (a prop underfoot cannot be grabbed), grab, walk backward, release.
- Upside down (both ends overhang): press `F` twice, or re-grab; `SnapUprightOnGrab` keeps it on a broad face.

**F. Unintended solutions.**
- Leaning the eraser against the cabinet as a ramp: the longest edge is 1.5 × 10.5 = 15.75, so reaching 14 needs a 62.7° lean. Not walkable (limit 50°). This is why the clamp is 10.5.
- Standing it on end as a tower: 15.75 tall but sheer. No.
- Step plus jump: top at 2.6, plus 1.3. No.
- Raising the eraser on the spool to bounce higher: a small eraser on the spool gives 1.1 + 2.0·s, and anything large enough to matter no longer balances there.
- Re-bouncing to gain height: the launch speed is fixed by scale, not by impact speed, so bounces do not accumulate.

**G. Win, gadgets, bot.** Win = exit on the cabinet top.

`BouncePad` (attached to a prop)
- Parameters: `gainPerScale` 1.75 (apex above the surface = gain × scale), `minImpact` 3 u/s, `minNormalY` 0.9, `cooldown` 0.1 s.
- Each tick: if the player has just become grounded on this prop (`player.GroundProp == prop`, not grounded on it last tick), the ground normal has Y ≥ `minNormalY`, and the downward speed recorded on the previous tick was ≥ `minImpact`, then call `player.SetVelocity(v.x, √(2 · gravity · gain · prop.Scale), v.z)`.
- Walking onto the pad or standing on it does nothing. The ramps (normal Y = 0.77) never bounce.
- Not active while the prop is held or moving faster than 1 u/s.
- Events: `Bounced(prop, launchSpeed, impactSpeed)`. Output: `BounceCount`. Presentation squashes the slab by `impact / 30` for 0.15 s.
- Deterministic: one comparison per tick, no collision callbacks.

```csharp
yield return bot.WalkTo(new Vector3(-6, 0, 7.9f));
yield return bot.Grab(eraser);
yield return bot.WalkTo(new Vector3(0, 0, 2));
yield return bot.DropAt(new Vector3(0, 3.5f, 18));
yield return bot.Wait(1.5f);
yield return bot.WalkTo(new Vector3(-9.5f, 0, 11));        // around the pedestal and the slab
yield return bot.WalkTo(new Vector3(-9.5f, 0, 15.6f));
yield return bot.WalkTo(new Vector3(-3.5f, 2.4f, 15.6f));  // up the left ramp
yield return bot.WalkTo(new Vector3(0, 2.4f, 16.6f));
yield return bot.Jump(false);
yield return bot.Until(() => pad.BounceCount >= 1, 3f);
yield return bot.WalkTo(new Vector3(0, 14, 21), 0.5f, 6f);  // steer in the air
yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > 13.5f, 4f);
yield return bot.WalkTo(new Vector3(0, 14, 27));
```

**H. Teaching.**
- Blurb: "Rubber remembers how to jump."
- Hint 1: "Climb the eraser by its slanted end and jump on it. How high you go depends on how much rubber is under you."
- Hint 2: "Hold the eraser against the cabinet and walk backward. The farther back you stand, the bigger it gets, and the higher it throws you."
- Hint 3: "Stand in the middle of the room, press the eraser against the cabinet, let go, climb on, stand close to the cabinet and jump. Push forward at the top."

**I. Wow beats.**
- The landing: a pink slab the size of a bus falls two units and the whole floor shivers; dust jumps off the boards in a ring.
- The bounce: the slab dishes under your feet, then the cabinet face streaks past for a full second and the room opens out below you at the apex.

**Physics confidence.** High. The bounce is a velocity set, the eraser is a heavy convex slab on a flat floor. Minor risk: the capsule catching on the cabinet's top edge while sliding up; keep that edge square and flush.

---

## Level 7 — The Teeter-Totter

**A. Setting.** `sunset-shelf`: a study nook where a plastic ruler lies across a fat marker at the foot of a tall bookend. *Fantasy: stand on the low end of the ruler, turn a pebble into a boulder at the far end, and be flung onto the roof.*

**B. Layout** (floor Y=0, the ruler runs along Z).

| Element | X | Z | Y |
|---|---|---|---|
| Floor | −10..10 | −6..15.5 | top 0 |
| Backstop wall | −10..10 | plane Z=15.5 | 0..20 |
| Side and back walls, height 20 | X=±10, Z=−6 | | |
| `SkyCap` | everything | | Y=20 |
| Bookend tower (goal), sheer faces, square top edge | 2..10 | −6..5 | top 11 |
| Marker (static cylinder, axis along X, radius 0.9, length 4) | −2..2 | axis at Z=10 | axis Y=0.9, top 1.8 |
| Ruler (`Seesaw` plank, kinematic) | −1.5..1.5 | 0..15 | pivots on the marker's top line (Y=1.8, Z=10) |
| Spool pedestal (radius 0.4) | −5 | 2 | 0..1.1 |

- Ruler: 15 long × 3 wide × 0.2 thick, with 45° chamfers on all top edges so the capsule walks onto it. Long arm 10 (toward −Z), short arm 5 (toward the backstop). Arm ratio r = 2.
- At rest the long tip is on the floor at Z=0: tilt sin⁻¹(1.8 / 10) = 10.4°, short tip at Y = 1.8 + 5 × 0.18 = **2.7**.
- Tipped, the short tip is on the floor: tilt sin⁻¹(1.8 / 5) = 21.1°, long tip at Y = 1.8 + 10 × 0.36 = **5.4**.
- The seat is the outer end of the long arm; the reference standing spot is Z=0.7 (0.7 in from the tip, 9.3 from the pivot). The tower face at X=2 is 0.5 from the ruler's edge.
- Spawn (−5, 0, −3.5), yaw 0, checkpoint at spawn. Exit box (3 × 2.5 × 3) centered (7, 12.25, −2).

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Pebble | Lumpy sphere, collider radius 0.5, volume 0.524 | (−5, 1.3, 2), scale 0.4 | density 3 (mass 1.57·s³), friction 0.8, clamps 0.2–**6**, tag `weight`, grabbable |
| Ruler, marker | see layout | | not grabbable |

**D. Intended solution.**
1. Walk to (−5, 0, 0.9) and grab the pebble. g = √(1.1² + 0.25²) = 1.13, k = 0.4 / 1.13 = **0.355**.
2. Carry it to the seat: walk to (0, ·, 0.7) on the low end of the ruler and face +Z. The ruler top there is at Y=0.32, so the eye is (0, 1.87, 0.7).
3. Aim at the backstop above the raised end, at (0, 7.5, 15.5): pitch +20.8°. The pebble stops when it touches the wall, 14.8 away horizontally: d·(cos 20.8° + 0.5 × 0.355) = 14.8, d = 14.8 / 1.112 = **13.3**.
4. Release. s = 0.355 × 13.3 = **4.7**: a boulder 4.7 across, mass 1.57 × 105 = **165**. It hangs at (0, 6.6, 13.1) with its underside 1.7 above the short arm, and falls onto it.
5. The lever formula (below) gives f = (165 − 6) / (165 + 12) = 0.90. The short end is driven down at 0.90 g for 0.52 s; the seat is thrown upward and released at Y=5.15 with speed 19.2 u/s.
6. Apex = 5.15 + 19.2² / 44 = **13.5**. Hold right (toward the bookend): the capsule slides up its face, passes Y=11, and lands on top with about 0.95 s to spare. Exit.

Cone check for step 3: the pebble's angular radius is 10.2°, so the lower edge of its cone is 10.6° above horizontal. From the seat the ruler's surface never appears higher than 4.3° above horizontal (its far tip), so the cone clears the ruler and the boulder reaches the wall.

**E. Tolerances.**
- Launch formula: apex = 5.15 + 9.34·f with f = (M − 6) / (M + 12). The shelf at 11 needs an apex of about 11.6: f ≥ 0.69, M ≥ 46, **s ≥ 3.1**. Comfortable (apex 12.2) at s ≥ 3.4. The clamp is 6 (M = 339, apex 14.0). Window 3.4–6, a factor of 1.75.
- Aim: any pitch from about +13° to +50° puts the boulder on the wall above the short arm, with s between 4.5 and 6. Below +12° the pebble touches the ruler on the player's own arm instead and stays small; the preview shows it sitting in front of the marker.
- Where the player stands matters physically: launch speed scales with distance from the pivot. Standing 2 units in from the tip costs 25% of the height. The seat end is painted with a bullseye.
- The grab is the real decision. k must be at least 0.25, that is, the pebble must be grabbed from within about 1.6. Grabbing it from the seat (5.2 away, k = 0.077) gives s = 1.0 and M = 1.7: the ruler does not move at all. Grabbing from 2.5 away gives s = 2.3, M = 20 and an apex of 9.2, visibly short; from 2 away the apex is 11.0, a hair under the edge.
- Recovery: grab the boulder from anywhere (the ruler settles back, carrying the player down gently). To make it heavier, pick it up from close and drop it from far: walk toward it, grab at about 6, return to the seat. It is clamped at 6, so overshooting is harmless.
- Boulder rolls off sideways: the ruler returns to rest; re-grab.

**F. Unintended solutions.**
- Boulder as a step: 6 high at most, plus 1.3. The shelf is 11. A sphere that size cannot be climbed anyway.
- Standing on the tipped-up long end (5.4) and jumping: 6.7.
- Dropping the boulder first and walking to the seat afterwards: the long end is up; walking up the ruler works but nothing launches. Grabbing the boulder from up there lowers the ruler slowly.
- Standing on the short end and dropping the boulder on the long end: r = 0.5, launch height at most a quarter of 5.4. A small hop.
- Riding the boulder: a prop that is being stood on cannot be grabbed.

**G. Win, gadgets, bot.** Win = exit on the bookend.

`Seesaw`
- Parameters: pivot (0, 1.8, 10), axis X, `armA` 10 (seat side), `armB` 5 (strike side), plank 15 × 3 × 0.2, rest state A-down, `returnRate` 60°/s, `tipMargin` 1.
- The plank is one `AddKinematic` mover rotated about the pivot. Two triggers are parented to it: `padB` over the short arm (Z 10.5..15.5, full width plus 1 each side, from the plank up 8 and down to the floor) and `padA` over the whole long arm.
- Loads: M = total mass of non-held props whose center is in `padB` and that overlap the plank's bounds expanded by 0.15. m = mass of riders in `padA` (the player counts 3 when grounded on the plank; props count their mass).
- States:
  - `Rest`: tilt −10.4° (A down). If M > m·r + `tipMargin`, go to `Swing`.
  - `Swing`: f = clamp((M − m·r) / (M + m·r²), 0.05, 1). The strike tip accelerates downward at f·g: the tilt advances with constant angular acceleration f·g / armB until the short tip reaches the floor. Duration √(2 × 2.7 / (f·g)), 0.52 s at f = 0.9. Riders are glued: each tick their velocity along the plank normal is set to the plank's point velocity.
  - Release (last tick of `Swing`): tip speed v = r·√(2·g·f·2.7). Each rider at distance x from the pivot gets `SetVelocity(horizontal unchanged, v·x / armA)`, straight up. Raise `SeesawLaunched(rider, speed)`.
  - `Tipped`: stays while M > 0.5 on `padB`.
  - `Return`: rotate back at `returnRate` with smoothstep ends, carrying riders. Then `Rest`.
- The same code handles the mirrored case (strike on A, riders on B) with r = armB / armA.
- Because the pad never accelerates faster than gravity, the dynamic boulder stays pressed on it the whole swing: the picture is physically right.
- Events: `SeesawStruck(M, f)`, `SeesawLaunched`, `SeesawReturned`. Outputs: `State`, `Launched` (latched per swing). Deterministic: closed-form in ticks since the strike.

```csharp
yield return bot.WalkTo(new Vector3(-5, 0, 0.9f));
yield return bot.Grab(pebble);
yield return bot.WalkTo(new Vector3(-2.2f, 0, 0.7f));
yield return bot.WalkTo(new Vector3(0, 0.3f, 0.7f), 0.15f);   // up the chamfer onto the seat
yield return bot.DropAt(new Vector3(0, 7.5f, 15.5f));
yield return bot.Until(() => seesaw.Launched, 4f);
yield return bot.WalkTo(new Vector3(5, 11, 0.7f), 0.5f, 5f);   // steer right in the air
yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > 10.5f, 4f);
yield return bot.WalkTo(new Vector3(7, 11, -2));
```

**H. Teaching.**
- Blurb: "A lever only argues with something heavier than you."
- Hint 1: "Stand on the low end. Something has to land on the high end, and it has to weigh far more than you do."
- Hint 2: "The pebble grows with distance, and weight grows much faster than size. Pick it up from right beside it so it looks big in your hand, then carry it to the low end."
- Hint 3: "From the very tip of the ruler, hold the pebble against the wall above the far end and let go. When you fly, push toward the bookend."

**I. Wow beats.**
- The strike: the boulder's shadow swells on the short arm, the ruler bends visibly (a shader flex, not physics), and the bookend's spines blur past as you go up.
- The apex: a held breath above the bookend with the boulder, the marker and the bent ruler tiny below, lit orange.

**Physics confidence.** Medium. The rule is closed-form, but it depends on a kinematic plank carrying the capsule upward at up to 19 u/s without the solver adding or eating speed, which is why the release sets the velocity explicitly. The other risk is the capsule being squeezed between the rising plank and the bookend; the 0.5 gap and the 0.47 inward drift of the tip during the swing should cover it.

**Fallback (`LaunchSeat`).** Keep the ruler as a purely visual lever and make the seat a 3 × 3 static pad. When a `weight` prop comes to rest in `padB`, wait the same swing time, then apply the same launch velocity to a player standing on the seat and swap the ruler's static collider to the tipped pose. The puzzle logic (mass against the lever) is unchanged; only the ride on the rising plank is lost.

---

## Level 8 — Funnel Physics

**A. Setting.** `night-light`: the lid of a marble-run toy, lit from below through three funnels of different sizes. *Fantasy: you are the sorting machine — make one marble grow, keep one as it is, and pull one out of the sky, until each rattles down its own hole.*

**B. Layout** (lid top Y=0).

| Element | X | Z | Y |
|---|---|---|---|
| Floor (lid) | −14..14 | −12..16 | top 0 |
| Walls, height 14; `SkyCap` at Y=14 | X=±14, Z=−12, Z=16 | | |
| Gate in the +Z wall, locked | 0..3 | 16 | 0..3, tunnel to Z=22 |
| Niche above the gate | −1.5..4.5 | 16..21.5 | 6..12 |
| Return port in the −X wall | −14 | 0 | mouth at Y 0.3..1.9 |
| Spool pedestal (radius 0.4) | −10 | −6 | 0..1.1 |

Funnels, all on the line Z=9, cone slope 35° (drop 0.70 per unit of radius), friction 0.05:

| Funnel | Axis (X, Z) | Throat radius | Mouth radius | Cone depth | Tube | Chamber height | Plate at Y |
|---|---|---|---|---|---|---|---|
| S | (−8, 9) | 0.40 | 1.0 | 0.42 | 1.0 | 1.2 | −2.62 |
| M | (−2, 9) | 0.80 | 1.9 | 0.77 | 1.0 | 2.0 | −3.77 |
| L | (6.5, 9) | 1.60 | 3.6 | 1.40 | 1.0 | 3.6 | −6.00 |

- Rim-to-rim gaps are 3.1 (S–M) and 3.0 (M–L); the gate is reached through the M–L gap.
- Spawn (0, 0, −9), yaw 0, checkpoint at spawn. Exit box (3 × 3 × 3) centered (1.5, 1.5, 19.5), locked until all three plates are pressed.

**C. Props.** All three are glass marbles: sphere collider radius 0.5 at scale 1, density 2.5 (mass **1.309·s³**), friction 0.2, bounciness 0.2, clamps 0.3–6, tag `marble`, grabbable until latched. They are interchangeable; color is flavor.

| Marble | Start | Start scale | What the player must do with it |
|---|---|---|---|
| Peewee (red) | (−10, 1.23, −6) on the spool | 0.25 | grow |
| Aggie (yellow) | (10, 0.6, −8) on the floor | 1.2 | keep about the same |
| Shooter (blue) | (1.5, 8.5, 18.6) in the niche | 5.0 | shrink |

Acceptance windows. The throat is the upper bound (a sphere passes when its radius is under the throat radius); the plate's minimum mass is the lower bound.

| Funnel | Passes the throat when | Plate `minMass` | Scale window | Looks like |
|---|---|---|---|---|
| S | s < 0.80 | 0.164 (s = 0.50) | **0.50–0.76** | marble 62–95% of the hole |
| M | s < 1.60 | 1.31 (s = 1.00) | **1.00–1.52** | same |
| L | s < 3.20 | 10.5 (s = 2.00) | **2.00–3.04** | same |

The windows are disjoint and each spans a factor of 1.5 (±21%). The quoted upper ends keep 5% clearance; the physical limit is 5% higher.

**D. Intended solution.** The rule the player discovers: the marble keeps its size on screen, but the hole gets bigger on screen as you walk toward it. *Stand where the marble looks a little smaller than the hole.* The gauge lamp confirms it.

A held sphere aimed across a funnel passes over the mouth and stops against the cone's far wall, so the wall is a backstop and pitch barely matters. For an eye at horizontal distance h from the axis looking down at angle β, the rest point x (horizontal, from the eye) solves `1.55 + 0.7·h + c − (0.7 + tan β)·x = 1.2207·(0.5·k / cos β)·x`, with c = 0.70 × mouth radius.

1. **Peewee → S.** Grab from (−10, 0, −7.1): g = √(1.1² + 0.325²) = 1.15, k = 0.25 / 1.15 = **0.218**. Walk to (−8, 0, 7.0) (h = 2.0). Aim at (−8, 0.08, 9.55), 30° down. 1.55 + 1.4 + 0.7 = 3.65 = 1.431·x, x = 2.55, d = 2.94, s = **0.64**. Release: it rolls into the throat and drops; mass 0.34 ≥ 0.164; plate S latches.
2. **Aggie → M.** Grab from (10, 0, −3.3): g = √(4.7² + 0.95²) = 4.80, k = 1.2 / 4.80 = **0.25**. Carry it to (−2, 0, 5.0) (h = 4.0). Aim at (−2, 0.27, 10.13), 14° down. 1.55 + 2.8 + 1.33 = 5.68 = 1.106·x, x = 5.13, d = 5.29, s = **1.32**. Mass 3.0 ≥ 1.31; plate M latches.
3. **Shooter → L.** From (6.5, 0, −3) look up at the niche and grab: g = √(5² + 6.95² + 21.6²) = 23.2, k = 5 / 23.2 = **0.215**. Walk to (6.5, 0, 0.2) (h = 8.8). Aim at (6.5, 0.76, 11.55), 4° down. 1.55 + 6.16 + 2.52 = 10.23 = 0.901·x, x = 11.35, d = 11.4, s = **2.45**. Mass 19.2 ≥ 10.5; plate L latches.
4. The gate opens. Walk between M and L to the exit.

**E. Tolerances.**
- In scale: the windows above.
- In standing position (distance from the funnel axis, for the grabs above): S 1.3–3.0, M 2.0–5.2, L 6.2–12.3. That is 1.7, 3.2 and 6.1 units of floor.
- In aim: anything that puts the preview in the mouth. The far cone wall catches it; ±2° of pitch changes s by under 4%.
- Too big: it sits in the mouth. Grab it and step closer.
- Too small, or the wrong hole: it falls through, the plate ticks without travelling, and after 0.6 s the return port spits it back into the room at the same scale.
- A different grab distance changes k, never the rule. Any marble can serve any funnel: grab from farther to make it smaller on screen, from closer to make it bigger.
- The player in a funnel: all three throats are wider than the capsule. A `HazardZone` in each chamber sends the player out of the return port.

**F. Unintended solutions.**
- Two light marbles on one plate: the plate tests the heaviest single prop, not the sum.
- The player as a weight: plates ignore the player.
- A latched marble cannot be grabbed, so plates cannot be un-pressed and no marble can serve twice.
- Jamming: a sphere within 5% of the throat radius may wedge in the tube. Any un-latched prop that rests below the lid for 1.5 s is ejected through the return port.
- Shooter in the niche is out of reach but inside grab range (150) from the whole back half of the room; the niche is the only place above Y=3.

**G. Win, gadgets, bot.** Win = exit behind the gate, unlocked by `plateS.Pressed && plateM.Pressed && plateL.Pressed`.

`Funnel` — builds the static cone, tube and chamber (one non-convex static `MeshCollider`, 32 segments) from `axis`, `throatRadius`, `mouthRadius`, `slope`, `tubeLength`, `chamberHeight`. Exposes `Footprint` (cylinder under the mouth) and `ChamberBox`.

`WeightPlate`
- Parameters: `ChamberBox` as the sensor, `minMass`, `settle` 0.3 s, `rejectDelay` 0.6 s, `acceptPlayer` false.
- Each tick, for non-held props in the sensor in `Prop.Id` order, once speed < 0.5 for `settle`: if mass ≥ `minMass`, latch — `BeginDrive`, ease to the plate center over 0.25 s, `Grabbable = false`, `Pressed = true`, raise `PlatePressed(prop)`. Otherwise raise `PlateRejected(mass / minMass)` and, after `rejectDelay`, call `ReturnPort.Eject(prop)`.
- Outputs: `Pressed` (latched), `Load01` for the plate travel animation.

`ReturnPort`
- `Eject(prop)`: `SetPose` to (−13.2, 1.1, 0), keep the scale, set velocity (3, 0, 0). If the mouth is blocked, retry every 0.5 s. `Eject(player)`: `Teleport` to (−12.5, 0, 0), yaw 90°.
- Also owns the jam rule: any non-held, non-latched prop whose center is inside a `Funnel.Footprint` below Y=−0.1 and slower than 0.3 for 1.5 s is ejected.
- Marbles larger than 1.6 are ejected at Y = radius + 0.1 so they do not start inside the wall.

`FunnelGauge` (one per funnel; reads the simulation, changes nothing)
- While `game.Grabber.Held` is a `marble` whose center is within mouth radius + 0.5 of the axis: state = `TooSmall` (s < window min), `Good`, or `TooBig` (s > window max). Otherwise `Idle`. Raises `GaugeChanged`. Presentation: a ring of lamps around the mouth, blue / green / amber.

```csharp
yield return bot.WalkTo(new Vector3(-10, 0, -7.1f));
yield return bot.Grab(peewee);
yield return bot.WalkTo(new Vector3(-8, 0, 7.0f));
yield return bot.DropAt(new Vector3(-8, 0.08f, 9.55f));
yield return bot.Until(() => plateS.Pressed, 6f);

yield return bot.WalkTo(new Vector3(10, 0, -3.3f));
yield return bot.Grab(aggie);
yield return bot.WalkTo(new Vector3(-2, 0, 5.0f));
yield return bot.DropAt(new Vector3(-2, 0.27f, 10.13f));
yield return bot.Until(() => plateM.Pressed, 6f);

yield return bot.WalkTo(new Vector3(6.5f, 0, -3));
yield return bot.Grab(shooter);
yield return bot.WalkTo(new Vector3(6.5f, 0, 0.2f));
yield return bot.DropAt(new Vector3(6.5f, 0.76f, 11.55f));
yield return bot.Until(() => plateL.Pressed, 8f);

yield return bot.WalkTo(new Vector3(1.4f, 0, 9));     // between M and L
yield return bot.WalkTo(new Vector3(1.5f, 0, 19.5f));
```

**H. Teaching.**
- Blurb: "Three holes. Three marbles. None of them the right size."
- Hint 1: "A marble has to nearly fill its hole: small enough to fall through, heavy enough to press the plate underneath. The lamps around each funnel tell you which way you are off."
- Hint 2: "The marble never changes size on your screen, but the hole does. Walk toward a funnel and the marble shrinks against it; back away and it grows."
- Hint 3: "Little red one: stand two steps from the small funnel. Yellow: four steps from the middle one. Blue, up in the niche: grab it from the back of the room and drop it in the big funnel from about nine steps away."

**I. Wow beats.**
- Shooter leaving the niche: a blue glass planet with a cat's-eye swirl slides off its shelf, crosses the ceiling without changing size, and ends up as a ball you could hug, throwing a caustic across the lid.
- Each latch: the marble lights from inside in its own color, and the light floods up through the funnel onto the ceiling. With all three lit the room is striped red, yellow and blue and the gate rolls up.

**Physics confidence.** Medium-high. Spheres in low-friction cones are reliable; the risks are a marble within a few percent of the throat radius wedging (covered by the jam rule) and small fast marbles tunnelling (the engine already switches thin or fast props to continuous detection).

**Fallback (`FunnelCapture`).** If rolling proves flaky: when a non-held marble's center is inside the mouth cylinder and it touches the cone, take it kinematic. If its radius is under the throat radius, play a fixed spiral into the throat (0.8 s) and drop it onto the plate; otherwise seat it on the cone and return it to physics. The size and weight logic is identical.

---

## Level 9 — The Moving Train

**A. Setting.** `moon-quilt`: night in the playroom. A toy train runs a lit loop on trestles around a lighthouse-shaped night-light, with nothing but dark under the track. *Fantasy: throw a plank across the gap like a clock hand, with one end on the lighthouse and the other on the moving train, then ride the train out to it and walk in.*

**B. Layout** (station top Y=0; the lighthouse axis is at X=0, Z=20). Radii `r` are measured from that axis.

| Element | Geometry |
|---|---|
| Station ledge (start) | X −9..9, Z −10..3.3, top Y=0. Its edge is r = 16.7 at X=0. |
| Lighthouse tower | Static cylinder, radius 2, Y −30..16. Sheer. |
| Doorstep ring (goal) | Annulus r 2..3.5 around the tower, top Y=−1, 0.5 thick |
| Track | Circle r = 14. **Visual only: rails and trestles have no collider.** |
| Train decks | r 12.5..15.5, top Y=−1 (see `Train`) |
| Pit | Everything else. `KillY` = −30. |
| Room walls, height to 16; `SkyCap` at Y=16 | X=±20, Z=−10, Z=40 |
| Spool pedestal (radius 0.4) | (2.5, 0, −1), top Y=1.1 |

- Gaps: station edge to deck 1.2 across and 1.0 down (a step off or a walking jump lands on the deck; a walking jump gets back up). Deck to ring **9.0**. Station edge to ring 13.2.
- Spawn (0, 0, −7), yaw 0, checkpoint at spawn. Exit box (6.6 × 3.5 × 6.6) centered (0, 0.25, 20): reaching the ring from any side wins.

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Wooden plank | 3.0 long (Z) × 0.6 wide (X) × 0.12 thick, box collider | (2.5, 1.14, −1), scale 0.65, long axis along Z | density 0.3 (mass 0.065·s³), friction 0.8, clamps 0.3–**4.4**, tags `plank`, `SnapUprightOnGrab`, grabbable |
| Train | see `Train` | | not grabbable |

At the clamp the plank is 13.2 long: exactly the station-to-ring distance, so it can never rest on both.

**D. Intended solution.**
1. Walk to (2.5, 0, −2.4) and grab the plank end-on. g = √(1.4² + 0.41²) = 1.46, k = 0.65 / 1.46 = **0.446**. Its length is along the view and stays that way.
2. Walk to the station edge at (0, 0, 2.9). Eye (0, 1.55, 2.9); the tower wall is D = 15.1 away.
3. Aim at the tower wall just above the ring, at (0, 0.9, 18): pitch −2.5°. The plank stops when its far end touches the wall. Its half-length is 1.5·s, so d·(cos 2.5° + 1.5 × 0.446) = 15.1, d = 15.1 / 1.667 = **9.06**.
4. The preview is s = 0.446 × 9.06 = **4.03**: 12.1 long, 2.4 wide, 0.48 thick, hanging level at Y=1.16 from the tower (r = 2.0) out to r = 14.1, which is 1.6 over the circle the decks run on. Hold it there.
5. Wait for the train. Release while cars are passing under the plank's near end. It falls 1.9 in 0.42 s (the train moves 10° in that time), lands with one end on a deck and the other on the ring, and `PropCarrier` locks it to that car. It is now a spoke that sweeps round the lighthouse with the train.
6. Board: either jump down onto one of the following cars straight away and walk forward along the train to the plank, or wait one lap (15 s) and jump onto the plank's car as it comes past.
7. Walk in along the plank. It turns under you, but it always points at the lighthouse, so walking toward the light stays on it. Reaching the ring completes the level.

**E. Tolerances.**
- Scale: the plank must reach from the tower to the decks, s ≥ 3.7 (11.1 long, 0.6 of overlap on a deck); the clamp is 4.4. A narrow band in scale, but a wide one in practice, because the tower backstop sets the size: L = 0.80·D for this grab, and every D available on the station (15.0 and up) is long enough.
- Standing position: from the edge back to Z = 0.3, 2.7 units deep. Farther back the clamped plank hangs short of the tower, misses the ring, and the preview shows the gap.
- Aim: any point on the tower wall from Y = 0.3 to about 6. Aiming at the ring or below stops the plank short on the ring edge; the preview visibly fails to reach the track circle.
- Grab distance: a grab from 1.9 away (k = 0.34) gives L = 0.68·D; step back 1.3 from the edge and it reaches. A grab from 1.1 hits the clamp; still fine.
- Timing: decks are under the drop point for about 5 s of every 15 s lap (eight cars, 14° each, plus the plank's own 10° width). The train's headlamp and whistle announce it 3 s ahead.
- Missed the train: the plank tips off the ring, falls into the pit and respawns on the spool at scale 0.65. Cost: about 20 s. The player loses nothing else.
- Landed crooked (captured but not pointing at the tower): grab it from the station as it passes — a grab releases it from the car — and place it again.
- Player falls: respawn on the station. A player riding the bare train can jump back up to the station on any lap.

**F. Unintended solutions.**
- Static bridge from the station to the ring: needs more than 13.2. The clamp is exactly 13.2.
- Jumping from the train to the ring: 9.0. The train's speed is tangential and does not help.
- Balancing the plank on a deck as a diving board toward the ring: not captured (capture requires the inner end over the ring), so it stays a loose 5.5-mass plank on a turning deck and tips when the player is 2.75 past the deck edge. From there the ring is still 6.25 away. Treated as blocked; if testing finds it doable, narrow the ring to r 2..3.
- Laying the plank from ring to track bed: there is no track bed collider.
- Standing on the plank while it is captured and grabbing it: a prop underfoot cannot be grabbed.

**G. Win, gadgets, bot.** Win = exit box around the ring.

`Train`
- Parameters: `center` (0, 20), `radius` 14, `deckY` −1, `angularSpeed` 24°/s (lap 15 s, 5.9 u/s at the deck), `startBearing` −150°, engine arc 16°, 8 cars of arc 14° each with a deck arc of 13° (3.2 long) and deck width 3.0; coupling gaps 0.24, too narrow to fall through.
- Bearing φ: position = (radius·sin φ, ·, 20 − radius·cos φ); φ = 0 is the station, increasing toward +X. Engine bearing = startBearing + angularSpeed × time, from the tick count. Car i (1..8) is at engine − 15° − 14°·(i − 1).
- Each car and the engine is one `AddKinematic` mover, moved with `MoveTo(position, tangent yaw)` every tick. They carry the player and push loose props. The engine is 1.0 tall (top Y=0) with a funnel to Y=0.4, low enough to pass under a held plank.
- Outputs: `EngineBearing`, `CarBearing(i)`, `CarMover(i)`. Event: `TrainAtStation(lap)`.

`PropCarrier`
- Parameters: `train`, accept tag `plank`, `ringRadius` 3.2, `maxOuterRadius` 16.5, `flatDot` 0.94, bed height 0.3.
- Each tick, for every non-held, non-carried tagged prop, capture it when all of these hold: its up-axis has Y ≥ `flatDot`; it overlaps the bed box of some car (a 0.3-high box on that car's deck); its innermost bottom corner is at r ≤ `ringRadius`; its outermost corner is at r ≤ `maxOuterRadius`.
- Capture: `BeginDrive`; store the pose relative to the car; ease its underside onto the deck plane over 0.1 s; every tick `MoveTo(carPose × relativePose)`. Raise `PlankCaptured(prop, carIndex)`. Output: `Captured`, `CarIndex`.
- Release: when the prop is grabbed. Never releases on its own.
- A carried prop is a mover, so riders get `Mover.PointVelocity` including the rotation about the tower.

```csharp
Vector3 aim = new Vector3(0, 0.9f, 18);
Vector3 tower = new Vector3(0, -1, 20);
yield return bot.WalkTo(new Vector3(2.5f, 0, -2.4f));
yield return bot.Grab(plank);
yield return bot.WalkTo(new Vector3(0, 0, 2.9f), 0.15f);
yield return bot.LookAt(aim);
// Car 4 must be at the station when the plank lands, 10 degrees after release.
yield return bot.Until(() => Mathf.Abs(Mathf.DeltaAngle(train.CarBearing(4), -10f)) < 1f, 20f);
yield return bot.DropAt(aim);
yield return bot.Until(() => carrier.Captured, 2f);
// One lap later, lead the plank by the walk-and-jump time (about 0.9 s = 22 degrees).
yield return bot.Until(() => Mathf.Abs(Mathf.DeltaAngle(train.CarBearing(4), -24f)) < 1f, 20f);
yield return bot.WalkTo(new Vector3(0, 0, 3.15f), 0.1f);
yield return bot.Jump();
yield return bot.WalkTo(new Vector3(0, -0.5f, 6.9f), 0.4f, 3f);
yield return bot.Until(() => bot.Player.GroundProp == plank, 2f);
yield return bot.WalkTo(tower, 2.8f, 12f);                 // inward along the turning plank
```

**H. Teaching.**
- Blurb: "The train never stops. Neither should your bridge."
- Hint 1: "You can ride the train, but it never gets closer to the lighthouse. Something has to reach from the train to the doorstep — and travel with it."
- Hint 2: "Hold the plank end-on against the lighthouse wall. From the platform edge it stretches all the way back to the track. It needs something under its near end when you let go."
- Hint 3: "Stand at the platform edge, press the plank against the lighthouse just above the doorstep, wait until the wagons are passing underneath, and release. Then hop on the train and walk the plank."

**I. Wow beats.**
- The catch: the plank drops, slaps onto a passing wagon, and instead of falling it is suddenly *moving* — a twelve-unit clock hand sweeping the dark, its shadow wheeling across the walls from the lighthouse lamp.
- The walk: the room turns around you while the lighthouse stays dead ahead, and the train's lit windows run along beside your feet.

**Physics confidence.** Medium. The train and the carried plank are kinematic and closed-form; the risks are (1) the capsule riding a mover that both translates and rotates at 5.9 u/s — the controller's ground-velocity tracking is built for this but untested at that speed — and (2) the 0.4 s between release and capture, during which the plank is a free body falling onto a moving deck. If (2) is unreliable, extend the capture test to "falling toward a bed box with the support conditions already met" and capture in the air.

**Fallback (straight ferry).** Replace the loop with a straight out-and-back track between the station and a far platform 24 away. The cars become open bunks (two cross-beams each, 4 apart, nothing to stand on). A plank dropped lengthwise so that it spans at least two bunks is captured and becomes the deck; ride it across. Same sizing rule (the plank must span the bunks) and the same timing rule (the bunks must be under it at release), no rotation.

---

## Phase ramp

| Level | New idea | Gadget rule in one line | Decisions | Target time | Risk |
|---|---|---|---|---|---|
| 5 | Size is area: wide catches wind | lift ∝ s², flies at s ≥ 6.5 | Aim from a vantage, board | 1–2 min | Low |
| 6 | Size is stored energy; tune by stepping back | apex = 2.0·s | Stand distance, climb, steer in the air | 1.5–2.5 min | Low |
| 7 | Size is mass (s³); you are part of the machine | apex = 5.15 + 9.34·(M − 6)/(M + 12) | Grab close, stand on the tip, aim high | 2–3 min | Medium (fallback given) |
| 8 | Exact sizes, three times, in both directions | throat radius above, minimum mass below | Three stand distances, read the gauge | 3–4 min | Low to medium (fallback given) |
| 9 | Size plus timing on a moving target | spans tower to deck, released over a car | Size, aim, wait, release, ride | 3–4 min | Medium (fallback given) |

Scale windows, for the bot tests to assert against:

| Level | Prop | Start scale | Intended scale | Works from | To (clamp) |
|---|---|---|---|---|---|
| 5 | Feather | 1.0 | 9.2 | 6.5 | 12 |
| 6 | Eraser | 0.8 | 9.5 | 7.5 | 10.5 |
| 7 | Pebble | 0.4 | 4.7 | 3.4 | 6 |
| 8 | Peewee / Aggie / Shooter | 0.25 / 1.2 / 5.0 | 0.64 / 1.32 / 2.45 | 0.50 / 1.00 / 2.00 | 0.76 / 1.52 / 3.04 |
| 9 | Plank | 0.65 | 4.03 | 3.7 | 4.4 |

**Open questions for engineering.**
1. `Prop.BeginDrive` / `EndDrive` (section 0.3) is the one engine addition three levels depend on. Without it each level's fallback applies.
2. Level 7 assumes a gadget may set the player's velocity every tick during the swing. Confirm that does not fight `Player.Tick`'s ground steering.
3. Level 9 assumes `Bot.Grab` can pick up a prop that is moving under a mover; `Bot.LookAt(Prop)` already tracks.
4. Environment keys used: `lamp-desk`, `golden-boards`, `sunset-shelf`, `night-light`, `moon-quilt` (the reserved list in `art-pitch-c-glow.md`). Swap freely if another art direction wins.

**Files.** Read `docs/ARCHITECTURE.md`, `docs/design-notes/levels-phase-1-draft.md` and the engine sources under `Assets/Toybox/Runtime/Engine` (read-only). Nothing outside `docs/design-notes/levels-phase-2-draft.md` was written.

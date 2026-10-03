# Phase 1 — The Basics (Levels 1–4): level design spec

**Engine note.** `docs/ARCHITECTURE.md` says the project moved to Unity 6 / PhysX on 2026-10-03, so this spec uses its conventions: left-handed, Y-up, yaw 0 looks down +Z, degrees, `PropOptions`, `LevelContext`, `Bot`. Units, player metrics and the mechanic are unchanged from the brief.

**Status.** Every number below is hand-calculated; nothing has been run in the engine. Treat aim points and thresholds as starting values for the bot test to tune.

## 0. Shared rules and engine requests

**The rule that shapes every layout.** The hold march stops at the first overlap going outward from the eye. A prop can therefore only arrive at a pose if the whole cone from the eye to that pose is empty. Three consequences:
- You cannot project a prop down into a pit from a low vantage; it stops on the near rim. Level 2 drops the thimble from mid-air against a backstop instead.
- Aiming a tall prop along a near-horizontal ray makes its scale very sensitive to pitch. Level 4 gives the player a raised vantage for this reason.
- Shrinking at your feet is trivially robust (Level 3).

**Engine requests (flagged, not assumed).**
1. `PropOptions.SnapUprightOnGrab` (default true): on grab, ease pitch and roll to the nearest 90° in the yaw frame over 0.15 s. Without it a toppled domino or tumbled wedge is re-grabbed at an odd tilt that `F` cannot fix.
2. `SetSpawn(pos, yaw, pitch)`: Level 3 wants a spawn pitch of +25°.
3. A falling heavy prop must not explode the player capsule; depenetrate sideways.

**Shared gadgets** (ticked in creation order inside the "level + gadgets" step, using overlap queries, `game.Time`, no RNG).

| Gadget | Behavior | Parameters |
|---|---|---|
| `ExitDoor` | Sphere trigger; player inside calls `ctx.Complete()`. Optional `enabledBy` signal. | pos, radius 1.5 |
| `SkyCap` | Invisible static slab on Default layer closing the top of a level so held props cannot be projected out of the play space. | AABB |
| `PropLeash` | If a prop is not held and its center has been outside `volume` for `grace` seconds, respawn it at its origin pose and scale, and raise `PropRespawned`. | volume, grace 2.0 s |
| `HazardZone` | Player feet inside for 0.1 s → respawn at last checkpoint. Ignores props. Has an `Enabled` flag. | volume |

**Held-prop presentation note (for art).** While held: no cast shadow, no fog, no DOF, constant-pixel outline. On release the shadow fades in over 0.2 s — that is the "true size" reveal in all four levels.

---

## Level 1 — The Cheese Wedge

**A. Setting.** `sunny-rug`: a canyon of rug between two stacks of picture books inside an open toy chest. *Fantasy: a crumb of plastic cheese becomes a hillside.*

**B. Layout** (rug floor Y=0, travel is +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Rug floor | −7..7 | −12..46 | top 0 |
| Book stack A (start) | −7..7 | −12..0 | top 4 |
| Book stack B (goal) | −7..7 | 30..46 | top 4 |
| Leaning book (static ramp A→rug, 26.6°) | −7..−4 | 0..8 | 4 → 0 |
| Chest walls | at X=±7, Z=−12, Z=46 | | height 12 |
| `SkyCap` | −7..7 | −12..46 | Y=12 |
| Spool pedestal (radius 0.35) | 0 | −3 | 4..5 |

- The gap between A and B is 30 wide; B's face is a sheer 4-unit wall.
- Spawn (0, 4, −9), yaw 0. Exit (0, 4, 42).
- The player can always walk down and back up the leaning book, so the rug is never a trap.

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Cheese wedge | Right-triangular prism: length 1.0 (Z), width 0.8 (X), height 0 at −Z rising to 0.5 at +Z; origin at bbox center; convex collider | (0, 5.1, −3), scale 0.4, thin end toward spawn | density 0.3, friction 0.9, clamps 0.2–14, grabbable |

Mass is 0.06·s³ (51 at s=9.5), so the player cannot shove it.

**D. Intended solution.**
1. Walk to (0, 4, −4.0). Eye is (0, 5.55, −4.0); wedge center is 1.0 ahead and 0.45 below. Grab distance g = √(1² + 0.45²) = 1.10, so k = 0.4 / 1.10 = 0.365.
2. Step around the spool to (2, 4, −0.6), near A's edge.
3. Aim at the foot of B's face. The wedge stops when its base meets the rug and its tall end meets B. With half-height 0.25s = 0.091d and half-length 0.5s = 0.182d:
   - d·(sin p + 0.091) = 5.55 (eye height above the rug)
   - d·(cos p + 0.182) = 30.6 (eye to B's face)
   - Solution: p ≈ −7°, d ≈ 26.0.
4. Release. s = 0.365 × 26.0 = **9.5**. The wedge is 9.5 long, 4.75 tall, 7.6 wide, with its toe at Z ≈ 20.5 and its tall end against B.
5. Walk down the leaning book, across the rug, up the 26.6° cheese slope, and step down 0.75 onto B. Exit.

**E. Tolerances.**
- Works for s from 5.6 to 14, provided the tall end is within about 1.5 of B's face. At s=5.6 the top is 2.8 high and needs a 1.2 jump; above s=8 the player just walks off the top.
- Aiming anywhere on B's lower face gives the same d ≈ 26, because the tall end hits the wall first. That is the forgiving target.
- Miss short (wedge lands mid-rug): re-grab from A. k is unchanged, so aiming farther fixes it. Re-grabbing from the rug also works.
- Reversed wedge: `Q` × 12, or walk around it.

**F. Unintended solutions.**
- Jumping: the gap is 30, and 4 high from the rug. Impossible.
- Standing the wedge on end as a tower next to B: tolerated; it is the same lesson.
- Wedge dropped on top of B: still visible and grabbable from A (ray range 150).
- Max clamp 14 keeps width at 11.2, inside the 14-wide chest.

**G. Win, gadgets, bot.** Win = `ExitDoor` at (0, 4, 42). Gadgets: `ExitDoor`, `SkyCap`.

```csharp
yield return bot.WalkTo(new Vector3(0, 4, -4.0f));
yield return bot.Grab(wedge);
yield return bot.WalkTo(new Vector3(2, 4, -0.6f));
yield return bot.DropAt(new Vector3(0, 2.4f, 25.3f));   // wedge center when seated
yield return bot.Wait(1f);
yield return bot.WalkTo(new Vector3(-5.5f, 4, -0.5f));
yield return bot.WalkTo(new Vector3(-5.5f, 0, 9));
yield return bot.WalkTo(new Vector3(0, 0, 19.5f));
yield return bot.WalkTo(new Vector3(0, 4.5f, 29.3f));
yield return bot.WalkTo(new Vector3(0, 4, 42));
```

**H. Teaching.**
- Blurb: "Things are as big as they look. Pick up the cheese."
- Hint 1: "Hold the cheese and look around. It lands on whatever is behind it."
- Hint 2: "Far away means bigger. Look at the tall books across the rug."
- Hint 3: "Stand at the edge, aim the cheese at the bottom of the far book stack, and let go. Then walk down and climb it."

**I. Wow beats.**
- The release: a crumb that filled a thumbnail of screen is suddenly a 9-unit hillside, and its shadow sweeps across the rug as it settles.
- Walking up the slope: the cheese holes are now cave-sized and the rug pile reads as grass beside it.

---

## Level 2 — The Thimble Chasm

**A. Setting.** `pegboard-workbench`: a pegboard walkway that ends in a round silo whose floor is missing. *Fantasy: cork a bottomless hole with a thimble the size of a water tower.*

**B. Layout** (pegboard top Y=0).

| Element | Geometry |
|---|---|
| Corridor floor | X −5..5, Z −8..19.7, slab 0.6 thick |
| Corridor walls | X=±5 and Z=−8, height 14 |
| Silo | Vertical cylinder, axis (0, ·, 22), radius 5.5, wall height 14; joins the corridor at the chord Z ≈ 19.7 |
| Hole / well | The entire silo floor: radius 5.5, well floor (spring pad) at Y=−9 |
| Exit door | In the silo's far wall at (0, 0, 27.5): opening X −1..1, Y 0..2.6, with a tunnel Z 27.5..33 |
| `SkyCap` | Y=14 over everything |
| Spool pedestal (radius 0.4) | (−3, 0, 2), top Y=1.1 |

- Spawn (0, 0, −6), yaw 0, with a checkpoint at spawn. Exit (0, 0, 31).
- Crossing distance: 11 on the centerline; 9.05 from the worst wall-hugging corner (4.7, 19.1) to the door edge (1.0, 27.4). Un-jumpable.

**C. Props.**

| Prop | Authored size | Start | Options |
|---|---|---|---|
| Silver thimble | Stout frustum: rim radius 0.5, top radius 0.49, height 0.8, closed flat dimpled top up, solid convex collider | (−3, 1.3, 2), scale 0.5 | density 1.5, clamps 0.2–14, tag `plug`, grabbable until seated |

**D. Intended solution.**
1. Walk to (−3, 0, 1.0) and grab. g = √(1² + 0.25²) = 1.03, so k = 0.5 / 1.03 = 0.485.
2. Walk to the centerline at (0, 0, 2.2).
3. Aim at the silo's far wall about 5 units up, above the door. The thimble stops when its rim touches that wall: horizontal distance from eye to center + 0.5s = 27.5 − zE.
   - s = (27.5 − zE) / (0.5 + cos p / k) = 25.3 / (0.5 + 0.986 / 0.485) = 25.3 / 2.53 = **10.0**, with d ≈ 20.6.
   - The preview hangs in the silo, 10 wide, 8 tall, bottom about 1 above floor level.
4. Release. The `SocketWell` captures it, it drops with a clunk, and its top seats flush at Y=0.
5. Walk across the dimpled top and through the door.

**E. Tolerances.**
- Seat window: s from 8.8 to 10.9. In terms of where the player stands on the centerline: zE from about −0.1 to 5.2, a 5-unit-deep zone. "Step back = bigger" is visible in the preview.
- The curved silo wall self-centers the thimble, so lateral aim barely matters.
- Too small (s < 8.8): falls to the pad and sits low. Look down from the rim and re-grab. A player who falls in is returned to the checkpoint by the `HazardZone`.
- Too big (rim radius ≥ 5.5): stops at the silo mouth or caps the hole. Re-grab and step forward.

**F. Unintended solutions.**
- Wall-hugging jump: blocked, because the silo wall is the well wall and leaves no ledge.
- Small thimble as a springboard: a 1.0-high step extends a jump to roughly 6.3, still under 9.
- Projecting the thimble over the walls: blocked by `SkyCap`.
- Corridor is 10 wide so the view cone to a 10-wide thimble clears the walls (half-width 4.45 needed at the junction, 5.0 available).

**G. Win, gadgets, bot.** Win = `ExitDoor` (0, 0, 31).

`SocketWell` (new, reusable for keyholes and sockets):
- Inputs: axis position, radius R=5.5, depth 9, accept tag `plug`, accept scale 8.8–10.9, capture radius 1.5.
- Capture condition, checked each tick: a tagged prop is not held, its scale is in the window, its axis is within the capture radius, and its bottom is above Y=−1.
- On capture: the prop goes kinematic; it eases onto the axis over 0.25 s, then descends at 22 u/s² until its **top is at Y=0**. The spring pad rises to meet its base — this is the height assist.
- Then: prop becomes Fixed and non-grabbable; a flat static grommet annulus is enabled from the thimble's top radius out to R, so the floor is continuous; the well's `HazardZone` is disabled; `Seated` is raised.
- Deterministic: a pure function of tick count after capture.
- Out-of-window props are left to normal physics.

`HazardZone`: cylinder radius 5.5, Y −9..−1.05.

```csharp
yield return bot.WalkTo(new Vector3(-3, 0, 1.0f));
yield return bot.Grab(thimble);
yield return bot.WalkTo(new Vector3(0, 0, 2.2f));
yield return bot.DropAt(new Vector3(0, 5.0f, 22.5f));
yield return bot.Until(() => socket.Seated, 5f);
yield return bot.WalkTo(new Vector3(0, 0, 26.5f));
yield return bot.WalkTo(new Vector3(0, 0, 31));
```

**H. Teaching.**
- Blurb: "The floor is missing. The thimble isn't."
- Hint 1: "The thimble has a flat top you could stand on, if it were big enough."
- Hint 2: "Hold it up against the round wall at the far side. Step backward to make it bigger, forward to make it smaller."
- Hint 3: "Stand in the middle of the walkway a few steps past the spool, aim above the little door, and let go when the thimble just fills the hole."

**I. Wow beats.**
- The drop: a ten-unit chrome thimble falls eight units past the camera into the well, with pegboard dots streaking in its mirror finish and a deep metallic clunk.
- The rubber grommet squeezing shut around it, and the dimples on top now the size of stepping stones.

**Physics confidence.** Medium without the socket (a tight convex-in-concave fit can jam), high with it. Fallback if capture feels too magnetic: keep the physical drop and use the socket only for the final 0.3 units of centering and the grommet.

---

## Level 3 — Shrinking the Apple

**A. Setting.** `cardboard-box`: inside an open-topped shipping box on the playroom floor, with a wall shelf looming above. *Fantasy: pluck a boulder-sized apple out of the sky and put it in an egg cup.*

**B. Layout** (box floor Y=0).

| Element | Geometry |
|---|---|
| Box interior | X −3..3, Z −4..4 |
| Walls | +Z wall height 4.0; the other three height 6.0; thickness 0.3 |
| Flap door | In the +X wall, Z −1..1, Y 0..3; hinged at the bottom, falls outward |
| Outside floor | Y=0, X 3..9, Z −3..3 |
| Funnel cup | Axis (1.8, 0, −2.2): mouth radius 0.6 at Y=0.6, narrowing to a tube of radius 0.26 from Y=0.3 down; button top at Y=0.12; friction 0.05 |
| Wall shelf | X −8..8, Z 24..36, top Y=18 |

- Spawn (0, 0, −2.5), yaw 0, pitch +25° (apple elevation is about 34°). Exit (6, 0, 0).
- The apple's center is visible over the +Z wall from anywhere with Z ≤ 0.5. At Z=0 the wall blocks elevations below 31.5° and the apple sits at 36.5°.

**C. Props.**

| Prop | Authored size | Start | Options |
|---|---|---|---|
| Plastic apple | Sphere collider radius 0.5; stem and leaf are visual only | (0, 23.7, 30) on the shelf, scale 11.4 (11.4 across) | density 4, bounciness 0.1, clamps 0.15–12, grabbable |

Mass is 2.09·s³.

**D. Intended solution.**
1. Walk to (1.2, 0, −1.9), beside the funnel, and look up at the apple. Distance from eye to center = √(1.2² + 31.9² + 22.15²) = 38.9, so k = 11.4 / 38.9 = 0.293.
2. Grab. The apple's on-screen size is unchanged, about 17° across.
3. Look down into the funnel, 0.67 away. The apple arrives at d ≈ 1.35 touching the funnel: s = 0.293 × 1.35 = **0.40**, radius 0.20.
4. Release. It rolls down the tube (radius 0.26) onto the button. Mass = 2.09 × 0.064 = 0.13, above the 0.04 threshold. The button latches and the flap falls.
5. Walk out over the flap.

The two-step route is equally fine: look straight down at your feet, where s = 1.55k / (1 + 0.5k) = 0.39, then carry the marble to the funnel.

**E. Tolerances.**
- Works for s from 0.27 (mass 0.04) to 0.50 (fits the tube).
- Grabbing from anywhere in the visible half of the box and dropping at the feet gives s from 0.38 to 0.42, so the natural action lands mid-window.
- Too big: it sits in the funnel mouth. Re-grab and drop closer.
- Too small (dropped against a wall at very short range): the button ticks but does not travel. Re-grab at the feet and drop against the far wall to regrow it.
- Apple leaves the box: `PropLeash` returns it to the shelf at full size after 2 s.

**F. Unintended solutions.**
- Pressing the button yourself: the capsule bottom dips only 0.15 into a 0.26-radius tube and stops 0.33 above the button; the button also ignores the player.
- Climbing out on the apple: the best chain is floor → apple (top ≤ 1.3) → jump, reaching 2.6. Via the funnel rim it is about 3.1. The lowest wall is 4.0.
- Crushing the cup with a huge apple: the cup is static, and max scale 12 cannot grow beyond the start size.
- No windows, so no route out and no need for a see-through collider layer.

**G. Win, gadgets, bot.** Win = `ExitDoor` (6, 0, 0).

- `PressureButton`: sensor = overlap cylinder (radius 0.26, Y 0.12..0.45). Sums `Rigidbody.mass` of non-held props whose center is inside. Parameters: `minMass` 0.04, `acceptPlayer` false, `latch` true, `debounce` 0.2 s. Outputs: `Pressed` (bool) and `Load01` (sum / minMass, clamped, for the cap travel animation). Deterministic: a fixed-order overlap query per tick.
- `FlapDoor`: kinematic panel 2 × 3 hinged on its bottom edge. Input signal `Pressed`. Rotates 0° → 90° outward over 0.8 s with ease-out, then lies flat as static floor. Raises `Open`.
- `PropLeash`: volume = box interior, Y 0..6, plus the origin pose; grace 2 s.

```csharp
yield return bot.WalkTo(new Vector3(1.2f, 0, -1.9f));
yield return bot.LookAt(apple);
yield return bot.Grab(apple);
yield return bot.DropAt(new Vector3(1.8f, 0.2f, -2.2f));
yield return bot.Until(() => button.Pressed, 5f);
yield return bot.Until(() => flap.Open, 3f);
yield return bot.WalkTo(new Vector3(2.4f, 0, 0));
yield return bot.WalkTo(new Vector3(6, 0, 0));
```

**H. Teaching.**
- Blurb: "The button wants something small. Look up."
- Hint 1: "That apple is very far away. It only looks small enough to hold."
- Hint 2: "Grab the apple, then look at something close. Your own feet are the closest thing there is."
- Hint 3: "Stand by the cup, grab the apple from the shelf, look straight down into the cup, and let go."

**I. Wow beats.**
- The grab: the apple peels off the shelf with no change in apparent size, and its enormous shadow vanishes from the wall behind it. That shadow disappearing is the tell.
- The release: a glossy marble with a leaf rattles around the funnel, and the whole box side falls open to daylight.

**Physics confidence.** High; a sphere in a low-friction funnel is the most reliable setup in the phase.

---

## Level 4 — Domino Effect

**A. Setting.** `block-hall`: a hallway of wooden building blocks, with a tilted drawing board leading down to a blocked archway. *Fantasy: one domino, made tall as a house, falls like a drawbridge through the wall.*

**B. Layout.**

| Element | X | Z | Y |
|---|---|---|---|
| Book-stack balcony (start) | −7..7 | −6..10 | top 3 |
| Block stair (rug ↔ balcony; 3 steps of 1.0 high, 1.5 deep) | 5..7 | 10..14.5 | 2, 1, 0 |
| Hall floor | −7..7 | 10..15 | 0 |
| Drawing board, 14° down-slope | −7..7 | 15..25 | 0 → −2.5 |
| Arch wall (height 12), opening X −4.5..4.5, Y −2.5..3.5 | −7..7 | 25..26.5 | |
| Barricade, filling the opening (9 × 6 × 1.2), front face flush at Z=25 | −4.5..4.5 | 25..26.2 | −2.5..3.5 |
| Exit corridor | −4.5..4.5 | 26.5..40 | −2.5 |
| Hall walls at X=±7 (height 12), `SkyCap` at Y=12 | | | |
| Pedestal block (0.8 × 0.8) | −3 | 8 | 3..3.9 |

- Spawn (0, 3, −3), yaw 0, with a checkpoint at spawn. Exit (0, −2.5, 36).

**C. Props.**

| Prop | Authored size | Start | Options |
|---|---|---|---|
| Domino | Width 1.0 (X), height 2.0 (Y), thickness 0.3 (Z), box collider, pips facing −Z | (−3, 4.4, 8), scale 0.5, upright | density 0.8 (mass 0.48·s³), friction 0.7, clamps 0.3–6, tag `smasher`, `SnapUprightOnGrab`, grabbable |
| Barricade | Static until broken | | not grabbable |

The domino tips on its own on any slope steeper than atan(0.3 / 2) = 8.5°; the board is 14°.

**D. Intended solution.**
1. Walk to (−3, 3, 6.9), facing +Z, and grab. g = √(1.1² + 0.15²) = 1.11, so k = 0.5 / 1.11 = 0.45.
2. Walk to the balcony edge at (0, 3, 9.5). Eye is (0, 4.55, 9.5).
3. Look slightly down (p ≈ −8°) so the domino's foot rests on the board about 6 in front of the barricade.
   - Foot height along the ray: 4.55 + d·(sin p − k) = 4.55 − 0.587d.
   - Board height along the ray: 1.25 − 0.248d.
   - Equal at d = 3.3 / 0.339 ≈ 9.7 for p=−8°; the bot's aim point corresponds to d ≈ 9.3, so s = 0.45 × 9.3 = **4.2**.
   - Result: 4.2 wide, 8.4 tall, 1.26 thick, mass 35.6, standing at Z ≈ 18.7. On screen it is visibly taller than the 6-high barricade.
4. Release. The upright domino touches the slope on its uphill edge, rocks onto the board, passes 8.5° and topples toward +Z. It strikes the barricade's top edge at about 50° from vertical:
   - ω² = 3 × 22 × (1 − cos 50.6°) / 8.4 = 2.87, so ω = 1.69 rad/s.
   - Contact speed ≈ 7.25 × 1.69 = 12 u/s.
5. Mass 35.6 ≥ 14 and speed 12 ≥ 2.5, so the barricade breaks and the domino slams flat through the arch.
6. Take the block stair down, pass the domino at X ≈ 3.5 (its half-width is 2.1), and exit.

**E. Tolerances.**
- Scale: s from 3.1 to 6.0. The readable rule is "taller than the barricade": s=3.08 gives mass 14.
- Placement: foot between 1.6 and 0.9 × height in front of the barricade. For s=4.2 that is Z from about 17.5 to 23.4.
- From the balcony edge the whole working window is pitch from about −12.5° to −5°, a 7.5° band. That is why the level starts on a balcony; from the hall floor the same band is under 2°.
- Too small: it topples, bonks, and the barricade shudders in proportion to mass / 14. Re-grab (it snaps upright) and step back or aim farther.
- Falls short: re-grab and place closer.
- Flush against the barricade: no speed, no break. Re-grab.
- Domino blocking the arch after the break (s near 6): re-grab and shrink it at your feet; nothing is locked.

**F. Unintended solutions.**
- Going over: the arch wall is 12 high and the barricade fills the opening.
- Dropping it from mid-air against the wall above the arch: it lands, tips and breaks through. Tolerated; same logic.
- Pushing an upright big domino by walking into it: tolerated; it still needs the mass.
- Lying the domino flat as a battering slab: no speed, no break.
- The clamp of 6 keeps width at or under the 9-wide arch and the 14-wide hall.

**G. Win, gadgets, bot.** Win = `ExitDoor` (0, −2.5, 36).

`BreakableBarricade`:
- Inputs: bounds, accept tag `smasher`, `minMass` 14, `minSpeed` 2.5.
- Each tick: overlap the bounds expanded by 0.15 against the Prop layer. For each tagged, non-held prop, take `GetPointVelocity` at the closest point and measure its component into the barricade face.
- If mass ≥ minMass and that speed ≥ minSpeed: destroy the collider with `Sim.Destroy` and raise `BarricadeBroken(prop, speed)`. Presentation spawns visual-only flying blocks, so no debris can jam the path.
- Otherwise, if speed ≥ 1: raise `BarricadeBonk(mass / minMass)`.
- Output: `Broken`. Deterministic; no collision callbacks.

```csharp
yield return bot.WalkTo(new Vector3(-3, 3, 6.9f));
yield return bot.Grab(domino);
yield return bot.WalkTo(new Vector3(0, 3, 9.5f));
yield return bot.DropAt(new Vector3(0, 3.27f, 18.74f));   // domino center when standing
yield return bot.Until(() => barricade.Broken, 6f);
yield return bot.Wait(1.5f);
yield return bot.WalkTo(new Vector3(6, 3, 9.5f));
yield return bot.WalkTo(new Vector3(6, 0, 15));            // down the block stair
yield return bot.WalkTo(new Vector3(3.5f, -2.5f, 27));
yield return bot.WalkTo(new Vector3(0, -2.5f, 36));
```

**H. Teaching.**
- Blurb: "That wall won't move for something small."
- Hint 1: "A domino only knocks down what it out-weighs. Bigger is heavier — much heavier."
- Hint 2: "Stand the domino on the tilted board, a little way back from the barricade. It has to be taller than the wall and have room to fall."
- Hint 3: "From the balcony edge, hold the domino so its foot sits halfway down the board and its top is above the arch. Let go and watch."

**I. Wow beats.**
- The fall: a house-sized domino leans in slow, heavy silence, its shadow racing down the board ahead of it, then blocks burst outward and the domino lands as a bridge through the arch with a camera shake.
- The view through the broken arch into the next, brighter room, pips glowing on the fallen slab.

**Physics confidence.** Medium. The risks are the domino sliding on the board instead of pivoting, bouncing on its first rock, or the point-velocity sample missing the impact tick. Mitigations: friction 0.7 on both surfaces, and sample over the 3 ticks before the overlap.

**Fallback (`TipAssist`).** A volume over the board. When a `smasher` prop is at rest and upright inside it, the gadget goes kinematic and rotates it about its downhill base edge with ω̇ = (3g / 2h)·sin φ. On reaching the barricade plane it applies the same mass test, breaks or stops, then hands back to physics. The puzzle logic (size and room to fall) is unchanged.

---

## Phase ramp

| Level | New idea | Decisions | Target time | Risk |
|---|---|---|---|---|
| 1 | Far = big | Aim | ~1 min | Low |
| 2 | Size must fit; step back or forward to tune | Stand + aim | 1.5–2 min | Low with socket |
| 3 | Near = small; the mechanic runs both ways | Reverse the habit | 1.5–2 min | Low |
| 4 | Size = mass; placement has consequences | Size + position + physics | 2–4 min | Medium (fallback given) |

**Files.** Read `C:/Users/shank/OneDrive/Desktop/tinkerstoybox/docs/ARCHITECTURE.md`; nothing was written to the repo.
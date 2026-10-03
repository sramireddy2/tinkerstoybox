# Phase 3 — Advanced Mechanics (Levels 10–15): level design spec

**Engine.** Unity 6 / PhysX, conventions from `docs/ARCHITECTURE.md`: left-handed, Y up, yaw 0 looks down +Z, degrees, `PropOptions`, `LevelContext`, `Bot`. Format follows `levels-phase-1-draft.md`.

**Status.** Every number is hand-calculated against the engine source as it stood on 2026-10-03 (`PerspectiveGrabber.cs`, `Prop.cs`, `Player.cs`, `Trigger.cs`). Nothing has been run. Aim points, windows and timings are starting values for the bot tests to tune.

**Progress marker (restart safety).** Sections are appended in order: 0, 10, 11, 12, 13, 14, 15, ramp. The file is complete only when it ends with the line `END OF PHASE 3 SPEC`.

## 0. Shared rules, engine facts and gadgets

### 0.1 Facts read from the engine that these levels lean on

1. **Cone rule.** The hold march goes outward from the eye and stops at the first overlap, and the center ray caps it (`far = min(far, hit.distance)`). A prop only arrives at a pose if the whole cone from the eye to that pose is empty. Every placement below is "aim at a backstop and let it fall the last bit".
2. **A held prop is not in the world.** It sits on the `Held` layer: nothing collides with it, triggers do not sense it, and gadgets must ignore it. Lasers pass through a held card (L11), a held sponge neither soaks nor leaks (L13), a held doorway is not a portal (L14), sockets only test props that are not held.
3. **Mid-air grabs are legal.** `FindTarget` only refuses a prop when the player is grounded on it. A player can jump off a prop and grab it in the air. L12 is designed so that this does not help.
4. **`PropBody.Fixed` + `Grabbable`** stays where it is dropped. L14's doorway builds on it.
5. **`Player.SetScale(s)`** already exists (capsule, eye, speeds, probes scale by s; jump speed by √s). L14 is built on it. With gravity fixed, apex scales by s and jump range by s^1.5.
6. **Kill plane** respawns a dynamic prop at its origin pose *and scale*. In these levels pits have floors and a `HazardZone` for the player, so props that fall stay where they land and can be re-grabbed; nothing reappears on top of something else.

### 0.2 Engine requests (flagged, not assumed)

| # | Request | Used by |
|---|---|---|
| 1 | `PropOptions.SnapUprightOnGrab` (from Phase 1) | 10, 11, 15 |
| 2 | `PropOptions.FrozenUntilGrabbed`: body is kinematic at its authored pose until the first grab, then behaves as `Dynamic` | 12 (key bridge), 10 fallback |
| 3 | `PropOptions.AllowPitch` (default true): when false, `F` does nothing and the prop keeps world-up | 14 (doorway) |
| 4 | `Prop.Dropped` / `Prop.Grabbed` per-prop callbacks inside the tick (today only `game.Events`, which are deferred) | sockets, sponge, doorway |
| 5 | `Player.Respawn` must keep the current player scale and must not drop the held prop | 12, 14 |
| 6 | A gadget hook to add mass to a prop without changing its scale (`prop.ExtraMass`) | 13 (wet sponge) |
| 7 | `PropOptions.KeepUpright`: freeze rotation about X and Z while the body is dynamic | 12 (spool) |
| 8 | `Prop.BeginDrive()` / `EndDrive(velocity)` from the Phase 2 spec: a gadget takes a dynamic prop kinematic, moves it through a `Mover`, and hands it back | 15 (ball run, baseball, seated parts) |
| 9 | A gadget may rotate **child colliders** of a seated prop (the hinged dominoes) and may hide one child and show a separate kinematic body in its place (the catapult's marble). `PropGeometry` is measured once at creation and is not re-measured | 15 |

### 0.3 Gadgets inherited from Phase 1

`ExitDoor`, `SkyCap`, `PropLeash`, `HazardZone` (player feet inside for 0.1 s → respawn at checkpoint, ignores props, has `Enabled`), `PressureButton`. Unchanged.

### 0.4 New shared gadget: `Socket`

Phase 1's `SocketWell` generalized. Levels 12 and 15 use it for the keyhole and the four machine stations.

- **Inputs:** capture volume (box, world pose); accept tag; scale window `[min, max]`; optional yaw tolerance against a reference direction; seated pose as a function of the prop's scale (`Func<float, Pose>`); `easeSeconds` (0.25); `lockOnSeat` (bool: prop becomes non-grabbable).
- **Tick:** for each tagged prop that is not held and whose center is inside the capture volume: if scale and yaw are in the window and the prop's speed is below 3 u/s, capture it: body kinematic, ease position and rotation to the seated pose over `easeSeconds` (pure function of ticks since capture). Otherwise raise `Rejected(prop, reason)` once per drop (`TooBig`, `TooSmall`, `Backwards`) and leave it to physics.
- **Outputs:** `Seated` (bool), `SeatedProp`, events `Seated`, `Unseated`, `Rejected`.
- **Release:** if not `lockOnSeat`, grabbing the seated prop unseats it and restores its body kind.
- **Determinism:** props are visited in `Prop.Id` order; no collision callbacks, no RNG.

### 0.5 Held-prop presentation (for art; same contract as Phase 1)

While held: no cast shadow, no fog, no depth of field, constant-pixel outline, and **no gadget effect drawn on it** — laser beams, water and portal views ignore it exactly as the simulation does. On release the shadow fades in over 0.2 s. Per-level notes say what that reveal should show.

---

## Level 10 — Matryoshka Boxes

**A. Setting.** `gift-nook`: a corner of rug under a bookshelf, with a child's height chart painted up the shelf's edge. *Fantasy: one gift box turns out to be three, and three boxes turn out to be a staircase.*

**B. Layout** (rug floor Y=0, travel is +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Rug floor | −8..8 | −6..16 | top 0 |
| Shelf plank (goal ledge), sheer front face at Z=12 | −8..8 | 12..16 | top 3.9 |
| Walls at X=±8, Z=−6, Z=16 | | | height 12 |
| `SkyCap` | −8..8 | −6..16 | Y=12 |
| Height chart (decal on the ledge face, ticks every 1.0, bands coloured at 1, 2, 3) | −4..0 | 12 | 0..3.9 |

- Spawn (0, 0, −4), yaw 0. Exit (4, 3.9, 14).
- No hazard anywhere; nothing can be lost. `PropLeash` volume = the room.

**C. Props.** One toy, three instances, nested.

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Gift box (shell) | Cube 1.0, open bottom, walls 0.06 thick: five box colliders (lid + four sides). Collider volume 0.272 | all three centered on (0, ·, 0), resting on the rug | density 20 (mass 5.44·s³), friction 0.9, clamps **0.3–2.0**, `SnapUprightOnGrab`, grabbable |
| — outer (red) | | center Y=0.6, scale 1.2 | |
| — middle (yellow) | | center Y=0.45, scale 0.9 | |
| — inner (teal) | | center Y=0.3, scale 0.6 | |

- Cavity of the outer at 1.2 is 1.056 wide, so the middle (0.9) sits inside with 0.08 clearance each side; the middle's cavity is 0.792 for the inner (0.6).
- The grab ray hits the outer first. Once it is held it is on the `Held` layer and passes through the others, so lifting it reveals the next. Order out is forced: red, yellow, teal.
- Masses: 2.8 at s=0.8, 5.4 at s=1.0, 43.5 at s=2.0 (player is 3).
- **The clamp is the puzzle.** No box can be taller than 2.0, the ledge is 3.9, and the player's reliable step-up is 1.15 (apex 1.3). So one box must stand on another.

**D. Intended solution.** Build A (1.0) on the rug, B (1.9–2.0) against the ledge, C (1.0) on top of B at the back.

1. **Red → B.** Stand at (0, 0, −2.6). Eye (0, 1.55, −2.6), box center (0, 0.6, 0): g = √(2.6² + 0.95²) = 2.77, k = 1.2 / 2.77 = 0.434. Grab; the yellow box is left standing there.
2. Walk to (−2, 0, 6.7). Aim at the foot of the ledge face. The box stops when its base meets the rug and its far face meets the ledge (half-size = 0.217·d):
   - d·(−sin p + 0.217) = 1.55
   - d·(cos p + 0.217) = 12 − 6.7 = 5.3
   - Solution: p ≈ −7.9°, d ≈ 4.39. Release: s = 0.434 × 4.39 = **1.90**. B occupies X −2.95..−1.05, Z 10.1..12, top Y=1.9.
3. **Yellow → A.** Stand at (0, 0, −2.3): g = √(2.3² + 1.1²) = 2.55, k = 0.9 / 2.55 = 0.353. Grab, walk back to (−2, 0, 6.7), look down at the rug 2.6 ahead (p ≈ −22°): d·(−sin p + 0.176) = 1.55 gives d = 2.83, s = 0.353 × 2.83 = **1.0**. A occupies Z 8.9..9.9, top Y=1.0, 0.2 short of B.
4. **Teal → C.** Stand at (0, 0, −1.9): g = √(1.9² + 1.25²) = 2.27, k = 0.6 / 2.27 = 0.264. Grab, walk to (−2, 0, 8.0), aim at the height chart above B at Y≈3.0 (p ≈ +22.5°). The box stops when its far face meets the ledge face: d·(cos p + 0.132) = 12 − 8.0 = 4.0, so d = 3.79, s = 0.264 × 3.79 = **1.0**. It is released at center (−2, 3.0, 11.5) and falls 0.6 onto B. C occupies Z 11..12 on B, leaving a 0.9-deep tread at Z 10.1..11.
   - Cone check: at B's front-top edge (Z=10.1, Y=1.9) the lower edge of the cone is at Y=2.22. Clear.
5. Climb: rug → A (rise 1.0) → B's tread (0.9) → C (1.0) → ledge (1.0). Walk to the exit.

**E. Tolerances.** With R = 1.15 as the largest comfortable rise and T = 0.7 as the smallest usable tread:

| Box | Works for | Why |
|---|---|---|
| B | 1.6–2.0 | B + C ≥ 3.9 − R; the clamp caps it |
| A | (B − 1.15)–1.15, i.e. 0.75–1.15 when B=1.9 | step onto it, step off it |
| C | (2.75 − B)–1.15, i.e. 0.85–1.15 when B=1.9; and C ≤ B − 0.7 | reach the ledge, keep a tread |

- B at the clamp is the easy case: stand anywhere 5.6 or more from the ledge and aim at its foot; the box stops growing at 2.0. Then A and C both have a 0.8–1.15 window.
- A's window is a pitch band from −30° to −16° when grabbed as in step 3 — 14° wide.
- The height chart behind the stack is the ruler: a box whose top reaches the "1" band is a step, the "2" band is the big one.
- Any miss: re-grab (the box under your feet excepted) and re-place. Nothing leaves the room.

**F. Unintended solutions.**
- Three boxes side by side on the rug (1, 2, 3 high): blocked by the 2.0 clamp.
- Tower of three: nothing to climb it with.
- B (2.0) on the rug, A (1.15) on B, C (1.15) on A: tops out at 3.15 with a 0.75 rise to the ledge, but A's top has no room for both C and a tread. If a player finds a variant that stands up, it is a staircase; tolerated.
- Jump-and-drop climbing (drop a box under yourself in mid-air): the box is projected to the farthest free point, which is the rug, not your feet. No height gain.
- Dropping a 2.0 shell over yourself: you stand inside the cavity, look at a wall of it, grab it and it is gone. No trap.

**G. Win, gadgets, bot.** Win = `ExitDoor` (4, 3.9, 14). Gadgets: `ExitDoor`, `SkyCap`, `PropLeash`. No new gadget.

```csharp
yield return bot.WalkTo(new Vector3(0, 0, -2.6f));
yield return bot.Grab(red);
yield return bot.WalkTo(new Vector3(-2, 0, 6.7f));
yield return bot.DropAt(new Vector3(-2, 0.95f, 11.05f));   // B center when seated
yield return bot.Wait(0.5f);
yield return bot.WalkTo(new Vector3(0, 0, -2.3f));
yield return bot.Grab(yellow);
yield return bot.WalkTo(new Vector3(-2, 0, 6.7f));
yield return bot.DropAt(new Vector3(-2, 0.5f, 9.4f));      // A center
yield return bot.Wait(0.5f);
yield return bot.WalkTo(new Vector3(0, 0, -1.9f));
yield return bot.Grab(teal);
yield return bot.WalkTo(new Vector3(-2, 0, 8.0f));
yield return bot.DropAt(new Vector3(-2, 3.0f, 11.5f));     // C against the chart, above B
yield return bot.Until(() => teal.Velocity.sqrMagnitude < 0.01f && teal.Center.y < 2.6f, 3f);
yield return bot.WalkTo(new Vector3(-2, 0, 7.4f));
yield return bot.WalkTo(new Vector3(-2, 0, 8.3f)); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(-2, 1.0f, 9.5f)); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(-2, 1.9f, 10.5f)); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(-2, 2.9f, 11.5f)); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(-2, 3.9f, 13));
yield return bot.WalkTo(new Vector3(4, 3.9f, 14));
```

**H. Teaching.**
- Blurb: "One box. Or is it? The shelf is four of you high."
- Hint 1: "Pick the box up. There is another one inside. And another."
- Hint 2: "No box will grow taller than the '2' on the height chart, and you can only hop up about one mark at a time."
- Hint 3: "Make the red box as big as it goes against the shelf. Put a one-mark box on the rug in front of it and another one-mark box on top of it at the back. Hop up: rug, box, box, box, shelf."

**I. Wow beats.**
- The un-nesting: the red box lifts away with no change in apparent size and a yellow one is simply *there*, same ribbon, same bow. The third time should get a laugh — give each box a rising musical note.
- The finished staircase seen from the shelf: three mismatched gift boxes, their long soft shadows lying across the height chart like a bar graph.

**Physics confidence.** Medium-high. Risks: three nested thin-walled compound colliders resting 0.08 apart at load time, and standing on an open-bottomed shell.

**Fallback.** Solid closed boxes plus a scripted `NestedSet`: the middle and inner props are inactive (no collider, hidden) until the box around them is first grabbed, then appear at rest in its place. Same puzzle, no shells.

---

## Level 11 — Blocking Lasers

**A. Setting.** `night-hall`: a corridor of building blocks at night. A mobile of red laser pointers hangs from the ceiling and rains beams straight down across the far half of the floor. *Fantasy: hold up the ace of spades as an umbrella the size of a roof and walk through the rain dry.*

**Reading of the brief.** The lasers shine downward, so the card's "shadow" is literal: the patch of floor with no red dots on it. The card is laid across two low walls as a roof.

**B. Layout** (floor Y=0, travel is +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Corridor floor | −5..5 | −2..23 | top 0 |
| Side walls at X=±5, back wall at Z=−2 | | | height 8 |
| Far wall, with door opening X −1..1, Y 0..2.6 | −5..5 | 23..24 | height 8 |
| Exit tunnel | −1..1 | 24..27 | floor 0, ceiling 2.6 |
| Ceiling slab (laser rig; doubles as `SkyCap`) | −5..5 | −2..24 | underside 8 |
| Low block wall, left | −4..−3 | 13..23 | top 3.0 |
| Low block wall, right | 3..4 | 13..23 | top 3.0 |
| Laser rain footprint | −5..5 | 14..22 | from Y=8 down |
| Card table (spool, radius 0.6) | axis X=0 | Z=3 | top 0.8 |

- The lane between the low walls is 6 wide. The rain also covers the wall tops and the strips outside them.
- Spawn (0, 0, −1), yaw 0, checkpoint at spawn. Exit (0, 0, 25.5).

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Playing card (ace of spades) | 1.4 (X) × 0.04 (Y) × 2.0 (Z), one box collider, **authored lying flat**, long axis along Z | center (0, 0.81, 3) on the table, scale 0.5 (0.7 × 1.0) | density 10 (mass 1.12·s³; 228 at s=5.9), friction 0.8, clamps **0.2–7.0**, `SnapUprightOnGrab`, grabbable |

- Because it starts flat and orientation follows camera yaw only, it is already in roof orientation when grabbed facing +Z. No `F` needed. `F` stands it on end, which is never useful here.
- Max 7.0 gives a 9.8-wide card: it can never wedge between the 10-wide corridor walls.

**D. Intended solution.**
1. Walk to (0, 0, 1.9) and look down at the card. Eye (0, 1.55, 1.9), card center (0, 0.81, 3): g = √(1.1² + 0.74²) = 1.33, k = 0.5 / 1.33 = 0.377. Grab.
2. Without moving, look up at the far wall above the door, at about (0, 6.4, 23): pitch p = atan(4.85 / 21.1) = 12.9°. The flat card slides out along the ray, widening, until its far edge meets the far wall (half-length = 1.0·s = k·d):
   - d·(cos p + k) = 23 − 1.9 = 21.1, so d = 21.1 / (0.975 + 0.377) = 15.6.
   - s = 0.377 × 15.6 = **5.9**. Card is 8.2 wide × 11.8 long, center (0, 5.0, 17.1), spanning Z 11.2..23.0.
   - Clearance of the low walls: the card's half-width reaches their inner faces (3.0) at d = 3 / (0.7k) = 11.4, where its plane is at Y = 1.55 + 11.4·sin 12.9° = 4.1. Above the 3.0 walls; it never touches them on the way out.
3. Release. The card drops 2 units flat onto both walls and lies at Y 3.0..3.24. Every beam over the lane and the walls from Z 11.2 to 23 now ends on the card.
4. Walk around the table, down the lane under the card, through the door.

**E. Tolerances.**
- **Scale:** 4.7 (wide enough to rest on both walls: 1.4s ≥ 6.6) to 7.0 (clamp). Coverage needs the near edge at Z ≤ 14: with the far edge on the wall that is s ≥ 4.5, already implied.
- **Where to stand** (grabbed as in step 1): s = 0.279·(23 − zE), so zE from the back wall (−2) to 6.1. Eight units of depth. Farther back than −1.7 is impossible; the clamp is not reached.
- **Pitch:** 8° to 26°. Below 8° the card meets the low walls' inner faces while narrower than the lane and falls into the lane as a doormat. Above 26° it stops on the ceiling early (s = 6.45k / sin p) and lands short of the door.
- **Grab distance matters.** Grabbed from 3 away, k is 0.17 and the best possible roof is s ≈ 3.6 — too small. This is the lesson of the level: *it has to look big when you pick it up.*
- **Recovery:** the card is always visible and grabbable from the dry half of the corridor, on the walls or on the lane floor. A card lying in the lane is re-grabbed from the zone edge at about 6 away (k ≈ 0.6), which is more than enough. A zapped player returns to spawn; the card stays where it is.

**F. Unintended solutions.**
- Carrying the card over your head: a held prop is not in the world, and the beams are drawn passing through it. The first zap teaches this in one second.
- Sprinting through: 8 units at 8 u/s is 1 s; the zap delay is 0.1 s.
- Climbing the low walls: 3.0 high, and the table (0.8) is 10 away.
- Card on the lane floor, card stood on end, card leaning against one wall: no roof, or a partial one. A lean-to that genuinely covers the whole zone is a shadow and is tolerated.
- Squeezing between beams: the hazard lattice has no gap a capsule fits through (see gadget).

**G. Win, gadgets, bot.** Win = `ExitDoor` (0, 0, 25.5).

`LaserRain` (new; a single-beam `Laser` is the degenerate case with a 1×1 footprint and an arbitrary direction):
- **Inputs:** emitter rectangle (X −5..5, Z 14..22 at Y=8), direction (0, −1, 0), range 8, lattice pitch 0.45 (triangular), `zapDelay` 0.1 s, `Enabled`.
- **Hazard (simulation):** if the player's XZ is inside the footprint, take five sample points on top of the capsule — the axis and four points at 0.8 × radius. For each, raycast from the emitter plane straight down against world + props + player. If the first hit is the player for any sample, the player is `Exposed`. Exposed for `zapDelay` continuously → raise `Zapped`, respawn at checkpoint. Five rays per tick, fixed order, no RNG.
- **Why samples instead of 450 beam rays:** the result is the same as a lattice with no capsule-sized gaps, and it costs five raycasts.
- **Beams (presentation):** the render layer raycasts each lattice beam against world + props (not `Held`) to find its end point and draws beam + dot with GPU instancing. On the low tier it refreshes a third of the beams per frame.
- **Outputs:** `Exposed`, `Zapped`, `Coverage01` (share of lattice beams over the lane that end above Y=2 — drives the hum and the "all clear" chime at 1.0).

```csharp
yield return bot.WalkTo(new Vector3(0, 0, 1.9f));
yield return bot.Grab(card);
yield return bot.DropAt(new Vector3(0, 6.4f, 23f));
yield return bot.Until(() => card.Center.y < 3.4f && card.Velocity.sqrMagnitude < 0.01f, 4f);
yield return bot.WalkTo(new Vector3(1.8f, 0, 3));          // around the table
yield return bot.WalkTo(new Vector3(0, 0, 12));
yield return bot.WalkTo(new Vector3(0, 0, 22.5f));
yield return bot.WalkTo(new Vector3(0, 0, 25.5f));
```

**H. Teaching.**
- Blurb: "It's raining lasers. All you have is a playing card."
- Hint 1: "The beams come straight down. Anything solid above your head stops them — but not while it's in your hand."
- Hint 2: "Those two low walls could hold up a roof. The card would have to be wider than the gap between them."
- Hint 3: "Stand right over the card so it looks big, pick it up, then look at the far wall well above the little door and let go."

**I. Wow beats.**
- The release: an ace of spades the size of a garage door drops onto the walls with a slap, and four hundred red dots on the floor wink out in a card-shaped rectangle. The dots that remain crawl along the card's edges.
- Walking underneath: the card is thin enough to glow. Every beam is a red pinpoint seen through the paper, and the spade is a dark silhouette overhead.

**Held-card note for art.** While held the card takes no laser dots and casts no shadow; beams are drawn straight through it. That is the visual explanation of the rule in F.

**Physics confidence.** High. A heavy flat slab falling two units onto two parallel walls is stable; the only scripted part is the hazard test.

---

## Level 12 — The Keyhole

**A. Setting.** `hallway-drawer`: a landing on top of a chest of drawers. An open drawer is a pit of socks between you and a dollhouse front door set in a sheer wall. A brass toy key the length of a bus lies across the drawer as a bridge. *Fantasy: the thing you are standing on is the thing you need in your hand.*

**Why crossing first does not work.** Any bridge can be grabbed from the far side, so the far side has **no floor**: the door is flush in a sheer wall and the key's tip rests in a slot under the sill that is too low to enter. At the door you are standing on the key, and nothing else. To hold the key at the door you must be standing on something that is not the key.

**B. Layout** (landing top Y=0, travel is +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Landing S | −6..6 | −6..8 | top 0 |
| Drawer pit | −6..6 | 8..20 | floor −9 (socks) |
| Door wall, sheer | −6..6 | 20..22 | −9..12 |
| Key slot in the wall (floor −0.5) | −1.1..1.1 | 20..21.2 | −0.5..0 |
| Door opening, closed by a slab flush at Z=20 | −1..1 | 20..22 | 0.15..2.75 |
| Keyhole | 0 | 20 | 1.35 |
| Exit tunnel behind the door | −1..1 | 20..24 | floor 0.15 |
| Key shelf on the back wall (chest height) | −5..−3 | −6..−5.2 | top 1.3 |
| Side walls X=±6, back wall Z=−6, `SkyCap` | | | Y=12 |

- Spawn (0, 0, −2), yaw 0, checkpoint at spawn. Exit (0, 0.15, 22.5), locked until the key turns.
- `HazardZone` over the pit floor (X −6..6, Z 8..20, Y −9..−7): the player returns to spawn. Props that fall stay on the socks, visible and grabbable from the landing's edge. Nothing reaches the kill plane.

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Toy key | Flat, long axis Z, total length 2.0: bow plate 0.6 (Z) × 0.7 (X) at the −Z end, blade 1.4 × 0.2, thickness 0.04; solid bow (no hole to fall through); three box colliders incl. a small bit tab | center (0, −0.09, 13), tilted 2.4° tip-down, **scale 8**: 16 long, spanning Z 5..21, blade 1.6 wide and 0.32 thick, tip 1.0 into the slot | density 2 (mass 0.056·s³: 28.7 at s=8), clamps 0.05–8, tag `key`, `FrozenUntilGrabbed`, `SnapUprightOnGrab`, grabbable |
| Thread spool | Cylinder radius 0.55, height 1.0, axis Y | (3, 0.5, 2), scale 1.0 | density 0.4 (mass 0.38·s³: 277 at s=9), clamps **0.5–9.6**, `KeepUpright` (engine request 7: freeze rotation about X and Z so it cannot tumble into the pit), grabbable |

**D. Intended solution.**
1. *(Optional look)* Walk the key to the door. The keyhole is at eye level and the key is under your feet.
2. **Shrink the key.** Stand at the key shelf, (−4, 0, −4.9), facing +Z. Eye (−4, 1.55, −4.9), key center (0, −0.09, 13): g = √(4² + 1.64² + 17.9²) = 18.4, k = 8 / 18.4 = 0.435. Grab. The bridge is gone.
3. Turn around and look down at the shelf top, 0.7 ahead and 0.25 below the eye: d = 0.74, s = 0.435 × 0.74 = **0.32** (a key 0.64 long). Release; it lies on the shelf.
4. **Build the other way across.** Walk to (3, 0, 0.8), grab the spool: g = √(1.2² + 1.05²) = 1.6, k = 1.0 / 1.6 = 0.627.
5. Walk to (0, 0, 0.7). Aim at the wall *above* the door, about (0, 5.6, 20): p = 11.9°. The spool stops when its side meets the wall (radius = 0.55·s = 0.345·d):
   - d·(cos p + 0.345) = 19.3, so d = 19.3 / 1.323 = 14.6, s = 0.627 × 14.6 = **9.1**.
   - Why aim up: the spool's base is at Y = 1.55 − 0.106·d. It must stay above the landing until its near side has passed the landing's edge; that needs p ≥ 10.7°.
6. Release. A spool 10 across and 9.1 tall drops 6 units onto the socks, upright. Top at Y = +0.1, near side at Z ≈ 10, far side touching the door wall.
7. Fetch the key: at the shelf, grab from 0.74 away, k = 0.32 / 0.74 = 0.43.
8. Jump the 2-unit gap onto the spool, walk to about 1.0 from the door, look at the keyhole. The key points away from you, so it stops when its tip meets the door: d = D / (1 + k) = 1.0 / 1.43 = 0.70, s = 0.43 × 0.70 = **0.30**. Release: the socket takes it, it turns, the door slides up.
9. Hop up the sill (0.05–1.15 depending on spool height) and exit.

**E. Tolerances.**
- **Key:** the socket accepts 0.06–0.5. With k = 0.43 that is any standing distance from 0.5 to 1.6 from the door. A key grabbed at bridge size from the landing's edge (k ≈ 1.1) still goes in from within 0.9 of the door: s = 1.1·D / 2.1. The shelf is a convenience, not a requirement.
- **Spool:** top must be within 1.15 below the sill and at most 0.6 above the landing: s from 8.0 to 9.6 (clamp). Grabbed as in step 4 that is a standing band of Z −0.5..2.9 and a pitch band of 11°..23°.
- Spool too small (aimed low, or grabbed from far so it looked small): it stops on the landing or makes a short pillar deep in the pit. It is always in view; re-grab and step back — the Level 2 lesson.
- Spool clamped at 9.6 and short of the wall: stand closer and re-place; `KeepUpright` means it never ends up on its side.
- Key rejected as too big: it clinks off the door and lands on the spool or the socks. Re-grab. From the landing's edge a key on the socks is 9+ away, which makes it *smaller* at the door.
- Fell in: `HazardZone` returns the player to spawn, still holding whatever was held.

**F. Unintended solutions.**
- Cross on the key and take it from the far side: there is no far side. Jump off the key and grab it in mid-air (legal): you fall, respawn at spawn holding the key. Same as step 2.
- Posting the key from the landing, 12 away: works if the key looks tiny enough (k ≤ 0.04, i.e. grabbed from 8+ away). The door opens — and you still need the spool to reach it. Tolerated, and rather good.
- Spool placed while the key still bridges the pit: it stops against the key at small size. No room beside the blade for a pillar tall enough (needs radius 4.4, half the drawer is 5.2 wide).
- Spool on its side as a bridge: 9.6 long at most, the pit is 12.
- Key as a ramp from the pit floor: the pit floor is a hazard for the player.
- Re-using the key as a bridge after it is in the lock: the socket locks it.

**G. Win, gadgets, bot.** Win = exit trigger (0, 0.15, 22.5) after `door.Open`.

- `Socket` "keyhole" (see 0.4): capture box 1.2 × 1.2 × 0.9 centered (0, 1.35, 19.6); tag `key`; scale 0.06–0.5; no yaw test; seated pose = blade in the hole, eased over 0.25 s while the scale eases to 0.15; then a 90° turn over 0.4 s; `lockOnSeat`. Raises `Seated` after the turn.
- `SlidingDoor`: kinematic slab 2 × 2.6 × 0.3. Input `Seated`. Slides up 2.7 into the lintel over 0.8 s (ease-out). Raises `Open`, unlocks the exit.
- `HazardZone` (pit), `SkyCap`.

```csharp
yield return bot.WalkTo(new Vector3(-4, 0, -4.9f));
yield return bot.Grab(key);                                   // 18.4 away
yield return bot.DropAt(new Vector3(-4, 1.3f, -5.6f));        // shelf top
yield return bot.Wait(0.5f);
yield return bot.WalkTo(new Vector3(3, 0, 0.8f));
yield return bot.Grab(spool);
yield return bot.WalkTo(new Vector3(0, 0, 0.7f));
yield return bot.DropAt(new Vector3(0, 5.6f, 20f));
yield return bot.Until(() => spool.Center.y < -4f && spool.Velocity.sqrMagnitude < 0.01f, 6f);
yield return bot.WalkTo(new Vector3(-4, 0, -4.9f));
yield return bot.Grab(key);
yield return bot.WalkTo(new Vector3(0, 0, 6.0f));
yield return bot.WalkTo(new Vector3(0, 0, 7.6f)); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(0, 0, 19.0f));
yield return bot.DropAt(new Vector3(0, 1.35f, 20f));
yield return bot.Until(() => door.Open, 4f);
yield return bot.WalkTo(new Vector3(0, 0, 19.4f)); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(0, 0.15f, 22.5f));
```

**H. Teaching.**
- Blurb: "The door is locked. You are standing on the key."
- Hint 1: "You can't pick up what's under your feet, and there is nowhere else to stand over there. The key has to stop being the bridge."
- Hint 2: "Take the key from far away and set it down somewhere close. Then find something else that could fill the drawer."
- Hint 3: "Shrink the key onto the little shelf. Pick the spool up from close, stand a few steps back from the edge, aim well above the door and let go. Carry the key across the spool and hold it up to the lock."

**I. Wow beats.**
- The grab: a bridge with a shadow twelve units long lifts off the drawer without changing size on screen, the shadow snaps off the socks, and it lands on the shelf as a trinket that rings like a dropped coin.
- The lock: the key leaves your hand, slides into the keyhole and turns by itself; tumblers light up one by one behind the brass plate and the door rises.

**Physics confidence.** High for the key (frozen until grabbed, then small). Medium for the spool's fall — a 277-mass cylinder landing flat from 6 units; `KeepUpright` removes the tipping risk.

**Fallback.** Give the pit a `Socket` like Level 2's well: a `pillar`-tagged prop released over the pit with scale 8.0–9.6 is eased onto the pit's center line and lowered to the floor.

---

## Level 13 — Escaping the Fishbowl

**A. Setting.** `fishbowl`: you start on the plastic castle inside a goldfish bowl. Water all round, glass all round, and a glass filter tower on the far side that reaches the rim. *Fantasy: drink the whole bowl with a bath sponge, then wring it out under your own feet and ride the flood over the rim.*

**Water is scripted, not simulated.** A `WaterVolume` is a footprint, a floor height and a number (`Volume`); its surface is flat at `floorY + Volume / Area`. Water moves between volumes and the sponge by bookkeeping. Nothing here depends on emergent fluid behaviour.

**B. Layout** (gravel floor Y=0; the bowl's axis is the Y axis through the origin).

| Element | Geometry |
|---|---|
| Bowl glass | 24-sided prism, interior radius 10, Y 0..8 (rim at 8); static, solid, transparent |
| `SkyCap` | Y=12 |
| Castle island (spawn) | X −2..2, Z −9.4..−6, top Y=3.0 |
| Castle stair, on the island's +X flank | X 2..3.5: Z −9.4..−8.2 top 2.0; Z −8.2..−7.0 top 1.0; then gravel |
| Filter tower (glass, 0.3 thick) | interior X −1.5..1.5, Z 6.7..9.7; side and front walls to Y=10.5; its back wall is the bowl glass (rim 8) |
| Tower doorway, in the front (−Z) wall at Z 6.4..6.7 | X −1..1, Y 0..2.4 |
| Cork float, fills the tower floor | 2.8 × 2.8 × 0.4, top flush with the gravel at rest (tower floor recessed to −0.4) |
| Exit ledge outside the bowl (a stack of books) | X −3..3, Z 10..15, top Y=8 |

- Bowl water: `Area` 270, initial `Volume` 405 → surface at **Y=1.5**. The island (3.0) and the top stair (2.0) are dry.
- Tower water: `Area` 9, initial `Volume` 0, overflows back into the bowl when its surface reaches 7.9.
- Spawn (0, 3, −9), yaw 0, checkpoint at spawn. Exit (0, 8, 13).
- The player wades up to 0.8 deep; deeper for 0.3 s → back to the island.

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Bath sponge | Box 1.0 (X) × 0.5 (Y) × 0.7 (Z), volume 0.35 | center (0, 3.5, −6.9) on the island's front edge, **scale 2.0** (2.0 × 1.0 × 1.4), dry | density 0.15 dry (mass 0.0525·s³), friction 0.9, clamps **0.5–12**, `SnapUprightOnGrab`, tag `sponge`, grabbable |
| Cork float | see layout | | kinematic, not grabbable |

- `Capacity(s) = 0.315·s³` (90% of its bulk). Dry at the start.
- Wet mass: `ExtraMass = 0.5 × Stored` (engine request 6). A full 9.5-scale sponge weighs 45 + 135 = 180. It is "heavy" to anything that pushes it; in the hand it is weightless like every held prop.
- Displacement is ignored: a sponge sitting in the bowl does not raise the water around it.

**D. Intended solution.**
1. Walk to (0, 3, −8.2), facing +Z. Eye (0, 4.55, −8.2), sponge center (0, 3.5, −6.9): g = √(1.3² + 1.05²) = 1.67, k = 2.0 / 1.67 = 1.20. Grab — it fills most of the view.
2. Step to the island's front edge, (0, 3, −6.4). Look down into the bowl at about −16°. The sponge stops when its underside meets the gravel (half-height = 0.25·s = 0.30·d):
   - d·(0.30 − sin p) = 4.55, so d = 4.55 / 0.574 = 7.9, s = 1.20 × 7.9 = **9.5**.
   - The sponge is 9.5 × 4.75 × 6.65, center (0, 2.4, 1.2), spanning Z −2.1..4.5.
3. Release. It absorbs at 150 units/s up to its capacity, 0.315 × 857 = 270. The bowl is left with 135: surface at **Y=0.5**. About two seconds.
4. Go down the castle stair, wade round the sponge on the +X side to (6.5, 0, 5).
5. Grab the soaked sponge: distance 7.6, k = 9.5 / 7.6 = 1.25. It keeps its water while held.
6. Walk through the tower doorway onto the cork, to (0, 0, 7.6). Look down at the cork ahead (p ≈ −45°): d = 1.55 / (0.707 + 0.31) = 1.5, s = 1.25 × 1.5 = **1.9**. Release.
7. Capacity is now 0.315 × 6.9 = 2.2 and it holds 270. It wrings itself out at 40 units/s into the tower. The surface rises at up to 3 u/s, the cork rides it, and you ride the cork: 8 units in under 3 seconds. The surplus pours over the tower's lip back into the bowl.
8. At the top, walk off the cork over the rim onto the books. Exit.

**E. Tolerances.**
- **Soak:** wading needs the bowl at or below 0.8: absorb ≥ 189, so **s ≥ 8.4**. Upper bound is whatever fits: at shallow pitch the sponge stops against the tower at s ≈ 11, or at the clamp, 12. All of that works (capacity up to 544, more than the bowl holds).
- From the island's edge, grabbed as in step 1, that is any pitch from **−20° upward**. Steeper gives a smaller sponge and a half-drained bowl.
- **Aiming from the middle of the island** instead of the edge stops the sponge on the island itself at s ≈ 3. Obvious miss; step forward.
- **Wring:** the tower needs 71 units. Any release inside the tower at s ≤ 8 delivers that; nothing bigger than about 3 fits in there anyway.
- **Half-drained bowl:** re-grab the sponge from the island (it keeps what it holds) and place it farther out; a bigger sponge resumes drinking.
- **Wrung out in the wrong place:** the water goes into whatever volume is underneath — in practice back into the bowl. If that puts you out of your depth you are returned to the island and start again with a dry sponge that is still in view.
- **Wrung into the tower without you on the cork:** the cork goes up and comes back; the tower leaks 3 units/s into the bowl, so it is down again within 24 s. From the island the sponge is visible through the doorway.

**F. Unintended solutions.**
- Sponge as a stepping stone to the tower: any sponge touching the water drinks it, which is the solution.
- Sponge as a step over the rim: lying flat its top is at most 6.0 (clamp 12); a jump from there reaches 7.3; the rim is 8. And there is no way onto it.
- Squeezing the sponge through the doorway from outside: covered above; it costs time, not the level.
- Standing beside the cork in the tower: the cork fills the floor; inside the tower you are on it.
- Jumping off the rising cork onto the rim elsewhere: tower walls on three sides go to 10.5.

**G. Win, gadgets, bot.** Win = `ExitDoor` (0, 8, 13).

`WaterVolume` (new):
- **Inputs:** footprint (cylinder or box), `floorY`, `Area`, initial `Volume`, `maxSurfaceY` with `overflowTo` (another volume, or null = drain), `leakTo` + `leakRate`, `wadeDepth` 0.8, `drownDelay` 0.3 s.
- **State/outputs:** `Volume`, `SurfaceY`, `Depth`; `Add(v)` and `Take(v)` return what actually moved; events `LevelChanged`, `Overflowing`, `PlayerSwept`.
- **Tick:** apply leak, clamp to max and pass the excess on, then test the player: feet inside the footprint and more than `wadeDepth` below `SurfaceY` for `drownDelay` → respawn at checkpoint. Optional buoyancy for props tagged `floats` (not used here).
- Bowl: cylinder r=10, floor 0, Area 270, Volume 405. Tower: box 3 × 3, floor 0, Area 9, Volume 0, max 7.9 → bowl, leak 3/s → bowl.

`Sponge` (new; behaviour attached to a prop):
- **Parameters:** `capacityPerScale3` 0.315, `absorbRate` 150/s, `squeezeRate` 40/s, `massPerUnit` 0.5.
- **Tick, only while not held:** find the `WaterVolume` whose footprint contains the prop's center (the tower is tested before the bowl). If the prop's lowest point is below that surface and `Stored < Capacity`, take `min(absorbRate·dt, Capacity − Stored)`. If `Stored > Capacity`, give `min(squeezeRate·dt, Stored − Capacity)` to that volume. Update `ExtraMass`.
- **Outputs:** `Stored`, `Saturation01`; events `Soaking`, `Wringing`, `Dry`, `Full`.

`FloatPlatform` (new): kinematic `Mover`. `topY = clamp(volume.SurfaceY + 0.1, restY, maxY)`, moved at no more than 3 u/s. Outputs `AtTop`, `AtRest`. While `!AtRest` a kinematic `Flap` closes the tower doorway, so nothing gets under the cork.

```csharp
yield return bot.WalkTo(new Vector3(0, 3, -8.2f));
yield return bot.Grab(sponge);
yield return bot.WalkTo(new Vector3(0, 3, -6.4f));
yield return bot.DropAt(new Vector3(0, 2.4f, 1.2f));          // center when seated at s = 9.5
yield return bot.Until(() => bowl.Depth <= 0.8f, 6f);
yield return bot.WalkTo(new Vector3(2.75f, 3, -8.8f));
yield return bot.WalkTo(new Vector3(2.75f, 2, -7.6f));        // down the stair
yield return bot.WalkTo(new Vector3(2.75f, 0, -6.0f));
yield return bot.WalkTo(new Vector3(6.5f, 0, 0));
yield return bot.WalkTo(new Vector3(6.5f, 0, 5.0f));
yield return bot.Grab(sponge);
yield return bot.WalkTo(new Vector3(0, 0, 5.6f));
yield return bot.WalkTo(new Vector3(0, 0, 7.6f));
yield return bot.DropAt(new Vector3(0, 0.4f, 9.0f));
yield return bot.Until(() => cork.AtTop, 8f);
yield return bot.WalkTo(new Vector3(0, 8, 13));
```

**H. Teaching.**
- Blurb: "Wind-up toys don't swim. Sponges do the opposite."
- Hint 1: "A sponge holds as much as its size. Make it big where the water is."
- Hint 2: "A full sponge that suddenly gets small has to put the water somewhere — wherever it happens to be standing."
- Hint 3: "From the castle's edge, drop the sponge into the middle of the bowl as big as you can. When the water is gone, carry the sponge into the glass tower, stand on the cork, and drop it at your feet."

**I. Wow beats.**
- The soak: a sponge the size of a house lands in the bowl, darkens from lemon to amber from the bottom up, and the water line slides down the glass and down the castle walls. A wind-up goldfish is left ticking on the gravel.
- The wring: inside the glass tower the sponge collapses to a brick, the tower fills like a syringe, and you rise past the bowl's rim while the surplus arcs back into the bowl behind you as a waterfall.

**Notes for art.** The water surface is one flat mesh per volume with a hand-written transparent shader; refraction and depth fade must not need a depth prepass on the low tier (use a vertex-height tint instead). The held sponge is exempt from water tint and caustics like any held prop. Saturation is a shader parameter on the sponge driven by `Saturation01`.

**Physics confidence.** Medium-high: the only rigid-body event is a large box dropping a short distance; everything wet is bookkeeping. The risk is feel — the ride must not jitter. The cork is kinematic for that reason.

**Fallback.** Drop the ride. The tower becomes a dry moat (Area 40, floor −2) between the bowl floor and a deck with the exit; a kinematic cork raft in it rises 2 units when the moat is flooded and bridges it. Same two-step logic, no vertical travel.

---

## Level 14 — The Infinite Hallway

**A. Setting.** `door-hall`: a dollhouse hallway papered with a pattern of little doors, and one loose door frame standing in the middle of it. *Fantasy: a door is always the right size for whoever walks through it — so walk through, and be that size.*

**How "a larger or smaller version of the same room" is built.** There is one room. Stepping through the doorway **rescales the player** to the doorway's scale, about the doorway's threshold. Being 0.3 of your size in this hall is exactly being in a hall 3.3 times bigger. `Player.SetScale` already exists. No second room, no streaming, and the puzzle logic is honest: what you saw through the frame is where you are.

**The rule the player learns:** *you come out the size the door is.* A door as tall as the mouse-hole makes you mouse-sized; a door as tall as the room makes you a giant.

**B. Layout** (floor Y=0, travel is +Z; halls A and B are dressed identically).

| Element | X | Z | Y |
|---|---|---|---|
| Hall A | −5..5 | 0..14 | floor 0 |
| Dividing wall, full height | −5..5 | 14..15 | 0..11 |
| Mouse-hole through it | −0.35..0.35 | 14..15 | 0..0.8 |
| Hall B, lower floor | −5..5 | 15..25 | floor 0 |
| Terrace (sheer face at Z=25) | −5..5 | 25..31 | top 3.0 |
| End wall, with exit opening X −0.8..0.8, Y 3.0..6.0 | −5..5 | 31..32 | 0..11 |
| Exit tunnel | −0.8..0.8 | 32..35 | floor 3.0 |
| Side walls X=±5, back wall Z=0; ceiling doubles as `SkyCap` | | | Y=11 |
| `RecallPad` (floor plate, radius 0.6) | −3.5 | 16.5 | 0 |

- Spawn (0, 0, 2), yaw 0. Exit (0, 3, 33.5). No hazards, no pits.
- Size gates: mouse-hole needs player scale **P ≤ 0.45** (1.7P < 0.78). Terrace needs **P ≥ 2.4** (apex 1.3P ≥ 3.1). Exit opening needs **P ≤ 1.7** (1.7P < 2.95).

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Doorway | Frame 1.6 (X) × 2.5 (Y) × 0.3 (Z); opening 1.2 × 2.2; two posts and a lintel (three box colliders); origin at the threshold center | standing at (0, 0, 8) facing −Z, scale 1.0 | `PropBody.Fixed`, clamps **0.1–3.2**, `AllowPitch = false`, grabbable; colliders ignore the player capsule permanently (the player can step "into" a door smaller than they are) |

Its center is 1.25·s above its base, so held at distance d it reaches 1.25·k·d above and below the view ray.

**D. Intended solution.** Three passes: small, big, medium.

1. **Small.** Walk to (0, 0, 3). Door center (0, 1.25, 8): g = 5.0, k = 1.0 / 5.0 = 0.20. Grab. Look down at the floor just ahead (p ≈ −60°). The frame stops when its base meets the floor: d·(−sin p + 1.25k) = 1.55, so d = 1.55 / (0.866 + 0.25) = 1.39, s = 0.20 × 1.39 = **0.28**. A door 0.7 tall stands at (0, 0, 3.7).
2. Walk into it. P becomes 0.3 (player clamp). Eye height 0.47. The hall is a cathedral.
3. Turn, grab the door from 0.43 away: k = 0.28 / 0.43 = 0.65. Carry it through the mouse-hole to (0, 0, 16).
4. **Big.** Look *up* at about 60° and let the door rise. Its base is at 0.47 + d·(sin p − 0.81): for p above 54° it never touches the floor, so it grows until the clamp: d = 3.2 / 0.65 = 4.9, s = **3.2**. Release; it settles 0.7 straight down to the floor at (0, 0, 18.5), 8.0 tall, 5.1 wide.
5. Walk through. P becomes 3.2 (5.4 tall, eye at 4.96). You come out at about Z=20.
6. Run and jump onto the terrace (apex 4.2 over a 3.0 face). Stop at (0, 3, 26.5).
7. **Medium.** Look back down at the door: g = √(8.0² + 4.0²) = 9.0, k = 3.2 / 9.0 = 0.36. Grab, turn to face the exit, look down at the terrace floor (p ≈ −60°): d = 4.96 / (0.866 + 0.45) = 3.8, s = 0.36 × 3.8 = **1.35**. The door stands at (0, 3, 28.4).
8. Walk through (P = 1.35, 2.3 tall) and on into the exit.

**E. Tolerances.**
- **Small:** needs s ≤ 0.45. Grabbed from 5 away: any pitch below −26°. Grabbed from closer than 2.3 it cannot be done in one pass (straight down gives 0.57 from 1.5 away) — walk through anyway, and from the smaller size do it again: 0.57 → 0.18. *That is the recursion, and the level is built so that it always converges.*
- **Big:** needs s ≥ 2.4. Any pitch above 54° reaches the 3.2 clamp. Aiming lower gives a floor-limited door (40° → 1.8). Walk through it and repeat from the new eye height: at P=1.8 a 30° aim already reaches the clamp. Two passes at worst.
- **Medium:** needs s ≤ 1.7. From the terrace, any pitch below −30°.
- **Blocked exits never hurt:** if the far side of the door has no room for the new you, the door returns you out of the side you came in (turned around); if neither side has room, it does nothing and its opening shows "no".
- **Lost door:** standing on the `RecallPad` for 0.5 s brings the doorway to the pad at your current scale. The pad is in hall B because that is the only place a door can be out of sight (deep on the terrace) from a player who cannot climb. In hall A the door is always visible.

**F. Unintended solutions.**
- Crossing the mouse-hole at full size: 0.8 high.
- Climbing the terrace without growing: nothing else to stand on; the frame is not solid to the player and cannot be laid flat (`AllowPitch = false`).
- Entering the exit as a giant: 3.0-tall opening against a 5.4-tall player.
- Staying tiny to the end: a tiny player cannot climb the terrace. Staying giant: cannot exit. All three sizes are needed, in that order.
- Projecting the door through the mouse-hole or onto the terrace from below: allowed; it changes where the door is, not what size you must be.
- Going big in hall A: you cannot pass the hole; the door is with you; shrink again.

**G. Win, gadgets, bot.** Win = `ExitDoor` (0, 3, 33.5).

`PortalDoorway` (new; behaviour attached to the doorway prop):
- **Parameters:** player-scale clamp 0.3–3.2, `cooldown` 0.5 s, settle speed limit 30 u/s.
- **Settle (on drop):** the prop is kinematic. Box-cast its footprint straight down and lower it at gravity until the base rests on a surface; it never rotates. Raises `Settled`. While held or settling the portal is inactive.
- **Trigger:** the opening is a thin box (1.2s × 2.2s × 0.3s). A pass happens on the tick the player's capsule overlaps it and the capsule's axis crosses the door's mid-plane.
- **Pass:** `newScale = clamp(s, 0.3, 3.2)`, `r = newScale / P`. Exit point = on the far side, `0.3·newScale + 0.15·s + 0.05` beyond the plane, lateral offset scaled by r and clamped to the opening. Test a capsule of the new size there against world + props (not the doorway). Free → `SetScale(newScale)`, `Teleport(exit)`, velocity × r. Blocked → test the mirrored point on the entry side and, if free, exit there with yaw + 180°. Both blocked → raise `Blocked`, do nothing.
- **Outputs:** `Settled`, `TargetScale`, events `Crossed(from, to)`, `Blocked`.
- **Determinism:** pure function of player and prop state; one capsule test per candidate side.

`RecallPad` (new): overlap disc; player on it for 0.5 s and doorway not held → doorway is moved to the pad at `player.Scale`, facing the player, and settles. Raises `Recalled`.

```csharp
yield return bot.WalkTo(new Vector3(0, 0, 3), 0.1f);
yield return bot.Grab(door);
yield return bot.DropAt(new Vector3(0, 0.35f, 3.69f));
yield return bot.Until(() => portal.Settled, 2f);
yield return bot.WalkTo(new Vector3(0, 0, 4.3f), 0.1f);
yield return bot.Until(() => bot.Player.Scale < 0.45f, 2f);
yield return bot.Grab(door);
yield return bot.WalkTo(new Vector3(0, 0, 13.5f), 0.1f, 15f, true);
yield return bot.WalkTo(new Vector3(0, 0, 16.0f), 0.1f, 15f);
yield return bot.DropAt(new Vector3(0, 4.73f, 18.46f));       // 60 degrees up
yield return bot.Until(() => portal.Settled, 3f);
yield return bot.WalkTo(new Vector3(0, 0, 19.0f), 0.1f, 15f);
yield return bot.Until(() => bot.Player.Scale > 2.4f, 2f);
yield return bot.WalkTo(new Vector3(0, 0, 21.0f), 0.5f); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(0, 3, 26.5f), 0.5f);
yield return bot.Grab(door);
yield return bot.DropAt(new Vector3(0, 4.69f, 28.39f));       // 60 degrees down, facing +Z
yield return bot.Until(() => portal.Settled, 2f);
yield return bot.WalkTo(new Vector3(0, 3, 29.5f));
yield return bot.Until(() => bot.Player.Scale < 1.7f, 2f);
yield return bot.WalkTo(new Vector3(0, 3, 33.5f));
```

**H. Teaching.**
- Blurb: "This door fits everyone. That's the problem, and the answer."
- Hint 1: "Walk through the door and you come out the size the door is. Look through it first."
- Hint 2: "A door the size of the mouse-hole makes you the size of a mouse. If one trip isn't enough, pick the door up again and take another."
- Hint 3: "Grab the door from across the hall and drop it at your feet; walk in. Carry it through the mouse-hole, look almost straight up and let it grow; walk in and jump the ledge. From up there, grab it again, drop it at your feet, walk in, leave."

**I. Wow beats.**
- The first look down: a door no taller than your shin, and inside it — live, in perspective, correctly lit — the hall you are standing in, vast, its ceiling lamps like suns. Stepping in has no cut: the frame swallows the screen and the room is simply that big.
- Tiny to giant: the door climbs out of your hand like a monolith until it hits its limit, you walk under an eight-unit lintel, and the hall drops away beneath you to dollhouse scale. The mouse-hole you crawled through is a dot beside your shoe.

**Portal rendering (must be decided with the art direction).**
- High tier: one extra camera into a half-resolution render texture, positioned at `doorBase + (eye − doorBase) × newScale / P` (rotated 180° about the door's up axis when the exit will be a turn-around), drawn on the opening with a screen-space-UV shader. Active only when settled and within 25·s of the eye. Its material lives under `Resources`.
- Low tier: no second camera. The opening shows a flat card: a toy-figure silhouette drawn at the size you will be, against the hall's wallpaper at that scale. The puzzle reads the same.
- While held, the opening is an opaque glowing card on both tiers — no depth cue, no extra camera.

**Physics confidence.** Medium. Risks: rescaling the player next to geometry (handled by the capsule test and the turn-around), controller tuning at P=0.3 and P=3.2, and the bot's walk tolerance at small scale.

**Fallback.** Quantize: when the doorway settles its scale snaps to the nearest of three bands (0.3 / 1.0 / 3.2) and the frame changes colour. Same puzzle, three well-tested player sizes instead of a continuum.

---

## Level 15 — The Rube Goldberg Machine

**A. Setting.** `night-light` (the finale preset in `ART_BIBLE.md`): the strip of floorboards along the skirting board, at night, seen from the edge of a quilt. Somebody built a chain-reaction machine down there and left it unfinished: four painted outlines are empty. *Fantasy: you finish the machine with four toys that are all the wrong size, stomp one pedal, and watch ten seconds of cause and effect end the game for you.*

**How the level works.** Four toys, four stations, one pedal. Each station seats its toy (`Socket`, section 0.4) and each link of the chain applies one honest rule of scale. The pedal can be pressed at any time: the motion runs as far as it can, stops visibly at the first wrong link, and the machine resets itself with every toy still where the player put it. **Running the machine is the diagnostic.** Every exotic motion is a scripted, closed-form path; the rules that decide pass or fail are real formulas of scale.

**The chain.** pedal → gate → **ball** rolls down the chute, bounces off a drum over a wall of books, lands on the **ruler** → the ruler flips a marble up through a hoop → the marble closes a switch → the **fan** blows down a duct → the puff spins the sail on the first **domino** → seven dominoes fall, each bigger than the last → the last one swats a baseball off a shelf → the baseball drops through the neck of a bell jar onto an alarm clock.

**B. Layout** (trench floor Y=0; the machine runs along +X at the foot of the back wall, centerline Z=18.25).

| Element | X | Z | Y |
|---|---|---|---|
| Gallery (quilt edge, start), sheer face at Z=11.5 | −28..28 | −6..11.5 | top 3 |
| Ramp notch in the gallery, 26.6° | 6..9 | 5.5..11.5 | 3 → 0 |
| Trench floor (walkway is Z 11.5..16.5, machine band is Z 16.5..20) | −28..28 | 11.5..20 | top 0 |
| Walls at X=±28, Z=−6, Z=20; `SkyCap` | | | height 16 |
| Door in the +X wall, tunnel X 28..32 | 28 | 12.5..15.5 | 0..3 |
| Pedal (disc, radius 0.7) on the gallery | 2 | 9.5 | 3 |
| Book pedestal for the dominoes (2.2 × 0.6) | 14 | 6 | 3..4.1 |
| Spool pedestal for the ball (radius 0.4) | −18 | 6 | 3..4.1 |

Machine band, left to right:

| Element | Geometry |
|---|---|
| Slide tower | X −21..−15, Z 16.5..20, body to Y=6.0. Hopper funnel on top: axis (−18, ·, 18.3), throat radius 1.6 at Y=6.0, mouth radius 2.0 at Y=6.6. Reject door at its foot facing the walkway |
| Chute (two rails 1.0 apart) | from (−18, 6.0) down to the lip at (−14.5, 3.55): 35°, length 4.27 |
| Drum | cylinder radius 1.8, axis at X=−12.45, head at Y=1.2 |
| Wall of books | X −9.7..−8.7, top Y=2.4 |
| Fulcrum (fat marker lying along Z) | axis at X=−5.0, Z 17.25..19.25, radius 0.4, top Y=0.8 |
| Hoop (horizontal ring, radius 0.9) | center (−2.9, 6.0, 18.25), on an arm from the control tower |
| Control tower | X −1.5..3.5, to Y=7.0, gabled roof above (nothing can rest on it). "Garage" niche in its front face: X 0.5..2.0, Y 0..1.6, Z 16.5..17.9 |
| Duct (bendy straw, static, radius 0.4) | from the back of the garage up to a nozzle whose tip is at (8.5, 5.6, 18.25), pointing +X |
| Plinth | X 8..21, Z 16.5..20, top Y=5. **Lane** = X 8.5..20 on top. End stop: block X 20..20.4, Y 5..5.6. Striped "wind zone": X 8.5..13.5 |
| Shelf plank on wall brackets | X 21.2..24.5, Z 17.25..19.25, top Y=6.8, 0.2 thick |
| Baseball | center (22.0, 7.4, 18.25), in a ring of putty |
| Bell jar over the alarm clock | axis at X=26.2. Body radius 1.7, Y 0..5.2; neck: throat radius 0.7 at Y=5.2 flaring to a mouth of radius 1.3 at Y=6.0. Clock inside: body radius 1.3, bell button at Y=2.8 |

- Spawn (0, 3, 0), yaw 0, checkpoint at spawn. Exit box 3 × 3 × 3 centered (30, 1.5, 14), locked until the clock is crushed.
- No hazards and no pits. The trench is reached by the ramp and nothing can be lost in it.
- The plinth (5) is 2 above the gallery (3) across a 5-wide trench: out of jumping reach, by height.
- From the pedal the whole machine is in view: the trench floor at Z=18.25 is 26° below the eye, the gallery edge 32°.

**C. Props.**

| Prop | Authored size (scale 1) | Start | Options |
|---|---|---|---|
| Bouncy ball | Sphere, radius 0.5 (volume 0.524) | center (−18, 4.4, 6) on the spool, **scale 0.6** | density 4 (mass **2.094·s³**: 0.45 at 0.6, 56.5 at 3.0), friction 0.6, bounciness 0.5, clamps **0.6–3.0**, tag `ball`, grabbable |
| Desk fan | 1.0 (X) × 1.2 (Y) × 0.7 (Z): base box 0.7 × 0.1 × 0.7, stem 0.12 × 0.25 × 0.12, guard box 1.0 × 1.0 × 0.3 centered at Y=0.7; volume 0.353 | base center (−22.6, 0, 13.6), guard facing −Z, **scale 5** (5 wide, 6 tall, 3.5 deep) | density 0.3 (mass 0.106·s³: 13.2 at 5), clamps **0.5–5**, `SnapUprightOnGrab`, tag `fan`, grabbable |
| Catapult ruler | Plank 4.0 (Z) × 0.6 (X) × 0.08 (Y); bottle cap (radius 0.3) on the +Z end with a marble (radius 0.15) in it, both children of the toy; volume 0.25 | center (−9, 0.04, 14) flat on the trench floor, long axis along X, **cap toward −X**, scale 1.0 | density 0.6 (mass 0.15·s³), clamps **0.6–1.2**, `SnapUprightOnGrab`, tag `ruler`, grabbable |
| Domino set | Seven dominoes hinged on a strip 3.2 (X) × 0.05 × 0.8 (Z), smallest at −X. See below. Volume 0.544 | center (14, 4.55, 6) on the book, long axis along X, **scale 0.6** | density 0.8 (mass 0.435·S³: 18.7 at 3.5), clamps **0.5–3.5**, `SnapUprightOnGrab`, tag `dominoes`, grabbable |
| Baseball | Sphere, radius 0.6 | on the shelf | density 10 (mass 9.05), kinematic until struck, **not grabbable** |

The domino set, at scale 1 (X measured from the strip's small end; height × 0.5 wide × 0.15 thick; each is 1.3 times the one before; gap to the next = 0.55 × its height):

| # | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|
| Height | 0.30 | 0.39 | 0.51 | 0.66 | 0.86 | 1.11 | 1.45 |
| Near face at X | 0.15 | 0.36 | 0.63 | 0.99 | 1.45 | 2.05 | 2.83 |

- Domino 1 carries a paper sail (a pinwheel, visual) at 0.6 of its height. Domino 7's far face is at X=3.05; its hinge is the bottom edge of that face. Bounding box 3.2 × 1.5 × 0.8, so held at distance d it reaches 0.75·k·d above and below the ray and 0.4·k·d toward a wall.
- The dominoes are spring-latched: nothing but the sail lets them go. Pushing them does nothing, and when the machine resets they click back upright.
- Why the clamps: the ball at 3.0 just fits the hopper throat (3.2); the fan can never be bigger than it starts; the ruler at 1.2 is 4.8 long, shorter than the 5.4 from the gallery edge to the plinth edge, so it is never a bridge; the domino set at 3.5 is 11.2 long on an 11.5 lane.

**D. Intended solution.** Any order works. The bot does dominoes, ball, ruler, fan.

1. **Dominoes: grow, sized by a backstop.** Stand at (14, 3, 4.3). Eye (14, 4.55, 4.3), set center (14, 4.55, 6): g = 1.70, k = 0.6 / 1.70 = **0.353**. Grab; walk round the book to the gallery edge at (14.25, 3, 11.1). The back wall is D = 8.9 away.
2. Aim at the wall above the lane, at (14.25, 8.9, 20): pitch +26°. The set stops when its far side meets the wall (half-depth 0.4·S = 0.141·d):
   - d·(cos 26° + 0.141) = 8.9, so d = 8.9 / 1.040 = 8.56 and S = 0.353 × 8.56 = **3.02**. The strip is 9.7 long; domino 7 is 4.4 tall.
   - Underside at 4.55 + 8.56 × (0.438 − 0.264) = 6.04, one unit above the lane.
   - Cone check: the set's far-bottom edge passes the plinth's front edge (Z=16.5) at d = 5.19, where its underside is at 5.45. Clear by 0.45. Aiming *at* the lane instead of above it stops the set on the plinth's face.
3. Release. It drops onto the lane; the `lane` socket slides it 0.9 along its rails until the big end meets the end stop. Strip X 10.3..20; domino 1 at X = 20 − 3.05 × 3.02 = 10.8, inside the wind zone; domino 7's hinge at (19.53, 5.15).
   - Reach check: hinge to baseball = √(2.47² + 2.25²) = 3.34; domino 7 is 4.37 tall. It reaches.
4. **Ball: grow, capped by the clamp.** Walk to (−18, 3, 4.5). Eye (−18, 4.55, 4.5), ball center (−18, 4.4, 6): g = 1.51, k = 0.6 / 1.51 = **0.398**. Grab; walk round the spool to the edge at (−18, 3, 11.1).
5. Aim at the wall above the hopper, at (−18, 10.8, 20): pitch +35°. The wall would allow 0.398 × 8.9 / (0.819 + 0.199) = 3.5; the clamp stops it at **3.0**, at d = 7.54.
   - Ball center (−18, 8.88, 17.27): 1.0 from the hopper's axis, underside at 7.38, above the rim (6.6). It cleared the funnel's front rim with its underside at 6.93.
6. Release. It drops into the funnel and the `hopper` socket seats it behind the gate. Mass 2.094 × 27 = **56.5**.
7. Walk to the ramp (6..9, 5.5..11.5) and down into the trench.
8. **Ruler: same size, right way round.** Stand at (−5.5, 0, 14) facing −X. Eye (−5.5, 1.55, 14), ruler center (−9, 0.04, 14): g = √(3.5² + 1.51²) = 3.81, k = 1.0 / 3.81 = **0.262**. Grab. It lies along the view with the cap at the far end.
9. Step to (−5, 0, 14.4) and face the fulcrum. The ruler now points at the wall, cap first. Press `Q` six times (90° to the right): cap toward +X, toward the hoop. Aim at the top of the marker, (−5, 0.88, 18.25): d = √(3.85² + 0.67²) = 3.91, s = 0.262 × 3.91 = **1.02** (4.1 long).
10. Release. The `fulcrum` socket slides it so that a third of its length is on the short side of the marker and lets the cap end rest on the floor. Short arm 1.36, long arm 2.72; short tip at Y=1.2.
11. **Fan: shrink.** Walk to (1.25, 0, 13) and look west along the trench. Eye (1.25, 1.55, 13), fan center (−22.6, 3.0, 13.6): g = √(23.85² + 1.45² + 0.6²) = 23.9, k = 5 / 23.9 = **0.209**. Grab.
12. Turn to the garage and aim at its back, low: (1.25, 0.6, 17.2), pitch −12.7°. The fan stops when its base meets the floor (half-height 0.6·s = 0.125·d): d·(sin 12.7° + 0.125) = 1.55, d = 1.55 / 0.345 = 4.49, s = 0.209 × 4.49 = **0.94**. It is 0.94 wide and 1.13 tall, center at Z=17.4: inside the garage (1.5 wide, 1.6 high, back at 17.9). Release; the `collar` socket seats it.
13. Walk back up the ramp to the pedal at (2, 3, 9.5) and step on it. The run, with these sizes:

| t (s) | Event | Rule and numbers |
|---|---|---|
| 0.0 | Gate lifts | 0.3 s |
| 0.3–1.3 | Ball rolls down the chute | a = (5/7)·22·sin 35° = 9.01; 4.27 long: 0.97 s, leaves at 8.78 u/s, velocity (7.19, −5.03) |
| 1.3–1.6 | Falls 2.35 to the drum | 0.29 s, lands at X=−12.44 with vy=−11.3 |
| 1.6–2.5 | Bounces (restitution 0.88: vy=+9.98), clears the books by 1.0 | 0.92 s, lands on the short arm at X=−5.85 |
| 2.5–2.8 | Ruler swings | m = 0.75 + 0.06 = 0.81; f = (56.5 − 2·0.81) / (56.5 + 4·0.81) = **0.92**; 0.34 s |
| 2.8–3.7 | Marble flies straight up from the long tip (Y=2.4) | apex = 2.4 + 4.8·f = **6.81**; the hoop needs 6.3. It drops through the net |
| 3.7–4.3 | Marble runs down the tube, closes the switch | |
| 4.3–5.2 | Fan spins up, the sail spins, the latch lets go | strength = (0.94 / 0.6)² = **2.4**; needs 1 |
| 5.2–8.1 | Dominoes 1–6 fall | 0.33, 0.38, 0.43, 0.49, 0.56, 0.64 s: each beat slower |
| 8.1–8.8 | Domino 7 falls 38° and meets the baseball | |
| 8.8–10.0 | Baseball rolls 2.5 along the shelf at 4 u/s, drops through the jar's neck onto the clock | mass 9.05 ≥ 8 |

14. The clock is flattened, the alarm rings, the door opens. Walk down the ramp, along the trench to (27, 0, 14) and out.

**E. Tolerances.**

| Toy | Start | Intended | Station seats it at | Chain works at | What sets the window |
|---|---|---|---|---|---|
| Dominoes | 0.6 | 3.02 | 1.0–3.5 | **2.2–3.5** | Low: domino 1 must stand in the wind zone, 20 − 3.05·S ≤ 13.5, so S ≥ 2.13 (domino 7 already reaches the baseball from S = 1.95). High: the clamp, which is the lane's length |
| Ball | 0.6 | 3.0 | 1.2–3.0 | **2.25–3.0** | Low: the marble must clear the hoop, f ≥ 0.81, so M ≥ 28·m ≈ 23 and s ≥ 2.25. High: the clamp, which is the hopper's throat |
| Ruler | 1.0 | 1.02 | 0.8–1.2 | **0.8–1.2** | Low: the short arm must reach under the ball's landing point (0.85 from the pivot). High: the clamp; the cap stays under the hoop (capture radius 0.75) across the whole window |
| Fan | 5 | 0.94 | 0.5–1.2 | **0.6–1.2** | Low: strength (s / 0.6)² ≥ 1. High: the garage (1.5 wide, 1.6 high) |

Three of the four upper limits are clamps, so "too far back" is harmless three times out of four. In terms of what the player does:

- **Dominoes.** Sized by the wall: S = 8.9·k / (0.899 + 0.4·k). Grabbing from 1.43 to 2.43 away gives 3.5 down to 2.2. From closer (down to 1.13) the clamp holds it at 3.5 a little short of the wall, still over the lane. Pitch +21° to +50°: lower and it stops on the plinth's face and falls into the trench at toy size; higher and it comes down in front of the plinth. Either way it is in view from the edge; re-grab.
- **Ball.** Grab from 1.4 to 2.5 away. Closer, the clamped ball hangs short of the funnel and falls into the trench: re-grab it from the gallery from 7 or more away (k ≤ 0.43) and it reaches the funnel at the clamp. Farther, it seats but is too light; the run shows the marble dying just under the hoop (at s = 2.0 the apex is 6.03). Pitch +31° to +45°.
- **Ruler.** Drop distance between 0.8 and 1.2 times the grab distance: pick it up and put it down from about the same range. Yaw within 30° of "cap toward the hoop": six clicks of `Q` give or take two, or no clicks at all if the player walks round and drops it while facing +X.
- **Fan.** Drop distance between 0.12 and 0.24 of the grab distance. Grabbed from the garage (23.9 away) that is standing 2.5 to 5 from the niche. Grabbed from 7 to 13 away and dropped at the feet (1.6) it is also in the window and can be carried over.

Recovery, never a restart:

- A seated toy is still grabbable while the machine is idle; grabbing it unseats it.
- Too small to seat: the ball drops between the gate bars and rolls out of the reject door at the tower's foot; the others lie on their outline with the socket's "too small" blink. Too big: the fan stops in front of the garage.
- A fizzled run resets in 3 s: gate down, ball lifted back into the hopper at the size the player gave it, ruler level, marble back in its cap, fan off, dominoes upright. The HUD names the link:

| Run stops at | What the player sees | Message |
|---|---|---|
| Gate | It lifts on nothing | "The gate opens. Nothing rolls." |
| Ruler missing | The ball lands on the marker and rolls off | "The ball lands on a bare pivot." |
| Ball too light | The marble peaks under the hoop and falls back | "The marble falls short. The ball isn't heavy enough." |
| Fan missing | The switch lamp lights, nothing spins | "Click. Nothing is plugged in." |
| Fan too small | It spins; the sail shivers | "The little fan wheezes. The sail doesn't turn." |
| Domino 1 outside the wind zone, or no dominoes | Streamers blow down an empty lane | "The wind has nothing to push." |
| Domino 7 too short | It slams down in front of the shelf | "The last domino falls short of the shelf." |

- Anything left where it would be out of sight (on the shelf, on a roof, or a toy other than the dominoes on the lane) is returned to its start pose and scale after 2 s (`NoParking`).

**F. Unintended solutions.**
- **Skipping the slide: dropping the ball straight onto the short arm.** It fires the catapult exactly as the boulder did in Level 7. Tolerated: the slide and the drum are the machine's own delivery; the chain from the ruler on is unchanged and still needs the other three toys.
- **Another toy as the weight.** It would be missing from its own station, and none is heavy enough anyway (dominoes 18.7 at the clamp, fan 13.2; the hoop needs about 23). The player jumping on the short arm weighs 3: f = 0.22.
- **Something else down the hoop.** The tube under the net is 0.55 across with an S-bend 0.6 below the rim. The ball's smallest diameter is 0.6, the fan's smallest cross-section is 0.5 × 0.35, the ruler is at least 2.4 long and the dominoes at least 1.6. Only the marble (at most 0.36 across) passes.
- **Running the fan somewhere else,** giant, to blow the dominoes over: the only switched outlet is in the garage. A fan anywhere else is furniture.
- **Pushing the dominoes by hand.** The plinth can be reached by standing the domino set in the trench as a staircase (at 3.5 its top is 5.24), but then the set is not on the lane; and seated dominoes are latched. Climbing the seated dominoes to the shelf is possible and pointless: the baseball is held in putty until domino 7 hits it. A player who drops into the bell jar is returned to the checkpoint (`HazardZone` inside the jar).
- **Crushing the clock with something else.** The jar's neck passes nothing wider than 1.4. A ball of 1.35 weighs 5.2; the clock needs 8 at 6 u/s. A bigger ball sits in the neck, in view, and can be taken back.
- **The ruler as a bridge** from the gallery to the plinth: 4.8 at most, the span is 5.4.
- **Dominoes facing the wrong way** (big end at the nozzle): the socket rejects it as `Backwards`.
- **Riding the catapult.** Standing on the cap when the ball lands: f = (56.5 − 2·3.81) / (56.5 + 4·3.81) = 0.68, apex 5.7 at the feet. A good view and nothing within reach. Tolerated.
- **Standing in the machine during a run.** Driven parts (ball, ruler, falling dominoes, baseball) ignore the player's capsule for the length of the run, so nobody is crushed against the books or launched across the room.
- **Pressing the pedal early or repeatedly.** The pedal is ignored while a run or a reset is in progress. An incomplete machine fizzles and resets.
- **Softlock audit.** No pits; every surface a toy can rest on is either in view from the gallery or the trench or is a `NoParking` zone; the fan can never exceed its start size, so it cannot wall anything off that it does not already; the gallery can always be left by jumping down (3) and regained by the ramp.

**G. Win, gadgets, bot.** Win = exit box (30, 1.5, 14), unlocked by `clock.Crushed`.

Reused as specified earlier: `Socket` × 4 (section 0.4), `Seesaw` (Level 7), `ReturnPort` (Level 8, the reject door), `PressureButton` (the pedal: `acceptPlayer` true, `latch` false), `SlidingDoor` (Level 12, input `TargetCrushed`), `HazardZone` (inside the bell jar), `SkyCap`.

The four sockets (none is `lockOnSeat`; the director locks them during a run):

| Socket | Tag | Capture volume | Scale | Yaw test | Seated pose (eased 0.25 s; the lane 0.4 s) |
|---|---|---|---|---|---|
| `hopper` | `ball` | cylinder radius 1.6 about (−18, ·, 18.3), Y 6..11 | 1.2–3.0 | none | center (−18, 6.0 + 0.5·s, 18.3), behind the gate. Below 1.2: `ReturnPort.Eject` to (−16.5, 0.5·s, 15.5) |
| `fulcrum` | `ruler` | box 7 × 2.5 × 2.5 centered (−4.5, 1.25, 18.25) | 0.8–1.2 | cap end within 30° of +X | the point one third along from the plain end on the marker's top line, long axis along X, cap end on the floor |
| `collar` | `fan` | the garage interior | 0.5–1.2 | none | base center (1.25, 0, 17.2), guard facing the duct |
| `lane` | `dominoes` | box X 8.5..20, Z 16.5..20, Y 5..9 | 1.0–3.5 | small end within 20° of −X | big end of the strip at X=20, centered on Z=18.25 |

`MachineDirector` (new)
- **State:** `Idle`, `Running`, `Resetting`, `Done`.
- **Start:** pedal pressed while `Idle`. Sets the four toys `Grabbable = false`, makes driven parts ignore the player's capsule, raises `MachineStarted`.
- **Fizzle:** while `Running`, if no link event has arrived for `fizzleTimeout` (2.5 s) the run is over: raise `MachineFizzled(lastLink)`, show the message from the table in E with `ctx.Say`, go to `Resetting`.
- **Reset:** 1.5 s. Calls `Reset()` on every gadget below in creation order, then restores `Grabbable` and collisions and returns to `Idle`.
- **Done:** on `TargetCrushed`. Toys stay locked; the exit unlocks.
- **Outputs:** `State`, `LastLink`, `Runs`. Events `MachineStarted`, `MachineFizzled`, `MachineReset`.

`BallRun` (new; uses `BeginDrive` / `EndDrive`)
- **Parameters:** gate at (−18, 6.0); chute 35°, length 4.27, roll acceleration 9.01; drum head Y=1.2; `restitution` 0.88; `gateTime` 0.3 s.
- **On `MachineStarted`:** the gate bar (kinematic) lifts. Without a seated ball: raise `GateOpened` and stop. With one: drive it. Its **lowest point** follows three closed-form segments:
  - (A) along the chute, distance = ½·9.01·t² for 0.974 s;
  - (B) ballistic from the lip (−14.5, 3.55) with velocity (7.19, −5.03) down to Y=1.2;
  - (C) ballistic from (−12.44, 1.2) with velocity (7.19, +9.98) until it meets the seated ruler (or, with no ruler, the marker).
- The center is the lowest point plus one radius: along the chute's normal on A, straight up on B and C, blended over 0.1 s. Spin is distance over radius.
- **End:** `EndDrive(velocity (0, −4, 0))`. The ball is an ordinary dynamic prop again and the `Seesaw` sees its mass. Events `GateOpened`, `BallBounced`, `BallLanded(mass)`.
- **Reset:** gate down. If the ball left the hopper in this run and is not held, it is driven back to the hopper seat along a fixed lift path over 1.5 s, scale unchanged.

`Seesaw` (Level 7), as used here
- Built when `fulcrum` seats a ruler and removed when it is unseated. The seated ruler is the plank (driven).
- Pivot (−5, 0.8, 18.25), axis Z. `armA` = 2.667·s_r (cap side), `armB` = 1.333·s_r, r = 2. With a fixed pivot height both tips travel the same heights at any ruler scale: short tip 1.2 → 0, cap 0 → 2.4.
- Load on the cap side: m = `armLoad` (0.75) + marble (0.0565·s_r³). f = clamp((M − 2·m) / (M + 4·m), 0.05, 1), where M is the mass of non-held props on the short arm. Swing time √(2.4 / (22·f)).
- **Release:** the ruler's marble child is hidden and `marbleShot` (kinematic sphere, radius 0.15·s_r, not grabbable) appears at the cap with speed v = 2·√(2·22·f·1.2) = 14.53·√f, **straight up**: y(t) = 2.4 + v·t − 11·t², apex 2.4 + 4.8·f. Raise `MarbleLaunched(apex)`.
- Stays tipped while M > 0.5. **Reset:** returns to rest at 60°/s.

`HoopCatch` (new)
- **Parameters:** ring center (−2.9, 6.0, 18.25), `captureRadius` 0.75, `clearance` 0.3, tube time 0.6 s.
- If the marble's apex ≥ 6.0 + `clearance` and its X is within `captureRadius` of the ring: on its way down through Y=6.0 it is eased onto the axis (0.15 s), follows the tube spline to the switch, and raises `SwitchClosed`. Otherwise it finishes its ballistic path, stops where it lands and raises `MarbleMissed(apex)`.
- **Reset:** shot hidden, the ruler's marble child shown.

`FanJet` (new)
- **Inputs:** `collar.Seated`, `SwitchClosed`.
- On the switch: no fan seated → `FanMissing`. Otherwise spin up 0.5 s, then `Strength = (fan.Scale / 0.6)²` and raise `JetOn(Strength)`.
- **Jet zone:** box X 8.5..13.5, Z 17.25..19.25, Y 5..7. The reach (5.0) belongs to the nozzle and does not depend on the fan.
- **Reset:** off. Presentation: blade speed and streamer length follow min(Strength, 2).

`DominoRun` (new; attached to the `dominoes` prop; engine request 9)
- **Start condition** on `JetOn`: the set is seated, `Strength ≥ 1`, and domino 1 (at X = 20 − 3.05·S) is inside the jet zone. Otherwise raise `SailStalled` (weak fan) or `NothingToPush`.
- **Wave:** after 0.4 s of sail spin, domino 1 is released. Domino i rotates about its hinge with θ̈ = (3·22 / (2·h_i·S))·sin θ from θ = 3° (the law of the `TipAssist` fallback in Level 4). At θ = asin 0.55 = 33.4° it meets the next one: raise `DominoFell(i)` and release i+1. It carries on to rest at 72°, shingled. Expected intervals: 0.35·√(h_i·S) s.
- **Last domino:** hinge H = (20 − 0.154·S, 5 + 0.05·S); D = distance from H to the center of the baseball (22.0, 7.4). If 1.448·S + 0.5 ≥ D, it stops at the contact angle (about 38° from vertical), raises `BaseballStruck` and settles on the edge of the shelf. Otherwise it falls flat and raises `FellShort`.
- **Reset:** the dominoes stand up again in reverse order over 1.0 s.
- Closed form in ticks since `JetOn`; no collision callbacks.

`BaseballDrop` (new)
- On `BaseballStruck` the baseball is driven: 2.5 along the shelf at 4 u/s, ballistic from (24.5, 7.4), eased onto the axis of the jar (X=26.2) through the neck, free fall to the bell button (center Y=3.4). It arrives at √(2·22·4.0) = 13.3 u/s and is handed back to physics.

`CrushTarget` (new; the alarm clock)
- **Parameters:** `minMass` 8, `minSpeed` 6; sensor = sphere radius 0.5 over the bell button.
- Each tick, for any non-held body in the sensor: if mass ≥ `minMass` and downward speed ≥ `minSpeed`, set `Crushed`, swap the clock's collider for its flattened shape (1.0 high), raise `TargetCrushed`. Lighter or slower: `TargetDinged(mass / minMass)`.

`NoParking` (new)
- **Volumes:** the shelf top; the two tower tops outside the hopper; the lane, for props without the tag `dominoes`.
- A non-held prop at rest (speed < 0.2) in a volume for 2 s is respawned at its start pose and scale (`prop.Respawn()`).

All of these tick in creation order inside `ctx.OnUpdate`, use tick counts only, and visit props in `Prop.Id` order.

```csharp
// 1. dominoes: grow against the wall above the lane
yield return bot.WalkTo(new Vector3(14, 3, 4.3f), 0.15f);
yield return bot.Grab(dominoes);
yield return bot.WalkTo(new Vector3(16, 3, 6));                 // round the book
yield return bot.WalkTo(new Vector3(14.25f, 3, 11.1f), 0.15f);
yield return bot.DropAt(new Vector3(14.25f, 8.9f, 20f));
yield return bot.Until(() => lane.Seated, 4f);

// 2. ball: grow to the clamp above the hopper
yield return bot.WalkTo(new Vector3(10, 3, 4.5f));              // south of the ramp notch
yield return bot.WalkTo(new Vector3(-18, 3, 4.5f), 0.15f, 12f);
yield return bot.Grab(ball);
yield return bot.WalkTo(new Vector3(-16.8f, 3, 6));             // round the spool
yield return bot.WalkTo(new Vector3(-18, 3, 11.1f), 0.15f);
yield return bot.DropAt(new Vector3(-18, 10.8f, 20f));
yield return bot.Until(() => hopper.Seated, 5f);

// 3. down the ramp
yield return bot.WalkTo(new Vector3(4, 3, 4.5f), 0.3f, 12f);
yield return bot.WalkTo(new Vector3(7.5f, 3, 5.0f));
yield return bot.WalkTo(new Vector3(7.5f, 0, 12.5f));

// 4. ruler: same size, turned 90 degrees
yield return bot.WalkTo(new Vector3(-5.5f, 0, 14f), 0.15f);
yield return bot.Grab(ruler);
yield return bot.WalkTo(new Vector3(-5, 0, 14.4f), 0.15f);
yield return bot.LookAt(new Vector3(-5, 0.88f, 18.25f));
yield return bot.RotateHeld(6);
yield return bot.DropAt(new Vector3(-5, 0.88f, 18.25f));
yield return bot.Until(() => fulcrum.Seated, 3f);

// 5. fan: shrink into the garage
yield return bot.WalkTo(new Vector3(1.25f, 0, 13f), 0.15f);
yield return bot.Grab(fan);                                      // 23.9 away
yield return bot.DropAt(new Vector3(1.25f, 0.6f, 17.2f));
yield return bot.Until(() => collar.Seated, 3f);

// 6. pedal
yield return bot.WalkTo(new Vector3(7.5f, 0, 12.5f));
yield return bot.WalkTo(new Vector3(7.5f, 3, 5.0f));
yield return bot.WalkTo(new Vector3(4, 3, 4.5f));
yield return bot.WalkTo(new Vector3(2, 3, 9.5f));
yield return bot.Until(() => clock.Crushed, 20f);

// 7. out
yield return bot.WalkTo(new Vector3(4, 3, 4.5f));
yield return bot.WalkTo(new Vector3(7.5f, 3, 5.0f));
yield return bot.WalkTo(new Vector3(7.5f, 0, 12.5f));
yield return bot.WalkTo(new Vector3(27, 0, 14), 0.3f, 12f);
yield return bot.WalkTo(new Vector3(30, 0, 14));
```

Tests to write beside the solver:
- The solver test asserts the link events in order: `BallLanded`, `MarbleLaunched` with apex ≥ 6.3, `SwitchClosed`, `JetOn`, `DominoFell(1..6)`, `BaseballStruck`, `TargetCrushed`.
- A second test seats the ball at 2.0, presses the pedal, expects `MarbleMissed` and `MachineReset`, then re-sizes the ball and solves. This is the "never softlocked" proof.
- A third test presses the pedal on the empty machine and expects `GateOpened`, a fizzle and a clean reset.

**H. Teaching.**
- Blurb: "Four toys, four outlines, one pedal. Nothing here is the right size."
- Hint 1: "Put a toy on every painted outline, then stomp the pedal and watch where the motion stops. That link is the one to fix; the machine sets itself up again."
- Hint 2: "The ball must be heavy enough to flip the marble up through the hoop. The fan must fit in its garage. The smallest domino must stand in the striped wind zone while the biggest reaches the shelf. The cap on the ruler points at the hoop."
- Hint 3: "From the quilt's edge: pick up the dominoes from two steps away and press them against the wall above the lane; do the same with the ball above the funnel. Downstairs: lay the ruler on the marker, turning it with Q until the cap is under the hoop. Stand in front of the garage, grab the big fan from there and drop it inside. Pedal."

**I. Wow beats.**
- **The fan.** A turbine the size of a house, its guard taller than the slide tower, lifts off the floor with no change in size on screen, loses its long shadow, and is parked in a garage like a toy car. The trench behind where it stood is suddenly open and moonlit.
- **The run.** One unbroken ten-second take that gets bigger and slower as it goes: the ball sailing over the books, the marble's small vertical hop through the net, the click, the whirr of a fan the size of a thimble, then seven dominoes falling in a ritardando, each beat lower in pitch and heavier than the last, the seventh the size of a door. The baseball rolls, drops through the glass neck, and the alarm clock bursts into springs and cogs.
- **The ending.** The bell rings, and the night preset turns to dawn (the art bible's `#FFB8C8` flood): every seated toy, which has been glowing as a night-light, hands its light back as the door swings open.

**Notes for art.**
- Held toys follow section 0.5. In particular the held fan's blades do not turn and no streamers come off it, and the held domino set's sail does not spin, whatever it is carried through.
- The four outlines are painted on the floor, the lane and the funnel in each toy's own candy colour, drawn at the middle of the working window, so "does it cover its outline" is the size check.
- The wind zone is striped paint plus small flags along the lane's back edge; the flags stand out when the jet is on and are the cue for `NothingToPush`.
- The marble, the net tube and the switch lamp need to read at 25 units from the pedal: give the marble an emissive trail and the tube a travelling light.
- During a run the camera stays first-person. No cut-scene camera: the player chose where to watch from.

**Physics confidence.** Medium, and the doubt is about integration rather than any single rule. Every motion in the run is a scripted closed-form path, so the chain itself is deterministic. What is real physics, and therefore the risk:
1. Three toys are released above their stations and must come to rest inside a capture volume: a 56-mass ball dropping into a funnel, a 12-mass compound strip of seven boxes dropping one unit onto the lane, and a thin ruler landing across a cylinder.
2. Handing the ball from a scripted arc back to PhysX on top of a kinematic plank that is about to swing.
3. Engine request 9: child colliders that rotate inside a seated prop.
4. Eleven gadget types in one level. Each is small, but the reset path of every one has to be right or the level leaks state between runs.

**Fallback A (strict stations).** Extend each socket's capture volume to the whole column above its station and capture **in the air**: the toy is eased into its seat from wherever it was released, so nothing has to land. Replace `BallRun` → `Seesaw` → `HoopCatch` hand-offs with one authored `MachineTimeline`: the pass or fail of each link is computed up front from the seated scales with the same formulas (f ≥ 0.81, strength ≥ 1, wind zone, reach), and every part plays a fixed animation up to the first failing link. Same puzzle, same messages, no physics inside a run at all.

**Fallback B (three toys).** If the level runs long in playtests, ship the ruler already seated and not grabbable. The exam is then grow (dominoes), grow to a weight (ball) and shrink (fan); about a minute shorter, one socket and the yaw test removed.

---

## Phase ramp

| Level | New idea | Rule in one line | Decisions | Target time | Risk |
|---|---|---|---|---|---|
| 10 | One toy hides others; a clamp is the puzzle | no box taller than 2.0, no step taller than 1.15 | Three sizes, three positions, one stack | 2–3 min | Low to medium (fallback given) |
| 11 | A held toy is not in the world; apparent size at the grab decides everything | the roof must be wider than the lane (s ≥ 4.7) | Grab close, aim high, one release | 1.5–2.5 min | Low |
| 12 | Shrink and grow in one level; the tool you need is the floor you stand on | key 0.06–0.5 in the lock; spool 8.0–9.6 in the drawer | Take the bridge away, build another, carry the key | 3–4 min | Low to medium (fallback given) |
| 13 | Size is capacity; a toy carries a quantity through a resize | capacity = 0.315·s³; the surplus comes out where it stands | Soak big, carry, wring small in the right place | 3–4 min | Medium (fallback given) |
| 14 | The toy resizes *you* | you leave a door at the door's size | Three door sizes in order, re-entry to converge | 3–4 min | Medium (fallback given) |
| 15 | Everything at once, judged by running it | lever, fit, reach, in one chain | Four sizes, one orientation, read the failure, fix it | 4–6 min | Medium (two fallbacks given) |

- Level 11 is deliberately lighter than Level 10: one toy and one release after a three-box build. If a strictly rising ramp is wanted, swap their order; neither depends on the other.
- Level 15 runs over the 1–4 minute target. It is the final exam and most of the extra time is walking between stations; Fallback B (ruler pre-seated) brings it back to about four minutes.

Scale windows, for the bot tests to assert against:

| Level | Prop | Start scale | Intended scale | Works from | To |
|---|---|---|---|---|---|
| 10 | Red box (B) | 1.2 | 1.90 | 1.6 | 2.0 (clamp) |
| 10 | Yellow box (A) / teal box (C) | 0.9 / 0.6 | 1.0 / 1.0 | 0.75 / 0.85 | 1.15 / 1.15 |
| 11 | Playing card | 0.5 | 5.9 | 4.7 | 7.0 (clamp) |
| 12 | Key | 8 | 0.30 | 0.06 | 0.5 |
| 12 | Spool | 1.0 | 9.1 | 8.0 | 9.6 (clamp) |
| 13 | Sponge, soaking | 2.0 | 9.5 | 8.4 | 12 (clamp) |
| 13 | Sponge, wringing | 9.5 | 1.9 | 0.5 (clamp) | about 3 (what fits in the tower) |
| 14 | Doorway: small / big / medium | 1.0 | 0.28 / 3.2 / 1.35 | 0.1 (clamp) / 2.4 / 0.1 (clamp) | 0.45 / 3.2 (clamp) / 1.7 |
| 15 | Domino set | 0.6 | 3.02 | 2.2 | 3.5 (clamp) |
| 15 | Bouncy ball | 0.6 | 3.0 | 2.25 | 3.0 (clamp) |
| 15 | Ruler | 1.0 | 1.02 | 0.8 | 1.2 (clamp) |
| 15 | Fan | 5 | 0.94 | 0.6 | 1.2 |

Gadgets introduced in this phase:

| Gadget | Level | One-line contract |
|---|---|---|
| `Socket` | 12, 15 | Seats a tagged prop whose scale (and yaw) is in a window; eases it to a pose that depends on its scale. |
| `LaserRain` | 11 | Downward beams over a rectangle; the player is zapped unless something solid is overhead. A single `Laser` is the 1 × 1 case. |
| `SlidingDoor` | 12, 15 | Kinematic slab that opens on a signal and unlocks the exit. |
| `WaterVolume` | 13 | A footprint, a floor and a number; flat surface; wade, drown, overflow, leak. |
| `Sponge` | 13 | Stores water up to 0.315·s³; soaks when below a surface, wrings out when over capacity. |
| `FloatPlatform` | 13 | Kinematic float that rides a `WaterVolume` surface. |
| `PortalDoorway` | 14 | Passing through rescales the player to the doorway's scale about its threshold. |
| `RecallPad` | 14 | Brings the doorway back to the player. |
| `MachineDirector` | 15 | Runs, fizzles and resets a chain of link gadgets; locks the toys during a run. |
| `BallRun`, `HoopCatch`, `FanJet`, `DominoRun`, `BaseballDrop`, `CrushTarget` | 15 | The links: each a closed-form motion plus one pass/fail rule of scale. |
| `NoParking` | 15 | Returns props left where they would be out of sight. |

Reused from earlier phases: `SkyCap`, `PropLeash`, `HazardZone`, `PressureButton` (Phase 1), `Seesaw` and `ReturnPort` (Phase 2).

**Open questions for engineering.**
1. **Engine requests (section 0.2).** The load-bearing ones are `FrozenUntilGrabbed` (Level 12's bridge), `Player.Respawn` keeping the player's scale and the held prop (12, 14), and `BeginDrive` / `EndDrive` plus rotating child colliders (15). Each level's fallback says what to do without them.
2. **Exit API.** Levels 10, 11, 13 and 14 say `ExitDoor` in the Phase 1 sense. In the engine that is `ctx.AddExit(position, size)` with `Lock()` / `Unlock()`.
3. **Environment keys.** The names in each section A (`gift-nook`, `night-hall`, `hallway-drawer`, `fishbowl`, `door-hall`) are level-local set dressing, not render presets. `ART_BIBLE.md` §6 maps Levels 10–15 to `cardboard-box`, `pegboard-workbench`, `high-shelf`, `cardboard-box`, `night-light`, `night-light`, and `LevelDefinition.Environment` should return those. Two things to settle with art: Level 11 is written as a dark corridor, where the beams and the dots read best, but is mapped to a daylight preset; and Level 13 needs the transparent glass and water materials to exist on the low tier.
4. **Gadget effects and the held prop.** Laser dots (11), water tint and wetness (13), the portal view (14) and wind streamers (15) must all ignore the held prop in rendering exactly as the simulation does (section 0.5), or they become depth cues.
5. **Player scale (14).** `PerspectiveGrabber` already scales its near distance with the player; the bot's `WalkTo` tolerance and slow radius do not, which is why Level 14's solver passes tight tolerances explicitly.
6. **Cost.** Level 11 draws about 450 instanced beams; Level 14 needs a second camera on the high tier only; nothing in Level 15 costs more than its colliders.

**Files.** Read `docs/ARCHITECTURE.md`, `docs/ART_BIBLE.md` (§6 level mapping, held-toy rules), the Phase 1 and Phase 2 drafts, and the engine sources under `Assets/Toybox/Runtime/Engine` (read-only). Nothing outside `docs/design-notes/levels-phase-3-draft.md` was written.

END OF PHASE 3 SPEC

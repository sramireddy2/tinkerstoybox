# Tinker's Toybox — Levels 1–15: verified, build-ready campaign spec

This document supersedes the three drafts in `docs/design-notes/` (`levels-phase-1-draft.md`,
`levels-phase-2-draft.md`, `levels-phase-3-draft.md`). Every intended solution in them was re-derived
against `docs/ARCHITECTURE.md` and the engine source as it stood on 2026-10-03 (`PerspectiveGrabber.cs`,
`Prop.cs`, `Player.cs`, `Trigger.cs`, `Bot.cs`), with a throwaway Python model of the hold march (same
constants: near 0.35, march ratio 1.08, 8 bisections, centre-ray cap, min/max clamps). Where the drafts'
arithmetic or layout did not hold, the level was changed; Appendix A lists every change and why.

`ARCHITECTURE.md` was revised by the engine team while this was being written (18:05 the same day).
The revision was read and this document agrees with it: the tick order is input → player → grab / drop
→ level + gadgets → physics → ground probe, held-toy placement, triggers, exits; the march is refined
near geometry; nothing crushes the player; a rider stays on a `Mover` through speed changes up to 6.
Pitch bands quoted below are the conservative ones (a grazing pass that only the coarse march would
have allowed is not counted).

**Status.** Geometry and arithmetic are checked by script. Nothing has been run in Unity: the numbers
are what the level classes should be built from, and the scale windows in Appendix B are what the bot
tests should assert. Physics behaviour that only PhysX can confirm is listed in Appendix C with a
fallback per level.

**Restart marker.** Sections are appended in order. The file is complete only when its last line is
`END OF LEVELS.md`.

**Contents.** 0 Conventions and rules · 1 Campaign overview · 2 Gadget catalog · 3 Toy catalog ·
4 Settings and environment presets · 5 Levels 1–15 · Appendix A Design corrections · Appendix B Scale
windows for the bot tests · Appendix C Risks and fallbacks

---

## 0. Conventions and rules every level obeys

### 0.1 Conventions

- Unity space: left-handed, **Y up**, yaw 0 looks down **+Z**, positive yaw turns right (toward +X),
  positive pitch looks up, degrees. Positions are `(X, Y, Z)`; player positions are the **feet**.
  The Phase 1 draft was written after the engine switch and already used this convention; it was
  re-checked axis by axis and no coordinate needed flipping. Travel is +Z in every level except 15 (+X).
- 1 unit = 1 Unity metre = 3 cm of playroom (`ART_BIBLE.md` §6.1). The player is 1.7 units tall.
- Symbols: `g` grab distance (eye to prop centre), `k = scale / g` (apparent size, constant for a
  hold), `d` hold distance at release, `s = k·d` resulting scale, `D` horizontal distance from the eye
  to a backstop, `p` pitch.
- Player numbers used for limits (measured values from `ARCHITECTURE.md`, not the nominal ones):
  eye 1.55, capsule radius 0.3, jump apex **1.25**, sprint jump **5.47**, walk / sprint 5 / 8,
  gravity 22, walkable slope 50°, no step-up (a vertical lip above 0.1 needs a jump or a chamfer).
  Design limits derived from them: a **rise of at most 1.15** per hop, a **gap of at least 9** to be
  un-jumpable, walk-on edges chamfered at 45°.

### 0.2 How a held toy lands (the rules the layouts are built on)

1. **First overlap wins.** The hold march goes outward from the eye and stops at the first overlap;
   the centre ray caps it. A toy only arrives at a pose if the whole cone from the eye to that pose is
   empty. Consequences: you cannot project a toy down into a pit from a low vantage; a tall or wide
   toy aimed along a floor stops on the floor; shrinking at your feet is always robust.
2. **Backstop sizing.** When a toy is stopped by a wall a horizontal distance `D` away and reaches
   `h·s` toward the wall from its centre, `s = k·D / (cos p + h·k)`. Stepping back grows it, stepping
   forward shrinks it, pitch barely matters. Levels 2, 6, 7, 9, 11, 12 and 15 size their key toy this
   way.
3. **Clamps are design tools.** `MaxScale` makes "too far back" harmless (the toy stops growing in
   mid-air and drops) and blocks degenerate solutions; every clamp below says which.
4. **Thin obstacles.** The march steps 8 % of the distance at a time and refines the step near
   geometry (down to 0.4 %, at most 256 fine steps per placement), so walls and bars are respected.
   Even so, nothing that must stop a held toy here is thinner than 0.3, and no intended throw passes a
   corner with less than 0.05 to spare.
5. **A held toy is not in the world.** It is on the `Held` layer: nothing collides with it, triggers
   and gadgets ignore it (`prop.Held`), lasers pass through it, a held sponge neither soaks nor leaks,
   a held doorway is not a portal. Mid-air grabs are legal (only a toy the player is grounded on is
   refused); no level is broken by one.
6. **Presentation of a held toy** follows `ART_BIBLE.md` §8 (sticker pass: no cast or received
   shadow, no haze, constant-pixel border). No gadget effect is drawn on it. The cast shadow returning
   on release is the true-size reveal; each level's wow beats say what that reveal shows.

### 0.3 Level-authoring rules adopted for the whole campaign

- **Painted outlines are the size language.** Where a toy has a target size, a dashed outline of the
  toy at that size is painted *on the surface the toy stops against or stands on* (Level 2 far wall,
  Level 4 board, Level 7 backstop, Level 12 door wall, Level 15 stations). Because the outline and the stopped toy
  are at the same depth, comparing them on screen is truthful. Outlines are `RoomLit` decals, never
  colliders.
- **Pedestal toys.** Where a distant first pickup would quietly break a level (2 and 7), the toy sits
  at eye height within one step of where the player stands, so the default grab is a close one.
  Elsewhere pedestals stand on the player's path. Spheres on pedestals and shelves use
  `FrozenUntilGrabbed`.
- **No strand, no restart.** Every prop is always either in view from somewhere the player can stand,
  or returned by a `PropLeash`. Every pit either has a floor with a `HazardZone` (player respawns, props
  stay or are leashed) or is a kill-plane drop (`KillY`; props respawn at their origin pose and scale).
  `Player.Respawn` keeps the player's scale and the held toy (verified in `Player.cs` / `Game.cs`).
- **Crush guard.** A gadget never moves a kinematic part into the player. Doors, flaps, seesaw returns
  and float platforms test the player's capsule against the pose they are about to take; on overlap they
  either wait (doors, returns) or suppress that part's collision with the player until they separate
  (a swing already in progress), exactly as the grabber does for a toy dropped onto the player.
- **Exits** are `ctx.AddExit(centre, size)` (`Exit.Lock()` / `Unlock()`); the drafts' `ExitDoor` gadget
  is gone. **Sky caps** are invisible static slabs on the `Default` layer that close the top of a level.
- **Ground plane.** `ART_BIBLE.md` §6.2 puts the preset's floor, bench or shelf top at y = 0. Levels
  with pits need it lower, so each level declares `GroundY` (section 4); the environment solver uses it
  in place of the literal 0. This is a request to the engine and render teams (0.4, item 6).

### 0.4 Engine requests (consolidated; each level names the ones it needs)

| # | Request | Used by | Without it |
|---|---|---|---|
| 1 | `PropOptions.GrabPose`: `Keep` (today's behaviour), `Snap90` (on grab ease pitch and roll to the nearest 90° in the yaw frame over 0.15 s), `Upright` (ease to the authored up axis, yaw kept). Replaces the drafts' `SnapUprightOnGrab` | 1, 2, 4, 5, 6, 9, 10, 11, 12, 13, 15 | A tumbled toy is re-grabbed at an odd tilt `F` cannot fix |
| 2 | `Prop.BeginDrive()` / `EndDrive(Vector3 velocity)`: a gadget takes a `Dynamic` prop kinematic, moves it through a `Mover` (so riders inherit its velocity), and hands it back. A grab during a drive ends it. While driven, the prop is exempt from the light-prop shove rule (it is ground, whatever its mass) | 2, 5, 8, 9, 12, 15 | Per-level fallback: an invisible kinematic twin |
| 3 | `PropOptions.FrozenUntilGrabbed`: kinematic at the authored pose until the first grab, then `Dynamic` | 3, 7, 8, 12, 15 | Spheres may creep off pedestals; Level 12's bridge needs a socket instead |
| 4 | `PropOptions.AllowPitch` (default true): when false `F` does nothing and the toy keeps world up | 14 | Doorway can be laid flat (harmless but ugly) |
| 5 | `PropOptions.KeepUpright`: rotation about X and Z frozen while dynamic | 12 | Level 12 fallback socket in the pit |
| 6 | `LevelDefinition.GroundY` (default 0), read by the environment solver as the play plane | 2, 4, 5, 8, 9, 12 | The preset floor would close the pits |
| 7 | Bot: `WalkTo` keeps steering while airborne (true today) and scales its slow radius with `player.Scale` | 6, 7, 14 | Level 14's solver passes tight tolerances by hand |

Requests the drafts made that are **not** needed: per-prop grab/drop callbacks (grab and drop run
before the gadgets in the same tick, so polling `prop.Held` sees the change at once);
`Player.Respawn` keeping scale and held toy (already true); a heavy prop landing on the player (the
engine now lets it pass through and come to rest round them); `prop.ExtraMass` (Level 13 no longer
uses it); rotating child colliders of a seated prop (Level 15's `HingeChain` swaps in its own
kinematic bodies). Note for request 2: a `Kinematic` prop can already be made grabbable (its `Mover`
ignores `MoveTo` while held); what is missing is switching a `Dynamic` prop to driven and back.

---

## 1. Campaign overview

| # | Title (`slug`) | Preset | The one new idea | Toy: start → intended scale (works) | Gadgets | Time | Risk |
|---|---|---|---|---|---|---|---|
| 1 | The Cheese Wedge (`cheese-wedge`) | `sunny-rug` | Far away means big | Wedge 0.4 → 9.4 (5.6–14) | — | 1 min | Low |
| 2 | The Thimble Chasm (`thimble-chasm`) | `pegboard-workbench` | A size has to fit; step back or forward while holding to tune it | Thimble 0.8 → 11.5 (9–13) | `Socket`, `HazardZone`, `PropLeash` | 1.5–2 min | Low |
| 3 | Shrinking the Apple (`shrinking-apple`) | `cardboard-box` | Near means small: the mechanic runs both ways | Apple 11.4 → 0.40 (0.27–0.50) | `Funnel`, `PressurePlate`, `Door`, `PropLeash` | 1.5–2 min | Low |
| 4 | Domino Effect (`domino-effect`) | `block-hall` | Size is weight (s³), and where a toy stands has consequences | Domino 0.5 → 4.1 (3.6–5.3) | `Breakable` | 2–4 min | Medium |
| 5 | The Fan and the Feather (`fan-feather`) | `sunny-rug` | Size is area: wide catches wind | Feather 1.0 → 9.2 (6.5–12) | `WindStream`, `SailRaft` | 1–2 min | Low |
| 6 | Bouncing Eraser (`bouncing-eraser`) | `block-hall` | Size is stored bounce | Eraser 0.8 → 9.5 (7.5–10.5) | `BouncePad`, `PropLeash` | 1.5–2.5 min | Low |
| 7 | The Teeter-Totter (`teeter-totter`) | `high-shelf` | A lever: you are one of the weights, and must be in place when the other lands | Pebble 0.6 → 4.6 (3.3–7) | `Seesaw` | 2–3 min | Medium |
| 8 | Funnel Physics (`funnel-physics`) | `pegboard-workbench` | Exact sizes, three times, both directions: what you carry is how big it *looks* | Marbles 0.25 / 1.2 / 5.0 → 0.64 / 1.32 / 2.45 | `Funnel`, `PressurePlate`, `ReturnPort`, `FitGauge`, `Door`, `HazardZone` | 3–4 min | Low–medium |
| 9 | The Moving Train (`moving-train`) | `high-shelf` | Timing: release onto a moving target | Plank 0.65 → 4.03 (3.7–4.4) | `Train`, `PropCarrier` | 3–4 min | Medium |
| 10 | Matryoshka Boxes (`matryoshka-boxes`) | `cardboard-box` | Several toys from one; a clamp is the puzzle; stack them | Boxes 1.2 / 0.9 / 0.6 → 1.9 / 1.0 / 1.0 | `NestedSet`, `PropLeash` | 2–3 min | Low–medium |
| 11 | Blocking Lasers (`blocking-lasers`) | `pegboard-workbench` | A held toy is not in the world: the roof only works once you let go | Card 0.5 → 5.9 (4.7–7) | `LaserRain` | 1.5–2.5 min | Low |
| 12 | The Keyhole (`the-keyhole`) | `high-shelf` | One toy, two jobs: take the bridge away to use it (first shrink + grow level) | Key 8 → 0.31 (0.06–0.5); spool 1.0 → 9.1 (8.4–9.9) | `Socket`, `Door`, `HazardZone` | 3–4 min | Low–medium |
| 13 | Escaping the Fishbowl (`fishbowl`) | `cardboard-box` | Size is capacity: a toy carries a quantity through a resize | Sponge 2.0 → 9.5 (8.4–12) → 1.9 (0.5–3) | `WaterVolume`, `Sponge`, `FloatPlatform`, `Door` | 3–4 min | Medium |
| 14 | The Infinite Hallway (`infinite-hallway`) | `night-light` | The toy resizes *you* | Doorway 1.0 → 0.28 / 3.2 / 1.35 | `PortalDoorway`, `RecallPad` | 3–4 min | Medium |
| 15 | The Rube Goldberg Machine (`rube-goldberg`) | `night-light` | Everything at once, judged by running it | Dominoes 0.6 → 3.0; ball 0.6 → 3.0; ruler 1.0 → 1.1; fan 5 → 0.94 | `Socket` ×4, `MachineDirector`, `PathDrive` ×3, `Seesaw`, `WindStream`, `HingeChain`, `Breakable`, `PressurePlate`, `Door`, `PropLeash`, `HazardZone`, `FitGauge` | 4–6 min | Medium |

**Difficulty ramp, checked.**

- Phase 1 teaches the mechanic itself, one direction at a time: aim (1), tune by standing distance (2),
  reverse (3), consequence (4). Level 2 no longer depends on how far away the thimble was picked up
  (the default grab is close and the clamp makes "too far back" harmless), so the only new thing in it
  is fitting.
- Phase 2 turns scale into one physical quantity per level: area (5), bounce height (6), mass on a
  lever (7), exact radius and mass (8), then adds timing (9). Level 7's drafted dependence on a
  close pickup was removed (the pebble now sits beside the seat), so its single idea is the lever.
  The pickup-distance idea ("what you carry is how big it looks") is taught once, in Level 8, where a
  lamp gauge makes it visible, and is then *required* in Level 11.
- Phase 3 is about roles: many toys from one (10), held versus released (11), one toy with two jobs
  (12), a toy as a container (13), a toy that changes the player (14), and the exam (15). Level 12 is
  the first level that needs both a shrink and a grow; Level 15 reuses backstop growing (2, 6), the
  lever (7), shrinking at close range (3), fan area (5) and falling dominoes (4), and adds only "read
  where the chain stops".
- Level 11 is lighter than Level 10 on purpose (one toy, one release after a three-box build). The
  brief's order is kept; neither level depends on the other.

---

## 2. Gadget catalog

The three drafts named 39 gadgets, two of them fallbacks. Overlapping ones are merged into 26 (the map is in
Appendix A). All live in `Toybox.Gadgets` and share one shape.

### 2.0 The shape every gadget has

```csharp
public abstract class Gadget {
    protected Gadget(LevelContext ctx, string name);  // registers ctx.OnUpdate(Tick) and ctx.OnDispose(Dispose)
    public string Name { get; }
    public bool Enabled { get; set; }                 // disabled: senses nothing, moves nothing
    public int Age { get; }                           // ticks since construction or the last Reset()
    protected abstract void Tick(float dt);           // dt == Sim.Dt; after player + grabber, before PhysicsScene.Simulate
    public virtual void Reset() { }                   // back to the state Build left it in
}
// in Build:  var plate = new PressurePlate(ctx, new PressurePlateOptions { ... });
```

- **Order.** Gadgets tick in construction order inside "level + gadgets". A gadget that feeds another
  is constructed first.
- **Sensing.** `Trigger`s made through `ctx`, or overlap queries and raycasts through
  `ctx.Game.PhysicsScene` with `Layers.SensedMask` / `Layers.SolidMask` and
  `QueryTriggerInteraction.Ignore`. Never physics callbacks. Triggers are evaluated after the physics
  step, so a gadget reads them one tick late; every delay below already includes that tick.
- **Props** are visited in `Prop.Id` order. Held props (`prop.Held`) are skipped by every gadget.
- **Motion.** Kinematic parts are `Mover`s (`ctx.AddKinematic`), given one `MoveTo` per tick. Wherever
  a closed form exists the pose is a pure function of ticks since an event, not an accumulated
  velocity. Time is `ticks × Sim.Dt`; no `Time.*`, no `UnityEngine.Random`. No gadget uses `ctx.Rng`.
- **Events.** Each event exists twice: a C# `event Action<T>` on the gadget, invoked inside the tick
  (level logic subscribes here), and a mirror on the partial `GameEvents` (`Raise...`), delivered when
  the tick ends (render, audio and UI subscribe there).
- **Crush guard** (0.3) applies to every gadget that moves a part.
- **Riders.** A part that carries the player keeps its speed changes under `Player.MoverGripSpeed`
  (6 units/s per tick) unless it means to throw them. `AddKinematic` geometry is on the `Default`
  layer: it blocks held toys and is not sensed by triggers; a mover a `Trigger` must sense is a
  `Kinematic` prop.

### 2.1 Sensors, safety and plumbing

**`SkyCap`** — static helper, not ticked. `SkyCap.Add(ctx, xMin, xMax, zMin, zMax, y, thickness = 1)`
adds an invisible `Default`-layer slab. Used by every level that has no ceiling of its own.

**`HazardZone`**
- Options: `Shape` (box, sphere or vertical cylinder, world pose), `Delay` 0.1 s, `OnCaught` (default
  `game.Player.Respawn()`; Level 8 passes `ReturnPort.Eject(player)`).
- Tick: the player's feet inside the shape for `Delay` without interruption → `OnCaught`. Props are
  ignored.
- Events: `PlayerCaught`. Deterministic: point-in-shape test and a tick counter.
- Levels: 2 (well), 8 (chambers), 12 (drawer), 15 (bell jar).

**`PropLeash`** (merges the drafts' `PropLeash`, `NoParking` and the funnel jam rule)
- Options: `Props` (list or tag), `Allowed` volumes (the prop's centre must be in one), `Forbidden`
  volumes, `Grace` 2 s, `RestSpeed` (0 = counts at any speed; otherwise only while slower),
  `Unless` (`Func<bool>`, for example "the player is standing up there"),
  `Action` = `Respawn` (origin pose **and** scale) or `Eject(ReturnPort)`.
- Tick: per prop that is neither held nor driven, run an out-of-bounds timer; on expiry act.
- Events: `PropReturned(prop)`. Deterministic: centre-in-volume tests in `Prop.Id` order.
- Levels: 2, 3, 6, 8, 10, 15.

**`PressurePlate`** (merges `PressureButton`, `WeightPlate` and the pedal)
- Options: `Sensor` (volume; a body counts when its centre is inside), `Mode` = `Sum` or `Heaviest`,
  `MinMass`, `AcceptPlayer` false (the player weighs 3), `AcceptTag`, `Settle` 0 s (a prop must be
  slower than 0.5 for this long before it is judged), `Latch`, `Debounce` 0.2 s, `LockProp` (on press:
  `BeginDrive`, ease to the plate centre over 0.25 s, `Grabbable = false`).
- State: `Pressed`, `Load01` (load / `MinMass`, clamped; drives the cap-travel animation), `PressedBy`.
- Events: `Pressed(prop)`, `Released`, `Rejected(prop, load01)` once per settled too-light prop.
- Deterministic: one fixed-order read of a trigger per tick.
- Levels: 3 (cup button: `Sum`, 0.04, latch), 8 (three plates: `Heaviest`, settle 0.3 s, latch,
  `LockProp`), 15 (pedal: `AcceptPlayer`, `MinMass` 2, no latch).

**`Breakable`** (merges `BreakableBarricade` and `CrushTarget`)
- Options: `Bounds` (box), `AcceptTag` (null = any body), `MinMass`, `MinSpeed`, `Direction` (the unit
  vector the speed is measured along: +Z into a barricade, −Y onto a clock), `Skin` 0.15,
  `History` 3 ticks, `OnBreak` = `RemoveCollider` or `SwapCollider(flattened)`.
- Tick: for each non-held body overlapping `Bounds` grown by `Skin` (and each body registered with
  `Watch(...)`, for driven parts), keep the last `History` ticks of its point velocity at the closest
  point, projected on `Direction`. On first overlap: if mass ≥ `MinMass` and the largest stored speed
  ≥ `MinSpeed`, break; otherwise, if that speed ≥ 1, raise `Bonked` once per approach.
- State: `Broken`. Events: `Broke(prop, speed)`, `Bonked(prop, mass / MinMass)`.
- Deterministic: overlap query + stored samples; no collision callbacks. Debris is presentation only.
- Levels: 4 (barricade: 14, 2.5, +Z, `RemoveCollider`), 15 (alarm clock: 8, 6, −Y, `SwapCollider`).

**`Socket`** (merges `SocketWell`, `Socket` and the plate latch)
- Options: `AcceptTag`, `Capture` (volume; test = prop centre inside), `MinScale`, `MaxScale`,
  `YawAxis` + `YawReference` + `YawTolerance` (optional), `MaxSpeed` 3, `SeatPose`
  (`Func<float, Pose>` of the prop's scale), `SeatScale` (optional `Func<float, float>`),
  `EaseSeconds` 0.25, `ThenFallTo` (optional: after the ease, descend under gravity until the prop's
  top reaches a height), `LockOnSeat`, `AirCapture` true (a prop released inside `Capture` with the
  conditions met is taken at once; nothing has to land).
- Tick: tagged props that are not held, in `Prop.Id` order. In window → `BeginDrive`, ease position,
  rotation (and scale) to the seat as a function of ticks since capture, then `Seated`. Out of window
  → `Rejected` once per drop, and physics keeps the prop. If not `LockOnSeat`, a grab unseats it.
- State: `Seated`, `SeatedProp`, `Fit(scale, yaw)` → `TooSmall | Good | TooBig | Backwards` (pure;
  shared with `FitGauge` and the rejection messages).
- Events: `Seated(prop)`, `Unseated(prop)`, `Rejected(prop, reason)`.
- Levels: 2 (the well), 12 (keyhole), 15 (hopper, fulcrum, collar, lane).

**`Door`** (merges `FlapDoor`, `SlidingDoor`, the gate and the tower flap)
- Options: `Panel`, `Closed` pose, `Open` pose, `Motion` = `Slide` or `Hinge(pivot, axis)`, `Seconds`,
  `Ease`, `StartsOpen`.
- Methods: `Open()`, `Close()`. State: `IsOpen`, `Moving`. Events: `Opened`, `Closed`.
- Tick: pose from ticks since the command; crush guard (waits). A hinged panel lying flat is floor.
- Levels: 3 (flap, hinge, 0.8 s), 8 (gate, slide), 12 (door, slide 0.8 s), 13 (tower flap), 15 (exit).

**`Funnel`** — static builder, not ticked.
`Funnel.Build(ctx, new FunnelOptions { Axis, RimY, MouthRadius, ThroatRadius, ConeDepth, TubeLength,
ChamberHeight, Segments = 32, Friction = 0.05 })` makes one non-convex static `MeshCollider` and
returns `Footprint` (the cylinder under the mouth) and `ChamberBox`. Polygons are **circumscribed**
(apothem = nominal radius) so no clearance is smaller than specified. Levels: 3, 8, 15.

**`ReturnPort`**
- Options: `Mouth` pose, `EjectVelocity`, `PlayerPose`, `RetrySeconds` 0.5.
- Methods: `Eject(Prop)` (pose at the mouth, at least radius + 0.1 above the floor, scale kept,
  velocity set; retried while the mouth is blocked), `Eject(Player)` (`Teleport`).
- Events: `Ejected(prop)`. Levels: 8, 15.

**`FitGauge`** (merges `FunnelGauge` and the station outlines)
- Options: `Target` (a `Socket`, or a `PressurePlate` plus a scale window), `Near` (volume), `Tag`.
- Tick: if `game.Grabber.Held` is a tagged prop whose centre is inside `Near`, state =
  `Target.Fit(held scale)`; otherwise `Idle`. Reads only.
- Events: `GaugeChanged(state)`. Presentation: lamps or outline tint — blue too small, green good,
  amber too big. It is the sanctioned world-space cue for a held toy's true size.
- Levels: 2 (outline tint), 8 (lamp rings), 15 (four outlines).

### 2.2 Movers and forces

**`WindStream`** (merges `WindStream` and `FanJet`)
- Options: `Box` (oriented), `Direction`, `Strength` 1, `PlayerAirPush` 3, `PropDrag` 12,
  `MaxPropMass` 1.
- Tick: airborne player inside → `player.AddPush(Direction · PlayerAirPush · Strength)` (nothing on
  the ground). Loose dynamic props inside that are lighter than `MaxPropMass` and not moored by a
  `SailRaft` → acceleration `min(40, PropDrag / scale)` along `Direction` (area over mass goes as 1/s).
- Methods: `Contains(point)`. Events: `WindChanged(strength)`.
- Levels: 5; 15 (the nozzle jet: pushes nothing, `Strength = (fan.Scale / 0.6)²` once the switch
  closes; `HingeChain` reads it).

**`SailRaft`** (uses `BeginDrive`)
- Options: `Prop`, `Stream`, `Launch` (volume), `Area` 0.30, `LiftPressure` 7.78, `MoorAbove` 2.5,
  `RiderMass` 3, `BoardDelay` 0.6 s, `CruiseSpeed` 6, `CruiseHeight` 1.2, `Landing` (point).
- Rule: `capacity(s) = LiftPressure · Area · s² / g − mass(s)`; it flies when capacity ≥ `RiderMass`.
- States: `Loose` (physics) → `Moored` (not held, centre in `Launch`, flat within 20°, slower than 0.2
  for 0.3 s, s ≥ `MoorAbove`: driven and pinned; still grabbable) → `Stall` (rider aboard for
  `BoardDelay`, capacity short: rise 0.3 and flop back over 1 s) or `Glide` (not grabbable; rise over
  1 s, accelerate at 6, ease onto the stream axis and yaw to it over 8 units, brake to `Landing`,
  descend over 0.8 s; no rider for 1 s → sink to the kill plane) → `Docked` (`EndDrive`).
- Events: `SailMoored`, `SailStalled(capacity / RiderMass)`, `SailLaunched`, `SailDocked`.
- Deterministic: the glide pose is a function of ticks since launch. Level: 5.

**`BouncePad`**
- Options: `Prop`, `GainPerScale` 1.75, `MinImpact` 3, `MinNormalY` 0.9, `Cooldown` 0.1 s.
- Tick: store the player's vertical speed. If the player is grounded on `Prop` this tick and was not
  last tick, the ground normal has y ≥ `MinNormalY`, the stored downward speed ≥ `MinImpact`, and the
  prop is slower than 1: `player.SetVelocity(v.x, √(2·g·Gain·scale), v.z)`. Launch speed depends on
  scale only, so bounces do not accumulate.
- State: `BounceCount`. Events: `Bounced(launchSpeed, impactSpeed)`. Level: 6.

**`Seesaw`**
- Options: `Pivot`, `Axis`, `ArmA`, `ArmB`, plank size (or an existing driven `Plank` prop),
  `RestSide` A, `PlankBias` 1 (the plank's own weight as a mass at tip A), `TipMargin` 1,
  `ReturnRate` 60°/s, `Projectile` (optional: Level 15's marble).
- Loads, per arm: mass of non-held props whose centre is in that arm's pad trigger and that touch the
  plank, plus 3 while the player is grounded on that arm.
- States: `Rest` → `Swing` when the strike side out-weighs the rider side:
  `f = clamp((M − m·r) / (M + m·r²), 0.05, 1)` with `r` = rider arm / strike arm, `M` strike load,
  `m` rider load. The strike tip accelerates down at `f·g` until it meets the floor; riders stay on
  by the engine's mover grip (the plank's speed changes by under 1 unit/s per tick). On the last tick each rider
  at distance `x` from the pivot gets vertical speed `r·√(2·g·f·h)·x / riderArm` (`h` = strike tip
  travel) through `player.SetVelocity`. → `Tipped` while the strike load stays above 0.5 → `Return` at
  `ReturnRate` (crush guard: waits) → `Rest`. `PlankBias` only decides which side is down with nobody
  on it; it is not part of `m`.
- State: `State`, `Launched` (latched per swing). Events: `SeesawStruck(M, f)`,
  `SeesawLaunched(rider, speed)`, `SeesawReturned`.
- Deterministic: closed form in ticks since the strike. Levels: 7, 15.

**`Train`**
- Options: `Center`, `Radius` 14, `DeckY`, `AngularSpeed` 24°/s, `StartBearing`, `EngineArc` 16°,
  `Cars` 8, `CarArc` 14° (deck arc 13°, deck width 3).
- Tick: bearing = start + speed × time (from `Age`); the engine and each car are one `Mover` each,
  `MoveTo(position, tangent yaw)`. They carry the player and push loose props.
- Methods: `EngineBearing`, `CarBearing(i)`, `CarMover(i)`, `BedBox(i)`. Events: `TrainAtStation(lap)`.
- Level: 9.

**`PropCarrier`** (uses `BeginDrive`)
- Options: `Train` (or any list of `Mover`s with bed boxes), `AcceptTag`, `FlatDot` 0.94,
  `MinRadius`, `MaxRadius` (optional limits on the prop's extent from a centre).
- Tick: a tagged, non-held, non-carried prop whose up axis has y ≥ `FlatDot`, that overlaps a bed box
  and lies within the radius limits is captured by the lowest-index bed: `BeginDrive`, its underside
  eased onto the deck over 0.1 s, then `MoveTo(bedPose × relativePose)` every tick. A grab releases it.
- State: `Captured`, `CarIndex`. Events: `PropCaptured(prop, index)`, `PropReleased(prop)`. Level: 9.

**`FloatPlatform`**
- Options: `Platform`, `Volume` (`WaterVolume`), `RestY`, `MaxY`, `MaxSpeed` 3, `Freeboard` 0.1.
- Tick: target top = clamp(`Volume.SurfaceY + Freeboard`, `RestY`, `MaxY`); move toward it at no more
  than `MaxSpeed`. State: `AtRest`, `AtTop`. Events: `FloatLeft`, `FloatArrived`. Level: 13.

**`PathDrive`** (merges `BallRun`, `HoopCatch`, `BaseballDrop`; also the engine behind `Socket`'s
ease and `SailRaft`'s glide)
- Options: `Body` (a prop through `BeginDrive`, or a `Mover`), `Segments` — `Roll(from, to, accel)`,
  `Ballistic(v0, untilY)`, `Ease(to, seconds)`, `Spline(points, seconds)`, `Hold(seconds)` —,
  `Offset` (for a sphere: centre = path point + radius along the surface normal), `EndVelocity`,
  `GhostToPlayer` (the body ignores the capsule while driven).
- Methods: `Start()`, `Abort()`. State: `Running`, `Segment`. Events: `SegmentEnded(index)`, `Finished`.
- Deterministic: position is a function of ticks since `Start`. Level: 15.

**`HingeChain`** (merges `DominoRun` and Level 4's `TipAssist` fallback)
- Options: `Prop`, `Pieces` (hinge line and height per piece, in the prop's space), `StartAngle` 3°,
  `ContactSin` 0.55, `RestAngle` 72°, `Target` (optional point and radius for the last piece).
- Law: each released piece turns about its hinge with `θ'' = (3g / 2h)·sin θ` (`h` = its height at
  the prop's scale); piece `i+1` is released when piece `i` reaches `asin(ContactSin)`. The last piece
  stops on `Target` if it reaches, otherwise falls flat.
- Implementation: on `Start()` the prop's own piece children are deactivated and one kinematic body
  per piece takes their place; `Reset()` stands them up in reverse order over 1 s and swaps back.
- Events: `PieceFell(i)`, `TargetStruck`, `FellShort`. Levels: 15; 4 only as the fallback.

### 2.3 Gadgets that carry one level's rule

**`NestedSet`** (promoted from Level 10's fallback)
- Options: `Props` (outermost first).
- Build: every prop but the first is inactive (hidden, no collider, not grabbable).
- Tick: the first tick `Props[i]` is held, `Props[i+1]` is activated at its authored pose and scale,
  at rest. Because the held toy keeps its footprint on screen, the reveal happens behind it.
- Events: `Revealed(index)`. Deterministic: a flag per prop. Level: 10.

**`LaserRain`**
- Options: `Emitter` (rectangle at a height), `Direction` (0, −1, 0), `Range`, `LatticePitch` 0.45,
  `ZapDelay` 0.1 s. A single `Laser` is the 1 × 1 case with any direction.
- Tick: if the player's XZ is inside the footprint, take five points on top of the capsule (the axis
  and four at 0.8 × radius) and raycast from the emitter plane along `Direction` against
  `SolidMask | Player`. If the first hit is the player for any sample, the player is `Exposed`.
  Exposed for `ZapDelay` without interruption → `Zapped`, `player.Respawn()`.
- State: `Exposed`, `Coverage01` (share of lattice beams over the walkable lane that end above the
  player's head height; refreshed a fifth of the lattice per tick). Events: `Zapped`, `AllClear`.
- Presentation raycasts each lattice beam against `SolidMask` (never `Held`) and draws beam + dot with
  instancing. Deterministic: five rays per tick in a fixed order. Level: 11.

**`WaterVolume`** (water is bookkeeping, not simulation)
- Options: `Footprint` (cylinder or box), `FloorY`, `Area`, `Volume`, `MaxSurfaceY` + `OverflowTo`,
  `LeakTo` + `LeakRate`, `WadeDepth` 0.8, `SweepDelay` 0.3 s.
- Methods: `Add(v)`, `Take(v)` (return what actually moved). State: `Volume`, `SurfaceY`
  (= `FloorY + Volume / Area`), `Depth`.
- Tick: apply the leak, clamp to the maximum and pass the excess on, then test the player: feet
  inside the footprint and more than `WadeDepth` below `SurfaceY` for `SweepDelay` → `PlayerSwept`,
  `player.Respawn()`.
- Events: `LevelChanged`, `Overflowing`, `PlayerSwept`. Level: 13.

**`Sponge`** (behaviour on a prop)
- Options: `Prop`, `Volumes` (tested in order), `CapacityPerScale3` 0.315, `AbsorbRate` 150 /s,
  `SqueezeRate` 24 /s.
- Tick, only while not held: find the first volume whose footprint contains the prop's centre. If the
  prop's lowest point is below that surface and `Stored < Capacity`, take
  `min(AbsorbRate·dt, Capacity − Stored)`. If `Stored > Capacity`, give
  `min(SqueezeRate·dt, Stored − Capacity)` to that volume.
- State: `Stored`, `Capacity` (= 0.315·s³), `Saturation01`. Methods: `EmptyInto(volume)`.
  Events: `Soaking`, `Wringing`, `Full`, `Dry`. Level: 13.

**`PortalDoorway`** (behaviour on the doorway prop; `PropBody.Fixed`)
- Options: `Prop`, `MinPlayerScale` 0.3, `MaxPlayerScale` 3.2, `Cooldown` 0.5 s, `SettleSpeed` 30.
- Settle: after a drop, box-cast the frame's footprint straight down and lower it under gravity until
  the base rests; it never rotates. Inactive while held or settling.
- Pass: on the tick the player's capsule overlaps the opening box (1.2s × 2.2s × 0.3s) and its axis
  crosses the mid-plane: `newScale = clamp(s, min, max)`, `r = newScale / player.Scale`. Exit point =
  on the far side, `0.3·newScale + 0.15·s + 0.05` beyond the plane, lateral offset scaled by `r` and
  clamped to the opening. If a capsule of the new size is free there (world + props, not the frame):
  `SetScale(newScale)`, `Teleport(exit)`, velocity × `r`. Otherwise try the mirrored point on the
  entry side with yaw + 180°. If both are blocked: `Blocked`, nothing happens.
- State: `Settled`, `TargetScale`. Events: `Settled`, `Crossed(from, to)`, `Blocked`.
- Deterministic: a pure function of player and prop state; one capsule test per candidate. Level: 14.

**`RecallPad`**
- Options: `Position`, `Radius` 0.6, `Prop`, `HoldSeconds` 0.5.
- Tick: the player's feet on the disc for `HoldSeconds` and the prop not held → the prop is placed on
  the pad at `player.Scale`, facing the player, and settles. Events: `Recalled`. Level: 14.

**`MachineDirector`**
- Options: `Start` (`PressurePlate`), `Toys`, `Links` (gadgets, in chain order), `FizzleTimeout` 2.5 s,
  `ResetSeconds` 1.5, `Messages` (link name → text for `ctx.Say`).
- States: `Idle` → `Running` on a press (toys become non-grabbable; driven parts ghost the player)
  → `Done` when the last link reports, or `Resetting` when no link has reported for `FizzleTimeout`
  (says the message of the last link reached, calls `Reset()` on every link in construction order,
  restores the toys) → `Idle`. Presses are ignored outside `Idle`.
- Methods: `Report(linkName)` (links call it as they fire). State: `State`, `LastLink`, `Runs`.
- Events: `MachineStarted`, `MachineFizzled(lastLink)`, `MachineReset`, `MachineDone`. Level: 15.

### 2.4 Which level uses which gadget

| Gadget | Levels | Gadget | Levels |
|---|---|---|---|
| `SkyCap` | 1, 2, 4–10, 12, 13, 15 | `Seesaw` | 7, 15 |
| `HazardZone` | 2, 8, 12, 15 | `Train`, `PropCarrier` | 9 |
| `PropLeash` | 2, 3, 6, 8, 10, 15 | `NestedSet` | 10 |
| `PressurePlate` | 3, 8, 15 | `LaserRain` | 11 |
| `Breakable` | 4, 15 | `WaterVolume`, `Sponge`, `FloatPlatform` | 13 |
| `Socket` | 2, 12, 15 | `PortalDoorway`, `RecallPad` | 14 |
| `Door` | 3, 8, 12, 13, 15 | `PathDrive`, `MachineDirector` | 15 |
| `Funnel` | 3, 8, 15 | `HingeChain` | 15 (4 as fallback) |
| `ReturnPort` | 8, 15 | `WindStream` | 5, 15 |
| `FitGauge` | 2, 8, 15 | `SailRaft` | 5 |
| `BouncePad` | 6 | | |

---

## 3. Toy catalog

One factory per toy in `Toybox.Toys`, authored at scale 1 with the origin at the centre of the
collider bounds unless noted. Mass = density × volume × s³ (never below 0.01). "Start" scales are per
level. Every toy takes the room's dip like everything else and its own Candy colour
(`ART_BIBLE.md` §2.2); visual-only parts (stems, bows, pips, barbs) have no collider.

| Toy | Authored size X × Y × Z | Collider(s) | Volume | Density (mass) | Friction | Bounce | Clamps | `GrabPose` | Levels |
|---|---|---|---|---|---|---|---|---|---|
| `CheeseWedge` | 0.8 × 0.5 × 1.0; height 0 at −Z rising to 0.5 at +Z | 1 convex (6 vertices) | 0.20 | 0.3 (0.06·s³) | 0.9 | 0 | 0.2–14 | `Upright` | 1 |
| `Thimble` | rim radius 0.5, top radius 0.49, height 0.8, closed top up | 1 convex (16-gon frustum) | 0.616 | 0.2 (0.123·s³) | 0.6 | 0 | 0.3–13 | `Upright` | 2 |
| `Apple` | sphere radius 0.5 | sphere | 0.524 | 4 (2.094·s³) | 0.6 | 0.1 | 0.15–12 | `Keep` | 3 |
| `Domino` | 1.0 × 2.0 × 0.3 | box | 0.60 | 0.8 (0.48·s³) | 0.7 | 0 | 0.3–6 | `Upright` | 4 |
| `Feather` | 0.4 × 0.03 × 1.0 lozenge, 0.03 thick on the quill tapering to 0.004 at the outline; plan area 0.30 | 1 convex | 0.0036 | 1.5 (0.0054·s³) | 0.6 | 0 | 0.5–12 | `Upright` | 5 |
| `Eraser` | 1.5 × 0.25 × 0.5 trapezoid: bottom 1.5 long, top 0.9; both ends 39.8° ramps | 1 convex | 0.15 | 1.2 (0.18·s³) | 0.9 | 0.3 | 0.4–10.5 | `Snap90` | 6 |
| `Pebble` | lumpy sphere, radius 0.5 | sphere | 0.524 | 3 (1.571·s³) | 0.8 | 0 | 0.2–7 | `Keep` | 7 |
| `Marble` | sphere radius 0.5 (three colours) | sphere | 0.524 | 2.5 (1.309·s³) | 0.2 | 0.2 | 0.3–6 | `Keep` | 8 |
| `Plank` | 0.6 × 0.12 × 3.0 | box | 0.216 | 0.3 (0.065·s³) | 0.8 | 0 | 0.3–4.4 | `Snap90` | 9 |
| `GiftBox` | 1 × 1 × 1 solid cube (ribbon and bow are visual) | box | 1.0 | 5.44 (5.44·s³) | 0.9 | 0 | 0.3–2.0 | `Upright` | 10 |
| `PlayingCard` | 1.4 × 0.04 × 2.0, authored lying flat | box | 0.112 | 10 (1.12·s³) | 0.8 | 0 | 0.2–7 | `Snap90` | 11 |
| `Key` | 0.7 × 0.04 × 2.0: bow plate 0.7 × 0.6 at −Z, blade 0.2 × 1.4, bit tab; top edges chamfered 45° | 3 boxes | 0.028 | 2 (0.056·s³) | 0.6 | 0 | 0.05–8 | `Snap90` | 12 |
| `ThreadSpool` | cylinder radius 0.55, height 1.0, axis Y | 1 convex (16-gon prism) | 0.950 | 0.4 (0.38·s³) | 0.8 | 0 | 0.5–9.9 | `Upright` + `KeepUpright` | 12 |
| `Sponge` | 1.0 × 0.5 × 0.7 | box | 0.35 | 0.15 (0.0525·s³) | 0.9 | 0 | 0.5–12 | `Snap90` | 13 |
| `Doorway` | frame 1.6 × 2.5 × 0.3, opening 1.2 × 2.2; origin at the threshold centre | 3 boxes (two posts, lintel); never collide with the player | — | `Fixed` | — | — | 0.1–3.2 | `Upright`, `AllowPitch = false` | 14 |
| `BouncyBall` | sphere radius 0.5 | sphere | 0.524 | 4 (2.094·s³) | 0.6 | 0.5 | 0.6–3.0 | `Keep` | 15 |
| `DeskFan` | 1.0 × 1.2 × 0.7: base 0.7 × 0.1 × 0.7, stem 0.12 × 0.25 × 0.12, guard 1.0 × 1.0 × 0.3 centred at Y 0.7; origin at the base centre | 3 boxes | 0.353 | 0.3 (0.106·s³) | 0.6 | 0 | 0.5–5 | `Upright` | 15 (Level 5's fan is the same factory as a static at scale 11) |
| `CatapultRuler` | plank 0.6 × 0.08 × 4.0 with a bottle cap (radius 0.3) on the +Z end holding a marble (radius 0.15, child) | box + short cylinder | 0.25 | 0.6 (0.15·s³) | 0.6 | 0 | 0.6–1.2 | `Snap90` | 15 (Level 7's ruler is the same visual on a `Seesaw` plank) |
| `DominoSet` | strip 3.2 × 0.05 × 0.8 carrying seven dominoes, each `h × 0.5h × 0.15h`, `h` = 0.30, 0.39, 0.51, 0.66, 0.86, 1.11, 1.45; bounding box 3.2 × 1.5 × 0.8 | 8 boxes | 0.544 | 0.8 (0.435·S³) | 0.7 | 0 | 0.5–3.5 | `Upright` | 15 |
| `Baseball` | sphere radius 0.6 | sphere | 0.905 | 10 (9.05, never rescaled) | 0.6 | 0.2 | not grabbable | — | 15 |

Notes.
- Spheres are four different toys (apple, pebble, marble, bouncy ball) only in looks, density and
  bounce; they share one collider and one mesh generator.
- `Domino` and the pieces of `DominoSet` share a mesh generator; `Plank`, `CatapultRuler` and the
  Level 7 ruler share another.
- **Walk-on rule.** Toys the player must walk onto without jumping have a tapered or chamfered edge:
  wedge (knife edge), feather (0.004·s rim), eraser (39.8° ends), key (45° chamfer), ruler tips (45°).
- Non-grabbable set pieces built by levels (not toys): spool and block pedestals, the cork float, train
  cars, doors and flaps, the seesaw plank, the alarm clock, the fan in Level 5.

---

## 4. Settings and environment presets

The six preset keys are those of `ART_BIBLE.md` §6.4, and the level-to-preset mapping is the art
bible's own. The drafts' ad-hoc keys (`lamp-desk`, `golden-boards`, `sunset-shelf`, `moon-quilt`,
`gift-nook`, `night-hall`, `hallway-drawer`, `fishbowl`, `door-hall`) are now only the names of each
level's **local set dressing**, built by the level from `RoomLit` statics.

| # | `Environment` | Setting (level-local dressing) | `GroundY` | `KillY` |
|---|---|---|---|---|
| 1 | `sunny-rug` | Inside an open toy chest: a canyon of rug between two stacks of picture books | 0 | −30 |
| 2 | `pegboard-workbench` | A pegboard walkway, 9 units above the bench top, ending in a round silo with no floor | −9 | −30 |
| 3 | `cardboard-box` | Inside an open shipping box on the den floor, under a wall shelf | 0 | −30 |
| 4 | `block-hall` | A hall of building blocks: balcony, tilted drawing board, a tall arch walled up with blocks | −2.5 | −30 |
| 5 | `sunny-rug` | Two board-game boxes standing on the rug with a gap between them; a desk fan clamped behind the first | −15 | −13 |
| 6 | `block-hall` | A cubby at the foot of a storage cabinet | 0 | −30 |
| 7 | `high-shelf` | A bookcase cubby: ruler over a fat marker, a bookend tower to the side | 0 | −30 |
| 8 | `pegboard-workbench` | The lid of a marble-run toy standing on the bench, lit from below through three funnels | −6.6 | −30 |
| 9 | `high-shelf` | A train set on trestles around a lighthouse lamp; the station is a book stack | −14 | −12 |
| 10 | `cardboard-box` | A corner of the den under a low shelf plank with a child's height chart | 0 | −30 |
| 11 | `pegboard-workbench` | Inside a lidded shoebox diorama on the bench: the lid is the laser rig, so the corridor is in shade and the beams read against it | 0 | −30 |
| 12 | `high-shelf` | The top of a toy chest of drawers standing on the shelf; an open drawer of socks | −9 | −30 |
| 13 | `cardboard-box` | A goldfish bowl on the den floor beside a stack of books | 0 | −30 |
| 14 | `night-light` | A dollhouse hallway papered with little doors | 0 | −30 |
| 15 | `night-light` | The strip of floorboards along the skirting board, seen from the edge of a quilt | 0 | −30 |

- Levels 5 and 9 are the only kill-plane drops; their decks stand 15 and 14 units above the preset's
  floor or shelf, and `KillY` sits 2 above it so nothing is seen to hit the ground.
- Level 9 was drafted as a night level and Level 8 as `night-light`; both now follow the art bible
  (night is reserved for 14 and 15). Their lamp effects still work in daylight: Signal colours are
  emissive on Ink bodies.
- Per `ART_BIBLE.md` §6.3 the preset's shell and furniture colliders exist in the simulation. Every
  level is closed by its own walls and a `SkyCap`, except Level 3 (open top by design) and Level 11
  (its own ceiling), so preset colliders never decide where an intended throw lands.

---

## 5. The levels

Each level lists: setting, layout, props, intended solution with arithmetic, tolerances, blocked
bypasses, win condition and gadgets, a bot solver outline, blurb and hints, wow beats, and a
confidence note. `WalkTo` arrival is horizontal, so the Y in a walk target is only documentation.

---

### Level 1 — The Cheese Wedge

**Setting.** `sunny-rug`, `GroundY` 0. Inside an open toy chest: a canyon of rug between two stacks of
picture books. *Fantasy: a crumb of plastic cheese becomes a hillside.*

**Layout** (rug floor Y = 0, travel +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Rug floor | −7..7 | −12..46 | top 0 |
| Book stack A (start) | −7..7 | −12..0 | top 4 |
| Book stack B (goal), sheer face at Z = 30 | −7..7 | 30..46 | top 4 |
| Leaning book (static ramp A → rug, 26.6°) | −7..−4 | 0..8 | 4 → 0 |
| Chest walls at X = ±7, Z = −12, Z = 46 | | | height 12 |
| `SkyCap` | −7..7 | −12..46 | 12 |
| Spool pedestal, radius 0.35 | 0 | −3 | 4..5 |

Spawn (0, 4, −9), yaw 0. Exit box 4 × 2 × 4 centred (0, 5, 42). The gap is 30 wide and B's face is a
sheer 4. The leaning book is always walkable both ways, so the rug is never a trap.

**Props.**

| Prop | Start | Options |
|---|---|---|
| `CheeseWedge` | centre (0, 5.1, −3) on the spool, scale 0.4, thin end toward the spawn | clamps 0.2–14 (11.2 wide at most in a 14-wide chest), `GrabPose.Upright` |

**Intended solution.**
1. Walk to (0, 4, −4.0). Eye (0, 5.55, −4.0); the wedge's centre is 1.0 ahead and 0.45 below:
   `g = √(1² + 0.45²) = 1.097`, `k = 0.4 / 1.097 = 0.365`. Grab.
2. Step round the spool to (2, 4, −0.6), 0.6 from A's edge.
3. Aim at the foot of B's face, at (0, 2.4, 25.3): yaw −4.4°, pitch −6.9°. The wedge travels out until
   its base meets the rug and its tall end meets B (half-height 0.25·s = 0.091·d, half-length
   0.5·s = 0.182·d): `d·(0.091 − sin p) = 5.55` and `d·(cos p + 0.182) = 30.6` give `p ≈ −7°`,
   `d = 25.8`.
4. Release: `s = 0.365 × 25.8 = 9.4`. The wedge is 9.4 long, 4.7 tall and 7.5 wide; toe at Z ≈ 20.2,
   tall end against B. Mass 0.06 × 9.4³ = 50, so the player (3) cannot shove it, and walking up it
   presses it into B.
5. Walk down the leaning book, across the rug, up the 26.6° cheese slope, step down 0.7 onto B. Exit.

**Tolerances** (script sweep from the edge position).
- Pitch from **−8.5° to +1.5°** works: below −7° the base meets the rug first (s 8.5–9.4, tall end
  within 2 of B, an easy hop); from −7° to +1.5° the tall end meets B and `s` stays 9.4 whatever the
  pitch. That wall is the forgiving target.
- The wedge works at any scale from 5.6 (top 2.8, a 1.2 hop up to B) to the clamp, provided its tall
  end is within about 2 of B.
- Aimed higher than +2°: it passes over B's edge and lands on top of B or stops at the clamp. It is
  in view from A; re-grab. Aimed lower than −9°: it lands mid-rug, short. Re-grab from A or from the
  rug; `k` is what the player now sees, and aiming farther fixes it.
- Standing back from the edge works too: from the grab spot itself pitch −6° to 0° gives s = 10.3–10.5.

**Blocked bypasses.**
- Jumping: 30 across and 4 up from the rug.
- The wedge as a bridge: 14 long at the clamp.
- The wedge stood on end beside B as a tower: tolerated, same lesson.
- Nothing can leave the chest (walls and `SkyCap` at 12); no pits; no prop can be out of sight.

**Win and gadgets.** Exit on B. Gadgets: `SkyCap` only.

```csharp
yield return bot.WalkTo(new Vector3(0, 4, -4.0f));
yield return bot.Grab(wedge);
yield return bot.WalkTo(new Vector3(2, 4, -0.6f));
yield return bot.DropAt(new Vector3(0, 2.4f, 25.3f));   // wedge centre when seated
yield return bot.Wait(1f);
yield return bot.WalkTo(new Vector3(-5.5f, 4, -0.5f));
yield return bot.WalkTo(new Vector3(-5.5f, 0, 9));       // down the leaning book
yield return bot.WalkTo(new Vector3(0, 0, 19.5f));
yield return bot.WalkTo(new Vector3(0, 4.5f, 29.3f));    // up the cheese
yield return bot.WalkTo(new Vector3(0, 4, 42));
```

**Blurb.** "Things are as big as they look. Pick up the cheese."
- Hint 1: "Hold the cheese and look around. It lands on whatever is behind it."
- Hint 2: "Far away means bigger. Look at the tall books across the rug."
- Hint 3: "Stand at the edge, aim the cheese at the bottom of the far book stack, and let go. Then walk
  down and climb it."

**Wow beats.**
- The release: a crumb that filled a thumbnail of screen is a 9-unit hillside, and its shadow sweeps
  across the rug as it settles. That shadow is the first time the game shows true size.
- Walking up the slope: the cheese holes are cave-sized and the rug pile reads as grass beside it.

**Confidence.** High. One heavy convex wedge settling on a flat floor against a wall.

---

### Level 2 — The Thimble Chasm

**Setting.** `pegboard-workbench`, `GroundY` −9. A pegboard walkway that ends in a round silo whose
floor is missing. *Fantasy: cork a bottomless hole with a thimble the size of a water tower.*

**Layout** (walkway top Y = 0, travel +Z). The draft's 10-wide corridor and 5.5-radius silo made the
throw fail on a 2° yaw error (the thimble scraped the corridor walls); everything is wider now.

| Element | Geometry |
|---|---|
| Walkway floor | X −7..7, Z −10..23, **minus** the disc of radius 7 about (0, 23); slab 0.6 thick |
| Walls | X = ±7 for Z −10..23, and Z = −10; height 18 |
| Silo wall | half cylinder, axis (0, ·, 23), radius 7, for Z ≥ 23; height 18. Its far point is Z = 30 |
| The hole | the whole disc of radius 7 about (0, 23); on the centre line the floor ends at Z = 16 |
| Well floor | a spring pad at Y = −9 |
| Door | in the silo wall at (0, ·, 30): opening X −1..1, Y 0..2.6; tunnel Z 30..36 |
| Painted outline | thimble silhouette 11 wide × 8.8 tall on the far wall, centred (0, 7.5, 30) |
| `SkyCap` | Y = 18 over everything |
| Spool pedestal, radius 0.4 | (0, 0, −4.5), top 1.1 |

Spawn (0, 0, −6), yaw 0, checkpoint at spawn. Exit box 2 × 2.6 × 3 centred (0, 1.3, 34).
Shortest crossing: from the wall-hugging corner (6.7, 20.97) to the door edge (1.0, 29.93) is 10.6;
on the centre line it is 14. There is no ledge: the silo wall is the well wall.

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Thimble` | centre (0, 1.42, −4.5) on the spool, scale 0.8 (0.8 across, 0.64 tall), directly in front of the spawn | clamps 0.3–**13**, tag `plug`, `GrabPose.Upright`, grabbable until seated |

**Intended solution.**
1. Grab from the spawn: eye (0, 1.55, −6), thimble centre 1.5 ahead: `g = 1.51`, `k = 0.531`.
2. Walk round the spool to (0, 0, 3).
3. Aim at the middle of the painted outline, (0, 7.5, 30): pitch 12.4°. The thimble (radius
   0.5·s = 0.266·d) stops when its rim meets the silo's far wall: `d·(cos p + 0.266) = 27`,
   `d = 27 / 1.243 = 21.7`, `s = 0.531 × 21.7 = 11.5`. It hangs with its underside 1.6 above floor
   level and its axis 1.2 from the hole's axis, filling the outline.
4. Release. The well's `Socket` takes it: it slides onto the axis, drops with a clunk, and the pad
   rises to meet it so its top is flush at Y = 0. A rubber grommet closes the ring around it.
5. Walk across the dimpled top and through the door.

**Tolerances** (script sweep: standing Z × pitch × yaw × grab distance).
- **Scale window 9–13**, and 13 is the clamp. Too far back is therefore harmless: the thimble stops
  growing, hangs short of the wall and is still captured if its centre is over the hole.
- Grabbed from the spawn (`k` = 0.53): any standing position from **Z = −4 to Z = 8** with pitch
  between 9° and 25° (from the spawn itself, pitch 9°–15°). That is twelve units of floor.
- Grabbed from 1.0 away (`k` = 0.79): the clamp is reached almost everywhere; walk forward with it
  until it hangs over the hole (stand at Z ≥ 3, aim 15° or more up) and let go.
- Grabbed from 2.5 away (`k` = 0.32): works from Z ≤ −4. From 3 or more away it cannot be made big
  enough in one throw; the thimble falls in small and the pad springs it back to the spool.
- Yaw: up to ±8° (s falls from 11.5 to 9.2 as the corridor wall starts to limit it); standing
  anywhere across the corridor and aiming at the outline works (s 9.9–11.5).
- Pitch below 8°: the thimble's underside meets the walkway and it stays small on the floor in front
  of the hole. The preview shows exactly that. Aim higher.
- The outline is the gauge: at the hole's edge the held thimble covers half of it; stepping back
  fills it. Its dashes tint green while the held thimble is in the window (`FitGauge`).

**Blocked bypasses and softlocks.**
- Jumping: 10.6 at best (limit 9). A thimble used as a springboard would have to be 7.7 tall to carry
  a sprint jump that far, and nothing gets the player onto it.
- A thimble too small for the window drops to the pad; its top is below Y = −1.8, inside the hazard.
  After 1.5 s at rest there the pad springs it back to the spool at its start scale (`PropLeash`), so
  it is never lost and never has to be fished out.
- A player who falls in returns to the checkpoint (`HazardZone`).
- Projecting the thimble over the walls: `SkyCap`.
- The seated thimble is `Fixed` and not grabbable, so the floor cannot be pulled from under the player.

**Win and gadgets.** Exit in the tunnel.
- `Socket` "well": tag `plug`; capture = cylinder radius 6 about the axis, Y −1..18 (the prop's centre
  is over the hole with 1 to spare); scale 9–13; `EaseSeconds` 0.35 (position onto the axis, rotation
  to upright); `ThenFallTo` top = 0; `LockOnSeat`. On `Seated`: enable the grommet (a flat static
  annulus from 0.49·s out to 7), disable the hazard.
- `HazardZone`: cylinder radius 7, Y −9..−1.2.
- `PropLeash`: forbidden = the well below Y = −1.5; `RestSpeed` 0.3; `Grace` 1.5 s; `Respawn`.
- `FitGauge` on the socket, `Near` = the silo above floor level; tints the outline.

```csharp
yield return bot.Grab(thimble);                          // from the spawn, 1.5 away
yield return bot.WalkTo(new Vector3(1.2f, 0, -4.5f));    // round the spool
yield return bot.WalkTo(new Vector3(0, 0, 3f));
yield return bot.DropAt(new Vector3(0, 7.5f, 30f));      // middle of the outline
yield return bot.Until(() => well.Seated, 5f);
yield return bot.WalkTo(new Vector3(0, 0, 29f), 0.3f, 12f);
yield return bot.WalkTo(new Vector3(0, 0, 34f));
```

**Blurb.** "The floor is missing. The thimble isn't."
- Hint 1: "The thimble has a flat top you could stand on, if it were big enough."
- Hint 2: "Hold it up against the outline on the round wall. Step backward to make it bigger, forward
  to make it smaller."
- Hint 3: "Pick the thimble up from right beside it, stand a few steps past the spool, cover the
  painted outline above the little door, and let go."

**Wow beats.**
- The drop: an eleven-unit chrome thimble falls past the camera into the well, pegboard dots
  streaking in its mirror finish, with a deep metallic clunk; its shadow appears on the silo wall the
  moment it is released and slides down with it.
- The grommet squeezing shut around it, and the dimples on top now the size of stepping stones.

**Confidence.** High: the seat is a scripted ease, and the only free physics is a too-small thimble
falling onto a flat pad. If the capture feels too magnetic, shrink its radius to 4 and keep the leash.


---

### Level 3 — Shrinking the Apple

**Setting.** `cardboard-box`, `GroundY` 0. Inside an open-topped shipping box on the den floor, with a
wall shelf looming above. *Fantasy: pluck a boulder-sized apple out of the sky and put it in an egg
cup.*

**Layout** (box floor Y = 0).

| Element | Geometry |
|---|---|
| Box interior | X −3..3, Z −4..4 |
| Walls, 0.3 thick | +Z wall height **4.0**; the other three height 6.0 |
| Flap door | in the +X wall, Z −1..1, Y 0..3; panel 2 × 3 × 0.08 with chamfered edges, hinged at the bottom, falls outward and lies flat |
| Outside floor | Y = 0, X 3..9, Z −3..3 |
| Funnel cup | `Funnel`: axis (1.8, −2.2), rim Y 0.6, mouth radius 0.6, throat radius 0.26 at Y 0.3, tube down to the button top at Y 0.12; outer body radius 0.7; friction 0.05 |
| Wall shelf | X −8..8, Z 24..36, top Y = 18, on a wall slab at Z 36..37 |

Spawn (0, 0, −2.5), yaw 0, **pitch +25°** (`SetSpawn` takes a pitch). Exit box 3 × 3 × 3 centred
(6, 1.5, 0). The level has no `SkyCap`: the apple has to be seen over the low +Z wall. From Z = 0 the
wall hides elevations below 31.5° and the apple's centre sits at 36.4°; it is fully in view from the
spawn (wall 20.7°, apple 26°–42.6°).

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Apple` | centre (0, 23.7, 30) on the shelf, scale **11.4** (11.4 across) | clamps 0.15–12 (it can never be bigger than it starts), `FrozenUntilGrabbed` |

**Intended solution.**
1. Walk to (1.2, 0, −1.9), beside the cup, and look up. Eye to apple centre:
   `g = √(1.2² + 31.9² + 22.15²) = 38.9`, `k = 11.4 / 38.9 = 0.293`. The ray clears the +Z wall by 1.6.
2. Grab. Nothing changes on screen (the apple is about 17° across) except that its huge shadow leaves
   the wall behind the shelf.
3. Look down into the cup, 0.67 away. The apple (radius 0.5·s = 0.147·d) arrives at `d ≈ 1.35`,
   touching the cone: `s = 0.293 × 1.35 = 0.40`, radius 0.20.
4. Release. It rolls down the tube (radius 0.26) onto the button. Mass 2.094 × 0.4³ = 0.13, above
   the button's 0.04. The button latches and the flap falls open.
5. Walk out over the flap.

The two-step route is as good: look straight down at the floor, where `d·(1 + 0.5k) = 1.55` gives
`s = 0.39`, then carry the marble to the cup.

**Tolerances.**
- Works for s from **0.27** (mass 0.04) to **0.50** (fits the tube).
- Grabbing from anywhere in the box (Z −3.7..0.5) gives `k` between 0.283 and 0.309, and dropping at
  the feet gives s between 0.38 and 0.42: the natural action lands mid-window.
- Too big: it sits in the cup's mouth. Re-grab and drop closer.
- Too small (dropped against a wall at very short range; the floor of the range is the clamp, 0.15):
  the button ticks without travelling (`Rejected`). Re-grab at the feet and drop against the far wall
  to regrow it: from `k` = 0.1 the 6-unit box gives 0.57.
- The apple leaves the box (thrown over the low wall, or dropped back near the shelf at an odd size):
  the leash returns it to the shelf at full size after 2 s.

**Blocked bypasses and softlocks.**
- Pressing the button yourself: the capsule's foot dips 0.15 into a 0.26-radius tube and stops 0.33
  above the button, and the button ignores the player.
- Climbing out: the best chain is floor → cup rim (0.6) → apple of diameter 1.85 → jump, which reaches
  3.1. The lowest wall is 4.0. A bigger apple cannot be mounted; a sphere cannot be grabbed from
  inside, but a player caught inside one walks out (collision with a toy dropped onto the player is
  suppressed until they separate).
- No windows and no gaps: the only way out is the flap.

**Win and gadgets.** Exit outside the flap.
- `PressurePlate` "button": sensor = cylinder radius 0.26, Y 0.12..0.45; `Mode` `Sum`; `MinMass`
  0.04; `Latch`; player not accepted. On `Pressed`: `flap.Open()`.
- `Door` "flap": `Hinge` on the bottom outer edge, 0° → 90° outward over 0.8 s, ease-out.
- `PropLeash`: allowed = box interior (X −3..3, Y 0..6.5, Z −4..4) or within 8 of the apple's origin;
  `Grace` 2 s; `Respawn`.

```csharp
yield return bot.WalkTo(new Vector3(1.2f, 0, -1.9f));
yield return bot.Grab(apple);                             // 38.9 away
yield return bot.DropAt(new Vector3(1.8f, 0.2f, -2.2f));  // down the cup
yield return bot.Until(() => button.Pressed, 5f);
yield return bot.Until(() => flap.IsOpen, 3f);
yield return bot.WalkTo(new Vector3(2.4f, 0, 0));
yield return bot.WalkTo(new Vector3(6, 0, 0));
```

**Blurb.** "The button wants something small. Look up."
- Hint 1: "That apple is very far away. It only looks small enough to hold."
- Hint 2: "Grab the apple, then look at something close. Your own feet are the closest thing there is."
- Hint 3: "Stand by the cup, grab the apple from the shelf, look straight down into the cup, and let go."

**Wow beats.**
- The grab: the apple peels off the shelf with no change in apparent size, and its enormous shadow
  vanishes from the wall behind it. The shadow disappearing is the tell.
- The release: a glossy marble with a leaf rattles round the funnel and the whole box side falls open
  to daylight.

**Confidence.** High. A sphere in a low-friction funnel is the most reliable set-up in the phase.

---

### Level 4 — Domino Effect

**Setting.** `block-hall`, `GroundY` −2.5. A hall of wooden building blocks: a balcony, a tilted
drawing board, and a tall arch walled up with blocks. *Fantasy: one domino, made tall as a house,
falls like a drawbridge through the wall.*

**What changed from the draft.** The draft's barricade was 6 high and flush under a solid wall. A
toppling domino is a straight rod, so its tip reaches that plane first, and for the draft's own
solution the tip arrives at Y = 5.1 — on the wall *above* the barricade, which would simply prop it up.
The barricade now fills an arch 12 high, so any tip strike lands on it.

**Layout** (hall floor Y = 0, travel +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Book-stack balcony (start) | −7..7 | −6..10 | top 3 |
| Two blocks as a stair down | 5..7 | 10..11.5 and 11.5..13 | tops 2 and 1 |
| Hall floor | −7..7 | 10..15 | 0 |
| Drawing board, 14° down-slope, friction 0.7 | −7..7 | 15..25 | 0 → −2.5 |
| Arch wall | −7..7 | 25..26.5 | −2.5..14; opening X −5.5..5.5, Y −2.5..9.5 |
| Barricade, filling the opening (11 × 12 × 1.2), front face at Z = 25 | −5.5..5.5 | 25..26.2 | −2.5..9.5 |
| Exit corridor | −5.5..5.5 | 26.5..40 | floor −2.5 |
| Hall walls at X = ±7; `SkyCap` | | | to 14 |
| Pedestal block, 0.8 × 0.8 | −3 | 8 | 3..3.9 |
| Painted footprint on the board, 4.1 × 1.25, with an inner dashed line 3.1 wide ("at least this") | centre 0 | centre 18.5 | on the board |

Spawn (0, 3, −3), yaw 0, checkpoint at spawn. Exit box 6 × 3 × 4 centred (0, −1, 36).

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Domino` | centre (−3, 4.4, 8) on the pedestal, scale 0.5, upright, pips toward −Z | clamps 0.3–6, tag `smasher`, `GrabPose.Upright` |

Mass is 0.48·s³. An upright domino tips by itself on any slope steeper than atan(0.3 / 2) = 8.5°; the
board is 14°. On the flat hall floor, or turned sideways on the board, it stands.

**Intended solution.**
1. Walk to (−3, 3, 6.9), face +Z, grab: `g = √(1.1² + 0.15²) = 1.11`, `k = 0.45`.
2. Walk to the balcony edge at (0, 3, 9.5). Eye (0, 4.55, 9.5).
3. Look slightly down (pitch −7.5°) so the domino's foot covers the painted footprint. The foot is at
   `4.55 + d·(sin p − k) = 4.55 − 0.58·d`; the board under its uphill edge is at
   `−0.25·(9.5 + 0.99·d − 0.068·d − 15) = 1.375 − 0.23·d`. They meet at `d = 9.06`:
   `s = 0.45 × 9.06 = 4.1`. The domino is 4.1 wide, 8.2 tall and 1.2 thick, mass 33, standing at
   Z ≈ 18.5.
4. Release. It settles onto the slope (14° lean), passes its 8.5° balance point and topples toward +Z
   about its downhill base edge at (Z 19.1, Y −1.0), 5.9 from the barricade.
5. The tip (8.2 from the pivot) meets the barricade at 46° from vertical, at Y = 4.6:
   `ω² = 3·22·(cos 14° − cos 46.4°) / 8.2 = 2.27`, tip speed 12.3, of which 8.5 is into the face.
   Mass 33 ≥ 14 and speed 8.5 ≥ 2.5: the barricade breaks and the domino slams flat through the arch.
6. Go down the two blocks, pass the fallen domino on its right (it is 4.1 wide in an 11-wide arch),
   and exit.

**Tolerances** (script sweep from the balcony edge).

| Pitch | s | Mass | Foot to barricade ÷ height | Tip strikes at Y | Speed into the face | Result |
|---|---|---|---|---|---|---|
| −12° | 3.3 | 17.5 | 1.18 | — | — | falls flat, short |
| −10° | 3.6 | 23 | 0.98 | 0.9 | 4.2 | breaks (just) |
| −8° | 4.0 | 30 | 0.77 | 4.1 | 8.4 | breaks |
| −6° | 4.4 | 41 | 0.58 | 6.0 | 7.7 | breaks |
| −4° | 5.0 | 59 | 0.38 | 7.6 | 5.0 | breaks |
| −3° | 5.3 | 71 | 0.28 | 8.4 | 2.7 | breaks (just) |
| −2° and up | 5.7–6.0 | 87–104 | ≤ 0.19 | — | 0 | leans on the barricade: no speed, no break |

- Working band: **pitch −10° to −3°** (s 3.6–5.3), centred on the footprint. From the hall floor the
  same band is under 2° wide, which is why the level starts on a balcony.
- The player may stand up to 2.7 back from the edge (Z ≥ 7.3); farther back the domino lands on the
  balcony, small.
- Too light (s < 3.08) or too slow: the barricade shudders in proportion to mass / 14 (`Bonked`).
  Re-grab (it comes upright) and place it again.
- Standing on the flat floor, or flush against the barricade: nothing happens. Re-grab.
- After the break the fallen domino can always be passed (at least 2.5 free on one side) or re-grabbed.

**Blocked bypasses and softlocks.**
- Going over: the arch wall reaches the `SkyCap` and the barricade fills the arch to 9.5; a domino
  leaning on it at a walkable angle tops out at 8.2, below the top, and there is nothing above anyway.
- Dropping the domino against the barricade from mid-air: it falls straight down and leans, with no
  speed into the face. No break.
- Laying it flat as a battering slab: no speed. Pushing it over by walking into it: the player weighs
  3; it will not move.
- The stair blocks are 1.0 hops, so the balcony can always be regained.

**Win and gadgets.** Exit in the corridor.
- `Breakable` "barricade": bounds X −5.5..5.5, Y −2.5..9.5, Z 25..26.2; tag `smasher`; `MinMass` 14;
  `MinSpeed` 2.5; `Direction` +Z; `RemoveCollider`. Flying blocks are presentation only, so no debris
  can jam the path.

```csharp
yield return bot.WalkTo(new Vector3(-3, 3, 6.9f));
yield return bot.Grab(domino);
yield return bot.WalkTo(new Vector3(0, 3, 9.5f), 0.15f);
yield return bot.DropAt(new Vector3(0, 3.3f, 19.0f));        // pitch -7.5: foot on the footprint
yield return bot.Until(() => barricade.Broken, 6f);
yield return bot.Wait(1.5f);
yield return bot.WalkTo(new Vector3(6, 3, 9.5f));
yield return bot.WalkTo(new Vector3(6, 0, 14.5f));            // down the two blocks
yield return bot.WalkTo(new Vector3(4.2f, -2.5f, 27f));       // beside the fallen domino
yield return bot.WalkTo(new Vector3(0, -2.5f, 36f));
```

**Blurb.** "That wall won't move for something small."
- Hint 1: "A domino only knocks down what it out-weighs. Bigger is heavier — much heavier."
- Hint 2: "Stand the domino on the tilted board, a little way back from the wall. It needs to cover
  the painted footprint, and it needs room to fall."
- Hint 3: "From the balcony edge, hold the domino so its foot sits on the painted footprint halfway
  down the board. Let go and watch."

**Wow beats.**
- The fall: a house-sized domino leans in slow, heavy silence, its shadow racing down the board ahead
  of it, then blocks burst outward and the domino lands as a bridge through the arch with a camera
  shake.
- The view through the broken arch into the next, brighter room, pips glowing on the fallen slab.

**Confidence.** Medium. The risks are the domino sliding on the board instead of pivoting, bouncing on
its first rock, or the velocity sample missing the impact tick (hence `History` 3).
**Fallback:** `HingeChain` in single-piece mode over the board: when a `smasher` is at rest and upright
there, it is taken kinematic and rotated about its downhill base edge by the same law
`θ'' = (3g / 2h)·sin θ`; on reaching the barricade plane the same mass and speed test applies, then
physics takes it back. The puzzle (size, and room to fall) is unchanged.


---

### Level 5 — The Fan and the Feather

**Setting.** `sunny-rug`, `GroundY` −15, `KillY` −13. Two board-game boxes stand on the rug with a
canyon between them, and a desk fan the size of a ferris wheel roars behind the first.
*Fantasy: lay a craft feather in the gale, step on the quill, and surf it across the gap.*

**Phase 2 principle.** Every exotic interaction from here on (wind raft, trampoline, catapult, funnel
plate, train carry) is a deterministic gadget that takes over a body kinematically or sets a velocity.
The *rule* each gadget applies is a real formula of scale (area ∝ s², mass ∝ s³, thickness ∝ s) stated
in the level, so the puzzle is honest even though PhysX is not asked to produce the result.

**Layout** (deck top Y = 0; the wind and the crossing run along +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Deck A (blotter) | −9..7 | −6..18 | top 0 |
| Fan guard (static disc, radius 5.5, hub at (−1, 5, −6)) | | plane Z = −6 | the back wall |
| Balcony (sticky-note block, start) | 7..15 | −4..10 | top 3 |
| Apron (calm strip) | 7..15 | 10..18 | top 0 |
| Leaning-ruler ramp, balcony → apron, 26.6° | 12..15 | 10..16 | 3 → 0 |
| Canyon | all | 18..42 | open down to the kill plane |
| Deck C (far box) | −9..7 | 42..58 | top 0 |
| Walls, height 12 | X = −9 and X = 15 (X = 7 beside deck C), Z = 58 | | |
| `SkyCap` | everything | | 12 |
| Spool pedestal, radius 0.4 | 11 | 2 | 3..4.1 |

Spawn (11, 3, −2.5), yaw 0, checkpoint at spawn. Exit box 4 × 3 × 3 centred (−1, 1.5, 55). The canyon
is 24 wide. The balcony is outside the wind; the player looks down and left onto deck A.

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Feather` | centre (11, 4.12, 2) on the spool, scale 1.0, quill along +Z | clamps 0.5–12, tag `sail`, `GrabPose.Upright` (flat) |

The taper makes the rim at most 0.05 high at s = 12, so the player walks onto it without jumping.

**Intended solution.**
1. Walk to (11, 3, 0.7) and grab: the feather's centre is 1.3 ahead and 0.43 below the eye,
   `g = 1.37`, `k = 0.73`.
2. Walk to the balcony edge at (7.6, 3, 4). Eye (7.6, 4.55, 4).
3. Aim at the blotter at (−1, 0.15, 12): offset (−8.6, −4.4, 8), `d = 12.5`, pitch −20.5°, yaw −47°.
4. Release: `s = 0.73 × 12.5 = 9.2`. The feather is 9.2 × 3.7, lying flat in the stream, mass 4.2. It
   settles, the raft moors it, and its barbs start to ripple.
5. Walk down the ramp, across the apron and onto the feather. After 0.6 s aboard the quill bows, the
   vane cups, and the feather lifts.
6. Ride: 37 units at 6 units/s, about 7 s. It settles on deck C. Step off and exit.

**The rule.** `capacity(s) = 7.78 × 0.30 × s² / 22 − 0.0054·s³ = 0.106·s² − 0.0054·s³`. It carries
the player (3) when `s ≥ 6.5`; the upper root is near 18, beyond the clamp.

**Tolerances.**
- Works for s from **6.5 to 12** (clamp). With `k` = 0.73 that is any drop distance from 8.9 up; from
  the balcony edge every aim point on the blotter beyond about (0, ·, 10) qualifies, and so does any
  shallow aim from deck A itself (pitch above −9° from eye height).
- Standing more than 1.5 back from the balcony edge puts the edge in the cone: the feather lands on
  the balcony, visibly.
- 2.5 ≤ s < 6.5: it lies there; boarded, it cups, lifts 0.3, shudders and flops back
  (`SailStalled`). Step off, re-grab, aim farther.
- s < 2.5: the wind slides it across the blotter and over the edge; it falls and respawns on the
  spool at scale 1.
- The feather must lie with its centre in the launch volume (the part of deck A that is in the
  stream). Elsewhere it is an ordinary prop; re-grab.
- The player falls off mid-ride: checkpoint. The riderless feather sinks, and respawns on the spool.

**Blocked bypasses.**
- The feather as a bridge: 12 long at most; from an overhanging tip a sprint jump adds 5.5 and the
  wind about 0.7. The gap is 24.
- Riding a small feather as it is blown away: the wind only moves a feather nobody stands on, and
  below 2.5 it is at most 1.0 wide.
- Projecting the feather onto deck C: with a close grab the clamp stops it over deck A or the canyon.
  With a distant grab it can be laid on deck C; it is still in view and can be taken back, but it
  gains nothing.

**Win and gadgets.** Exit on deck C.
- `WindStream`: box X −6..4, Y 0..9, Z −6..44, direction +Z; `PlayerAirPush` 3; `PropDrag` 12.
- `SailRaft`: `Launch` = X −6..4, Z −5..17.5, Y 0..1.5; `Area` 0.30; `LiftPressure` 7.78;
  `MoorAbove` 2.5; `BoardDelay` 0.6; `CruiseSpeed` 6; `CruiseHeight` 1.2; `Landing` (−1, 0, 49).

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

**Blurb.** "The wind only carries what it can catch."
- Hint 1: "Drop the little feather in the wind and watch where it goes. Now imagine standing on it."
- Hint 2: "A feather has to be much wider than you before the wind can lift you both. From up here
  the blotter is a long way down, and far means big."
- Hint 3: "From the balcony edge, aim the feather at the middle of the blotter and let go. Walk down,
  stand on the quill, and hold on."

**Wow beats.**
- Boarding: the quill bows under your weight, a hundred barbs lift and comb the wind like a wheat
  field, and the deck drops away.
- Mid-canyon: the feather's shadow slides off the blotter, vanishes into the depth, and reappears
  racing up the far box to meet you.
- Held-toy note: the held feather's barbs do not react to the wind (it is not in the world yet). They
  start rippling 0.2 s after release, with the shadow.

**Confidence.** High; the ride is a scripted mover and the only free physics is a flat prop settling
on a flat deck. Without `BeginDrive`, parent the feather's visual to an invisible kinematic platform of
the same footprint for the glide.

---

### Level 6 — Bouncing Eraser

**Setting.** `block-hall`, `GroundY` 0. The floor of a cubby at the foot of a tall storage cabinet
whose top is the only way on. *Fantasy: turn a pink eraser into a trampoline the size of a bus and
bounce onto the roof.*

**Layout** (floor Y = 0, the cabinet is at +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Floor | −12..12 | −10..18 | top 0 |
| Cabinet face (sheer, square top edge, no lip) | −12..12 | plane Z = 18 | 0..14 |
| Cabinet top (goal) | −12..12 | 18..30 | top 14 |
| Walls, height 26; `SkyCap` at 26 | X = ±12, Z = −10, Z = 30 | | |
| Spool pedestal, radius 0.4 | −6 | 9 | 0..1.1 |

Spawn (0, 0, −6), yaw 0, checkpoint at spawn. Exit box 3 × 2.5 × 3 centred (0, 15.25, 27).

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Eraser` | centre (−6, 1.2, 9) on the spool, scale 0.8, long axis along X | clamps 0.4–**10.5**, tag `bouncy`, `GrabPose.Snap90` |

Its bevelled ends are walkable 39.8° ramps at any scale: that is how the player gets onto a slab
2.4 thick.

**Intended solution.**
1. Walk to (−6, 0, 7.9), face +Z, grab: `g = √(1.1² + 0.35²) = 1.15`, `k = 0.693`.
2. Walk back to the middle of the room, (0, 0, 2). The cabinet face is `D = 16` away.
3. Aim at the face a little above eye level, at (0, 3.5, 18): pitch 6.9°. The eraser stops when its far
   edge meets the face (half-depth 0.25·s): `d·(cos 6.9° + 0.25 × 0.693) = 16`, `d = 13.7`.
4. Release: `s = 0.693 × 13.7 = 9.5`. The slab is 14.2 long at the bottom, 8.5 on top, 4.7 deep, 2.4
   thick, mass 154. It was hanging 2.0 above the floor and lands flat against the cabinet.
5. Walk round its left end and up the ramp to the top; stand about 1.2 from the cabinet face.
6. Jump. Landing on the top face at 7.6 triggers the bounce: launch speed
   `√(2 × 22 × 1.75 × 9.5) = 27.0`, apex 16.6 above the top, **19.0** above the floor.
7. Hold forward. The capsule slides up the cabinet face, clears the edge at 14 after 0.55 s and has
   1.35 s above the top: it lands about 5 units in. Exit.

**The rule.** Apex above the floor = thickness + bounce = `0.25·s + 1.75·s = 2.0·s`.

**Tolerances.**
- Comfortable for **s ≥ 7.5** (apex 15, 0.6 s above the top); 7.2 is the hard minimum. Clamp 10.5
  (apex 21).
- In standing distance: `s = 0.594·D` for this grab, so `D ≥ 12.6` (stand at Z ≤ 5.4). Beyond
  `D = 17.7` the clamp takes over and the eraser hangs short of the cabinet; it still works, because a
  21-high bounce gives 1.6 s above the top and a sprint drift of 14.
- Pitch anywhere from 0° to 25°. Below 0° it meets the floor first and stays smaller.
- The intended first failure: grabbing and looking at the cabinet without stepping back (`D` = 10.1)
  gives s = 6.0 and an apex of 12.1, two units short. Stepping back while holding visibly grows it.
- Grabbed from the side (long axis along the view): the half-depth is 0.75·s and `s = 0.456·D`; step
  back farther. Upside down: `F` twice, or re-grab (`Snap90` keeps it on a broad face).

**Blocked bypasses and softlocks.**
- Leaning it against the cabinet as a ramp: the longest edge is 15.75, so reaching 14 needs a 62.7°
  lean; the limit is 50°. That is why the clamp is 10.5.
- Standing it on end: 15.75 tall but sheer. Step plus jump: 2.6 + 1.25.
- Re-bouncing to gain height: launch speed depends on scale, not on impact speed.
- Eraser thrown onto the cabinet top with a distant grab (small `k`): from the floor it may be out of
  sight. `PropLeash` returns it to the spool after 2 s unless the player is up there too.

**Win and gadgets.** Exit on the cabinet top.
- `BouncePad` on the eraser: `GainPerScale` 1.75, `MinImpact` 3, `MinNormalY` 0.9 (the ramps, normal
  y = 0.77, never bounce), `Cooldown` 0.1 s. Presentation squashes the slab by impact / 30 for 0.15 s.
- `PropLeash`: forbidden = the eraser's centre above Y = 14; `Unless` the player is grounded above
  13.5; `Grace` 2 s; `Respawn`.

```csharp
yield return bot.WalkTo(new Vector3(-6, 0, 7.9f));
yield return bot.Grab(eraser);
yield return bot.WalkTo(new Vector3(0, 0, 2));
yield return bot.DropAt(new Vector3(0, 3.5f, 18));
yield return bot.Wait(1.5f);
yield return bot.WalkTo(new Vector3(-9.5f, 0, 11));        // round the pedestal and the slab
yield return bot.WalkTo(new Vector3(-9.5f, 0, 15.6f));
yield return bot.WalkTo(new Vector3(-3.5f, 2.4f, 15.6f));  // up the left ramp
yield return bot.WalkTo(new Vector3(0, 2.4f, 16.6f));
yield return bot.Jump(false);
yield return bot.Until(() => pad.BounceCount >= 1, 3f);
yield return bot.WalkTo(new Vector3(0, 14, 21), 0.5f, 6f);  // steer in the air
yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > 13.5f, 4f);
yield return bot.WalkTo(new Vector3(0, 14, 27));
```

**Blurb.** "Rubber remembers how to jump."
- Hint 1: "Climb the eraser by its slanted end and jump on it. How high you go depends on how much
  rubber is under you."
- Hint 2: "Hold the eraser against the cabinet and walk backward. The farther back you stand, the
  bigger it gets, and the higher it throws you."
- Hint 3: "Stand in the middle of the room, press the eraser against the cabinet, let go, climb on,
  stand close to the cabinet and jump. Push forward at the top."

**Wow beats.**
- The landing: a pink slab the size of a bus falls two units and the whole floor shivers; dust jumps
  off the boards in a ring.
- The bounce: the slab dishes under your feet, the cabinet face streaks past for a full second, and
  the room opens out below you at the apex.

**Confidence.** High. The bounce is a velocity set and the eraser is a heavy convex slab on a flat
floor. Keep the cabinet's top edge square and flush so the capsule does not catch on it.


---

### Level 7 — The Teeter-Totter

**Setting.** `high-shelf`, `GroundY` 0. A bookcase cubby where a plastic ruler lies across a fat
marker at the foot of a tall bookend. *Fantasy: stand on the low end of the ruler, turn a pebble into
a boulder at the far end, and be flung onto the roof.*

**Layout** (floor Y = 0, the ruler runs along Z).

| Element | X | Z | Y |
|---|---|---|---|
| Floor | −10..10 | −6..15.5 | top 0 |
| Backstop wall, with a painted boulder outline (4.6 across) centred (0, 6.6, 15.5) | −10..10 | plane Z = 15.5 | 0..20 |
| Side and back walls; `SkyCap` | X = ±10, Z = −6 | | to 20 |
| Bookend tower (goal), sheer faces, square top edge | 2..10 | −6..5 | top 11 |
| Marker (static cylinder, axis along X, radius 0.9) | −2..2 | axis Z = 10 | axis Y 0.9, top 1.8 |
| Ruler (`Seesaw` plank, kinematic), 15 × 3 × 0.2, tips and edges chamfered 45° | −1.5..1.5 | 0..15 | pivots on the marker's top line (Y 1.8, Z 10) |
| Bobbin tower (static, radius 0.25) | −2.1 | 0.7 | 0..2.3 |

- Long arm 10 (toward −Z), short arm 5 (toward the backstop): arm ratio `r = 2`.
- At rest the long tip is on the floor at Z ≈ 0.2: tilt asin(1.8 / 10) = 10.4°, short tip at Y 2.7.
  Tipped, the short tip is on the floor: tilt 21.1°, long tip at Y 5.4.
- The seat is a painted bullseye at Z = 0.7 on the long arm (9.3 from the pivot); the ruler's top there
  is at Y 0.30. The tower's face at X = 2 is 0.5 from the ruler's edge.
- Spawn (−5, 0, −3.5), yaw 0, checkpoint at spawn. Exit box 3 × 2.5 × 3 centred (7, 12.25, −2).

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Pebble` | centre (−2.1, 2.6, 0.7) on the bobbin, scale **0.6**, an arm's length from the seat | clamps 0.2–**7**, tag `weight`, `FrozenUntilGrabbed` |

The draft kept the pebble (scale 0.4) on a low spool five units from the seat, which made the level
fail for any pickup from farther than 1.6 and — as the script check showed — also for very close
pickups, where the clamp stopped the boulder short of the pivot. Perched beside the seat at eye
height, every pickup the player can make from within 2.5 works, and the clamp is never the limiter.

**Intended solution.**
1. Walk to (−2.1, 0, −0.7) and grab the pebble: `g = √(1.4² + 1.05²) = 1.75`, `k = 0.6 / 1.75 = 0.343`.
2. Step onto the ruler over its tip and stand on the bullseye, facing +Z. Eye (0, 1.85, 0.7).
3. Aim at the outline on the backstop above the raised end, (0, 7.5, 15.5): pitch 20.9°. The pebble
   stops when it touches the wall, 14.8 away: `d·(cos 20.9° + 0.5 × 0.343) = 14.8`, `d = 13.4`.
4. Release: `s = 0.343 × 13.4 = 4.6`, a boulder 4.6 across, mass 1.571 × 4.6³ = **151**. It hangs
   at (0, 6.6, 13.2), 1.7 above the short arm, and falls onto it.
5. The lever: `f = (151 − 2·3) / (151 + 4·3) = 0.89`. The short tip is driven down 2.7 at 0.89 g in
   0.53 s; the seat is released at Y 5.35 with speed `2·√(2·22·0.89·2.7) × 9.3 / 10 = 19.1`.
6. Apex = 5.35 + 19.1² / 44 = **13.7**. Hold right (toward the bookend): the capsule slides up its
   face, is above Y 11 from 0.38 s to 1.36 s after release, and lands on top. Exit.

**The rule.** `apex = 5.35 + 9.34·f`, `f = (M − 6) / (M + 12)`, `M = 1.571·s³`. The top at 11 needs an
apex of 11.6: `f ≥ 0.67`, `M ≥ 42`, **s ≥ 3.0**; comfortable from 3.3. At the clamp (7) the apex is 14.4.

**Tolerances** (script sweep of pickup distance × pitch).

| Pickup | `k` | Pitch that works | Boulder |
|---|---|---|---|
| From the floor at the bobbin's foot (closest possible, 1.26) | 0.48 | 16°–45° | 5.2–7 |
| The bot's spot (1.75) | 0.34 | 13°–50° | 4.2–6.2 |
| From the seat itself (2.23) | 0.27 | 13°–50° | 3.6–5.1 |
| From 2.8 away | 0.20 | only above 30° | 3.1–4.0 |
| From 3.5 away or more | ≤ 0.16 | (short) | 2.3–2.7: thrown to 9–10, visibly not enough |

- Below 13° the pebble touches the ruler on the player's own arm and stays there, small; the preview
  shows it sitting in front of the marker. The wall outline says where to look.
- Where the player stands matters physically: launch speed scales with distance from the pivot, and
  two units in from the tip costs a fifth of the height. Hence the bullseye.
- Dropped the boulder first and walked to the seat afterwards: the long end is up; nothing launches.
  Grab the boulder (now big and near, so `k` is large), stand on the bullseye, and drop it again.
- Boulder rolls off sideways: the ruler returns; re-grab.

**Blocked bypasses and softlocks.**
- Boulder as a step: 7 high at most plus 1.25; the tower is 11, and a sphere that size cannot be
  climbed. Standing on the tipped-up long end (5.4) and jumping: 6.65.
- Standing on the short end with the boulder on the long end: `r` = 0.5, a quarter of the height.
- Walking out along the short arm tips the ruler gently under the player (3 × 5 against the plank's
  bias 1 × 10). Nothing is gained.
- The bobbin (2.3) cannot be mounted.
- Crush guard: the return swing waits while the player is under the long arm; a strike with the
  player under the short arm suppresses plank–player collision for that swing (the engine already lets
  the boulder itself pass through a player it comes down on).

**Win and gadgets.** Exit on the bookend.
- `Seesaw`: pivot (0, 1.8, 10), axis X, `ArmA` 10 (seat side, down at rest), `ArmB` 5, plank
  15 × 3 × 0.2, `PlankBias` 1, `TipMargin` 1, `ReturnRate` 60°/s. Pad B = Z 10.5..15.5, the plank's
  width plus 1 each side, from the plank up 8; pad A = the long arm. Because the strike tip never
  accelerates faster than gravity, the dynamic boulder stays pressed on it for the whole swing.
  Presentation bends the ruler with a shader flex.

```csharp
yield return bot.WalkTo(new Vector3(-2.1f, 0, -0.7f), 0.15f);
yield return bot.Grab(pebble);                                // 1.75 away, k = 0.34
yield return bot.WalkTo(new Vector3(0, 0, -0.6f));
yield return bot.WalkTo(new Vector3(0, 0.3f, 0.7f), 0.15f);   // over the tip onto the bullseye
yield return bot.DropAt(new Vector3(0, 7.5f, 15.5f));
yield return bot.Until(() => seesaw.Launched, 4f);
yield return bot.WalkTo(new Vector3(5, 11, 0.7f), 0.5f, 5f);  // steer right in the air
yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > 10.5f, 4f);
yield return bot.WalkTo(new Vector3(7, 11, -2));
```

**Blurb.** "A lever only argues with something heavier than you."
- Hint 1: "Stand on the low end. Something has to land on the high end, and it has to weigh far more
  than you do."
- Hint 2: "You have to be standing on the bullseye when it lands. Hold the pebble and look at the wall
  above the far end: weight grows much faster than size."
- Hint 3: "Take the pebble off its perch, stand on the bullseye, cover the outline on the far wall
  with it, and let go. When you fly, push toward the bookend."

**Wow beats.**
- The strike: the boulder's shadow swells on the short arm, the ruler bends, and the bookend's spines
  blur past as you go up.
- The apex: a held breath above the bookend with the boulder, the marker and the bent ruler tiny below.

**Confidence.** Medium. The rule is closed-form, but it relies on a kinematic plank carrying the
capsule upward at up to 19 units/s without the solver adding or eating speed, which is why the release
sets the velocity explicitly.
**Fallback (`LaunchSeat`).** Keep the ruler as a visual lever and make the seat a static 3 × 3 pad.
When a `weight` prop comes to rest in pad B, wait the same swing time, apply the same launch velocity
to a player on the seat, and swap the ruler's static collider to the tipped pose.

---

### Level 8 — Funnel Physics

**Setting.** `pegboard-workbench`, `GroundY` −6.6. The lid of a marble-run toy, lit from below through
three funnels of different sizes. *Fantasy: you are the sorting machine — make one marble grow, keep
one as it is, and pull one out of the sky, until each rattles down its own hole.*

**Layout** (lid top Y = 0).

| Element | X | Z | Y |
|---|---|---|---|
| Floor (lid) | −14..14 | −12..16 | top 0 |
| Walls; `SkyCap` | X = ±14, Z = −12, Z = 16 | | to 14 |
| Gate in the +Z wall (`Door`, closed) | 0..3 | 16 | 0..3; tunnel to Z = 22 |
| Niche above the gate | −1.5..4.5 | 16..21.5 | 6..12 |
| Return port in the −X wall | −14 | 0 | mouth Y 0.3..1.9 |
| Spool pedestal, radius 0.4 | −10 | −6 | 0..1.1 |

Funnels (`Funnel`), all on the line Z = 9, cone slope 35° (0.70 down per unit of radius), friction 0.05:

| Funnel | Axis (X, Z) | Throat radius | Mouth radius | Cone depth | Tube | Chamber | Plate at Y |
|---|---|---|---|---|---|---|---|
| S | (−8, 9) | 0.40 | 1.0 | 0.42 | 1.0 | 1.2 | −2.62 |
| M | (−2, 9) | 0.80 | 1.9 | 0.77 | 1.0 | 2.0 | −3.77 |
| L | (6.5, 9) | 1.60 | 3.6 | 1.40 | 1.0 | 3.6 | −6.00 |

Rim-to-rim gaps are 3.1 (S–M) and 3.0 (M–L); the gate is reached through the M–L gap. Spawn
(0, 0, −9), yaw 0, checkpoint at spawn. Exit box 3 × 3 × 3 centred (1.5, 1.5, 19.5), behind the gate.

**Props.** Three `Marble`s, interchangeable (colour is flavour), clamps 0.3–6, tag `marble`,
grabbable until latched.

| Marble | Start | Start scale | What it needs |
|---|---|---|---|
| Peewee (red) | (−10, 1.23, −6) on the spool, `FrozenUntilGrabbed` | 0.25 | grow |
| Aggie (yellow) | (10, 0.6, −8) on the floor | 1.2 | stay about the same |
| Shooter (blue) | (1.5, 8.5, 18.6) in the niche, `FrozenUntilGrabbed` | 5.0 | shrink |

**The rule.** A marble passes a throat when its radius is smaller than the throat's; the plate below
needs a minimum mass (1.309·s³).

| Funnel | Passes when | Plate `MinMass` | Scale window | Looks like |
|---|---|---|---|---|
| S | s < 0.80 | 0.164 (s = 0.50) | **0.50–0.76** | marble 62–95 % of the hole |
| M | s < 1.60 | 1.31 (s = 1.00) | **1.00–1.52** | same |
| L | s < 3.20 | 10.5 (s = 2.00) | **2.00–3.04** | same |

The windows are disjoint, each a factor of 1.5 wide; the upper ends keep 5 % clearance.

**Intended solution.** The thing to discover: the marble keeps its size on screen, but the hole gets
bigger on screen as you walk toward it. *Stand where the marble looks a little smaller than the hole.*
The lamp ring confirms it. A marble aimed across a funnel passes over the mouth and stops against the
far cone wall, so that wall is the backstop. For an eye at horizontal distance `h` from the axis
looking down at angle `β`, the rest point `x` solves
`1.55 + 0.7·h + c − (0.7 + tan β)·x = 1.2207·(0.5·k / cos β)·x`, with `c` = 0.70 × mouth radius.

1. **Peewee → S.** Grab from (−10, 0, −7.1): `g = 1.15`, `k = 0.218`. Walk to (−8, 0, 7.0) (`h` = 2.0).
   Aim at (−8, 0.08, 9.55), 30° down: `3.65 = 1.431·x`, `x = 2.55`, `d = 2.94`, **s = 0.64**.
   It rolls into the throat; mass 0.34 ≥ 0.164; plate S latches.
2. **Aggie → M.** Grab from (10, 0, −3.3): `g = 4.80`, `k = 0.250`. Carry it to (−2, 0, 5.0) (`h` = 4.0).
   Aim at (−2, 0.27, 10.13), 14° down: `5.68 = 1.107·x`, `x = 5.13`, `d = 5.29`, **s = 1.32**.
   Mass 3.0 ≥ 1.31; plate M latches.
3. **Shooter → L.** From (6.5, 0, −3) look up into the niche and grab: `g = 23.2`, `k = 0.215`.
   Walk to (6.5, 0, 0.2) (`h` = 8.8). Aim at (6.5, 0.76, 11.55), 4° down: `10.23 = 0.901·x`,
   `x = 11.35`, `d = 11.4`, **s = 2.45**. Mass 19.2 ≥ 10.5; plate L latches.
4. The gate opens. Walk between M and L to the exit.

**Tolerances** (script sweep of standing distance × pitch for the grabs above).

| Funnel | Stand this far from the axis | Pitch band there |
|---|---|---|
| S | 1.5–2.5 | −50° to −25° |
| M | 2.5–5.0 | −25° to −9° |
| L | 6–12 | −12° to −2° |

- Too big: it sits in the mouth. Grab it and step closer.
- Too small, or the wrong hole: it falls through, the plate ticks without travelling, and after 0.6 s
  the return port rolls it back into the room at the same scale.
- A different pickup distance changes `k`, never the rule: any marble can serve any funnel. A marble
  latched in the "wrong" funnel is not an error; the other two can still be made to fit what is left.
- The player in a funnel (all throats are wider than the capsule): the chamber's `HazardZone` sends
  them out of the return port.

**Blocked bypasses and softlocks.**
- Two light marbles on one plate: plates test the heaviest single prop. The player as a weight:
  ignored. A latched marble is not grabbable, so no marble serves twice and no plate is un-pressed.
- Jamming: a marble at rest below the lid, un-latched, for 1.5 s is ejected through the return port.
- Shooter is out of reach but in grab range from the whole back half of the room; nothing else in the
  level is above Y = 3, and the marbles cannot be used to climb (the niche floor is at 6).

**Win and gadgets.** Exit behind the gate; `gate.Open()` when all three plates are pressed.
- `Funnel` × 3 as tabled (32 segments, circumscribed).
- `PressurePlate` × 3: `Sensor` = the chamber box, `Mode` `Heaviest`, `MinMass` as tabled,
  `Settle` 0.3 s, `Latch`, `LockProp`. On `Rejected`: after 0.6 s, `port.Eject(prop)`.
- `ReturnPort`: mouth (−13.2, 1.1, 0), velocity (3, 0, 0); player pose (−12.5, 0, 0), yaw 90°.
- `PropLeash` (jam rule): forbidden = each `Funnel.Footprint` below Y = −0.1; `RestSpeed` 0.3;
  `Grace` 1.5 s; `Eject`.
- `HazardZone` × 3 in the chambers, `OnCaught` = `port.Eject(player)`.
- `FitGauge` × 3: `Near` = within mouth radius + 0.5 of each axis; drives a ring of lamps round the
  mouth. The lamps are world objects and are the sanctioned cue for the held marble's true size.
- `Door` "gate": slide up 3 over 0.8 s.

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

**Blurb.** "Three holes. Three marbles. None of them the right size."
- Hint 1: "A marble has to nearly fill its hole: small enough to fall through, heavy enough to press
  the plate underneath. The lamps round each funnel tell you which way you are off."
- Hint 2: "The marble never changes size on your screen, but the hole does. Walk toward a funnel and
  the marble shrinks against it; back away and it grows."
- Hint 3: "Little red one: stand two steps from the small funnel. Yellow: four steps from the middle
  one. Blue, up in the niche: grab it from the back of the room and drop it in the big funnel from
  about nine steps away."

**Wow beats.**
- Shooter leaving the niche: a blue glass planet with a cat's-eye swirl slides off its shelf, crosses
  the ceiling without changing size, and ends up as a ball you could hug.
- Each latch: the marble lights from inside in its own colour and the light floods up through the
  funnel onto the ceiling. With all three lit the room is striped red, yellow and blue and the gate
  rolls up.

**Confidence.** Medium-high. Spheres in low-friction cones are reliable; the risks are a marble within
a few percent of the throat radius wedging (the jam rule) and small fast marbles tunnelling (the engine
already switches small or fast props to continuous detection).
**Fallback (`FunnelCapture`).** When a released marble's centre is inside the mouth cylinder and it
touches the cone, drive it with a `PathDrive`: under the throat radius, a fixed 0.8 s spiral into the
throat and a drop onto the plate; otherwise seat it on the cone and hand it back to physics.


---

### Level 9 — The Moving Train

**Setting.** `high-shelf`, `GroundY` −14, `KillY` −12. A toy train runs a loop on trestles round a
lighthouse-shaped lamp; the station is the top of a book stack and there is nothing under the track
but a fourteen-unit drop. *Fantasy: throw a plank across the gap like a clock hand, one end on the
lighthouse and the other on the moving train, then ride the train out to it and walk in.*

**Layout** (station top Y = 0; the lighthouse axis is X = 0, Z = 20; `r` is measured from it).

| Element | Geometry |
|---|---|
| Station ledge (start) | X −9..9, Z −10..3.3, top Y 0. Its edge is at r = 16.7 on the centre line |
| Lighthouse tower | static cylinder, radius 2, Y −14..16, sheer |
| Doorstep ring (goal) | annulus r 2..3.5 round the tower, top Y −1, 0.5 thick |
| Track | circle r = 14. **Rails and trestles are visual only: no collider** |
| Train decks | r 12.5..15.5, top Y −1, velcro beds (see `PropCarrier`) |
| Room walls; `SkyCap` | X = ±20, Z = −10, Z = 40; to Y 16 |
| Spool pedestal, radius 0.4 | (2.5, 0, −1), top 1.1 |

Gaps: station edge to deck 1.2 across and 1.0 down on the centre line (a step off lands on the deck,
a walking jump gets back up); deck to ring **9.0**; station edge to ring 13.2. Spawn (0, 0, −7),
yaw 0, checkpoint at spawn. Exit box **5 × 3.5 × 5** centred (0, 0.25, 20): reaching the ring from any
side wins. (The draft's 6.6-wide box could be touched in mid-air 7.8 from a deck; this one is 8.7.)

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Plank` | centre (2.5, 1.14, −1) on the spool, scale 0.65, long axis along Z | clamps 0.3–**4.4**, tag `plank`, `GrabPose.Snap90` |

At the clamp the plank is 13.2 long: exactly the station-to-ring distance, so it can never rest on both.

**Intended solution.**
1. Walk to (2.5, 0, −2.4) and grab the plank end-on: `g = √(1.4² + 0.41²) = 1.46`, `k = 0.445`.
2. Walk to the station edge at (0, 0, 2.9). The tower wall is `D = 15.1` away.
3. Aim at the tower just above the ring, at (0, 0.9, 18): pitch −2.5°. The plank stops when its far end
   touches the tower (half-length 1.5·s): `d·(cos 2.5° + 1.5 × 0.445) = 15.1`, `d = 9.05`.
4. The preview is `s = 0.445 × 9.05 = 4.03`: 12.1 long, 2.4 wide, 0.48 thick, hanging level at Y 1.16
   from the tower (r = 2.0) out to r = 14.1, over the circle the decks run on. Hold it there.
5. Wait for the train. Release while cars are passing under the near end. The plank falls 1.9 in
   0.42 s (the train moves 10° meanwhile), lands with one end on a deck and the other on the ring, and
   the car's velcro bed grips it. It is now a spoke sweeping round the lighthouse with the train.
6. Board: step down onto a following car at once and walk forward along the train to the plank, or
   wait one lap (15 s) and jump onto the plank's car as it passes.
7. Walk in along the plank. It turns under you but always points at the lighthouse, so walking toward
   the light stays on it. Reaching the ring completes the level.

**Tolerances** (script sweep).
- The tower backstop sets the size: `L = 0.80·D` for this grab. Standing anywhere from the edge back
  to **Z = 0.3** gives s between 4.0 and the clamp with the inner end on the ring and the outer end on
  the decks, for any pitch from −6° to +15°. Farther back the clamped plank hangs short of the tower.
- Needs s ≥ 3.7 to reach from tower to deck. A pickup from 1.9 away (`k` = 0.34) gives `L = 0.68·D`:
  step back 1.3 from the edge and it reaches.
- Timing: decks are under the drop point for about 5 s of every 15 s lap (eight cars of 14°, plus the
  plank's own 10° of width). The headlamp and whistle announce the train 3 s ahead.
- Missed the train: the plank tips off the ring, falls and respawns on the spool at 0.65. About 20 s.
- Landed crooked: grab it from the station as it passes (a grab releases it from the car).
- The player falls: back to the station. A player riding the bare train can hop back up on any lap.

**Blocked bypasses and softlocks.**
- A static bridge from the station to the ring needs more than 13.2; the clamp is 13.2.
- Jumping from the train to the ring: 9.0; the train's speed is tangential and does not help.
- **Diving board** (a clamped plank lying across a deck, pointing inward, with no ring support). The
  draft treated this as blocked; a check shows a sprinting player could run out and jump before a
  loose plank tips. It is now simply allowed and made reliable: any flat plank on a bed is gripped
  rigidly. Centred on a deck its inner end is at r = 7.4, a 3.9 jump from the ring. It needs the same
  two things as the intended route — a near-clamp plank and a release timed onto a moving car.
- A captured plank whose outer end would sweep the station (r > 16.5) or whose inner end would cut the
  tower (r < 1.95) is not gripped.
- The plank is in view from the station every lap; on the ring or the station it is always grabbable.

**Win and gadgets.** Exit box round the ring.
- `Train`: `Center` (0, 20), `Radius` 14, `DeckY` −1, `AngularSpeed` 24°/s (lap 15 s, 5.9 units/s at
  the deck), `StartBearing` −150°, `EngineArc` 16°, 8 cars of 14° (deck arc 13° = 3.2 long, width 3,
  coupling gaps 0.24: too narrow to fall through). Bearing φ: position
  `(14·sin φ, ·, 20 − 14·cos φ)`; φ = 0 is the station, increasing toward +X. Car `i` (1..8) is at
  engine − 15° − 14°·(i − 1). The engine is 1.0 tall with a funnel to Y 0.4, below a held plank.
- `PropCarrier`: tag `plank`, `FlatDot` 0.94, bed = a 0.3-high box on each deck, `MinRadius` 1.95,
  `MaxRadius` 16.5 about the tower axis.

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
yield return bot.WalkTo(tower, 2.6f, 12f);                 // inward along the turning plank
```

**Blurb.** "The train never stops. Neither should your bridge."
- Hint 1: "You can ride the train, but it never gets closer to the lighthouse. Something has to reach
  from the train to the doorstep — and travel with it."
- Hint 2: "Hold the plank end-on against the lighthouse wall. From the platform edge it stretches all
  the way back to the track. It needs something under its near end when you let go."
- Hint 3: "Stand at the platform edge, press the plank against the lighthouse just above the doorstep,
  wait until the wagons are passing underneath, and release. Then hop on the train and walk the plank."

**Wow beats.**
- The catch: the plank drops, slaps onto a passing wagon, and instead of falling it is suddenly
  *moving* — a twelve-unit clock hand whose shadow wheels across the shelf wall.
- The walk: the room turns around you while the lighthouse stays dead ahead, and the train's lit
  windows run along beside your feet.

**Confidence.** Medium. The train and the carried plank are kinematic and closed-form; the risks are
the capsule riding a mover that both translates and rotates at 5.9 units/s, and the 0.4 s in which the
released plank is a free body falling onto a moving deck. If the second is unreliable, capture in the
air: grip a falling plank whose support conditions are already met.
**Fallback (straight ferry).** A straight out-and-back track between the station and a platform 24
away; the cars are open bunks (two cross-beams, 4 apart). A plank dropped lengthwise across two bunks
is gripped and becomes the deck. Same sizing rule, same timing rule, no rotation.

---

### Level 10 — Matryoshka Boxes

**Setting.** `cardboard-box`, `GroundY` 0. A corner of the den under a low shelf plank, with a child's
height chart painted up the shelf's edge. *Fantasy: one gift box turns out to be three, and three boxes
turn out to be a staircase.*

**Layout** (floor Y = 0, travel +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Rug floor | −8..8 | −6..16 | top 0 |
| Shelf plank (goal ledge), sheer front face at Z = 12 | −8..8 | 12..16 | top 3.9 |
| Walls at X = ±8, Z = −6, Z = 16; `SkyCap` | | | to 12 |
| Height chart (decal on the ledge face: ticks every 1.0, colour bands at 1, 2 and 3) | −4..0 | 12 | 0..3.9 |

Spawn (0, 0, −4), yaw 0. Exit box 3 × 2.5 × 3 centred (4, 5.15, 14). No hazard; nothing can be lost.

**Props.** Three `GiftBox`es, one inside the next. They are **solid cubes** revealed by a `NestedSet`
(the draft's thin-walled open shells are gone: same puzzle, no 0.04-thick colliders nested 0.08 apart).

| Box | Rest pose | Start scale | Options |
|---|---|---|---|
| Red (outer) | centre (0, 0.6, 0) | 1.2 | clamps 0.3–**2.0**, `GrabPose.Upright` |
| Yellow | centre (0, 0.45, 0), hidden until red is first grabbed | 0.9 | same |
| Teal | centre (0, 0.3, 0), hidden until yellow is first grabbed | 0.6 | same |

Masses 5.44·s³: 5.4 at 1.0, 37 at 1.9 (the player is 3). **The clamp is the puzzle:** no box can be
taller than 2.0, the ledge is 3.9 and a hop is 1.15, so one box must stand on another.

**Intended solution.** A (1.0) on the rug, B (1.9) against the ledge, C (1.0) on B at the back.
1. **Red → B.** Stand at (0, 0, −2.6): `g = √(2.6² + 0.95²) = 2.77`, `k = 0.434`. Grab; the yellow box
   is simply there. (The held box now stops against the yellow one, a little in front of it.)
2. Walk to (−2, 0, 6.7). Aim at the foot of the ledge face. The box stops when its base meets the rug
   and its far face meets the ledge (half-size 0.217·d): `d·(0.217 − sin p) = 1.55` and
   `d·(cos p + 0.217) = 5.3` give `p ≈ −7.9°`, `d = 4.38`. Release: `s = 0.434 × 4.38 = 1.90`.
   B occupies X −2.95..−1.05, Z 10.1..12, top 1.9.
3. **Yellow → A.** Stand at (0, 0, −2.3): `g = 2.55`, `k = 0.353`. Grab, walk back to (−2, 0, 6.7), look
   down at the rug about 2.7 ahead (pitch −21°): `d·(0.176 − sin p) = 1.55`, `d = 2.87`,
   `s = 1.01`. A occupies Z 8.9..9.9, top 1.0, 0.2 short of B.
4. **Teal → C.** Stand at (0, 0, −1.9): `g = 2.27`, `k = 0.264`. Grab, walk to (−2, 0, 8.0), aim at the
   height chart above B at Y ≈ 3.0 (pitch +22.5°). The box stops when its far face meets the ledge face:
   `d·(cos p + 0.132) = 4.0`, `d = 3.78`, `s = 1.0`. It is released with its base at Y 2.5 and falls
   0.6 onto B. C occupies Z 11..12, leaving a 0.9-deep tread on B.
5. Climb: rug → A (1.0) → B's tread (0.9) → C (1.0) → ledge (1.0). Walk to the exit.

**Tolerances.** With 1.15 as the largest rise and 0.7 as the smallest tread:

| Box | Works for | Why | In aim terms (script sweep) |
|---|---|---|---|
| B | 1.6–2.0 | B + C ≥ 2.75; the clamp caps it | pitch −11° or higher from (−2, 0, 6.7); from −7° up the ledge sets it at 1.9 |
| A | (B − 1.15)–1.15 | step onto it, step off it | pitch −30° to −12° (0.81–1.06); from −18° up it stops against B |
| C | (2.75 − B)–1.15, and C ≤ B − 0.7 | reach the ledge, keep a tread | pitch +20° to +38° (0.98–1.15) |

- The height chart behind the stack is the ruler: a box whose top reaches the "1" band is a step, the
  "2" band is the big one.
- C aimed below +20° stops on B's front face at half size and falls between the boxes; aimed above
  +40° it sails over the ledge. Either way: re-grab (the box under your feet excepted) and re-place.

**Blocked bypasses and softlocks.**
- Three boxes side by side (1, 2, 3 high): the clamp. Two boxes: 1.0 + 2.0 + a 1.25 jump = 3.25.
- Jump-and-drop climbing: a box released in mid-air is projected to the farthest free point, which is
  the rug, not the player's feet.
- A box thrown onto the ledge could lie out of sight from the rug (a 0.3 box at the back).
  `PropLeash` returns any box at rest up there to its start pose and scale after 2 s, unless the
  player is on the ledge too.
- Variants that stand up (for example A against B's side, C on A) are staircases and are tolerated.

**Win and gadgets.** Exit on the ledge.
- `NestedSet`: red, yellow, teal.
- `PropLeash`: forbidden = centre above Y 3.9 with Z > 12; `Unless` the player is grounded above 3.5;
  `RestSpeed` 0.3; `Grace` 2 s; `Respawn`.

```csharp
yield return bot.WalkTo(new Vector3(0, 0, -2.6f));
yield return bot.Grab(red);
yield return bot.WalkTo(new Vector3(-2, 0, 6.7f));
yield return bot.DropAt(new Vector3(-2, 0.95f, 11.05f));   // B centre when seated
yield return bot.Wait(0.5f);
yield return bot.WalkTo(new Vector3(0, 0, -2.3f));
yield return bot.Grab(yellow);
yield return bot.WalkTo(new Vector3(-2, 0, 6.7f));
yield return bot.DropAt(new Vector3(-2, 0.5f, 9.4f));      // A centre
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

**Blurb.** "One box. Or is it? The shelf is four of you high."
- Hint 1: "Pick the box up. There is another one inside. And another."
- Hint 2: "No box will grow taller than the '2' on the height chart, and you can only hop up about one
  mark at a time."
- Hint 3: "Make the red box as big as it goes against the shelf. Put a one-mark box on the rug in front
  of it and another one-mark box on top of it at the back. Hop up: rug, box, box, box, shelf."

**Wow beats.**
- The un-nesting: the red box lifts away with no change in apparent size and a yellow one is simply
  *there*, same ribbon, same bow. The third time should get a laugh; give each box a rising note.
- The finished staircase seen from the shelf: three mismatched gift boxes, their long soft shadows
  lying across the height chart like a bar graph.

**Confidence.** Medium-high. Solid cubes on a flat floor; the one stacked contact is 5.4 on 37.


---

### Level 11 — Blocking Lasers

**Setting.** `pegboard-workbench`, `GroundY` 0. Inside a lidded shoebox diorama on the bench: a
corridor of building blocks in the shade of the lid, from which a mobile of red laser pointers rains
beams straight down across the far half of the floor. *Fantasy: hold up the ace of spades as an
umbrella the size of a roof and walk through the rain dry.*

**Reading of the brief.** The lasers shine downward, so the card's "shadow" is literal: the patch of
floor with no red dots on it. The card is laid across two low walls as a roof.

**Layout** (floor Y = 0, travel +Z). The corridor is 12 wide (the draft's 10 left ±3° of yaw).

| Element | X | Z | Y |
|---|---|---|---|
| Corridor floor | −6..6 | −2..23 | top 0 |
| Side walls at X = ±6, back wall at Z = −2 | | | height 8 |
| Far wall, with door opening X −1..1, Y 0..2.6 | −6..6 | 23..24 | height 8 |
| Exit tunnel | −1..1 | 24..27 | floor 0, ceiling 2.6 |
| Ceiling slab (the lid and laser rig; it is the sky cap) | −6..6 | −2..24 | underside 8 |
| Low block wall, left | −4..−3 | 13..23 | top 3.0 |
| Low block wall, right | 3..4 | 13..23 | top 3.0 |
| Laser rain footprint | −6..6 | 14..22 | from Y 8 down |
| Card table (spool, radius 0.6) | axis 0 | 3 | top 0.8 |

The lane between the low walls is 6 wide. The rain also covers the wall tops and the strips outside
them. Spawn (0, 0, −1), yaw 0, checkpoint at spawn. Exit box 2 × 2.6 × 2 centred (0, 1.3, 25.5).

**Props.**

| Prop | Start | Options |
|---|---|---|
| `PlayingCard` (ace of spades) | centre (0, 0.81, 3) flat on the table, long axis Z, scale 0.5 (0.7 × 1.0) | clamps 0.2–**7.0** (9.8 wide: it cannot wedge between the 12-wide walls), `GrabPose.Snap90` |

It starts flat and orientation follows camera yaw only, so it is already in roof orientation when
grabbed facing +Z. `F` stands it on end, which is never useful here.

**Intended solution.**
1. Walk to (0, 0, 1.9) and look down at the card: `g = √(1.1² + 0.74²) = 1.33`, `k = 0.377`. Grab.
2. Without moving, look up at the far wall above the door, at (0, 6.4, 23): pitch 12.9°. The flat card
   slides out along the ray, widening, until its far edge meets the far wall (half-length 1.0·s):
   `d·(cos p + k) = 21.1`, `d = 15.6`, `s = 0.377 × 15.6 = 5.9`. The card is 8.2 wide × 11.7 long,
   centre (0, 5.0, 17.1), spanning Z 11.2..23.
   - It never touches the low walls on the way: its far edge reaches Z = 13 at `d = 8.2`, where its
     plane is at Y 3.4, already above them.
3. Release. The card drops 2 units flat onto both walls and lies at Y 3.0..3.24 (mass 227). Every beam
   over the lane from Z 11.2 to 23 now ends on the card.
4. Walk round the table, down the lane under the card, through the door.

**Tolerances** (script sweep).
- **Scale 4.7** (wide enough to rest on both walls: 1.4·s ≥ 6.6) **to 7.0** (clamp).
- Where to stand, for the pickup above: `s = 0.279·(23 − Z)`, so anywhere from the back wall to
  **Z = 6.1**: eight units.
- Pitch **9° to 22°**. Below that the card meets the low walls' inner faces while it is still narrower
  than the lane and falls into the lane as a doormat. Above 22° it stops on the ceiling and ends
  short of the door.
- Yaw ±6° (the side walls start to limit it beyond that).
- **Pickup distance decides it.** Picked up from more than 2.2 away (`k` < 0.23) the best possible
  roof is too narrow; from the spawn (4.1 away) it is 2.7. This is where Level 8's lesson is applied:
  *it has to look big when you pick it up.*
- Recovery: the card is always visible and grabbable from the dry half. A card lying in the lane is
  re-grabbed from the edge of the rain at about 6 away, where `k ≈ 0.5`, which is plenty. A zapped
  player returns to the spawn; the card stays where it is.

**Blocked bypasses.**
- Carrying the card over your head: a held toy is not in the world, and the beams are drawn passing
  through it. The first zap teaches this in a second.
- Sprinting through: 8 units at 8 units/s is 1 s; the zap delay is 0.1 s. A partial roof leaves at
  most 0.8 of uncovered run.
- Grabbing the roof from underneath: the beams reach the player the same tick.
- Climbing the low walls: 3.0 high, and their tops are in the rain. A card leaning on a wall as a ramp
  only leads there.
- Squeezing between beams: the hazard test has no gap a capsule fits through.

**Win and gadgets.** Exit in the tunnel.
- `LaserRain`: emitter X −6..6, Z 14..22 at Y 8; `Range` 8; `LatticePitch` 0.45 (triangular, about
  550 beams); `ZapDelay` 0.1 s. `Coverage01` over the lane drives the hum and the all-clear chime.

```csharp
yield return bot.WalkTo(new Vector3(0, 0, 1.9f));
yield return bot.Grab(card);
yield return bot.DropAt(new Vector3(0, 6.4f, 23f));
yield return bot.Until(() => card.Center.y < 3.4f && card.Velocity.sqrMagnitude < 0.01f, 4f);
yield return bot.WalkTo(new Vector3(1.8f, 0, 3));          // round the table
yield return bot.WalkTo(new Vector3(0, 0, 12));
yield return bot.WalkTo(new Vector3(0, 0, 22.5f));
yield return bot.WalkTo(new Vector3(0, 0, 25.5f));
```

**Blurb.** "It's raining lasers. All you have is a playing card."
- Hint 1: "The beams come straight down. Anything solid above your head stops them — but not while
  it's in your hand."
- Hint 2: "Those two low walls could hold up a roof. The card would have to be wider than the gap
  between them."
- Hint 3: "Stand right over the card so it looks big, pick it up, then look at the far wall well above
  the little door and let go."

**Wow beats.**
- The release: an ace of spades the size of a garage door drops onto the walls with a slap, and
  hundreds of red dots on the floor wink out in a card-shaped rectangle. The dots that remain crawl along
  the card's edges. This level's "shadow" reveal is the dark rectangle.
- Walking underneath: the card is thin enough to glow. Every beam is a red pinpoint seen through the
  paper and the spade is a dark silhouette overhead.
- Held-card note: while held the card takes no laser dots and casts no shadow; beams are drawn
  straight through it. That is the visual explanation of the rule.

**Confidence.** High. A heavy flat slab falling two units onto two parallel walls is stable; the only
scripted part is the hazard test.

---

### Level 12 — The Keyhole

**Setting.** `high-shelf`, `GroundY` −9. The top of a toy chest of drawers. An open drawer is a pit of
socks between the landing and a dollhouse front door set in a sheer wall, and a brass toy key the
length of a bus lies across the drawer as a bridge. *Fantasy: the thing you are standing on is the
thing you need in your hand.*

**Why crossing first does not work.** Any bridge can be grabbed from the far side, so the far side has
**no floor**: the door is flush in a sheer wall and the key's tip rests in a slot under the sill that
is too low to enter. At the door the player is standing on the key and nothing else.

**Layout** (landing top Y = 0, travel +Z).

| Element | X | Z | Y |
|---|---|---|---|
| Landing | −6..6 | −6..8 | top 0 |
| Drawer pit | −6..6 | 8..20 | floor −9 (socks) |
| Door wall, sheer | −6..6 | 20..22 | −9..12 |
| Key slot in the wall | −1.1..1.1 | 20..21.2 | 0..0.4 |
| Door opening, closed by a slab flush at Z = 20 | −1..1 | 20..22 | 0.45..3.05 |
| Keyhole | 0 | 20 | 1.65 |
| Exit tunnel behind the door | −1..1 | 20..24 | floor 0.45 |
| Painted outline of the spool's side (10 wide × 9.1 tall) on the door wall | −5..5 | 20 | 0..9.1 |
| Key shelf on the back wall | −5..−3 | −6..−5.2 | top 1.3 |
| Side walls X = ±6, back wall Z = −6; `SkyCap` | | | to 12 |

Spawn (0, 0, −2), yaw 0, checkpoint at spawn. Exit box 2 × 2.6 × 2 centred (0, 1.75, 22.5), locked
until the door has opened. The key lies **level** on the landing (the draft's 2.4° tilt left its bow
floating); its chamfered rim is the walk-on edge.

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Key` | centre (0, 0.16, 13), level, long axis Z, **scale 8**: 16 long, spanning Z 5..21; bow 5.6 × 4.8 on the landing, blade 1.6 wide and 0.32 thick, tip 1.0 into the slot | clamps 0.05–8, tag `key`, `FrozenUntilGrabbed`, `GrabPose.Snap90` |
| `ThreadSpool` | centre (3, 0.5, 2), scale 1.0 | clamps 0.5–**9.9**, `KeepUpright`, `GrabPose.Upright` |

Masses: key 0.056·s³ (28.7 at 8); spool 0.38·s³ (289 at 9.1).

**Intended solution.**
1. *(Optional look.)* Walk the key to the door. The keyhole is at eye level and the key is underfoot.
2. **Shrink the key.** Stand at the key shelf, (−4, 0, −4.9), facing +Z. Eye to key centre:
   `g = √(4² + 1.39² + 17.9²) = 18.4`, `k = 8 / 18.4 = 0.435`. Grab. The bridge is gone.
3. Turn round and look down at the shelf top, 0.7 ahead and 0.25 below the eye: `d = 0.72`,
   `s = 0.435 × 0.72 = 0.31` (a key 0.63 long). Release; it lies on the shelf.
4. **Build the other way across.** Walk to (3, 0, 0.8), grab the spool: `g = √(1.2² + 1.05²) = 1.59`,
   `k = 0.627`.
5. Walk to (0, 0, 0.7). Aim at the wall *above* the door, about (0, 5.6, 20): pitch 11.9°. The spool
   stops when its side meets the wall (radius 0.55·s = 0.345·d): `d·(cos p + 0.345) = 19.3`,
   `d = 14.55`, `s = 0.627 × 14.55 = 9.1`.
   - Why aim up: the spool's base is at `1.55 − 0.108·d`. It must stay above the landing until its near
     side has passed the landing's edge, which needs pitch ≥ 10.7°.
6. Release. A spool 10 across and 9.1 tall drops 9 units onto the socks, upright. Its top is at
   Y +0.12, its near side at Z 9.9, its far side on the door wall.
7. Fetch the key: at the shelf, grab from 0.72 away: `k = 0.31 / 0.72 = 0.435`.
8. Jump the 1.9 gap onto the spool, walk to 1.0 from the door, look at the keyhole. The key points away
   from the player, so it stops when its tip meets the door: `d = 1.0 / (1 + k) = 0.70`,
   `s = 0.435 × 0.70 = 0.30`. Release: the keyhole takes it, it turns, the door slides up.
9. Hop up the sill (0.33) and exit.

**Tolerances** (script sweep).
- **Key:** the keyhole accepts 0.06–0.5. With `k` = 0.435 that is any standing distance from 0.4 to
  1.6 from the door. A key grabbed at bridge size from the landing's edge (`k` ≈ 1.1) still goes in
  from within 0.9 of the door. The shelf is a convenience, not a requirement.
- **Spool:** its top must be between 1.1 below the sill and 0.9 above the landing: **s from 8.35 to
  9.9** (clamp). For the pickup in step 4 that is a standing band of Z −0.9..2.4 with pitch 11°–23°.
- Spool too small (aimed low, or picked up from far): it stops on the landing or makes a short pillar
  deep in the pit. It is always in view; re-grab and step back — the Level 2 lesson, with an outline
  on the wall to fill.
- Spool clamped and short of the wall: stand closer and re-place; `KeepUpright` means it never ends up
  tumbled.
- Key rejected as too big: it clinks off the door and lands on the spool or the socks. Re-grab. A key
  on the socks is 9 or more away from the landing's edge, which makes it *smaller* at the door.
- Fell in: `HazardZone` returns the player to the spawn, still holding whatever was held.

**Blocked bypasses and softlocks.**
- Cross on the key and take it from the far side: there is no far side. Jump off the key and grab it
  in mid-air (legal): the player falls and respawns holding the key — the same as step 2. Posting it
  into the lock during that jump opens the door but leaves no bridge; the spool is still needed.
- Posting the key from the landing, 12 away: works if the key looks tiny enough (`k` ≤ 0.04). The door
  opens, and the spool is still needed to reach it. Tolerated.
- Spool placed while the key still bridges the pit: it stops against the key. Beside the 1.6-wide
  blade there is room for a pillar at most 5.2 across, whose top would be 4.3 below the landing.
- Spool on its side: 9.9 long against a 12 pit as a bridge; lying in the pit along Z it is a second
  way of filling the drawer and is tolerated.
- Standing on a too-short spool: the hazard zone covers the pit below Y −1.5, so a player who lands
  on a pillar lower than that is returned at once instead of being stranded.
- The seated key is locked, so it cannot be reused as a bridge.

**Win and gadgets.** Exit box, unlocked by `door.Opened`.
- `Socket` "keyhole": capture box 1.2 × 1.2 × 0.9 centred (0, 1.65, 19.6); tag `key`; scale 0.06–0.5;
  no yaw test; seat = blade in the hole, `SeatScale` → 0.15, `EaseSeconds` 0.25, then a 90° turn over
  0.4 s; `LockOnSeat`. On `Seated`: `door.Open()`.
- `Door`: slab 2 × 2.6 × 0.3, slides up 2.7 into the lintel over 0.8 s (ease-out).
- `HazardZone`: X −6..6, Z 8..20, Y −9..−1.5. Props that fall stay on the socks, in view from the
  landing's edge.

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
yield return bot.DropAt(new Vector3(0, 1.65f, 20f));
yield return bot.Until(() => door.IsOpen, 4f);
yield return bot.WalkTo(new Vector3(0, 0, 19.4f)); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(0, 0.45f, 22.5f));
```

**Blurb.** "The door is locked. You are standing on the key."
- Hint 1: "You can't pick up what's under your feet, and there is nowhere else to stand over there. The
  key has to stop being the bridge."
- Hint 2: "Take the key from far away and set it down somewhere close. Then find something else that
  could fill the drawer."
- Hint 3: "Shrink the key onto the little shelf. Pick the spool up from close, stand a few steps back
  from the edge, fill the outline above the door and let go. Carry the key across the spool and hold it
  up to the lock."

**Wow beats.**
- The grab: a bridge with a shadow twelve units long lifts off the drawer without changing size on
  screen, the shadow snaps off the socks, and it lands on the shelf as a trinket that rings like a
  dropped coin.
- The lock: the key leaves your hand, slides into the keyhole and turns by itself; tumblers light up
  one by one behind the brass plate and the door rises.

**Confidence.** High for the key (frozen until grabbed, then small). Medium for the spool's fall: a
289-mass cylinder landing flat from 9 units; `KeepUpright` removes the tipping risk.
**Fallback.** Give the pit a `Socket`: a spool released over the pit with scale 8.35–9.9 is eased onto
the pit's centre line and lowered to the floor.


---

### Level 13 — Escaping the Fishbowl

**Setting.** `cardboard-box`, `GroundY` 0. The player starts on the plastic castle inside a goldfish
bowl standing on the den floor. Water all round, glass all round, and a glass filter tower on the far
side that reaches the rim. *Fantasy: drink the whole bowl with a bath sponge, then wring it out under
your own feet and ride the flood over the rim.*

**Water is scripted, not simulated.** A `WaterVolume` is a footprint, a floor height and a number; its
surface is flat at `FloorY + Volume / Area`. Water moves between volumes and the sponge by
bookkeeping, and the total (405) is conserved. Displacement is ignored.

**Layout** (gravel floor Y = 0; the bowl's axis is the Y axis through the origin).

| Element | Geometry |
|---|---|
| Bowl glass | 24-sided prism, interior radius 10, Y 0..8 (rim at 8), 0.3 thick; static, solid, transparent |
| `SkyCap` | Y = 12 |
| Castle island (spawn) | X −2..2, Z −9.2..−6, top Y 3.0 |
| Castle stair, on the island's +X flank | X 2..3.5: Z −9.0..−7.8 top 2.0; Z −7.8..−6.6 top 1.0; then gravel |
| Filter tower (glass, 0.3 thick) | interior X −1.5..1.5, Z 6.7..10; side and front walls to Y 10.5; its back is the bowl glass (rim 8) |
| Tower doorway, in the front wall at Z 6.4..6.7 | X −1..1, Y 0..2.4, with a flap (`Door`) |
| Cork float, filling the tower floor | 2.8 × 0.4 × 3.0, top flush with the gravel at rest (tower floor recessed to −0.4) |
| Exit ledge outside the bowl (a stack of books) | X −3..3, Z 10.3..15, top Y 8 |

- Bowl water: `Area` 270, `Volume` 405 → surface at **Y 1.5**. The island (3.0) and the top stair (2.0)
  are dry. Tower water: `Area` 9, `Volume` 0, overflowing into the bowl at 7.9.
- Spawn (0, 3, −9), yaw 0, checkpoint at spawn. Exit box 3 × 2.5 × 3 centred (0, 9.25, 13).
- The player wades up to 0.8 deep; deeper for 0.3 s → swept back to the island.

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Sponge` | centre (0, 3.5, −6.9) on the island's front edge, **scale 2.0** (2.0 × 1.0 × 1.4), dry | clamps 0.5–12, tag `sponge`, `GrabPose.Snap90` |

`Capacity(s) = 0.315·s³` (90 % of its bulk). The draft's wet-mass rule is dropped: nothing in the level
ever pushes the sponge, so its weight is told by sound and drip, not by physics.

**Intended solution.**
1. Walk to (0, 3, −8.2), facing +Z: `g = √(1.3² + 1.05²) = 1.67`, `k = 2.0 / 1.67 = 1.20`. Grab — it
   fills most of the view.
2. Step to the island's front edge, (0, 3, −6.4). Look down into the bowl at about −16°. The sponge
   stops when its underside meets the gravel (half-height 0.25·s = 0.30·d):
   `d·(0.30 − sin p) = 4.55`, `d = 7.9`, `s = 1.20 × 7.9 = 9.5`. It is 9.5 × 4.7 × 6.6, centre
   (0, 2.4, 1.2), spanning Z −2.1..4.5.
3. Release. It drinks at 150 units/s up to its capacity, 0.315 × 9.47³ = 268. The bowl is left with
   137: surface at **Y 0.5**. About two seconds.
4. Go down the castle stair and wade round the sponge on the +X side to (6.5, 0, 5).
5. Grab the soaked sponge: distance 7.6, `k = 9.5 / 7.6 = 1.25`. It keeps its water while held.
6. Walk through the tower doorway onto the cork, to (0, 0, 7.6). Look down at the cork ahead (about
   −40°): `d ≈ 1.6`, `s ≈ 2.0`. Release.
7. Capacity is now 2.6 and it holds 268. It wrings itself out at 24 units/s into the tower; the flap
   shuts, the surface rises at 2.7 units/s, the cork rides it, and the player rides the cork: 8 units
   in 3 s. The surplus pours over the tower's lip back into the bowl.
8. At the top, walk off the cork over the rim onto the books. Exit.

**Tolerances** (script sweep).
- **Soak:** wading needs the bowl at or below 0.8: absorb ≥ 189, so **s ≥ 8.4**. From the island's edge
  that is any pitch from **−20° upward**; above −10° the sponge stops against the tower at s ≈ 10.9 and
  drains the bowl completely. Steeper gives a smaller sponge and a half-drained bowl: re-grab it from
  the island (it keeps what it holds) and place it farther out; a bigger sponge resumes drinking.
- Aiming from the middle of the island stops the sponge on the island at s ≈ 3. Step forward.
- **Wring:** the tower needs 71 units. Any release inside the tower at s ≤ 8 delivers that, and nothing
  bigger than about 2.3 fits in there.
- **Anything that leaves the player out of their depth** — wrung out in the bowl, or into the tower
  with nobody on the cork (the surplus refills the bowl) — sweeps the player to the island and
  **resets the water**: the sponge returns to its start pose dry at scale 2 and the bowl to 405, unless
  the sponge is in the player's hand. A clean retry, never a strand.

**Blocked bypasses and softlocks.**
- The sponge as a step over the rim: lying flat its top is at most 6.0 (the island is 3.0, so it
  cannot be mounted above 4.25); stood on edge it is taller but sheer. The rim is 8.
- Standing beside the cork in the tower: the cork fills the floor. Leaving the rising cork anywhere but
  the back: the tower's other three walls go to 10.5.
- **The water outrunning the cork** (draft: squeeze 40/s = 4.4 units/s of rise against a cork capped
  at 3, which put the rider 0.8 under within a second and swept them off): the squeeze rate is now 24/s.
- **A sponge behind glass.** The grab ray stops at glass, so a sponge outside the bowl, or in the tower
  while the player cannot wade to it, would be lost. `PropLeash` returns it (its water goes back to
  the bowl).
- The flap never closes on the player (crush guard); until it has closed, water wrung into the tower
  runs straight out into the bowl.

**Win and gadgets.** Exit on the books.
- `WaterVolume` "bowl": cylinder radius 10, `FloorY` 0, `Area` 270, `Volume` 405, `WadeDepth` 0.8,
  `SweepDelay` 0.3 s.
- `WaterVolume` "tower": box X −1.5..1.5, Z 6.7..10, `Area` 9, `Volume` 0, `MaxSurfaceY` 7.9 →
  `OverflowTo` bowl, `LeakTo` bowl at 3 /s (it is down again 24 s after a wasted wring). While the
  flap is open its maximum is 0.
- `Sponge`: `Volumes` (tower, bowl), `CapacityPerScale3` 0.315, `AbsorbRate` 150, `SqueezeRate` 24.
- `FloatPlatform` "cork": `RestY` 0, `MaxY` 8.0, `MaxSpeed` 3, `Freeboard` 0.1.
- `Door` "flap": closes when the sponge starts wringing in the tower, opens when the tower is empty.
- `PropLeash`: allowed = the bowl interior below Y 12, excluding the tower unless the player is in it;
  `Grace` 3 s; `Respawn` (and `sponge.EmptyInto(bowl)`).
- Level glue: on `PlayerSwept`, if the sponge is not held: respawn it dry, bowl = 405, tower = 0.

```csharp
yield return bot.WalkTo(new Vector3(0, 3, -8.2f));
yield return bot.Grab(sponge);
yield return bot.WalkTo(new Vector3(0, 3, -6.4f));
yield return bot.DropAt(new Vector3(0, 2.4f, 1.2f));          // centre when seated at s = 9.5
yield return bot.Until(() => bowl.Depth <= 0.8f, 6f);
yield return bot.WalkTo(new Vector3(2.75f, 3, -8.4f));        // onto the top stair
yield return bot.WalkTo(new Vector3(2.75f, 2, -7.2f));
yield return bot.WalkTo(new Vector3(2.75f, 0, -5.6f));
yield return bot.WalkTo(new Vector3(6.5f, 0, 0));
yield return bot.WalkTo(new Vector3(6.5f, 0, 5.0f));
yield return bot.Grab(sponge);
yield return bot.WalkTo(new Vector3(0, 0, 5.6f));
yield return bot.WalkTo(new Vector3(0, 0, 7.6f));
yield return bot.DropAt(new Vector3(0, 0.4f, 9.0f));
yield return bot.Until(() => cork.AtTop, 8f);
yield return bot.WalkTo(new Vector3(0, 8, 13));
```

**Blurb.** "Wind-up toys don't swim. Sponges do the opposite."
- Hint 1: "A sponge holds as much as its size. Make it big where the water is."
- Hint 2: "A full sponge that suddenly gets small has to put the water somewhere — wherever it happens
  to be standing."
- Hint 3: "From the castle's edge, drop the sponge into the middle of the bowl as big as you can. When
  the water is gone, carry the sponge into the glass tower, stand on the cork, and drop it at your
  feet."

**Wow beats.**
- The soak: a sponge the size of a house lands in the bowl, darkens from lemon to amber from the bottom
  up, and the water line slides down the glass and down the castle walls. A wind-up goldfish is left
  ticking on the gravel.
- The wring: inside the glass tower the sponge collapses to a brick, the tower fills like a syringe,
  and the player rises past the bowl's rim while the surplus arcs back into the bowl as a waterfall.
- For art: one flat mesh per volume with a hand-written transparent shader; on the low tier use a
  vertex-height tint, not a depth fade. The held sponge is exempt from water tint and caustics.
  Saturation is a shader parameter driven by `Saturation01`.

**Confidence.** Medium-high: the only rigid-body event is a large box dropping a short distance;
everything wet is bookkeeping. The risk is feel — the ride must not jitter, which is why the cork is
kinematic.
**Fallback.** Drop the ride. The tower becomes a dry moat (`Area` 40, floor −2) between the bowl floor
and a deck with the exit; a cork raft in it rises 2 when the moat is flooded and bridges it.

---

### Level 14 — The Infinite Hallway

**Setting.** `night-light`, `GroundY` 0. A dollhouse hallway papered with a pattern of little doors,
and one loose door frame standing in the middle of it. *Fantasy: a door is always the right size for
whoever walks through it — so walk through, and be that size.*

**How "a larger or smaller version of the same room" is built.** There is one room. Stepping through
the doorway **rescales the player** to the doorway's scale, about its threshold. Being 0.3 of your
size in this hall is exactly being in a hall 3.3 times bigger. `Player.SetScale` exists (capsule, eye,
speeds and probes scale by `P`; jump speed by √`P`, so the apex scales by `P`). No second room, no
streaming, and the puzzle is honest: what you saw through the frame is where you are.

**Layout** (floor Y = 0, travel +Z; halls A and B are dressed identically).

| Element | X | Z | Y |
|---|---|---|---|
| Hall A | −5..5 | 0..14 | floor 0 |
| Dividing wall, full height | −5..5 | 14..15 | 0..11 |
| Mouse-hole through it | −0.35..0.35 | 14..15 | 0..0.8 |
| Hall B | −5..5 | 15..25 | floor 0 |
| Terrace (sheer face at Z = 25) | −5..5 | 25..31 | top 3.0 |
| End wall, with exit opening X −0.8..0.8, Y 3.0..**6.4** | −5..5 | 31..32 | 0..11 |
| Exit tunnel | −0.8..0.8 | 32..35 | floor 3.0 |
| Side walls X = ±5, back wall Z = 0, ceiling (the sky cap) | | | 11 |
| `RecallPad` A (floor plate, radius 0.6) | −3.5 | 1.5 | 0 |
| `RecallPad` B | −3.5 | 16.5 | 0 |

Spawn (0, 0, 2), yaw 0. Exit box 1.6 × 3 × 2 centred (0, 4.5, 33.8), wholly inside the tunnel. No
hazards, no pits.

**Size gates** (with the measured apex 1.25·`P`).

| Gate | Needs | Because |
|---|---|---|
| Mouse-hole | `P` ≤ 0.45 | 1.7·`P` < 0.78 |
| Terrace | `P` ≥ 2.5 | apex 1.25·`P` ≥ 3.1 |
| Exit opening | `P` < 2.0 | 1.7·`P` < 3.4 |

No single size passes both the terrace and the exit, so three passes are needed: small, big, medium.

**Props.**

| Prop | Start | Options |
|---|---|---|
| `Doorway` | standing at (0, 0, 8) facing −Z, scale 1.0 | `PropBody.Fixed`, clamps 0.1–**3.2**, `AllowPitch = false`; its colliders never collide with the player |

Its centre is 1.25·s above its base, so held at distance `d` it reaches 1.25·k·d above and below the ray.

**Intended solution.**
1. **Small.** Walk to (0, 0, 3). Door centre (0, 1.25, 8): `g = 5.0`, `k = 0.20`. Grab. Look down at the
   floor just ahead (pitch −60°). The frame stops when its base meets the floor:
   `d·(1.25·k − sin p) = 1.55`, `d = 1.39`, `s = 0.28`. A door 0.7 tall stands at (0, 0, 3.7).
2. Walk into it. `P` becomes 0.3 (the player clamp). Eye height 0.47. The hall is a cathedral.
3. Turn, grab the door from 0.43 away: `k = 0.28 / 0.43 = 0.65`. Carry it through the mouse-hole to
   (0, 0, 16).
4. **Big.** Look *up* at 60° and let the door rise. Its base is at `0.47 + d·(sin p − 0.81)`: above 50°
   it never meets the floor before the clamp: `d = 3.2 / 0.65 = 4.95`, `s = 3.2`. Release; it settles
   0.76 straight down and stands at (0, 0, 18.5), 8.0 tall and 5.1 wide.
5. Walk through. `P` becomes 3.2 (5.4 tall, eye 4.96). The player comes out at about Z = 20.
6. Run and jump onto the terrace (apex 4.0 over a 3.0 face). Stop at (0, 3, 26.5).
7. **Medium.** Look back down at the door: `g = √(8.0² + 4.0²) = 8.9`, `k = 0.36`. Grab, turn to the
   exit, look down at the terrace floor (pitch −60°): `d·(1.25·k − sin p) = 4.96`, `d = 3.8`,
   `s = 1.35`. The door stands at (0, 3, 28.4).
8. Walk through (`P` = 1.35, 2.3 tall) and on into the exit.

**Tolerances** (script sweep).
- **Small:** needs s ≤ 0.45: any pitch below −26° when grabbed from 5 away. Grabbed from closer than
  2.3 it cannot be done in one pass (straight down gives 0.57 from 1.5 away): walk through anyway and
  do it again from the smaller size (0.57 → 0.18). *That is the recursion, and it always converges.*
- **Big:** needs s ≥ 2.5: any pitch from 50° up reaches the 3.2 clamp, provided the mouse-sized pickup
  was from at least 0.36 away (`k` ≤ 0.78). Aimed lower, or picked up closer, the floor limits it
  (40° → 1.8); walk through that and repeat from the new eye height. Two passes at worst.
- **Medium:** needs s < 2.0: from the terrace any aim at the terrace floor or the end wall works — the
  floor gives 1.2–1.7 and the end wall at most 1.74. (The opening was 3.0 tall in the draft, which made 1.74 a failure.)
- **Blocked exits never hurt:** if the far side has no room for the new size the door returns the
  player out of the side they came in; if neither side has room it does nothing and shows "no".
- **Lost door:** standing on a `RecallPad` for 0.5 s brings the doorway to the pad at the player's
  current scale. There is one in each hall, because the door can be projected through the mouse-hole
  into the other hall while the player is too big to follow (the draft had only pad B).

**Blocked bypasses.**
- The mouse-hole at full size: 0.8 high. The terrace without growing: the frame is not solid to the
  player and cannot be laid flat. The exit as a giant: a 5.4-tall player and a 3.4-tall opening; a
  giant standing at the end wall cannot touch the exit box, which starts 0.8 inside the tunnel.
- Staying tiny: cannot climb. Staying giant: cannot exit. A door set inside the tunnel mouth: a giant's
  axis cannot reach its plane.
- No softlock: the door is in view or recallable from every floor the player can stand on (from the
  terrace, the lower hall is in view over the edge).

**Win and gadgets.** Exit in the tunnel.
- `PortalDoorway`: player clamp 0.3–3.2, `Cooldown` 0.5 s, `SettleSpeed` 30.
- `RecallPad` × 2 as placed.

```csharp
yield return bot.WalkTo(new Vector3(0, 0, 3), 0.1f);
yield return bot.Grab(door);
yield return bot.DropAt(new Vector3(0, 0.35f, 3.69f));        // 60 degrees down
yield return bot.Until(() => portal.Settled, 2f);
yield return bot.WalkTo(new Vector3(0, 0, 4.15f), 0.05f);     // through it; stop 0.45 beyond
yield return bot.Until(() => bot.Player.Scale < 0.45f, 2f);
yield return bot.Grab(door);
yield return bot.WalkTo(new Vector3(0, 0, 13.5f), 0.1f, 15f, true);
yield return bot.WalkTo(new Vector3(0, 0, 16.0f), 0.1f, 15f);
yield return bot.DropAt(new Vector3(0, 4.76f, 18.48f));       // 60 degrees up
yield return bot.Until(() => portal.Settled, 3f);
yield return bot.WalkTo(new Vector3(0, 0, 19.0f), 0.1f, 15f);
yield return bot.Until(() => bot.Player.Scale > 2.5f, 2f);
yield return bot.WalkTo(new Vector3(0, 0, 21.0f), 0.5f); yield return bot.Jump();
yield return bot.WalkTo(new Vector3(0, 3, 26.5f), 0.5f);
yield return bot.Grab(door);
yield return bot.DropAt(new Vector3(0, 4.69f, 28.39f));       // 60 degrees down, facing +Z
yield return bot.Until(() => portal.Settled, 2f);
yield return bot.WalkTo(new Vector3(0, 3, 29.5f));
yield return bot.Until(() => bot.Player.Scale < 2.0f, 2f);
yield return bot.WalkTo(new Vector3(0, 3, 33.5f));
```

**Blurb.** "This door fits everyone. That's the problem, and the answer."
- Hint 1: "Walk through the door and you come out the size the door is. Look through it first."
- Hint 2: "A door the size of the mouse-hole makes you the size of a mouse. If one trip isn't enough,
  pick the door up again and take another."
- Hint 3: "Grab the door from across the hall and drop it at your feet; walk in. Carry it through the
  mouse-hole, look almost straight up and let it grow; walk in and jump the ledge. From up there, grab
  it again, drop it at your feet, walk in, leave."

**Wow beats.**
- The first look down: a door no taller than your shin, and inside it — live, in perspective — the hall
  you are standing in, vast, its night-light like a sun. Stepping in has no cut: the frame swallows the
  screen and the room is simply that big.
- Tiny to giant: the door climbs out of your hand like a monolith until it hits its limit, you walk
  under an eight-unit lintel, and the hall drops away to dollhouse scale. The mouse-hole you crawled
  through is a dot beside your shoe. The player's own shadow is the size cue here.
- **Portal rendering.** High tier: one extra camera into a half-resolution render texture at
  `doorBase + (eye − doorBase) × newScale / P`, drawn on the opening with a screen-space-UV shader;
  active only when settled and within 25·s of the eye; its material lives under `Resources`. Low tier:
  no second camera — the opening shows a flat card with a toy-figure silhouette at the size you will
  be. While held, the opening is an opaque glowing card on both tiers: no depth cue, no extra camera.

**Confidence.** Medium. Risks: rescaling the player next to geometry (the capsule test and the
turn-around), controller tuning at `P` = 0.3 and 3.2, the bot's tolerances at small scale.
**Fallback.** Quantize: when the doorway settles its scale snaps to the nearest of 0.3 / 1.0 / 3.2 and
the frame changes colour (with 1.0 the exit opening stays as specified). Three well-tested player
sizes instead of a continuum.


---

### Level 15 — The Rube Goldberg Machine

**Setting.** `night-light`, `GroundY` 0. The strip of floorboards along the skirting board, at night,
seen from the edge of a quilt. Somebody built a chain-reaction machine down there and left it
unfinished: four painted outlines are empty. *Fantasy: finish the machine with four toys that are all
the wrong size, stomp one pedal, and watch ten seconds of cause and effect end the game.*

**How the level works.** Four toys, four stations, one pedal. Each station seats its toy (`Socket`) and
each link of the chain applies one honest rule of scale. The pedal can be pressed at any time: the
motion runs as far as it can, stops visibly at the first wrong link, and the machine resets itself
with every toy where the player put it. **Running the machine is the diagnostic.** Every exotic motion
is a scripted, closed-form path; the rules that decide pass or fail are real formulas of scale.

**The chain.** pedal → gate → **ball** rolls down the chute, bounces off a drum over a wall of books,
lands on the **ruler** → the ruler flips a marble up through a hoop → the marble closes a switch → the
**fan** blows down a duct → the puff spins the sail on the first **domino** → seven dominoes fall, each
bigger than the last → the last swats a baseball off a shelf → the baseball drops through the neck of
a bell jar onto an alarm clock.

**Layout** (trench floor Y = 0; the machine runs along +X at the foot of the back wall, centre line
Z = 18.25).

| Element | X | Z | Y |
|---|---|---|---|
| Gallery (quilt edge, start), sheer face at Z = 11.5 | −28..28 | −6..11.5 | top 3 |
| Ramp notch in the gallery, 26.6° | 6..9 | 5.5..11.5 | 3 → 0 |
| Trench floor (walkway Z 11.5..16.5, machine band Z 16.5..20) | −28..28 | 11.5..20 | top 0 |
| Walls at X = ±28, Z = −6, Z = 20; `SkyCap` | | | to 16 |
| Door in the +X wall, tunnel X 28..32 | 28 | 12.5..15.5 | 0..3 |
| Pedal (disc, radius 0.7) on the gallery | 2 | 9.5 | 3 |
| Book pedestal for the dominoes (2.2 × 0.6) | 14 | 6 | 3..4.1 |
| Spool pedestal for the ball (radius 0.4) | −18 | 6 | 3..4.1 |

Machine band, left to right:

| Element | Geometry |
|---|---|
| Slide tower | X −21..−15, Z 16.5..20, body to Y 6.0. Hopper `Funnel` on top: axis (−18, 18.25), throat radius 1.6 at Y 6.0, mouth radius **1.75** at Y 6.4 (the draft's 2.0 at Z 18.3 overlapped the back wall). Reject door at its foot facing the walkway |
| Chute (two rails 1.0 apart) | from (X −18, Y 6.0) down to the lip at (−14.5, 3.55): 35°, length 4.27 |
| Drum | cylinder radius 1.8, axis at X = −12.45, head at Y 1.2 |
| Wall of books | X −9.7..−8.7, top Y 2.4 |
| Fulcrum (fat marker lying along Z) | axis at X = −5.0, Z 17.25..19.25, radius 0.4, top Y 0.8 |
| Hoop (horizontal ring, radius 0.9) | centre (−2.9, 6.0, 18.25), on an arm from the control tower |
| Control tower | X −1.5..3.5, to Y 7.0, gabled roof (nothing rests on it). Garage niche in its front face: X 0.5..2.0, Y 0..1.6, Z 16.5..17.9 |
| Duct (bendy straw, static, radius 0.4) | from the back of the garage up to a nozzle with its tip at (8.5, 5.6, 18.25), pointing +X |
| Plinth | X 8..21, Z 16.5..20, top Y 5. **Lane** = X 8.5..20 on top. End stop: block X 20..20.4, Y 5..5.6. Striped wind zone: X 8.5..13.5 |
| Shelf plank on wall brackets | X 21.2..24.5, Z 17.25..19.25, top Y 6.8, 0.2 thick |
| Baseball | centre (22.0, 7.4, 18.25), in a ring of putty |
| Bell jar over the alarm clock | axis at X = 26.2. Body radius 1.7, Y 0..5.2; neck (`Funnel`): throat radius 0.7 at Y 5.2 flaring to a mouth of radius 1.3 at Y 6.0. Clock inside, bell button at Y 2.8 |

- Spawn (0, 3, 0), yaw 0, checkpoint at spawn. Exit box 3 × 3 × 3 centred (30, 1.5, 14), locked until
  the clock is crushed. No pits; the trench is reached by the ramp.
- From the pedal the whole machine is in view: the trench floor at Z 18.25 is 26° below the eye and the
  gallery edge 32°.
- Each station has a painted outline of its toy in the toy's own Candy colour, drawn at the middle of
  its working window (`FitGauge` tints it while the toy is held near).

**Props.**

| Prop | Start | Options |
|---|---|---|
| `BouncyBall` | centre (−18, 4.4, 6) on the spool, **scale 0.6** | clamps 0.6–**3.0** (3.0 just fits the hopper throat), tag `ball`, `FrozenUntilGrabbed` |
| `DeskFan` | base centre (−22.6, 0, 13.6), guard facing −Z, **scale 5** (5 wide, 6 tall, 3.5 deep) | clamps 0.5–**5** (never bigger than it starts), tag `fan`, `GrabPose.Upright` |
| `CatapultRuler` | centre (−9, 0.04, 14) flat on the trench floor, long axis along X, **cap toward −X**, scale 1.0 | clamps 0.6–**1.2** (4.8 long: shorter than the 5.0 from gallery to plinth, so never a bridge), tag `ruler`, `GrabPose.Snap90` |
| `DominoSet` | centre (14, 4.55, 6) on the book, long axis along X, small end toward −X, **scale 0.6** | clamps 0.5–**3.5** (11.2 long on an 11.5 lane), tag `dominoes`, `GrabPose.Upright` |
| `Baseball` | on the shelf | kinematic until struck, not grabbable |

The domino set at scale 1, X measured from the strip's small end (each domino is `h × 0.5h × 0.15h`,
1.3 times the one before; the gap to the next is 0.55 × its height):

| # | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|
| Height | 0.30 | 0.39 | 0.51 | 0.66 | 0.86 | 1.11 | 1.45 |
| Near face at X | 0.15 | 0.36 | 0.63 | 0.99 | 1.45 | 2.05 | 2.83 |

Domino 1 carries a paper sail (visual). Domino 7's far face is at X = 3.05 and its hinge is the bottom
edge of that face. The dominoes are spring-latched: nothing but the sail lets them go.

**Intended solution.** Any order works. The bot does dominoes, ball, ruler, fan.

1. **Dominoes: grow, sized by a backstop.** Stand at (14, 3, 4.3): `g = 1.70`, `k = 0.6 / 1.70 = 0.353`.
   Grab; walk round the book to the gallery edge at (14.25, 3, 11.1). The back wall is `D = 8.9` away.
2. Aim at the wall above the lane, at (14.25, 8.9, 20): pitch +26°. The set stops when its far side
   meets the wall (half-depth 0.4·S): `d·(cos 26° + 0.141) = 8.9`, `d = 8.55`, **S = 3.02**. The
   strip is 9.6 long and domino 7 is 4.4 tall; its underside is at 6.0, one unit above the lane, and
   it cleared the plinth's front edge by 0.45.
3. Release: the `lane` socket takes it and slides it until the big end meets the end stop. Strip
   X 10.3..20; domino 1 at X = 20 − 3.05 × 3.02 = 10.8, inside the wind zone; domino 7's hinge at
   (19.53, 5.15), 3.34 from the baseball, with 4.37 of domino to reach it.
4. **Ball: grow, capped by the clamp.** Walk to (−18, 3, 4.5): `g = 1.51`, `k = 0.398`. Grab; walk round
   the spool to the edge at (−18, 3, 11.1).
5. Aim at the wall above the hopper, at (−18, 10.8, 20): pitch +35°. The wall would allow 3.5; the
   clamp stops it at **3.0**, at `d = 7.54`: centre (−18, 8.9, 17.3), 1.0 from the hopper's axis,
   underside at 7.4, above the rim.
6. Release: the `hopper` socket seats it behind the gate. Mass 2.094 × 27 = **56.5**.
7. Walk to the ramp and down into the trench.
8. **Ruler: same size, right way round.** Stand at (−5.5, 0, 14) facing −X: `g = √(3.5² + 1.51²) = 3.81`,
   `k = 0.262`. Grab. It lies along the view with the cap at the far end.
9. Step to (−5, 0, 14.4) and face the fulcrum. The ruler now points at the wall, cap first. Press `Q`
   six times (90° to the right): cap toward +X, toward the hoop. Aim at the top of the marker,
   (−5, 0.88, 18.25): the ruler stops with its underside on the marker at `d = 4.1`, **s = 1.08**.
10. Release: the `fulcrum` socket slides it so that a third of its length is on the short side of the
    marker and the cap end rests on the floor. Short arm 1.44, long arm 2.88; short tip at Y 1.2.
11. **Fan: shrink.** Walk to (1.25, 0, 13) and look west along the trench:
    `g = √(23.85² + 1.45² + 0.6²) = 23.9`, `k = 5 / 23.9 = 0.209`. Grab.
12. Turn to the garage and aim at its back, low, at (1.25, 0.6, 17.2): pitch −12.7°. The fan stops when
    its base meets the floor (half-height 0.6·s): `d·(sin 12.7° + 0.125) = 1.55`, `d = 4.47`,
    **s = 0.94**: 0.94 wide and 1.12 tall in a garage 1.5 wide and 1.6 high. The `collar` socket seats it.
13. Walk back up the ramp to the pedal at (2, 3, 9.5) and step on it. The run, with these sizes:

| t (s) | Event | Rule and numbers |
|---|---|---|
| 0.0 | Gate lifts | 0.3 s |
| 0.3–1.3 | Ball rolls down the chute | `a = (5/7)·22·sin 35° = 9.01`; 4.27 long: 0.97 s, leaves at 8.77, velocity (7.19, −5.03) |
| 1.3–1.6 | Falls 2.35 to the drum | 0.29 s; lands at X −12.44 with vy −11.3 |
| 1.6–2.5 | Bounces (restitution 0.88: vy +9.98), clears the books by 1.0 | 0.92 s; lands on the short arm at X −5.85 |
| 2.5–2.8 | Ruler swings | `m` = 0.75 + 0.06 = 0.81; `f = (56.5 − 2·0.81) / (56.5 + 4·0.81) = 0.92`; 0.34 s |
| 2.8–3.7 | Marble flies straight up from the long tip (Y 2.4) | apex = 2.4 + 4.8·`f` = **6.81**; the hoop needs 6.3 |
| 3.7–4.3 | Marble runs down the tube and closes the switch | |
| 4.3–5.2 | Fan spins up, the sail spins, the latch lets go | strength = (0.94 / 0.6)² = **2.4**; needs 1 |
| 5.2–8.1 | Dominoes 1–6 fall | 0.33, 0.38, 0.43, 0.49, 0.56, 0.64 s: each beat slower |
| 8.1–8.8 | Domino 7 falls 37° and meets the baseball | |
| 8.8–10.0 | Baseball rolls 2.5 along the shelf at 4, drops through the jar's neck onto the clock | arrives at 13.3; mass 9.05 ≥ 8 |

14. The clock is flattened, the alarm rings, the door opens. Walk down the ramp, along the trench to
    (27, 0, 14) and out.

**Tolerances.**

| Toy | Start | Intended | Station seats it at | Chain works at | What sets the window |
|---|---|---|---|---|---|
| Dominoes | 0.6 | 3.02 | 1.0–3.5 | **2.2–3.5** | Low: domino 1 must stand in the wind zone, 20 − 3.05·S ≤ 13.5 (domino 7 already reaches the baseball from S = 1.95). High: the clamp |
| Ball | 0.6 | 3.0 | 1.2–3.0 | **2.25–3.0** | Low: the marble must clear the hoop, `f` ≥ 0.81, M ≥ 22.7. High: the clamp |
| Ruler | 1.0 | 1.08 | 0.8–1.2 | **0.8–1.2** | Low: the short arm must reach under the ball's landing point. High: the clamp |
| Fan | 5 | 0.94 | 0.5–1.2 | **0.6–1.2** | Low: strength (s / 0.6)² ≥ 1. High: the garage |

Three of the four upper limits are clamps, so "too far back" is harmless three times out of four. In
terms of what the player does (script sweep):

- **Dominoes.** Sized by the wall: pick up from 1.4 to 2.4 away and aim 23°–45° up. Aimed lower, the
  set stops on the plinth's face and falls into the trench at toy size; in view from the edge, re-grab.
- **Ball.** Pick up from 1.4 to 2.5 away; pitch 31°–42° (lower clips the funnel's front rim). Closer, the clamped ball hangs short of the
  funnel and falls into the trench: re-grab it from the gallery from 7 or more away and it reaches the
  funnel at the clamp. Farther, it seats but is too light; the run shows the marble dying just under
  the hoop (at s = 2.0 the apex is 6.03).
- **Ruler.** Put it down from about the distance it was picked up at (0.8 to 1.2 times). Yaw within 30°
  of "cap toward the hoop": six clicks of `Q` give or take two, or none if the player walks round and
  drops it while facing +X.
- **Fan.** Drop distance between 0.12 and 0.24 of the pickup distance: grabbed from the garage, stand
  2.5 to 5 from the niche and aim −15° to −8°.

Recovery, never a restart:

- A seated toy is still grabbable while the machine is idle; grabbing it unseats it.
- Too small to seat: the ball drops between the gate bars and rolls out of the reject door; the others
  lie on their outline, which blinks "too small". Too big: the fan stops in front of the garage.
- A fizzled run resets in 1.5 s: gate down, ball lifted back into the hopper at the size the player
  gave it, ruler level, marble back in its cap, fan off, dominoes upright. The HUD names the link:

| Run stops at | What the player sees | Message |
|---|---|---|
| Gate | It lifts on nothing | "The gate opens. Nothing rolls." |
| Ruler missing | The ball lands on the marker and rolls off | "The ball lands on a bare pivot." |
| Ball too light | The marble peaks under the hoop and falls back | "The marble falls short. The ball isn't heavy enough." |
| Fan missing | The switch lamp lights, nothing spins | "Click. Nothing is plugged in." |
| Fan too small | It spins; the sail shivers | "The little fan wheezes. The sail doesn't turn." |
| Domino 1 outside the wind zone, or no dominoes | Streamers blow down an empty lane | "The wind has nothing to push." |
| Domino 7 too short | It slams down in front of the shelf | "The last domino falls short of the shelf." |

**Blocked bypasses and softlocks.**
- Skipping the slide by dropping the ball straight onto the short arm: it fires the catapult as the
  boulder did in Level 7. Tolerated; the rest of the chain still needs the other three toys.
- Another toy as the weight: it would be missing from its own station, and none is heavy enough
  (dominoes 18.7 at the clamp, fan 13.2; the hoop needs 22.7). The player on the short arm weighs 3.
- Something else down the hoop: the tube under the net is 0.55 across with an S-bend; only the marble
  (at most 0.36) passes.
- Running the fan somewhere else to blow the dominoes over: the only switched outlet is in the garage.
- Pushing the dominoes by hand: they are latched. Reaching the plinth or the shelf (possible with the
  fan or the domino set as a step) gains nothing: the baseball is kinematic until domino 7 strikes it.
  A player who drops into the bell jar is returned to the checkpoint.
- Crushing the clock with something else: the jar's neck passes nothing wider than 1.4; a ball of that
  size weighs 5.6 and the clock needs 8 at 6 units/s. The player weighs 3.
- The ruler as a bridge from the gallery to the plinth: 4.8 against 5.0. Dominoes the wrong way round:
  rejected as `Backwards`.
- Standing in the machine during a run: driven parts ignore the capsule (`GhostToPlayer`). Pressing the
  pedal during a run or a reset: ignored.
- Softlock audit: no pits; every surface a toy can rest on is in view from the gallery or the trench,
  or is a no-parking zone; the fan can never exceed its start size; the gallery is left by jumping down
  and regained by the ramp.

**Win and gadgets.** Exit box, unlocked by the clock's `Broke`.

| Socket | Tag | Capture volume | Scale | Yaw test | Seat (eased 0.25 s; the lane 0.4 s) |
|---|---|---|---|---|---|
| `hopper` | `ball` | cylinder radius 1.6 about (−18, ·, 18.25), Y 6..11 | 1.2–3.0 | — | centre (−18, 6.0 + 0.5·s, 18.25), behind the gate. Below 1.2: `ReturnPort.Eject` to (−16.5, 0.5·s, 15.5) |
| `fulcrum` | `ruler` | box 7 × 2.5 × 2.5 centred (−4.5, 1.25, 18.25) | 0.8–1.2 | cap end within 30° of +X | a third of the way along from the plain end on the marker's top line, long axis along X, cap end on the floor |
| `collar` | `fan` | the garage interior | 0.5–1.2 | — | base centre (1.25, 0, 17.2), guard facing the duct |
| `lane` | `dominoes` | box X 8.5..20, Z 16.5..20, Y 5..9 | 1.0–3.5 | small end within 20° of −X | big end of the strip at X = 20, centred on Z = 18.25 |

None is `LockOnSeat`; the director locks the toys during a run. The other gadgets, in construction
order:

- `PressurePlate` "pedal": `AcceptPlayer`, `MinMass` 2, no latch.
- `MachineDirector`: `Start` = pedal, `FizzleTimeout` 2.5 s, `ResetSeconds` 1.5, messages as tabled.
- `PathDrive` "ball run" (gate lift 0.3 s, then the seated ball's **lowest point** follows):
  `Roll` along the chute, 4.27 at 9.01; `Ballistic` from the lip (−14.5, 3.55) with (7.19, −5.03) down
  to Y 1.2; `Ballistic` from (−12.44, 1.2) with (7.19, +9.98) until it meets the seated ruler (or the
  bare marker). `Offset` = one radius; `EndVelocity` (0, −4, 0). Then the ball is an ordinary prop and
  the `Seesaw` weighs it. `Reset`: gate down; a ball that left the hopper is lifted back over 1.5 s.
- `Seesaw`, built when `fulcrum` seats a ruler: pivot (−5, 0.8, 18.25), axis Z, cap arm 2.667·s_r,
  strike arm 1.333·s_r (`r` = 2), rider load `m` = 0.75 + 0.0565·s_r³ (the marble). With a fixed pivot
  height both tips travel the same heights at any ruler scale: short tip 1.2 → 0, cap 0 → 2.4.
  `Projectile`: the ruler's marble child is hidden and a kinematic sphere leaves the cap straight up at
  `2·√(2·22·f·1.2) = 14.53·√f`: apex 2.4 + 4.8·`f`.
- `PathDrive` "hoop": if the marble's apex ≥ 6.3 and it is within 0.75 of the ring's axis, it is eased
  onto the axis as it falls through Y 6.0 and follows the tube to the switch in 0.6 s → `SwitchClosed`.
  Otherwise it finishes its arc → `MarbleMissed(apex)`.
- `WindStream` "jet": box X 8.5..13.5, Z 17.25..19.25, Y 5..7. On `SwitchClosed`: no fan seated →
  `FanMissing`; otherwise spin up 0.5 s, then `Strength = (fan.Scale / 0.6)²`. The reach belongs to the
  nozzle, not to the fan.
- `HingeChain` on the domino set: starts 0.4 s after `Strength ≥ 1` if domino 1 is inside the jet box;
  otherwise `SailStalled` or `NothingToPush`. `Target` = the baseball: the last domino (hinge at
  `(20 − 0.154·S, 5 + 0.05·S)`) reaches it when `1.448·S + 0.5 ≥` the distance from hinge to ball.
- `PathDrive` "baseball", on `TargetStruck`: `Roll` 2.5 along the shelf at 4; `Ballistic` from
  (24.5, 7.4); `Ease` onto the jar's axis through the neck; free fall to the bell button; handed back
  to physics at 13.3 downward.
- `Breakable` "alarm clock": bounds = a 1.0 box over the bell button; `MinMass` 8; `MinSpeed` 6;
  `Direction` −Y; `SwapCollider` (flattened, 1.0 high); `Watch(baseball)`. On `Broke`: the exit `Door`
  opens, `exit.Unlock()`, the director is `Done`.
- `ReturnPort` (the reject door), `HazardZone` (inside the bell jar), `FitGauge` × 4 (the outlines).
- `PropLeash` (no parking): forbidden = the shelf top, the two tower tops outside the hopper, and the
  lane for props without the tag `dominoes`; `RestSpeed` 0.2; `Grace` 2 s; `Respawn`.

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
yield return bot.Until(() => clock.Broken, 20f);
// 7. out
yield return bot.WalkTo(new Vector3(4, 3, 4.5f));
yield return bot.WalkTo(new Vector3(7.5f, 3, 5.0f));
yield return bot.WalkTo(new Vector3(7.5f, 0, 12.5f));
yield return bot.WalkTo(new Vector3(27, 0, 14), 0.3f, 12f);
yield return bot.WalkTo(new Vector3(30, 0, 14));
```

Tests to write beside the solver:
- Link events in order: `BallLanded`, `MarbleLaunched` with apex ≥ 6.3, `SwitchClosed`, `WindChanged`,
  `PieceFell(1..6)`, `TargetStruck`, `Broke`.
- Seat the ball at 2.0, press the pedal, expect `MarbleMissed` and `MachineReset`; then re-size the ball
  and solve. This is the "never softlocked" proof.
- Press the pedal on the empty machine: gate, fizzle, clean reset.

**Blurb.** "Four toys, four outlines, one pedal. Nothing here is the right size."
- Hint 1: "Put a toy on every painted outline, then stomp the pedal and watch where the motion stops.
  That link is the one to fix; the machine sets itself up again."
- Hint 2: "The ball must be heavy enough to flip the marble up through the hoop. The fan must fit in
  its garage. The smallest domino must stand in the striped wind zone while the biggest reaches the
  shelf. The cap on the ruler points at the hoop."
- Hint 3: "From the quilt's edge: pick up the dominoes from two steps away and press them against the
  wall above the lane; do the same with the ball above the funnel. Downstairs: lay the ruler on the
  marker, turning it with Q until the cap is under the hoop. Stand in front of the garage, grab the
  big fan from there and drop it inside. Pedal."

**Wow beats.**
- **The fan.** A turbine the size of a house lifts off the floor with no change in size on screen,
  loses its long shadow, and is parked in a garage like a toy car. The trench behind where it stood is
  suddenly open and moonlit.
- **The run.** One unbroken ten-second take that gets bigger and slower as it goes: the ball sailing
  over the books, the marble's vertical hop through the net, the click, the whirr of a fan the size of
  a thimble, then seven dominoes falling in a ritardando, each beat lower and heavier than the last,
  the seventh the size of a door. The baseball rolls, drops through the glass neck, and the alarm clock
  bursts into springs and cogs. The camera stays first-person: the player chose where to watch from.
- **The ending.** The bell rings and the night preset turns to dawn; every seated toy, which has been
  glowing as a night-light, hands its light back as the door swings open.
- Held toys: the held fan's blades do not turn and no streamers come off it; the held domino set's sail
  does not spin, whatever it is carried through. The marble, tube and switch lamp must read at 25 units
  from the pedal: emissive trail, travelling light.

**Confidence.** Medium, and the doubt is integration rather than any single rule: eleven gadget types
in one level, each with a reset path that must leave no state behind. With `AirCapture` no toy has to
land, and every motion in a run is closed-form.
**Fallback A (timeline).** Replace the ball-run → seesaw → hoop hand-offs by one authored timeline: the
pass or fail of each link is computed up front from the seated scales with the same formulas
(`f` ≥ 0.81, strength ≥ 1, wind zone, reach) and every part plays a fixed animation up to the first
failing link. Same puzzle, same messages, no physics inside a run.
**Fallback B (three toys).** If the level runs long, ship the ruler already seated and not grabbable:
grow (dominoes), grow to a weight (ball), shrink (fan); about a minute shorter.


---

## Appendix A — Design corrections

Every change made to the drafts, and why. "Script" means the Python model of the hold march described
at the top of this document.

### A.1 Across the campaign

| # | Change | Why |
|---|---|---|
| G1 | Phase 1 coordinates re-checked against the Unity convention; none flipped | The Phase 1 draft was written after the engine switch and already used left-handed, Y-up, yaw 0 = +Z. The check was still made axis by axis (spawn yaw, travel direction, which side "right" is) |
| G2 | `ExitDoor` gadget removed; every exit is `ctx.AddExit(centre, size)` with explicit boxes | The engine has an `Exit`; a second mechanism would diverge |
| G3 | `SnapUprightOnGrab` → `PropOptions.GrabPose { Keep, Snap90, Upright }` | "Nearest 90°" leaves a toppled domino lying flat; toys with a real up need `Upright` |
| G4 | 39 gadget names merged into 26 (table below) | Three phases had re-invented sockets, plates, doors, leashes, impact tests and scripted paths |
| G5 | Three engine requests dropped: per-prop grab/drop callbacks, respawn keeping scale and held toy, `ExtraMass`. One replaced: rotating child colliders → `HingeChain` owns kinematic stand-ins | Read from the source: gadgets tick after the grabber, so polling `prop.Held` is same-tick; `Player.Respawn` is a `Teleport` that touches neither scale nor grabber |
| G6 | `GroundY` and `KillY` per level; kill-plane levels (5, 9) get a visible floor 2 below the plane | `ART_BIBLE.md` §6.2 places every preset's floor, bench or shelf at y = 0, which would have closed six levels' pits |
| G7 | Environment keys are the art bible's six presets with its level mapping; the drafts' nine ad-hoc keys became local set dressing; Levels 8 and 9 are no longer night levels | One source of truth for presets; night is reserved for 14 and 15 |
| G8 | Painted outlines as the size language (Levels 2, 4, 7, 12, 15) | The held toy keeps its footprint, so a size target is only readable against something at the depth where the toy stops |
| G9 | Crush guard for every kinematic gadget part; riders rule (2.0) | Seesaw returns, doors and flaps could pin the capsule (the engine protects the player from props, not from movers); a mover that changes speed by more than 6 throws its rider |
| G10 | Limits use the measured jump (apex 1.25, range 5.47), not the nominal 1.3 / 5.5 | Level 14's terrace gate was drafted at `P` ≥ 2.4, which gives an apex of exactly 3.0 against a 3.0 face |
| G11 | Thin-obstacle rule (0.2, item 4); pitch bands quoted with grazing passes excluded | The march steps 8 % at a time; the engine now refines it near geometry, which removes passes that only a coarse step allowed (Level 12's spool at 10°, Level 15's ball at 28°) |
| G12 | Pedestal spheres are `FrozenUntilGrabbed`; in Levels 2 and 7 the pedestal is within one step of where the player stands | A sphere on a flat pedestal top is only metastable; and a distant first pickup silently changes `k` |

Gadget merge map:

| Draft names | Catalog name |
|---|---|
| `ExitDoor` | engine `Exit` |
| `PropLeash`, `NoParking`, the funnel jam rule in `ReturnPort` | `PropLeash` |
| `PressureButton`, `WeightPlate`, the pedal | `PressurePlate` |
| `BreakableBarricade`, `CrushTarget` | `Breakable` |
| `SocketWell`, `Socket`, the plate latch | `Socket` |
| `FlapDoor`, `SlidingDoor`, the gate, the tower `Flap` | `Door` |
| `FunnelGauge`, the station outlines | `FitGauge` |
| `WindStream`, `FanJet` | `WindStream` |
| `BallRun`, `HoopCatch`, `BaseballDrop` | `PathDrive` |
| `DominoRun`, `TipAssist` | `HingeChain` |
| `Funnel` plus the Level 3 cup, the hopper, the bell-jar neck | `Funnel` |
| `NestedSet` (was a fallback) | `NestedSet` (now the design) |
| `SkyCap`, `HazardZone`, `ReturnPort`, `SailRaft`, `BouncePad`, `Seesaw`, `Train`, `PropCarrier`, `LaserRain`, `WaterVolume`, `Sponge`, `FloatPlatform`, `PortalDoorway`, `RecallPad`, `MachineDirector` | unchanged names |
| `LaunchSeat`, `FunnelCapture` | remain fallbacks only (Levels 7, 8) |

### A.2 Per level

| Level | Change | Why |
|---|---|---|
| 1 | No geometry change. Tolerances restated from the script (pitch −8.5° to +1.5°; s = 9.4, not 9.5, because the 4° yaw puts one corner on the wall first). Wedge is `Upright` | Arithmetic held |
| 2 | Silo and hole radius 5.5 → 7; corridor 10 → 14 wide and tangent to the silo; far wall at Z = 30; door farther | Script: with a 10-wide corridor a **2° yaw error** stopped the thimble on the corridor wall at s = 8.8, two units off the axis. The draft's claim that the curved wall self-centres it was wrong. Now ±8° |
| 2 | Scale window 8.8–10.9 → 9–13 with the clamp equal to the top of the window | "Too far back" is now harmless: the thimble stops growing and is still captured |
| 2 | Capture rule: centre within 6 of the axis, eased over 0.35 s, rotation included | The draft's 1.5 capture radius left clamp-limited and off-axis throws to tumble on the rim |
| 2 | Thimble starts at 0.8 on a spool directly in front of the spawn (was 0.5, 20 units from the hole and off to the side) | The default first pickup is now a close one (`k` = 0.53); a pickup from 3 away could not be made big enough in the draft either, but nothing said so |
| 2 | Undersized thimble is sprung back to the spool by a `PropLeash` | It could come to rest out of sight under the near rim, and fishing it out is a three-step recovery too subtle for the second level |
| 2 | Hole clarified as the full disc; painted outline added; density 1.5 → 0.2 | The draft said both "chord at Z 19.7" and "11 across on the centre line"; a 1,400-mass thimble served no purpose |
| 3 | Flap is 0.08 thick with chamfered edges; leash volume includes the apple's origin; `SetSpawn` pitch is not an engine request (it exists) | A 0.3 lip needs a jump; the apple starts outside the box |
| 4 | Barricade and arch 6 → 12 high (Y −2.5..9.5), arch 9 → 11 wide | **The draft's solution did not work.** The barricade was flush under a solid wall; a toppling domino's tip reaches that plane first, at Y = 5.1 for the intended placement, i.e. on the wall above the barricade. The "taller than the barricade" rule was backwards for that geometry |
| 4 | Painted footprint on the board replaces "taller than the wall" as the readable rule; working band restated (pitch −10° to −3°); bot aim moved to the band's middle | Needed once the barricade is 12 high; the old aim sat at the short end of the band |
| 4 | Board-height formula corrected (1.375 − 0.25·z, not 1.25) | Arithmetic slip; the bot aim was unaffected |
| 5 | Launch volume widened to all of deck A inside the stream; one wind rule (`min(40, 12 / s)`) | A feather overhanging the canyon edge would not have moored; the draft had two overlapping blow-away rules |
| 5 | `GroundY` −15, `KillY` −13 | G6 |
| 6 | `PropLeash` on the cabinet top | A small eraser thrown up there with a distant pickup is out of sight from the floor |
| 7 | Pebble moved from a low spool 5 units away to a 2.3-high bobbin beside the seat; start scale 0.4 → 0.6; clamp 6 → 7 | Draft: any pickup from beyond 1.6 failed. Script also found the opposite failure: a pickup from 0.75 hit the 6 clamp at `d` = 8.4, short of the pivot, so the boulder landed on the player's own arm. Now every pickup within 2.5 works and the wall, not the clamp, limits it |
| 7 | Launch height uses the plank's top (5.35), not its underside (5.15): `apex = 5.35 + 9.34·f` | The feet are on the top surface |
| 7 | Loads are symmetric (the player counts on either arm) with a `PlankBias`; bot walks on over the tip | The draft's rule ignored a player on the short arm; the ruler's side edge is a 0.33 step |
| 8 | No geometry change. Funnel polygons circumscribed; standing bands restated from the script (S 1.5–2.5, M 2.5–5, L 6–12) | A 32-gon inscribed in a throat eats a tenth of the 5 % clearance |
| 9 | Exit box 6.6 → 5 wide | Its corners were 7.8 from the decks and the box spans the jump's whole height: a mid-air touch would have completed the level with under a unit to spare |
| 9 | `PropCarrier` grips any flat plank on a bed (within radius limits); the diving board is an accepted second route | Check: a 5.5-mass loose plank tips at about 1 rad/s², slow enough for a sprint and a 3.9 jump from its end. Blocking it physically was unreliable; gripping it makes it deterministic and it needs the same size and the same timing |
| 9 | `GroundY` −14, `KillY` −12, tower shortened; daylight preset | G6, G7 |
| 10 | Solid cubes with `NestedSet` (was the fallback); density set to keep the same masses | Three nested open shells with 0.04–0.07 walls 0.08 apart, and a hold march that can swallow obstacles through an open bottom |
| 10 | `PropLeash` on the ledge | A 0.3 box at the back of the ledge is out of sight from the rug |
| 11 | Corridor 10 → 12 wide | Script: yaw tolerance was ±3°; now ±6° |
| 12 | Key lies level on the landing with a chamfered rim; slot Y 0..0.4, sill 0.45, keyhole at 1.65 | The drafted 2.4° tilt left the bow floating 0.1 above the landing and gave a 0.4 lip to walk onto |
| 12 | Spool window 8.0–9.6 → 8.35–9.9 (clamp 9.9) | Follows the sill; keeps the standing band about 3.3 deep |
| 12 | Hazard zone covers the pit below Y −1.5 (was only the floor) | A player who dropped onto a too-short spool was above the hazard and below any exit |
| 12 | "Drops 6 units" → 9; spool outline painted on the door wall | Arithmetic; G8 |
| 13 | `SqueezeRate` 40 → 24 /s | **The ride drowned its rider.** 40/s into an area of 9 raises the tower at 4.4 units/s; the cork was capped at 3; the rider was 0.8 under after 0.6 s and was swept away |
| 13 | Water reset on `PlayerSwept`; `PropLeash` for a sponge behind glass | Softlock: a sponge wrung in the tower with nobody on the cork refilled the bowl, and the grab ray does not pass glass |
| 13 | Castle stair moved inside the bowl radius; tower and ledge dimensions squared with a radius-10 bowl; wet-mass rule dropped | A stair corner was at r = 10.03; nothing ever pushes the sponge |
| 14 | Exit opening 3.0 → 3.4 tall; exit box moved inside the tunnel | Script: from the terrace the end wall limits the door to 1.74, a hair over the drafted 1.7 limit; and a giant's capsule must not be able to touch the exit |
| 14 | Terrace gate `P` ≥ 2.5 (was 2.4); medium pitch claim corrected | G10; the draft's "below −30°" was really −36° |
| 14 | Second `RecallPad`, in hall A | The door can be projected through the mouse-hole while the player is too big to follow; the draft's "always visible in hall A" was false |
| 15 | Hopper axis Z 18.3 → 18.25, mouth radius 2.0 → 1.75 | The funnel mouth overlapped the back wall and overhung the tower front in a 3.5-deep band |
| 15 | Ten link gadgets → `PathDrive` × 3, `Seesaw`, `WindStream`, `HingeChain`, `Breakable`; sockets capture in the air | G4; nothing has to land |
| 15 | Ruler lands at 1.08, not 1.02; ball's lower limit is 2.21 (2.25 kept); gallery-to-plinth gap is 5.0, not 5.4 | The ruler stops when its underside meets the marker, 0.2 beyond the aim point; rounding |

---

## Appendix B — Scale windows for the bot tests

Each level test asserts `LevelCompleted`; these are the scales the solver's drops should produce
(± 5 %) and the windows the gadgets accept.

| Level | Prop | Start | Intended | Works from | To |
|---|---|---|---|---|---|
| 1 | Cheese wedge | 0.4 | 9.4 | 5.6 | 14 (clamp) |
| 2 | Thimble | 0.8 | 11.5 | 9.0 | 13 (clamp) |
| 3 | Apple | 11.4 | 0.40 | 0.27 | 0.50 |
| 4 | Domino | 0.5 | 4.1 | 3.6 | 5.3 (mass floor 3.08; placement decides the rest) |
| 5 | Feather | 1.0 | 9.2 | 6.5 | 12 (clamp) |
| 6 | Eraser | 0.8 | 9.5 | 7.5 | 10.5 (clamp) |
| 7 | Pebble | 0.6 | 4.6 | 3.3 (hard 3.0) | 7 (clamp) |
| 8 | Peewee / Aggie / Shooter | 0.25 / 1.2 / 5.0 | 0.64 / 1.32 / 2.45 | 0.50 / 1.00 / 2.00 | 0.76 / 1.52 / 3.04 |
| 9 | Plank | 0.65 | 4.03 | 3.7 | 4.4 (clamp) |
| 10 | Red (B) | 1.2 | 1.90 | 1.6 | 2.0 (clamp) |
| 10 | Yellow (A) / teal (C) | 0.9 / 0.6 | 1.01 / 1.00 | 0.75 / 0.85 | 1.15 / 1.15 |
| 11 | Playing card | 0.5 | 5.9 | 4.7 | 7.0 (clamp) |
| 12 | Key | 8 | 0.31 on the shelf, 0.30 in the lock | 0.06 | 0.5 |
| 12 | Spool | 1.0 | 9.1 | 8.35 | 9.9 (clamp) |
| 13 | Sponge, soaking | 2.0 | 9.5 | 8.4 | 12 (clamp) |
| 13 | Sponge, wringing | 9.5 | 2.0 | 0.5 (clamp) | about 2.3 (what fits in the tower) |
| 14 | Doorway: small / big / medium | 1.0 | 0.28 / 3.2 / 1.35 | 0.1 (clamp) / 2.5 / 0.1 (clamp) | 0.45 / 3.2 (clamp) / below 2.0 |
| 15 | Domino set | 0.6 | 3.02 | 2.2 | 3.5 (clamp) |
| 15 | Bouncy ball | 0.6 | 3.0 | 2.25 | 3.0 (clamp) |
| 15 | Ruler | 1.0 | 1.08 | 0.8 | 1.2 (clamp) |
| 15 | Fan | 5 | 0.94 | 0.6 | 1.2 |

Other values worth asserting: Level 4 `Broke` speed ≥ 5; Level 6 first bounce launch speed 27.0;
Level 7 `SeesawStruck` with `f` 0.89 ± 0.03 and the player grounded above Y 10.5 within 3 s;
Level 13 bowl depth 0.5 after the soak and `cork.AtTop` within 4 s of the wring; Level 15 the link
events in order with the marble's apex 6.8.

---

## Appendix C — Risks and fallbacks

Levels whose behaviour still depends on PhysX or on engine work that does not exist yet, with what to
ship instead if it does not hold up in the bot test.

| Level | What is still in doubt | Fallback |
|---|---|---|
| 4 | The domino must pivot on a 14° board (not slide, not bounce) and the impact must be sampled on the right tick | `HingeChain` single-piece mode: scripted rotation by the same law, same mass and speed test |
| 5 | `Prop.BeginDrive` for the moored and gliding feather | Invisible kinematic platform under the feather's visual |
| 7 | A kinematic plank throwing the capsule upward at 19 units/s; the capsule between the rising plank and the bookend | `LaunchSeat`: static seat, visual lever, same launch velocity after the same swing time |
| 8 | A marble within a few percent of the throat wedging; small fast marbles tunnelling the cone mesh | Jam rule (in the design); `FunnelCapture` scripted spiral |
| 9 | The capsule riding a mover that translates and rotates at 5.9 units/s; a free plank landing on a moving deck in the 0.4 s before capture | Capture in the air; or the straight ferry |
| 12 | A 289-mass rotation-frozen spool landing flat from 9 units | A `Socket` in the pit eases it down |
| 13 | Ride feel on the rising cork; glass and water shaders on the low tier | The moat variant (2-unit raft, no vertical ride) |
| 14 | `Player.SetScale` next to geometry and at the extremes (0.3, 3.2); bot tolerances at small scale; the portal's second camera on WebGL | Quantized sizes 0.3 / 1.0 / 3.2; flat-card portal on both tiers |
| 15 | Eleven gadget types and their reset paths in one level | Fallback A (authored timeline); Fallback B (ruler pre-seated) |

Low-risk levels (1, 2, 3, 6, 10, 11) have one rigid body settling on flat static geometry, or a
scripted seat.

**Cross-cutting.**
- Engine requests 1–7 (section 0.4). The load-bearing ones are `BeginDrive` / `EndDrive` (six levels),
  `GrabPose` (eleven levels) and `GroundY` (six levels). Each level's fallback says what to do without.
- `ART_BIBLE.md` §6.2 must take `GroundY`; the outlines, the glass (Level 13) and the portal card
  (Level 14) need material recipes there; Level 11 relies on its own lid for shade under a daylight
  preset.
- Gadget effects (laser dots, water tint, the portal view, wind streamers, gauge lamps) must ignore
  the held toy in rendering exactly as the simulation does, or they become depth cues. Gauge lamps and
  outline tints are the one sanctioned exception, and they are world objects.
- Costs to watch on WebGL: Level 11 draws about 550 instanced beams (refresh a third per frame on the
  low tier); Level 14's second camera is high-tier only; Level 13's water is two flat meshes.
- Level 15 runs over the 1–4 minute target by design; most of the extra is walking between stations.

END OF LEVELS.md

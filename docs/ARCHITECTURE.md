# Tinker's Toybox — Engine Architecture

This is the engineering contract for the game. Every module and every level is written against it.
If you need to deviate, change this document in the same commit.

> **Engine note.** The project started as a three.js/Rapier prototype and moved to **Unity** on
> 2026-10-03. Units, player metrics and the mechanic are unchanged. If instructions you were given
> mention three.js, Rapier, TypeScript or Vitest, translate them to the Unity equivalents below.

## Stack

- **Unity 6** (`6000.6.4f1`), **URP 17.6**, PhysX, Input System 1.20, UGUI + TextMeshPro, Unity Test Framework.
- Target: **WebGL**, deployed to GitHub Pages.
- No imported art or audio. Meshes, textures, shaders, sounds and music are generated in code.

## Two rules that shape everything

### 1. Code-first

Levels, toys, environments and UI are built from C# at runtime. There is one scene
(`Assets/Toybox/Scenes/Main.unity`) containing one `Bootstrap` object. No prefabs, no hand-placed scene
content, no hand-edited `.asset` / `.unity` / `.meta` YAML. Project settings, pipeline assets, template
materials and every other generated asset are made by **setup steps** that
`Toybox.EditorTools.ProjectSetup.Run` finds and runs (see "Setup steps" below) — add a step instead of
editing settings by hand.

### 2. The simulation is ticked manually and runs without Play Mode

`Physics.simulationMode = SimulationMode.Script`. `Game.Tick()` advances exactly one 1/60 s step:

```
sample input -> player -> grab / drop -> level + gadgets -> PhysicsScene.Simulate(dt)
             -> ground probe, held-prop placement, prop impacts, triggers, exits, kill plane -> events
```

Simulation code (`Engine`, `Gadgets`, `Toys`, `Art`, `Levels`) therefore must **not** depend on the
MonoBehaviour lifecycle: no `Start` / `Update` / `FixedUpdate`, no `OnTriggerEnter` / `OnCollisionEnter`,
no coroutines, no `Time.time` / `Time.deltaTime`, no `UnityEngine.Random`. MonoBehaviours are allowed
there only as passive tags (for example `PropRef`, which maps a GameObject back to its `Prop`).

- Triggers are evaluated with overlap queries every tick, and enter/exit is diffed in code.
- Destroy objects with `Sim.Destroy(obj)` (it picks `DestroyImmediate` outside Play Mode).
- After moving a transform by hand, call `Physics.SyncTransforms()` before querying. It costs about
  50 µs per call, so do not call it in a loop.
- Use `game.Rng` (seeded) and `game.Time`.
- **Query physics through `game.PhysicsScene`** (`Raycast`, `SphereCast`, `OverlapSphere`, `OverlapBox`, ...),
  never through the static `Physics.Raycast` family. See the next section for why.

This is what lets every level ship with an EditMode test in which a scripted bot actually solves it —
no Play Mode, no rendering, a few seconds per level.

### The simulation has its own scene and its own physics world

PhysX only replays identically from a freshly created physics scene. In a scene that bodies have been
added to and removed from, the same inputs give slightly and then wildly different results (measured:
the shared default scene does not even repeat a run twice). So `Game` keeps everything it creates under
`game.Root` in a private Unity scene (`game.Scene`) with a physics world of its own (`game.PhysicsScene`),
and **every `LoadLevel` gets a brand-new one**. A level therefore behaves the same in its test, after a
restart and after any other level; `DeterminismTests` checks this bit for bit.

Consequences:

- The static `Physics.Raycast` / `Physics.OverlapSphere` / `Physics.Simulate` only see the *default*
  physics scene, which is empty. Use `game.PhysicsScene`. (`Physics.ComputePenetration`,
  `Physics.IgnoreCollision` and `Physics.SyncTransforms` are not tied to a scene and are fine.)
- Objects join the simulation by being parented under `game.Root` / `game.LevelRoot` — `LevelContext`
  does that for everything added through it. Do not reparent `game.Root`.
- `game.Scene` changes on every level load; re-read it on `LevelLoaded`.
- In Play Mode and in the build the scene is an ordinary additive runtime scene
  (`LocalPhysicsMode.Physics3D`) and is rendered like any other. Outside Play Mode Unity cannot create
  those, so it is an editor *preview scene*, which ordinary cameras do not render: the camera has to be
  in that scene (parent it under `game.Root`) and have `camera.scene = game.Scene`. Editor tools that
  render a level outside Play Mode must do that, or pass `GameOptions.Isolated = false`, which puts
  everything into the open scene and the default physics scene instead (renderable as usual, but not
  reproducible). `Render/CameraRig` does the former in both modes, and the preview-scene path is what
  `Shots.Capture` renders through. Ambient light and fog (`RenderSettings`) are taken from the *active*
  scene even then, not from the simulation's scene.
- Global physics settings are still global: `Game.Create` sets `Physics.simulationMode`, gravity, the
  default solver iterations, the depenetration speed and the layer matrix, and `Dispose` restores them.

Physics settings that have no runtime API are written by the setup step `CoreSetup.ConfigurePhysics`: the solver is
**Temporal Gauss-Seidel**, run with 32 position and 4 velocity iterations and a depenetration speed
of 2 (both set by `Game`). Mass goes with scale cubed, so mass ratios of hundreds to one are routine, and
the default solver lets a heavy body sink straight through a light one. What holds (see
`StackingTests`): a prop resting on props 320 times lighter, a tower of eight blocks. At 8000 : 1 the
heavy one sinks through the light ones, without exploding. The player is a special case, see below.

`Game` also sets **`Physics.improvedPatchFriction = true`**. Without it PhysX gives a flat contact twice
the friction its coefficient says (a box with 0.6 on a level deck does not move under a push of 0.9 g);
with it, friction is the coefficient - the average of the two materials' - which is what levels compute
slides and topples from (`IntegrationPhysicsTests`).

Presentation code (`Render`, `UI`, `Audio`, `Platform`) may use MonoBehaviours freely. The simulation
never calls into it; it raises events and presentation subscribes.

## Layout

```
Assets/Toybox/
  Runtime/                 assembly "Toybox"
    Bootstrap.cs             scene entry point
    Engine/                  simulation core, including the room a level stands in (Environment*.cs)
    Toys/                    procedural toy catalog (visual + colliders + physical properties)
    Art/                     palette, recipes, materials, procedural meshes and textures
    Gadgets/                 reusable level machinery (buttons, plates, fans, lasers, movers, water, portals)
    Levels/                  one class per level, discovered by reflection
    Render/                  camera rig and presenters: quality and post-processing, lighting, the room's
                             visuals (Environment/), pools, exit marks, the held-toy sticker
    UI/                      presenters: HUD, menus, level select; the sticker kit they are built from
    Audio/                   one presenter: synthesized sfx + generative music
    Platform/                GameRunner, GameFlow, Settings, Progress, human input, launch options, the presenter seam
  Editor/                  assembly "Toybox.Editor": ProjectSetup, BuildScript, Shots, LookShots, PlayCheck
    Setup/                   setup steps, one file per area (CoreSetup, PipelineSetup, RoomSetup,
                             ToyShadingSetup, UiSetup), and SetupUtil
  Tests/EditMode/          assembly "Toybox.Tests.EditMode"
  Shaders/                 the five shaders of the game: ToyLit, RoomLit, Sticker, Flat, MacroBand
  Resources/               what must ship without a scene reference: Materials/ (a template per shader +
                             PlainLit), Volumes/ToyboxPost, ToyboxVariants, Fonts/ (two baked TMP fonts,
                             three material presets), UI/Frame
  Fonts/                   Unbounded-Bold.ttf, Figtree-SemiBold.ttf and their licences (the only imported assets)
  Scenes/, Settings/       generated by setup steps: Main.unity; ToyboxRenderer + ToyboxURP_Low/_Medium/_High
Assets/WebGLTemplates/Toybox/   page shell and loading screen
tools/unity.ps1            batch-mode wrapper (compile | test | exec | playcheck | build)
```

`Assets/Toybox/link.xml` preserves the whole `Toybox` assembly, because levels and presenters are found
by reflection.

## Working with Unity from the command line

Only one Unity instance can open the project at a time. Always go through the wrapper — it queues
concurrent callers behind a mutex and prints a compact summary ending in `RESULT: OK` or `RESULT: FAILED`.

```powershell
.\tools\unity.ps1 compile                                   # import + compile, list compiler errors
.\tools\unity.ps1 test                                      # all EditMode tests
.\tools\unity.ps1 test -Filter "Toybox.Tests.PlayerTests"   # one fixture / test / regex
.\tools\unity.ps1 exec -Method Toybox.EditorTools.ProjectSetup.Run
.\tools\unity.ps1 exec -Method Toybox.EditorTools.ProjectSetup.Run -UnityArgs '-toyboxSteps','RoomSetup'
.\tools\unity.ps1 exec -Method Toybox.EditorTools.Shots.Capture -UnityArgs '-toyboxLevel','3','-toyboxTimes','0,2,5'
.\tools\unity.ps1 playcheck                                 # Play Mode smoke run: the bot autoplays a level
.\tools\unity.ps1 playcheck -UnityArgs '-toyboxPlain'       # the same with the plain look
.\tools\unity.ps1 build                                     # WebGL build into the cache directory
```

`Shots.Capture` takes `-toyboxLevel <id>` (0), `-toyboxTimes "0,2,5"` (seconds of bot autoplay before
each capture; default `"0"`, right after the spawn), `-toyboxSize "1280x720"`, `-toyboxOut <dir>`
(`tools/out/shots`), `-toyboxOverview` (adds a third-person view of the whole level with a marker for
the player), `-toyboxPlain` (the debug look), `-toyboxQuality low|medium|high` and `-toyboxUi <screen>`
(below). It writes `levelNN-tSS.png` / `levelNN-tSS-overview.png` and logs `[Toybox] shot: <path>` for
each; a failing solve script is logged as `[Toybox] bot error: ...` and the shots are taken anyway.
`playcheck` also takes `-toyboxUrl "?level=3"`, the query string a page address would carry.

**Pictures are what the game shows.** `Shots.Photograph` renders the camera into an HDR target of the
game's own colour format (B10G11R11) with the tier's sample count and encodes it afterwards - URP takes
the format of its intermediate target from the camera's target texture, and through an 8-bit one
nothing would exceed 1 and bloom would have nothing to work with. Every capture tool goes through it:

| Tool (`unity.ps1 exec -Method ...`) | What it is for |
|---|---|
| `Toybox.EditorTools.Shots.Capture` | A registered level, played by its bot |
| `... -UnityArgs '-toyboxUi','hud'` (any tool below too) | The same picture **with the UI**: `hud`, `toast`, `title`, `pause`, `hints`, `settings`, `select`, `catalogue` (the fifteen planned levels with some progress), `complete`. The canvases become world-space canvases on the camera, drawn by an overlay camera stacked on it (`UiCapture.OverlayFor`) - after post-processing, as the real overlay is |
| `Toybox.EditorTools.RoomShots.Capture` | A registered level in other rooms: `-toyboxEnv all` or keys; `-toyboxSpawn "x,y,z,yaw,pitch"`, `-toyboxVisit n`, `-toyboxTag`, `-toyboxPresenters "LightingRig,RoomVisuals"`. Files `<key>[-tag]-tSS.png` |
| `Toybox.EditorTools.LookShots.Capture` | `LookCheckLevel`: catalog toys of every recipe on the room's play surface; the bot holds the apple from 1 s and lets go at 4 s. `-toyboxEnv`; default times `0.5,3,4.05,4.2,6` (at rest, in hand, release, splash, settled). Files `look-<key>[-tag]-tSS.png` |
| `Toybox.EditorTools.PipelineSetup.CaptureTiers` | A level at all three tiers with measurements (vignette, clipping, sharpness); `-toyboxCheck` draws the post-processing check level |
| `Toybox.Tests.Gadgets.GadgetShowroomTests.Capture` | Every gadget with a body, in one level |

- Pass `-UnityArgs` from PowerShell (`& .\tools\unity.ps1 exec ... -UnityArgs 'a','b'`). Through
  `powershell -File` from another shell the array is dropped silently and the method runs with defaults.
- Never create or edit `.meta` files. Unity generates them on import; they are committed afterwards.
- The project must not be open in the Unity editor GUI while the wrapper runs.
- Lines logged with the prefix `[Toybox]` are echoed by the wrapper — use it to report results from
  editor scripts.

## Conventions

- Unity's left-handed, **Y-up** space. Yaw 0 looks down **+Z**; positive yaw turns right; positive pitch
  looks **up**. Angles are in **degrees**.
- 1 unit = 1 Unity meter. The player is 1.7 tall; a playroom is hundreds of units across. Keep play
  space within ±150 on every axis.
- **Fixed timestep 1/60 s** (`Sim.Dt`), deterministic for a given build and machine: the same level with
  the same inputs ends in bit-identical transforms, whatever was played before.
- Scale is always **uniform**. `prop.Scale` is an absolute multiplier where `1` is the authored size.
- Namespaces mirror folders: `Toybox.Engine`, `Toybox.Toys`, `Toybox.Levels`, ...

## Player metrics (levels are designed against these — do not change casually)

| Metric | Value |
|---|---|
| Capsule | radius 0.3, total height 1.7 |
| Eye height | 1.55 above feet |
| Walk / sprint speed | 5 / 8 units·s⁻¹ |
| Gravity | 22 units·s⁻² (`Physics.gravity`, set by `Game`) |
| Jump speed | 7.6 → apex ≈ 1.3 units |
| Longest jump (sprint, level ground) | ≈ 5.5 units — design gaps ≥ 9 to be un-jumpable |
| Max walkable slope | 50° |
| Highest step walked over | ≈ 0.1 units (a static lip); higher is a wall |
| Ground held on to | up to 0.2 below the feet (`Player.SnapDistance`): a step down, a crest, a down slope |
| Grip on a kinematic `Mover` | sudden speed changes up to 6 units·s⁻¹ (`Player.MoverGripSpeed`) |
| Mass | 3 |
| Kill plane | `level.KillY` (default −30): player respawns at last checkpoint, props respawn at origin |

The player is a **dynamic Rigidbody** (rotation-frozen capsule, continuous collision detection,
zero-friction physics material), not a `CharacterController`, because the game needs the player to be
launched by a seesaw, bounced by a trampoline, pushed by wind and carried by moving platforms. The
controller steers horizontal velocity toward `input × speed + groundVelocity` with high ground
acceleration and low air acceleration, so external impulses persist in the air and die on the ground.

Details of `Engine/Player.cs` that levels and gadgets can rely on:

- The transform position is the **feet**. `Yaw` / `Pitch` live in the controller (the body never
  rotates); pitch is clamped to ±89°.
- **Ground** is found after every physics step with a sphere cast under the capsule and a ray down its
  axis. On walkable ground the controller steers along the surface and cancels the part of gravity that
  lies in the plane the capsule rests on, so the frictionless capsule neither sticks to walls nor creeps
  down slopes. `GroundCollider`, `GroundNormal`, `GroundPoint` and `GroundProp` describe it.
- **Steeper than 50° is not ground.** A face between a wall and a walkable slope stops the player or
  turns them aside, but never lifts them: walking into it does not bob, and jumping at it reaches the
  ordinary jump height and no more. Two such faces leaning against each other (a crevice) do carry the
  player, who then counts as standing and can jump out.
- **The speed along the ground normal belongs to the solver, except where the last step showed that it
  is not a launch.** After each step the controller compares what it commanded with what came out:
  - *A bump*: the speed relative to the ground did not grow, it was only turned upward (a seam, a lip, the
    crest of a ramp). The upward part is taken out and the player stays grounded.
  - *Ground a little further down* (within `Player.SnapDistance` = 0.2 per unit of scale: a low step
    down, the far side of a crest, a slope that starts going down): the feet are pulled onto it, at up to
    6 units·s⁻¹, and the player stays grounded. A drop deeper than that is a fall.
  - *A kinematic `Mover` that changes its speed*: up to `Player.MoverGripSpeed` = 6 units·s⁻¹ of sudden
    change along the ground normal the rider stays on it (an elevator may start, stop and reverse
    abruptly). Above that the rider is thrown: a piston that stops after rising at 12 keeps none of them.
  - *A launch*: anything else that leaves the player rising faster than 1.5 units·s⁻¹ relative to the
    ground - a seesaw, a prop from below, a bounce after coming down hard, `AddImpulse`, `SetVelocity` -
    releases the ground. Less than that is swallowed.
- **Steps.** The foot of the capsule rolls over a static lip up to about 0.1 high (a third of its
  radius) without leaving the ground; anything higher is a wall. Design steps accordingly, or use ramps.
- Ground velocity is inherited from kinematic `Mover`s and from dynamic bodies (point velocity). A turning
  mover reports the chord of its arc (`Mover.PointVelocity`), so a rider stays on a turntable.
- **Air steering turns the velocity toward the input direction and adds speed up to walk / sprint
  speed, never beyond**; it does not brake momentum from a launch either. Steering in the air cannot
  lengthen a jump, and chained jumps do not build up speed.
- For a *steady* push (wind, a current, a conveyor) call `player.AddPush(acceleration)` every tick: in
  the air it accelerates the player, on the ground it shifts the velocity they are steered toward (a
  push of 20 is a drift of 5). An upward push stronger than gravity lifts the player off.
- Jump is edge-triggered (holding the key does not re-jump), with 0.1 s coyote time and a 0.12 s buffer.
  With the 1/60 s step the measured apex is 1.25 and the sprint jump 5.47 units.
- `bool SetScale(s)` scales capsule, eye height, speeds, accelerations and probes by `s` and the jump
  speed by `√s` (so the apex scales by `s`). Mass stays 3. The feet stay where they are; a bigger capsule
  that touches something is moved clear of it sideways or upward. If there is no room (a low ceiling) it
  returns `false` and changes nothing - check it. Jump *length* goes with `s^1.5`.
- `PlayerLanded` reports the impact speed and what was landed on; it needs at least two ticks in the air.
- `EyeAt(alpha)` (what the camera uses) interpolates the player's movement *relative to what they stand
  on*; the ground's own movement during the tick is taken as it is.

`Engine/PlayerContactScaler.cs` adjusts the contacts PhysX generates for the capsule (a contact
modification; nothing else in the scene is affected):

- **Standing on light props.** In contacts between the player and a prop, the prop counts as at least
  `player mass / 8`. Without it the player sinks into and through crates much lighter than themselves,
  and an ordinary half-unit crate weighs 0.125. The prop's real mass governs everything else. Landing on a
  prop can still dip the feet a few centimetres into it for a tick or two.
- **Light props are shoved, not climbed.** A prop lighter than `player mass / 8` only counts as ground
  when it is right under the player's axis. Touched by the side of the foot it is pushed along the floor
  (the contact is made horizontal) instead of pinning it down and throwing the walking player into the
  air - the pebbles the mechanic leaves behind are kicked away.
- **Seams.** While the capsule stands on one collider PhysX already generates a contact against the edge
  of the next (floor tiles, a ramp box meeting a platform box); it would turn walking speed into a bump.
  Contacts that lie in the plane the player walks on and have not been reached yet are dropped, so a
  floor may be built from flush boxes. A real lip sticks out of that plane and is kept.

## The perspective mechanic (`Engine/PerspectiveGrabber.cs`)

The spec formula is `NewScale = OldScale × (NewDistance / OldDistance)`. We implement it continuously.

**Grab.** Raycast from the eye along the view direction (range 150). If the first solid hit belongs to
a grabbable prop, take it. Otherwise a small aim-assist cone (2.5°) takes the grabbable prop whose
*colliders* come closest to the view ray, if it is in plain view there: it covers tiny targets and near
misses, not a crosshair that points well past the prop. Record
`k = prop.Scale / distance(eye, prop center)` - the object's apparent size, which stays constant for
the whole hold. A prop the player is standing on cannot be grabbed. A `Kinematic` prop is not grabbable
unless its options say so (`Grabbable = true`); while one is held its `Mover` ignores `MoveTo`.

`grabber.Focus` is the **focus candidate**: the prop a click would take right now (null while something
is held). It is worked out at most once per tick — on first use after the tick, from the view at that
moment — and remembered until the next one, so presentation may ask every frame (`FocusQueries` counts
the searches).

**Hold.** Each tick, *after the physics step* (from the eye and the view the tick ends with), the held
prop is *projected* along the view ray to the farthest free spot: march the distance `d` outward
geometrically (ratio 1.08) from near the eye (0.35 units); at each step test whether the prop's
colliders, scaled to `k·d` and placed with the prop's center at `eye + dir·d`, overlap the world (the
player and the prop itself are excluded). At the first overlap, bisect back to the boundary
(8 iterations) and pull in slightly. The prop is shown there at scale `k·d`. Because `scale / distance`
is constant its on-screen footprint never changes - it just "lands" on whatever is behind it. The march
is capped by a center raycast, by the prop's `MinScale` / `MaxScale`, and by a maximum hold distance
of 150.

A step only tests the pose it arrives at, so a thin prop (or one grabbed from far away, which is thin
relative to its distance) could step over a thin wall or between bars. Therefore, wherever anything is
near the prop's path during a step (one `OverlapBox` around both end poses), that step is walked in
finer ones whose ratio follows from the prop's thinnest collider (`Prop.BaseThinness`), down to 1.004.
A placement spends at most 256 fine steps; after that the march goes on coarsely.

The overlap test is exact for boxes, spheres, capsules and convex meshes, compound props included: an
`OverlapBox` of the prop's bounding box finds candidates, then `Physics.ComputePenetration` tests every
collider of the prop against every candidate. If there is no free spot at all on the ray (the player
stands with their nose against a wall), the prop stays at the last pose that was free and
`grabber.PlacementValid` is false.

While held, the prop's Rigidbody is kinematic and it sits on the `Held` layer, which collides with
nothing and is invisible to grab and hold queries - and to triggers, which only sense a prop once it is
dropped. Orientation is kept relative to camera **yaw**; the player can turn the held prop in 15° yaw
steps and 90° pitch steps (about the camera's right axis); a prop with `AllowPitch = false` ignores the
pitch key. At the end of every tick the prop's center is on the view ray of the eye that tick ended with.

**Grab pose** (`PropOptions.GrabPose`). `Keep` (the default) holds a prop with the tilt it was picked up
with. `Upright` eases it onto its authored up axis and `Snap90` stands up whichever of its own six axes
points most nearly up, both over the first 0.15 s of the hold (`PerspectiveGrabber.PoseTicks` = 9, a
smoothstep) and by the smallest turn, which leaves the heading alone; an upside-down prop is rolled over
about its own forward axis. A prop let go before the ease is over is still put down in the final pose.
The pitch and yaw keys turn the prop from that pose.

Between ticks the view keeps turning (the mouse) and the camera's eye is interpolated. The render layer
calls `grabber.Present(eye, lookRotation)` for each frame, which draws the held prop on that camera's
crosshair at its current hold distance and scale. That is drawing only: the simulation keeps its own
placement and restores it when the next tick begins, so frames do not change results.

**Drop.** The prop is placed once more for the current view, returns to the `Prop` layer at its new
scale, becomes dynamic again with zero velocity, its mass becomes `density × volume × scale³` (never
less than `Prop.MinMass` = 0.01), and sleeping bodies are woken. A `Fixed` prop stays where it is dropped.

**Props and the player's body.** Nothing crushes the player. A prop that overlaps the player at the
moment of release passes through them until they have separated. The same goes for a dynamic prop
heavier than the player that sinks more than 0.15 into the capsule from above or from the side - the
prop let go overhead that grows and comes down on its owner, or one a respawn put the player inside of:
it comes to rest around the player, who walks out, and then it is solid for them again. (A prop the
player lands *on* is not affected.) With the default `MaxScale` of 80 a prop held against the open sky
reaches some 40 units and tens of tonnes; levels with open sky should set `MaxScale`.

`PropGrabbed`, `PropHeld` (every tick of a hold, after the placement) and `PropDropped` carry
`{ Prop, OldScale, NewScale, GrabDistance, DropDistance }`; unless a clamp was hit,
`NewScale / OldScale == DropDistance / GrabDistance`. The same payload read the way presentation thinks
of it: `GrabScale` (= `OldScale`, the scale at the grab), `Scale` (= `NewScale`, the scale now),
`Distance` (= `DropDistance`), `Factor` (`Scale / GrabScale`, the "×3.2" of the readout) and `Radius`
(the true bounding radius now). While a prop is held the grabber also has `GrabScale` / `GrabDistance`.
`PropDropped` is raised whenever a hold ends, also when the held prop is respawned or removed (then
`e.Prop.Removed` is true by the time it arrives) — but not when the level is unloaded with a prop in
hand: listen to `LevelLoaded` / `LevelUnloading` and reset.

Controls: click (or `E`) toggles grab/drop. `Q` / mouse wheel yaw the held prop. `F` pitches it 90°.

## Layers

| Layer | Index | Notes |
|---|---|---|
| Default | 0 | static world geometry |
| Prop | 8 | grabbable and loose physics objects |
| Player | 9 | the player capsule |
| Held | 10 | the prop currently being held — collides with nothing |
| Trigger | 11 | sensor volumes; ignored by all raycasts |

`Toybox.Engine.Layers` holds the indices and masks and configures the collision matrix at startup.
`Layers.SolidMask` (Default + Prop) is what blocks movement, grabbing and the held-prop projection;
`Layers.SensedMask` (Prop + Player) is what triggers sense. Raycasts in simulation code should pass one
of them and `QueryTriggerInteraction.Ignore`.

## Core types (sketch — see the source for full signatures)

```csharp
public sealed class PropOptions {
    public string Name;
    public float Scale = 1f;
    public float Density = 1f;          // mass = Density * volume * Scale^3
    public float Friction = 0.6f, Bounciness = 0f;
    public float LinearDamping = 0f, AngularDamping = 0.05f;
    public bool? Grabbable;             // unset: true for Dynamic and Fixed, false for Kinematic
    public PropBody Body = PropBody.Dynamic;   // Dynamic | Fixed | Kinematic
    public float MinScale = 0.02f, MaxScale = 80f;
    public string[] Tags;
    public GrabPose GrabPose = GrabPose.Keep;  // Keep | Snap90 | Upright: what a grab does to a tumbled prop's tilt
    public bool FrozenUntilGrabbed;     // Dynamic only: stays put (kinematic) at its authored pose until the first grab
    public bool AllowPitch = true;      // false: the pitch key (F) does nothing while it is held
    public bool KeepUpright;            // rotation about X and Z frozen while it is dynamic
}

[Level(1, "cheese-wedge", "The Cheese Wedge", Phase = 1)]
public sealed class Level01CheeseWedge : LevelDefinition {
    public override string Blurb => "...";                    // one-line objective
    public override string[] Hints => new[] { "...", "...", "..." };
    public override string Environment => "sunny-rug";         // preset key; the default is the art bible's table
    public override float GroundY => 0f;                       // the play plane: where the room's floor / rug / bench top goes
    public override float KillY => -30f;
    public override void Build(LevelContext ctx) { ... }
    public override IEnumerator Solve(Bot bot) { ... }         // scripted solution: tests + autoplay
}
```

**Props a gadget needs a hand on** (LEVELS 0.4):

- `prop.BeginDrive()` lends a `Dynamic` prop to a gadget: the body becomes kinematic and is moved through
  the returned `Mover` (`prop.Mover` while `prop.Driven`) with one `MoveTo` per tick, so the player and
  whatever else rides it inherit its velocity. While driven it is ground whatever its mass — the
  light-prop rules below only apply to simulated bodies — triggers go on sensing it, and it can still be
  grabbed: **a grab ends the drive**, so a gadget checks `prop.Driven` every tick. `EndDrive(velocity)`
  hands it back to the simulation moving at that velocity. `Respawn` and removal end a drive too.
  `BeginDrive` returns null (and changes nothing) for a held or removed prop and throws for `Fixed` and
  `Kinematic` ones.
- `FrozenUntilGrabbed`: `prop.Frozen` until the first grab thaws it (or `prop.Unfreeze()` does); a
  `Respawn` freezes it again at its authored pose. Frozen it is kinematic: it neither falls nor is pushed.
- `KeepUpright` freezes rotation about X and Z (`Rigidbody.constraints`), so a toy that was put down
  upright slides but never tips. Combine it with `GrabPose.Upright`.

A toy is a GameObject hierarchy (renderers + colliders, authored at scale 1, no Rigidbody) returned by
a factory in `Toybox.Toys`. `ctx.AddProp(toy, position, rotation, options)` turns it into a `Prop`.
Colliders may be boxes, spheres, capsules and convex meshes, on the root or on (possibly non-uniformly
scaled) children; volume, center and bounding radius are measured from them. `prop.Center` is the middle
of the colliders' bounds and is what perspective distances are measured to. Body kinds: `Dynamic` is
simulated; `Fixed` never moves on its own (and stays where it is dropped if it is grabbable);
`Kinematic` is driven by level code through `prop.Mover` and is not grabbable unless asked for (make a
moving platform a kinematic *prop* when triggers must sense it; `AddKinematic` geometry is on the
Default layer and is not sensed). `PropRef.Of(collider)` leads from any collider back to its `Prop`.

`LevelContext` is the authoring surface. Everything a level adds is torn down on unload.

```csharp
GameObject AddStatic(GameObject go, Vector3 position, Quaternion rotation);   // world geometry
Prop       AddProp(GameObject toy, Vector3 position, Quaternion rotation, PropOptions options = null);
void       RemoveProp(Prop prop);
Mover      AddKinematic(GameObject go, Vector3 position, Quaternion rotation); // call mover.MoveTo(...) every tick
Trigger    AddTrigger(string name, Volume volume, Vector3 position, Quaternion rotation);
void       RemoveTrigger(Trigger trigger);
void       SetSpawn(Vector3 feetPosition, float yawDeg, float pitchDeg = 0);
Trigger    AddCheckpoint(Volume volume, Vector3 position, string name = null);   // respawn on the floor under its center
Trigger    AddCheckpoint(Volume volume, Vector3 position, Vector3 respawnFeet, float respawnYawDeg, string name = null);
Exit       AddExit(Vector3 position, Vector3 size, string name = null);          // starts unlocked; AddExit(...).Lock()
void       Complete();   void Say(string text, float seconds = 4);
void       OnUpdate(Action<float> update);   void OnDispose(Action dispose);
Game Game;  Rng Rng;  Transform Root;  float Time;  int Ticks;
LevelDefinition Level;  Dip Dip;  float GroundY;       // the level being built, its room's dip, its play plane
```

- Every pose is the **center** of the thing: `Volume.Box(size)` / `Volume.Sphere(radius)` are centered on
  the position given, and so is the exit's box. Overloads without the rotation exist. `SetSpawn` and an
  explicit respawn point are **feet** positions.
- A checkpoint makes the player respawn on the floor under the volume's center: the first static surface
  below the top of the volume, or its bottom center if there is none inside it. So it does not matter
  whether the volume stands on the floor or is centered on it. Checkpoints and exits are named
  `"Checkpoint"` / `"Exit"` unless given a name, which is what `TriggerEntered` listeners see.
- `OnUpdate` callbacks run every tick before the physics step, with `dt = Sim.Dt`. That is where gadgets
  live and where a `Mover` is driven: `MoveTo(position, rotation)` states where the body should be after
  this step; a tick without a call means it is at rest. The mover reports `Velocity` /
  `PointVelocity(point)`, which is how the player rides it (see the grip rule above).
- A `Trigger` has `OnEnter` / `OnExit` (payload: the trigger, `IsPlayer`, `Prop`), `PlayerInside`,
  `PropsInside` (ordered by `prop.Id`), `Enabled`, `SensesPlayer` / `SensesProps`, and a `Transform` that
  can be parented to something that moves. It senses colliders that *touch* the volume - a pressure plate
  for props needs `SensesPlayer = false`. A prop that is removed leaves every trigger at once (`OnExit`
  fires, `PropsInside` never holds a removed prop). A trigger parented to a prop goes with it when the
  prop is removed: whatever was inside leaves and `trigger.Removed` becomes true.
- An `Exit` has `Trigger`, `Name`, `Position`, `Locked`, and `Lock()` / `Unlock()` that return the exit.
  Changing the lock raises `ExitLockChanged`.
- `Build` may run more than once on the same level instance (`RestartLevel`), so it must initialize all
  of the level's own fields. `LevelRegistry.Get(id)` returns a fresh instance each time. Ids and slugs
  must be unique; the registry throws otherwise. `Slug` and `Title` come from the attribute and are
  virtual, for ad-hoc levels without one.
- `game.LoadLevel` and `game.RestartLevel` called from inside a tick take effect when that tick ends,
  after the tick's events have been delivered.

## Events (`game.Events`)

`LevelLoaded`, `LevelUnloading`, `LevelCompleted`, `LevelRestarted`, `PropGrabbed`, `PropHeld`,
`PropDropped`, `PropImpact`, `PropRespawned`, `PlayerJumped`, `PlayerLanded`, `PlayerRespawned`,
`TriggerEntered`, `TriggerExited`, `ExitLockChanged`, `Message`, plus the gadgets' 53 mirror channels
(`Gadgets/GadgetEvents.cs`, all with the payload `GadgetEvent { Gadget, Prop, Position, Value, Value2,
Index, Text }`). Render, UI and audio react to these; the simulation never waits on them.

- **`PropImpact { Prop, Speed, Mass, Point, Normal }`**: a dynamic prop hit something. There is no
  physics callback behind it: right before the physics step every simulated prop's velocity and the
  force accumulated on it are noted, and right after it the velocity is compared with what gravity,
  that force and damping account for. A difference of `Prop.ImpactSpeed` (1.5 units·s⁻¹) or more is an
  impact — a landing, a crate struck by another, a block the player kicked — and `Speed` is its size
  (resting contact is 0.37, a push from level code is none at all). `Normal` is the direction of the push
  the prop received (up, for a landing) and `Point` the spot of its colliders facing the other way, an
  estimate good enough for dust and sound. One prop reports at most one impact per 6 ticks. Being
  derived from the simulation's own state, the events replay bit for bit.
- **`LevelUnloading`** is raised right before a loaded level is torn down (another level is loaded, the
  level restarts, the Game is disposed), while everything of it still exists, and is always delivered at
  once. A restart is `LevelRestarted`, `LevelUnloading`, `LevelLoaded`.

Events are plain C# events with small payload structs (`Engine/GameEvents.cs`). Those raised during a
tick are **queued and delivered in order when the tick ends** - before a level switch requested during
that tick tears the level down, so a listener still finds the objects an event refers to. Outside a tick
they are delivered at once. An exception in a listener is logged and does not stop the others. Level
logic that must react *inside* the tick uses `Trigger.OnEnter` / `OnExit` or `ctx.OnUpdate`, not
`game.Events`. `GameEvents` is a partial class: a gadget adds an event with a channel field, an event
accessor and a `Raise...` method in its own file.

**Subscriptions made by a level end with the level.** Whatever subscribes to `game.Events` while a level
is being built or during a tick - that is, level and gadget code - is recorded with that level and
unsubscribed when it is unloaded or restarted; there is nothing to pair with `ctx.OnDispose`.
Presentation code subscribes outside ticks and keeps its subscriptions across levels.

## The Bot (`Engine/Bot.cs`)

A scripted player that drives the **same input path** as a human, so a passing solver proves a human
can do it. Scripts are iterator methods; each `yield` is one tick.

```csharp
yield return bot.LookAt(target);            // Vector3 or Prop - turns at a human-plausible rate
yield return bot.WalkTo(point);             // steer + hold forward until within tolerance
yield return bot.Jump();
yield return bot.Grab(prop);                // aims, clicks, fails loudly if the prop is not held
yield return bot.GrabAt(prop, worldPoint);  // the same, aiming at a point of the script's choosing
yield return bot.Click();                   // presses grab for one tick, whatever the view points at
yield return bot.RotateHeld(yawSteps, pitchSteps);
yield return bot.DropAt(point);             // aims so the view ray passes through point, releases
yield return bot.Drop();                    // releases where the prop is
yield return bot.Wait(seconds);
yield return bot.Until(() => condition, timeoutSeconds);
```

`new Bot(game)` installs itself as `game.Input`. `WalkTo(point, tolerance = 0.3, timeout = 10,
sprint = false)` walks in a straight line and measures arrival horizontally; it keeps steering while
airborne and eases off inside `Bot.SlowRadius` (0.6) times the player's scale of the target, so a
shrunken player meets tight tolerances and an enlarged one does not overshoot. `Jump()` keeps the previous
tick's movement keys down, so `WalkTo` followed by `Jump` is a running jump. The view turns at 360°/s.
A command that cannot succeed (timeout, the click took the wrong thing) throws a `BotException` whose
message names the bot's position and the target. Remember that the held prop lands where it first
touches something on the view ray, which is usually *short* of the point `DropAt` aims at.

- **Aiming follows what moves.** `LookAt`, `Grab` and `DropAt` lead their aim by how far the direction
  to the target wandered during the last tick, so they line up on a prop riding a conveyor and while the
  bot itself rides a platform.
- **`Grab` clicks as soon as a click would take the prop** (`grabber.FindTarget() == prop`), and it aims
  at a part of the prop the bot can see: the center if that is in view, else the middle of one of its
  colliders, else another spot on one. An arch, or a plank whose middle is behind a pillar, is taken
  like a person would take it.
- `RotateHeld` pitch steps may be negative: the pitch key only turns one way, so `-1` is three presses.
- **The bot cannot cheat.** `bot.Game` and `bot.Player` are for reading. `BotRunner` compares the
  simulation before and after every step of the script (the player, every prop, the level's state); a
  script that teleports, moves or scales something, unlocks an exit or completes the level itself ends
  with a `BotException`. So does a loop of commands that never lets a tick pass (10 000 in a row), and -
  in `Run` - a script that created commands it never ran: a `bot.Jump();` without `yield return` in
  front does nothing, and is reported instead of silently skipped.

In tests `BotRunner.Run(game, script, timeoutSeconds)` pumps the script and calls `game.Tick()` per
yield. In the build, the same script is paced by the real game loop (`?level=N&autoplay=1`), which gives
a visual replay of the solution: `BotRunner.Attach(game, bot, script)` makes the runner the game's input
source, so the script advances exactly one step per tick however the loop runs its ticks
(`game.Step(realDeltaTime)` returns the interpolation alpha). A script that fails is over: the runner
is `Finished` and `Failed`, `Error` says why, and the code after the failed command never runs.

## The room a level stands in (`Engine/Environment*.cs`)

Levels are authored around their own origin. The playroom is solved around the level when it loads
(`ART_BIBLE.md` §6.2, "the sun lands on the level"), and the result is **simulation-side data**, because
the room's colliders decide where a held toy stops and a headless bot test must see the same room as
the player. `Game` does this on every level load, **after** the level's `Build`:

1. `EnvironmentSolver.StaticBounds`: the bounds `B` of the level's static geometry — every enabled,
   non-trigger collider on the Default layer under `game.LevelRoot` without a Rigidbody.
2. `EnvironmentSolver.Solve(preset, B, level.GroundY, level.EnvironmentVisit)` → an immutable
   `EnvironmentDescriptor`, published as **`game.Environment`** (it is there when `LevelLoaded` arrives).
3. One `BoxCollider` per `descriptor.Boxes` entry, on the Default layer, under a root of their own
   (`game.Root` / "Environment" — not under `LevelRoot`). No renderers: `Render` builds everything
   visible from the same descriptor.

**Presets** (`EnvironmentPreset`): the six of §6.4 as data — `SunnyRug`, `BlockHall`,
`PegboardWorkbench`, `CardboardBox`, `HighShelf`, `NightLight` — each with its dip, patterns
(`PlayPattern`, `FloorPattern`, `WallPattern`, `BackWallPattern`), backdrop furniture, sun colour /
intensity / elevation / azimuth, window wall, haze density, patch colour and gain, pool gain, toy glow
gain, bloom, exposure, music key (`MusicRoot`, `MusicScale`, `Bpm`), island kind, floor drop and back
wall gap. `EnvironmentPreset.None` (key `"none"`) is no room at all: the sun is solved, nothing else
exists and no collider is made.

**Which level gets which room.** `LevelDefinition.Environment` defaults to the art bible's table for the
campaign levels 1–15, to `"sunny-rug"` for any other level with a `[Level]` attribute (the sandbox, a
showroom), and to `"none"` for ad-hoc levels without one — test fixtures keep their open space. An
unknown key is reported with a warning and gets `"none"`. `EnvironmentVisit` (how many earlier campaign
levels use the same preset) lowers the sun by 2° per visit, never below 38°, and swings it by 6° in
whichever direction keeps it within 30° of the window wall's outward direction and 30°–75° off the
travel axis (+Z); it keeps swinging the way it went last (`EnvironmentSolver.Sun`).

**`GroundY`** (`LevelDefinition.GroundY`, default 0) is the play plane: where the preset puts its floor,
rug, bench top or shelf top. A level with pits declares the bottom of the deepest one, or the room's
floor would close them; a level whose pit is a kill-plane drop puts `KillY` a little above `GroundY`
(the sandbox: `GroundY` −15, `KillY` −13) so nothing is seen to hit the ground.

**The descriptor** (`game.Environment`):

| Member | Meaning |
|---|---|
| `Preset`, `HasRoom`, `Visit` | the preset; false for `"none"`; the repeat-visit index |
| `LevelBounds`, `GroundY`, `Focus` | `B`; the play plane; `F` = the centre of `B` on the play plane |
| `SunElevation`, `SunAzimuth`, `SunDirection`, `SunRotation` | degrees (azimuth clockwise from +Z seen from above); the unit vector toward the sun; the rotation of a light shining from it |
| `WindowSide`, `Delta`, `WallDistance`, `WallClamped` | −1 / +1 for the −X / +X wall; the angle between sun and wall; `L` = 82.5 · cos Δ / tan e, at least `B`'s extent toward the wall + 20 |
| `WindowCenter`, `WindowNormal`, `WindowTangent`, `WindowHalfSize`, `WindowU`, `WindowV`, `WindowCenterHeight`, `WindowShift` | `_WinO`, `_WinN`, the unit tangent, (35, 52.5), `_WinU`, `_WinV`; 82.5 above the play plane unless the wall was pushed out (then it rises with the ray, capped at 120) |
| `ShellMin`, `ShellMax`, `FloorY` | the inside of the 400 × 170 × 400 shell: the window wall is `L` from `F`, the ceiling 170 above the play plane, the real floor `preset.FloorDrop` below it; elevated presets put the +Z wall 30 behind `B` |
| `GlintDir`, `GlintRight`, `GlintUp` | `_WinDir` and its basis |
| `Island`, `IslandBounds` | the rug (160 × 110, 0.5 thick, grown to the level + 20 and cut off at the walls), bench (wall to wall, 4 thick) or shelf (3 thick) whose top is the play plane |
| `Pieces` | the furniture that found room: `Kind`, `Index`, `Position`, `Rotation`, `FirstBox`, `BoxCount` |
| `Boxes`, `TryGetBox(kind, out box)` | every collider: `Floor`, `Ceiling`, `WallNegX`, `WallPosX`, `WallNegZ`, `WallPosZ`, `WindowPane`, `Island`, then `Furniture` boxes piece by piece — each `{ Kind, Name, Center, Size, Rotation, Piece }` |

The ray from `WindowCenter` along `-SunDirection` lands on `Focus`. The one exception: where the back
wall of an elevated preset stands too near for the opening (a short level), the window slides along its
wall by `WindowShift` and the patch with it.

**Furniture** has one to four boxes per piece, with the sizes of §6.3 (a chair's or a table's legs are a
piece of their own, `ChairLegs` / `TableLegs`). Pieces stand on the island where their middle is over
it, else on the shell's floor, or hang on the window wall or the back wall; each keeps
`EnvironmentSolver.KeepOut` (12) clear of the level's footprint and inside the shell, and a piece that
finds no room is left out — the same way every time. The visuals must fill exactly these boxes. The
window pane stands 0.05 proud of the wall's inner plane so that a ray at the window meets the pane.

## Art API (`Art/`)

Simulation-side helpers: pure data and procedural geometry, headless-safe and deterministic.

- **`Palette`** — every colour of `ART_BIBLE.md` §2 by name, sRGB as written: `Paper`, `Ink`, `Kraft`,
  `Birch`, `Steel`; the candy colours `Cherry` … `Bubblegum` (`Candy[]`, `CandyName`, `IsCandy`); the
  signals `Amber`, `Go`, `Hazard`, `Exit` (`Signal { Color, Gain, PulseMin, PulseHz, Striped, Emission,
  EmissionAt(gain), GainAt(t) }`); the dips `Mint`, `Butter`, `Pool`, `Peach`, `Lilac`, `Plum` (`Dip { Light, Mid, Deep,
  Haze, Hero, Banned, Night, Allows(candy) }`, `DipOf(presetKey)`). `Palette.Lin(hex)` /
  `Lin(Color)` convert to linear once (the exact sRGB curve); `Hex`, `ToHex`, `Srgb`, `Mix`, `Same`.
  **A signal's emission is `signal.EmissionAt(gain)`, never colour times gain**: the picture has no
  tonemapping curve, so only the largest channel may exceed 1 (it feeds the bloom) and the others keep
  their ratio - otherwise Amber clips to lemon and Go to cyan.
- **`MeshKit`** — meshes at their real size: `Box(size)`, `RoundedBox(size, bevel, segments = 2)`,
  `Cylinder(radius, height, sides = 24, bevel = 0, bevelSegments = 2)`, `Sphere(radius, segments, rings)`,
  `Lathe(profile, segments, smoothAngle)`, `Extrude(shape2D, depth, bevel, smoothAngle)`,
  `Wedge(width, height, length, bevel)`, `Grid(size, cellsX, cellsZ)`, and `Merge(name, parts)` (one mesh
  per material is the draw-call budget; `MeshPart { Mesh, Matrix, Color }`). Every mesh has unit
  normals, triangles that face along them, bounds, **object-space UVs with one repeat per unit**
  (`TEXCOORD0`) and the **outline normal** in `TEXCOORD3`. `MeshKit.Cached(key, build)` /
  `MeshKit.Key(name, numbers...)` share identical meshes; `UnitCylinderHull` / `UnitWedgeHull` are the
  collision hulls the simulation was tuned with.
- **`MeshUtil`** — `BakeOutlineNormals(mesh)` (for every vertex the average of the distinct normals at
  its position; every toy mesh passes through it), `ObjectSpaceUVs(mesh)` (the UV rule for a mesh made
  elsewhere), `SetColor`, `TriangleCount`.
- **Recipes** are values (equal numbers, equal recipe, one shared material; `recipe.With(r => ...)`
  copies). `ToyRecipe` holds every per-material property of `Toybox/ToyLit` (§3.2) plus what the rest
  of the game needs (`Pool`, `PoolTint`, `Squash`, `Sound`), with the rows of §4.3 and §4.4 ready-made:
  `GlossyPlastic`, `PaintedWood`, `Rubber`, `BrushedMetal`, `Glass`, `Felt`, `Cardboard`, `PaperSheet`,
  `TapeStrip`, `Sponge`, `Feather`, `PlainProp`, `GadgetBody`, `GadgetSignal`, `GadgetMetal`, `Water`,
  `Lamp`. `RoomRecipe` (`Top`, `Side`, `Dado`, `DadoY`, `Pattern`, `Corner`, `Cull`;
  `RoomRecipe.For(RoomSurface, dip, pattern, groundY)`, `RoomRecipe.Solid(colour)`) is `Toybox/RoomLit`
  (§3.3) with `PatternSpec` / `RoomPattern` for `_Pattern` and `_PatternA`; `FlatRecipe` (`Color`,
  `Shape`, `Soft`, `Blend`, `ZWrite`, `ZTest`, `Cull`, `QueueOffset`) is `Toybox/Flat` (§3.5).
- **`Materials`** — the one place materials come from; one shared `Material` per parameter set:

  ```csharp
  Material   Materials.Toy(ToyRecipe recipe, Color candy);
  Material[] Materials.ToySet(ToyRecipe recipe, Color candy);       // + the glass back shell on Medium / High
  Material   Materials.Emissive(ToyRecipe recipe, Color color, Color linearHdrEmission);
  Material   Materials.Gadget(GadgetPart part);                      // Body (Ink satin) | Metal (Steel)
  Material   Materials.Gadget(Signal signal[, float gain]);          // signal element
  Material   Materials.Water(Dip dip = null);
  Material   Materials.Room(RoomSurface surface, Dip dip = null, PatternSpec pattern = default, float groundY = 0);
  Material   Materials.Room(RoomRecipe recipe);
  Material   Materials.Flat(Color linearHdr, FlatShape shape = Quad, FlatBlend blend = Opaque, float soft = 0);
  Material   Materials.Flat(FlatRecipe recipe);
  void       Materials.SetEmission(Renderer renderer, Color linearHdr);   // per renderer: a pulsing signal
  bool Plain;  QualityTier Tier;  Dip Dip;  int Count;
  ```

  A material is a clone of a template under `Resources/Materials` — `ToyLit.mat`, `RoomLit.mat`,
  `Flat.mat` — with every property of §3.2 / §3.3 / §3.5 set **by name** from the recipe, so a shader
  takes effect the moment its setup step points the template at it. Toy materials get their detail
  texture from **`TexCache`** (nine procedural textures of §4.2, deterministic, 128² on Low; the orange
  peel only on High; `TexCache.WarmNext(tier)` makes one per call and is called once a frame in play
  until all exist). Colours are pushed as
  linear vectors; **the game's shaders must declare colour properties as `Vector`, not `Color`** — Unity
  converts a `Color` property from sRGB whichever setter is used (measured), which would darken it a
  second time. A template that is missing or does not carry a `Toybox/...` shader gets a stand-in: URP
  Lit with the recipe's base colour, smoothness, metallic and emission. `Materials.Plain` (the
  `?plain=1` look) always does that, from `PlainLit.mat`. `Materials.Dip` is the dip of the level being
  built (`Game` sets it before `Build`; `ctx.Dip` is the same thing), which is what `Materials.Room`
  uses when it is not given one. `Materials.Tier` follows `PresentationContext.Quality` (existing
  materials have their `_DetailBump` updated). `Materials.OverrideTemplate(name, material)` swaps a
  template for tests and tools; `ArtTests` uses it to pin down every property name written.
- **`ToyInfo`** — a passive tag on a toy's root: `Recipe`, `Candy`, `PoolProxies` (up to three spheres in
  the toy's own space). A toy factory calls `ToyInfo.Tag(toy, recipe, candy, proxies...)`; render and
  audio read it with `ToyInfo.Of(prop.GameObject)`.
- `Toys/BasicToys` builds on these: `Block`, `Ball`, `Cylinder`, `Wedge` are glossy-plastic toys in a
  candy colour with bevelled meshes at real size; `Slab(size[, colour | material])` and
  `Ramp(length, height, width)` are room surfaces in the dip of the level being built.

## Toy catalog (`Toys/`)

Every toy of `LEVELS.md` section 3 is a factory in `ToyFactory` (a GameObject at scale 1 with renderers,
colliders - boxes, spheres and convex hulls only - and a `ToyInfo` tag; no Rigidbody) and an entry in
`ToyCatalog` with its physical properties. A level adds one in a line:

```csharp
Prop thimble = ToyCatalog.Add(ctx, ToyId.Thimble, new Vector3(0f, 1.42f, -4.5f), 0.8f, options: o => o.Tags = new[] { "plug" });
ctx.AddStatic(ToyFactory.ThreadSpool(0.4f, 1.1f, grabbable: false), new Vector3(0f, 0.55f, -4.5f));   // a set piece
```

```csharp
enum ToyId { WoodenBlock, CheeseWedge, Thimble, Apple, Domino, Feather, Eraser, Pebble, Marble, Plank, GiftBox, PlayingCard, Key,
             ThreadSpool, Sponge, Doorway, BouncyBall, DeskFan, CatapultRuler, DominoSet, Baseball, Marker, TrainEngine, TrainCar }

ToyCatalog: All; Get(ToyId); Find(slugOrName); HeroOf(levelId 1..15); Build(ToyId, Color? = null); Options(ToyId, scale = 1);
            Prop Add(ctx, ToyId, position[, rotation], scale = 1, Color? color = null, Action<PropOptions> options = null)
ToyDef:     Id, Name, Slug, Size, Recipe, Color, Friction, Bounciness, MinScale, MaxScale, GrabPose, KeepUpright, AllowPitch, Grabbable, Body;
            measured: Volume, Center, HalfExtents, Radius, RestHeight (origin above the floor), Mass (at scale 1), Density, Triangles, Draws;
            Build(Color?), Options(scale), ColorIn(Dip)
ToySilhouette: Mask(ToyId, size = 128) (coverage, for the UI), Texture(ToyId, size)
```

- `ToyCatalog.Add` places the toy's **origin** at the position; `def.RestHeight * scale` is how far above
  a floor the origin rests. It substitutes the room's hero candy where the toy's own colour is the room's
  banned one (a Lemon cheese in the Butter room comes out Grape); pass `color` to decide yourself.
- Mass at scale 1 is the catalog's (`PropOptions.Density` = catalog mass / measured collider volume).
- The last four ids are set pieces (Birch `PlainProp`, not grabbable); `WoodenBlock`, `ThreadSpool`, `Plank`,
  `Ruler`, `Domino` and `DeskFan` take `grabbable: false` for the same look. Details are Paper or Ink only.
- Factories take sizes where LEVELS varies them: `WoodenBlock(size)`, `Plank(size)`, `Ruler(size)`,
  `ThreadSpool(radius, height)`, `DominoPiece(height, ...)`, `Marker(radius, length)`. `ToyFactory` also has
  the numbers gadgets need (`DominoSetHinge(i)`, `CatapultMarble`, `DeskFanBlades`, `TrainDeckThickness`, ...).
- `BasicToys.Block / Ball / Cylinder / Wedge` remain for test geometry: glossy plastic in a candy colour.
- A toy is at most 3 draws and 3,000 triangles; `Level99Showroom` (`?level=99`, Phase 0) shows all of them
  and its Solve is a timed tour (`Level99Showroom.LookTime(ToyId)`).

## Gadgets (`Gadgets/`)

The 26 gadgets of `LEVELS.md` section 2. Each is a plain C# class created in `Build` from the
`LevelContext` and an options object, ticked through `ctx.OnUpdate` in the order it was created (so create
them in feed order: a plate before the door it opens), with in-tick C# events and tick-end mirrors on
`game.Events`. Visuals are an Ink body, Steel for moving metal and exactly one signal element
(`SignalLamp`: Amber pulsing while it waits, Go steady once satisfied, Hazard for what hurts).

```csharp
var plate = new PressurePlate(ctx, new PressurePlateOptions { Sensor = Zone.Cylinder(4f, 9f, 0.9f, -0.2f, 1.2f), MinMass = 1f });
var door  = new Door(ctx, new DoorOptions { Size = new Vector3(3f, 4f, 0.4f), ClosedPosition = ..., OpenPosition = ... });
plate.OnPressed += prop => door.Open();   plate.OnReleased += () => door.Close();
```

```
abstract Gadget(ctx, name) { Name; Enabled; Age (ticks); Seconds; Disposed; virtual Reset(); protected abstract Tick(dt) }
Zone: Box(center, size[, rotation]) | MinMax(min, max) | Sphere(center, r) | Cylinder(x, z, r, yMin, yMax); Contains, ContainsXZ, ClosestPoint, Grown
SkyCap.Add(ctx, xMin, xMax, zMin, zMax, y, thickness = 1)                              an invisible lid for levels with open sky
HazardZone { Shape, Delay 0.1, OnCaught } -> PlayerInside, Catches; PlayerCaught [HazardCaught]
PropLeash { Props | Tag, Allowed[], Forbidden[], Grace 2, RestSpeed, Unless, Action Respawn|Eject, Port, OnReturn } [LeashReturned]
PressurePlate { Sensor, Mode Sum|Heaviest, MinMass, AcceptPlayer, AcceptTag, Settle, SettleSpeed, Latch, Debounce, LockProp, LockSeconds, LockPoint, Visual, Travel }
      -> Pressed, Load, Load01, PressedBy, LockedProp, Signal, Accepts(mass); OnPressed(prop), OnReleased, OnRejected [PlatePressed, PlateReleased, PlateRejected]
Breakable { Center, Size, Rotation, AcceptTag, MinMass, MinSpeed, Direction, Skin, History, OnBreak RemoveCollider|SwapCollider, FlattenedHeight, Body }
      -> Broken, BrokenBy, BreakSpeed, Watch(prop); Broke, Bonked [BreakableBroke, BreakableBonked]
Socket { AcceptTag, Capture, MinScale, MaxScale, YawAxis, YawReference, YawTolerance, MaxSpeed 3, SeatPose(scale), SeatScale(scale), EaseSeconds,
         ThenFallTo, ThenTurnDegrees / Axis / Seconds, LockOnSeat, AirCapture, Lamp }
      -> Seated, Seating, SeatedProp, Fit(scale | prop), HeadingFits, Release(velocity); OnSeated, OnUnseated, OnRejected [SocketSeated, SocketUnseated, SocketRejected]
Door { Panel | Size, ClosedPosition / Rotation, OpenPosition / Rotation, Motion Slide|Hinge, HingePivot / Axis / Angle, Seconds, Ease, StartsOpen }
      -> Open(), Close(), IsOpen, IsClosed, Moving, Waiting (crush guard), Openness, Mover [DoorStarted, DoorOpened, DoorClosed]
Funnel.Build(ctx, FunnelOptions { Axis, RimY, MouthRadius, ThroatRadius, ConeDepth, TubeLength, ChamberHeight, ChamberRadius, OuterRadius, OuterBaseY,
         CollarHalfSize, Segments 32, HardGate true, Friction 0.05, Material }) -> FunnelResult { GameObject, Footprint, ChamberBox, PlateCenter, ThroatY, FloorY }
ReturnPort { Mouth, EjectVelocity, PlayerPosition, PlayerYaw, RetrySeconds } -> Eject(prop, delay), Eject(player) [PortEjected]
FitGauge { Target (Socket) | MinScale..MaxScale, Near, Tag, Lamp } -> State, Scale, Fit(scale) [GaugeChanged]
WindStream { Center, Size, Rotation, Direction, Strength, PlayerAirPush 3, PlayerGroundPush 0, PropDrag 12, MaxPropAcceleration 40, MaxPropMass 1, PropLift 0.9 }
      -> Strength {get;set}, Contains, PlayerInside, PropAcceleration(scale, mass) [WindChanged]
SailRaft { Prop, Stream, Launch, Area, LiftPressure, MoorAbove, RiderMass, BoardDelay, CruiseSpeed, Acceleration, CruiseHeight, Landing, AlignDistance }
      -> State (Loose|Moored|Stall|Glide|Docked), Capacity(s), Carries(s), RiderAboard [SailMoored, SailStalled, SailLaunched, SailDocked]
BouncePad { Prop | Surface + Scale, GainPerScale 1.75, MinImpact 3, MinNormalY 0.9, Cooldown 0.1 } -> BounceCount, LaunchSpeed(s), Apex(s) [Bounced]
Seesaw { Pivot, ArmADirection | Axis, ArmA, ArmB, Width, Thickness, TipTaper, Plank (prop), RestSide, PlankBias, RiderBias, TipMargin, PadHeight, ReturnRate, FloorY, ProjectileArm, SeatArm }
      -> State (Rest|Swing|Tipped|Return), Angle, F(M, m), LaunchSpeed(f, x), PointAt(...) [SeesawStruck, SeesawLaunched, SeesawReturned, SeesawProjectile]
Train { Center, Radius 14, DeckY, AngularSpeed 24, StartBearing, EngineArc, CarArc, Cars 8, StationBearing, BedHeight, DeckSize, EngineFactory, CarFactory } : ICarrierBeds
      -> EngineBearing, CarBearing(i), CarMover(i), BedBox(i), PoseAt(bearing), LapSeconds, DeckSpeed [TrainAtStation]
PropCarrier { Beds (ICarrierBeds), AcceptTag, FlatDot, MinRadius, MaxRadius, RadiusCenter, EaseSeconds } -> Captured, CarIndex, CarriedProp, Release [CarrierCaptured, CarrierDropped]
FloatPlatform { Platform | Size, Position (x, z), Volume, RestY, MaxY, MaxSpeed 3, Acceleration 30, Freeboard, TopOffset } -> TopY, AtRest, AtTop, Mover [FloatLeft, FloatArrived]
PathDrive { Body (prop) | Mover, Segments, Offset, EndVelocity, GhostToPlayer } -> Start(), Abort(), Running, Segment, Duration, PointAt(t) [PathSegmentEnded, PathFinished]
      PathSegment.Roll(to, acceleration, startSpeed, from) | Ballistic(velocity, untilY, from) | Ease(to, seconds) | Spline(points, seconds) | Hold(seconds)
HingeChain { Prop, Pieces, PiecesChild, PieceFactory, StartAngle, ContactSin, RestAngle, Target, TargetRadius, ResetSeconds }; HingeChain.ForDominoSet(prop)
      -> Start(), Reset(), ResetNow(), State, Fallen, Struck, Reaches(), Angle(i), PieceMover(i) [ChainPieceFell, ChainTargetStruck, ChainFellShort]
NestedSet { Props (outermost first) } -> RevealedCount, IsRevealed(i) [NestRevealed]
LaserRain { Center, Size, Direction, Up, Range, LatticePitch 0.45, ZapDelay 0.1, Lane, OnZapped, Visual, BeamWidth } -> Exposed, Zaps, Coverage01, BeamCount [LaserZapped, LaserAllClear]
WaterVolume { Footprint, FloorY, Area, Volume, MaxSurfaceY, OverflowTo, LeakTo, LeakRate, WadeDepth 0.8, SweepDelay 0.3, OnSwept, Visual }
      -> Add, Take, Set, Volume, Depth, SurfaceY, Capacity, Over(point) [WaterLevelChanged, WaterOverflowing, WaterSwept]
Sponge { Prop, Volumes, CapacityPerScale3 0.315, AbsorbRate 150, SqueezeRate 24 } -> Stored, Capacity, Saturation01, EmptyInto(volume) [SpongeSoaking, SpongeWringing, SpongeFull, SpongeDry]
PortalDoorway { Prop, MinPlayerScale 0.3, MaxPlayerScale 3.2, Cooldown 0.5, SettleSpeed 30, Opening, FrameWidth, FrameDepth }
      -> Settled, Active, TargetScale, ViewRatio, Threshold, OpeningSize, ViewEye(eye), Crossings [PortalSettled, PortalCrossed, PortalBlocked]
RecallPad { Position, Radius 0.6, Prop, HoldSeconds 0.5, Clearance } -> PlayerOn, Progress01, Recalls [Recalled]
MachineDirector { Start (plate), Toys, Links (names, chain order), Resets (gadgets), FizzleTimeout 2.5, ResetSeconds 1.5, Messages }
      -> Begin(), Report(link), Fail(message), State (Idle|Running|Resetting|Done) [MachineStarted, MachineFizzled, MachineReset, MachineDone]
```

Names in brackets are the mirrors on `game.Events`. What a level author has to know:

- Sensors (`PressurePlate`, `Socket`, `HazardZone`, `PropLeash`, `FitGauge`) test prop **centres** against a
  `Zone` in `Prop.Id` order; they do not use `Trigger`s. A held prop is sensed by nothing.
- `Funnel` keeps `HardGate` on: without it a ball about 5% too big squeezes through.
- `Seesaw` given a `Plank` prop needs that prop already lying in the rest pose. `PropCarrier` is created
  after its `Train`. `WindStream.PropLift` makes anything under `MaxPropMass` nearly weightless in the stream.
- `prop.BeginDrive()` can return null (held, removed): gadgets check it, and `prop.Driven` every tick.
- Not built (LEVELS lists them as fallbacks only): HingeChain's single-piece mode, `LaunchSeat`, `FunnelCapture`.
- Gadget sounds are bound by name (`Audio/GadgetSounds.Named`): add a line there for a new mirror.

## Presentation (`Platform`, `Render`, `UI`, `Audio`)

Everything here sits on top of the simulation and can be replaced without touching it. The scene's
`Bootstrap` creates one `GameRunner`; the runner creates the rest.

### The presenter seam (`Platform/Presenter.cs`)

Whatever shows or sounds the game is a **presenter**: a class with a parameterless constructor that
implements `IPresenter` and carries `[Presenter(order)]`. Presenters are found by reflection in the
game assembly; **nobody registers anything, and nobody edits `GameRunner`, `Shots` or `PlayCheck` to add
one** — all three create a `Presentation` and call its `Frame`.

```csharp
public interface IPresenter : IDisposable {
    void Attach(Game game, PresentationContext context);   // once; subscribe to game.Events here
    void Frame(float dt, float alpha);                     // once per rendered frame, after the camera was placed
}                                                           // Dispose: unsubscribe, destroy your objects, restore globals

[Presenter(120, ProvidesLook = true)]
public sealed class LightingRig : IPresenter { ... }
```

- **Order.** Attach and Frame run in ascending order (ties by type name), Dispose in reverse. Suggested
  ranges: 0–99 pipeline and quality (the tier is decided before anyone reads it), 100–199 lighting and
  the room, 200–299 toys, held look and pools, 300–399 audio, 500–599 HUD and menus. `PlainLook` is
  −1000 and `DebugHudPresenter` 1000.
- **Levels** reach a presenter through `game.Events`: `LevelLoaded` (read `game.Environment` and
  `game.Level` there), `LevelUnloading`, and the rest. Attach may happen while a level is already
  loaded (Shots does that): treat it like a `LevelLoaded`.
- **Objects** a presenter creates go under `context.Root` — a child of `game.Root`, so they are in the
  simulation's scene, follow it through level loads, and are what the camera renders outside Play
  Mode. Colliders must never be added there: they would join the simulation.
- **The rule.** `ProvidesLook` marks a presenter that is part of the game's look (lighting, room,
  post-processing, the sticker pass); `ProvidesHud` one that is the HUD and menus. `Fallback` marks a
  stand-in. With the plain look asked for (`?plain=1`, `-toyboxPlain`), presenters that provide look or
  HUD are left out and the fallbacks run; otherwise a fallback runs only while no other presenter
  provides the same thing (`PresenterRegistry.Select`). Presenters that provide neither (audio) always
  run. `presentation.LookProvided` / `HudProvided` say which case it is.
- **Failures.** A presenter that throws in `Attach` is reported and left out; one that throws in `Frame`
  is reported once, disposed and retired. The game goes on.

`PresentationContext` is what presenters work with:

| Member | |
|---|---|
| `Game`, `Camera`, `Rig`, `Root` | the game, its camera (the rig's), the camera rig, the presentation root |
| `HasGraphics` | false in a headless run (`-nographics`): make no textures, render nothing |
| `Plain` | the debug look was asked for |
| `Quality`, `QualityChanged` | the tier in force (`Art.QualityTier` Low / Medium / High). The pipeline's presenter **sets** it (starting tier, governor, the settings menu); everyone else reads it. It starts on the player's manual choice, or Medium for Auto |
| `UnscaledTime`, `DeltaTime`, `FrameCount` | real seconds of presentation (the sum of every frame's `dt`), this frame's `dt`, frames so far |
| `Flow`, `State` | the `GameFlow`, or null where there is none (Shots) — `State` is then `Playing` |
| `Autoplay`, `AutoplayError`, `PointerLocked`, `LookHeld` | set by the driver every frame |
| `EscapeClaimed` | set by a menu while Esc is its own (the settings card over the pause card): the runner then does not take Esc as "resume" |
| `Presentation` | `Get<T>()` finds another active presenter |

`Presentation.Create(game, new PresentationOptions { Plain, Flow, Presenters, Quality })` creates the
camera rig and the root, picks the active presenters and attaches them. `Presentation.Frame(dt, alpha)`
places the camera (`Rig.Apply(alpha)`), then runs the presenters; `dt` is the real frame time, long
frames included (a negative or NaN one counts as 0).

**`Render/CameraRig`** is camera placement and nothing else: the main camera at
`player.EyeAt(alpha)` with `player.LookRotation`, the field of view from `Settings.FieldOfView`
(default 70° vertical), near plane 0.05 and far plane 700, both scaled with the player. Background,
lighting and post-processing belong to presenters, which get the camera through the context.

Frames and ticks do not line up, and only two things are made to look right in between: the camera,
which is interpolated relative to whatever the player stands on (so a platform they ride is steady under
it), and the held prop, which `Apply` draws on the camera's crosshair through `grabber.Present` (so it
does not trail a turning view). Other props and movers are drawn at their last tick pose; at display
rates other than 60 Hz they move in tick-sized steps against an interpolated camera.

### The presenters of the game

| Order | Presenter | Provides | What it does |
|---|---|---|---|
| −1000 | `Render/PlainLook` | look, fallback | One directional light with soft shadows, gradient ambient and a solid background, for stock URP Lit materials. Runs with `?plain=1`, or while nobody else provides look |
| 10 | `Render/QualityPresenter` | — | The tier (§7.3, §7.4): Quality level, URP asset and render scale from the pixel budget; the starting tier and the governor (Play Mode and the build only); `Settings.Quality`; the §12 counters. **Sets** `context.Quality` |
| 20 | `Render/PostLook` | look | The post chain (§7.2): camera post flags, the global Volume (an instance of `Resources/Volumes/ToyboxPost`), bloom and exposure per room and tier, the MacroBand lens blur (`Settings.LensBlur`), the pause's full-screen blur (`FullBlur`, `FullBlurReached`), the release flash (+0.12 EV, 90 ms) and the level-complete flash (+1.5 EV, 300 ms) |
| 100 | `Render/LightingRig` | look | The one Light (the sun) and every preset-load global of §3.7: ambient, kicker, glint basis, StudioEnv bands, toy glow, haze, window patch, shell box; ambient mirrored into `RenderSettings`; the camera cleared to the haze colour |
| 110 | `Render/Environment/RoomVisuals` | look | The room from `game.Environment`: shell, island, trim, window and sky card, the 21 furniture kinds filling their collider boxes, hull shadows, light shaft and dust motes (Medium / High), the player's shadow figure |
| 210 | `Render/PoolSystem` | look | Colour pools (§9.4): the per-frame globals `_PoolPos` / `_PoolTint` / `_PoolCount` / `_PoolGain`, grab drain, release flood, the splash ring (`_Splash`, `_SplashTint`), first-impact dust |
| 215 | `Render/DimensionCallout` | — | §9.4: after a release at another size (more than 15% off), a dimension line of the toy's true height, the factor and a 1.7-unit figure stand beside it for 1.5 s, in Ink, in the world |
| 220 | `Render/ExitMarks` | — | The four-pane mark at every exit: white × 3 while open, small faint Paper while locked; it faces the camera and thins out as the eye walks into it |
| 250 | `Render/HeldLook` | — | Everything about a held toy that is not the render pass: shadow off, border pop, peel slide and breathing, jump flash (writes `StickerLook` for `StickerFeature`); the focus sweep (`_Sweep`); the "every toy sweeps" of level complete; high-visibility rim; toys too thin to cast (`ThinCaster`) cast no shadow; detail-texture warm-up |
| 252 | `Render/RimFade` | — | §2.5 rule 4: the rim of a toy that stops being grabbable fades in 300 ms (its pool goes in `PoolSystem`) |
| 255 | `Render/ToySquash` | — | §9.5 impact squash (`_SquashA`) on the first impact after a release |
| 260 | `Render/ImpactShake` | — | §9.5 camera shake on the first impact after a release: `clamp(log10(mass) × 0.05, 0, 0.25)` units for 180 ms, none below mass 1, off with Reduce Motion. The camera is then at the eye plus `Offset` |
| 300 | `Audio/AudioPresenter` | — | Every sound (§11): events and flow to effects and music; runs with the plain look too |
| 500 | `UI/MenuPresenter` | HUD | Title, pause card, settings, the Catalogue, level complete; their buttons call the flow |
| 510 | `UI/HudPresenter` | HUD | Reticle, scale pill, hint toasts, level card, control pills |
| 1000 | `UI/DebugHudPresenter` | HUD, fallback | The IMGUI overlay of M1 (`DebugHud`), with the plain look |

`HeldLook`, `RimFade`, `ToySquash`, `DimensionCallout`, `ImpactShake` and `ExitMarks` are part of the look
but not marked `ProvidesLook`: they bring no light, so their presence must not retire the plain look's
sun. `HeldLook`, `RimFade`, `ToySquash` and `DimensionCallout` check `context.Plain` and do nothing then.
A presenter that needs objects makes them at attach and puts them away (the menus, the callout), so
showing them later creates nothing - `PlatformTests` counts objects across a session.

**Who sets which global.** `LightingRig`: `_AmbSky`, `_AmbEquator`, `_AmbGround`, `_KickDir`, `_KickColor`,
`_WinDir`, `_WinRight`, `_WinUp`, `_GlintColor`, `_EnvCeil`, `_EnvWall`, `_EnvFloor`, `_ToyGlowGain`,
`_HazeColor`, `_HazeParams`, `_WinO`, `_WinU`, `_WinV`, `_WinN`, `_WinMullion`, `_PatchColor`, `_ShellMin`,
`_ShellMax`. `PoolSystem`: the pool and splash globals. `StickerFeature` (inside its pass): `_ToyHeld`,
`_ToyHeldScale`, `_StickerPx`, `_StickerColor`, `_PeelColor`. `HeldLook`: `_ToyRimBoost`. `PostLook` (per
camera): `_RadiusPx`, `_Taps`, `_Full`. Without a lighting rig (a test, a tool) `ToyLookStandIn` fills the
toy-look globals in so toys are not unlit.

**Per-renderer values share one property block.** `_Emission` (`Materials.SetEmission`), `_Sweep`, `_SquashA`,
`_Rim` (RimFade) and `_Color` (ExitMarks) are written through `MaterialPropertyBlock`s; whoever writes one
reads the renderer's block first, so they do not clobber each other.

### The render pipeline (`Editor/Setup/PipelineSetup`, `Render/Quality.cs`)

- One shared `ToyboxRenderer.asset` (Forward, no depth priming, intermediate texture Always, D24_S8, the
  opaque and transparent layer masks without `Held`) with two features in this order: the stock
  full-screen pass **MacroBand** (`MacroBand.mat`, Before Rendering Post Processing) and **Sticker**
  (`StickerFeature`, `BeforeRenderingPostProcessing + 1`, holding `Sticker.mat`). `ToyShadingSetup`
  (order 300+) adds the sticker and takes `Held` out of the masks; a setup run limited to `PipelineSetup`
  must be followed by it.
- Three URP assets `ToyboxURP_Low / _Medium / _High` from one table (`TierSpec.Low / Medium / High`,
  `TierSpec.Of(tier)`), on exactly three Quality levels, all enabled for WebGL, default Medium. Everyone
  reads tier numbers from `TierSpec.Of(context.Quality)` (`PoolSpheres`, `DustMotes`, `DetailSize`,
  `GlassBackShell`, `Confetti`, `Msaa`, ...).
- `Resources/ToyboxVariants.shadervariants` lists the five shaders with each tier's shadow keywords;
  `ShaderWarmup.Run()` warms it once when the presentation starts in Play Mode. After adding or renaming a
  shader run `PipelineSetup.WriteShaderVariants` (or the whole setup).
- The sticker pass (§8): clears depth and stencil, draws the `Held` layer with the border, the peel shadow
  and its own materials. It runs whenever a Game holds a prop, also in the plain look (there without
  border). The homothety of §8.3 is always on and lives in the shader (`ToyWorldToHClip`).

### HUD and menus (`UI/`)

UGUI + TextMeshPro built from code: one procedural 256² atlas (`UiAtlas`), `Sticker` (shadow, border,
face), `UiTween` (stick, peel, press, hover; fades with Reduce Motion), `UiRoot` (a canvas at 1920 × 1080
matching height), `UiFonts` (the two baked fonts and three material presets under `Resources/Fonts`, the
TMP default as a fallback). `UiControls` has `UiButton`, `UiSlider`, `UiToggle`, `UiChoice` and `UiScreen`
(focus, Move / Submit / Cancel); `UiInput` is the EventSystem with code-made Input System actions.

- `HudPresenter`: `Reticle`, `Readout` (the scale pill of §9.6; `Readout.Jumps` counts surface jumps),
  `Hints` (toasts from `Message` events, for the message's own seconds), `LevelCard`, `Controls`.
- `MenuPresenter`: `Title`, `Pause`, `SettingsCard`, `Catalogue`, `Complete`, `Backdrop`, `Stage` (the
  title's turntable toy). Button actions are queued and run in `Frame` (after the runner read the frame's
  keys). Tests drive it without an EventSystem: `Move`, `Submit`, `Cancel`, `control.Activate()`.
- **`MenuBackdrop`** owns the camera while a menu is up. Pause: the frame as it was goes onto a card; the
  camera keeps running under the wash until `PostLook.FullBlurReached`, then one blurred picture becomes
  the background and the camera is disabled. Level complete: the camera renders into the card's texture
  (the scene stays live in it). Catalogue: camera off. While the game camera does not render to the
  screen a second camera, "Menu Screen Camera" (culling mask 0, no post), carries the overlay canvases.
  **Every texture the game camera is pointed at is B10G11R11** (`MenuBackdrop.NewTexture`) - an 8-bit one
  would switch HDR off, and with it bloom and glints, for as long as the card shows.
- The level-complete card presses Next itself after `CompleteMenu.AutoNextSeconds` (5 s) unless the
  player moves the focus; the flow's own `AutoAdvance` is off while the menus exist. The white flash of
  §9.7 is `PostLook`'s exposure flash; the card's Paper overlay only stands in where there is no `PostLook`.
- Esc on the settings card goes back to the pause card (`context.EscapeClaimed`), not on to the game.
- Menus are heard: `UiSounds.Hover()` when the focus moves or a slider steps, `UiSounds.Click()` when a
  control is activated (through `AudioPresenter.Active.PlayUi`).
- **Not seen on a real screen yet**: the batch editor has no Game view. Pause, catalogue and level
  complete (the stand-in screen camera, the camera redirect), mouse and keyboard through the EventSystem
  and pointer lock after a menu button are the first things to check in a WebGL build.

### Audio (`Audio/`)

Every sound is synthesised into float arrays (`Synth`, `SoundRecipes`) and wrapped with `AudioClip.Create`
in `SoundBank` (time-sliced, 4 ms a frame; 43 clips, about 7.7 MB). `AudioPresenter`:

- `static AudioPresenter Active`; `Unlocked` / `Unlock()` - nothing sounds before the first gesture (it
  unlocks itself on the first click, key or touch); `PlayUi(UiSound.Hover | Click)`; `Play(SoundId, volume,
  pitch)`, `PlayAt(SoundId, position, volume, pitch)`; `KeyPitch(writtenTonic)`; `Output`, `Bank`, `Music`,
  `Score`, `Key`.
- Size-to-pitch (`PitchLaw`): `-6 × log2(true diameter)` semitones, clamped to ±24, snapped to the room's
  pentatonic. Landings come from `PropImpact` and `ToyInfo.Recipe.Sound`.
- Music (`MusicScore`, `MusicBox`): a pure function of the level id and the room's key and tempo
  (`EnvironmentPreset.MusicRoot / MusicScale / Bpm`), scheduled with `PlayScheduled` 200 ms ahead.
- Gadgets: `GadgetSounds` binds by name to `game.Events` - `...Pressed` / `...Released` are the button,
  and `GadgetSounds.Named` maps the other mirrors to clips of the bank. The listeners are created by
  reflection over the payload struct; under IL2CPP that has not run yet (a failure is caught and logged).
- Outside Play Mode there are no AudioSources: `AudioOutput` keeps a logical account (`Count(id)`,
  `Played`) that tests read. **Nobody has listened to any of it yet.**

### Game flow, progress and settings (`Platform`)

**`GameFlow`** is where the player is in the game: `FlowState` `Title`, `Playing`, `Paused`,
`LevelSelect`, `LevelComplete`. It owns which level is loaded and keeps the player's progress; it
decides nothing about input or pictures. Every method is safe in any state and returns false where it
makes no sense.

```csharp
bool ShowTitle(int backdropLevel = -1);   // Title, with a level standing still behind it
bool StartLevel(int id);                  // any state -> Playing (loads; a level that fails to build is reported)
bool Restart();   bool NextLevel();   bool PreviousLevel();          // any state -> Playing
bool Pause();     bool Resume();                                     // Playing <-> Paused
bool ReturnToTitle();   bool OpenLevelSelect();   bool CloseLevelSelect();
void Update(float dt);                    // the LevelComplete countdown
event Action<FlowChange> StateChanged;    // { From, To }, after the change
FlowState State;  int LevelId;  LevelList Levels;  Progress Progress;  int ContinueLevel;
bool AutoAdvance = true;  float AdvanceDelay = 2.5f;  bool AdvancePending;  float AdvanceIn;
LevelEvent LastCompletion;  bool LastCompletionWasBest;  bool RecordProgress;  bool ForceAutoAdvance;
```

- `LevelCompleted` puts the flow into `LevelComplete` and records the completion. After `AdvanceDelay`
  it starts the next level by itself — unless a menu turned `AutoAdvance` off to wait for its "Next"
  button (`flow.NextLevel()`). While a bot plays, the runner forces the countdown and turns
  `RecordProgress` off: a bot's completion is not the player's.
- **`Progress`** (completed levels, best times, the level played last) is kept in an `IPrefStore`:
  `PlayerPrefsStore` in the game, a `MemoryStore` in tests and editor tools (`PrefStores.Default` picks
  by `Application.isPlaying`; `RunnerOptions.Store` injects one). `IsCompleted(id)`, `BestTime(id)`,
  `Completions(id)`, `LastPlayed`, `RecordCompletion(id, seconds)`, `IsUnlocked(id, levels)` (completed,
  the first of the campaign, or the level after a completed one), `Reset(ids)`, `Changed`.
- **`LevelList`** is every level that can be loaded (`Ids`, `Has`, `Resolve(launch)`) and the
  **campaign** play moves through (`Campaign`, `First`, `Last`, `After`, `Before`, `InCampaign`). From
  the registry the campaign is the levels with `Phase > 0`; Phase 0 levels (the sandbox, a showroom) are
  only reached by asking for them (`?level=0`, `?level=sandbox`), do not move the bookmark and record
  no progress. While no level has a phase, the campaign is all of them.

**`Settings`** is the player's settings (§10.5) as a static store with change events, kept in the same
kind of store: `Quality` (`Auto` / `Low` / `Medium` / `High`; `ForcedTier`), `LensBlur` (0..1, default
0.6), `MouseSensitivity` (degrees per pixel, default 0.1), `FieldOfView` (default 70), `MasterVolume`,
`MusicVolume`, `SfxVolume`, `ReduceMotion`, `HighVisibility`. Values are clamped; `Settings.Changed`
(`Action<Setting>`) is raised only when a value really changes; `Save()` writes through;
`ResetToDefaults()`; `Settings.Use(store)` switches stores (tests). Whoever applies a setting reads it
and listens; the menu only writes. `HumanInput` reads the sensitivity and `CameraRig` the field of view.

**`Platform/GameRunner`** owns the `Game` in Play Mode and in the build. It is a MonoBehaviour, but Unity's
`Start` / `Update` / `OnDestroy` only forward to `Begin(launch, options)`, `Frame(realDeltaTime)` and
`Shutdown()`, which take time and devices as arguments — so EditMode tests drive the very same code.
`Begin` creates the `Game`, the `HumanInput`, the `GameFlow` and the `Presentation`. `Frame` polls input,
turns keys and clicks into calls on the flow, calls `game.Step` **only while the state is `Playing`**
(with the frame time cut to `MaxFrameTime`, 0.1 s: after a hidden tab the game pauses instead of
fast-forwarding), runs the flow's countdown and presents.

- The page address picks the start (`LaunchOptions.FromEnvironment()`): `?level=N` (an id, or a slug
  such as `?level=sandbox`), `&autoplay=1`, `&plain=1`. **A launch that names a level or asks for
  autoplay starts playing at once; otherwise the game opens on the title**, with the level to continue
  with standing still behind it. Unknown levels fall back to the first. (`RunnerOptions.SkipTitle`
  starts playing regardless; tests of play use it.)
- **While not `Playing` the simulation does not tick, the mouse does not turn the view, and the pointer
  is free.** Esc — or a pointer the browser took away — pauses; entering `Playing` from a menu asks for
  the pointer again. A capture only counts as taken away if it had been held for
  `GameRunner.PointerHoldTime` (0.25 s): a browser that refuses the lock reports it for a frame or two,
  and play then goes on with the right-button look instead of pausing for ever.
- **Who gets the clicks.** While the game's own HUD is there (`presentation.HudProvided`), clicks
  outside `Playing` belong to its menus: they neither capture the mouse nor start anything, and the
  menus call the flow. While the debug HUD stands in, a click on the title starts the level and a click
  while paused resumes. Either way Esc toggles the pause - except while a menu has set
  `context.EscapeClaimed` (the settings card over the pause card: Esc closes that card) - and R restarts
  from `Paused` and `LevelComplete`.
- Autoplay runs `game.Level.Solve(bot)` through `BotRunner`, paced by the game's own ticks, and carries
  on from level to level. A script that throws is reported (`runner.AutoplayError`, a HUD message) and
  stops steering; it never takes the game loop down.
- Debug keys: `[` / `]` previous / next level, `P` toggles autoplay (turning it on restarts the level and
  plays it from wherever the flow was, turning it off hands the level over as it is).

**`Platform/HumanInput`** is the human `IInputSource`. It polls keyboard and mouse through an
`IInputDevices` (`UnityInputDevices` reads `Keyboard.current` / `Mouse.current` and tolerates their
absence; tests use a fake), once per rendered frame in `Update(player)`:

- Frames and ticks do not line up. Every press is **latched** in `Update` and handed to exactly one tick
  by `Sample()`, whether the frame runs no tick or several; held keys are repeated for every tick. A jump
  tapped between two ticks still reaches one. Wheel notches are paid out one yaw step per tick.
- **Mouse look bypasses `InputFrame`**: it is applied with `player.AddLook` in `Update`, at the display
  rate, scaled by `Settings.MouseSensitivity`. (The bot's look does go through `InputFrame`.) The runner
  passes `null` while a bot plays and while the flow is not `Playing`.
- **Pointer lock.** A click captures the mouse (`Cursor.lockState`) and is not a grab — while
  `CaptureOnClick` is on, which the runner turns off where a menu wants the clicks; `Esc` releases it.
  While it is free the mouse does not turn the view, and holding the right mouse button looks around —
  the fallback where a browser refuses the lock. The keyboard, `E` included, works either way. The lock
  is always *read back*, never remembered: a browser may refuse it or take it away (it handles `Esc`
  itself; the page never sees that key). In the Web build `UnityInputDevices` therefore turns
  `WebGLInput.stickyCursorLock` off, without which `Cursor.lockState` would keep saying "locked". The
  cursor is hidden exactly while the lock is held and shown again when it is lost.
- Losing focus clears held keys and every press that has not reached a tick.

Keys: `WASD` / arrows move, `Space` jump, `Shift` sprint, click or `E` grab / drop, `Q` and the mouse
wheel yaw the held prop, `F` pitches it, `R` restarts the level, `Esc` pauses.

The Web build is made with **exception support "Full Without Stacktrace"** (`CoreSetup.ConfigurePlayer`).
Levels and solve scripts are code, and the runner's promises - a level that fails to build is reported, a
solve script that goes wrong stops steering, a faulty event listener does not stop the others - are kept
with catch blocks. With "explicitly thrown exceptions only" a null reference in level code would not be an
exception at all in the browser and would stop the page.

## Setup steps (`Editor/Setup`)

`ProjectSetup.Run` holds no configuration of its own: it finds every static, parameterless method marked
**`[SetupStep(order)]`** in the `Toybox.Editor` assembly and runs them in ascending order (ties by type
and method name), logging `[Toybox] setup: <Type.Method>` before each. A step that throws is reported
and the rest still run; the run then fails as a whole. `-toyboxSteps "RoomSetup,Shadows"` limits a run to
steps whose name contains one of the texts; `ProjectSetup.List` prints them.

- **Every step is idempotent**: running it twice leaves the project as running it once does. Create only
  what is missing; write a value only where it differs.
- **Every area keeps its steps in a file of its own**, `Editor/Setup/<Area>Setup.cs`, and never edits
  another area's. Orders 0–99 are `CoreSetup`'s (folders, player settings, layers, physics, a pipeline
  asset to start from with four shadow cascades over 80 units and soft shadows, the template materials
  `ToyLit.mat` and `PlainLit.mat`, the bootstrap scene).
  Areas use 100 and up — pipeline 100–199, room 200–299, toy shading 300–399, UI 400–499 — and a later
  step may replace what an earlier one assigned (the pipeline's tier assets replace the starting asset).
- `SetupUtil` has the shared helpers: `EnsureFolder(path)`; `LoadOrCreate<T>(path, create[, out
  created])`; `LoadOrCreateMaterial(path, shaderName, repoint = true)` (throws with the shader's name if
  it does not exist or does not compile); `SetField(object, "m_Field", value)` for settings without a
  public setter — it returns whether anything changed and **throws, naming the object, the field and the
  fields with a similar name, if there is no such field**; `Field(serializedObject, path)`;
  `ProjectSettingsAsset(path)`; `EnsureRendererFeature<T>(rendererData, name)` and
  `OrderRendererFeatures(rendererData, "MacroBand", "Sticker")` for the one renderer asset two areas add
  features to.
- A shader only ships in a player build if a material asset under `Resources` (or a serialized reference
  from an asset in the build) points at it: each of the game's shaders gets its template material from
  its owner's setup step. `Shader.Find` alone is never enough.

## Areas, their files and the seams between them

| Area | Files |
|---|---|
| **Engine** | `Runtime/Engine/**`, `Runtime/Platform/**`, `Runtime/Bootstrap.cs`, `Runtime/Art/**` (palette, meshes, recipes, `Materials`, `TexCache`), `Editor/ProjectSetup.cs`, `Editor/Setup/CoreSetup.cs`, `SetupUtil.cs`, `Editor/Shots.cs`, `LookShots.cs`, `PlayCheck.cs` |
| **Toy shading** | `Shaders/ToyboxCommon.hlsl`, `ToyLit.shader`, `Sticker.shader`; `Render/StickerFeature.cs`, `HeldLook*.cs`, `RimFade.cs`, `ImpactShake.cs`; `Editor/Setup/ToyShadingSetup.cs` |
| **Room** | `Shaders/RoomLit.shader`, `RoomLitLib.hlsl`, `Flat.shader`; `Render/Environment/**`, `LightingRig.cs`, `PoolSystem.cs`, `ExitMarks.cs`, `DimensionCallout.cs`; `Editor/Setup/RoomSetup.cs` |
| **Pipeline** | `Shaders/MacroBand.shader`; `Render/Quality.cs`, `PostLook.cs`, `ShaderWarmup.cs`; `Editor/Setup/PipelineSetup.cs` |
| **Toys** | `Runtime/Toys/**`, `Runtime/Levels/Level99Showroom.cs` |
| **UI** | `Runtime/UI/**`, `Editor/Setup/UiSetup.cs` |
| **Audio** | `Runtime/Audio/**` |
| **Gadgets** | `Runtime/Gadgets/**` |
| **Levels** | `Runtime/Levels/**`, `Tests/EditMode/Levels/**` |

Seams between the areas that no single file shows:

- **The `Held` layer and the sticker pass belong together.** The renderer's opaque and transparent layer
  masks lose layer 10 in `ToyShadingSetup`, in the same step that adds `StickerFeature` — not in
  `PipelineSetup`. With the masks changed and no sticker pass a held toy would not be drawn at all.
- **Renderer features** go through `SetupUtil.EnsureRendererFeature` and every step that adds one ends
  with `SetupUtil.OrderRendererFeatures(renderer, "MacroBand", "Sticker")`, whichever area ran first.
- **The quality tier** is set by the pipeline's presenter through `context.Quality`; everyone else
  listens to `context.QualityChanged`. `Materials.Tier` follows it by itself.
- **Per-toy data** for pools, squash and landing sounds comes from `ToyInfo` on the prop's GameObject,
  left there by the toy factory. Anything grabbable needs the tag for its pool; a long toy without proxy
  spheres gets three along its length (`PoolSystem.LongAspect`).
- **Materials' contract**: `Materials.Toy / Room / Flat` clone `Resources/Materials/ToyLit.mat / RoomLit.mat /
  Flat.mat` and set the properties of §3.2 / §3.3 / §3.5 by name; the shaders declare colours as `Vector`.
- **A camera target is HDR** wherever the game camera is redirected (menus, tools): see `MenuBackdrop`
  and `Shots.Photograph`.
- **Test fixtures** keep working with any set of presenters: a test that depends on the stand-ins asks
  for the plain look (`?plain=1`, `PresentationOptions.Plain`), one that needs particular presenters
  passes them (`RunnerOptions.Presenters`, `ShotRequest.Presenters`).

## Where the game departs from the art bible

Decided while putting the areas together and looking at the pictures; each has its reason next to the
code. (The areas' own deviations are in `tools/out/notes/m2-*.md`.)

| Art bible | The game | Why |
|---|---|---|
| §7.2 Tonemapping Neutral | `PostLook.Tonemap` = None | URP's Neutral curve shows the pastels 13% too dark and greyer at any exposure (§5.1 asks for ±4%) |
| §6.4 exposure +0.2 EV | plus `PostLook.ExposureTrim` −0.2 EV | The +0.2 was written for the Neutral curve. Without a curve it put every sunlit light-tone top in the window patch above 1: level floors and toy tops clipped to white |
| §3.2 Fresnel terms on N·V | `ToyLit`: a flat face is shaded as if seen at N·V ≥ 0.6 (`TOYBOX_FLAT_NDV`), and takes 20% of the window glint | From a figure's eye height a flat top is at a grazing angle all over: coat, StudioEnv and rim turned it white from end to end and the candy colour - the "you can lift this" signal - was gone. Flatness is the screen-space change of the normal, the same at every hold distance; bevels, balls and silhouettes are unaffected |
| §3.3 pools: `albedo × glow`, facing term `nl` | Halo with `sqrt(nl)`, 70% in the toy's own hue, 30% strength on what is level with or above the toy, all halos together capped at 0.45; ring × 1.5 (`RoomLitLib.hlsl`, mirrored in `PoolSystem.OwnHue / HaloCap / RingBoost`) | Each room's hero candy is the complement of its dip: a multiplied Cherry pool on a Mint rug was invisible. Floors see a toy at a low angle and got nothing, walls beside it got everything - a bridged plank tinted every wall of the sandbox orange |
| §2.3 signal = colour × HDR gain | `Signal.EmissionAt(gain)`: only the largest channel exceeds 1 | Without a tonemapping curve Amber × 2.2 clipped to lemon and Go × 2.6 to cyan. The idle pulse now also shows in the lamp's core (dimmer at its low), so it reads without bloom |
| §4.4 exit portal (no owner) | `Render/ExitMarks`: alpha-blended, small and faint while locked | Nobody drew exits. Additive white would vanish against a pale wall; a locked exit must not depend on hue alone |
| §5.3 every toy casts | A toy thinner than 0.07 (true size) casts nothing (`HeldLook.ThinCaster`) | Its shadow is narrower than two texels of the near cascade: a row of teeth beside a playing card |
| §2.5 rule 7 on the room only | `ToyLit` has the same Ink toe as `RoomLit` (`InkToe` in `ToyboxCommon.hlsl`) | Shaded details and unlit metal went to black in the night room |
| §4.1 smooth normals | `MeshKit.Extrude` / `Lathe` weight smooth normals by edge length | A flat side next to a rounded corner was shaded like a pillow (the cheese wedge's slope ran from Lemon to white) |
| §10.5 pause: blur, then capture | The card shows the sharp frame, the background the blurred one | One texture cannot be both; the frame "as it was" is what the card is for |
| §3.2 bump height | `TOYBOX_BUMP_UNIT` 0.07 authored units per unit of detail | 1.0 tilts normals by 80°; 0.1 still read as corrugation on painted wood |
| §7.4 HDR-less devices: clamp the glint gain | Not done | An 8-bit target clamps at 1 by itself; no device to try it on |

## Testing and verification

- `Tests/EditMode/*Tests.cs` — mechanic math and clamps (`PerspectiveTests`), controller behavior
  (`PlayerTests`), triggers, exits, level loading and global state (`FrameworkTests`), mass ratios
  (`StackingTests`), the bot (`BotTests`), bit-exact replay (`DeterminismTests`).
- The regression suites from the M1 review, each test a defect that was found and fixed or an attack
  that was withstood: `PlayerTerrainTests` (seams, crests, steep faces, steps, pebbles, movers, low
  ceilings), `PerspectiveHoldTests` (thin obstacles, aim assist, overhead drops, compound props, extreme
  scales, the cost of a hold tick), `LevelLifecycleTests` (a level written against the public API and
  solved by its bot, reloads, triggers and removed props, events around a level switch, replays) and
  `PlatformTests` (the held prop and a ridden platform against the camera at 75 and 144 Hz, pointer lock
  and cursor, a fuzzed session through `GameRunner`, the Web build's settings).
- `Tests/EditMode/TestHelpers.cs` — derive a fixture from `SimTest` (it disposes the Game after every
  test), build ad-hoc geometry with `Build(ctx => ...)` and `TestHelpers.Floor / Box / Room / Ramp`,
  steer with `ScriptedInput`, and run a level's own solution with `TestHelpers.PlayLevel(game)`.
  Every test must dispose its Game: only one may exist, and it owns global physics state.
- `Tests/EditMode/Levels/LevelNNTests.cs` — builds the level, runs `Solve(bot)`, asserts
  `LevelCompleted` fired before a timeout. A level without a passing solver is not done.
- `Tests/EditMode/PresentationTests.cs` — input latching across frames that run zero, one or three ticks,
  pointer lock and focus loss (`HumanInputTests`, through `FakeDevices`), URL parsing and level order
  (`LaunchOptionsTests`), the runner's level flow, autoplay and camera (`GameRunnerTests`, by calling
  `Begin` / `Frame` directly), and a real render of level 0 (`ShotsTests`; skipped under `-nographics`).
- The milestone 2 foundation: `ArtTests` (palette, MeshKit meshes — unit normals, outward triangles,
  enclosed volume, bounds, budgets, outline normals, object-space UVs —, recipes, one shared material per
  parameter set), `EngineAsksTests` (PropImpact and its determinism, the focus candidate, hold payloads,
  LevelUnloading, GrabPose, BeginDrive / EndDrive, FrozenUntilGrabbed, AllowPitch, KeepUpright, the bot's
  slow radius), `EnvironmentTests` (the solve's numbers for all six presets, repeat visits, the level
  table, GroundY, the promised colliders, a held toy landing on the room's wall in a headless test),
  `GameFlowTests` (transitions, progress through a `MemoryStore`, the campaign, the runner's title, pause
  and who gets the clicks), `SettingsTests` (defaults, ranges, change events, persistence),
  `PresenterTests` (discovery, the plain / fallback rule, lifecycle, the context, GameRunner and Shots
  driving the same presenters) and `SetupTests` (step discovery and order, `SetupUtil`).
- Milestone 2, one file per area: `ToyShadingTests` (the two shaders compile for WebGL, the detail
  textures, the sticker pass, acceptance test 8.3 for every recipe and tier), `RoomTests` (RoomLit / Flat,
  the lighting rig's numbers, the kit, pools, pictures measured against the art bible's formulas),
  `PipelineTests` (tier rules, the governor, setup idempotence, post-processing, pictures per tier),
  `ToyCatalogTests` (every toy builds, hulls, masses, grow-and-drop, the showroom tour), `UiTests` (the
  kit, HUD, menus through the real `GameRunner`, the capture), `AudioTests` (synthesis, every clip's
  numbers, the scheduler, the presenter) and `Gadgets/*Tests` (one file per gadget and a showroom that
  replays bit for bit).
- `IntegrationTests` is what only shows with everything together: every presenter of the game running
  through `GameRunner`, one flash on level complete, menus that are heard, Esc on the settings card, the
  plain look as the fallback, exit marks, the dimension callout, the impact shake, the rim fade, pools of
  long toys, a signal's hue, flat faces of generated meshes, gadget sounds, friction being its
  coefficient, the screenshot tool's HDR - and `BuildSurvivalTests`,
  which walks `ART_BIBLE.md` §3.8 and §7.5 item by item against the assets on disk.
- The test assembly references URP, TextMeshPro, UGUI and the Input System, so area tests can name
  their types. A test file that uses `Toybox.Engine.Volume` and imports `UnityEngine.Rendering` needs
  `using Volume = Toybox.Engine.Volume;`.
- `Toybox.EditorTools.Shots.Capture` renders PNGs of a level (optionally mid-solve) to `tools/out/shots`
  for visual review without making a WebGL build: it creates a Game like a test does, attaches the
  game's own `Presentation` (the camera rig and every active presenter, given one frame per tick) and
  lets the bot play. Look at the pictures; do not assume.
- `Toybox.EditorTools.PlayCheck.Run` (`tools\unity.ps1 playcheck`) is the smoke check of the real loop:
  it opens `Main.unity`, enters Play Mode, turns autoplay on, watches the bot solve the first level
  through `GameRunner.Update` (runtime simulation scene, real frame times), takes pictures through the
  game's camera, leaves Play Mode and writes `tools/out/playcheck/result.txt` (`PLAYCHECK: OK` or the
  problems it saw, including every error logged while playing, and which presenters were active). It
  runs on a memory store (`PrefStores.EditorMemoryKey`), so the progress and settings saved on the
  machine neither steer it nor are changed by it. Statics set in Play Mode outlive it in the editor
  (there is no script reload on the way out): anything cached in a static must not depend on what Play
  Mode happened to be doing - the tier, for one (`ToyDef` re-measures when the tier differs). It
  returns before the check has run, and the editor must not serve anything else while it is in Play
  Mode — the wrapper holds the batch mutex until the result file appears and passes `-toyboxExclusive`.
- Not covered by anything: the WebGL build - so §7.5 item 8 (a held glossy toy in the browser with bloom
  on its glint, the vignette, the border and the peel shadow) is still to be looked at -, real keyboard
  and mouse, the browser's pointer lock (including the `stickyCursorLock` line, which only the Web player
  compiles), contact modification and the reflection in `GadgetSounds` under IL2CPP, overlay canvases on
  a real screen (pause, catalogue and level complete with their stand-in camera), how any of the audio
  sounds, and the IMGUI drawing of `DebugHud` (a batch-mode editor never calls `OnGUI`). Check `Esc`,
  alt-tab and a quick re-click in a real browser after every change to `Platform/`.
- `.\tools\unity.ps1 test` must pass before any commit.

## Deploying

`tools/deploy.ps1` makes a WebGL build and publishes it to the `gh-pages` branch, which GitHub Pages
serves. Unity cannot be built on GitHub's hosted runners without license secrets, so builds are made
locally.

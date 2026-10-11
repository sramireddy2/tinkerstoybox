# Tinker's Toybox — Engine Architecture

This is the engineering contract for the game. Every module and every level is written against it.
If you need to deviate, change this document in the same commit.

> **Engine note.** The project started as a three.js/Rapier prototype and moved to **Unity** on
> 2026-10-03. Units, player metrics and the mechanic are unchanged. If instructions you were given
> mention three.js, Rapier, TypeScript or Vitest, translate them to the Unity equivalents below.

## Stack

- **Unity 6** (`6000.6.4f1`), **URP 17.6**, PhysX, Input System 1.20, UGUI + TextMeshPro, Unity Test Framework.
- Target: **WebGL**, deployed to GitHub Pages.
- No imported art or audio. Meshes, textures, shaders, sounds and music are generated in code. The typefaces
  are the exception: two of the game's own and TextMesh Pro's fallback (see "Layout").

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
sample input -> player -> grab / drop -> level + gadgets -> heavy props that come at the player are let through
             -> PhysicsScene.Simulate(dt)
             -> a throw by a heavy prop is taken back
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
- **Never end a loop on a float that is stepped by a computed remainder; count with an integer.** The
  WebGL build computes floats in strict 32-bit, the editor keeps a float local wider: a step that
  rounds to nothing there never ends, and the page freezes while every editor check passes. That is
  what `Level05Shapes.Dashed` did on load (it counts its dashes now); only `tools/webgl_check.py`
  saw it. This holds for presentation code too.

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
                             PlainLit and PlainLitEmissive for the plain look), Volumes/ToyboxPost,
                             ToyboxVariants, Fonts/ (two baked TMP fonts, three material presets), UI/Frame
  Fonts/                   Unbounded-Bold.ttf, Figtree-SemiBold.ttf and their licences (the only imported
                             assets of the game's own; TextMesh Pro brings a third typeface, below)
  Scenes/, Settings/       generated by setup steps: Main.unity; ToyboxRenderer + ToyboxURP_Low/_Medium/_High
                             (the core's starting asset, ToyboxURP.asset, exists only until the tier assets do)
Assets/TextMesh Pro/       TMP Essential Resources as Unity imports them: TMP's shaders and settings and its
                             fallback font, Liberation Sans (LiberationSans.ttf, a static and a dynamic
                             "SDF - Fallback" font asset, its licence). The font assets are under a Resources
                             folder, so that font file ships in every build beside the game's two baked atlases
Assets/WebGLTemplates/Toybox/   index.html (page shell and loading screen), licenses.txt (the three fonts'
                             copyright notices and the SIL Open Font License) and preview.png (the picture
                             a shared link shows), which Unity copies into every build beside the page
docs/                      this contract; LEVEL_AUTHORING.md (the level author's short reference); LEVELS.md;
                             ART_BIBLE.md; design-notes/ (historical drafts, the builders' close-out logs);
                             media/ (pictures)
tools/unity.ps1            batch-mode wrapper (compile | test | exec | playcheck | build | status | stop)
tools/typecheck.ps1        the three assemblies through Unity's own compiler, without Unity: seconds
tools/build-clone.ps1      a WebGL build from a private copy of the project; the test editor keeps running
tools/deploy.ps1           publishes a build to the gh-pages branch
tools/editor-check.ps1     plays levels in the real, windowed editor (on the private copy)
tools/webgl_check.py       plays a WebGL build in a real browser: freezes, frame rate, errors, sound, input
tools/out/                 what the tools and the builders wrote: logs, probe tables, pictures. Git-ignored,
                             so NOT in the repository
```

`Assets/Toybox/link.xml` preserves the whole `Toybox` assembly, because levels and presenters are found
by reflection.

**Paths under `tools/out` are the builders' local logs, probe tables and pictures; they are not in the
repository.** This document still names them where a number or a decision came from one, so that
whoever has the folder can find it. What a reader needs has been copied into `docs/`:
[LEVEL_AUTHORING.md](LEVEL_AUTHORING.md), the three close-out logs and the hold times in
[design-notes/](design-notes/README.md), and two contact sheets in [media/](media).

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

The other tools:

| Tool | What it does |
|---|---|
| `tools\typecheck.ps1` | Compiles `Toybox`, `Toybox.Editor` and `Toybox.Tests.EditMode` with the compiler that ships with Unity, from the reference and define lists Unity wrote on its last compile (`Library/Bee`): a few seconds, no lock, the same errors. `unity.ps1` runs it first and stops there if it fails. On a fresh clone it needs one `unity.ps1 compile` before it; so does an `.asmdef` that gains a reference |
| `tools\build-clone.ps1` | Mirrors `Assets`, `Packages`, `ProjectSettings` and the tool scripts into a private copy (`%USERPROFILE%\.cache\tinkerstoybox\buildclone`, with a `Library` of its own) and makes the WebGL build there, so a build never stops the editor that serves tests. `-Committed` builds HEAD from a side checkout instead of the working folder; `-Opt RuntimeSpeed` is the release setting (**its default, `BuildTimes`, is a faster build of a slower game**); `-SyncOnly` stops after the mirror; `-Deploy` publishes what it built |
| `tools\deploy.ps1` | Publishes `%USERPROFILE%\.cache\tinkerstoybox\Build\WebGL` as the single commit of `gh-pages`: with `-SkipBuild` the build that is there, otherwise after building (`RuntimeSpeed`) in the main folder, which stops the background editor |
| `tools\editor-check.ps1 -Level 1,2,3` | Opens the private copy in the real, windowed editor, enters Play Mode on each level through `PlayCheck.Run`, lets the bot solve it and closes the editor: a Game view and real frame times, which the batch editor does not have |
| `python tools/webgl_check.py` | Plays a served WebGL build in the installed Edge or Chrome, headless but on the real GPU (Playwright): see "Testing and verification" and "Releasing" |

**Pictures are what the game shows.** `Shots.Photograph` renders the camera into an HDR target of the
game's own colour format (B10G11R11) with the tier's sample count and encodes it afterwards - URP takes
the format of its intermediate target from the camera's target texture, and through an 8-bit one
nothing would exceed 1 and bloom would have nothing to work with. Every capture tool goes through it:

| Tool (`unity.ps1 exec -Method ...`) | What it is for |
|---|---|
| `Toybox.EditorTools.Shots.Capture` | A registered level, played by its bot |
| `... -UnityArgs '-toyboxUi','hud'` (any tool below too) | The same picture **with the UI**: `hud`, `toast`, `keys` (the HUD with the key caps of an empty hand), `title`, `pause`, `hints`, `settings`, `select`, `catalogue` (the fifteen levels with some progress), `complete`, `ending`. The canvases become world-space canvases on the camera, drawn by an overlay camera stacked on it (`UiCapture.OverlayFor`) - after post-processing, as the real overlay is |
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
  What the capsule does not collide with (`Physics.IgnoreCollision`: a prop that is passing through the
  player, a gadget part that would have crushed them) is not ground and not a face in the way either -
  the probes skip it, so a plank that slides through the player's feet does not carry them off.
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
    releases the ground. Less than that is swallowed. (A dynamic prop that outweighs the player lifts
    or launches them at up to `PerspectiveGrabber.CarrySpeed` = 6 only; one that comes up faster passes
    through them - "Props and the player's body". A kinematic `Mover` has no such limit.)
- **Steps.** The foot of the capsule rolls over a static lip up to about 0.1 high (a third of its
  radius) without leaving the ground; anything higher is a wall. Design steps accordingly, or use ramps.
- Ground velocity is inherited from kinematic `Mover`s and from dynamic bodies (point velocity). A turning
  mover reports the chord of its arc (`Mover.PointVelocity`), so a rider stays on a turntable. A dynamic
  prop heavier than the player carries them at up to 6 units·s⁻¹ (`CarrySpeed`); faster, it is not
  ground any more and goes on without them.
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

Its `Prepare` runs on the main thread as the last thing before the physics step. That is also where the
props that must not touch the capsule in this step at all are taken out of it
(`PerspectiveGrabber.BeforePhysics`, see "Props and the player's body" below): at that moment every
velocity of the tick is known - the player's, and whatever levels and gadgets did to the props. What
the step itself sets going and throws at the player in one go cannot be known then; that is looked at
right after the step, before the controller reads its result (`PerspectiveGrabber.AfterPhysics`).

## The perspective mechanic (`Engine/PerspectiveGrabber.cs`)

The spec formula is `NewScale = OldScale × (NewDistance / OldDistance)`. We implement it continuously.

**Grab.** Raycast from the eye along the view direction (range 150). If the first solid hit belongs to
a grabbable prop, take it. Otherwise a small aim-assist cone (2.5°) takes the grabbable prop whose
*colliders* come closest to the view ray, if it is in plain view there: it covers tiny targets and near
misses, not a crosshair that points well past the prop. Record
`k = prop.Scale / distance(eye, prop center)` - the object's apparent size, which stays constant for
the whole hold. A prop that is underfoot cannot be grabbed (`player.Underfoot`: the prop the feet are on
and, in the air, still the one they left the ground from - until the feet are on something else or the
level puts the player somewhere; `UnderfootTests`). A `Kinematic` prop is not grabbable
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
of 150. The scale it is given is clamped to those limits as well: stopped by its own limit the prop is
at `d = MaxScale / k`, and `k·(MaxScale / k)` is a hair over `MaxScale` in one pick-up of ten in 32-bit
arithmetic - enough for a gauge whose window ends at the limit to call a fitting toy too big.

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

**Props and the player's body.** Nothing crushes the player and nothing throws them. "Passing through"
below always means the same thing: the prop's colliders ignore the capsule (`Physics.IgnoreCollision`),
the prop goes on colliding with everything else, and the player's ground and foot probes do not see it.
Four rules switch it on, one switches it off; levels need no guard of their own. Together they hold
this: **no dynamic prop that outweighs the player ever moves them at 9 units·s⁻¹ or more** - not from
the side, not from above, not from under their feet (`HeavyPropTests`, `HeavyPropAttackTests`).

1. *Let go around the player.* A prop that overlaps the capsule at the moment of release passes through.
2. *Coming at the player* (`PerspectiveGrabber.BeforePhysics`, run right before every physics step). A
   dynamic prop that outweighs the player (mass > 3) and whose surface moves toward them faster than
   `PassSpeed` = 1 unit·s⁻¹ passes through - decided *before* it touches, as soon as the gap is no more
   than what the two close in 0.05 s plus 0.1. Sliding, rolling, falling, toppling, from any side, onto
   a player who stands, walks into it or is in the air: they are not touched (before the rule a heavy
   prop handed them its own speed, and an edge that caught the capsule against the floor squirted them
   out at four times that: 12 to 76 units·s⁻¹ measured).
   - What counts is the speed of the prop's own surface along the line to the capsule, at the point of
     the prop nearest the capsule and at the points nearest its foot and its head (a toppling slab's
     nearest part is not always the part that arrives first). **Walking into a heavy prop, or being
     carried into one that rests, never makes it give way**: the smaller of "speed in the world" and
     "speed relative to what the player stands on" is taken, so a heavy prop that rides the same
     platform is at rest too.
   - **What the player stands on is exempt** (`GroundProp`): it carries them. And where the prop is
     under the foot (a contact the player could stand on) only its sideways speed counts: a prop that
     comes up from below lifts or launches the player, as a seesaw does, and that is meant.
   - **…up to `CarrySpeed` = 6 units·s⁻¹** (less than the player's own jump, 7.6). A heavy prop under
     the foot - stood on, or about to be landed on - whose surface there moves faster than that,
     sliding, rolling or rising, is no ground: it passes through them and goes on without them (they
     drop to whatever is underneath). Before this limit a block knocked away under the player carried
     them off at up to 25, and the low end of a plank flung them upward at 37 when a block came down
     on its high end.
   - Slower than `PassSpeed` a heavy prop is solid and shoves the player. If it goes on shoving faster
     than `PushSpeed` = 0.3 for `PushSeconds` = 0.25, it passes through as well - a boulder rolling at
     walking pace moves the player a hand's width, not across the room.
   - `Kinematic`, `Fixed`, frozen, held and driven props are not this rule's business: a platform a
     gadget carries is the gadget's (crush guard, `GhostToPlayer`).
   - The speeds are per unit of player scale, like the player's own. Props at or below the player's
     mass never pass: they push the player and are pushed, by their momentum (half the player's mass
     at 25 gives them 8).
3. *Lying on the player.* A dynamic prop heavier than the player that has sunk more than 0.15 into the
   capsule from above or from the side without coming at them - let go just overhead, or put there by a
   respawn - passes through too (`LetHeavyPropsPass`, after the step). A prop the player lands *on* is
   not affected.
4. *Thrown all the same* (`PerspectiveGrabber.AfterPhysics` → `TakeBackThrows`, right after every step
   and before the controller reads its result). Rule 2 needs a prop that is already moving. A step can
   set a resting one going and hit the player with it at once: a block that rests against them is
   struck by another, the block they stand on is knocked away or tipped up under their feet. So: if
   the step left the player more than `PassSpeed` faster than they were going by themselves
   (`Player.ExpectedVelocity`: what the controller set, and gravity), moving at more than `CarrySpeed`
   the way they were pushed, and a heavy dynamic prop that is moving is at the capsule, the throw is
   **taken back** - `Player.TakeBack` puts them where, and as fast as, they would be had nothing
   touched them in that step - and the prop passes through from then on. Likewise what they stood on
   during the step and is now faster than `CarrySpeed` passes before the controller takes its speed
   for the ground's. A kinematic `Mover` that launches its rider is recognised (it moves that way
   itself) and left alone, also with a heavy toy riding beside the player.

*Solid again* (after every step): as soon as the prop is not held, does not overlap the capsule, and
is not coming at the player by rule 2. For a prop that fell on the player that means: it has come to
rest round them and they have walked out of it. A boulder that has rolled through them is solid again
behind them. A prop that slid away under them is solid again once it is no longer under their feet,
or slower than `CarrySpeed`.

Known limits: props at or below the player's mass are outside all of this (they push by their
momentum: 0.9 of the player's mass at 25 gives them 11.7). A loose heavy toy that rides a kinematic
platform faster than `CarrySpeed` cannot be stood on while it does (to the rule it is a prop moving at
that speed); toys a gadget carries (`PropCarrier`, `BeginDrive`) are kinematic and not affected. A
heavy ball that comes to rest across a corner can shut the player in behind it; they pick it up.

With the default `MaxScale` of 80 a prop held against the open sky reaches some 40 units and tens of
tonnes; levels with open sky should set `MaxScale`.

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
    public bool SquareOnGrab;           // on a grab the heading eases to the nearest quarter turn of the view
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
  upright slides but never tips. Combine it with `GrabPose.Upright`. **A toy that cannot tip hangs by
  its rim from any ledge, however small:** Level 12's spool stayed on a sill 0.015 deep and became a
  way across. Next to a pit or a door, give the level a rule for a toy at rest over the drop.
- `SquareOnGrab` (`prop.SquareOnGrab`): on a pick-up the toy's heading eases, with its `GrabPose` and
  over the same 0.15 s, to the nearest quarter turn of the view, so it is held square to the eye
  wherever the click landed. For toys whose heading decides the outcome: Level 11's card (four degrees
  askew misses a wall), Level 10's boxes, Level 14's door.

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
  signals `Amber`, `Go`, `Hazard`, `Exit`, `Laser` (`Signal { Color, Gain, PulseMin, PulseHz, Striped, Emission,
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
- **The Plank is painted wood** (the owner's brief for Level 9: "a wooden plank"; it used to be a flat
  Lemon slab that read as plastic). All of it is looks, inside the 3-draw budget: grain streaks in the
  body's own mesh, tinted a tone darker than the paint through vertex colour (`ToyGeo.Sliver`,
  `ToyFactory.PlankGrainTone`), heavier chips at the edges, and an "Ends" draw of bare `Palette.Birch`
  (`PlainProp`, the palette's raw wood) with growth rings on the two sawn ends; the Ink nails stay. They
  stand 0.0015 proud of the faces. Collider, size, mass and `RestHeight` are the box's, as before.
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
SailRaft { Prop, Stream, Launch, Area, LiftPressure, MoorAbove, RiderMass, BoardDelay, CruiseSpeed, Acceleration, CruiseHeight, Landing, AlignDistance, Settle 0, SettleSeconds 0.2 }
      -> State (Loose|Moored|Stall|Glide|Docked), Capacity(s), Carries(s), RiderAboard, RiderOverhead [SailMoored, SailStalled, SailLaunched, SailDocked]
BouncePad { Prop | Surface + Scale, GainPerScale 1.75, MinImpact 3, MinNormalY 0.9, Cooldown 0.1 } -> BounceCount, LaunchSpeed(s), Apex(s) [Bounced]
Seesaw { Pivot, ArmADirection | Axis, ArmA, ArmB, Width, Thickness, TipTaper, Plank (prop), RestSide, PlankBias, RiderBias, TipMargin, PadHeight, ReturnRate, FloorY, ProjectileArm, SeatArm }
      -> State (Rest|Swing|Tipped|Return), Angle, F(M, m), LaunchSpeed(f, x), PointAt(...), RiderOverhead; consts HoldLoad 0.5, ReleaseTicks 30, MinStrikeShare 0.1, OverheadReach 1.5
         [SeesawStruck, SeesawLaunched, SeesawReturned, SeesawProjectile]
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
WaterVolume { Footprint, FloorY, Area, Volume, MaxSurfaceY, OverflowTo, LeakTo, LeakRate, WadeDepth 0.8, SweepDelay 0.3, OnSwept, Visual, Dip (null: the room's) }
      -> Add, Take, Set, Volume, Depth, SurfaceY, Capacity, Over(point), Dip [WaterLevelChanged, WaterOverflowing, WaterSwept]
Sponge { Prop, Volumes, CapacityPerScale3 0.315, AbsorbRate 150, SqueezeRate 24 } -> Stored, Capacity, Saturation01, EmptyInto(volume) [SpongeSoaking, SpongeWringing, SpongeFull, SpongeDry]
PortalDoorway { Prop, MinPlayerScale 0.3, MaxPlayerScale 3.2, Cooldown 0.5, SettleSpeed 30, Opening, FrameWidth, FrameDepth }
      -> Settled, Active, TargetScale, ViewRatio, Threshold, OpeningSize, ViewEye(eye), Crossings; static Carried(velocity, ratio, back), FootSlack 0.1, MaxDrop 1
         [PortalSettled, PortalCrossed, PortalBlocked]
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
- **A rider who hops is still a rider.** A jump pressed just before landing goes off on the tick the
  feet touch down, so a gadget that only asks `GadgetKit.PlayerStandsOn` never sees such a rider
  standing. `SailRaft.RiderOverhead` and `Seesaw.RiderOverhead` are true while the player is in the
  air over the thing they rode, not under it. The raft does not let go of a rider who is overhead; the
  seesaw goes further, because its plank comes up faster than a jumper falls: instead of ghosting
  through them (the crush guard's answer to anybody else in the way of a swing) it gives them, tick by
  tick, the upward speed that keeps their feet on its top, and throws them with the rule's speed when
  the swing ends. Only a jump at the very end of the swing, which is higher and faster than the throw
  by the player's own doing, keeps its own speed.
- **A seesaw's swing is called off when what tipped it is picked up again** (`prop.Held` or `Removed`
  in mid-swing): the riders keep the speed the plank has given them so far and the plank swings back.
  A tipped plank whose weight was picked up, or that only the player held down, swings back at once;
  one whose weight is merely not touching it for a moment waits `ReleaseTicks`, and a swing with less
  than `MinStrikeShare` of the travel ahead of it raises no strike or launch cue: one let-go is one
  `SeesawStruck`.
- `SailRaft.Settle` presses a moored sail `Settle × scale` into the deck: a flat toy rests on its
  thickest part with its rim in the air, and moored (pinned) that rim is a lip nobody walks over.
- `Funnel`'s chamber floor is a sheet without thickness: a marble under about 0.35 coming down the tube
  at 11 units·s⁻¹ goes through it. Level 8 stands solid boxes under its lid and chamber floors and a
  net under those; anything else that drops small balls through a `Funnel` needs the same.
- **`PortalDoorway` carries a walk with the size and a jump with its root** (`Carried`): the velocity
  through the door is × `r` along the floor and × √`r` up and down, and a crossing that is turned back
  reverses only the horizontal part. Gravity does not scale with the player, so × `r` throughout made
  a hop into a growing door a leap of four to eight units (Level 14 was done in two trips that way). A
  door is entered feet first (`FootSlack`: not from the floor below it) and never out onto a spot with
  no floor within `MaxDrop` (the player is turned back, `PortalBlocked`).
- `WaterVolumeOptions.Dip` draws a volume's body and surface in a dip of its own (Level 13's bowl is
  pool blue in a peach room); `WaterVisuals` keeps ripple rings inside the footprint of the water they
  are on. `LaserVisuals` draws a fourth mesh per emitter, "Laser Glow": a pinpoint of beam light on the
  underside of whatever roofs a beam over.
- `LaserRain`'s hazard test looks at five points on top of the capsule, 0.24 apart, each only while it
  is inside the emitter's rectangle: a cover that begins less than 0.24 behind the rain's edge leaves
  exposures too short to zap at a walk (Level 11's lean-to rests on that).
- `Sponge`'s `CapacityPerScale3` and rates are the level's to set: Level 13 uses 0.48 and 200 (the
  defaults are the text's 0.315 and 150).
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
| 100 | `Render/LightingRig` | look | The one Light (the sun) and every preset-load global of §3.7: ambient, kicker, glint basis, StudioEnv bands, toy glow, haze, window patch, shell box; ambient mirrored into `RenderSettings`; the camera cleared to the haze colour In a night room the night turns to dawn when the room's machine is done (`Dawn`, `DawnSeconds`; Level 15) |
| 110 | `Render/Environment/RoomVisuals` | look | The room from `game.Environment`: shell, island, trim, window and sky card, the 21 furniture kinds filling their collider boxes, hull shadows, light shaft and dust motes (Medium / High), the player's shadow figure |
| 210 | `Render/PoolSystem` | look | Colour pools (§9.4): the per-frame globals `_PoolPos` / `_PoolTint` / `_PoolCount` / `_PoolGain`, grab drain, release flood, the splash ring (`_Splash`, `_SplashTint`), first-impact dust |
| 215 | `Render/DimensionCallout` | — | §9.4: after a release at another size (more than 15% off), a dimension line of the toy's true height, the factor and a 1.7-unit figure stand beside it for 1.5 s, in Ink, in the world. Nearer than 2.1 figure heights from the camera the figure is left out (it fades in up to 2.6) and the factor is written for that distance (`FigureAt`, `LabelSizeFor`): a toy made small is let go at arm's length |
| 220 | `Render/ExitMarks` | — | The four-pane mark at every exit: white × 3 while open, small faint Paper while locked; it faces the camera and thins out as the eye walks into it |
| 232–248 | `Render/GadgetVisuals/*` | look | What the gadgets' effects look like, one presenter per kind, all built on `GadgetVisual` (finds a level's gadgets through `GadgetList.Of`, rebuilds on level load and tier change): `LaserVisuals` (232), `WindVisuals` (234), `FitLamps` (236: amber waiting, green fits, red blinking wrong size; the gauge's read-out), `WaterVisuals` (238), `PortalView` (240: a second camera on Medium / High, a card on Low and while the door is held), `LaunchCues` (242), `TrainMarks` (244), `BreakDebris` (246), `GadgetCues` (248: the recall pad's wait ring and the accept / refuse / caught rings). `Flat` shader, no lights, no colliders; none of them draws on a held toy. (The builder's notes were `tools/out/notes/gadget-visuals.md`, a local file) |
| 250 | `Render/HeldLook` | — | Everything about a held toy that is not the render pass: shadow off, border pop, peel slide and breathing, jump flash (writes `StickerLook` for `StickerFeature`); the focus sweep (`_Sweep`); the "every toy sweeps" of level complete; high-visibility rim; toys too thin to cast (`ThinCaster`) cast no shadow; detail-texture warm-up |
| 252 | `Render/RimFade` | — | §2.5 rule 4: the rim of a toy that stops being grabbable fades in 300 ms (its pool goes in `PoolSystem`) |
| 255 | `Render/ToySquash` | — | §9.5 impact squash (`_SquashA`) on the first impact after a release |
| 260 | `Render/ImpactShake` | — | §9.5 camera shake on the first impact after a release: `clamp(log10(mass) × 0.05, 0, 0.25)` units for 180 ms, none below mass 1, off with Reduce Motion. The camera is then at the eye plus `Offset` |
| 300 | `Audio/AudioPresenter` | — | Every sound (§11): events and flow to effects and music; runs with the plain look too |
| 500 | `UI/MenuPresenter` | HUD | Title, pause card, settings, the Catalogue, level complete, the ending; their buttons call the flow |
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
- **The budget check** (`ART_BIBLE.md` §12.1): `SceneCensus.Take(game)` counts what is under `game.Root`
  before any culling - renderers, draws, shadow-caster draws per cascade, triangles, materials,
  particles - and `census.Over(tier)` says what is over a tier's budget. It is a count of **one view at
  one moment**: `QualityPresenter` takes it once, two frames after a level loaded, and logs
  `[Toybox] budget: ...` once per level and tier if it is over; a level whose toys arrive later (Level
  15's machine) peaks after that. A level with a `PortalDoorway` has a second camera on Medium and High
  (`PortalView`), which draws the level and casts its shadows again: there the check is of
  `census.WithSecondView()` (`SceneCensus.HasSecondView(game, tier)`). A test that measures a tier
  **sets `Materials.Tier` before it loads the level** (the game's presentation exists before the level
  does, and the toys' materials - the glass back shell, for one - are chosen while the level builds):
  `Level08Tests` / `Level13Tests.TheLevelWithItsRoom_IsWithinTheBudgetOfEveryTier`, `BudgetCensusTests`.
- **The governor and the display** (`QualityGovernor`, `ART_BIBLE.md` §7.4 rule 5): a session whose
  frames are long whatever is drawn (a 50 Hz display, a throttled browser, a dropped frame in ten) goes
  down the ladder like a weak GPU, is found out at the bottom - the window's median frame time is not
  10% shorter than when the descent began - and is given everything back (`GaveBack`, `Pace`,
  `Locked`). From then on a step down takes a full window whose mean is over 1.25 × that pace.
- **Tier changes are cheap**: `TexCache` makes the half-size twin of every detail texture with the
  full-size one and `Materials.Tier` trims nothing, so a step down generates no texture. `PortalView`
  rounds the piece of its texture it renders up to a short ladder of sizes (`PortalView.Steps`,
  `RenderPixels`), so the pipeline finds its camera targets instead of making new ones as the player
  moves, and gives its texture back in a level without a door. Presenters whose meshes keep their shape
  lay them out once and then only move them (`FxMesh.Move` / `Tint` / `ApplyMoved`: the laser lattice,
  the wind's ribbons).
- **The plain look's glow** (`?plain=1`): URP Lit's `_EMISSION` is a shader_feature, so its variant is
  only in a build if a material asset uses it. `Resources/Materials/PlainLitEmissive.mat`
  (`CoreSetup.CreateMaterials`) is that asset, and what `Materials` clones for anything that emits in
  the plain look (signal lamps, the stand-ins of the flat effects); `BuildSurvivalTests` holds it there.
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
- **Where the keys are named** (the game named none of them before the final review): `ControlPills`
  at the bottom right - while a toy is held "Q / wheel: turn", "F: flip", "click: let go"; while nothing
  is held "click: pick up" (with the reticle on a toy), "W A S D: move", "Space: jump" and "Esc: pause
  and hints". Each stops appearing once the player has used its key (`ControlPills.Limit`: three uses,
  one pause for Esc; kept in the player's store). **A use is a press or a gesture, never a tick**: a
  second of walking, a press of Space, one scroll of the wheel (`TurnGap`). The empty hand's pills are
  the player's alone: not while a bot plays, not in a tool without a flow (`-toyboxUi keys` shows
  them). The title has one row of the same key caps along its bottom edge (`TitleMenu.Keys`), and
  the pause card has the whole list beside it (`PauseMenu.Guide`, `PauseMenu.Keys`) under the level's
  number, title and blurb. None of the three takes the focus or a click.
- `MenuPresenter`: `Title`, `Pause`, `SettingsCard`, `Catalogue`, `Complete`, `Ending`, `Backdrop`, `Stage`
  (the title's turntable toy). Button actions are queued and run in `Frame` (after the runner read the frame's
  keys). Tests drive it without an EventSystem: `Move`, `Submit`, `Cancel`, `control.Activate()`.
- **Keys held over into a menu do nothing in it.** The UI's Navigate action binds W A S D as well as
  the arrows and the EventSystem repeats a held direction ten times a second: a screen that has just
  opened ignores every move (`UiScreen.MoveBlocked`) until `MenuPresenter` has seen a frame with no
  direction held (`UiInput.NavigateHeld`; tests set `MenuPresenter.NavigationHeld` / `SubmitHeld`).
  The complete card's and the ending's buttons are not `Interactable` before their result card is up.
  `CompleteMenu.GoOn` only acts in `LevelComplete` (two queued presses of Next are one level on), a
  press of Next ends the card's countdown at once, and Esc on the card stops the countdown (`Held`).
- A card that is reopened **from the catalogue** (`MenuPresenter.CameFrom`: Levels, then Back) opens
  as it was left - the complete card and the ending without a second flash, stamp, fanfare or confetti,
  the complete card held.
- `UiScreen.Move`: up and down go to the nearest control of the next row that can take the focus; in
  the catalogue (`UiScreen.GridRows`) left and right run on from one row of cards into the next.
- The pause card's Hints button shows the hint that was up once more on its first press after the card
  comes back (`PauseMenu.NextHint`), and only then goes on. The settings card has every setting the
  code has (invert look and the effects volume among them); while it is up the level's music plays
  softly behind it (`AudioPresenter.MusicPreview`).
- **What the menus' textures cost** (`MenuBackdrop`): the pause's frame is a still with the pixels the
  camera really renders (`StillSize`: the canvas times the render scale, rendered at scale 1); the
  blurred frame behind the pause card is half that each way (`BlurredSize`). The level-complete card's
  texture is still the canvas's size: the camera renders into it live. All of them keep a depth buffer
  nothing reads: URP's render graph does not render a camera into a texture without one (it warns, in
  Play Mode, "the output Render Texture must have a depth buffer").
- **`MenuBackdrop`** owns the camera while a menu is up. Pause: the frame as it was goes onto a card; the
  camera keeps running under the wash until `PostLook.FullBlurReached`, then one blurred picture becomes
  the background and the camera is disabled. Level complete: the camera renders into the card's texture
  (the scene stays live in it). Catalogue: camera off. While the game camera does not render to the
  screen a second camera, "Menu Screen Camera" (culling mask 0, no post), carries the overlay canvases.
  **Every texture the game camera is pointed at is B10G11R11** (`MenuBackdrop.NewTexture`) - an 8-bit one
  would switch HDR off, and with it bloom and glints, for as long as the card shows.
- The level-complete card presses Next itself after `CompleteMenu.AutoNextSeconds` (5 s) unless the
  player moves the focus or presses Esc; the flow's own `AutoAdvance` is off while the menus exist.
  Its pick-ups are the flow's (`flow.PickUps`: whoever played, the bot included, as the ending counts them). The white flash of
  §9.7 is `PostLook`'s exposure flash; the card's Paper overlay only stands in where there is no `PostLook`.
- **The ending** (`UI/EndingScreen.cs`, `EndingMenu`; `FlowState.Ending`; `ART_BIBLE.md` §9.8) is what the last level's
  complete card leads to: that card's Cherry pill says "Finish" (`CompleteMenu.FinishLabel`) and calls
  `flow.Advance()` like every other. On the catalogue's Paper surface (camera off): "THE END", the
  count ("15 OF 15 COLLECTED"), the campaign's blister cards sticking on one every 60 ms (`Entries`,
  `CardsShown`; a level that is not collected is a Kraft "?"), a "COLLECTED" stamp with the
  level-complete flourish and paper confetti when all are collected (UGUI pieces made up front; none
  with Reduce Motion), then the result card (`Info`): a thank-you line, `flow.Report()`'s total time
  and pick-ups, and `Again` ("Play again", the first level), `LevelsButton` (the catalogue; Back
  returns here), `TitleButton` (also Esc). The buttons are not interactable until the result card is
  up (`InfoAt`), so a key held over from the last card cannot skip the ending, and nothing moves on by
  itself. While a bot plays it reads "15 OF 15 PLAYED" / "THE BOT PLAYED IT THROUGH" and nothing is
  stamped; P there (the bot lets go) starts the first level for the player, as its line says. The
  player's figures are labelled "Best runs" (the best run of every level added up, not the time spent). `-toyboxUi ending` captures it with every registered campaign level collected.
- Esc on the settings card goes back to the pause card (`context.EscapeClaimed`), not on to the game.
- Menus are heard: `UiSounds.Hover()` when the focus moves or a slider steps, `UiSounds.Click()` when a
  control is activated, `UiSounds.Fanfare()` at the ending's stamp (through `AudioPresenter.Active.PlayUi`).
- **Seen on a real screen only through `tools/webgl_check.py` and `tools/editor-check.ps1`**: the batch
  editor has no Game view. `webgl_check.py --play` clicks "Click to play", has the mouse captured and
  presses `Esc` for the pause card in a real browser; the catalogue, the settings, level complete and
  the ending (the stand-in screen camera, the camera redirect), the keyboard through the EventSystem
  and pointer lock after a menu button are still looked at by hand in a WebGL build.

### Audio (`Audio/`)

Every sound is synthesised into float arrays (`Synth`, `SoundRecipes`) and wrapped with `AudioClip.Create`
in `SoundBank` (time-sliced, 4 ms a frame; 43 clips, about 7.7 MB). `AudioPresenter`:

- `static AudioPresenter Active`; `Unlocked` / `Unlock()` - nothing sounds before the first gesture (it
  unlocks itself on the first click, key or touch); `PlayUi(UiSound.Hover | Click | Fanfare)`; `Play(SoundId, volume,
  pitch)`, `PlayAt(SoundId, position, volume, pitch)`; `KeyPitch(writtenTonic)`; `Output`, `Bank`, `Music`,
  `Score`, `Key`.
- Size-to-pitch (`PitchLaw`): `-6 × log2(true diameter)` semitones, clamped to ±24, snapped to the room's
  pentatonic. Landings come from `PropImpact` and `ToyInfo.Recipe.Sound`.
- Music (`MusicScore`, `MusicBox`): a pure function of the level id and the room's key and tempo
  (`EnvironmentPreset.MusicRoot / MusicScale / Bpm`), scheduled with `PlayScheduled` 200 ms ahead.
- Gadgets: `GadgetSounds` binds by name to `game.Events` - `...Pressed` / `...Released` are the button,
  and `GadgetSounds.Named` maps the other mirrors to clips of the bank. The listeners are created by
  reflection over the payload struct; under IL2CPP a failure is caught and logged as a warning
  (`[Toybox] audio could not listen to GameEvents...`), which `webgl_check.py` echoes from the browser's
  console.
- Outside Play Mode there are no AudioSources: `AudioOutput` keeps a logical account (`Count(id)`,
  `Played`) that tests read. In a WebGL build `tools/webgl_check.py` meters what the page plays (audible
  or silent, peak, clipping). **No test can say how any of it sounds: that takes a person's ears.**

### Game flow, progress and settings (`Platform`)

**`GameFlow`** is where the player is in the game: `FlowState` `Title`, `Playing`, `Paused`,
`LevelSelect`, `LevelComplete`, `Ending`. It owns which level is loaded and keeps the player's progress; it
decides nothing about input or pictures. Every method is safe in any state and returns false where it
makes no sense.

```csharp
bool ShowTitle(int backdropLevel = -1);   // Title, with a level standing still behind it
bool StartLevel(int id);                  // any state -> Playing (loads; a level that fails to build is reported)
bool Restart();   bool NextLevel();   bool PreviousLevel();          // any state -> Playing; these two wrap round the campaign
bool Advance();                           // what follows a completion: the next level - or, after the last, ShowEnding()
bool ShowEnding();                        // any state -> Ending; the level stays loaded behind it
bool Pause();     bool Resume();                                     // Playing <-> Paused
bool ReturnToTitle();   bool OpenLevelSelect();   bool CloseLevelSelect();
void Update(float dt);                    // the LevelComplete countdown (calls Advance)
event Action<FlowChange> StateChanged;    // { From, To }, after the change
FlowState State;  int LevelId;  LevelList Levels;  Progress Progress;  int ContinueLevel;
bool AutoAdvance = true;  float AdvanceDelay = 2.5f;  bool AdvancePending;  float AdvanceIn;
LevelEvent LastCompletion;  bool LastCompletionWasBest;  bool RecordProgress;  bool ForceAutoAdvance;
int PickUps;  bool AtLastLevel;           // pick-ups since the load; the loaded level is the campaign's last
CampaignReport Report();                  // what the ending tells; static ReportOf(levels, progress)
```

- `LevelCompleted` puts the flow into `LevelComplete` and records the completion (time and pick-ups);
  the bookmark (`Progress.LastPlayed`) moves on to the level after it at once - a session that ends on
  the card continues with the next level - except after the campaign's last, whose bookmark stays until
  the ending puts it back to the first.
  After `AdvanceDelay` it moves on by itself — unless a menu turned `AutoAdvance` off to wait for its
  "Next" button (`flow.Advance()`). While a bot plays, the runner forces the countdown and turns
  `RecordProgress` off: a bot's completion is not the player's.
- **The campaign ends; it does not wrap round.** `Advance()` from the complete card of the campaign's
  last level is `ShowEnding()`: the state becomes `Ending`, nothing is loaded (the level stays behind
  it, standing still), and - when the player's progress is being recorded - the ending is counted
  (`Progress.Endings`) and the bookmark goes back to the first level, so a fresh game after the ending
  starts at the beginning with everything collected kept. From `Ending`: `StartLevel(Levels.First)`
  ("Play again"), `OpenLevelSelect()` (closing returns to `Ending`), `ReturnToTitle()` (which loads the
  first level behind the title); it cannot be paused and nothing moves on by itself. `NextLevel()` and
  `PreviousLevel()` still wrap: they are the level keys' steps, for looking at levels. A level outside
  the campaign, or the last level while it is not completed, has no ending: `Advance()` is `NextLevel()`.
- **`CampaignReport`** (`Levels`, `Collected[]`, `CollectedCount`, `Total`, `Full`, `Seconds`,
  `PickUps`, `Watched`): for the player, their saved progress - the best run of every completed level
  added up, over as many sessions as it took; while a bot plays (`Watched`), what was completed since
  the game began, by anybody, none of it kept.
- **`Progress`** (completed levels, best times and their pick-ups, the level played last, endings) is
  kept in an `IPrefStore`: `PlayerPrefsStore` in the game, a `MemoryStore` in tests and editor tools
  (`PrefStores.Default` picks by `Application.isPlaying`; `RunnerOptions.Store` injects one).
  `IsCompleted(id)`, `BestTime(id)`, `PickUps(id)` (of the best run; negative if not counted),
  `Completions(id)`, `LastPlayed`, `Endings`, `RecordCompletion(id, seconds, pickUps = -1)`,
  `RecordEnding()`, `TotalTime(ids)`, `TotalPickUps(ids)`, `IsUnlocked(id, levels)` (completed, the
  first of the campaign, or the level after a completed one), `Reset(ids)`, `Changed`. Keys are
  `toybox.progress.done.N / best.N / picks.N / last / ended`.
- **`LevelList`** is every level that can be loaded (`Ids`, `Has`, `Resolve(launch)`) and the
  **campaign** play moves through (`Campaign`, `First`, `Last`, `After`, `Before`, `InCampaign`). From
  the registry the campaign is the levels with `Phase > 0`; the three Phase 0 levels (0 the sandbox, 98
  the gadget gallery, 99 the toy showroom) are
  only reached by asking for them (`?level=0`, `?level=sandbox`), do not move the bookmark and record
  no progress. While no level has a phase, the campaign is all of them.

**`Settings`** is the player's settings (§10.5) as a static store with change events, kept in the same
kind of store: `Quality` (`Auto` / `Low` / `Medium` / `High`; `ForcedTier`), `LensBlur` (0..1, default
0.6), `MouseSensitivity` (degrees per pixel, default 0.1), `FieldOfView` (default 70), `MasterVolume`,
`MusicVolume`, `SfxVolume`, `ReduceMotion`, `HighVisibility`, `InvertLook`. Values are clamped; `Settings.Changed`
(`Action<Setting>`) is raised only when a value really changes; `Save()` writes through;
`ResetToDefaults()`; `Settings.Use(store)` switches stores (tests). Whoever applies a setting reads it
and listens; the menu only writes. `HumanInput` reads the sensitivity and the inverted look, and
`CameraRig` the field of view.

**`Platform/GameRunner`** owns the `Game` in Play Mode and in the build. It is a MonoBehaviour, but Unity's
`Start` / `Update` / `OnDestroy` only forward to `Begin(launch, options)`, `Frame(realDeltaTime)` and
`Shutdown()`, which take time and devices as arguments — so EditMode tests drive the very same code.
`Begin` creates the `Game`, the `HumanInput`, the `GameFlow` and the `Presentation`. `Frame` polls input,
turns keys and clicks into calls on the flow, calls `game.Step` **only while the state is `Playing`**
(with the frame time cut to `MaxFrameTime`, 0.1 s: after a hidden tab the game pauses instead of
fast-forwarding), runs the flow's countdown and presents.

- The page address picks the start (`LaunchOptions.FromEnvironment()`): `?level=N` (an id, or a slug
  such as `?level=sandbox`), `&autoplay=1`, `&plain=1`, `&debug=1` (the debug keys, below). **A launch that names a level or asks for
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
  `context.EscapeClaimed` (the settings card over the pause card: Esc closes that card) - and a tap of R
  restarts from `Paused` and `LevelComplete`. While playing R is a hold (`HumanInput`, below): a tap
  only says `GameRunner.HoldRestartLine`. On the ending the menus' own buttons do everything; with the
  debug HUD a click there goes back to the title.
- **A level nobody is playing stands still.** When the level-complete card moves on by itself and
  neither keyboard nor mouse stirred in that frame, the next level is loaded and waits, its clock at
  zero, for the first key, click or move of the mouse (`runner.AwaitingPlayer`): time nobody played is
  not a level's time.
- Autoplay runs `game.Level.Solve(bot)` through `BotRunner`, paced by the game's own ticks, and carries
  on from level to level - and after the last one to the ending, where it stays (`?autoplay=1` plays
  the whole campaign: 206 s of play and fifteen cards). A script that throws is reported
  (`runner.AutoplayError`, a HUD message) and stops steering; it never takes the game loop down.
- Debug keys: `[` / `]` previous / next level, `P` toggles autoplay (turning it on restarts the level and
  plays it from wherever the flow was, turning it off hands the level over as it is). **They are live
  only where the launch asks for them** (`runner.DebugKeys`): `?debug=1` or `-toyboxDebug`
  (`LaunchOptions.Debug`), a launch with `?autoplay=1` (`P` takes over from the bot), always in the
  editor's Play Mode, and where a test sets `RunnerOptions.DebugKeys`. In the game as it is published
  they do nothing: `P` is the pause key of many games, and a stray `]` moved the bookmark to a level
  the player had never reached. A level the bot has had its hands on records no progress, whoever
  walks it into the exit.

**`Platform/HumanInput`** is the human `IInputSource`. It polls keyboard and mouse through an
`IInputDevices` (`UnityInputDevices` reads `Keyboard.current` / `Mouse.current` and tolerates their
absence; tests use a fake), once per rendered frame in `Update(player, realDeltaTime)`:

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

Keys: `WASD` / arrows move, `Space` jump, `Shift` sprint, click or `E` pick up / let go, `Q` and the mouse
wheel yaw the held prop, `F` pitches it, `Esc` pauses, and `R` **held** for
`HumanInput.RestartHoldSeconds` (0.5 s) restarts the level - it sits between `E` and `F`, and a restart
throws away everything placed so far. (`RestartHold` is how far the hold has come, 0 to 1.)

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
  another area's. Orders 0–99 are `CoreSetup`'s (folders, player settings and the version, layers,
  physics, the shared renderer and - only while the project has no pipeline assigned - a pipeline
  asset to start from with four shadow cascades over 80 units and soft shadows, the template materials
  `ToyLit.mat` and `PlainLit.mat`, the bootstrap scene).
  Areas use 100 and up — pipeline 100–199, room 200–299, toy shading 300–399, UI 400–499 — and a later
  step may replace what an earlier one assigned: the pipeline's tier assets replace the starting asset,
  and `PipelineSetup.ConfigureQualityLevels` then deletes it (`Settings/ToyboxURP.asset`: nothing
  refers to it any more, and the core does not make it again beside the tiers; `ReleaseTests`).
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

## Phase 1 as built (levels 1–4)

`Levels/Level01CheeseWedge.cs` (+ `Level01Set.cs`, its drawn-only set), `Level02ThimbleChasm.cs`
(+ `Level02Shapes.cs`), `Level03ShrinkingApple.cs`, `Level04DominoEffect.cs` (+ `Level04Barricade.cs`).
`LEVELS.md` has an "As built" note at the end of each of these levels and Appendix D with the four side
by side; the close-out's log is [design-notes/phase-1-campaign.md](design-notes/phase-1-campaign.md).
(Local, not in the repository: the builders' and reviewers' logs `tools/out/notes/level0N-build.md` /
`level0N-review.md` and the contact sheets in `tools/out/shots/phase-1`.)

| Level | Room | Solver's drop | Works | Solve (game s) | Gadgets as built |
|---|---|---|---|---|---|
| 1 The Cheese Wedge | `sunny-rug` | wedge 0.4 → 9.46 | 5.5–14 | 12.63 | `SkyCap`, `PropLeash` (crumb) |
| 2 The Thimble Chasm | `pegboard-workbench`, `GroundY` −9 | thimble 0.8 → 11.62 | 9–13 | 9.28 | `Socket`, `FitGauge`, `HazardZone`, `PropLeash`, `SkyCap` |
| 3 Shrinking the Apple | `cardboard-box`, `GroundY` −0.1 | apple 11.4 → 0.451 | 0.10–0.64 | 3.77 | `Funnel`, `PressurePlate`, `Door`, `PropLeash`, `HazardZone` × 4 (outside the box) |
| 4 Domino Effect | `block-hall`, `GroundY` −2.5 | domino 0.5 → 4.128 | 3.2–5.3 | 11.93 | `Breakable`, `PropLeash` × 2 (hall, crumb), `SkyCap` |

What building them taught, and every later level should take over:

- **How far away the toy is picked up from decides what it can become** (`k` = scale / grab distance is
  fixed for a hold). The script behind `LEVELS.md` treated it as a detail; in the simulation a far
  pick-up cannot be made to work in levels 1, 2 and 4. The pattern the three share: the toy stands on
  a pedestal one step from the spawn or on the way; a `PropGrabbed` handler compares
  `e.OldScale / e.GrabDistance` with the level's measured least `k` and says "It only ever gets as big
  as it looks. Pick it up from closer." (the same constant text in all three); a `PropLeash` with
  `Unless` returns a toy that is too small to use to its pedestal, because on a floor it cannot be
  picked up from nearer than the eye is high.
- **No attempt ends in silence.** A level judges a let-go toy once, when it has come to rest
  (`ctx.OnUpdate`, a flag set in `PropDropped` and cleared on the grab), and says in one line what
  went wrong: fell short, too small, too big, too light, no room, not over the hole. Lines are
  `public const string` on the level (the tests compare against them) and go through `ctx.Say`.
- **A let-go the view could not place** leaves the toy at the last pose that was free:
  `game.Grabber.PlacementValid` is false in the `PropDropped` handler. That happens when the place asked
  for is nearer than the toy would be at `MinScale` (`near = MinScale / ratio`). Level 3 says so for its
  pea; Level 1's crumb leash covers it.
- **Dashed paint is the size language** (LEVELS 0.3): a `RoomLit` decal of the toy at the size that
  works, on the surface it stops against or stands on. Level 2's is also its `FitGauge` read-out.
- **A heavy toy that falls is the engine's to guard, not the level's.** A house-sized domino toppling
  sideways onto the player used to throw them at 35–50 units a second, and Level 4 had a guard of its
  own for it. The engine now lets every dynamic prop that outweighs the player and comes at them pass
  through before it touches ("Props and the player's body"; `HeavyPropTests`), and Level 4's guard is
  gone. Levels 5 to 15 need none. (The old guard was blind to direction: it also turned a domino the
  player shoved into air. Now a toy the player shoves is solid, like any other.) The same goes for a
  heavy toy *under* the player: it carries or lifts them at up to 6 units·s⁻¹ and no faster, so a
  level cannot use a loose heavy toy as a fast raft or a catapult for the player - that is a gadget's
  job (a kinematic `Mover`, `BeginDrive`, `player.SetVelocity`).
- **Walls may be drawn lower than they are.** Level 4's colliders reach its sky cap; the drawn rows of
  blocks are low so that the sun reaches the board. Anything opaque between the eye and a held toy still
  needs a collider, never the other way round.
- **Words.** Toys are *picked up* and *let go*; text is plain ASCII (the UI font has no dash but the
  hyphen); a level has three hints, the last one the recipe. `Phase1CampaignTests` holds the four
  levels to that.

Known limits the levels live with: Level 2 tolerates ±2.9° of yaw from its spawn (±7° two steps on);
a toy at its smallest cannot be put down nearer than `MinScale / ratio`. (The scale readout no longer
lies on the held toy: it is docked on the bottom edge and moves to the top right corner when the toy
reaches down to it - `ART_BIBLE.md` §9.6, `HudLayoutTests`.)

## Phase 2 as built (levels 5–9)

`Levels/Level05FanFeather.cs` (+ `Level05Shapes.cs`), `Level06BouncingEraser.cs`, `Level07TeeterTotter.cs`
(+ `Level07Set.cs`), `Level08FunnelPhysics.cs` (+ `Level08Shapes.cs`), `Level09MovingTrain.cs`
(+ `Level09Shapes.cs`). Twelve of the fifteen levels draw their sets with `Level01Set.cs` (all but 7,
12 and 15, which have a set class of their own; its header says so), and every set keeps its merged
meshes over a restart in `SetMeshes`. `LEVELS.md` has an "As built" note at the end of each of these levels and
Appendix E with the five side by side; the close-out's log is
[design-notes/phase-2-campaign.md](design-notes/phase-2-campaign.md). (Local, not in the repository:
`tools/out/notes/level0N-build.md` / `level0N-review.md` and the contact sheets in
`tools/out/shots/phase-2`.)

| Level | Room | Solver's let-go | Works | Solve (game s) | Gadgets as built |
|---|---|---|---|---|---|
| 5 The Fan and the Feather | `sunny-rug`, `GroundY` −15, `KillY` −13 | feather 1.0 → 9.33 | 6.5–12 | 17.35 | `WindStream`, `SailRaft`, `PropLeash`, `SkyCap` |
| 6 Bouncing Eraser | `block-hall` | eraser 0.8 → 9.37 | 7.08–10.5 | 12.12 | `BouncePad`, `PropLeash` × 3 (cabinet top, crumb, room), `SkyCap` |
| 7 The Teeter-Totter | `high-shelf` | pebble 0.6 → 4.63 | 2.7–7 | 3.68 | `Seesaw`, `PropLeash`, `SkyCap` |
| 8 Funnel Physics | `pegboard-workbench`, `GroundY` −6.6 | marbles → 0.651 / 1.221 / 2.413 | 0.50–0.76 / 1.00–1.52 / 2.00–3.04 | 20.50 | `Funnel` × 3, `PressurePlate` × 3, `FitGauge` × 3, `HazardZone` × 3, `ReturnPort`, `PropLeash` × 2 (jam, niche), `Door`, `SkyCap` |
| 9 The Moving Train | `high-shelf`, `GroundY` −14, `KillY` −12 | plank 0.65 → 4.086 | 3.68–4.4 | 11.75 | `Train`, `PropCarrier`, `PropLeash` × 2 (crumb, stranded), `SkyCap` |

What building them added to phase 1's list:

- **The start is the first pick-up.** Every level of the batch starts the player one step from the toy
  with the crosshair on it; the text's spawns were three to five units away, from where the toy looks
  a third as big and cannot be made to work. (Level 8, with three toys, starts on a picture of all
  three and their holes, two of them at the player's feet.) What stands between the start and the
  target counts too: Level 5's spool had to be lower for the most natural first try - click, turn to
  the outline, click - to pass over it.
- **A pick-up can also be too near.** A toy picked up again from close by looks big, reaches its clamp
  or meets something before it is where it should be, and Levels 7 and 9 say so at the pick-up
  ("From this close it looks too big ..."); Level 6 hides the spool under an eraser that comes down on
  it for the same reason.
- **Decide every try quickly, and say what it was.** Level 9 judges a plank within 2.5 s of the let-go
  (gripped, fallen, or sent back with its line); a boulder, an eraser or a feather is judged when it has
  come to rest. A try that needs the player in place (Level 7) says so before the moment, while there
  is time to read it.
- **Lamps must not say more than is true.** Level 8's gauge used to turn green for a marble that would
  land on the lid beside the hole. A read-out is only on while the thing it judges is where it will be
  judged; until then something else points at where to hold it (the blinking dashes).
- **A toy that fits or works as it lies is a way round the mechanic.** Level 8's middling marble was
  rolled into its hole by walking into it; no toy of the batch now does its job at the size it starts
  with.
- **Fast kinematic parts and the player:** Level 7's throw sets the player's velocity explicitly at
  the end of the swing; Level 9 needed `Player.LimitSteepFaces` to measure the speed into a steep face
  relative to the face's own sideways motion (a hop at a step riding the same train cost the train's
  whole speed); `GadgetKit.SteadyRider` adds the turn a rider of a circling mover is owed.
- **Words.** `Phase2CampaignTests` holds the five to phase 1's voice and adds two rules: every hint is
  set at the pause card's full size (five lines of the panel; a sixth shrinks the type), and a toast is
  at most 110 characters.

Known limits the levels live with are listed at the end of `LEVELS.md` Appendix E.

## Phase 3 as built (levels 10–15) and the end of the game: the campaign complete

`Levels/Level10MatryoshkaBoxes.cs`, `Level11BlockingLasers.cs`, `Level12Keyhole.cs` (+ `Level12Set.cs`),
`Level13Fishbowl.cs` (+ `Level13Shapes.cs`), `Level14InfiniteHallway.cs`, `Level15RubeGoldberg.cs`
(+ `Level15Stage.cs`, `Level15Set.cs`). `LEVELS.md` has an "As built" note at the end of each of these
levels and Appendix F with the six side by side and the end of the game; the close-out's log is
[design-notes/phase-3-campaign.md](design-notes/phase-3-campaign.md), and two of its contact sheets are
[media/campaign-spawn-sheet.png](media/campaign-spawn-sheet.png) (the fifteen spawn views) and
[media/ending-sheet.png](media/ending-sheet.png). (Local, not in the repository:
`tools/out/notes/level1N-build.md` / `level1N-review.md` and the other sheets - one per level, the
fifteen held toys - in `tools/out/shots/phase-3`.)

| Level | Room | Solver's let-go | Works | Solve (game s) | Gadgets as built |
|---|---|---|---|---|---|
| 10 Matryoshka Boxes | `cardboard-box`, `GroundY` −0.1 | boxes 1.2 / 0.9 / 0.6 → 1.912 / 0.971 / 1.081 | 1.52–2.0 / 0.61–1.15 / 0.69–1.15 | 17.53 | `NestedSet`, `PropLeash` × 2 (shelf, crumb), `SkyCap` |
| 11 Blocking Lasers | `pegboard-workbench`, `GroundY` −0.1 | card 0.5 → 5.947 | 4.3–7 | 6.10 | `LaserRain`, `PropLeash` × 2 (out of reach, crumb), `SkyCap` |
| 12 The Keyhole | `high-shelf`, `GroundY` −9.5 | key 8 → 0.582 → 0.341; spool 1 → 9.162 | key 0.06–0.5; spool 8.35–9.9 | 13.97 | `Socket` (lock), `Door`, `HazardZone` × 2, `ReturnPort`, `PropLeash` × 2, `SkyCap` |
| 13 Escaping the Fishbowl | `cardboard-box`, `GroundY` −0.6 | sponge 2.0 → 9.593 → 1.862 | 6.09–12; 0.5–2.3 | 14.32 | `WaterVolume` × 2, `Sponge`, `FloatPlatform`, `Door` (flap), `PropLeash`, `SkyCap` |
| 14 The Infinite Hallway | `night-light`, `GroundY` −0.2 | door 1.0 → 0.281 / 3.2 / 1.319 | ≤ 0.47 / ≥ 2.48 / ≤ 2.00 | 9.95 | `PortalDoorway`, `RecallPad` × 2, `SkyCap` |
| 15 The Rube Goldberg Machine | `night-light` | dominoes 3.103, ball 3.000, ruler 1.012, fan 0.924 | 2.2–3.5 / 2.25–3.0 / 0.8–1.2 / 0.6–1.2 | 40.75 | `Socket` × 6 (four stations, two approaches), `MachineDirector`, `PathDrive` × 3, `Seesaw`, `WindStream`, `HingeChain`, `Breakable`, `PressurePlate`, `Door` × 2, `Funnel` (the bell jar), `FitGauge` × 4, `PropLeash` × 2, `HazardZone`, `ReturnPort`, `SkyCap` |

What building them added to the lists of phases 1 and 2:

- **A clamp is a design tool only against the hop that was measured.** The text designed with a hop of
  1.15; the capsule gets up 1.30 always and 1.35 sometimes (its round foot rolls over an edge). Level
  10's two-box routes and Level 14's 3.0 ledge both came from that margin. Gates are set from the
  measured limit, and a test holds the margin on both sides (Level 14: fits at 2.00, climbs from 2.48).
- **What the player stands on is part of the puzzle's state.** Through phase 3 the engine refused a
  click at the prop under the feet only while the feet were on it; in the air, the box one had jumped
  off could be picked up and let go bigger under oneself - a way up in every level with a toy to stand
  on. Since the final review the engine refuses it until the feet are on something else
  (`Player.Underfoot`). A level where growing something underfoot is worth anything (10, 12) still
  keeps its own "last stood on", to say why the click did nothing.
- **Paint that judges.** Where the size window is narrow and depends on the pick-up (11: 4.3–7 across a
  six-wide lane; 12: a ratio of 1.19), the dashed outline is the gauge, in Level 2's colours: paint
  while nothing is judged, red and blinking for "not like this", green and steady for "let go now".
  Green must never be wrong (Level 11: 1872 tries, green and no roof 0); red may be shy.
- **Seats take what is nearly right.** A let-go that is square, flat and within reach of the place it
  was plainly meant for is slid home as it falls (11's card on its walls, 12's spool to the door, 15's
  approach sockets) - a correct-looking try that fails by a hand's width is the worst thing a level of
  this game can do. Everything outside the seat falls as it will and is told why.
- **A toy that changes the player** (14) needs its rule checked against everything else the player can
  do while crossing: jumping in, walking in from above, crossing onto nothing. The fix belonged in the
  gadget, with its test next to the gadget's.
- **A machine keeps what it is running** (15): toys are locked while the director runs or sets itself
  up, a run that stops still finishes the drive that was under way, and anything handed on by a helper
  socket is the player's again once the machine is idle.
- **Check a solution against the fantasy, not only against its window.** Level 13 was solved with the
  text's numbers and left a third of the bowl; the brief says the sponge soaks up the fishbowl.
- **Words.** `Phase3CampaignTests` holds the six to the voice of the nine before them, and every hint
  and blurb of all fifteen to full size on its card.

**The end of the game.** See "HUD and menus" and "Game flow" above: `FlowState.Ending`, `EndingMenu`,
`GameFlow.Advance / ShowEnding / Report`, `Progress.Endings / PickUps`. `Phase3CampaignTests` plays the
real game from the title through fifteen cards to "Finish" and the ending, then a new session from the
same store (title over level 1, catalogue 15 of 15), and plays the whole campaign by `?autoplay=1` in
one game to the ending. **The dawn** before it is the lighting rig's: in a night room,
`MachineDone` (Level 15's alarm) starts `LightingRig.Dawn` on its way from 0 to 1 over `DawnSeconds`
(4 s), and the rig blends the night's values toward `LightingValues.Dawn(env)` - the sunny-rug's sun
and window patch, the day's ambient on the room's own tones, the day's glint, the toys' glow back to
1. A level load is night again; day rooms take no notice (`LightingRigTests`).

Known limits the levels live with are listed at the end of `LEVELS.md` Appendix F.

## The final review (2026-10-11): what changed, and what is knowingly left

Before release the finished game was read through five lenses - menus and flow, a first-time player,
what works in the editor and breaks in the browser, frame cost and memory, and what a stranger finds in
the repository - with three points from the owner on top (lasers that were not red, a plank that looked
like plastic, a toy picked up from under one's own feet in a jump). What was found was fixed in four
areas and then closed as a whole: the suite, a Play Mode run of every level, the diffs read once more.
`LEVELS.md` Appendix G is the same account for the levels, with the fifteen solve times.

What changed, and where it is described:

| What | Where |
|---|---|
| A toy that is underfoot cannot be picked up - also not in the air after a jump off it (`Player.Underfoot`). It was a way up in every level with a toy to stand on | "The perspective mechanic"; `UnderfootTests` |
| A held toy is never a hair outside its own limits (`PerspectiveGrabber.UpdatePlacement` clamps what it places): a gauge whose window ends at the toy's limit read "too big" for a toy the station takes | `PerspectiveTests`, `FitGaugeTests` |
| The game names its keys: key caps on the HUD until each has been used, a row on the title, the whole list and the level's name and goal beside the pause card | "HUD and menus" |
| `R` restarts only when held for half a second while playing; `[`, `]` and `P` do nothing unless the launch asks for them (`?debug=1`); a level the bot has touched records no progress | "Game flow, progress and settings" |
| The bookmark moves on at the completion, not at "Next"; a level the complete card moved on to with nobody there waits for the first input (`GameRunner.AwaitingPlayer`) | the same |
| Menus: a key held when a menu opens does not walk its focus; the complete card takes no input before it shows and "Next" works once; Esc holds the card; cards reopened from the catalogue come back as they were left; the catalogue's keyboard runs on from row to row; hints repeat before they go on; invert look and effects volume on the settings card, with the music audible behind it | "HUD and menus" |
| The quality governor gives a descent back when it bought nothing: a display or browser that paces frames under 50 a second is not a slow GPU | "The render pipeline"; `ART_BIBLE.md` §7.4 rule 5 |
| Lasers are red (`Palette.Laser`), the plank is painted wood with bare sawn ends | "Where the game departs from the art bible" |
| `?plain=1` glows in a build too (`PlainLitEmissive.mat` keeps URP Lit's emission variant in the player) | "The render pipeline" |
| Draws and garbage: the sets of levels 2, 3, 4, 8, 9 and 11 are merged per material; merged set meshes survive a restart (`SetMeshes`); the portal's camera renders at a few sizes instead of a new one every frame; a tier change generates no texture; lasers and wind move their meshes instead of rebuilding them; the pause's frozen frame is the size the camera renders | "The render pipeline", "HUD and menus", "Phase 2 as built", the art bible §12 |
| The page holds a visitor without keyboard and mouse before the game is fetched, has an icon and a link preview, stops looking busy when the game cannot start, and carries the fonts' licences (`licenses.txt`) | "Releasing" |
| Level 4 starts at its domino; Level 9's words no longer ask for a run; the wooden dominoes of Level 4 answer a click | `LEVELS.md`, the levels' notes and Appendix G |
| The documents: README rewritten against the code, `LEVEL_AUTHORING.md`, the close-out logs and two contact sheets in `docs/`, `ReleaseTests` | "Testing and verification" |

**Knowingly left.** None of it stops the game from being played through; each is here so that nobody
has to find it again.

- **Nothing of this round has been seen in a browser.** The build that is published was made before it.
  The next build needs the whole of "Releasing", and these looked at by hand, because no tool judges
  them: the page on a phone (the note, "Load anyway") and with WebGL switched off (the message, no
  spinner); the tab's icon and the link preview; the key caps on the HUD, the title's row and the pause
  card's guide at the window's real size; `W` held while `Esc` is pressed (the pause card's focus must
  stay on Resume); the pause card on a 4K canvas and on the Low tier (its frozen frame is rendered at
  another size than before); `?plain=1` (the signal lamps must glow); the laser curtain and the plank; a
  session on a display that is not 60 Hz (it must end on the tier it started on, with one line in the
  console saying so).
- **`Esc` does nothing while the bot plays** (`?autoplay=1`): no pause card, so no way to the settings
  from there. `GameRunner.HandleKeys` returns early during autoplay; pausing a bot has not been tried.
- **A level started with the complete card's "Next" usually waits for one more input.** The runner
  sees keys and the mouse, not what a menu button did. A button acts when its key or the mouse button
  comes up again, so in the frame the next level starts nothing is pressed: after Enter or Space on
  "Next", or a click with the mouse at rest, the level stands still until the first key, click or move
  of the mouse - which is whatever the player does next. The level's clock starts there.
- **A restart still rebuilds the room** (`RoomVisuals` clears on `LevelUnloading` and builds on
  `LevelLoaded`): about 1 MB of garbage and 5 to 10 ms per restart in the editor. Keeping it needs an
  equality test for the solved environment. Level 11 makes another 7 MB per restart because
  `LaserRain` merges its lens mesh on every build.
- **The menus' textures.** Every one keeps a depth buffer nothing reads (URP's render graph refuses a
  camera whose target has none), and the level-complete card's texture is the canvas's size, not the
  tier's pixel budget: 17 MB at 1080p, 66 MB on a 3840 × 2160 canvas, for as long as the card is up.
- **The governor's one step up.** A session that starts on Low is never raised, and a session that
  starts on Medium tries High about 25 s into play (a hitch of a few tens of milliseconds; taken back
  for good if it does not hold). Whether Low may try Medium, and whether the trial should wait for a
  level load, is the owner's rule to set (`QualityGovernor.StepUp`).
- **The levels' lines that ask for a turn or a flip do not name the key** (levels 1, 9, 11, 13, 14),
  and Level 6's "Jump on it." does not name Space. The key caps on the HUD do, while they are needed,
  and the pause card lists every key.
- **Level 10 keeps a guard of its own** on top of the engine's rule: from up on a box no other box is
  picked up either. It is wider than `Player.Underfoot` on purpose.
- **The tools** (changed after the review, where it had found their defaults wanting): `build-clone.ps1`
  builds with `RuntimeSpeed` when it deploys and with `BuildTimes` otherwise; `webgl_check.py` watches
  48 s by default, has `--all` for the fifteen levels and `--phone` for the page as a touch-only device
  sees it. Still so: it does not judge whether the bot finished a level (look at its pictures), and it
  lists "favicon" as console noise although the page has an icon now; `unity.ps1`, `typecheck.ps1` and
  `editor-check.ps1` expect Unity in the Hub's default folder; and the build copy has no README or
  `docs/`, so `ReleaseTests` skip themselves there.
- **The licence** of the game's own code and content is MIT (`LICENSE`); the fonts keep the SIL Open
  Font License. The game itself shows no credits: the notices are on the page and in the README.
- **The pictures in the repository are captures of the game as it stood at the close:**
  `docs/media/title.png` and the page's `preview.png` (the same picture, in the WebGL template
  folder), and the two contact sheets beside it (the fifteen starts, the end of the game). Capture
  them again when what they show changes (`Shots.Capture -toyboxLevel 1 -toyboxUi title`; a sheet is
  fifteen `-toyboxUi hud` captures at time 0 pasted together).

## Where the game departs from the art bible

Decided while putting the areas together and looking at the pictures; each has its reason next to the
code. (The areas' own deviations were logged in `tools/out/notes/m2-*.md`, local files that are not in
the repository; what matters of them is in this table and in the sections above.)

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
| §9.4 the callout's 1.7-unit figure at the toy's base, always | `DimensionCallout`: no figure nearer than 2.1 figure heights from the camera, and the factor sized for the distance | Level 3 lets its marble go a step and a half from the eye: the figure stood half the picture tall between the player and the flap that falls open |
| §10.5 held-control pill "click: drop"; the level-complete card's "N grabs" | "click: let go"; "N pick-ups" (`UI/Hud.cs` `ControlPills`, `UI/MenuScreens.cs`) | Every line and hint of the levels says *pick up* and *let go* - one verb for one act, held to by the campaign tests - and the pill is on screen beside them. The pill's stored key is still `drop` |
| §2.2 candy on toys, dips on the room; a Lagoon toy never stands in the Pool room | Level 13's Lagoon sponge lies on water drawn in the Pool dip (`WaterVolumeOptions.Dip`) inside the Peach room | Water in the room's own peach read as sand, in mint as pond slime. The sponge keeps its rim and its pores against the water; it is the one place the banned pair meets |
| §2.3 lasers in the Hazard signal (magenta) | `Palette.Laser` `#FF1414` with a near-white heart (`Palette.LaserCore`): beams, dots, lenses, the zap's ring (`Render/LaserVisuals`, `Gadgets/LaserRain`, `GadgetCues`). Kill surfaces stay Hazard | The owner's brief for Level 11 says "Red laser pointers act as security beams". §2.3 now has the row; red beams also read as a laser at a glance, against every dip the gadget stands in (Pool in Level 11, Mint in the gallery) |
| §2.5 "details are Paper or Ink only" | The Plank's two sawn ends are bare `Palette.Birch` with growth rings (`ToyFactory.Plank`, the "Ends" draw) | The owner's brief for Level 9 says "a wooden plank". Birch is §2.1's raw wood ("at chipped edges"); a sawn end is the biggest chip there is. The candy coat, with its grain showing, is still what says "you can lift this" |
| §2.5 rule 3 as first written: every metal toy is anodised candy | Level 2's thimble is bare `Palette.Silver` (`ToyFactory.Silver`) with an anodised Tangerine band round its rim | The owner's brief says "a tiny silver thimble". Rule 3 now names it as the one exception; the band is the candy that says "you can lift this", and it is what the pool, the tag and a room's banned-colour swap use |

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
- `HeavyPropTests` — nothing throws the player: props from half to 200 times the player's mass sliding,
  rolling, falling and toppling at them at 1 to 25 units·s⁻¹ (speed under 9 and less than 1.5 units
  moved, always; untouched when the prop is heavy and fast), and everything that must still work around
  it: standing, walking and landing on a heavy prop at rest and under way, pushing props light and
  heavy, platforms, the slab that fell being solid again. Its explicit test
  `Probe_WhatAPropDoesToThePlayer` (`-Filter "HeavyPropTests.Probe"`) writes the table of measurements
  the thresholds were decided from to `tools/out/notes/prelude-engine-fling-probe.txt` (local output: run
  the probe to have it).
- `HeavyPropAttackTests` — the checker's attacks on that rule, kept: a long plank that sweeps round, a
  tall beam and a real domino that topple (foot, middle, tip), a domino knocked over by another, a
  ball down a ramp (the player at its foot, on it, in the notch against a wall), a block dropped on a
  player who walks or sprints, a toy grown in the hand and let go overhead or at the feet, jumping
  into a block that slides toward, away or across, pinned against a wall or in a corner, standing on
  a heavy prop that another one hits (slide, roll, fall, catapult), a block at rest beside the player
  struck by another. In each: under 9 units·s⁻¹ sideways and upward, never more than a second inside
  a prop that is solid, the player walks away, the prop at rest can be stood on, and the same run
  twice gives the same bits. Every run writes its tables to `Temp/ToyboxHeavyPropAttacks.txt` (those of
  the run that checked the rule were kept in `tools/out/notes/prelude-verify-attacks.txt`, a local file).
- `Tests/EditMode/TestHelpers.cs` — derive a fixture from `SimTest` (it disposes the Game after every
  test), build ad-hoc geometry with `Build(ctx => ...)` and `TestHelpers.Floor / Box / Room / Ramp`,
  steer with `ScriptedInput`, and run a level's own solution with `TestHelpers.PlayLevel(game)`.
  Every test must dispose its Game: only one may exist, and it owns global physics state.
- `Tests/EditMode/Levels/LevelNNTests.cs` — builds the level, runs `Solve(bot)`, asserts
  `LevelCompleted` fired before a timeout. A level without a passing solver is not done. Levels 1 to 4
  go much further (64, 43, 55 and 44 cases): the scale window, every stand-and-aim variation that was
  measured, each bypass and soft-lock that was tried, restarts at awkward moments, and every line the
  level says.
- `Tests/EditMode/Levels/Phase1CampaignTests.cs` — the four levels as a campaign, with the real level
  list: the order, title → level 1, each completion offering the next, the catalogue's cards (title, hero
  toy, room colour, COLLECTED, best time), progress written to the store and continued in a new
  session, the bot solving all four in one `Game` forwards and backwards with the same times to the
  tick, one hero toy per level in view at the start, and one voice across the levels' texts.
- `Tests/EditMode/Levels/Phase2CampaignTests.cs` — the same for levels 5 to 9, played on from phase 1:
  the order after level 4, title → nine completions each offering the next, the nine cards, progress
  continued in phase 2, the bot solving the five in one `Game` forwards and backwards (17.35 / 12.12 /
  3.68 / 20.50 / 11.75 s, the same to the tick), the hero toy under the crosshair at the start and
  nothing else to pick up, one voice with phase 1, and every hint at the pause card's full size.
  `Level05Tests` to `Level09Tests` are of the same kind as phase 1's, and larger.
- `Tests/EditMode/Levels/Phase3CampaignTests.cs` — levels 10 to 15 and the campaign as a whole: fifteen
  levels in order in three phases; title → fifteen completions → "Finish" → the ending (15 OF 15
  COLLECTED, the cards, the totals) → the catalogue → the title → a new session from the same store
  that starts at level 1 with the catalogue kept; progress continued in phase 3; straight to
  `?level=15` gives an honest "1 OF 15"; the bot solving the six in one `Game` forwards and backwards
  (17.53 / 6.10 / 13.97 / 14.32 / 9.95 / 40.75 s, the same to the tick); **the whole campaign by
  `?autoplay=1` through the real runner in one game** (every level loaded and completed once, in
  order, each in its own solver's time; the ending up; the store empty); the hero toy under the
  crosshair in a candy colour the room allows; the last level ending in the morning (the dawn); one
  voice; every hint and blurb of all fifteen at full
  size. `Level10Tests` to `Level15Tests` (with `Level10HintTests` and `Level1NUiTests`) are 70 to 113
  cases each. The ending screen itself is in `UiTests` (`MenuTests`: the timeline, keyboard only, levels
  missing, Reduce Motion, a bot's ending, the layout with fifteen cards) and `GameFlowTests`.
- **Explicit tests run when the filter names them.** The levels' probes and picture tours are
  `[Explicit]`; a run without a filter skips them, but `-Filter "Level09Tests"` matches their names and
  runs them (minutes of pictures and tables). Filter them out:
  `"Level05Tests\."` (the tour is the class `Level05Tour`), `"Level06Tests\.(?!Probe|Tour|Review_)"`,
  `"Level08Tests\.(?!Probe)"`, `"Level09Tests\.(?!Tour)"`, and for phase 3
  `"Level1[0-5](Hint|Ui)?Tests\.(?!Probe_|Tour_)"`. `Phase3CampaignTests.Tour_HoldTimes` writes when every
  solver picks up and lets go (`tools/out/notes/phase-3-holds.txt`; the table of 2026-10-11 is kept as
  [design-notes/phase-3-holds.txt](design-notes/phase-3-holds.txt)): the times the contact sheets are
  taken at (by two local scripts, `tools/out/shots/phase-3/work/capture.ps1` and `make_sheets.py`).
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
- `ReleaseTests` reads what a stranger gets: the README lists the campaign's levels by the registry's
  titles, uses the game's words for its controls and names the editor version and every tool; every
  link in the README and under `docs/` leads to a file in the repository and none into `tools/out`;
  no document carries a path on somebody's machine; every font file under `Assets/` has its copyright
  notice and the licence text in the template's `licenses.txt` and in the README's credits; the
  version is a release; the core's starting pipeline asset is gone and the setup does not bring it
  back. (A copy of the project made for a build has no documents: there these tests are skipped.)
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
  `playcheck -UnityArgs '-toyboxUrl','?level=3&autoplay=1'` plays that level: with autoplay already on
  in the address the check waits for one load after the completion (the next level), otherwise for two
  (its own `SetAutoplay` reloads the level first). After the campaign's last level nothing is loaded:
  the check waits for the ending instead and reports "the game showed its ending". It watches for 70 s
  (Level 15's own solution is 41 s of play).
- **The size of the suite** (2026-10-11, at the close of the final review): 2206 tests, of which 67 are
  the explicit probes and picture tours that a run without a filter skips; six and a half minutes. It
  is green - 2139 passed, none failed, 67 skipped - on the code and the documents as they stand, and
  each of the fifteen levels was played through by its bot in Play Mode on the same code (`LEVELS.md`,
  Appendix G). When phase 3 closed it was 2112 tests, 2047 passed. (The one test that failed through phase 1,
  `ToyShadingTests.HeldToy_IsDrawnByTheStickerPassAlone_WithBorderAndPeelShadow`, passes since the
  `PeelShadow` pass of `Sticker.shader` blends with the destination as the factor, `Blend Zero SrcColor`:
  with MSAA on, the editor's Direct3D 12 device brightened what `Blend DstColor Zero` should have
  darkened.)
- **Checked only against a WebGL build, by `tools/webgl_check.py`** - it is not part of
  `unity.ps1 test`: it needs a build, served locally ("Releasing") or the published page (`--base`).
  For each level, loaded with the bot playing it: the player starts, the page keeps answering (a loop
  that never ends freezes it - Level 5's dashes), the frame rate (under 30 is a problem), console
  errors, and what the page plays (audible or silent, peak, clipping). With `--play`, the first minute
  of a person at the keyboard through real mouse and key events: the title, click to play, the mouse
  captured (the `stickyCursorLock` line, which only the Web player compiles), look, walk, pick up, let
  go, jump, `Esc` (it fails when the click does not capture the mouse, `Esc` does not free it, the page
  is silent or the console shows an error). It does **not** judge whether the bot finished the level - its pictures
  (`tools/out/shots/webgl`, every six seconds) are for looking at: §7.5 item 8 (a held glossy toy with
  bloom on its glint, the vignette, the border and the peel shadow), the level-complete card, the
  ending and the dawn are judged there by eye. `--seconds` must cover the level's solve (Level 15:
  41 s; the default is 40). `tools/editor-check.ps1` is the same idea in the windowed editor.
  The build of commit `9ca6138` - all fifteen levels and the ending - was run through it and published
  on 2026-10-11; until then only levels 1 to 9 had been in a browser.
- **Not checked by anything**: how any of the audio sounds to a person; the catalogue, the settings
  card and the level select on a real screen; pointer lock after `Esc` and a quick Resume (a browser
  refuses a new lock for about a second after the user's `Esc`), alt-tab; contact modification under
  IL2CPP beyond "no console error"; a display that is not 60 Hz; a touch-only device (there are no
  touch controls); and the IMGUI drawing of `DebugHud` (a batch-mode editor never calls `OnGUI`).
  Check `Esc`, alt-tab and a quick re-click in a real browser after every change to `Platform/`.
- `.\tools\unity.ps1 test` must pass before any commit.

## Releasing

GitHub Pages serves the `gh-pages` branch, which always holds exactly one commit: the latest build.
Unity cannot be built on GitHub's hosted runners without licence secrets, so builds are made locally.
A release is four steps, each of which must pass before the next:

```powershell
# 1. The whole suite. Commit what passes: step 2 builds HEAD, not the working folder.
.\tools\unity.ps1 test

# 2. The release build of HEAD, in the private copy (%USERPROFILE%\.cache\tinkerstoybox\buildclone);
#    the build lands in %USERPROFILE%\.cache\tinkerstoybox\Build\WebGL. The main folder is not touched.
.\tools\build-clone.ps1 -Committed -Opt RuntimeSpeed

# 3. The build in a real browser. Serve it in one window ...
python -m http.server 5180 --directory $env:USERPROFILE\.cache\tinkerstoybox\Build\WebGL
#    ... and in another play every level with the bot and the first one by hand (once: pip install playwright).
python tools/webgl_check.py --levels 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 --seconds 60 --play

# 4. Publish the build that was checked.
.\tools\deploy.ps1 -SkipBuild
```

- **`-Opt RuntimeSpeed` has to be typed.** `build-clone.ps1` (and `unity.ps1 build`, and the editor's
  Toybox > Build WebGL) default to `BuildTimes`, a faster build of a slower game, and
  `build-clone.ps1 -Deploy` publishes whatever it has just built. Only `deploy.ps1` without
  `-SkipBuild` defaults to `RuntimeSpeed` - but it builds in the main folder, which stops the
  background editor and takes work in progress along with what is committed.
- `--seconds 60`: the check watches each level for that long and takes a picture every six seconds; the
  default of 40 ends before the bot has finished Level 15 (41 s) and before the ending is up. Look at
  the pictures in `tools/out/shots/webgl` - the check does not judge whether a level was completed.
- `deploy.ps1` writes the commit it is run from into its message ("Deploy WebGL build of abc1234")
  and refuses a build with a file over 95 MB; run it from the commit the build was made of.
- The page's version is `PlayerSettings.bundleVersion`, set by `CoreSetup.ConfigurePlayer`.
- `licenses.txt` (the fonts' notices) comes from the WebGL template and must be in the published tree
  beside `index.html`.
- **The page** (`Assets/WebGLTemplates/Toybox/index.html`) is the loading screen and three small
  decisions of its own. A visitor whose device cannot hover and whose first pointer is a finger
  (`(hover: none) and (pointer: coarse)`: a phone, a tablet) is held before the game is fetched, told
  that the game needs a keyboard and a mouse, and can press "Load anyway"; `?autoplay=1` is never
  held, and an error in the check starts the game. A start that fails stops the spinner and shows the
  loader's own words with "Try a current Chrome, Edge or Firefox on a computer.". The tab's icon is
  an inline SVG (no file to miss), and the `og:` / `twitter:` tags point at `preview.png` beside the
  page. `PlatformTests.TheWebPage_...` reads the template as text and holds its autoplay test to
  `LaunchOptions`; in a browser it has not been seen yet ("The final review").
- After publishing, `python tools/webgl_check.py --levels 1 --base https://sramireddy2.github.io/tinkerstoybox/`
  checks the page people get.

# Level author's reference

The short reference for writing a level: what a level class looks like, the rooms, the materials, the
toys, the gadgets and the commands that take pictures of it. Names only; the source has the full
signatures and [ARCHITECTURE.md](ARCHITECTURE.md) the rules behind them — the engine's own types
(`Game`, `LevelContext`, `Prop`, `Mover`, `Trigger`) are in its "Core types", the solver's commands in
"The Bot", and every gadget's behaviour in "Gadgets". [LEVELS.md](LEVELS.md) is the campaign itself;
its appendices D, E and F hold the lessons and the measured numbers of the fifteen levels.

## A level

```text
  [Level(3, "shrinking-apple", "Shrinking the Apple", Phase = 1)] public sealed class Level03 : LevelDefinition {
      override string Blurb; override string[] Hints; override string Environment; override float GroundY (play plane; bottom of the deepest pit); override float KillY;
      override void Build(LevelContext ctx);   // may run again on restart
      override IEnumerator Solve(Bot bot); }   // every command yielded; Tests/EditMode/Levels/LevelNNTests.cs -> TestHelpers.PlayLevel(game)
  Phase > 0 = campaign; Phase 0 = reached by ?level=N only.
```

## Environment keys

`LevelDefinition.Environment`; the default is the art bible's table for ids 1-15 and `"sunny-rug"` for any
other `[Level]`.

```text
  "sunny-rug" (Mint; hero Cherry, banned Lime; levels 1, 5)
  "block-hall" (Butter; Grape / Lemon; 4, 6)
  "pegboard-workbench" (Pool; Tangerine / Lagoon; 2, 8, 11; bench top, floor 60 below)
  "cardboard-box" (Peach; Lagoon / Tangerine; 3, 10, 13)
  "high-shelf" (Lilac; Lemon / Grape; 7, 9, 12; shelf top, floor 75 below)
  "night-light" (Plum; Lime / none; 14, 15)
  "none" (no room, no colliders)
  game.Environment is solved after Build; its colliders are in the simulation. ctx.Dip is the room's dip.
```

## Materials for level statics

`RoomLit` in the room's dip: light top, deep sides.

```text
  ctx.AddStatic(BasicToys.Slab(size), centre[, rotation]);   BasicToys.Ramp(length, height, width[, material])   // rises toward +Z
  Materials.Room(RoomSurface.LevelStatic, pattern: PatternSpec.Of(RoomPattern.Tiles).WithPitch(x).WithGain(g))  -> BasicToys.Slab(size, material)
  RoomPattern: None, Dots, Planks, Tiles, Stripes, Quilt, Pegboard, Corrugated (world units).
  Materials.Room(RoomRecipe.Solid(colour)) / Slab(size, colour) for test geometry only.
  Machinery: Materials.Gadget(GadgetPart.Body | Metal), Materials.Gadget(signal[, gain]), Materials.Water(dip), Materials.Flat(linearHdr, FlatShape, FlatBlend, soft).
  Signals: Materials.SetEmission(renderer, signal.EmissionAt(signal.GainAt(t)))  -- never colour x gain.
  Anything opaque between the eye and a held toy needs a collider; visual-only objects must have none.
```

## Toys

Namespace `Toybox.Toys`.

```text
  Prop p = ToyCatalog.Add(ctx, ToyId.X, position[, rotation], scale = 1, color: null, options: o => { ... });
     position = the toy's origin; ToyCatalog.Get(id).RestHeight * scale = origin above a floor; a banned candy becomes the room's hero.
  enum ToyId { WoodenBlock, CheeseWedge, Thimble, Apple, Domino, Feather, Eraser, Pebble, Marble, Plank, GiftBox, PlayingCard, Key, ThreadSpool, Sponge, Doorway,
               BouncyBall, DeskFan, CatapultRuler, DominoSet, Baseball, Marker, TrainEngine, TrainCar }   // last four are set pieces
  ToyCatalog: All; Get(id); Find(slugOrName); HeroOf(levelId); Build(id, Color?); Options(id, scale)
  ToyDef: Id, Name, Slug, Size, Recipe, Color, Friction, Bounciness, MinScale, MaxScale, GrabPose, KeepUpright, AllowPitch, Grabbable, Body,
          Volume, Center, HalfExtents, Radius, RestHeight, Mass, Density, Triangles, Draws; Build(Color?), Options(scale), ColorIn(Dip)
  ToyFactory (GameObject at scale 1): WoodenBlock(size, color, grabbable), CheeseWedge, Thimble, Apple, Pebble, Marble, BouncyBall, Baseball, Domino(color, grabbable),
      DominoPiece(height, ...), DominoSet, Feather, Eraser, Plank(size, color, grabbable), Ruler(size, ...), CatapultRuler, GiftBox, PlayingCard, Key,
      ThreadSpool(radius, height, color, grabbable), Sponge, Doorway, DeskFan(color, grabbable), Marker(radius, length), TrainEngine, TrainCar
  A toy of your own: MeshKit meshes -> MeshUtil.BakeOutlineNormals -> Materials.Toy(recipe, candy) -> ToyInfo.Tag(root, recipe, candy[, up to 3 pool proxy spheres]).
  Open sky: set PropOptions.MaxScale or SkyCap.Add.
```

## Gadgets

Namespace `Toybox.Gadgets`. `new X(ctx, new XOptions { ... })` in `Build`, in feed order; the mirrors on
`game.Events` are in brackets, their payload is a `GadgetEvent`.

```text
  Zone.Box(center, size[, rotation]) | MinMax(min, max) | Sphere(center, r) | Cylinder(x, z, r, yMin, yMax)
  PressurePlate { Sensor, Mode Sum|Heaviest, MinMass, AcceptPlayer, AcceptTag, Settle, SettleSpeed, Latch, Debounce, LockProp, LockSeconds, LockPoint, Visual, Travel }
      -> Pressed, Load, PressedBy; OnPressed(prop), OnReleased, OnRejected  [PlatePressed, PlateReleased, PlateRejected]
  Socket { AcceptTag, Capture, MinScale, MaxScale, YawAxis, YawReference, YawTolerance, MaxSpeed, SeatPose(scale), SeatScale(scale), EaseSeconds, ThenFallTo,
           ThenTurnDegrees/Axis/Seconds, LockOnSeat, AirCapture, Lamp } -> Seated, SeatedProp, Fit(scale|prop), Release(velocity)  [SocketSeated, SocketUnseated, SocketRejected]
  Door { Panel | Size, ClosedPosition/Rotation, OpenPosition/Rotation, Motion Slide|Hinge, HingePivot/Axis/Angle, Seconds, Ease, StartsOpen } -> Open(), Close(), IsOpen, Mover  [DoorStarted, DoorOpened, DoorClosed]
  Breakable { Center, Size, Rotation, AcceptTag, MinMass, MinSpeed, Direction, OnBreak RemoveCollider|SwapCollider, FlattenedHeight, Body } -> Broken, Watch(prop)  [BreakableBroke, BreakableBonked]
  HazardZone { Shape, Delay, OnCaught } [HazardCaught]
  PropLeash { Props | Tag, Allowed[], Forbidden[], Grace, Action Respawn|Eject, Port } [LeashReturned]
  ReturnPort { Mouth, EjectVelocity, PlayerPosition, PlayerYaw } -> Eject(prop|player) [PortEjected]
  FitGauge { Target (Socket) | MinScale..MaxScale, Near, Tag, Lamp } -> State [GaugeChanged]
  Funnel.Build(ctx, FunnelOptions { Axis, RimY, MouthRadius, ThroatRadius, ConeDepth, TubeLength, ChamberHeight, ChamberRadius, OuterRadius, OuterBaseY, CollarHalfSize, HardGate = true })
      -> FunnelResult { GameObject, Footprint, ChamberBox, PlateCenter, ThroatY, FloorY }
  WindStream { Center, Size, Rotation, Direction, Strength, PlayerAirPush, PlayerGroundPush, PropDrag, MaxPropAcceleration, MaxPropMass, PropLift } [WindChanged]
  SailRaft { Prop, Stream, Launch, Landing, ... } -> State [SailMoored, SailStalled, SailLaunched, SailDocked]
  BouncePad { Prop | Surface + Scale, GainPerScale, MinImpact, MinNormalY, Cooldown } -> LaunchSpeed(s), Apex(s) [Bounced]
  Seesaw { Pivot, ArmADirection | Axis, ArmA, ArmB, Width, Thickness, TipTaper, Plank, RestSide, PlankBias, RiderBias, ProjectileArm, SeatArm, ... } -> State, LaunchSpeed(f, x)  [SeesawStruck, SeesawLaunched, SeesawReturned, SeesawProjectile]
  Train { Center, Radius, DeckY, AngularSpeed, StartBearing, EngineArc, CarArc, Cars, StationBearing, ... } : ICarrierBeds -> CarMover(i), BedBox(i), LapSeconds [TrainAtStation]
  PropCarrier { Beds, AcceptTag, FlatDot, MinRadius, MaxRadius, EaseSeconds } (create after its Train) [CarrierCaptured, CarrierDropped]
  FloatPlatform { Platform | Size, Position, Volume, RestY, MaxY, MaxSpeed, Acceleration } -> AtTop, Mover [FloatLeft, FloatArrived]
  WaterVolume { Footprint, FloorY, Area, Volume, MaxSurfaceY, OverflowTo, LeakTo, LeakRate, WadeDepth, SweepDelay, OnSwept, Visual } -> Add, Take, SurfaceY [WaterLevelChanged, WaterOverflowing, WaterSwept]
  Sponge { Prop, Volumes, CapacityPerScale3, AbsorbRate, SqueezeRate } -> Stored, EmptyInto(volume) [SpongeSoaking, SpongeWringing, SpongeFull, SpongeDry]
  PathDrive { Body | Mover, Segments, Offset, EndVelocity, GhostToPlayer } -> Start(), Abort() [PathSegmentEnded, PathFinished]
      PathSegment.Roll | Ballistic | Ease | Spline | Hold
  HingeChain(ctx, HingeChain.ForDominoSet(prop)) -> Start(), Reset(), Fallen, Struck [ChainPieceFell, ChainTargetStruck, ChainFellShort]
  NestedSet { Props } [NestRevealed]
  LaserRain { Center, Size, Direction, Up, Range, LatticePitch, ZapDelay, Lane, OnZapped } -> Exposed, Coverage01 [LaserZapped, LaserAllClear]
  PortalDoorway { Prop, MinPlayerScale, MaxPlayerScale, Cooldown, SettleSpeed, ... } -> TargetScale, Crossings [PortalSettled, PortalCrossed, PortalBlocked]
  RecallPad { Position, Radius, Prop, HoldSeconds } [Recalled]
  MachineDirector { Start, Toys, Links, Resets, FizzleTimeout, ResetSeconds, Messages } -> Begin(), Report(link), Fail(message), State [MachineStarted, MachineFizzled, MachineReset, MachineDone]
  SkyCap.Add(ctx, xMin, xMax, zMin, zMax, y, thickness = 1);   SignalLamp(renderer, signal).Set / Pulse(seconds) / Off
  Sensors test prop centres in Prop.Id order; a held prop is sensed by nothing. Friction is the coefficient now (Physics.improvedPatchFriction).
```

## Presentation a level can rely on

```text
  Signal.EmissionAt(float gain) -> Color (hue-preserving HDR; Signal.Emission = EmissionAt(Gain))
  PresentationContext.EscapeClaimed {get;set}
  PrefStores.EditorMemoryKey (SessionState flag: Play Mode on a memory store)
  Presenters:
    ExitMarks (220): Count, RendererOf(i), OpenOf(i)
    DimensionCallout (215): Shown, Height, Text, Opacity, Root
    RimFade (252): Count, ShownOf(prop)
    ImpactShake (260): Offset, Amplitude, static AmplitudeFor(mass)
  HeldLook.ThinCaster, HeldLook.CastsShadow(prop); PoolSystem.LongAspect / OwnHue / HaloCap / RingBoost; PostLook.ExposureTrim
  GadgetSounds.Named (event name -> SoundId); UiCapture.OverlayFor(camera), Overlay, PictureHeight; MenuBackdrop.BlurPending, BlurWaitLimit
  Shots.Photograph(camera, width, height, msaa = 1) -> Texture2D (HDR path; PipelineSetup.Photograph delegates)
  Presentation does by itself: exit marks, pools, rim, focus sweep, sticker, release flash, splash ring, dimension callout, impact shake, landing and gadget sounds; ctx.Say = hint toast; Blurb = level card.
```

## Pictures of a level

From PowerShell in the repository root, so that `-UnityArgs` reaches the method; the files written are
echoed as `[Toybox] shot: ...`. Pictures go to `tools/out/shots`, which is not in the repository.

```powershell
  $u = './tools/unity.ps1'
  & $u exec -Method Toybox.EditorTools.Shots.Capture -UnityArgs '-toyboxLevel','3','-toyboxTimes','0,2,5','-toyboxOut','tools/out/shots/level03','-toyboxOverview'
  # with the UI: add '-toyboxUi','hud'   (hud | toast | title | pause | hints | settings | select | catalogue | complete | ending)
  # tier: '-toyboxQuality','low|medium|high';  debug look: '-toyboxPlain';  size: '-toyboxSize','1920x1080'
  & $u exec -Method Toybox.EditorTools.RoomShots.Capture -UnityArgs '-toyboxLevel','3','-toyboxEnv','all'      # the level in every room
  & $u exec -Method Toybox.EditorTools.LookShots.Capture -UnityArgs '-toyboxEnv','all'                         # the look-check stage (held at 3 s, released at 4 s)
  & $u exec -Method Toybox.EditorTools.PipelineSetup.CaptureTiers -UnityArgs '-toyboxLevel','3'                # all three tiers with measurements
  & $u playcheck -UnityArgs '-toyboxUrl','?level=3'                                                           # Play Mode through GameRunner
  # Times are seconds of the level's Solve script; a held toy only shows at a time inside a hold.
```

## Added after phase 2 (engine rules a level author must know)

- A dynamic prop heavier than the player (mass > 3) that comes at them faster than 1 passes through them; one under their feet carries / lifts them at up to 6 units a second and passes when faster. Levels need no guard of their own; a fast raft or a catapult for the PLAYER is a gadget's job (kinematic Mover, BeginDrive, player.SetVelocity - see ARCHITECTURE, "Props and the player's body"). Props at or below the player's mass still push by their momentum.
- The shared Funnel's chamber floor is a zero-thickness sheet that small fast marbles can pass through: give a hopper its own solid blocks and a catch net as Level 8 does (Level08*.cs), or fix the gadget with tests.
- The Seesaw throws its rider through the gadget (not through raw contact), a boulder picked up again mid-swing calls the swing off, and one let-go is one strike (SeesawTests).
- The HUD says "let go" (not "drop") and "pick up"; the scale readout docks at the bottom edge and moves to the top right corner when a very large held toy would reach it.
- Lessons and as-built numbers of levels 1-9: [LEVELS.md](LEVELS.md) appendices D and E; the close-out logs
  [design-notes/phase-1-campaign.md](design-notes/phase-1-campaign.md) and [phase-2-campaign.md](design-notes/phase-2-campaign.md).

## Added in phase 3 (levels 10-15 and the end of the game)

- PropOptions.SquareOnGrab / prop.SquareOnGrab: on a pick-up the toy's heading eases to the nearest quarter turn of the view (Levels 10, 11, 14).
- WaterVolumeOptions.Dip (null = the room's dip): the water's body and surface are drawn in that dip (Level 13: Palette's Pool in the Peach room). Ripples stay inside their volume's footprint.
- Sponge: CapacityPerScale3 / AbsorbRate / SqueezeRate are the level's to set (Level 13: 0.48 / 200 / 24; the defaults 0.315 / 150 / 24 are LEVELS' text).
- PortalDoorway: static Carried(velocity, ratio, back) - through the door a walk scales with the size, a jump with its square root; FootSlack 0.1 (entered feet first), MaxDrop 1 (no crossing out onto nothing: PortalBlocked).
- LaserVisuals draws "Laser Glow" (a pinpoint under whatever roofs a beam). DimensionCallout hides when its toy is respawned.
- FlatBlend.Multiply is written Zero, SrcColor (same product; Flat.shader reads both).
- A toy with KeepUpright hangs by its rim from any ledge: beside a pit or a door, give the level a rule for a toy at rest over the drop (Level 12).
- The engine refuses a click at the prop that is underfoot (`player.Underfoot`): the one the feet are on and, in the air, still the one they left - until the feet are on something else. (Through phase 3 it was refused only while the feet were ON it, which is why Level 10 keeps its own "last stood on": StandOn / FeetLine. It still does, to say why the click did nothing.)
- The measured hop: 1.30 up always, 1.33-1.35 sometimes, 1.40 never (a player of size P: times P). Design gates from that, not from 1.15.
- GAME FLOW: FlowState.Ending. flow.Advance() is what follows a completion (next level, or the ending after the campaign's last); flow.NextLevel() still wraps (level keys).
  flow.ShowEnding(), flow.Report() -> CampaignReport { Levels, Collected, CollectedCount, Total, Full, Seconds, PickUps, Watched }, flow.PickUps, flow.AtLastLevel.
  Progress: PickUps(id), Endings, RecordEnding(), TotalTime(ids), TotalPickUps(ids), RecordCompletion(id, seconds, pickUps = -1).
  MenuPresenter.Ending (EndingMenu): Entries, CardsShown, Stamp, Info, Again / LevelsButton / TitleButton, Report, InfoAt. CompleteMenu.NextLabel / FinishLabel.
- SCREENSHOTS: '-toyboxUi','ending' (the ending with every registered campaign level collected; use '-toyboxTimes','1.2,4' to see the stamp and the settled screen).
  Sheets of the whole campaign: [media/campaign-spawn-sheet.png](media/campaign-spawn-sheet.png) and [media/ending-sheet.png](media/ending-sheet.png) are two of them
  (made in the builders' local `tools/out/shots/phase-3`; the times at which each level's solver holds its toy are in
  [design-notes/phase-3-holds.txt](design-notes/phase-3-holds.txt)).
- LightingRig.Dawn (0..1), LightingRig.DawnSeconds = 4, LightingValues.Dawn(env) / Blend(from, to, t): in a night room the night turns to morning when a MachineDirector is done (Level 15's alarm); a level load is night again.
- PLAY MODE: playcheck on the campaign's last level waits for the ending instead of a load ("the game showed its ending").
- Lessons and as-built numbers of levels 10-15: [LEVELS.md](LEVELS.md) Appendix F; the close-out log
  [design-notes/phase-3-campaign.md](design-notes/phase-3-campaign.md).

## Added in the final review

- A solver that jumps off a toy and wants it back has to land on something else first (`player.Underfoot`, above).
- The debug keys (`[`, `]`, `P`) are live only with `?debug=1`, with `?autoplay=1` and in the editor's Play Mode; while playing, `R` restarts only when held for half a second.
- A budget test sets `Materials.Tier` before it loads the level (the toys' materials are chosen while the level builds), and a level with a `PortalDoorway` is counted with its second camera: `SceneCensus.HasSecondView(game, tier)`, `census.WithSecondView()` (`BudgetCensusTests`).
- Never end a loop on a float stepped by a computed remainder: count with an integer. The WebGL build's floats are strictly 32-bit and such a loop froze Level 5 there while every editor test passed (ARCHITECTURE, rule 2).

# Phase 3 campaign close-out (levels 10-15) and the end of the game - running log

> **Builder's log, kept as it was written.** It records one stage of the build; later stages changed some of what it
> says (LEVELS.md's appendices and ARCHITECTURE.md are current). Paths under `tools/out` are the builders' local
> logs and pictures and are not in the repository.

Started 2026-10-11 01:30. Restart-safe: read this first. Scratch scripts: scratchpad/p3 (edit_*.py are applied already).

## Status: DONE (2026-10-11 02:50)

## Done
- (01:33) typecheck OK. No notes file existed: this is the first run of the close-out.
- (01:33-01:41) Whole suite as found: **2091 total, 2027 passed, 0 failed, 64 skipped (explicit)**, 458 s. Nothing to fix.
- THE ENDING (task 6), code + tests, all green ("GameFlowTests|GameRunnerTests|MenuTests|UiCaptureTests" 63/63):
  - Platform/GameFlow.cs: FlowState.Ending; Advance() (the card's Next and the countdown: next level, or the
    ending after the campaign's last level - NextLevel() still wraps, it is the level keys' step); ShowEnding()
    (records the ending and puts the bookmark back on level 1 when the player's progress is recorded);
    ReturnToTitle() from the ending loads the first level behind the title; PickUps; AtLastLevel; Report() /
    static ReportOf(levels, progress) -> CampaignReport (collected, seconds, pick-ups; Watched = a bot's session).
  - Platform/Progress.cs: PickUps(id) (kept with a best time), Endings, RecordEnding(), TotalTime, TotalPickUps;
    RecordCompletion(id, seconds, pickUps = -1); Reset forgets them.
  - Platform/GameRunner.cs: in Ending without menus a click goes to the title.
  - UI/EndingScreen.cs (NEW): EndingMenu - "THE END", "15 OF 15 COLLECTED", the campaign's 15 blister cards
    sticking on one by one, a COLLECTED stamp + paper confetti when all are collected, a result card (thank-you,
    total time, pick-ups, Play again / Levels / Title). Buttons are not interactable before the card is up.
  - UI/MenuPresenter.cs (Ending screen, Surface backdrop), UI/MenuScreens.cs (the last level's pill says
    "Finish"; GoOn -> Advance), UI/UiRoot.cs (capture '-toyboxUi','ending'; UiCapture.FullProgress),
    UI/DebugHud.cs (prompt), Editor/PlayCheck.cs (reports the ending; watch 70 s / timeout 140 s for level 15).
  - Tests: GameFlowTests (+3, 1 changed), PresentationTests.GameRunnerTests (AfterTheLastLevelComesTheEnding...,
    TheLevelKeys_StillStepRoundTheList...), UiTests.MenuTests (+5: Finish -> ending timeline; keyboard only;
    levels missing / reduce motion; a bot's ending; layout at 15 cards), UiCaptureTests (ending).
  - Picture: tools/out/shots/phase-3/work/ui-ending/level15-t01.2.png (looked at: fine).
  - Play Mode ?level=15&autoplay=1: PLAYCHECK OK, "completed the level after 44.3 s and the game showed its ending".

- (about 01:55) NEW Tests/EditMode/Levels/Phase3CampaignTests.cs, 8 tests, all green: order 1-15 and phase 3's place;
  title -> fifteen cards -> "Finish" -> ending -> catalogue (15 of 15, batch's hero glyphs) -> title -> a new
  session starts at level 1 with the catalogue kept; progress continues in phase 3 / straight to ?level=15 gives
  "1 OF 15 COLLECTED"; the bot solves 10-15 in one Game forwards and backwards (same time to the tick);
  **Autoplay_PlaysTheWholeCampaign_OneToFifteen_InOneGame_AndArrivesAtTheEnding** (real runner, ?autoplay=1:
  loads 1..15 once each, completes each once, ends on the ending "15 OF 15 PLAYED", store empty);
  hero toy under the crosshair + candy colours; one voice (ASCII, pick up / let go, FarLine = Level 1's, toast
  <= 110, hint 3 starts "Pick" and says "let go"); every hint AND every blurb of all 15 at full size.
- Measured solve times (game seconds to LevelCompleted, own solver; identical by autoplay in one game 1..15):
  L1 12.63 | L2 9.28 | L3 3.77 | L4 14.18 | L5 17.35 | L6 12.12 | L7 3.68 | L8 20.50 | L9 11.75 |
  L10 17.53 | L11 6.10 | L12 13.97 | L13 14.32 | L14 9.95 | L15 40.75  (total 207.9 s).
  (The reviewers' 17.82 / 6.27 / 14.60 / 10.07 are their scripts' ends, a few ticks after LevelCompleted.)
  Solver's toys at the end: L10 1.912 / 0.971 / 1.081; L11 5.947; L12 key 0.15 in the lock, spool 9.162;
  L13 sponge 1.862; L14 door 1.319; L15 dominoes 3.103, ball 3.000, ruler 1.012, fan 0.924.
- Level 15: ShortArmLine "... Take it off." -> "... Pick it up." (the campaign's verb; the one-voice test found it).

- (02:05-02:25) Contact sheets, tools/out/shots/phase-3: level10..15-sheet.png, campaign-spawn-sheet.png (15 spawn
  views), campaign-held-sheet.png (15 toys held over their targets), ending-sheet.png (the "Finish" card, the
  ending at 1.2 s and settled, the catalogue of a new game). Work pictures + capture.ps1 / capture2.ps1 /
  make_sheets.py / playchecks.ps1 in tools/out/shots/phase-3/work. All looked at. Hold times:
  tools/out/notes/phase-3-holds.txt (explicit Phase3CampaignTests.Tour_HoldTimes).
- Level 13's five explicit probes re-run on the final numbers (tables level13-probe-*.txt are current).
- The ending's stamp plays the level-complete flourish (UiSound.Fanfare; AudioPresenterTests).
- A captured complete card of the last level says "Finish" too (no flow in a capture).
- Docs written: LEVELS.md (status, section 1, "As built" notes for 10-15, Appendix F with the end of the game),
  ARCHITECTURE.md (SquareOnGrab, gadget notes, the ending in "HUD and menus" and "Game flow", "phase 3 as built",
  one deviation row, testing section), ART_BIBLE.md (9.8 The end of the game; 10.5 Ending),
  tools/out/notes/m2-final-api.md ("Added in phase 3"). Scripts: scratchpad/p3/docs_*.py (each refuses to run twice).

## Reviewers' open items: what became of each
Level 10
- Top box picked up from 1.2-1.4 before the nest tumbles and knocks the step: unchanged (told by InnerClampLine,
  the stairs still work); documented (LEVELS as built, Appendix F).
- 0.8 nearer than the shoe prints -> a last hop of 1.33-1.36 fails; a hop that starts a step early bumps the face;
  the eye is below the red box's top; third box by hand never works: unchanged, each is told; documented.
- Leaning blue box (1 of 30): not pursued (no way up by itself per the review; the two-box margin is tested).
- Hop limit fuzzy 1.30-1.37: engine; documented in ARCHITECTURE ("a clamp is a design tool only against the hop
  that was measured") and m2-final-api.md.
- Per-box clamp (inner boxes 1.15) is a spec deviation: WRITTEN into LEVELS (Level 10 as built, Appendix F).
Level 11
- Beams magenta, not red: DECIDED for the art bible (Hazard #FF2BD6 is the game's one hazard colour; the level's
  own "no roof" cue is red). Written into LEVELS as a deliberate departure from the draft; one constant to change.
- The lean-to (second way through): KEPT, as Level 9's pier was; written into LEVELS and Appendix F.
- Spade silhouette not built; gauge shy at 4.29-4.38; HUD pill lingering 1.2 s; level card over the wall's corner:
  unchanged, documented.
- LEVELS as-built note: written.
Level 12
- Narrow spool window / marks / smallest workable spool / invisible "high" rule / door timing: unchanged (all
  told or tested); documented.
- "Any other level that gives a toy KeepUpright next to a pit": CHECKED - only the ThreadSpool has KeepUpright in
  the catalog and only Level 12 places one (Levels 1, 5, 6 draw their spools as set dressing). Rule written into
  ARCHITECTURE and m2-final-api.md.
- LEVELS as-built text: written from the three reviews' lists.
Level 13
- Spec deviations (0.48 / 200 / 1.1) and WaterVolume.Dip: WRITTEN into LEVELS, ARCHITECTURE (gadget signature +
  note) and m2-final-api.md.
- Lagoon sponge on pool-blue water: looked at in the sheets - the sponge reads (rim, pores, sticker border);
  kept; recorded in ARCHITECTURE's "departs from the art bible" table.
- Held sponge hides the footprint; near-third aims fail once; 12 s refill; sponge under the closing flap; bot's
  path on one side: unchanged, documented.
- The builder's five explicit probes with stale tables: RE-RUN, tables current.
Level 14
- Carried door hides the picture; second trip's window; scramble at 2.2-2.47; walk off the ledge through the top
  of a big door; portal view through a much bigger door; dim paper doors: unchanged, documented.
- Phase 1 / 2 suites after the gadget change: RUN (whole suite, 0 failures).
- LEVELS still had the drafted numbers: as-built note written (ledge 3.4 + kerb 0.25, stations 11.2 / 17.8 /
  26.75, dashes 12.3 / 18.6 / 28.65, gates 0.47 / 2.48 / 2.00).
Level 15
- Bottom quarter of the dominoes' outline; slanted pick-up seats at 2.0-2.2; small seated sets found out by the
  run; the gift of 3.5 from the start; approach flight through an edge; "caught on the edge" vs "turn with Q";
  FarLine at 3.5 from the start; baseball small from the pedal: unchanged, documented.
- "Take it off." -> "Pick it up." (ShortArmLine; found by the one-voice test).
- Night-to-dawn ending: BUILT (below).
All levels: a WebGL build was not made or checked (forbidden here).

## The dawn (Level 15's wow beat "the night preset turns to dawn", the last open item)
- SHARED Render/LightingRig.cs: LightingValues.Dawn(env) (the sunny-rug's sun and window patch, the day's ambient on
  the room's own tones, the day's glint, toy glow back to 1) and Blend(from, to, t); LightingRig.Dawn (0..1),
  DawnSeconds = 4: in a NIGHT room, MachineDone starts the dawn; a level load is night again; day rooms take no
  notice; no allocation per frame. The room stays the Plum room (a change of light only).
- Tests: RoomTests.LightingRigTests.InANightRoom_TheNightTurnsToDawn_WhenTheMachineIsDone_AndTheNextLoadIsNightAgain,
  ADayRoom_TakesNoNoticeOfAMachine; Phase3CampaignTests.TheLastLevel_EndsInTheMorning_... (the solver: the alarm
  rings at 34.53 s, out at 40.77 s, Dawn = 1 by then).
- Picture: tools/out/shots/phase-3/level15-dawn-sheet.png (looked at: blue night -> warm grey-lilac walls).

## One failure in the first "final" suite, fixed
- PlatformTests.WalkingThroughLevelsLeaksNoUnityObjects completed the last of its three levels and waited for
  "the runner moves on": that is the ending now. The test walks through the ending too (asserts it, lets it run
  two seconds, goes on by the autoplay key) and still finds no object left behind.

## Final runs (final code; nothing in Assets/ was edited after 02:29)
- Typecheck OK.
- Whole suite (02:29-02:37): **2112 total, 2047 passed, 0 failed, 65 skipped** (the explicit probes and tours), 426 s.
  (As found at 01:33: 2091 / 2027 / 0 / 64.)
- Play Mode, every level by ?level=N&autoplay=1 (02:37-02:45), all PLAYCHECK: OK, no problems, autoplay error none
  (tools/out/notes/phase-3-playchecks.txt; the run before the dawn change is phase-3-playchecks-run1.txt):
  L1 16.0 s -> 2 | L2 12.5 -> 3 | L3 7.0 -> 4 | L4 17.6 -> 5 | L5 20.6 -> 6 | L6 15.4 -> 7 | L7 6.9 -> 8 |
  L8 23.8 -> 9 | L9 15.2 -> 10 | L10 20.9 -> 11 | L11 9.5 -> 12 | L12 17.5 -> 13 | L13 17.8 -> 14 | L14 12.9 -> 15 |
  L15 44.1 -> "the game showed its ending (level 15 is the campaign's last; nothing was loaded)".
  No address: title over level 1, the check's autoplay completes it (16.0 s), the runner loads level 2.
  (Wall-clock seconds, start-up frames included.)
- Solve times in game seconds: see "Measured solve times" above (207.9 s for the fifteen).

## Shared code changed by this close-out (each with tests)
- Platform/GameFlow.cs, Progress.cs, GameRunner.cs; UI/EndingScreen.cs (new), MenuPresenter.cs, MenuScreens.cs,
  UiRoot.cs, UiControls.cs, DebugHud.cs; Audio/AudioPresenter.cs (UiSound.Fanfare); Editor/PlayCheck.cs;
  Render/LightingRig.cs (the dawn). Level15RubeGoldberg.cs (one line's wording).
- Tests: GameFlowTests, PresentationTests (GameRunnerTests), UiTests (MenuTests, UiCaptureTests), AudioTests,
  RoomTests (LightingRigTests), PlatformTests (one test follows the ending), Levels/Phase3CampaignTests.cs (new, 9
  tests + the explicit Tour_HoldTimes).
- Nothing was committed (not allowed here). No .meta / .asset / ProjectSettings edited by hand; Unity made the two
  .meta files of the two new .cs files.

## Seen in the pictures, left as it is
- Level 13: held and let go, the sponge fills the picture from the castle (the text's design); the dry bowl shows
  as soon as the player walks round it (t 5.5 in the sheet).
- Level 14 is dark by design (night-light); the door, its lime pool and the exit mark carry the picture.
- Level 15's overview is small and dark (the level is 56 units wide); its sheet uses the view from the pedal.
- The ending's stamp touches the top edge of card 05 by a few pixels (a stamp may).

## Not done
- A WebGL build of any of this (forbidden here): the ending on a real screen, its keyboard and mouse through the
  EventSystem, the dawn in the browser are the first things to look at.

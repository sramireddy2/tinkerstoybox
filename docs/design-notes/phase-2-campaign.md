# Phase 2 campaign close-out (levels 5-9) - running log

> **Builder's log, kept as it was written.** It records one stage of the build; later stages changed some of what it
> says (LEVELS.md's appendices and ARCHITECTURE.md are current). Paths under `tools/out` are the builders' local
> logs and pictures and are not in the repository.

Started 2026-10-04 23:25. Restart-safe: read this first.

## Status: DONE (2026-10-05 00:42)

## What was done

1. Whole suite at the start (23:27-23:38): 1447 total, 1413 passed, 0 failed, 34 skipped (explicit). Nothing to fix.
   (The Level08Tests failures two reviewers saw were that level being worked on at the time.)
2. NEW Assets/Toybox/Tests/EditMode/Levels/Phase2CampaignTests.cs (7 tests): order 5-9 after 1-4; title -> nine
   "Next"s -> the catalogue's cards (glyph, room colour, COLLECTED, best time); progress written and continued in
   phase 2; the bot solves the five in one Game forwards and backwards (same time to the tick); the hero toy under
   the crosshair at the start and nothing else to pick up (Level 8: three marbles in the picture, two at hand); one
   voice (ASCII, pick up / let go, FarLine = Level 1's sentence in all five, toast <= 110 characters, first hint
   names no place, third hint "Pick ... let go"); every hint at the pause card's FULL size (20 pt, five lines).
3. Solve times (game seconds, own solver, in that test): L5 17.35 | L6 12.12 | L7 3.68 | L8 20.50 | L9 11.75.
4. Play Mode (all PLAYCHECK: OK, completions 1, autoplay error none):
   ?level=5&autoplay=1 -> completed after 20.6 s, loaded level 6 | 6 -> 15.2 s, level 7 | 7 -> 6.8 s, level 8 |
   8 -> 23.8 s, level 9 | 9 -> 15.0 s, level 1 (nine levels so far: it wraps). No address: title over level 1,
   autoplay completes it, the runner loads level 2. (Wall-clock seconds, start-up frames included.)
5. Contact sheets: tools/out/shots/phase-2/level05..09-sheet.png and campaign-sheet.png (work pictures, the browse
   sheets and make_sheets.py / make_browse.py in tools/out/shots/phase-2/work). Looked at, all six.
6. Docs: LEVELS.md (status, section 1 remark, an "As built" note at the end of levels 5-9, Appendix E);
   ARCHITECTURE.md (gadget signatures and author's notes, "phase 2 as built" section, a deviation row for the HUD's
   words, testing section: Phase2CampaignTests, explicit tests and filters, the stale "one test fails" note).

## Changes (each with a test)

- SHARED Gadgets/Seesaw.cs (Level 7's two open gadget items + the double strike). SeesawTests 6 -> 15.
  1. A rider who jumps while the plank swings is still its rider (RiderOverhead; Carry gives them, tick by tick, the
     upward speed that keeps their feet on the plank's top): no ghosting through them, thrown with the rule's speed.
     A jump at the very end keeps its own, higher speed. Tests ARiderWhoJumpsWhileItSwings... (4 jump times, failed
     before: 0 launches), AJumpAtTheVeryEndOfTheSwing_KeepsItsOwnSpeed.
  2. The weight picked up again in mid-swing (prop.Held / Removed) ends the swing: Throw() with the speed so far, the
     plank swings back. Test TheBoulderPickedUpAgainInMidSwing... (failed before: still Swing).
  3. One let-go, one strike: a tipped plank whose weight is still about waits ReleaseTicks (30) before swinging back;
     picked up / only the player: at once, as before (Level 7's "let go again without waiting" needs that). A swing
     with under MinStrikeShare (10 %) of the travel left raises no strike events and does not reset Launched.
     Test ABoulderThatComesToRestOnTheTippedPlank_StrikesOnce (3.2 / 4.6 / 7; 7 struck three times before).
- Level 7: LeftLine is "The ruler came up without you. Stay on it until it throws you."; PropGrabbed resets
  awaiting / leftBehind / cameIn (a boulder picked up in mid-swing is no try). Level07Tests: the three "a jump loses
  the throw" cases became AJumpWhileTheRulerSwings_DoesNotLoseTheThrow (5) + ..._WithoutAPush_IsThrownAndToldToSteer,
  AJumpWhileLeaningOnTheBooks_IsStillThrownOntoThem, TheBoulderPickedUpAgainInMidSwing_CallsTheThrowOff...,
  OneLetGo_IsOneStrike_AndOneThrow (solver's boulder, 3.05, 7).
- Level 5: SpoolWayLine ("Its spool was in the way. Let go of it from the mark at the balcony edge.") when the held
  feather stopped on its own spool and is put back - that used to be silent.
- Hints that were set smaller than the others (six lines): L6 hint 3 217 -> 198 characters, L9 hint 3 233 -> 202.
  L8 TooLightLine 123 -> 106 characters.
- SHARED UI: the held-control pill says "click: let go" (was "drop"); the level-complete card counts "pick-ups" (was
  "grabs"). UiTests assert both. Seen in the pictures: the pill said "drop" beside toasts that say "let go".
- Level01Set.cs: header says it is shared by Levels 1, 5 and 6 (the reviewers' note that Level 6 depends on it).

## Reviewers' open items: what became of each

Level 5
- Fan not in the first picture; first picture pitched lower: unchanged, documented (LEVELS as built).
- Pick-up distance between k 0.73 and 0.52; "from closer" between 0.45 and 0.52: unchanged (the line is a warning
  there), documented.
- Right-hand third of the blotter in the spool's shadow, silent: now told (SpoolWayLine).
- Aimed 3 short: balcony edge catches it, CalmLine: unchanged (told, fetchable).
- Feather rocking for up to 10 s before its line; blocky highlights (toy shading); unseen wall above the block rows:
  unchanged, documented.
- LEVELS as-built note: written.
Level 6
- Spool stops a level eraser walked straight back from it; second pick-up from 8-12 away; spool vanishing under a
  landing eraser; turned 60-90 degrees at the edge of the window; decorative blocks prop it up; hop at the top of a
  slanted end: unchanged, documented.
- Level01Set dependency: made explicit in the helper's header and in ARCHITECTURE.
- Mid-air pick-up test only proves nothing breaks on one path: unchanged.
- LEVELS as-built note: written (from the reviewer's text, with the measured solve time).
Level 7
- Jump early in the swing ghosts the ruler through the player: FIXED in the gadget.
- Boulder picked up in mid-swing still throws; SeesawStruck twice: FIXED in the gadget.
- Books' far corner catches a big-looking pebble aimed > 6 degrees right; raised end hides the boulder from the
  ruler's own line; upper half of the outline under the level card for 3.5 s (tried in thought: pitching the start
  up puts the bullseye out of the picture - the two are at its top and bottom edge): unchanged, documented.
- ClampLine hard to reach, no test: left as the fallback it is.
- LEVELS as-built note: written.
Level 8
- Marble held over the throat stops on the lid; big funnel least forgiving; lamp post in the way from behind:
  unchanged (signalled and told), documented.
- Funnel.cs chamber floor is a sheet: NOT changed (Levels 3 and 8 are measured against it); written into
  ARCHITECTURE's gadget notes for Level 15.
- LEVELS as-built note: written.
Level 9
- Half-unit kerb onto the riding plank; jump from the platform goes over the train: unchanged, documented.
- OWNER DECISION, still open: the pier-on-a-wagon route skips the lighthouse wall and the timing. Kept (LEVELS accepts
  the diving board). The one-line change that removes it is in LEVELS (Level 9, as built) and Appendix E.
- Third hint not seen on the card: it was six lines (set at 19.7 pt); shortened to five, tested for all 15 hints.
- Player.cs LimitSteepFaces: stays; documented in ARCHITECTURE.
- Tour_Pictures runs with a plain filter: that is NUnit ([Explicit] runs when the filter names it); the filters to
  use are in ARCHITECTURE's testing section.
- LEVELS as-built note: written.
Not done anywhere: a WebGL build (forbidden here).

## Seen in the pictures, left as it is
- The scale read-out is a factor of the toy's start size ("x9.3", "x12", "x7.7", "x2.6", "x6.3"), in every level.
- Exit marks are the same four white panes in every level; stand marks are shoe prints on a pad (L7: on the
  bullseye and on the start book); targets are dashes in the toy's colour.
- Level 9's "gripped" cue is a large green ring through the whole view for a moment.
- After level 9 "Next" starts level 1 again (nine levels so far).

## Final runs
- Typecheck OK.
- Whole suite (00:32-00:38): **1470 total, 1436 passed, 0 failed, 34 skipped** (the explicit probes and tours), 394 s.
- After it, two small edits (Seesaw.BeginSwing sets heldDownBy; the campaign test also asserts the solver's let-go
  sizes), then "Phase2CampaignTests|SeesawTests|Level07Tests|GadgetShowroomTests|Level98": 119 / 119, and both
  campaign suites once more: 13 / 13.
- Earlier on the final level files: "Level05Tests\.|Level06Tests\.(?!Probe|Tour|Review_)|Level08Tests\.(?!Probe)|
  Level09Tests\.(?!Tour)|Phase1CampaignTests|Phase2CampaignTests": 334 / 334.
- Solver's let-go sizes, measured in the campaign test: feather 9.329, eraser 9.369 (the review said 9.42), pebble
  4.628, marbles 0.651 / 1.221 / 2.413, plank 4.086.
- Play Mode: see "What was done", 4 (run before the HUD wording change and the last Seesaw edit). Level 7 again on
  the final code: PLAYCHECK OK, completed after 6.9 s, the runner loaded level 8.

## Capture commands (PowerShell; then `python work/make_sheets.py` in tools/out/shots/phase-2)
  $u = './tools/unity.ps1'
  & $u exec -Method Toybox.EditorTools.Shots.Capture -UnityArgs '-toyboxLevel','5','-toyboxTimes','0,0.9,1.45,3.3','-toyboxOut','tools/out/shots/phase-2/work/level05','-toyboxOverview','-toyboxUi','hud'
  level 6: 0,2.9,3.4 | level 7: 0,1.1,1.2,1.45 | level 8: 0,3.78,4.0,14.2 | level 9: 0,7.6,7.9,8.3
  screens: '-toyboxLevel','5','-toyboxTimes','0','-toyboxOut','tools/out/shots/phase-2/work/ui-<name>','-toyboxUi','<name>'
           for title, select, catalogue, complete

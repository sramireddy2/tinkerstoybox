# Design notes (historical)

Drafts and logs from the making of the game, kept for the record. **None of them is the contract**: where
one disagrees with [ARCHITECTURE.md](../ARCHITECTURE.md), [ART_BIBLE.md](../ART_BIBLE.md),
[LEVELS.md](../LEVELS.md) or the code, those win.

| File | What it is |
|---|---|
| `art-pitch-a-vinyl.md`, `art-pitch-b-print.md`, `art-pitch-c-glow.md` | The three art directions pitched before the look was chosen. They **predate the move to Unity**: they are written for the three.js / Rapier prototype (removed since), so their API names, shadow techniques and GPU figures do not apply. Direction A, "Dip & Die-Cut", became [ART_BIBLE.md](../ART_BIBLE.md) |
| `levels-phase-1-draft.md`, `levels-phase-2-draft.md`, `levels-phase-3-draft.md` | The first level specifications, hand-calculated before anything ran in the engine. Superseded by [LEVELS.md](../LEVELS.md), whose "as built" notes and appendices D, E and F hold the measured numbers |
| `phase-1-campaign.md`, `phase-2-campaign.md`, `phase-3-campaign.md` | The builders' close-out logs of the three phases of the campaign (levels 1-4, 5-9, 10-15 and the ending): what was changed, what the reviewers' open items became, the runs. Kept as written; each describes the game at the end of its own phase |
| `phase-3-holds.txt` | When each level's solver picks its toy up and lets it go, and at what size, measured on the code of the final review (2026-10-11; `Phase3CampaignTests.Tour_HoldTimes` writes it). At the end of phase 3 only Level 4 differed: it started at the back of its balcony and its solver took 2.25 s longer |

The logs and the reference documents mention paths under `tools/out` (notes, probe tables, pictures):
that folder is the builders' local output, git-ignored and **not in the repository**. What a reader
needs from it has been copied here, into [../media](../media) and into
[LEVEL_AUTHORING.md](../LEVEL_AUTHORING.md).

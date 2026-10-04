# Tinker's Toybox

A first-person puzzle platformer where you play a tiny action figure in an oversized playroom.
Your only tool is **forced perspective**: grab a toy, and it keeps its on-screen size while it lands on
whatever is behind it. Pick up a block in front of your face, look across the room, let go — and it
arrives as a building-sized monolith.

**Play it:** https://sramireddy2.github.io/tinkerstoybox/

Built with **Unity 6** (URP, WebGL). No imported art or audio: meshes, textures, shaders and sounds are
all generated in code.

## Controls

| Input | Action |
|---|---|
| `W` `A` `S` `D` | Move |
| Mouse | Look (click the game to capture the mouse) |
| Click / `E` | Grab / release |
| `Q` / mouse wheel | Turn the held object |
| `F` | Tip the held object 90° |
| `Space` | Jump |
| `Shift` | Sprint |
| `R` | Restart level |
| `Esc` | Pause (frees the mouse); `Esc` or a click resumes |

Add `?level=3` to the address to start on a level, `&autoplay=1` to watch the built-in bot solve it, and
`&plain=1` for the plain debug look.

## How the mechanic works

```
NewScale = OldScale × (NewDistance / OldDistance)
```

While you hold an object it is continuously projected along your line of sight to the farthest free
spot, scaled so that it covers exactly the same part of your screen. Releasing it hands it back to the
physics engine at that size — with mass to match.

## Opening the project

Open the repository root in **Unity 6000.6.4f1** (Unity Hub → Add → project from disk) and press Play:
the game starts on its title screen whichever scene is open. Keep the project **outside OneDrive /
Dropbox** — synced folders make Unity's file operations fail in odd ways.

The **Toybox** menu in the editor has the rest:

| Menu item | What it does |
|---|---|
| Play Game | Enters Play Mode on the title screen |
| Level Launcher | A window listing every level, each with **Play** and **Watch bot** (the built-in solver plays it) |
| Build WebGL | Makes the browser build into `Build/WebGL` |
| Capture Shots | Renders stills of a level without entering Play Mode |
| Run Project Setup | Regenerates the scene, materials, fonts and render settings from code |

Everything is built from C#: levels, toys and environments are code, not hand-placed scene content, so
the Main scene holds a single `Bootstrap` object and the Hierarchy fills in when the game runs.
See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the engine contract.

## Command-line tooling (Windows PowerShell)

```powershell
.\tools\typecheck.ps1
```

Compiles the game's C# with Unity's own compiler in a few seconds, without starting Unity.

```powershell
.\tools\unity.ps1 test
```

Runs the EditMode tests through a background headless editor. The simulation is stepped manually, so
each level ships with a test in which a scripted bot actually solves it — no Play Mode needed.

```powershell
.\tools\editor-check.ps1 -Level 1,2,3
```

Opens the project in the real, windowed Unity editor (on a private copy), enters Play Mode on each
level, lets the bot solve it and closes the editor again.

```powershell
.\tools\unity.ps1 build
```

Makes a WebGL build.

```powershell
.\tools\unity.ps1 stop
```

Shuts the background editor down (do this before opening the project in the Unity editor yourself).

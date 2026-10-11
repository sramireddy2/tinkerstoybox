<div align="center">

# Tinker's Toybox

**You're a tiny action figure. The playroom is huge. Your only tool is perspective.**

![The game's seven candy colours](docs/media/candy-ribbon.png)

[![Play it in your browser](docs/media/badges/play.svg)](https://sramireddy2.github.io/tinkerstoybox/)

![Unity 6000.6.4f1](docs/media/badges/unity.svg)
![C# 9.0](docs/media/badges/csharp.svg)
![URP 17.6](docs/media/badges/urp.svg)
![WebGL 2 and WebAssembly](docs/media/badges/webgl.svg)
![Physics: PhysX](docs/media/badges/physics.svg)
![More than 2100 tests passing](docs/media/badges/tests.svg)
![60 frames a second in the browser](docs/media/badges/fps.svg)
[![Licence: MIT](docs/media/badges/licence.svg)](LICENSE)

![Level 1, played by the built-in bot: a crumb of cheese is picked up, held against the far wall, let go as a hillside, and climbed](docs/media/gameplay.gif)

</div>

## The trick

Pick up a toy and it keeps the size it has **on your screen**. So:

- let it go **far away** and it lands **huge** — a crumb of cheese becomes a hillside;
- let it go **at your feet** and it lands **tiny** — an apple the size of a boulder fits on a button.

```
NewScale = OldScale × (NewDistance / OldDistance)
```

That is the whole game: fifteen rooms, one idea, and physics that takes the new size seriously.

**[Play it in your browser](https://sramireddy2.github.io/tinkerstoybox/)** — on a computer, with a
keyboard and a mouse.

## Screenshots

<table>
  <tr>
    <td><img src="docs/media/level-02.jpg" alt="Level 2: a silver thimble the size of a water tower drops into the hole in the floor"></td>
    <td><img src="docs/media/level-04.jpg" alt="Level 4: a purple domino grown to eight times its size stands in front of a wall of blocks"></td>
    <td><img src="docs/media/level-09.jpg" alt="Level 9: a plank held out over a circling toy train toward the lighthouse"></td>
  </tr>
  <tr>
    <td><img src="docs/media/level-11.jpg" alt="Level 11: a playing card lies as a roof on two low walls under a rain of red laser beams"></td>
    <td><img src="docs/media/level-13.jpg" alt="Level 13: a blue sponge the size of a room held over the fishbowl"></td>
    <td><img src="docs/media/level-15.jpg" alt="Level 15: a row of green dominoes, five times their size, in the night-time machine room"></td>
  </tr>
</table>

![The title screen: the first room, the cheese wedge on its pedestal](docs/media/title.png)

## The fifteen levels

![Where each of the fifteen levels starts](docs/media/campaign-spawn-sheet.png)

| | Level | The idea |
|---|---|---|
| 🧀 | 1 The Cheese Wedge | Far away means big |
| 🧵 | 2 The Thimble Chasm | A size has to fit |
| 🍎 | 3 Shrinking the Apple | Near means small |
| 🧱 | 4 Domino Effect | Bigger is heavier |
| 🪶 | 5 The Fan and the Feather | Bigger catches more wind |
| ✏️ | 6 Bouncing Eraser | Bigger bounces higher |
| 📏 | 7 The Teeter-Totter | A boulder on a lever |
| 🔮 | 8 Funnel Physics | Three exact sizes |
| 🚂 | 9 The Moving Train | A bridge you have to time |
| 🎁 | 10 Matryoshka Boxes | One toy turns out to be three |
| 🃏 | 11 Blocking Lasers | A playing card as a roof |
| 🗝️ | 12 The Keyhole | The key is also the bridge |
| 🧽 | 13 Escaping the Fishbowl | A sponge that carries the water |
| 🚪 | 14 The Infinite Hallway | A door that resizes you |
| ⚙️ | 15 The Rube Goldberg Machine | Everything at once |

Stuck? Every level has three hints on the pause screen.

## Controls

| Key | What it does |
|---|---|
| `W` `A` `S` `D` | Move |
| Mouse | Look |
| Click or `E` | Pick up / let go |
| `Q` or mouse wheel | Turn the held toy (15° steps) |
| `F` | Flip the held toy a quarter turn |
| `Space` | Jump |
| `Shift` | Sprint |
| `R` | Restart: hold it for half a second |
| `Esc` | Pause; `Esc` again or **Resume** goes on |

## Tech stack

| | |
|---|---|
| **Engine** | Unity 6 (6000.6.4f1), C# |
| **Rendering** | URP 17.6 with five hand-written shaders |
| **Physics** | PhysX, stepped by hand at 60 Hz so a run replays exactly |
| **Input and UI** | Input System 1.20, UGUI with TextMesh Pro |
| **Art and audio** | All generated in code: no imported models, images or sound files |
| **Platform** | WebGL 2 and WebAssembly, hosted on GitHub Pages |
| **Tests** | About 2200, including a bot that solves every level |
| **Tooling** | PowerShell, and Python with Playwright for checking the build in a real browser |

## Run it yourself

1. Install **Unity 6000.6.4f1** with the WebGL Build Support module.
2. Open this folder in Unity Hub and press **Play**.
3. Try **Toybox → Level Launcher** in the menu bar: it starts any level, and a built-in bot can play it for you.

To jump to a level in the browser, add `?level=cheese-wedge` (or `?level=3`) to the address, and
`&autoplay=1` to watch the bot solve it.

Building, testing and releasing are in **[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)**. The design is in
the [architecture](docs/ARCHITECTURE.md), [levels](docs/LEVELS.md), [art bible](docs/ART_BIBLE.md) and
[level authoring](docs/LEVEL_AUTHORING.md) documents.

## Credits

Everything in the game is generated in code except its typefaces:

- **Unbounded** Bold — Copyright 2022 The Unbounded Project Authors (https://github.com/googlefonts/unbounded)
- **Figtree** SemiBold — Copyright 2022 The Figtree Project Authors (https://github.com/erikdkennedy/figtree)
- **Liberation Sans**, the fallback face of Unity's TextMesh Pro — Digitized data copyright (c) 2010
  Google Corporation; Copyright (c) 2012 Red Hat, Inc.

All three are under the SIL Open Font License 1.1; the licence texts sit beside the font files and ship
with every build.

## Licence

[MIT](LICENSE) for the game's code and content. The typefaces keep their own licence.

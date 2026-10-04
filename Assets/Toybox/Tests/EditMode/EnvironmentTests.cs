using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.TestTools;

namespace Toybox.Tests
{
    /// <summary>
    /// The environment descriptor: the six presets of ART_BIBLE 6.4 as data, the solve of 6.2 (the sun
    /// lands on the level), the level-to-preset table, GroundY (LEVELS 0.4 request 6), and the colliders
    /// the kit promises - created by Game after a level's Build, in headless tests too.
    /// </summary>
    public class EnvironmentTests : SimTest
    {
        // A level 30 wide and 80 long whose middle is not the origin: nothing in the solve may assume it is.
        static readonly Bounds Level = new Bounds(new Vector3(2f, 3f, 10f), new Vector3(30f, 8f, 80f));

        // key, elevation, azimuth, window side, wall distance L, window centre (x, z) for the bounds above, floor drop
        static readonly object[][] Solved =
        {
            new object[] { "sunny-rug", 45f, 300f, -1, 71.447f, -69.447f, 51.250f, 0.5f },
            new object[] { "block-hall", 40f, 60f, 1, 85.148f, 87.148f, 59.160f, 0f },
            new object[] { "pegboard-workbench", 48f, 285f, -1, 71.752f, -69.752f, 29.226f, 60f },
            new object[] { "cardboard-box", 50f, 75f, 1, 66.867f, 68.867f, 27.917f, 0f },
            new object[] { "high-shelf", 42f, 290f, -1, 86.100f, -84.100f, 41.338f, 75f },
            new object[] { "night-light", 45f, 300f, -1, 71.447f, -69.447f, 51.250f, 0.5f },
        };

        [Test]
        public void Presets_AreTheSixOfTheArtBible()
        {
            Assert.AreEqual(6, EnvironmentPreset.All.Length);
            CollectionAssert.AreEqual(new[] { "sunny-rug", "block-hall", "pegboard-workbench", "cardboard-box", "high-shelf", "night-light" },
                new List<string>(EnvironmentPreset.Keys));

            EnvironmentPreset rug = EnvironmentPreset.Find("sunny-rug");
            Assert.AreSame(EnvironmentPreset.SunnyRug, rug);
            Assert.AreSame(Palette.Mint, rug.Dip);
            Assert.AreEqual("#FFF1DC", Palette.ToHex(rug.SunColor));
            Assert.AreEqual(0.62f, rug.SunIntensity);
            Assert.AreEqual(0.0024f, rug.HazeDensity);
            Assert.AreEqual("#FFE9C4", Palette.ToHex(rug.PatchColor));
            Assert.AreEqual(0.28f, rug.PatchGain);
            Assert.AreEqual(0.5f, rug.PoolGain);
            Assert.AreEqual(1f, rug.ToyGlowGain);
            Assert.AreEqual(0.35f, rug.Bloom);
            Assert.AreEqual(0.2f, rug.Exposure);
            Assert.AreEqual(84, rug.Bpm);
            Assert.AreEqual(RoomPattern.Dots, rug.PlayPattern.Pattern);
            Assert.AreEqual(RoomPattern.Planks, rug.FloorPattern.Pattern);
            Assert.AreEqual(RoomPattern.Stripes, rug.WallPattern.Pattern);
            Assert.AreEqual(IslandKind.Rug, rug.Island);
            CollectionAssert.Contains(rug.Backdrop, FurnitureKind.Bed);

            EnvironmentPreset night = EnvironmentPreset.NightLight;
            Assert.IsTrue(night.Night);
            Assert.AreSame(Palette.Plum, night.Dip);
            Assert.AreEqual("#BFD0FF", Palette.ToHex(night.SunColor));
            Assert.AreEqual(0.5f, night.SunIntensity);
            Assert.AreEqual(6f, night.ToyGlowGain);
            Assert.AreEqual(0.9f, night.PoolGain);
            Assert.AreEqual(0.55f, night.Bloom);
            Assert.AreEqual(76, night.Bpm);
            Assert.AreEqual(MusicScale.MinorPentatonic, night.MusicScale);
            Assert.AreEqual(9, night.MusicRoot, "A");

            Assert.AreSame(Palette.Butter, EnvironmentPreset.BlockHall.Dip);
            Assert.AreEqual(0.66f, EnvironmentPreset.BlockHall.SunIntensity);
            Assert.AreEqual(0.34f, EnvironmentPreset.BlockHall.PatchGain);
            Assert.AreSame(Palette.Pool, EnvironmentPreset.PegboardWorkbench.Dip);
            Assert.AreEqual(0.0028f, EnvironmentPreset.PegboardWorkbench.HazeDensity);
            Assert.AreEqual(30f, EnvironmentPreset.PegboardWorkbench.BackWallGap);
            Assert.AreEqual(RoomPattern.Pegboard, EnvironmentPreset.PegboardWorkbench.BackWallPattern.Pattern);
            Assert.AreSame(Palette.Peach, EnvironmentPreset.CardboardBox.Dip);
            Assert.AreEqual(0.55f, EnvironmentPreset.CardboardBox.PoolGain);
            Assert.AreEqual(0.40f, EnvironmentPreset.CardboardBox.Bloom);
            Assert.AreSame(Palette.Lilac, EnvironmentPreset.HighShelf.Dip);
            Assert.AreEqual(0.0032f, EnvironmentPreset.HighShelf.HazeDensity);
            Assert.AreEqual(0.02f, EnvironmentPreset.HighShelf.PlayPattern.Gain, "laminate: stripes at gain 0.02");
            Assert.IsTrue(EnvironmentPreset.HighShelf.Elevated);
            Assert.IsFalse(EnvironmentPreset.SunnyRug.Elevated);

            Assert.AreSame(EnvironmentPreset.None, EnvironmentPreset.Find("none"));
            Assert.AreSame(EnvironmentPreset.None, EnvironmentPreset.Find(null));
            Assert.IsFalse(EnvironmentPreset.None.HasRoom);
            Assert.IsNull(EnvironmentPreset.Find("lamp-desk"), "the drafts' ad-hoc keys are not presets");
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                Assert.AreSame(preset.Dip, Palette.DipOf(preset.Key), preset.Key + ": Palette and the preset agree on the dip");
                Assert.IsTrue(preset.HasRoom);
                Assert.AreEqual(preset.Elevated, preset.Island == IslandKind.Bench || preset.Island == IslandKind.Shelf);
            }
        }

        [Test]
        public void Solve_TheSunLandsOnTheLevel_ForAllSixPresets([ValueSource(nameof(Solved))] object[] row)
        {
            string key = (string)row[0];
            float elevation = (float)row[1], azimuth = (float)row[2];
            int side = (int)row[3];
            float distance = (float)row[4], windowX = (float)row[5], windowZ = (float)row[6], drop = (float)row[7];
            const float ground = 0f;

            EnvironmentDescriptor d = EnvironmentSolver.Solve(EnvironmentPreset.Find(key), Level, ground);

            // 1. Bounds and focus.
            Assert.AreEqual(Level, d.LevelBounds);
            Assert.AreEqual(new Vector3(2f, ground, 10f), d.Focus, key + ": the focus is the middle of the bounds on the play plane");

            // 2. Sun: azimuth clockwise from +Z seen from above.
            Assert.AreEqual(elevation, d.SunElevation, key);
            Assert.AreEqual(azimuth, d.SunAzimuth, key);
            float e = elevation * Mathf.Deg2Rad, a = azimuth * Mathf.Deg2Rad;
            var sun = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
            Assert.Less((sun - d.SunDirection).magnitude, 1e-5f, key + ": sun direction");
            Assert.AreEqual(1f, d.SunDirection.magnitude, 1e-5f);
            Assert.Greater(d.SunDirection.z, 0f, key + ": the sun is in the front hemisphere of the travel axis");
            Assert.Less(Vector3.Angle(-d.SunDirection, d.SunRotation * Vector3.forward), 0.01f, key + ": a light with SunRotation shines from the sun");

            // 3. Window wall on the sun side.
            Assert.AreEqual(side, d.WindowSide, key);
            Assert.AreEqual(side, (int)Mathf.Sign(d.SunDirection.x), key + ": the window wall is on the side the sun is");
            Assert.LessOrEqual(d.Delta, 30.01f, key + ": presets keep the sun within 30 degrees of the wall's outward direction");
            Assert.AreEqual(new Vector3(-side, 0f, 0f), d.WindowNormal, key + ": the inward normal");

            // 4. Wall distance.
            Assert.AreEqual(distance, d.WallDistance, 0.01f, key + ": L = 82.5 cos(delta) / tan(elevation)");
            Assert.IsFalse(d.WallClamped, key);
            Assert.AreEqual(82.5f, d.WindowCenterHeight, 1e-3f, key + ": sill 30 + half of 105");

            // 5. Window rectangle.
            Assert.Less((new Vector3(windowX, ground + 82.5f, windowZ) - d.WindowCenter).magnitude, 0.02f, key + ": window centre " + d.WindowCenter);
            Assert.AreEqual(new Vector2(35f, 52.5f), d.WindowHalfSize);
            Assert.Less((d.WindowU - Vector3.forward / 35f).magnitude, 1e-6f, key + ": _WinU = tangent / 35");
            Assert.Less((d.WindowV - Vector3.up / 52.5f).magnitude, 1e-6f, key + ": _WinV = up / 52.5");
            Assert.AreEqual(0.04f, EnvironmentDescriptor.WindowMullion);
            // The point of it all: the sun's ray through the middle of the window lands on the focus.
            float t = (d.WindowCenter.y - ground) / d.SunDirection.y;
            Vector3 landing = d.WindowCenter - d.SunDirection * t;
            Assert.Less((landing - d.Focus).magnitude, 0.01f, key + ": the ray through the window centre lands at " + landing);
            Assert.AreEqual(0f, d.WindowShift, key + ": the window is where the sun wants it");

            // 6. Shell: 400 x 170 x 400, the window wall L from the focus.
            Assert.AreEqual(400f, d.ShellMax.x - d.ShellMin.x, 1e-3f, key);
            Assert.AreEqual(400f, d.ShellMax.z - d.ShellMin.z, 1e-3f, key);
            Assert.AreEqual(ground + 170f, d.ShellMax.y, 1e-3f, key + ": the ceiling is 170 above the play plane");
            Assert.AreEqual(ground - drop, d.ShellMin.y, 1e-3f, key + ": the real floor");
            Assert.AreEqual(d.ShellMin.y, d.FloorY);
            float wallX = side < 0 ? d.ShellMin.x : d.ShellMax.x;
            Assert.AreEqual(d.Focus.x + side * distance, wallX, 0.01f, key + ": the window wall is L from the focus");
            Assert.AreEqual(wallX, d.WindowCenter.x, 1e-3f, key + ": the window is in that wall");
            if (d.Preset.Elevated) Assert.AreEqual(Level.max.z + 30f, d.ShellMax.z, 1e-3f, key + ": the back wall is 30 behind the level");
            else Assert.AreEqual(d.Focus.z, (d.ShellMin.z + d.ShellMax.z) * 0.5f, 1e-3f, key + ": centred on the focus along the other axis");

            // 7. Glint basis: the glint on every toy mirrors the real window.
            Assert.AreEqual(d.SunDirection, d.GlintDir);
            Assert.AreEqual(0f, Vector3.Dot(d.GlintDir, d.GlintRight), 1e-5f);
            Assert.AreEqual(0f, Vector3.Dot(d.GlintDir, d.GlintUp), 1e-5f);
            Assert.AreEqual(0f, Vector3.Dot(d.GlintRight, d.GlintUp), 1e-5f);
            Assert.AreEqual(1f, d.GlintRight.magnitude, 1e-5f);
            Assert.Greater(d.GlintUp.y, 0f);
        }

        [Test]
        public void Solve_PromisesItsColliders_ForAllSixPresets([ValueSource(nameof(Solved))] object[] row)
        {
            string key = (string)row[0];
            EnvironmentPreset preset = EnvironmentPreset.Find(key);
            EnvironmentDescriptor d = EnvironmentSolver.Solve(preset, Level, 0f);

            // Six shell faces and the window pane, once each.
            var counts = new Dictionary<EnvironmentBoxKind, int>();
            foreach (EnvironmentBox box in d.Boxes)
            {
                counts.TryGetValue(box.Kind, out int n);
                counts[box.Kind] = n + 1;
                Assert.Greater(Mathf.Min(box.Size.x, Mathf.Min(box.Size.y, box.Size.z)), 0.4f, key + ": " + box.Name + " is thick enough to stop a held toy");
            }
            foreach (EnvironmentBoxKind kind in new[]
                     {
                         EnvironmentBoxKind.Floor, EnvironmentBoxKind.Ceiling, EnvironmentBoxKind.WallNegX, EnvironmentBoxKind.WallPosX,
                         EnvironmentBoxKind.WallNegZ, EnvironmentBoxKind.WallPosZ, EnvironmentBoxKind.WindowPane,
                     })
            {
                Assert.IsTrue(counts.ContainsKey(kind) && counts[kind] == 1, key + ": exactly one " + kind);
            }

            // The faces enclose exactly the shell's inside.
            Assert.IsTrue(d.TryGetBox(EnvironmentBoxKind.Floor, out EnvironmentBox floor));
            Assert.AreEqual(d.ShellMin.y, floor.Bounds.max.y, 1e-3f, key + ": the floor's top is the shell's bottom");
            Assert.IsTrue(d.TryGetBox(EnvironmentBoxKind.Ceiling, out EnvironmentBox ceiling));
            Assert.AreEqual(d.ShellMax.y, ceiling.Bounds.min.y, 1e-3f, key);
            Assert.IsTrue(d.TryGetBox(EnvironmentBoxKind.WallNegX, out EnvironmentBox negX));
            Assert.AreEqual(d.ShellMin.x, negX.Bounds.max.x, 1e-3f, key);
            Assert.IsTrue(d.TryGetBox(EnvironmentBoxKind.WallPosX, out EnvironmentBox posX));
            Assert.AreEqual(d.ShellMax.x, posX.Bounds.min.x, 1e-3f, key);
            Assert.IsTrue(d.TryGetBox(EnvironmentBoxKind.WallNegZ, out EnvironmentBox negZ));
            Assert.AreEqual(d.ShellMin.z, negZ.Bounds.max.z, 1e-3f, key);
            Assert.IsTrue(d.TryGetBox(EnvironmentBoxKind.WallPosZ, out EnvironmentBox posZ));
            Assert.AreEqual(d.ShellMax.z, posZ.Bounds.min.z, 1e-3f, key);

            // The pane fills the opening and its inner face is the wall's inner plane.
            Assert.IsTrue(d.TryGetBox(EnvironmentBoxKind.WindowPane, out EnvironmentBox pane));
            Assert.AreEqual(70f, pane.Size.z, 1e-3f, key + ": 70 wide");
            Assert.AreEqual(105f, pane.Size.y, 1e-3f, key + ": 105 high");
            Assert.AreEqual(30f, pane.Bounds.min.y, 1e-3f, key + ": sill at 30");
            float inner = d.WindowSide < 0 ? pane.Bounds.max.x : pane.Bounds.min.x;
            Assert.AreEqual(d.WindowCenter.x, inner, EnvironmentSolver.PaneInset + 1e-3f, key + ": the pane is in the wall's inner plane (a hair proud of it)");

            // The island: its top is the play plane and the level stands on it.
            bool island = preset.Island != IslandKind.None;
            Assert.AreEqual(island ? 1 : 0, counts.ContainsKey(EnvironmentBoxKind.Island) ? counts[EnvironmentBoxKind.Island] : 0, key);
            Assert.AreEqual(preset.Island, d.Island);
            if (island)
            {
                Assert.AreEqual(0f, d.IslandBounds.max.y, 1e-3f, key + ": the island's top is the play plane");
                Assert.LessOrEqual(d.IslandBounds.min.x, Level.min.x, key);
                Assert.GreaterOrEqual(d.IslandBounds.max.x, Level.max.x, key);
                Assert.LessOrEqual(d.IslandBounds.min.z, Level.min.z, key);
                Assert.GreaterOrEqual(d.IslandBounds.max.z, Level.max.z, key);
                Assert.GreaterOrEqual(d.IslandBounds.min.y, d.ShellMin.y - 1e-3f, key);
            }
            if (preset.Island == IslandKind.Rug)
            {
                Assert.AreEqual(0.5f, d.IslandBounds.size.y, 1e-3f, "a rug is 0.5 thick");
                Assert.AreEqual(120f, d.IslandBounds.size.z, 1e-3f, "110 deep, or 20 more than the level on every side");
                Assert.AreEqual(110f, EnvironmentSolver.Solve(preset, new Bounds(Vector3.zero, new Vector3(20f, 4f, 30f)), 0f).IslandBounds.size.z, 1e-3f);
                // 160 wide, except that the window wall (71.4 from the middle) cuts it off on that side.
                Assert.AreEqual(80f + d.WallDistance, d.IslandBounds.size.x, 1e-3f);
                Assert.AreEqual(d.ShellMin.x, d.IslandBounds.min.x, 1e-3f);
            }

            // Furniture: every piece of the backdrop found room here, has one to four boxes, stands inside
            // the shell and keeps clear of the level.
            Assert.AreEqual(preset.Backdrop.Count, d.Pieces.Count, key + ": every backdrop piece is placed around a level of ordinary size");
            int furniture = 0;
            for (int p = 0; p < d.Pieces.Count; p++)
            {
                EnvironmentPiece piece = d.Pieces[p];
                Assert.AreEqual(preset.Backdrop[p], piece.Kind, key + ": pieces come in the backdrop's order");
                Assert.GreaterOrEqual(piece.BoxCount, 1, key + ": " + piece.Kind);
                Assert.LessOrEqual(piece.BoxCount, 4, key + ": " + piece.Kind + " has at most four boxes");
                for (int i = piece.FirstBox; i < piece.FirstBox + piece.BoxCount; i++)
                {
                    EnvironmentBox box = d.Boxes[i];
                    furniture++;
                    Assert.AreEqual(EnvironmentBoxKind.Furniture, box.Kind);
                    Assert.AreEqual(p, box.Piece);
                    Bounds b = box.Bounds;
                    string what = key + ": " + box.Name + " " + b;
                    Assert.GreaterOrEqual(b.min.x, d.ShellMin.x - 0.02f, what);
                    Assert.LessOrEqual(b.max.x, d.ShellMax.x + 0.02f, what);
                    Assert.GreaterOrEqual(b.min.y, d.ShellMin.y - 0.02f, what);
                    Assert.LessOrEqual(b.max.y, d.ShellMax.y + 0.02f, what);
                    Assert.GreaterOrEqual(b.min.z, d.ShellMin.z - 0.02f, what);
                    Assert.LessOrEqual(b.max.z, d.ShellMax.z + 0.02f, what);
                    bool overX = b.min.x < Level.max.x + EnvironmentSolver.KeepOut && b.max.x > Level.min.x - EnvironmentSolver.KeepOut;
                    bool overZ = b.min.z < Level.max.z + EnvironmentSolver.KeepOut && b.max.z > Level.min.z - EnvironmentSolver.KeepOut;
                    Assert.IsFalse(overX && overZ, what + " stands in the level's keep-out");
                }
            }
            Assert.AreEqual(furniture, counts.ContainsKey(EnvironmentBoxKind.Furniture) ? counts[EnvironmentBoxKind.Furniture] : 0, key + ": every furniture box belongs to a piece");

            // No two pieces stand in each other (a shrink of 0.05 lets a table top rest on its legs).
            for (int i = 0; i < d.Boxes.Count; i++)
                for (int j = i + 1; j < d.Boxes.Count; j++)
                {
                    EnvironmentBox first = d.Boxes[i], second = d.Boxes[j];
                    if (first.Kind != EnvironmentBoxKind.Furniture || second.Kind != EnvironmentBoxKind.Furniture) continue;
                    if (first.Piece == second.Piece) continue;
                    Bounds one = first.Bounds, other = second.Bounds;
                    one.Expand(-0.1f);
                    Assert.IsFalse(one.Intersects(other), key + ": " + first.Name + " and " + second.Name + " overlap");
                }

            // The same input, the same room.
            EnvironmentDescriptor again = EnvironmentSolver.Solve(preset, Level, 0f);
            Assert.AreEqual(d.Boxes.Count, again.Boxes.Count);
            for (int i = 0; i < d.Boxes.Count; i++)
            {
                Assert.AreEqual(d.Boxes[i].Center, again.Boxes[i].Center);
                Assert.AreEqual(d.Boxes[i].Size, again.Boxes[i].Size);
                Assert.AreEqual(d.Boxes[i].Name, again.Boxes[i].Name);
            }
        }

        [Test]
        public void Solve_AWideLevelPushesTheWallOut_AndRaisesTheWindow()
        {
            // 120 wide: the wall cannot stand 71.4 from the middle.
            var wide = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(120f, 4f, 60f));
            EnvironmentDescriptor d = EnvironmentSolver.Solve(EnvironmentPreset.SunnyRug, wide, 0f);
            Assert.IsTrue(d.WallClamped);
            Assert.AreEqual(60f + EnvironmentSolver.WallMargin, d.WallDistance, 1e-3f, "extent toward the wall + 20");
            float raised = 80f * Mathf.Tan(45f * Mathf.Deg2Rad) / Mathf.Cos(30f * Mathf.Deg2Rad);
            Assert.AreEqual(raised, d.WindowCenterHeight, 1e-3f, "the window rises so that the ray still lands on the focus");
            Vector3 landing = d.WindowCenter - d.SunDirection * (d.WindowCenter.y / d.SunDirection.y);
            Assert.Less((landing - d.Focus).magnitude, 0.01f);

            // 240 wide: the window stops rising at 120 and the patch slides toward the wall.
            var huge = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(240f, 4f, 60f));
            d = EnvironmentSolver.Solve(EnvironmentPreset.SunnyRug, huge, 0f);
            Assert.AreEqual(140f, d.WallDistance, 1e-3f);
            Assert.AreEqual(EnvironmentDescriptor.MaxWindowCenter, d.WindowCenterHeight, 1e-3f, "capped at 120");
            Assert.AreEqual(170f, d.ShellMax.y, "the ceiling stays where it is");
            Assert.Less(d.Pieces.Count, EnvironmentPreset.SunnyRug.Backdrop.Count + 1);
            foreach (EnvironmentBox box in d.Boxes)
            {
                if (box.Kind != EnvironmentBoxKind.Furniture) continue;
                Bounds b = box.Bounds;
                bool overX = b.min.x < huge.max.x + EnvironmentSolver.KeepOut && b.max.x > huge.min.x - EnvironmentSolver.KeepOut;
                bool overZ = b.min.z < huge.max.z + EnvironmentSolver.KeepOut && b.max.z > huge.min.z - EnvironmentSolver.KeepOut;
                Assert.IsFalse(overX && overZ, box.Name + " stands in the level's keep-out");
            }
        }

        [Test]
        public void Solve_AShortLevelOnAShelfSlidesTheWindowAlongItsWall()
        {
            // 20 long: the back wall stands 40 behind the middle, but the sun wants the window 31 further on.
            var small = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(20f, 2f, 20f));
            EnvironmentDescriptor d = EnvironmentSolver.Solve(EnvironmentPreset.HighShelf, small, 0f);
            Assert.AreEqual(40f, d.ShellMax.z, 1e-3f, "the back wall is 30 behind the level");
            Assert.AreEqual(40f - 35f - EnvironmentSolver.WindowFrame, d.WindowCenter.z, 1e-3f, "the opening stays in its wall, a frame's width from the corner");
            Assert.Less(d.WindowShift, -20f, "which is a long way from where the sun wanted it");
            float t = d.WindowCenter.y / d.SunDirection.y;
            Vector3 landing = d.WindowCenter - d.SunDirection * t;
            Assert.AreEqual(Mathf.Abs(d.WindowShift), (landing - d.Focus).magnitude, 0.01f, "the patch slides by as much");
            Assert.AreEqual(landing.x, d.Focus.x, 0.01f, "along the wall only");
        }

        [Test]
        public void Solve_GroundYIsThePlayPlane()
        {
            EnvironmentDescriptor d = EnvironmentSolver.Solve(EnvironmentPreset.PegboardWorkbench, Level, -9f);
            Assert.AreEqual(-9f, d.GroundY);
            Assert.AreEqual(-9f, d.Focus.y);
            Assert.AreEqual(-9f, d.IslandBounds.max.y, 1e-3f, "the bench top is the play plane");
            Assert.AreEqual(-69f, d.FloorY, 1e-3f, "and the real floor 60 below it");
            Assert.AreEqual(161f, d.ShellMax.y, 1e-3f);
            Assert.AreEqual(-9f + 82.5f, d.WindowCenter.y, 1e-3f);
            Assert.IsTrue(d.TryGetBox(EnvironmentBoxKind.WindowPane, out EnvironmentBox pane));
            Assert.AreEqual(21f, pane.Bounds.min.y, 1e-3f, "the sill is 30 above the play plane");
            foreach (EnvironmentPiece piece in d.Pieces)
                Assert.GreaterOrEqual(piece.Position.y, -9f - 1e-3f, piece.Kind + " stands on the bench or hangs above it");

            EnvironmentDescriptor rug = EnvironmentSolver.Solve(EnvironmentPreset.SunnyRug, Level, -15f);
            Assert.AreEqual(-15f, rug.IslandBounds.max.y, 1e-3f, "the rug's top");
            Assert.AreEqual(-15.5f, rug.FloorY, 1e-3f, "the boards under it");
        }

        [Test]
        public void Sun_RepeatVisitsLowerItByTwoDegreesAndSwingItBySix()
        {
            (EnvironmentPreset preset, int visit, float elevation, float azimuth)[] expected =
            {
                (EnvironmentPreset.SunnyRug, 0, 45f, 300f), (EnvironmentPreset.SunnyRug, 1, 43f, 294f), (EnvironmentPreset.SunnyRug, 2, 41f, 288f),
                (EnvironmentPreset.BlockHall, 1, 38f, 66f), (EnvironmentPreset.BlockHall, 2, 38f, 72f),
                (EnvironmentPreset.PegboardWorkbench, 1, 46f, 291f), (EnvironmentPreset.PegboardWorkbench, 2, 44f, 297f),
                (EnvironmentPreset.CardboardBox, 1, 48f, 69f), (EnvironmentPreset.CardboardBox, 2, 46f, 63f),
                // The third visit cannot go on to 302 (32 degrees off the wall), so it swings back.
                (EnvironmentPreset.HighShelf, 1, 40f, 296f), (EnvironmentPreset.HighShelf, 2, 38f, 290f),
                (EnvironmentPreset.NightLight, 1, 43f, 294f),
            };
            foreach ((EnvironmentPreset preset, int visit, float elevation, float azimuth) in expected)
            {
                EnvironmentSolver.Sun(preset, visit, out float e, out float a);
                Assert.AreEqual(elevation, e, 1e-3f, preset.Key + " visit " + visit + ": elevation");
                Assert.AreEqual(azimuth, a, 1e-3f, preset.Key + " visit " + visit + ": azimuth");
            }

            // Whatever the visit: never below 38 degrees, within 30 of the wall, 30 to 75 off the travel axis.
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
                for (int visit = 0; visit < 8; visit++)
                {
                    EnvironmentDescriptor d = EnvironmentSolver.Solve(preset, Level, 0f, visit);
                    Assert.GreaterOrEqual(d.SunElevation, 38f, preset.Key);
                    Assert.LessOrEqual(d.SunElevation, 52f, preset.Key);
                    Assert.LessOrEqual(d.Delta, 30.01f, preset.Key + " visit " + visit);
                    float offAxis = Mathf.Abs(Mathf.DeltaAngle(d.SunAzimuth, 0f));
                    Assert.GreaterOrEqual(offAxis, 29.99f, preset.Key + " visit " + visit);
                    Assert.LessOrEqual(offAxis, 75.01f, preset.Key + " visit " + visit);
                    Assert.AreEqual(visit, d.Visit);
                }
        }

        // ------------------------------------------------------------------------------------------
        // Which level gets which room
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Levels_DefaultToTheArtBiblesTable()
        {
            string[] table =
            {
                null,
                "sunny-rug", "pegboard-workbench", "cardboard-box", "block-hall", "sunny-rug",
                "block-hall", "high-shelf", "pegboard-workbench", "high-shelf", "cardboard-box",
                "pegboard-workbench", "high-shelf", "cardboard-box", "night-light", "night-light",
            };
            for (int id = 1; id <= 15; id++) Assert.AreEqual(table[id], EnvironmentPreset.KeyForLevel(id, true), "level " + id);
            Assert.AreEqual("sunny-rug", EnvironmentPreset.KeyForLevel(0, true), "any other registered level");
            Assert.AreEqual("sunny-rug", EnvironmentPreset.KeyForLevel(99, true));
            Assert.AreEqual("none", EnvironmentPreset.KeyForLevel(-1, false), "an ad-hoc level has no room");

            // Repeat visits follow from the table: no two levels share a light.
            int[] visits = { 0, 0, 0, 0, 0, 1, 1, 0, 1, 1, 1, 2, 2, 2, 0, 1 };
            var lights = new HashSet<string>();
            for (int id = 1; id <= 15; id++)
            {
                Assert.AreEqual(visits[id], EnvironmentPreset.VisitForLevel(id, table[id]), "level " + id);
                EnvironmentSolver.Sun(EnvironmentPreset.Find(table[id]), visits[id], out float e, out float a);
                Assert.IsTrue(lights.Add(table[id] + " " + e + " " + a), "level " + id + " has a light of its own");
            }
            Assert.AreEqual(0, EnvironmentPreset.VisitForLevel(0, "sunny-rug"));
            Assert.AreEqual(0, EnvironmentPreset.VisitForLevel(99, "sunny-rug"));
        }

        [Level(905, "test-room", "Test Room")]
        sealed class RegisteredLevel : LevelDefinition
        {
            public override void Build(LevelContext ctx)
            {
                TestHelpers.Floor(ctx, 20f);
                ctx.SetSpawn(Vector3.zero, 0f);
            }
        }

        sealed class RoomLevel : LevelDefinition
        {
            public string Key = "block-hall";
            public float Ground;
            public Prop Block;
            public Dip SeenDip;
            public Material Slab;

            public override string Environment => Key;
            public override float GroundY => Ground;

            public override void Build(LevelContext ctx)
            {
                // A floor 20 x 20 whose top is the play plane.
                GameObject floor = ctx.AddStatic(BasicToys.Slab(new Vector3(20f, 1f, 20f)), new Vector3(0f, Ground - 0.5f, 0f));
                Slab = floor.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                SeenDip = ctx.Dip;
                // A small block floating in front of the player's eyes.
                Block = ctx.AddProp(BasicToys.Block(0.2f), new Vector3(4f, Ground + 1.55f, 0f), new PropOptions { Name = "Block", Body = PropBody.Fixed });
                ctx.SetSpawn(new Vector3(0f, Ground, 0f), 90f);
            }
        }

        [Test]
        public void Levels_ARegisteredLevelStandsInARoom_AnAdHocLevelDoesNot()
        {
            Assert.AreEqual("sunny-rug", new RegisteredLevel().Environment);
            Assert.AreEqual(0f, new RegisteredLevel().GroundY);
            Assert.AreEqual(0, new RegisteredLevel().EnvironmentVisit);
            Assert.AreEqual("none", new AdHocLevel(ctx => { }).Environment);

            Game = Game.Create();
            Game.LoadLevel(new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            Assert.IsNotNull(Game.Environment, "there is always a descriptor while a level is loaded");
            Assert.IsFalse(Game.Environment.HasRoom);
            Assert.AreEqual(0, Game.Environment.Boxes.Count);
            Assert.IsNull(Game.Root.transform.Find("Environment"), "no room, no colliders");
            Assert.AreEqual(1f, Game.Environment.SunDirection.magnitude, 1e-5f, "but there is still a sun to light it with");

            Game.LoadLevel(new RegisteredLevel());
            Assert.AreSame(EnvironmentPreset.SunnyRug, Game.Environment.Preset);
            Assert.IsTrue(Game.Environment.HasRoom);
            Assert.IsNotNull(Game.Root.transform.Find("Environment"));
        }

        [Test]
        public void Levels_AnUnknownEnvironmentIsReported_AndGetsNoRoom()
        {
            Game = Game.Create();
            LogAssert.Expect(LogType.Warning, new Regex("unknown environment 'lamp-desk'"));
            Game.LoadLevel(new RoomLevel { Key = "lamp-desk" });
            Assert.AreSame(EnvironmentPreset.None, Game.Environment.Preset);
        }

        // ------------------------------------------------------------------------------------------
        // The colliders are in the simulation
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Game_CreatesTheRoomsCollidersAfterTheLevelsBuild()
        {
            Game = Game.Create();
            EnvironmentDescriptor atLoad = null;
            Game.Events.LevelLoaded += e => atLoad = Game.Environment;
            Game.LoadLevel(0);

            EnvironmentDescriptor d = Game.Environment;
            Assert.IsNotNull(d);
            Assert.AreSame(d, atLoad, "the descriptor is ready when LevelLoaded arrives");
            Assert.AreSame(EnvironmentPreset.SunnyRug, d.Preset, "the sandbox stands on the rug");
            Assert.AreEqual(-15f, d.GroundY, "and declares where the room's floor is, to keep its gap");
            Assert.Greater(Game.KillY, d.GroundY, "the kill plane lies above that floor: nothing is seen to hit the ground");

            // The solve ran on what the level built: x in [-11, 11], y in [-1, 8], z in [-13, 31].
            Assert.Less((d.LevelBounds.min - new Vector3(-11f, -1f, -13f)).magnitude, 1e-3f, "bounds " + d.LevelBounds);
            Assert.Less((d.LevelBounds.max - new Vector3(11f, 8f, 31f)).magnitude, 1e-3f, "bounds " + d.LevelBounds);
            Assert.AreEqual(new Vector3(0f, -15f, 9f), d.Focus);

            // One BoxCollider per box, static, on the Default layer, exactly where the descriptor says.
            Transform root = Game.Root.transform.Find("Environment");
            Assert.IsNotNull(root);
            Assert.AreNotSame(Game.LevelRoot, root.parent, "the room is not part of what the level built");
            BoxCollider[] colliders = root.GetComponentsInChildren<BoxCollider>();
            Assert.AreEqual(d.Boxes.Count, colliders.Length);
            Assert.GreaterOrEqual(colliders.Length, 8, "six faces, the pane, the rug, and furniture");
            for (int i = 0; i < colliders.Length; i++)
            {
                EnvironmentBox box = d.Boxes[i];
                BoxCollider collider = colliders[i];
                Assert.AreEqual(box.Name, collider.name);
                Assert.AreEqual(Layers.Default, collider.gameObject.layer, box.Name);
                Assert.IsNull(collider.attachedRigidbody, box.Name);
                Assert.IsFalse(collider.isTrigger, box.Name);
                Assert.Less((collider.transform.position - box.Center).magnitude, 1e-4f, box.Name);
                Assert.Less((collider.size - box.Size).magnitude, 1e-4f, box.Name);
                Assert.Less(Quaternion.Angle(collider.transform.rotation, box.Rotation), 0.01f, box.Name);
                Assert.AreEqual(0, collider.GetComponentsInChildren<Renderer>().Length, "colliders only: the visuals are the render side's");
            }

            // They are in the simulation's physics world.
            // (At z = -30 nothing hangs on that wall; the curtains are beside the window.)
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 20f, -30f), Vector3.left, out RaycastHit wall, 500f, Layers.DefaultMask, QueryTriggerInteraction.Ignore));
            Assert.AreEqual("Shell Wall -X", wall.collider.name);
            Assert.AreEqual(d.WallDistance, wall.distance, 0.01f);
            Assert.IsTrue(Game.PhysicsScene.Raycast(d.Focus + Vector3.up * 100f, Vector3.up, out RaycastHit ceiling, 500f, Layers.DefaultMask, QueryTriggerInteraction.Ignore));
            Assert.AreEqual("Shell Ceiling", ceiling.collider.name);
            Assert.AreEqual(d.GroundY + 170f, ceiling.point.y, 0.01f);
            Vector3 towardWindow = (d.WindowCenter - new Vector3(0f, 20f, 9f)).normalized;
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 20f, 9f), towardWindow, out RaycastHit pane, 500f, Layers.DefaultMask, QueryTriggerInteraction.Ignore));
            Assert.AreEqual("Window Pane", pane.collider.name, "a toy thrown at the window lands on the glass");
            Assert.Less((pane.point - d.WindowCenter).magnitude, 0.1f);

            // The sandbox's gap is still a gap: under it there is nothing down to the rug.
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 5f, 13f), Vector3.down, out RaycastHit below, 500f, Layers.DefaultMask, QueryTriggerInteraction.Ignore));
            Assert.AreEqual("Rug", below.collider.name);
            Assert.AreEqual(-15f, below.point.y, 1e-3f);

            // A restart builds the same room again; another level replaces it; unloading removes it.
            Vector3 window = d.WindowCenter;
            Game.RestartLevel();
            Assert.AreNotSame(d, Game.Environment);
            Assert.AreEqual(window, Game.Environment.WindowCenter);
            Assert.AreEqual(colliders.Length, Game.Root.transform.Find("Environment").GetComponentsInChildren<BoxCollider>().Length);
            Assert.IsTrue(colliders[0] == null, "the old colliders are gone");

            Game.LoadLevel(new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            Assert.IsNull(Game.Root.transform.Find("Environment"));
            Assert.IsFalse(Game.PhysicsScene.Raycast(new Vector3(0f, 20f, 9f), Vector3.left, 500f, Layers.DefaultMask, QueryTriggerInteraction.Ignore));
        }

        [Test]
        public void Game_AHeldToyLandsOnTheRoomsWall_InAHeadlessTest()
        {
            // Without a room the block is carried out to the limit of the hold ...
            var open = new RoomLevel { Key = "none" };
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(open);
            LookAt(open.Block.Center);
            Click();
            Assert.AreSame(open.Block, Game.Grabber.Held);
            Game.Player.Pitch = 12f;
            Game.Tick();
            float unbounded = Game.Grabber.HoldDistance;
            Assert.Greater(unbounded, 140f);

            // ... with one it stops against the window wall, 85 units away, at the size that goes with that.
            var room = new RoomLevel { Key = "block-hall" };
            Game.LoadLevel(room);
            EnvironmentDescriptor d = Game.Environment;
            Assert.AreSame(Palette.Butter, room.SeenDip, "ctx.Dip is the dip of the level's preset");
            Assert.AreSame(Materials.Room(RoomSurface.LevelStatic, Palette.Butter), room.Slab, "and a slab built without a colour takes it");
            LookAt(room.Block.Center);
            Click();
            Assert.AreSame(room.Block, Game.Grabber.Held);
            Game.Player.Pitch = 12f;
            Game.Tick();
            Assert.IsTrue(Game.Grabber.PlacementValid);
            float wallX = d.Focus.x + d.WallDistance;
            // The block's far face: its centre plus half its (scaled) size.
            float face = room.Block.Center.x + 0.1f * room.Block.Scale;
            Assert.Less(face, wallX + 0.01f, "the block does not pass through the wall");
            Assert.Greater(face, wallX - 0.6f, "it rests against it");
            Assert.Less(Game.Grabber.HoldDistance, 95f);
            Assert.Less(Game.Grabber.HoldDistance, unbounded - 40f);
            Assert.AreEqual(Game.Grabber.Ratio * Game.Grabber.HoldDistance, room.Block.Scale, 1e-3f);

            // And the play plane moves the whole room with it.
            var sunk = new RoomLevel { Key = "pegboard-workbench", Ground = -9f };
            Game.LoadLevel(sunk);
            Assert.AreEqual(-9f, Game.Environment.GroundY);
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(60f, 20f, 0f), Vector3.down, out RaycastHit bench, 500f, Layers.DefaultMask, QueryTriggerInteraction.Ignore));
            Assert.AreEqual("Bench", bench.collider.name);
            Assert.AreEqual(-9f, bench.point.y, 1e-3f);
        }
    }
}

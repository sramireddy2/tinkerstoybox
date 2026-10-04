using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Tests
{
    /// <summary>
    /// The art API of milestone 2: the palette of ART_BIBLE section 2, the procedural meshes of MeshKit
    /// (unit normals, outward triangles, bounds, budgets, outline normals, object-space UVs), the recipes
    /// of 4.3 / 4.4 and the Materials facade (one shared material per parameter set).
    /// </summary>
    public class ArtTests
    {
        // ------------------------------------------------------------------------------------------
        // Palette
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Palette_HoldsTheDocumentsColoursByName()
        {
            Assert.AreEqual("#FFFDF7", Palette.ToHex(Palette.Paper));
            Assert.AreEqual("#2B2140", Palette.ToHex(Palette.Ink));
            Assert.AreEqual("#C99A62", Palette.ToHex(Palette.Kraft));
            Assert.AreEqual("#E9C9A0", Palette.ToHex(Palette.Birch));
            Assert.AreEqual("#C9CED8", Palette.ToHex(Palette.Steel));

            string[] candy = { "#FF2E55", "#FF7A1A", "#FFCE1F", "#7FDB2E", "#18A8FF", "#8A4BFF", "#FF5FB0" };
            Assert.AreEqual(7, Palette.Candy.Length);
            for (int i = 0; i < candy.Length; i++) Assert.AreEqual(candy[i], Palette.ToHex(Palette.Candy[i]), Palette.CandyNames[i]);
            Assert.AreEqual("Lagoon", Palette.CandyName(Palette.Lagoon));
            Assert.IsFalse(Palette.IsCandy(Palette.Ink));

            Assert.AreEqual("#FFB627", Palette.ToHex(Palette.Amber.Color));
            Assert.AreEqual(2.2f, Palette.Amber.Gain);
            Assert.AreEqual(1.2f, Palette.Amber.PulseMin);
            Assert.AreEqual(1f, Palette.Amber.PulseHz);
            Assert.AreEqual(2.6f, Palette.Go.Gain);
            Assert.AreEqual(3f, Palette.Hazard.Gain);
            Assert.IsTrue(Palette.Hazard.Striped);
            Assert.AreEqual("#FFFFFF", Palette.ToHex(Palette.Exit.Color));
            Assert.AreEqual(1.2f, Palette.Amber.GainAt(0f), 1e-4f, "a pulse starts at its low");
            Assert.AreEqual(2.2f, Palette.Amber.GainAt(0.5f), 1e-4f, "and peaks half a period later");
            Assert.AreEqual(2.6f, Palette.Go.GainAt(0.37f), "a steady signal does not pulse");
        }

        [Test]
        public void Palette_EveryPresetHasItsDip()
        {
            var expected = new Dictionary<string, string[]>
            {
                { "sunny-rug", new[] { "Mint", "#DDF5EA", "#B4E6D2", "#7CCDB3", "#EAF8F1" } },
                { "block-hall", new[] { "Butter", "#FFF4CC", "#FFE699", "#F2CC5C", "#FFF9E3" } },
                { "pegboard-workbench", new[] { "Pool", "#DCEEFB", "#B5D9F5", "#7FB8E8", "#EAF5FD" } },
                { "cardboard-box", new[] { "Peach", "#FFE6D6", "#FFCDB2", "#F2A88A", "#FFF1E8" } },
                { "high-shelf", new[] { "Lilac", "#E9E2FA", "#CFC2F2", "#A996E0", "#F1ECFC" } },
                { "night-light", new[] { "Plum", "#5A4A82", "#453769", "#2F2550", "#3A2E5C" } },
            };
            foreach (KeyValuePair<string, string[]> entry in expected)
            {
                Dip dip = Palette.DipOf(entry.Key);
                Assert.AreEqual(entry.Value[0], dip.Name, entry.Key);
                Assert.AreEqual(entry.Value[1], Palette.ToHex(dip.Light), entry.Key + " light");
                Assert.AreEqual(entry.Value[2], Palette.ToHex(dip.Mid), entry.Key + " mid");
                Assert.AreEqual(entry.Value[3], Palette.ToHex(dip.Deep), entry.Key + " deep");
                Assert.AreEqual(entry.Value[4], Palette.ToHex(dip.Haze), entry.Key + " haze");
                Assert.IsTrue(Palette.IsCandy(dip.Hero), entry.Key + ": the hero is a candy colour");
                Assert.IsTrue(dip.Allows(dip.Hero), entry.Key + ": and it is not the banned one");
            }
            Assert.IsFalse(Palette.Mint.Allows(Palette.Lime), "Lime is too close to the Mint room");
            Assert.IsTrue(Palette.Plum.Allows(Palette.Lime), "the night dip bans nothing");
            Assert.IsTrue(Palette.Plum.Night);
            Assert.AreSame(Palette.Mint, Palette.DipOf("no such preset"));
        }

        [Test]
        public void Palette_LinConvertsSrgbToLinearExactly()
        {
            Color white = Palette.Lin("#FFFFFF");
            Assert.AreEqual(1f, white.r, 1e-6f);
            Color grey = Palette.Lin("#808080");
            Assert.AreEqual(0.21586f, grey.r, 1e-4f, "sRGB 128 is 21.6% linear");
            Color dark = Palette.Lin("#0A0A0A");
            Assert.AreEqual(10f / 255f / 12.92f, dark.r, 1e-6f, "the linear toe");
            Assert.AreEqual(0.5f, Palette.Lin(new Color(0.2f, 0.3f, 0.4f, 0.5f)).a, "alpha is not a colour");

            foreach (Color candy in Palette.Candy)
                Assert.IsTrue(Palette.Same(candy, Palette.Srgb(Palette.Lin(candy))), "linear and back is the same 8-bit colour");
            Assert.AreEqual(Palette.Hex("#FFCE1F"), Palette.Hex("FFCE1F"), "the '#' is optional");
            Assert.AreEqual(0.5f, Palette.Hex("#FFCE1F80").a, 0.01f);
            Assert.Throws<System.FormatException>(() => Palette.Hex("#FFF"));
            Assert.Throws<System.FormatException>(() => Palette.Hex("#GG0000"));
        }

        // ------------------------------------------------------------------------------------------
        // MeshKit
        // ------------------------------------------------------------------------------------------

        static readonly List<Mesh> Made = new List<Mesh>();

        [TearDown]
        public void DestroyMeshes()
        {
            foreach (Mesh mesh in Made) Object.DestroyImmediate(mesh);
            Made.Clear();
            Materials.Plain = false;
            Materials.Tier = QualityTier.Medium;
        }

        static Mesh Keep(Mesh mesh)
        {
            Made.Add(mesh);
            return mesh;
        }

        static readonly Vector2[] LShape =
        {
            new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(1f, 1f), new Vector2(1f, 3f), new Vector2(0f, 3f),
        };

        static readonly Vector2[] VaseProfile =
        {
            new Vector2(0f, 0f), new Vector2(0.6f, 0f), new Vector2(0.8f, 0.5f), new Vector2(0.5f, 1.2f), new Vector2(0.7f, 1.6f), new Vector2(0f, 1.6f),
        };

        // name, mesh, expected size of the bounds, triangle budget
        static IEnumerable<(string name, Mesh mesh, Vector3 size, int budget)> Shapes()
        {
            yield return ("Box", Keep(MeshKit.Box(new Vector3(2f, 1f, 3f))), new Vector3(2f, 1f, 3f), 12);
            yield return ("RoundedBox", Keep(MeshKit.RoundedBox(new Vector3(1f, 0.5f, 2f), 0.05f, 2)), new Vector3(1f, 0.5f, 2f), 300);
            yield return ("RoundedBox chamfer", Keep(MeshKit.RoundedBox(new Vector3(1f, 1f, 1f), 0.1f, 1)), Vector3.one, 108);
            yield return ("RoundedBox all bevel", Keep(MeshKit.RoundedBox(new Vector3(1f, 1f, 1f), 0.5f, 3)), Vector3.one, 600);
            yield return ("Cylinder", Keep(MeshKit.Cylinder(0.5f, 1.2f)), new Vector3(1f, 1.2f, 1f), 96);
            yield return ("Cylinder bevelled", Keep(MeshKit.Cylinder(0.5f, 1.2f, 24, 0.05f)), new Vector3(1f, 1.2f, 1f), 300);
            yield return ("Sphere", Keep(MeshKit.Sphere(0.4f)), new Vector3(0.8f, 0.8f, 0.8f), 768);
            yield return ("Lathe", Keep(MeshKit.Lathe(VaseProfile, 20)), new Vector3(1.6f, 1.6f, 1.6f), 200);
            yield return ("Extrude", Keep(MeshKit.Extrude(LShape, 0.5f)), new Vector3(2f, 3f, 0.5f), 20);
            yield return ("Extrude bevelled", Keep(MeshKit.Extrude(LShape, 0.5f, 0.1f)), new Vector3(2f, 3f, 0.5f), 44);
            yield return ("Wedge", Keep(MeshKit.Wedge(1.5f, 1f, 2f)), new Vector3(1.5f, 1f, 2f), 8);
            yield return ("Grid", Keep(MeshKit.Grid(new Vector2(4f, 6f), 4, 3)), new Vector3(4f, 0f, 6f), 24);
        }

        [Test]
        public void MeshKit_NormalsAreUnit_TrianglesFaceOutward_BoundsAndBudgetsHold()
        {
            foreach ((string name, Mesh mesh, Vector3 size, int budget) in Shapes())
            {
                Vector3[] positions = mesh.vertices, normals = mesh.normals;
                int[] triangles = mesh.triangles;
                Assert.Greater(positions.Length, 0, name);
                Assert.AreEqual(positions.Length, normals.Length, name + ": a normal per vertex");
                Assert.AreEqual(positions.Length, mesh.uv.Length, name + ": a UV per vertex");
                foreach (Vector3 normal in normals)
                    Assert.AreEqual(1f, normal.magnitude, 1e-3f, name + ": unit normals");

                int count = MeshUtil.TriangleCount(mesh);
                Assert.AreEqual(triangles.Length / 3, count, name);
                Assert.Greater(count, 0, name);
                Assert.LessOrEqual(count, budget, name + ": triangle budget");
                Assert.LessOrEqual(count, 3000, name + ": a toy is at most 3,000 triangles");

                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = positions[triangles[t]], b = positions[triangles[t + 1]], c = positions[triangles[t + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    Assert.Greater(face.magnitude, 1e-7f, name + ": no degenerate triangles");
                    Vector3 shading = normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]];
                    Assert.Greater(Vector3.Dot(face.normalized, shading.normalized), 0.2f, name + ": triangle " + t / 3 + " faces along its normals");
                }

                Bounds bounds = mesh.bounds;
                Assert.Less((bounds.size - size).magnitude, 1e-3f, name + ": bounds " + bounds.size + ", expected " + size);
                if (name != "Extrude" && name != "Extrude bevelled" && name != "Lathe")
                    Assert.Less(bounds.center.magnitude, 1e-3f, name + ": centred on the origin");
            }
        }

        [Test]
        public void MeshKit_ClosedShapesEncloseTheirVolume()
        {
            // Signed tetrahedron volumes: positive and right only if the surface is closed and faces outward.
            (Mesh mesh, float volume, float tolerance)[] solids =
            {
                (Keep(MeshKit.Box(new Vector3(2f, 1f, 3f))), 6f, 1e-4f),
                (Keep(MeshKit.RoundedBox(new Vector3(2f, 1f, 3f), 0.05f)), 6f, 0.05f),
                (Keep(MeshKit.Cylinder(0.5f, 2f, 48)), Mathf.PI * 0.25f * 2f, 0.01f),
                (Keep(MeshKit.Sphere(1f, 48, 32)), 4f / 3f * Mathf.PI, 0.06f),
                (Keep(MeshKit.Extrude(LShape, 0.5f)), 4f * 0.5f, 1e-4f),
                (Keep(MeshKit.Wedge(1.5f, 1f, 2f)), 1.5f, 1e-4f),
            };
            foreach ((Mesh mesh, float volume, float tolerance) in solids)
            {
                Vector3[] p = mesh.vertices;
                int[] t = mesh.triangles;
                double sum = 0.0;
                for (int i = 0; i < t.Length; i += 3) sum += Vector3.Dot(p[t[i]], Vector3.Cross(p[t[i + 1]], p[t[i + 2]])) / 6.0;
                Assert.AreEqual(volume, (float)sum, tolerance, mesh.name + ": enclosed volume");
            }
        }

        [Test]
        public void MeshKit_OutlineNormalsAreTheAverageOfTheNormalsAtAPosition()
        {
            foreach ((string name, Mesh mesh, Vector3 _, int _) in Shapes())
            {
                var outline = new List<Vector3>();
                mesh.GetUVs(3, outline);
                Vector3[] positions = mesh.vertices, normals = mesh.normals;
                Assert.AreEqual(positions.Length, outline.Count, name + ": TEXCOORD3 holds an outline normal per vertex");

                // Group by position and compare with the average of the distinct normals there.
                var groups = new Dictionary<Vector3Int, List<int>>();
                for (int i = 0; i < positions.Length; i++)
                {
                    var key = Vector3Int.RoundToInt(positions[i] * 10000f);
                    if (!groups.TryGetValue(key, out List<int> group)) groups[key] = group = new List<int>();
                    group.Add(i);
                }
                foreach (List<int> group in groups.Values)
                {
                    var distinct = new List<Vector3>();
                    foreach (int index in group)
                        if (!distinct.Exists(n => Vector3.Dot(n, normals[index]) > 0.9999f)) distinct.Add(normals[index]);
                    Vector3 sum = Vector3.zero;
                    foreach (Vector3 normal in distinct) sum += normal;
                    if (sum.magnitude < 1e-4f) continue;
                    foreach (int index in group)
                    {
                        Assert.AreEqual(1f, outline[index].magnitude, 1e-3f, name + ": unit outline normals");
                        Assert.Less(Vector3.Angle(sum, outline[index]), 0.5f, name + ": vertices at one position share the averaged normal");
                    }
                }
            }

            // The case it exists for: a hard-edged box. Extruded along its face normals it would tear open at the
            // edges; every corner's three vertices share the diagonal instead.
            Mesh box = Keep(MeshKit.Box(Vector3.one));
            var corners = new List<Vector3>();
            box.GetUVs(3, corners);
            Vector3[] at = box.vertices;
            for (int i = 0; i < at.Length; i++)
                Assert.Less(Vector3.Angle(at[i], corners[i]), 0.1f, "a cube's outline normals point from the centre through its corners");
        }

        [Test]
        public void MeshUtil_BakesOutlineNormalsAndObjectSpaceUVsIntoAForeignMesh()
        {
            // Two triangles folded along an edge, with hard normals: four vertices on the shared edge.
            var mesh = Keep(new Mesh());
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 2f), new Vector3(3f, 0f, 0f),
                new Vector3(0f, 0f, 0f), new Vector3(0f, 4f, 0f), new Vector3(0f, 0f, 2f),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.right, Vector3.right, Vector3.right };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            mesh.RecalculateBounds();

            MeshUtil.BakeOutlineNormals(mesh);
            var outline = new List<Vector3>();
            mesh.GetUVs(3, outline);
            Vector3 diagonal = new Vector3(1f, 1f, 0f).normalized;
            Assert.Less(Vector3.Angle(diagonal, outline[0]), 0.01f);
            Assert.Less(Vector3.Angle(diagonal, outline[3]), 0.01f);
            Assert.Less(Vector3.Angle(diagonal, outline[1]), 0.01f);
            Assert.Less(Vector3.Angle(Vector3.up, outline[2]), 0.01f, "a vertex nobody shares keeps its own normal");
            Assert.Less(Vector3.Angle(Vector3.right, outline[4]), 0.01f);

            MeshUtil.ObjectSpaceUVs(mesh);
            Vector2[] uv = mesh.uv;
            Assert.AreEqual(new Vector2(3f, 0f), uv[2], "an up-facing vertex is mapped by (x, z): one repeat per unit");
            Assert.AreEqual(new Vector2(0f, 4f), uv[4], "a side-facing one by (z, y)");

            MeshUtil.SetColor(mesh, Palette.Birch);
            Assert.AreEqual(6, mesh.colors.Length);
            Assert.AreEqual(2, MeshUtil.TriangleCount(mesh));
        }

        [Test]
        public void MeshKit_UVsAreObjectSpace_OneRepeatPerUnit()
        {
            Mesh box = Keep(MeshKit.Box(new Vector3(4f, 1f, 2f)));
            Vector3[] positions = box.vertices, normals = box.normals;
            Vector2[] uv = box.uv;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            for (int i = 0; i < positions.Length; i++)
            {
                if (normals[i].y < 0.9f) continue;
                minU = Mathf.Min(minU, uv[i].x);
                maxU = Mathf.Max(maxU, uv[i].x);
                minV = Mathf.Min(minV, uv[i].y);
                maxV = Mathf.Max(maxV, uv[i].y);
            }
            float spanU = maxU - minU, spanV = maxV - minV;
            Assert.AreEqual(6f, spanU + spanV, 1e-4f, "the top face of a 4 x 2 box spans 4 and 2 texture repeats");
            Assert.AreEqual(8f, spanU * spanV, 1e-4f);
        }

        [Test]
        public void MeshKit_IsDeterministic_AndCachesByKey()
        {
            Mesh first = Keep(MeshKit.RoundedBox(new Vector3(1f, 2f, 3f), 0.1f, 3));
            Mesh second = Keep(MeshKit.RoundedBox(new Vector3(1f, 2f, 3f), 0.1f, 3));
            CollectionAssert.AreEqual(first.vertices, second.vertices, "the same arguments give the same mesh, bit for bit");
            CollectionAssert.AreEqual(first.normals, second.normals);
            CollectionAssert.AreEqual(first.triangles, second.triangles);

            int builds = 0;
            Assert.AreEqual("ArtTests box|1|2|0.3", MeshKit.Key("ArtTests box", 1f, 2f, 0.3f));
            // The cache lives as long as the editor session does: a key no earlier run of this test has used.
            string key = MeshKit.Key("ArtTests box " + System.Guid.NewGuid(), 1f, 2f, 0.3f);
            Mesh a = MeshKit.Cached(key, () => { builds++; return MeshKit.Box(new Vector3(1f, 2f, 0.3f)); });
            Mesh b = MeshKit.Cached(key, () => { builds++; return MeshKit.Box(new Vector3(1f, 2f, 0.3f)); });
            Assert.AreSame(a, b);
            Assert.AreEqual(1, builds, "a cached mesh is built once");
            Assert.AreNotEqual(MeshKit.Key("x", 0.1f), MeshKit.Key("x", 0.1f + 1e-7f), "keys are exact to the bit");
        }

        [Test]
        public void MeshKit_MergeMakesOneMeshOfMany()
        {
            Mesh box = Keep(MeshKit.Box(Vector3.one));
            Mesh ball = Keep(MeshKit.Sphere(0.5f, 12, 8));
            Mesh merged = Keep(MeshKit.Merge("Merged", new[]
            {
                new MeshPart(box, new Vector3(-2f, 0f, 0f)),
                new MeshPart(ball, Matrix4x4.TRS(new Vector3(2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(1f, 2f, 1f)), Palette.Birch),
                // Mirrored: the triangles must be turned round or the part would be inside out.
                new MeshPart(box, Matrix4x4.TRS(new Vector3(0f, 3f, 0f), Quaternion.identity, new Vector3(-1f, 1f, 1f))),
            }));

            Assert.AreEqual(box.vertexCount * 2 + ball.vertexCount, merged.vertexCount);
            Assert.AreEqual(MeshUtil.TriangleCount(box) * 2 + MeshUtil.TriangleCount(ball), MeshUtil.TriangleCount(merged));
            Assert.AreEqual(1, merged.subMeshCount, "one material, one draw");
            Assert.Less((merged.bounds.min - new Vector3(-2.5f, -1f, -0.5f)).magnitude, 1e-3f, "bounds " + merged.bounds);
            Assert.Less((merged.bounds.max - new Vector3(2.5f, 3.5f, 0.5f)).magnitude, 1e-3f, "bounds " + merged.bounds);

            Vector3[] positions = merged.vertices, normals = merged.normals;
            int[] triangles = merged.triangles;
            foreach (Vector3 normal in normals) Assert.AreEqual(1f, normal.magnitude, 1e-3f, "normals stay unit under non-uniform scale");
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 face = Vector3.Cross(positions[triangles[t + 1]] - positions[triangles[t]], positions[triangles[t + 2]] - positions[triangles[t]]);
                Vector3 shading = normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]];
                Assert.Greater(Vector3.Dot(face, shading), 0f, "triangle " + t / 3 + " faces outward after the merge");
            }

            Color[] colors = merged.colors;
            Assert.AreEqual(merged.vertexCount, colors.Length, "a tinted part gives the merged mesh vertex colours");
            Assert.AreEqual(Color.white, colors[0]);
            Assert.AreEqual(Palette.Birch, colors[box.vertexCount]);
            var outline = new List<Vector3>();
            merged.GetUVs(3, outline);
            Assert.AreEqual(merged.vertexCount, outline.Count, "and outline normals of its own");
        }

        [Test]
        public void MeshKit_CollisionHullsAreTheUnitShapesTheSimulationWasTunedWith()
        {
            Mesh cylinder = MeshKit.UnitCylinderHull, wedge = MeshKit.UnitWedgeHull;
            Assert.AreSame(cylinder, MeshKit.UnitCylinderHull, "shared");
            Assert.Less((cylinder.bounds.size - Vector3.one).magnitude, 1e-4f);
            Assert.Less((wedge.bounds.size - Vector3.one).magnitude, 1e-4f);
            Assert.AreEqual(18, wedge.vertexCount);
            Assert.AreEqual(144, cylinder.vertexCount);
        }

        // ------------------------------------------------------------------------------------------
        // Recipes and materials
        // ------------------------------------------------------------------------------------------

        [Test]
        public void ToyRecipes_CarryTheNumbersOfTheTable()
        {
            ToyRecipe plastic = ToyRecipe.GlossyPlastic;
            Assert.AreEqual(0.62f, plastic.Smoothness);
            Assert.AreEqual(0.20f, plastic.Wrap);
            Assert.AreEqual(0.5f, plastic.Env);
            Assert.AreEqual(1f, plastic.Coat);
            Assert.AreEqual(new Vector3(1f, 0.02f, 1f), new Vector3(plastic.Glint, plastic.GlintSoft, plastic.GlintStretch));
            Assert.AreEqual(0.55f, plastic.Rim);
            Assert.AreEqual(3f, plastic.RimPow);
            Assert.AreEqual(0.06f, plastic.SelfGlow);
            Assert.AreEqual(DetailTexture.Peel, plastic.Detail);
            Assert.AreEqual(0.03f, plastic.DetailBump);
            Assert.AreEqual(0.06f, plastic.Squash);
            Assert.IsTrue(Palette.Same(Palette.Cherry, Palette.Srgb(plastic.BaseColor(Palette.Cherry))), "plastic is the pure candy colour");

            Assert.AreEqual(1f, ToyRecipe.BrushedMetal.Metallic);
            Assert.AreEqual(0.18f, ToyRecipe.BrushedMetal.Streak);
            Assert.AreEqual(3f, ToyRecipe.BrushedMetal.GlintStretch);

            ToyRecipe glass = ToyRecipe.Glass;
            Assert.AreEqual(ToyBlend.Alpha, glass.Blend);
            Assert.AreEqual(3000, glass.RenderQueue);
            Assert.AreEqual(0.22f, glass.AlphaFace);
            Assert.AreEqual(0.85f, glass.AlphaEdge);
            Assert.IsTrue(glass.ShadowDither);
            Assert.IsTrue(glass.BackShell);
            Assert.AreEqual(1.6f, glass.PoolTint);
            Color glassBase = glass.BaseColor(Palette.Lagoon), lagoon = Palette.Lin(Palette.Lagoon);
            Assert.AreEqual(Mathf.Lerp(1f, lagoon.r, 0.35f), glassBase.r, 1e-4f, "glass is lerp(white, candy, 0.35)");
            Assert.IsTrue(Palette.Same(Palette.Lagoon, Palette.Srgb(glass.RimColor(Palette.Lagoon))), "and its rim is the candy colour");

            Assert.AreEqual(0.92f, ToyRecipe.Rubber.BaseColor(Color.white).r, 1e-4f, "rubber is candy x 0.92");
            Assert.AreEqual(0.14f, ToyRecipe.Rubber.Squash);
            Assert.AreEqual(0.18f, ToyRecipe.Sponge.Squash);
            Assert.AreEqual(CullMode.Off, ToyRecipe.Feather.Cull);
            Assert.AreEqual(0.8f, ToyRecipe.Feather.Translucency);
            Assert.AreEqual(0.5f, ToyRecipe.PaperSheet.Translucency);
            Assert.AreEqual(0.7f, ToyRecipe.TapeStrip.Smoothness);

            // 4.4: things that cannot be grabbed have no rim and no pool.
            foreach (ToyRecipe recipe in new[] { ToyRecipe.PlainProp, ToyRecipe.GadgetBody, ToyRecipe.GadgetSignal, ToyRecipe.GadgetMetal, ToyRecipe.Water })
            {
                Assert.AreEqual(0f, recipe.Rim, recipe.Name + ": rim");
                Assert.IsFalse(recipe.Pool, recipe.Name + ": pool");
            }
            Assert.LessOrEqual(ToyRecipe.PlainProp.Glint, 0.3f);
            Assert.AreEqual(0.1f, ToyRecipe.GadgetBody.Metallic);
            Assert.AreEqual(0f, ToyRecipe.GadgetSignal.Glint);
            Assert.AreEqual(0.45f, ToyRecipe.Water.AlphaFace);
            foreach (ToyRecipe recipe in ToyRecipe.Toys) Assert.IsTrue(recipe.Pool && recipe.Rim > 0f, recipe.Name + " is a toy: rim and pool");
        }

        [Test]
        public void ToyRecipes_AreValues()
        {
            ToyRecipe copy = ToyRecipe.GlossyPlastic.With(r => { });
            Assert.AreNotSame(ToyRecipe.GlossyPlastic, copy);
            Assert.AreEqual(ToyRecipe.GlossyPlastic, copy, "the same numbers are the same recipe");
            Assert.AreEqual(ToyRecipe.GlossyPlastic.GetHashCode(), copy.GetHashCode());
            ToyRecipe smoother = ToyRecipe.GlossyPlastic.With(r => r.Smoothness = 0.7f);
            Assert.AreNotEqual(ToyRecipe.GlossyPlastic, smoother);
            Assert.AreEqual(0.62f, ToyRecipe.GlossyPlastic.Smoothness, "With copies; the ready-made recipe is untouched");
        }

        [Test]
        public void Materials_OneSharedMaterialPerParameterSet()
        {
            Material cherry = Materials.Toy(ToyRecipe.GlossyPlastic, Palette.Cherry);
            Assert.IsNotNull(cherry);
            Assert.IsNotNull(cherry.shader);
            Assert.AreSame(cherry, Materials.Toy(ToyRecipe.GlossyPlastic, Palette.Cherry));
            Assert.AreSame(cherry, Materials.Toy(ToyRecipe.GlossyPlastic.With(r => { }), new Color(Palette.Cherry.r, Palette.Cherry.g, Palette.Cherry.b)),
                "an equal recipe and an equal colour are the same parameter set");
            Assert.AreNotSame(cherry, Materials.Toy(ToyRecipe.GlossyPlastic, Palette.Lagoon));
            Assert.AreNotSame(cherry, Materials.Toy(ToyRecipe.Rubber, Palette.Cherry));

            Assert.AreSame(Materials.Gadget(GadgetPart.Body), Materials.Toy(ToyRecipe.GadgetBody, Palette.Ink));
            Assert.AreSame(Materials.Gadget(GadgetPart.Metal), Materials.Toy(ToyRecipe.GadgetMetal, Palette.Steel));
            Assert.AreSame(Materials.Gadget(Palette.Go), Materials.Gadget(Palette.Go, Palette.Go.Gain));
            Assert.AreNotSame(Materials.Gadget(Palette.Go), Materials.Gadget(Palette.Hazard));
            Assert.AreNotSame(Materials.Gadget(Palette.Amber), Materials.Gadget(Palette.Amber, Palette.Amber.PulseMin));
            Assert.AreSame(Materials.Water(Palette.Pool), Materials.Toy(ToyRecipe.Water, Palette.Pool.Deep));

            Material floor = Materials.Room(RoomSurface.ShellFloor, Palette.Mint, PatternSpec.Dots);
            Assert.AreSame(floor, Materials.Room(RoomSurface.ShellFloor, Palette.Mint, PatternSpec.Dots));
            Assert.AreNotSame(floor, Materials.Room(RoomSurface.ShellFloor, Palette.Mint, PatternSpec.Planks));
            Assert.AreNotSame(floor, Materials.Room(RoomSurface.ShellFloor, Palette.Butter, PatternSpec.Dots));
            Assert.AreNotSame(Materials.Room(RoomSurface.ShellWall, Palette.Mint, PatternSpec.Stripes, 0f), Materials.Room(RoomSurface.ShellWall, Palette.Mint, PatternSpec.Stripes, -9f),
                "the dado line follows the play plane");

            Material laser = Materials.Flat(Palette.Lin(Palette.Hazard.Color) * 3f, FlatShape.SoftDisc, FlatBlend.Additive);
            Assert.AreSame(laser, Materials.Flat(Palette.Lin(Palette.Hazard.Color) * 3f, FlatShape.SoftDisc, FlatBlend.Additive));
            Assert.AreNotSame(laser, Materials.Flat(Palette.Lin(Palette.Hazard.Color) * 3f, FlatShape.Ring, FlatBlend.Additive));
        }

        [Test]
        public void Materials_FollowTheTierThePlainFlagAndTheLevelsDip()
        {
            Materials.Tier = QualityTier.Medium;
            Material[] glass = Materials.ToySet(ToyRecipe.Glass, Palette.Lagoon);
            Assert.AreEqual(2, glass.Length, "Medium and High add the back shell");
            Assert.AreSame(Materials.Toy(ToyRecipe.Glass, Palette.Lagoon), glass[1], "the shell is drawn first");
            Materials.Tier = QualityTier.Low;
            Assert.AreEqual(1, Materials.ToySet(ToyRecipe.Glass, Palette.Lagoon).Length, "Low drops it");
            Assert.AreEqual(1, Materials.ToySet(ToyRecipe.GlossyPlastic, Palette.Cherry).Length);
            Materials.Tier = QualityTier.Medium;

            Material normal = Materials.Toy(ToyRecipe.GlossyPlastic, Palette.Grape);
            Materials.Plain = true;
            Material plain = Materials.Toy(ToyRecipe.GlossyPlastic, Palette.Grape);
            Assert.AreNotSame(normal, plain, "the plain look has materials of its own");
            StringAssert.Contains("Universal Render Pipeline", plain.shader.name);
            Materials.Plain = false;
            Assert.AreSame(normal, Materials.Toy(ToyRecipe.GlossyPlastic, Palette.Grape));

            Dip before = Materials.Dip;
            try
            {
                Materials.Dip = Palette.Peach;
                Assert.AreSame(Materials.Room(RoomSurface.LevelStatic, Palette.Peach), Materials.Room(RoomSurface.LevelStatic),
                    "without a dip a room surface takes the dip of the level being built");
                Assert.AreSame(Materials.Water(Palette.Peach), Materials.Water());
            }
            finally
            {
                Materials.Dip = before;
            }
        }

        // A shader with the property block of one of the game's shaders and nothing else: enough to see what
        // Materials writes into a template that carries a "Toybox/..." shader.
        static Shader ProbeShader(string name, string properties) =>
            UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Toybox/Test/" + name + "\" { Properties { " + properties + " } SubShader { Pass { } } }", false);

        static Vector3 Rgb(Vector4 v) => new Vector3(v.x, v.y, v.z);
        static Vector3 Rgb(Color c) => new Vector3(c.r, c.g, c.b);

        [Test]
        public void Materials_WriteEveryShaderPropertyByName_OnceATemplateCarriesOneOfTheGamesShaders()
        {
            const string toyProperties =
                "_BaseColor(\"\",Vector)=(1,1,1,1) _Metallic(\"\",Float)=0 _Smoothness(\"\",Float)=0 _Wrap(\"\",Float)=0 _Env(\"\",Float)=0 _Coat(\"\",Float)=0 " +
                "_Streak(\"\",Float)=0 _Glint(\"\",Float)=0 _GlintSoft(\"\",Float)=0 _GlintStretch(\"\",Float)=0 _Rim(\"\",Float)=0 _RimPow(\"\",Float)=0 " +
                "_RimColor(\"\",Vector)=(1,1,1,1) _SelfGlow(\"\",Float)=0 _Emission(\"\",Vector)=(0,0,0,0) _Translucency(\"\",Float)=0 " +
                "_DetailMap(\"\",2D)=\"gray\"{} _DetailAlbedo(\"\",Float)=0 _DetailSmooth(\"\",Float)=0 _DetailBump(\"\",Float)=0 " +
                "_AlphaFace(\"\",Float)=1 _AlphaEdge(\"\",Float)=1 _AlphaPow(\"\",Float)=1 _ShadowDither(\"\",Float)=0 " +
                "_SrcBlend(\"\",Float)=1 _DstBlend(\"\",Float)=0 _ZWrite(\"\",Float)=1 _Cull(\"\",Float)=2";
            const string roomProperties =
                "_ColorTop(\"\",Vector)=(1,1,1,1) _ColorSide(\"\",Vector)=(1,1,1,1) _ColorDado(\"\",Vector)=(1,1,1,1) _DadoY(\"\",Float)=0 " +
                "_Pattern(\"\",Float)=0 _PatternA(\"\",Vector)=(0,0,0,0) _Corner(\"\",Float)=0 _Cull(\"\",Float)=2";
            const string flatProperties =
                "_Color(\"\",Vector)=(1,1,1,1) _Shape(\"\",Float)=0 _Soft(\"\",Float)=0 _SrcBlend(\"\",Float)=1 _DstBlend(\"\",Float)=0 " +
                "_ZWrite(\"\",Float)=1 _ZTest(\"\",Float)=4 _Cull(\"\",Float)=0";

            // Materials are cached for the session; names no earlier run of this test has used.
            string run = " " + System.Guid.NewGuid();
            Shader toyShader = ProbeShader("ToyProbe", toyProperties), roomShader = ProbeShader("RoomProbe", roomProperties), flatShader = ProbeShader("FlatProbe", flatProperties);
            var toyTemplate = new Material(toyShader);
            var roomTemplate = new Material(roomShader);
            var flatTemplate = new Material(flatShader);
            try
            {
                Materials.OverrideTemplate("ToyLit", toyTemplate);
                Materials.OverrideTemplate("RoomLit", roomTemplate);
                Materials.OverrideTemplate("Flat", flatTemplate);
                Materials.Tier = QualityTier.Medium;

                // A toy: every number of the recipe, colours as linear vectors.
                ToyRecipe wood = ToyRecipe.PaintedWood.Named("Probe Wood" + run);
                Material toy = Materials.Toy(wood, Palette.Grape);
                Assert.AreSame(toyShader, toy.shader);
                Assert.Less((Rgb(toy.GetVector("_BaseColor")) - Rgb(Palette.Lin(Palette.Grape))).magnitude, 1e-5f, "the base colour arrives linear");
                Assert.AreEqual(0.45f, toy.GetFloat("_Smoothness"));
                Assert.AreEqual(0.25f, toy.GetFloat("_Wrap"));
                Assert.AreEqual(0.3f, toy.GetFloat("_Env"));
                Assert.AreEqual(0.35f, toy.GetFloat("_Coat"));
                Assert.AreEqual(0.35f, toy.GetFloat("_Glint"));
                Assert.AreEqual(0.25f, toy.GetFloat("_GlintSoft"));
                Assert.AreEqual(1f, toy.GetFloat("_GlintStretch"));
                Assert.AreEqual(0.4f, toy.GetFloat("_Rim"));
                Assert.AreEqual(3f, toy.GetFloat("_RimPow"));
                Assert.Less((Rgb(toy.GetVector("_RimColor")) - Rgb(Palette.Lin(Palette.Paper))).magnitude, 1e-5f);
                Assert.AreEqual(0.03f, toy.GetFloat("_SelfGlow"));
                Assert.AreEqual(0.08f, toy.GetFloat("_DetailAlbedo"));
                Assert.AreEqual(0.2f, toy.GetFloat("_DetailSmooth"));
                Assert.AreEqual((float)BlendMode.One, toy.GetFloat("_SrcBlend"));
                Assert.AreEqual((float)BlendMode.Zero, toy.GetFloat("_DstBlend"));
                Assert.AreEqual(1f, toy.GetFloat("_ZWrite"));
                Assert.AreEqual((float)CullMode.Back, toy.GetFloat("_Cull"));
                Assert.AreEqual(2000, toy.renderQueue);
                // The one number that follows the tier - on materials that already exist, too.
                Assert.AreEqual(0f, toy.GetFloat("_DetailBump"), "the bump is off below High");
                Materials.Tier = QualityTier.High;
                Assert.AreEqual(0.6f, toy.GetFloat("_DetailBump"));
                Materials.Tier = QualityTier.Medium;
                Assert.AreEqual(0f, toy.GetFloat("_DetailBump"));

                Material glass = Materials.Toy(ToyRecipe.Glass.Named("Probe Glass" + run), Palette.Lagoon);
                Assert.AreEqual((float)BlendMode.SrcAlpha, glass.GetFloat("_SrcBlend"));
                Assert.AreEqual((float)BlendMode.OneMinusSrcAlpha, glass.GetFloat("_DstBlend"));
                Assert.AreEqual(0f, glass.GetFloat("_ZWrite"));
                Assert.AreEqual(0.22f, glass.GetFloat("_AlphaFace"));
                Assert.AreEqual(0.85f, glass.GetFloat("_AlphaEdge"));
                Assert.AreEqual(1f, glass.GetFloat("_ShadowDither"));
                Assert.AreEqual(3000, glass.renderQueue);
                Material[] set = Materials.ToySet(ToyRecipe.Glass.Named("Probe Glass" + run), Palette.Lagoon);
                Assert.AreEqual((float)CullMode.Front, set[0].GetFloat("_Cull"), "the back shell culls front faces");
                Assert.AreEqual(0.18f, set[0].GetFloat("_AlphaFace"));
                Assert.AreEqual(2999, set[0].renderQueue);

                Material signal = Materials.Emissive(ToyRecipe.GadgetSignal.Named("Probe Signal" + run), Palette.Ink, Palette.Lin(Palette.Go.Color) * Palette.Go.Gain);
                Assert.Less((Rgb(signal.GetVector("_Emission")) - Rgb(Palette.Lin(Palette.Go.Color)) * 2.6f).magnitude, 1e-4f, "emission is linear and HDR");
                Assert.AreEqual(0f, signal.GetFloat("_Rim"));

                // A room surface.
                RoomRecipe wall = RoomRecipe.For(RoomSurface.ShellWall, Palette.Lilac, PatternSpec.Quilt, -9f).With(r => r.Name = "Probe Wall" + run);
                Material room = Materials.Room(wall);
                Assert.AreSame(roomShader, room.shader);
                Assert.Less((Rgb(room.GetVector("_ColorTop")) - Rgb(Palette.Lin(Palette.Lilac.Light))).magnitude, 1e-5f);
                Assert.Less((Rgb(room.GetVector("_ColorSide")) - Rgb(Palette.Lin(Palette.Lilac.Light))).magnitude, 1e-5f);
                Assert.Less((Rgb(room.GetVector("_ColorDado")) - Rgb(Palette.Lin(Palette.Lilac.Mid))).magnitude, 1e-5f);
                Assert.AreEqual(21f, room.GetFloat("_DadoY"));
                Assert.AreEqual(5f, room.GetFloat("_Pattern"));
                Assert.AreEqual(new Vector4(6f, 0.1f, 0f, 0.04f), room.GetVector("_PatternA"));
                Assert.AreEqual(1f, room.GetFloat("_Corner"));
                Assert.AreEqual((float)CullMode.Back, room.GetFloat("_Cull"));

                // An unlit thing.
                var beam = new FlatRecipe { Name = "Probe Beam" + run, Color = Palette.Lin(Palette.Hazard.Color) * 3f, Shape = FlatShape.SoftDisc, Soft = 0.2f, Blend = FlatBlend.Additive };
                Material flat = Materials.Flat(beam);
                Assert.AreSame(flatShader, flat.shader);
                Assert.Less((Rgb(flat.GetVector("_Color")) - Rgb(Palette.Lin(Palette.Hazard.Color)) * 3f).magnitude, 1e-4f);
                Assert.AreEqual(1f, flat.GetFloat("_Shape"));
                Assert.AreEqual(0.2f, flat.GetFloat("_Soft"));
                Assert.AreEqual((float)BlendMode.One, flat.GetFloat("_SrcBlend"));
                Assert.AreEqual((float)BlendMode.One, flat.GetFloat("_DstBlend"));
                Assert.AreEqual(0f, flat.GetFloat("_ZWrite"));
                Assert.AreEqual((float)CompareFunction.LessEqual, flat.GetFloat("_ZTest"));
                Assert.AreEqual((float)CullMode.Off, flat.GetFloat("_Cull"));
                Assert.AreEqual(3000, flat.renderQueue);

                // The plain look ignores all of it.
                Materials.Plain = true;
                StringAssert.Contains("Universal Render Pipeline", Materials.Room(wall).shader.name);
                StringAssert.Contains("Universal Render Pipeline", Materials.Toy(wood, Palette.Grape).shader.name);
                Materials.Plain = false;
            }
            finally
            {
                Materials.Plain = false;
                Materials.OverrideTemplate("ToyLit", null);
                Materials.OverrideTemplate("RoomLit", null);
                Materials.OverrideTemplate("Flat", null);
                foreach (Object probe in new Object[] { toyTemplate, roomTemplate, flatTemplate, toyShader, roomShader, flatShader }) Object.DestroyImmediate(probe);
            }

            // Without a room template (today) a room surface is a stand-in on the toy template.
            Material standIn = Materials.Room(RoomRecipe.Solid(Palette.Kraft).With(r => r.Name = "Probe Stand-in" + run));
            Assert.IsNotNull(standIn);
            Assert.IsNotNull(standIn.shader);
            Assert.AreNotEqual("Toybox/Test/RoomProbe", standIn.shader.name, "the override is gone");
        }

        [Test]
        public void RoomAndFlatRecipes_ExpressEveryPropertyOfTheirShaders()
        {
            RoomRecipe wall = RoomRecipe.For(RoomSurface.ShellWall, Palette.Lilac, PatternSpec.Quilt, -9f);
            Assert.AreEqual(Palette.Lilac.Light, wall.Side);
            Assert.AreEqual(Palette.Lilac.Mid, wall.Dado);
            Assert.AreEqual(21f, wall.DadoY, "the dado line is 30 above the play plane");
            Assert.IsTrue(wall.Corner);
            Assert.AreEqual(new Vector4(6f, 0.1f, 0f, 0.04f), wall.Pattern.A);

            RoomRecipe floor = RoomRecipe.For(RoomSurface.ShellFloor, Palette.Mint, PatternSpec.Dots);
            Assert.AreEqual(Palette.Mint.Mid, floor.Top);
            Assert.AreEqual(RoomRecipe.NoDado, floor.DadoY);
            RoomRecipe platform = RoomRecipe.For(RoomSurface.LevelStatic, Palette.Mint);
            Assert.AreEqual(Palette.Mint.Light, platform.Top, "platforms read light-on-mid against the floor");
            Assert.AreEqual(Palette.Mint.Deep, platform.Side);
            Assert.AreEqual(RoomPattern.None, platform.Pattern.Pattern);
            RoomRecipe furniture = RoomRecipe.For(RoomSurface.Furniture, Palette.Mint);
            Assert.AreEqual(Palette.Mint.Mid, furniture.Top);
            Assert.AreEqual(Palette.Mint.Deep, furniture.Side);
            Assert.AreEqual(Palette.Paper, RoomRecipe.For(RoomSurface.Trim, Palette.Mint).Top);
            Assert.AreEqual(platform, RoomRecipe.For(RoomSurface.LevelStatic, Palette.Mint), "recipes are values");

            Assert.AreEqual(new Vector4(8f, 1.4f, 0f, 0.04f), PatternSpec.Dots.A);
            Assert.AreEqual(new Vector4(4f, 60f, 0.12f, 0.04f), PatternSpec.Planks.A);
            Assert.AreEqual(new Vector4(5f, 0.15f, 0f, 0.04f), PatternSpec.Tiles.A);
            Assert.AreEqual(12f, PatternSpec.Stripes.Pitch);
            Assert.AreEqual(new Vector4(0.85f, 0.11f, 0f, -0.25f), PatternSpec.Pegboard.A);
            Assert.AreEqual(new Vector4(0.15f, 0.5f, 0f, 0.04f), PatternSpec.Corrugated.A);
            Assert.AreEqual(PatternSpec.Quilt, PatternSpec.Of(RoomPattern.Quilt));
            Assert.AreEqual(0.02f, PatternSpec.Stripes.WithGain(0.02f).Gain);
            Assert.AreEqual(7, (int)RoomPattern.Corrugated, "the enum value is the shader's _Pattern");

            var hull = new FlatRecipe { Blend = FlatBlend.Multiply };
            Assert.AreEqual(BlendMode.DstColor, hull.SrcBlend);
            Assert.AreEqual(BlendMode.Zero, hull.DstBlend);
            Assert.IsFalse(hull.WritesDepth);
            Assert.AreEqual(3000, hull.RenderQueue);
            var sky = new FlatRecipe();
            Assert.AreEqual(BlendMode.One, sky.SrcBlend);
            Assert.AreEqual(BlendMode.Zero, sky.DstBlend);
            Assert.IsTrue(sky.WritesDepth);
            Assert.AreEqual(2000, sky.RenderQueue);
            Assert.AreEqual(BlendMode.OneMinusSrcAlpha, new FlatRecipe { Blend = FlatBlend.Alpha }.DstBlend);
            Assert.AreEqual(BlendMode.One, new FlatRecipe { Blend = FlatBlend.Additive }.DstBlend);
            Assert.AreEqual(3, (int)FlatShape.FourPane, "the enum value is the shader's _Shape");
        }

        [Test]
        public void Templates_ShipInResources()
        {
            Material toy = Resources.Load<Material>("Materials/ToyLit");
            Material plain = Resources.Load<Material>("Materials/PlainLit");
            Assert.IsNotNull(toy, "Resources/Materials/ToyLit.mat (CoreSetup.CreateMaterials)");
            Assert.IsNotNull(toy.shader);
            Assert.IsNotNull(plain, "Resources/Materials/PlainLit.mat (CoreSetup.CreateMaterials)");
            StringAssert.Contains("Universal Render Pipeline/Lit", plain.shader.name, "the plain look is URP Lit for good");
        }

        // ------------------------------------------------------------------------------------------
        // Toys
        // ------------------------------------------------------------------------------------------

        [Test]
        public void BasicToys_AreBevelledTaggedAndShareMeshesAndMaterials()
        {
            GameObject block = BasicToys.Block(new Vector3(1f, 0.5f, 2f), Palette.Grape);
            GameObject twin = BasicToys.Block(new Vector3(1f, 0.5f, 2f), Palette.Grape);
            GameObject ball = BasicToys.Ball(0.4f);
            GameObject drum = BasicToys.Cylinder(0.5f, 1.2f);
            GameObject wedge = BasicToys.Wedge(2f, 1f, 1.5f);
            GameObject slab = BasicToys.Slab(new Vector3(4f, 1f, 4f));
            GameObject ramp = BasicToys.Ramp(2f, 1f, 1.5f);
            try
            {
                MeshFilter filter = block.GetComponentInChildren<MeshFilter>();
                Assert.AreSame(filter.sharedMesh, twin.GetComponentInChildren<MeshFilter>().sharedMesh, "identical toys share a mesh");
                Assert.AreSame(block.GetComponentInChildren<MeshRenderer>().sharedMaterial, twin.GetComponentInChildren<MeshRenderer>().sharedMaterial,
                    "and a material");
                Assert.AreEqual(Vector3.one, filter.transform.localScale, "the mesh is built at its real size, so UVs are one repeat per unit");
                Assert.Less((filter.sharedMesh.bounds.size - new Vector3(1f, 0.5f, 2f)).magnitude, 1e-3f);
                Assert.Greater(MeshUtil.TriangleCount(filter.sharedMesh), 12, "bevelled, not a hard box");

                foreach (GameObject toy in new[] { block, ball, drum, wedge })
                {
                    ToyInfo info = ToyInfo.Of(toy);
                    Assert.IsNotNull(info, toy.name + " carries a ToyInfo");
                    Assert.AreEqual(ToyRecipe.GlossyPlastic, info.Recipe);
                    Assert.IsTrue(Palette.IsCandy(info.Candy), toy.name + " is a candy colour");
                    Assert.AreSame(Materials.Toy(info.Recipe, info.Candy), toy.GetComponentInChildren<MeshRenderer>().sharedMaterial);
                    Mesh mesh = toy.GetComponentInChildren<MeshFilter>().sharedMesh;
                    Assert.LessOrEqual(MeshUtil.TriangleCount(mesh), 3000);
                    var outline = new List<Vector3>();
                    mesh.GetUVs(3, outline);
                    Assert.AreEqual(mesh.vertexCount, outline.Count, toy.name + ": outline normals");
                    Assert.AreEqual(1, toy.GetComponentsInChildren<Collider>().Length);
                }
                Assert.IsTrue(Palette.Same(Palette.Grape, ToyInfo.Of(block).Candy));

                Assert.IsNull(ToyInfo.Of(slab), "a slab is part of the room, not a toy");
                Assert.IsNull(ToyInfo.Of(ramp));
                Assert.AreSame(Materials.Room(RoomSurface.LevelStatic), slab.GetComponentInChildren<MeshRenderer>().sharedMaterial);
                Assert.AreSame(MeshKit.UnitCylinderHull, drum.GetComponentInChildren<MeshCollider>().sharedMesh, "the collision hull is the one the simulation was tuned with");
                Assert.AreEqual(new Vector3(1f, 1.2f, 1f), drum.GetComponentInChildren<MeshCollider>().transform.localScale);

                Assert.Throws<System.ArgumentException>(() => ToyInfo.Tag(block, ToyRecipe.Felt, Palette.Lime, Vector4.one, Vector4.one, Vector4.one, Vector4.one));
                ToyInfo tagged = ToyInfo.Tag(block, ToyRecipe.Felt, Palette.Lime, new Vector4(0f, 0f, -0.5f, 0.3f), new Vector4(0f, 0f, 0.5f, 0.3f));
                Assert.AreSame(tagged, ToyInfo.Of(block), "tagging again replaces the tag");
                Assert.AreEqual(2, tagged.PoolProxies.Length);
            }
            finally
            {
                foreach (GameObject toy in new[] { block, twin, ball, drum, wedge, slab, ramp }) Object.DestroyImmediate(toy);
            }
        }
    }
}

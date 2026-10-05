using System;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Levels;
using Toybox.Toys;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// The toy catalog (LEVELS.md section 3, ART_BIBLE 4.1): every toy builds without a graphics device,
    /// stays inside the geometry rules and the draw budget, has honest convex colliders with the catalog's
    /// size and mass, and survives the mechanic - grabbed, made ten times bigger and dropped.
    /// </summary>
    public class ToyCatalogTests : SimTest
    {
        // Mass at scale 1 as LEVELS.md section 3 gives it.
        static readonly Dictionary<ToyId, float> CatalogMass = new Dictionary<ToyId, float>
        {
            { ToyId.CheeseWedge, 0.06f }, { ToyId.Thimble, 0.123f }, { ToyId.Apple, 2.094f }, { ToyId.Domino, 0.48f },
            { ToyId.Feather, 0.0054f }, { ToyId.Eraser, 0.18f }, { ToyId.Pebble, 1.571f }, { ToyId.Marble, 1.309f },
            { ToyId.Plank, 0.065f }, { ToyId.GiftBox, 5.44f }, { ToyId.PlayingCard, 1.12f }, { ToyId.Key, 0.056f },
            { ToyId.ThreadSpool, 0.38f }, { ToyId.Sponge, 0.0525f }, { ToyId.BouncyBall, 2.094f }, { ToyId.DeskFan, 0.106f },
            { ToyId.CatapultRuler, 0.15f }, { ToyId.DominoSet, 0.435f }, { ToyId.Baseball, 9.05f },
        };

        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void DestroyToys()
        {
            foreach (GameObject toy in made)
                if (toy != null) Object.DestroyImmediate(toy);
            made.Clear();
        }

        GameObject Make(ToyDef def, Color? color = null)
        {
            GameObject toy = def.Build(color);
            made.Add(toy);
            return toy;
        }

        GameObject Keep(GameObject toy)
        {
            made.Add(toy);
            return toy;
        }

        static IEnumerable<ToyId> Grabbables()
        {
            foreach (ToyDef def in ToyCatalog.All)
                if (def.Grabbable) yield return def.Id;
        }

        // Runs first in this fixture (by name), so after a script reload it times the builds with cold mesh
        // caches: what a level pays for the first toy of each kind. Later builds reuse the meshes.
        [Test]
        public void ABuild_OfEveryToy_IsQuick_AndTheSecondOneIsFree()
        {
            var watch = new System.Diagnostics.Stopwatch();
            var lines = new List<string>();
            double first = 0.0, second = 0.0;
            foreach (ToyDef def in ToyCatalog.All)
            {
                watch.Restart();
                Make(def);
                double cold = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                Make(def);
                double warm = watch.Elapsed.TotalMilliseconds;
                first += cold;
                second += warm;
                lines.Add(def.Name + " " + cold.ToString("0.0") + "/" + warm.ToString("0.00"));
            }
            Debug.Log("[Toybox] toy builds, first/again in ms: " + string.Join(", ", lines) + "; total " + first.ToString("0") + " / " + second.ToString("0.0"));
            Assert.Less(second, 100.0, "a toy whose meshes exist costs a few GameObjects");
            Assert.Less(first, 4000.0, "the first build of every toy in the catalog");

            // The catalog as measured, for the notes and for whoever reads the log.
            var table = new System.Text.StringBuilder("[Toybox] toy catalog (volume, mass, density, radius, triangles, draws):");
            foreach (ToyDef def in ToyCatalog.All)
                table.Append(' ').Append(def.Name).Append(' ').Append(def.Volume.ToString("0.####")).Append(' ').Append(def.Mass.ToString("0.####"))
                    .Append(' ').Append(def.Density.ToString("0.###")).Append(' ').Append(def.Radius.ToString("0.###")).Append(' ').Append(def.Triangles)
                    .Append(' ').Append(def.Draws).Append(';');
            Debug.Log(table.ToString());
        }

        // ---- The catalog ------------------------------------------------------------------------------------

        [Test]
        public void Catalog_EnumeratesEveryToyByIdAndSlug()
        {
            Array ids = Enum.GetValues(typeof(ToyId));
            Assert.AreEqual(ids.Length, ToyCatalog.All.Count, "one entry per ToyId");
            Assert.GreaterOrEqual(ToyCatalog.All.Count, 24);
            var slugs = new HashSet<string>();
            for (int i = 0; i < ToyCatalog.All.Count; i++)
            {
                ToyDef def = ToyCatalog.All[i];
                Assert.AreEqual((ToyId)i, def.Id, "the list is in the order of the ids");
                Assert.AreSame(def, ToyCatalog.Get(def.Id));
                Assert.IsTrue(slugs.Add(def.Slug), "slug " + def.Slug + " is taken twice");
                Assert.AreSame(def, ToyCatalog.Find(def.Slug));
                Assert.AreSame(def, ToyCatalog.Find(def.Name));
                Assert.IsNotNull(def.Recipe, def.Name);
                Assert.Greater(def.Levels.Count, 0, def.Name + " is used by some level");
                if (def.Grabbable)
                {
                    Assert.IsTrue(Palette.IsCandy(def.Color), def.Name + ": a toy carries a candy colour");
                    Assert.IsTrue(def.Recipe.Pool, def.Name + ": a toy has a pool");
                    Assert.Greater(def.Recipe.Rim, 0f, def.Name + ": a toy has the rim");
                    Assert.Less(def.MinScale, def.MaxScale, def.Name);
                }
                else
                {
                    Assert.IsFalse(Palette.IsCandy(def.Color), def.Name + ": a set piece never carries a candy colour");
                    Assert.IsFalse(def.Recipe.Pool, def.Name);
                    Assert.AreEqual(0f, def.Recipe.Rim, def.Name);
                }
            }
            Assert.AreEqual("cheese-wedge", ToyCatalog.Get(ToyId.CheeseWedge).Slug);
            Assert.IsNull(ToyCatalog.Find("no-such-toy"));
            Assert.IsNull(ToyCatalog.Find(null));

            // The key toy of every campaign level, for the cards of the level select.
            for (int level = 1; level <= 15; level++)
            {
                ToyDef hero = ToyCatalog.HeroOf(level);
                Assert.IsNotNull(hero, "level " + level + " has a hero toy");
                Assert.IsTrue(hero.Grabbable);
                CollectionAssert.Contains(hero.Levels, level);
            }
            Assert.AreEqual(ToyId.CheeseWedge, ToyCatalog.HeroOf(1).Id);
            Assert.AreEqual(ToyId.Doorway, ToyCatalog.HeroOf(14).Id);
            Assert.IsNull(ToyCatalog.HeroOf(0));
            Assert.IsNull(ToyCatalog.HeroOf(99));
        }

        [Test]
        public void Catalog_OptionsCarryTheNumbersOfTheLevelDocument()
        {
            PropOptions thimble = ToyCatalog.Options(ToyId.Thimble, 0.8f);
            Assert.AreEqual("Thimble", thimble.Name);
            Assert.AreEqual(0.8f, thimble.Scale);
            Assert.AreEqual(0.3f, thimble.MinScale);
            Assert.AreEqual(13f, thimble.MaxScale);
            Assert.AreEqual(0.6f, thimble.Friction);
            Assert.AreEqual(GrabPose.Upright, thimble.GrabPose);
            Assert.AreEqual(PropBody.Dynamic, thimble.Body);
            Assert.IsNull(thimble.Grabbable, "grabbable by default");
            Assert.AreNotSame(thimble, ToyCatalog.Options(ToyId.Thimble), "a fresh instance every time: levels change them");

            PropOptions eraser = ToyCatalog.Options(ToyId.Eraser);
            Assert.AreEqual(0.3f, eraser.Bounciness);
            Assert.AreEqual(0.9f, eraser.Friction);
            Assert.AreEqual(10.5f, eraser.MaxScale);
            Assert.AreEqual(GrabPose.Snap90, eraser.GrabPose);
            Assert.AreEqual(1.2f, eraser.Density, 1e-3f, "the eraser's hull is exact, so its density is the catalog's");

            Assert.AreEqual(0.5f, ToyCatalog.Options(ToyId.BouncyBall).Bounciness);
            Assert.AreEqual(4f, ToyCatalog.Options(ToyId.Apple).Density, 0.02f);
            Assert.AreEqual(GrabPose.Keep, ToyCatalog.Options(ToyId.Apple).GrabPose);
            Assert.AreEqual(5.44f, ToyCatalog.Options(ToyId.GiftBox).Density, 1e-3f);
            Assert.AreEqual(2f, ToyCatalog.Options(ToyId.GiftBox).MaxScale);
            Assert.AreEqual(0.05f, ToyCatalog.Options(ToyId.Key).MinScale);
            Assert.AreEqual(8f, ToyCatalog.Options(ToyId.Key).MaxScale);
            Assert.AreEqual(4.4f, ToyCatalog.Options(ToyId.Plank).MaxScale);

            PropOptions spool = ToyCatalog.Options(ToyId.ThreadSpool);
            Assert.IsTrue(spool.KeepUpright);
            Assert.AreEqual(GrabPose.Upright, spool.GrabPose);
            Assert.AreEqual(9.9f, spool.MaxScale);

            PropOptions doorway = ToyCatalog.Options(ToyId.Doorway);
            Assert.AreEqual(PropBody.Fixed, doorway.Body);
            Assert.IsFalse(doorway.AllowPitch);
            Assert.AreEqual(3.2f, doorway.MaxScale);
            Assert.AreEqual(0.1f, doorway.MinScale);

            Assert.AreEqual(false, ToyCatalog.Options(ToyId.Baseball).Grabbable);
            Assert.AreEqual(PropBody.Kinematic, ToyCatalog.Options(ToyId.TrainCar).Body);

            // A room that bans a toy's colour gives it its hero colour instead.
            ToyDef door = ToyCatalog.Get(ToyId.Doorway);
            Assert.IsTrue(Palette.Same(Palette.Lime, door.Color));
            Assert.IsTrue(Palette.Same(Palette.Cherry, door.ColorIn(Palette.Mint)), "Mint bans Lime");
            Assert.IsTrue(Palette.Same(Palette.Lime, door.ColorIn(Palette.Plum)));
            Assert.IsTrue(Palette.Same(Palette.Birch, ToyCatalog.Get(ToyId.Baseball).ColorIn(Palette.Mint)), "set pieces keep their tone");
        }

        // ---- Visuals: ART_BIBLE 4.1 --------------------------------------------------------------------------

        [Test]
        public void EveryToy_BuildsHeadlessly_TaggedBevelledAndWithinTheDrawBudget()
        {
            foreach (ToyDef def in ToyCatalog.All)
            {
                GameObject toy = Make(def);
                string name = def.Name;
                Assert.IsNull(toy.GetComponentInChildren<Rigidbody>(true), name + ": a toy has no Rigidbody");
                Assert.IsNull(toy.GetComponentInChildren<LODGroup>(true), name + ": no LODGroup");
                Assert.AreEqual(Vector3.one, toy.transform.localScale, name + ": authored at scale 1");

                ToyInfo info = ToyInfo.Of(toy);
                Assert.IsNotNull(info, name + ": tagged for render and audio");
                Assert.AreEqual(def.Recipe, info.Recipe, name + ": the tag names the recipe");
                Assert.IsTrue(Palette.Same(def.Color, info.Candy), name + ": the tag names the colour");
                Assert.LessOrEqual(info.PoolProxies.Length, 3, name);

                MeshFilter[] filters = toy.GetComponentsInChildren<MeshFilter>(true);
                Assert.Greater(filters.Length, 0, name + ": something to draw");
                int triangles = 0, draws = 0;
                foreach (MeshFilter filter in filters)
                {
                    Mesh mesh = filter.sharedMesh;
                    string part = name + " / " + filter.name;
                    Assert.IsNotNull(mesh, part);
                    Assert.IsNull(filter.GetComponent<Collider>(), part + ": visuals carry no collider");
                    Assert.Less(mesh.vertexCount, 65535, part);
                    triangles += MeshUtil.TriangleCount(mesh);

                    Vector3[] normals = mesh.normals;
                    Assert.AreEqual(mesh.vertexCount, normals.Length, part + ": a normal per vertex");
                    Assert.AreEqual(mesh.vertexCount, mesh.uv.Length, part + ": an object-space UV per vertex");
                    Assert.IsTrue(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord3), part + ": outline normals in TEXCOORD3");
                    var outline = new List<Vector3>();
                    mesh.GetUVs(3, outline);
                    Assert.AreEqual(mesh.vertexCount, outline.Count, part);
                    for (int i = 0; i < normals.Length; i++)
                    {
                        Assert.AreEqual(1f, normals[i].magnitude, 2e-3f, part + ": unit normals");
                        Assert.AreEqual(1f, outline[i].magnitude, 2e-3f, part + ": unit outline normals");
                    }
                    // Bevels: a toy shades smoothly across its edges, so far more normal directions than a box has.
                    foreach (Vector2 uv in mesh.uv)
                        Assert.IsFalse(float.IsNaN(uv.x) || float.IsNaN(uv.y), part);

                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    Assert.IsNotNull(renderer, part);
                    Assert.AreEqual(1, mesh.subMeshCount, part + ": merged per material");
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.IsNotNull(material, part + ": a material from Art.Materials");
                        draws++;
                    }
                }
                Assert.LessOrEqual(triangles, 3000, name + ": at most 3,000 triangles (has " + triangles + ")");
                Assert.LessOrEqual(draws, 3, name + ": at most three draws (has " + draws + ")");
                Assert.AreEqual(triangles, def.Triangles, name);
                Assert.AreEqual(draws, def.Draws, name);

                // The toy wears its own recipe and colour: all over its body or - the silver thimble - as the
                // band round its rim (TheThimble_IsSilver_AndWearsItsCandyAsABand).
                bool main = false;
                Material expected = Materials.Toy(def.Recipe, def.Color);
                foreach (MeshRenderer renderer in toy.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (Material material in renderer.sharedMaterials) main |= material == expected;
                Assert.IsTrue(main, name + ": it wears Materials.Toy(recipe, colour)");
            }
        }

        [Test]
        public void EveryBody_IsBevelled()
        {
            // A hard-edged box has six normal directions. A toy's body shades smoothly across its edges.
            foreach (ToyDef def in ToyCatalog.All)
            {
                // The feather's vane is a sheet: it has an outline, not edges.
                if (def.Id == ToyId.Feather) continue;
                GameObject toy = Make(def);
                Material body = Materials.Toy(def.Recipe, def.Color);
                var directions = new HashSet<Vector3Int>();
                foreach (MeshRenderer renderer in toy.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (Array.IndexOf(renderer.sharedMaterials, body) < 0) continue;
                    foreach (Vector3 normal in renderer.GetComponent<MeshFilter>().sharedMesh.normals)
                        directions.Add(Vector3Int.RoundToInt(normal * 20f));
                }
                Assert.Greater(directions.Count, 12, def.Name + ": bevelled edges with smooth normals (" + directions.Count + " normal directions)");
            }
        }

        [Test]
        public void Toys_ShareMeshesAndMaterials_AndTakeAnotherColour()
        {
            foreach (ToyDef def in ToyCatalog.All)
            {
                GameObject a = Make(def), b = Make(def);
                MeshFilter[] first = a.GetComponentsInChildren<MeshFilter>(true), second = b.GetComponentsInChildren<MeshFilter>(true);
                Assert.AreEqual(first.Length, second.Length, def.Name);
                for (int i = 0; i < first.Length; i++)
                {
                    Assert.AreSame(first[i].sharedMesh, second[i].sharedMesh, def.Name + ": one mesh for every toy of a kind");
                    Assert.AreSame(first[i].GetComponent<MeshRenderer>().sharedMaterial, second[i].GetComponent<MeshRenderer>().sharedMaterial, def.Name);
                }
                MeshCollider[] hulls = a.GetComponentsInChildren<MeshCollider>(true), others = b.GetComponentsInChildren<MeshCollider>(true);
                for (int i = 0; i < hulls.Length; i++) Assert.AreSame(hulls[i].sharedMesh, others[i].sharedMesh, def.Name + ": one hull");
            }

            GameObject marble = Make(ToyCatalog.Get(ToyId.Marble), Palette.Grape);
            Assert.IsTrue(Palette.Same(Palette.Grape, ToyInfo.Of(marble).Candy), "a marble in another colour");
            Assert.AreEqual(ToyRecipe.Glass, ToyInfo.Of(marble).Recipe);

            // The same shapes as set pieces: plain props without rim or pool, in Birch.
            GameObject block = Keep(ToyFactory.WoodenBlock(new Vector3(1.6f, 0.8f, 0.8f), grabbable: false));
            Assert.AreEqual(ToyRecipe.PlainProp, ToyInfo.Of(block).Recipe);
            Assert.IsTrue(Palette.Same(Palette.Birch, ToyInfo.Of(block).Candy));
            Assert.AreEqual(new Vector3(1.6f, 0.8f, 0.8f), block.GetComponent<BoxCollider>().size);
            GameObject pedestal = Keep(ToyFactory.ThreadSpool(0.4f, 1.1f, grabbable: false));
            Assert.IsFalse(ToyInfo.Of(pedestal).Recipe.Pool);
            GameObject machine = Keep(ToyFactory.DeskFan(grabbable: false));
            Assert.AreEqual(ToyRecipe.GadgetBody, ToyInfo.Of(machine).Recipe, "the fan of Level 5 is machinery: an Ink body");
            Assert.IsNotNull(machine.transform.Find(ToyFactory.DeskFanBlades), "the blades are a child of their own, to spin");
        }

        // ---- The silver thimble ------------------------------------------------------------------------------------

        static MeshRenderer Part(GameObject toy, string name)
        {
            Transform part = toy.transform.Find(name);
            Assert.IsNotNull(part, toy.name + " has no part called " + name);
            return part.GetComponent<MeshRenderer>();
        }

        // The area of a mesh that is seen from outside the thimble: what faces away from its axis above a
        // height, and what faces up.
        static float OutsideArea(Mesh mesh, float above)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            float area = 0f;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3 centre = (a + b + c) / 3f, cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-16f || centre.y < above) continue;
                Vector3 face = cross.normalized;
                Vector3 radial = new Vector3(centre.x, 0f, centre.z).normalized;
                if (Vector3.Dot(face, radial) > 0.2f || face.y > 0.5f) area += cross.magnitude * 0.5f;
            }
            return area;
        }

        // The owner's brief for Level 2: "a tiny silver thimble". Candy is still what says "you can lift
        // this", so the thimble wears its candy as a band round the rim - and is silver everywhere else.
        [Test]
        public void TheThimble_IsSilver_AndWearsItsCandyAsABand()
        {
            ToyDef def = ToyCatalog.Get(ToyId.Thimble);
            Assert.IsTrue(Palette.Same(Palette.Tangerine, def.Color), "its candy: the band, the pool, the tag");
            Assert.AreEqual(ToyRecipe.BrushedMetal, def.Recipe, "the band is anodised");

            // Silver is a neutral: a cool grey, a good way darker than the Paper of the die-cut border (a
            // body that is too pale melts into it) and nowhere near a candy colour.
            Assert.IsFalse(Palette.IsCandy(Palette.Silver));
            Color silverTone = Palette.Silver;
            Assert.Less(Mathf.Max(silverTone.r, Mathf.Max(silverTone.g, silverTone.b)) - Mathf.Min(silverTone.r, Mathf.Min(silverTone.g, silverTone.b)), 0.05f, "grey");
            Assert.GreaterOrEqual(silverTone.b, silverTone.r, "cool");
            Assert.That(silverTone.grayscale, Is.InRange(0.55f, 0.75f), "pale, and well under Paper (" + Palette.Paper.grayscale.ToString("0.00") + ")");

            // The recipe is metal that was never lacquered or anodised: brushed, with a toy's rim and pool.
            ToyRecipe bare = ToyFactory.Silver;
            Assert.AreEqual(LandSound.Metal, bare.Sound);
            Assert.Greater(bare.Metallic, 0.5f);
            Assert.AreEqual(0f, bare.Coat, "no lacquer");
            Assert.AreEqual(DetailTexture.Streak, bare.Detail);
            Assert.Greater(bare.Streak, ToyRecipe.BrushedMetal.Streak, "the brushing shows");
            Assert.Greater(bare.Rim, 0f, "a toy has the rim");
            Assert.Less(bare.Rim, ToyRecipe.BrushedMetal.Rim, "less of it: a Paper rim on silver is what melts into the border");
            Assert.IsTrue(bare.Pool);

            GameObject toy = Make(def);
            MeshRenderer body = Part(toy, "Visual"), band = Part(toy, "Band"), dimples = Part(toy, "Detail");
            Material silver = Materials.Toy(ToyFactory.Silver, Palette.Silver);
            Assert.AreSame(silver, body.sharedMaterial, "the body is bare silver");
            Assert.AreSame(Materials.Toy(ToyRecipe.BrushedMetal, Palette.Tangerine), band.sharedMaterial, "the band is anodised Tangerine");
            Assert.AreNotSame(silver, dimples.sharedMaterial, "the dimples are hollows: a material of their own");
            Assert.AreNotSame(band.sharedMaterial, dimples.sharedMaterial);

            // What the game is told about it is its candy: the tag, and so the pool on the floor.
            ToyInfo info = ToyInfo.Of(toy);
            Assert.IsTrue(Palette.Same(Palette.Tangerine, info.Candy));
            Color pool = info.Recipe.PoolColor(info.Candy), tangerine = Palette.Lin(Palette.Tangerine);
            Assert.Less(Mathf.Abs(pool.r - tangerine.r) + Mathf.Abs(pool.g - tangerine.g) + Mathf.Abs(pool.b - tangerine.b), 1e-4f, "its pool is its candy's");

            // Silver first: the band is the rolled rim and a collar, the lowest fifth, and a sixth of what
            // is seen from outside.
            Mesh bandMesh = band.GetComponent<MeshFilter>().sharedMesh, bodyMesh = body.GetComponent<MeshFilter>().sharedMesh;
            float rim = -ToyFactory.ThimbleHeight * 0.5f, bandTop = rim + ToyFactory.ThimbleBandHeight;
            Assert.AreEqual(rim, bandMesh.bounds.min.y, 1e-4f, "the band starts at the rim");
            Assert.AreEqual(bandTop, bandMesh.bounds.max.y, 1e-4f);
            Assert.That(ToyFactory.ThimbleBandHeight / ToyFactory.ThimbleHeight, Is.InRange(0.15f, 0.25f));
            float candy = OutsideArea(bandMesh, rim - 1f), metal = OutsideArea(bodyMesh, bandTop);
            Debug.Log("[Toybox] thimble, seen from outside: silver " + metal.ToString("0.00") + ", candy " + candy.ToString("0.00"));
            Assert.That(candy / (candy + metal), Is.InRange(0.1f, 0.22f), "enough candy to say 'yours', little enough to be silver first");
            // Seated in the well of Level 2 its top is the floor: no candy up there.
            Assert.Less(bandMesh.bounds.max.y, 0f);

            // Everything it draws stays within the box of its collider (the dimples stand 0.003 proud of the top).
            foreach (MeshFilter filter in toy.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Assert.LessOrEqual(new Vector2(vertex.x, vertex.z).magnitude, ToyFactory.ThimbleRimRadius + 1e-4f, filter.name + " at " + vertex);
                    Assert.LessOrEqual(Mathf.Abs(vertex.y), ToyFactory.ThimbleHeight * 0.5f + 0.0031f, filter.name + " at " + vertex);
                }

            // In another colour only the band changes; a room that bans Tangerine gives it its hero candy.
            GameObject other = Make(def, Palette.Lagoon);
            Assert.AreSame(silver, Part(other, "Visual").sharedMaterial, "silver whatever the colour asked for");
            Assert.AreSame(Materials.Toy(ToyRecipe.BrushedMetal, Palette.Lagoon), Part(other, "Band").sharedMaterial);
            Assert.AreSame(dimples.sharedMaterial, Part(other, "Detail").sharedMaterial);
            Assert.IsTrue(Palette.Same(Palette.Lagoon, ToyInfo.Of(other).Candy));
            Assert.IsTrue(Palette.Same(Palette.Lagoon, def.ColorIn(Palette.Peach)), "Peach bans Tangerine");
            Assert.IsTrue(Palette.Same(Palette.Tangerine, def.ColorIn(Palette.Pool)), "Level 2's room: its own");
        }

        // What keeps a silver thimble from being a flat grey: its collider is all but a cylinder, and a
        // mirror cylinder shows one band of the room from top to bottom. So the wall's normals lean as a
        // domed thimble's would (the ceiling at the shoulder, the floor at the foot), and the top is turned
        // in rings that lean in and out by turns.
        [Test]
        public void TheThimble_MirrorsTheRoom_WithADomedWallAndATurnedTop()
        {
            GameObject toy = Make(ToyCatalog.Get(ToyId.Thimble));
            Mesh mesh = Part(toy, "Visual").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            float half = ToyFactory.ThimbleHeight * 0.5f, bandTop = -half + ToyFactory.ThimbleBandHeight;

            // The wall, above the band: its lean (degrees above the level) by height.
            var leans = new SortedDictionary<int, float>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                var radial = new Vector3(p.x, 0f, p.z);
                if (radial.magnitude < 0.47f || p.y < bandTop - 1e-4f || p.y > 0.33f + 1e-4f) continue;
                if (Vector3.Dot(normals[i], radial.normalized) < 0.9f) continue;
                leans[Mathf.RoundToInt(p.y * 1000f)] = Mathf.Asin(normals[i].y) * Mathf.Rad2Deg;
            }
            Assert.GreaterOrEqual(leans.Count, 4, "rows up the wall, for the lean to turn on");
            float previous = float.NegativeInfinity, first = float.NaN, last = float.NaN;
            foreach (KeyValuePair<int, float> row in leans)
            {
                Assert.Greater(row.Value, previous, "the higher, the more it leans (at y = " + row.Key * 0.001f + ")");
                if (float.IsNaN(first)) first = row.Value;
                previous = last = row.Value;
            }
            Assert.That(first, Is.InRange(0f, 6f), "all but level at the band");
            Assert.That(last, Is.InRange(10f, 20f), "and into the shoulder at the top");

            // The top: flat, at the collider's top, in rings whose normals lean toward the axis and away from
            // it by turns.
            var rings = new HashSet<int>();
            int inward = 0, outward = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                var radial = new Vector3(p.x, 0f, p.z);
                if (p.y < half - 0.01f || radial.magnitude > 0.4f + 1e-4f || normals[i].y < 0.97f) continue;
                Assert.AreEqual(half, p.y, 1e-5f, "the top is the collider's: flat");
                rings.Add(Mathf.RoundToInt(radial.magnitude * 1000f));
                if (radial.magnitude < 1e-4f) continue;
                float tilt = Vector3.Dot(normals[i], radial.normalized);
                if (tilt > 0.03f) outward++;
                if (tilt < -0.03f) inward++;
            }
            Assert.GreaterOrEqual(rings.Count, 7, "ring after ring");
            Assert.Greater(inward, 0, "some lean toward the axis");
            Assert.Greater(outward, 0, "and their neighbours away from it");
        }

        // ---- Colliders ------------------------------------------------------------------------------------------

        [Test]
        public void EveryToy_HasConvexCollidersOfTheCatalogsSizeAndMass()
        {
            foreach (ToyDef def in ToyCatalog.All)
            {
                GameObject toy = Make(def);
                string name = def.Name;
                Collider[] colliders = toy.GetComponentsInChildren<Collider>(true);
                Assert.Greater(colliders.Length, 0, name + ": something to collide with");
                foreach (Collider collider in colliders)
                {
                    Assert.IsFalse(collider.isTrigger, name);
                    Assert.IsTrue(collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider || collider is MeshCollider, name + ": " + collider.GetType().Name);
                    if (collider is MeshCollider hull)
                    {
                        Assert.IsTrue(hull.convex, name + ": mesh colliders are convex");
                        AssertClosedConvexHull(hull.sharedMesh, name);
                    }
                }

                PropGeometry.Measure(toy.transform, colliders, out float volume, out Vector3 center, out Vector3 halfExtents, out float radius);
                Assert.Greater(volume, 0f, name + ": positive volume");
                Assert.AreEqual(volume, def.Volume, volume * 1e-4f, name);
                Assert.Less((center - def.Center).magnitude, 1e-4f, name);
                Assert.AreEqual(radius, def.Radius, 1e-4f, name);
                Assert.Less((halfExtents * 2f - def.Size).magnitude, 0.011f, name + ": the colliders fill the authored size " + def.Size + ", they measure " + halfExtents * 2f);

                // The bounding radius is that of the colliders: it holds their box, and no more than its diagonal.
                Assert.GreaterOrEqual(radius, Mathf.Max(halfExtents.x, Mathf.Max(halfExtents.y, halfExtents.z)) - 1e-4f, name);
                Assert.LessOrEqual(radius, halfExtents.magnitude + 1e-4f, name);

                Assert.AreEqual(def.Mass, def.Density * def.Volume, def.Mass * 1e-4f, name);
                Assert.Greater(def.Mass, 0f, name);
                if (CatalogMass.TryGetValue(def.Id, out float mass))
                    Assert.AreEqual(mass, def.Mass, mass * 0.01f, name + ": the mass of the level document at scale 1");
                if (def.Body == PropBody.Dynamic)
                    Assert.That(def.Mass, Is.InRange(0.004f, 12f), name + ": a sensible mass at scale 1");
            }

            // Where the colliders are exact the density is the catalog's own.
            Assert.AreEqual(0.3f, ToyCatalog.Get(ToyId.CheeseWedge).Density, 1e-3f);
            Assert.AreEqual(0.8f, ToyCatalog.Get(ToyId.Domino).Density, 1e-3f);
            Assert.AreEqual(2.5f, ToyCatalog.Get(ToyId.Marble).Density, 0.01f);
            Assert.AreEqual(0.15f, ToyCatalog.Get(ToyId.Sponge).Density, 1e-3f);
            Assert.AreEqual(0.20f, ToyCatalog.Get(ToyId.CheeseWedge).Volume, 1e-4f);
            Assert.AreEqual(0.15f, ToyCatalog.Get(ToyId.Eraser).Volume, 1e-4f);
            // And where a hull is a little smaller than the ideal shape, the density makes up for it.
            Assert.That(ToyCatalog.Get(ToyId.Thimble).Volume, Is.InRange(0.59f, 0.616f), "a 16-sided frustum inside the round one");
            Assert.That(ToyCatalog.Get(ToyId.ThreadSpool).Volume, Is.InRange(0.91f, 0.950f));

            // Origins that are not the middle of the colliders, as the catalog says.
            Assert.AreEqual(0f, ToyCatalog.Get(ToyId.Doorway).RestHeight, 1e-4f, "the doorway's origin is its threshold");
            Assert.AreEqual(0f, ToyCatalog.Get(ToyId.DeskFan).RestHeight, 1e-4f, "the fan's origin is under its base");
            Assert.AreEqual(0.04f, ToyCatalog.Get(ToyId.CatapultRuler).RestHeight, 1e-4f, "the ruler's origin is the middle of its plank");
            Assert.AreEqual(1.25f, ToyCatalog.Get(ToyId.Doorway).Center.y, 1e-4f);
            Assert.AreEqual(0.4f, ToyCatalog.Get(ToyId.Thimble).RestHeight, 1e-4f);
        }

        // Every edge is shared by exactly two triangles that run along it in opposite directions, and no
        // vertex lies outside any face.
        static void AssertClosedConvexHull(Mesh mesh, string name)
        {
            Assert.IsNotNull(mesh, name);
            Assert.IsTrue(mesh.isReadable, name + ": the simulation measures the hull");
            Vector3[] p = mesh.vertices;
            int[] t = mesh.triangles;
            Assert.LessOrEqual(p.Length, 255, name + ": PhysX hull limit");
            Assert.GreaterOrEqual(t.Length, 12, name);
            var edges = new Dictionary<long, int>();
            float size = mesh.bounds.size.magnitude;
            double volume = 0.0;
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                foreach ((int u, int v) in new[] { (a, b), (b, c), (c, a) })
                {
                    long key = ((long)u << 32) | (uint)v;
                    edges.TryGetValue(key, out int count);
                    edges[key] = count + 1;
                }
                Vector3 normal = Vector3.Cross(p[b] - p[a], p[c] - p[a]).normalized;
                foreach (Vector3 point in p)
                    Assert.LessOrEqual(Vector3.Dot(normal, point - p[a]), size * 1e-4f, name + ": convex, and wound outward");
                volume += Vector3.Dot(p[a], Vector3.Cross(p[b], p[c])) / 6.0;
            }
            foreach (KeyValuePair<long, int> edge in edges)
            {
                Assert.AreEqual(1, edge.Value, name + ": no edge is used twice in one direction");
                long reverse = ((edge.Key & 0xFFFFFFFFL) << 32) | (uint)(edge.Key >> 32);
                Assert.IsTrue(edges.ContainsKey(reverse), name + ": closed - every edge has its opposite");
            }
            Assert.Greater(volume, 0.0, name);
        }

        [Test]
        public void TheDominoSet_KeepsItsPiecesWhereAGadgetFindsThem()
        {
            GameObject set = Make(ToyCatalog.Get(ToyId.DominoSet));
            Transform pieces = set.transform.Find(ToyFactory.DominoSetPieces);
            Assert.IsNotNull(pieces);
            float[] heights = { 0.30f, 0.39f, 0.51f, 0.66f, 0.86f, 1.11f, 1.45f };
            float[] nearX = { 0.15f, 0.36f, 0.63f, 0.99f, 1.45f, 2.05f, 2.83f };
            for (int i = 0; i < ToyFactory.DominoSetCount; i++)
            {
                Transform piece = pieces.Find("Piece " + (i + 1));
                Assert.IsNotNull(piece, "Piece " + (i + 1));
                BoxCollider box = piece.GetComponent<BoxCollider>();
                Assert.IsNotNull(box);
                Assert.AreEqual(heights[i], ToyFactory.DominoSetHeight(i), 1e-5f);
                Assert.Less((box.size - ToyFactory.DominoPieceSize(heights[i])).magnitude, 1e-5f);
                // In the set's space the piece is 0.15 h thick along X, h tall and 0.5 h wide, with its near face where the level document puts it.
                Bounds bounds = new Bounds(piece.localPosition, piece.localRotation * box.size);
                Vector3 extent = new Vector3(Mathf.Abs(bounds.size.x), Mathf.Abs(bounds.size.y), Mathf.Abs(bounds.size.z));
                Assert.Less((extent - new Vector3(0.15f * heights[i], heights[i], 0.5f * heights[i])).magnitude, 1e-4f);
                Assert.AreEqual(nearX[i] - 1.6f, piece.localPosition.x - extent.x * 0.5f, 1e-4f, "near face of piece " + (i + 1));
                Assert.AreEqual(-0.70f, piece.localPosition.y - extent.y * 0.5f, 1e-4f, "it stands on the strip");
                Vector3 hinge = ToyFactory.DominoSetHinge(i);
                Assert.AreEqual(piece.localPosition.x + extent.x * 0.5f, hinge.x, 1e-4f);
                Assert.AreEqual(-0.70f, hinge.y, 1e-4f);
            }
            Assert.AreEqual(3.05f - 1.6f, ToyFactory.DominoSetHinge(6).x, 0.005f, "domino 7's far face is at X = 3.05");

            // Switching the pieces off leaves the strip: its collider and one draw.
            pieces.gameObject.SetActive(false);
            Assert.AreEqual(1, set.GetComponentsInChildren<Collider>(false).Length);
            Assert.AreEqual(1, set.GetComponentsInChildren<MeshRenderer>(false).Length);
            // And a single piece is a toy of its own, for the gadget's falling bodies.
            GameObject single = Keep(ToyFactory.DominoPiece(1.45f, Palette.Lime));
            Assert.Less((single.GetComponent<BoxCollider>().size - ToyFactory.DominoPieceSize(1.45f)).magnitude, 1e-5f);

            GameObject ruler = Make(ToyCatalog.Get(ToyId.CatapultRuler));
            Assert.IsNotNull(ruler.transform.Find(ToyFactory.CatapultMarble), "the marble in the cap can be hidden by the gadget that shoots it");
            Assert.IsNull(ruler.transform.Find(ToyFactory.CatapultMarble).GetComponent<Collider>());
        }

        // ---- In the simulation ----------------------------------------------------------------------------------

        [Test]
        public void Add_MakesAPropWithTheCatalogsOptions()
        {
            Prop thimble = null, apple = null, door = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                thimble = ToyCatalog.Add(ctx, ToyId.Thimble, new Vector3(0f, 0.33f, 4f), 0.8f, options: o => o.Tags = new[] { "plug" });
                apple = ctx.AddProp(ToyFactory.Apple(), new Vector3(3f, 0.5f, 4f), ToyCatalog.Options(ToyId.Apple));
                door = ToyCatalog.Add(ctx, ToyId.Doorway, new Vector3(-4f, 0f, 4f), Quaternion.Euler(0f, 180f, 0f));
            });
            Assert.AreEqual("Thimble", thimble.Name);
            Assert.IsTrue(thimble.HasTag("plug"));
            Assert.AreEqual(0.8f, thimble.Scale);
            Assert.AreEqual(13f, thimble.MaxScale);
            Assert.AreEqual(GrabPose.Upright, thimble.GrabPose);
            Assert.IsTrue(thimble.Grabbable);
            ToyDef def = ToyCatalog.Get(ToyId.Thimble);
            Assert.AreEqual(def.Volume, thimble.BaseVolume, 1e-4f);
            Assert.AreEqual(def.Radius, thimble.BaseRadius, 1e-4f);
            Assert.AreEqual(0.123f * 0.8f * 0.8f * 0.8f, thimble.Mass, 0.002f, "0.123 s^3");
            // Silver; the candy it is known by is the band round its rim (Mint does not ban Tangerine).
            Assert.IsTrue(Palette.Same(Palette.Tangerine, ToyInfo.Of(thimble.GameObject).Candy));
            Assert.AreSame(Materials.Toy(ToyFactory.Silver, Palette.Silver), Part(thimble.GameObject, "Visual").sharedMaterial);
            Assert.AreSame(Materials.Toy(ToyRecipe.BrushedMetal, Palette.Tangerine), Part(thimble.GameObject, "Band").sharedMaterial);

            Assert.AreEqual(2.094f, apple.Mass, 0.02f);
            Assert.AreEqual(0.5f, apple.BaseRadius, 1e-4f);
            Assert.AreEqual(PropBody.Fixed, door.BodyKind);
            Assert.IsFalse(door.AllowPitch);
            Assert.IsTrue(door.Grabbable, "a Fixed prop is grabbable");
            Assert.Less((door.Center - new Vector3(-4f, 1.25f, 4f)).magnitude, 1e-3f, "the doorway's centre is 1.25 s above its base");
            // Ad-hoc levels have the Mint dip, which bans the doorway's Lime.
            Assert.IsTrue(Palette.Same(Palette.Cherry, ToyInfo.Of(door.GameObject).Candy));

            RunSeconds(1f);
            Assert.AreEqual(0.32f, thimble.Position.y, 0.02f, "the thimble rests on its rim");
        }

        [TestCaseSource(nameof(Grabbables))]
        public void AToy_IsGrabbed_EnlargedTenTimes_AndDropped_WithoutThePhysicsExploding(ToyId id)
        {
            ToyDef def = ToyCatalog.Get(id);
            const float start = 0.3f;
            Prop prop = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 240f);
                PropOptions options = def.Options(start);
                // The catalog's clamps are the levels' puzzles; here the clamp is the tenfold itself.
                options.MinScale = 0.02f;
                options.MaxScale = start * 10f;
                prop = ctx.AddProp(def.Build(), new Vector3(0f, def.RestHeight * start + 0.01f, 2.5f), options);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            RunSeconds(0.75f);
            Assert.Less((prop.Position - new Vector3(0f, def.RestHeight * start, 2.5f)).magnitude, 0.25f, def.Name + " rests where it was put");
            float smallMass = prop.Mass;

            // Aim at the part of it nearest its middle (the middle of a doorway is its opening).
            Collider nearest = prop.Colliders[0];
            foreach (Collider collider in prop.Colliders)
                if ((collider.bounds.center - prop.Center).sqrMagnitude < (nearest.bounds.center - prop.Center).sqrMagnitude) nearest = collider;
            LookAt(nearest.bounds.center);
            Click();
            Assert.AreSame(prop, Game.Grabber.Held, def.Name + " can be grabbed");

            // Up into the open: nothing stops it before the clamp does.
            Game.Player.Yaw = 0f;
            Game.Player.Pitch = 32f;
            Run(PerspectiveGrabber.PoseTicks + 3);
            Assert.AreEqual(start * 10f, prop.Scale, start * 0.01f, def.Name + " grows to ten times its size");
            Click();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsFalse(prop.Held);
            Assert.AreEqual(start * 10f, prop.Scale, start * 0.01f);
            Vector3 dropped = prop.Center;
            if (prop.BodyKind == PropBody.Dynamic)
                Assert.AreEqual(Mathf.Max(Prop.MinMass, def.Mass * 27f), prop.Mass, prop.Mass * 0.01f, def.Name + ": mass goes with scale cubed");
            Assert.GreaterOrEqual(prop.Mass, smallMass);

            float highest = dropped.y;
            for (int i = 0; i < TestHelpers.Ticks(9f); i++)
            {
                Game.Tick();
                Vector3 p = prop.Center;
                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z), def.Name + ": a position that is a number");
                highest = Mathf.Max(highest, p.y);
            }
            Assert.LessOrEqual(highest, dropped.y + 0.5f, def.Name + " was not thrown upward");
            if (prop.BodyKind != PropBody.Dynamic)
            {
                Assert.Less((prop.Center - dropped).magnitude, 1e-3f, def.Name + " is Fixed: it stays where it was dropped");
                return;
            }
            Vector3 rest = prop.Center;
            float radius = prop.Radius;
            Assert.That(rest.y, Is.InRange(0f, radius + 0.05f), def.Name + " lies on the floor, not in it and not above it (centre " + rest + ", radius " + radius + ")");
            Assert.Less(new Vector2(rest.x - dropped.x, rest.z - dropped.z).magnitude, 25f, def.Name + " came down where it was let go");
            Assert.Less(prop.Velocity.magnitude, 1.5f, def.Name + " has come to rest");
            Assert.Less(prop.Body.angularVelocity.magnitude, 6f, def.Name + " is not spinning");
            Assert.Less(TestHelpers.DeepestOverlap(Game, prop), 0.15f, def.Name + " is not sunk into the floor");
        }

        // The walk-on rule of the level document: wedge (knife edge), feather (thin rim), eraser (ramped ends)
        // and key (chamfered edges) are walked onto at their bridge sizes without a jump.
        [TestCase(ToyId.CheeseWedge, 9.4f, 0f, 3.8f)]
        [TestCase(ToyId.Eraser, 9.5f, 90f, 2.375f)]
        [TestCase(ToyId.Key, 8f, 90f, 0.32f)]
        [TestCase(ToyId.Feather, 12f, 90f, 0.36f)]
        public void WalkOnToys_AreWalkedOntoWithoutAJump(ToyId id, float scale, float yaw, float height)
        {
            ToyDef def = ToyCatalog.Get(id);
            Prop prop = null;
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            // Where to stand on it in the end, in the toy's own space at scale 1: over the wedge's upper
            // half, the middle of the eraser, the key's bow, the feather's quill.
            Vector3 goal = id == ToyId.CheeseWedge ? new Vector3(0f, 0f, 0.3f) * scale : id == ToyId.Key ? new Vector3(0f, 0f, -0.7f) * scale : Vector3.zero;
            float reach = Vector3.Dot(def.HalfExtents, new Vector3(Mathf.Abs(forward.x), 0f, Mathf.Abs(forward.z))) * scale;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 120f);
                prop = ctx.AddProp(def.Build(), new Vector3(0f, def.RestHeight * scale + 0.01f, 0f), def.Options(scale));
                ctx.SetSpawn(goal - forward * (reach + 3f + Vector3.Dot(goal, forward)), yaw);
            });
            RunSeconds(1f);
            Assert.AreEqual(scale, prop.Scale, 1e-4f, "the catalog's clamps allow the size the level uses");
            Vector3 lay = prop.Position;

            Input.Hold.MoveZ = 1f;
            bool arrived = TestHelpers.RunUntil(Game, () => Vector3.Dot(Game.Player.Position - goal, forward) >= 0f, 8f);
            Input.Hold.MoveZ = 0f;
            RunSeconds(0.3f);
            Assert.IsTrue(arrived, def.Name + ": the player walks onto it (stopped at " + Game.Player.Position + ")");
            Assert.IsTrue(Game.Player.Grounded, def.Name);
            Assert.AreSame(prop, Game.Player.GroundProp, def.Name + ": standing on the toy");
            Assert.AreEqual(height, Game.Player.Position.y, 0.12f, def.Name + ": on top of it");
            Assert.Less((prop.Position - lay).magnitude, 0.6f, def.Name + " was walked onto, not shoved away");
        }

        [Test]
        public void TheDoorway_LetsThePlayerThroughItsPosts_ButStillStopsRaysAndToys()
        {
            Prop door = null, block = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                door = ToyCatalog.Add(ctx, ToyId.Doorway, new Vector3(0f, 0f, 4f));
                block = ToyCatalog.Add(ctx, ToyId.WoodenBlock, new Vector3(0.7f, 3f, 4f), 0.25f);
                // Straight at the right-hand post.
                ctx.SetSpawn(new Vector3(0.7f, 0f, 0f), 0f);
            });
            Input.Hold.MoveZ = 1f;
            RunSeconds(1.6f);
            Assert.Greater(Game.Player.Position.z, 6f, "the frame never collides with the player: the capsule walked through the post");
            Assert.Less(Mathf.Abs(Game.Player.Position.x - 0.7f), 0.05f, "and was not turned aside");
            Assert.AreEqual(PropBody.Fixed, door.BodyKind);
            Assert.Less((door.Position - new Vector3(0f, 0f, 4f)).magnitude, 1e-4f);

            // Queries see the frame like any prop, and other props collide with it: the block lies on the lintel.
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0.7f, 1f, 0f), Vector3.forward, out RaycastHit hit, 20f, Layers.PropMask, QueryTriggerInteraction.Ignore));
            Assert.AreSame(door, PropRef.Of(hit.collider));
            Assert.AreEqual(3.85f, hit.point.z, 0.01f, "the near face of the post");
            Assert.AreEqual(2.5f + 0.125f, block.Position.y, 0.03f, "the block rests on top of the frame");
        }

        // ---- The showroom ----------------------------------------------------------------------------------------

        [Test]
        public void TheShowroom_ShowsEveryToyAtRest_AndItsTourReachesEachOne()
        {
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(99);
            var showroom = (Level99Showroom)Game.Level;
            Assert.AreEqual("showroom", showroom.Slug);
            Assert.AreEqual(0, showroom.Phase, "not part of the campaign");
            Assert.AreEqual(ToyCatalog.All.Count + Level99Showroom.Columns, showroom.StopCount);

            var places = new Dictionary<Prop, Vector3>();
            foreach (Prop prop in Game.Props) places[prop] = prop.Position;
            foreach (ToyDef def in ToyCatalog.All)
            {
                Prop prop = showroom.PropOf(def.Id);
                Assert.IsNotNull(prop, def.Name + " is in the showroom");
                Assert.AreEqual(1f, prop.Scale, def.Name + " is shown at its authored size");
                Assert.IsTrue(Palette.Same(def.Color, ToyInfo.Of(prop.GameObject).Candy), def.Name + " is shown in its own colour");
                Assert.AreEqual(def.Grabbable, prop.Grabbable, def.Name);
                foreach (ToyDef other in ToyCatalog.All)
                    if (other.Id > def.Id)
                        Assert.IsFalse(Around(prop, 0.15f).Intersects(Around(showroom.PropOf(other.Id), 0.15f)), def.Name + " and " + other.Name + " stand apart");
            }

            RunSeconds(3f);
            foreach (KeyValuePair<Prop, Vector3> entry in places)
            {
                Assert.Less((entry.Key.Position - entry.Value).magnitude, 0.12f, entry.Key.Name + " stays where it was put");
                Assert.Less(entry.Key.Velocity.magnitude, 0.2f, entry.Key.Name + " is at rest");
            }

            // The tour, from a fresh load: no command fails, every stop gets its time, and nothing is kicked on the way.
            Game.LoadLevel(99);
            showroom = (Level99Showroom)Game.Level;
            places.Clear();
            foreach (Prop prop in Game.Props) places[prop] = prop.Position;
            var bot = new Bot(Game);
            float total = showroom.LookTime(showroom.StopCount - 1) + 0.2f;
            BotRunner.Run(Game, showroom.Solve(bot), total + 30f);
            Assert.AreEqual(total, Game.Time, 0.5f, "the tour keeps its timetable, so Shots knows when to look");
            for (int i = 1; i < showroom.StopCount; i++) Assert.Greater(showroom.LookTime(i), showroom.LookTime(i - 1));
            Assert.AreEqual(Level99Showroom.StopSeconds - 0.2f, showroom.LookTime(ToyId.WoodenBlock), 1e-3f);
            foreach (KeyValuePair<Prop, Vector3> entry in places)
                Assert.Less((entry.Key.Position - entry.Value).magnitude, 0.2f, entry.Key.Name + " was not disturbed by the tour");
            Assert.IsFalse(Game.LevelCompleted, "there is nothing to complete");
        }

        // The box round a prop's colliders, grown by a margin.
        static Bounds Around(Prop prop, float margin)
        {
            Physics.SyncTransforms();
            Bounds bounds = prop.Colliders[0].bounds;
            foreach (Collider collider in prop.Colliders) bounds.Encapsulate(collider.bounds);
            bounds.Expand(margin * 2f);
            return bounds;
        }

        // ---- Silhouettes -------------------------------------------------------------------------------------------

        [Test]
        public void Silhouettes_AreStencilsOfTheToys()
        {
            const int size = 64;
            foreach (ToyDef def in ToyCatalog.All)
            {
                byte[] mask = ToySilhouette.Mask(def.Id, size);
                Assert.AreEqual(size * size, mask.Length, def.Name);
                Assert.AreSame(mask, ToySilhouette.Mask(def.Id, size), def.Name + ": cached");
                int covered = 0, minX = size, maxX = -1, minY = size, maxY = -1;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        if (mask[y * size + x] < 128) continue;
                        covered++;
                        minX = Mathf.Min(minX, x);
                        maxX = Mathf.Max(maxX, x);
                        minY = Mathf.Min(minY, y);
                        maxY = Mathf.Max(maxY, y);
                    }
                float share = covered / (float)(size * size);
                Assert.That(share, Is.InRange(0.03f, 0.8f), def.Name + ": a shape, not a blank and not a block (" + share + ")");
                Assert.GreaterOrEqual(Mathf.Max(maxX - minX, maxY - minY), size * 0.8f, def.Name + ": it fills the picture");
                Assert.Greater(minX, 0, def.Name + ": with a margin");
                Assert.Less(maxX, size - 1, def.Name);
                Assert.Greater(minY, 0, def.Name);
                Assert.Less(maxY, size - 1, def.Name);
            }

            // What is printed on a toy is cut out of its glyph. The domino stands 2 units tall in 88% of the
            // picture; the middle pip of its upper half is half a unit above its middle.
            byte[] domino = ToySilhouette.Mask(ToyId.Domino, size);
            float perUnit = size * 0.88f / 2f;
            int pip = size / 2 + Mathf.RoundToInt(0.5f * perUnit);
            Assert.Less(domino[pip * size + size / 2], 64, "a pip is a hole in the glyph");
            Assert.Greater(domino[(pip + 5) * size + size / 2], 192, "the body around it is not");
            // A marble is a disc: a quarter of pi of the square it fills. The swirl inside does not show.
            byte[] ball = ToySilhouette.Mask(ToyId.Marble, size);
            int disc = 0;
            foreach (byte value in ball) disc += value;
            Assert.AreEqual(Mathf.PI * 0.25f * 0.88f * 0.88f, disc / 255f / (size * size), 0.02f);
        }
    }
}

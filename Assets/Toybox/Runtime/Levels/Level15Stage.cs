using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Levels
{
    // Level 15, the place: the quilt's edge, the trench of floorboards, the machine's fixed parts, the
    // paint that says where each toy goes and where to stand, and the odds and ends around them.
    //
    // What stops the player and a held toy is a collider added here (Solid); what is seen is collected
    // per material in one Level15Set and merged when the stage is finished, so the whole place is a
    // handful of draws.
    public sealed partial class Level15RubeGoldberg
    {
        /// <summary>The chute's floor lies this far under the line the ball's lowest point follows, so no ball sinks into it.</summary>
        const float ChuteDrop = 0.342f;
        // (3.3 high it stood in the way of a ball held a little to the right of the hopper: 2.45 instead of 3.)
        const float GateHeight = 2.5f, GateWidth = 3.1f;
        static float ChuteSlope => (HopperFoot.y - ChuteLip.y) / (ChuteLip.x - HopperFoot.x);
        static float ChuteFloorAt(float x) => HopperFoot.y - ChuteDrop - ChuteSlope * (x - HopperFoot.x);
        static Vector3 GateClosed => new Vector3(GateX, ChuteFloorAt(GateX) + GateHeight * 0.5f, AxisZ);

        /// <summary>The baseball rolls off the shelf at this speed and is over the jar's axis when it has fallen to the neck.</summary>
        public const float BaseballSpeed = 4f;

        /// <summary>The way of the marble from the hoop down the tube to the switch.</summary>
        static readonly Vector3[] TubePath =
        {
            new Vector3(-3.3f, 5.3f, AxisZ), new Vector3(-3.15f, 4.9f, 17.9f), new Vector3(-2.5f, 4.58f, 17.15f), new Vector3(-1.8f, 4.42f, 16.6f),
        };

        /// <summary>The outlines are painted at these sizes: about the middle of what works.</summary>
        public const float DominoOutlineScale = 3f, RulerOutlineScale = 1f, FanOutlineScale = 0.9f, BallRingRadius = 1.4f;
        /// <summary>
        /// The dominoes' outline floats this far above the lane: its middle is then where the crosshair is when
        /// the set is held against the wall and works (the solver's aim). Painted on the lane itself it asked
        /// for an aim that catches the set's foot on the plinth's edge (measured: anything under y 8.0 at the wall).
        /// (The review: at the builder's 1.6 its middle was at 8.85 and the lowest aim that works at 8.3 - the
        /// lower third of the outline was a miss. At 2.2 the middle is at 9.5, the solver aims at 9.4, and only
        /// the bottom quarter misses.)
        /// </summary>
        public const float DominoOutlineLift = 2.2f;

        Level15Set stage, paint;
        Material quilt, boards, skirting, masonry, cover, trim, birch, kraft, metal, body;
        // Lit from inside: the garage, the windows of the control tower, the mouse hole, the lamps. A colour of paint.
        Color glow;

        static GameObject Solid(string name, Vector3 size)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.AddComponent<BoxCollider>().size = size;
            return solid;
        }

        // A level static between two corners: a collider, and its looks in the stage's set.
        void Block(LevelContext ctx, Material material, Vector3 min, Vector3 max, float tone = 1f)
        {
            ctx.AddStatic(Solid("Block", max - min), (min + max) * 0.5f);
            stage.Span(material, min, max, tone);
        }

        // The same, turned.
        void Block(LevelContext ctx, Material material, Vector3 centre, Vector3 size, Quaternion rotation, float tone = 1f)
        {
            ctx.AddStatic(Solid("Block", size), centre, rotation);
            stage.Box(material, centre, size, rotation, tone);
        }

        // A wedge that rises toward +Z before it is turned: its hull as it comes, its looks into the set.
        void Wedge(LevelContext ctx, Material material, float length, float height, float width, Vector3 centre, Quaternion rotation)
        {
            GameObject ramp = BasicToys.Ramp(length, height, width, material);
            ctx.AddStatic(ramp, centre, rotation);
            stage.Absorb(ramp);
        }

        // What stops the player and a held toy where nothing (or something else) is drawn.
        static GameObject Hidden(LevelContext ctx, string name, Vector3 min, Vector3 max) =>
            ctx.AddStatic(Solid(name, max - min), (min + max) * 0.5f);

        // ---- The quilt, the floorboards, the walls ---------------------------------------------------------------

        void BuildRoom(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            stage = new Level15Set();
            paint = new Level15Set();
            quilt = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Quilt, 3f, 0.1f, 0f, 0.07f)));
            boards = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Planks, 2.5f, 60f, 0.08f)).With(r =>
            {
                r.Name = dip.Name + " Floorboards";
                r.Top = dip.Mid;
            }));
            // The skirting board the machine stands against is the floorboards' own material (sides in the room's
            // deep tone): the backdrop the lit paint and the machine's lighter blocks read against.
            skirting = boards;
            // The machine's own blocks: light on every side, so they stand out of the dark at night.
            masonry = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Tiles, 1.75f, 0.12f, 0f, 0.06f)).With(r =>
            {
                r.Name = dip.Name + " Machine";
                r.Top = Palette.Mix(dip.Light, Palette.Paper, 0.4f);
                r.Side = Palette.Mix(dip.Light, Palette.Paper, 0.22f);
                r.Dado = r.Side;
            }));
            cover = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip).With(r =>
            {
                r.Name = dip.Name + " Cover";
                r.Side = dip.Mid;
                r.Dado = dip.Mid;
            }));
            trim = Materials.Room(RoomSurface.Trim, dip);
            birch = Materials.Toy(ToyRecipe.PlainProp, Palette.Birch);
            kraft = Materials.Toy(ToyRecipe.PlainProp, Palette.Kraft);
            metal = GadgetKit.Metal;
            body = GadgetKit.Body;
            glow = Palette.Lin(Palette.WarmWhite) * 0.85f;

            float w = HalfWidth, t = WallThickness;
            // The trench: floorboards from the quilt's edge to the skirting board, and under the ramp.
            Block(ctx, boards, new Vector3(-w, -1f, EdgeZ), new Vector3(w, 0f, BackZ));
            Block(ctx, boards, new Vector3(RampMinX, -1f, RampTopZ), new Vector3(RampMaxX, 0f, EdgeZ));
            // The quilt's edge: the gallery, with a notch for the ramp.
            Block(ctx, quilt, new Vector3(-w, -1f, NearZ), new Vector3(RampMinX, GalleryTop, EdgeZ));
            Block(ctx, quilt, new Vector3(RampMaxX, -1f, NearZ), new Vector3(w, GalleryTop, EdgeZ));
            Block(ctx, quilt, new Vector3(RampMinX, -1f, NearZ), new Vector3(RampMaxX, GalleryTop, RampTopZ));
            Wedge(ctx, quilt, EdgeZ - RampTopZ, GalleryTop, RampMaxX - RampMinX,
                new Vector3((RampMinX + RampMaxX) * 0.5f, GalleryTop * 0.5f, (RampTopZ + EdgeZ) * 0.5f), Quaternion.Euler(0f, 180f, 0f));

            // The skirting board at the back, and the two ends. The east end has the door.
            Block(ctx, skirting, new Vector3(-w - t, -1f, BackZ), new Vector3(w + t, WallTop, BackZ + t));
            Block(ctx, skirting, new Vector3(-w - t, -1f, NearZ - t), new Vector3(-w, WallTop, BackZ));
            float doorMinZ = ExitCentre.z - ExitSize.z * 0.5f, doorMaxZ = ExitCentre.z + ExitSize.z * 0.5f, doorTop = ExitSize.y;
            Block(ctx, skirting, new Vector3(w, -1f, NearZ - t), new Vector3(w + t, WallTop, doorMinZ));
            Block(ctx, skirting, new Vector3(w, -1f, doorMaxZ), new Vector3(w + t, WallTop, BackZ));
            Block(ctx, skirting, new Vector3(w, doorTop, doorMinZ), new Vector3(w + t, WallTop, doorMaxZ));
            // The mouse hole behind the door: a short tunnel, lit from its far end.
            float end = w + 5f;
            Block(ctx, boards, new Vector3(w, -1f, doorMinZ), new Vector3(end, 0f, doorMaxZ));
            Block(ctx, cover, new Vector3(w + t, 0f, doorMinZ - 0.5f), new Vector3(end, doorTop + 0.5f, doorMinZ));
            Block(ctx, cover, new Vector3(w + t, 0f, doorMaxZ), new Vector3(end, doorTop + 0.5f, doorMaxZ + 0.5f));
            Block(ctx, cover, new Vector3(w + t, doorTop, doorMinZ), new Vector3(end, doorTop + 0.5f, doorMaxZ));
            Block(ctx, cover, new Vector3(end - 0.5f, 0f, doorMinZ), new Vector3(end, doorTop, doorMaxZ));
            // The way on: an arch of light at its end.
            float archZ = (doorMinZ + doorMaxZ) * 0.5f;
            stage.Span(glow, new Vector3(end - 0.53f, 0f, archZ - 0.8f), new Vector3(end - 0.5f, 1.5f, archZ + 0.8f));
            stage.Disc(glow, new Vector3(end - 0.515f, 1.5f, archZ), 0.8f, 0.03f, Quaternion.Euler(0f, 0f, 90f), 1f, 28);
            stage.Span(trim, new Vector3(end - 0.56f, 0f, archZ - 0.95f), new Vector3(end - 0.5f, 1.5f, archZ - 0.8f), 0.7f);
            stage.Span(trim, new Vector3(end - 0.56f, 0f, archZ + 0.8f), new Vector3(end - 0.5f, 1.5f, archZ + 0.95f), 0.7f);

            // Toward the room the quilt runs on: what stops the player and a held toy there is not drawn.
            Hidden(ctx, "Open Side", new Vector3(-w - t, -1f, NearZ - t), new Vector3(w + t, WallTop, NearZ));
            SkyCap.Add(ctx, -w - t, w + t, NearZ - t, BackZ + t, WallTop);

            // The quilt beyond the level's edge, and a moulding along the top of the skirting board.
            stage.Span(quilt, new Vector3(-w - t, -1f, NearZ - 16f), new Vector3(w + t, GalleryTop, NearZ));
            stage.Span(trim, new Vector3(-w, WallTop - 0.9f, BackZ - 0.18f), new Vector3(w, WallTop - 0.35f, BackZ), 0.7f);
            // The door's frame.
            stage.Span(trim, new Vector3(w - 0.12f, 0f, doorMinZ - 0.3f), new Vector3(w, doorTop + 0.3f, doorMinZ), 0.85f);
            stage.Span(trim, new Vector3(w - 0.12f, 0f, doorMaxZ), new Vector3(w, doorTop + 0.3f, doorMaxZ + 0.3f), 0.85f);
            stage.Span(trim, new Vector3(w - 0.12f, doorTop, doorMinZ), new Vector3(w, doorTop + 0.3f, doorMaxZ), 0.85f);
        }

        // ---- The slide: tower, hopper, chute, gate ---------------------------------------------------------------------

        void BuildSlide(LevelContext ctx)
        {
            float lipFloor = ChuteFloorAt(ChuteLip.x), brinkFloor = ChuteFloorAt(HopperX);
            // The platform the ball sits on, the block under the chute, and the chute's own slope out to the lip.
            Block(ctx, masonry, new Vector3(TowerMinX, 0f, BandZ), new Vector3(HopperX, TowerTop, BackZ));
            Block(ctx, masonry, new Vector3(HopperX, 0f, BandZ), new Vector3(TowerMaxX, lipFloor, BackZ));
            float run = ChuteLip.x - HopperX, rise = brinkFloor - lipFloor;
            Wedge(ctx, masonry, run, rise, BackZ - BandZ, new Vector3(HopperX + run * 0.5f, lipFloor + rise * 0.5f, AxisZ), Quaternion.Euler(0f, -90f, 0f));

            // Two rails along the chute, from the gate down. (They began at the hopper: measured, a ball held
            // toward the funnel a little low then stopped on the front one at two thirds of its size.)
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            float railFrom = GateX + 0.12f;
            float length = (ChuteLip.x - railFrom) / Mathf.Cos(angle * Mathf.Deg2Rad);
            const float railHeight = 0.9f, railWidth = 0.2f;
            var normal = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad), 0f);
            Vector3 middle = new Vector3((railFrom + ChuteLip.x) * 0.5f, (ChuteFloorAt(railFrom) + lipFloor) * 0.5f, 0f) + normal * (railHeight * 0.5f);
            Quaternion tilt = Quaternion.Euler(0f, 0f, -angle);
            foreach (float z in new[] { BandZ + railWidth * 0.5f, BackZ - railWidth * 0.5f })
                Block(ctx, trim, new Vector3(middle.x, middle.y, z), new Vector3(length, railHeight, railWidth), tilt, 0.82f);

            // The hopper: a collar round the back of the platform, open toward the chute and toward the quilt
            // (the ball comes in from there, and a rim in its way stopped it short).
            const int pieces = 7;
            float radius = (HopperInner + HopperOuter) * 0.5f, arc = 18f;
            float chord = 2f * HopperOuter * Mathf.Tan(arc * 0.5f * Mathf.Deg2Rad);
            for (int i = 0; i < pieces; i++)
            {
                float a = (90f + (i + 0.5f) * arc) * Mathf.Deg2Rad;
                var at = new Vector3(HopperX + Mathf.Cos(a) * radius, (TowerTop + HopperRim) * 0.5f, AxisZ + Mathf.Sin(a) * radius);
                var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Block(ctx, trim, at, new Vector3(HopperOuter - HopperInner, HopperRim - TowerTop, chord), Quaternion.LookRotation(tangent, Vector3.up), 0.82f);
            }

            // A ladder up the tower's front, and the reject door at its foot.
            float ladderX = -19.9f, face = BandZ - 0.07f;
            stage.Rod(birch, new Vector3(ladderX - 0.4f, 0f, face), new Vector3(ladderX - 0.4f, TowerTop - 0.1f, face), 0.06f);
            stage.Rod(birch, new Vector3(ladderX + 0.4f, 0f, face), new Vector3(ladderX + 0.4f, TowerTop - 0.1f, face), 0.06f);
            for (float y = 0.55f; y < TowerTop - 0.2f; y += 0.6f)
                stage.Rod(birch, new Vector3(ladderX - 0.4f, y, face), new Vector3(ladderX + 0.4f, y, face), 0.045f);
            stage.Span(body, new Vector3(-17.2f, 0f, BandZ - 0.03f), new Vector3(-15.8f, 1.3f, BandZ));
            stage.Soft(kraft, new Vector3(-16.5f, 1.02f, BandZ - 0.33f), new Vector3(1.4f, 0.08f, 0.75f), Quaternion.Euler(-28f, 0f, 0f), 0.02f);
        }

        // The gate across the chute: steel bars. It sinks into the tower when the pedal is pressed.
        GameObject BuildGate()
        {
            var panel = new GameObject("Gate") { layer = Layers.Default };
            panel.AddComponent<BoxCollider>().size = new Vector3(0.15f, GateHeight, GateWidth);
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level15/Gate", GateHeight, GateWidth), () =>
            {
                var parts = new List<MeshPart>();
                const int bars = 7;
                Mesh bar = MeshKit.Cylinder(0.06f, GateHeight - 0.1f, 10);
                for (int i = 0; i < bars; i++)
                    parts.Add(new MeshPart(bar, new Vector3(0f, 0f, -GateWidth * 0.5f + GateWidth * (i + 0.5f) / bars)));
                Mesh rail = MeshKit.RoundedBox(new Vector3(0.16f, 0.14f, GateWidth), 0.03f);
                parts.Add(new MeshPart(rail, new Vector3(0f, GateHeight * 0.5f - 0.07f, 0f)));
                parts.Add(new MeshPart(rail, new Vector3(0f, -GateHeight * 0.5f + 0.07f, 0f)));
                Mesh merged = MeshKit.Merge("Level15 Gate", parts);
                MeshKit.Release(bar);
                MeshKit.Release(rail);
                return merged;
            });
            GadgetKit.Visual(panel.transform, "Visual", mesh, GadgetKit.Metal);
            return panel;
        }

        // ---- Drum, wall of books, the marker the ruler goes on ---------------------------------------------------------

        void BuildDrumAndBooks(LevelContext ctx)
        {
            var drum = new GameObject("Drum") { layer = Layers.Default };
            MeshCollider hull = drum.AddComponent<MeshCollider>();
            hull.sharedMesh = MeshKit.Cached(MeshKit.Key("Level15/DrumHull", DrumRadius, DrumTop), () => MeshKit.Cylinder(DrumRadius, DrumTop, 20));
            hull.convex = true;
            ctx.AddStatic(drum, new Vector3(DrumX, DrumTop * 0.5f, AxisZ));

            Hidden(ctx, "Wall of Books", new Vector3(BooksMinX, 0f, BandZ), new Vector3(BooksMaxX, BooksTop, BackZ));
            GameObject marker = ToyFactory.Marker(MarkerRadius, MarkerLength, Palette.Steel);
            ctx.AddStatic(marker, new Vector3(Pivot.x, MarkerRadius, Pivot.z));
            // (Its barrel in the gadgets' own steel: the same colour, one material less.)
            stage.Absorb(marker, material => material != null && material.name.Contains(Palette.ToHex(Palette.Steel)) ? metal : null);

            // The drum: a shell with two hoops and a pale head.
            var drumAt = new Vector3(DrumX, 0f, AxisZ);
            stage.Disc(kraft, drumAt + Vector3.up * (DrumTop * 0.5f - 0.02f), DrumRadius - 0.06f, DrumTop - 0.04f, Quaternion.identity, 1f, 28);
            stage.Disc(body, drumAt + Vector3.up * 0.14f, DrumRadius - 0.01f, 0.16f, Quaternion.identity, 1f, 28);
            stage.Disc(body, drumAt + Vector3.up * (DrumTop - 0.16f), DrumRadius - 0.01f, 0.16f, Quaternion.identity, 1f, 28);
            stage.Disc(trim, drumAt + Vector3.up * (DrumTop - 0.03f), DrumRadius - 0.1f, 0.05f, Quaternion.identity, 0.9f, 28);
            // Two fat books standing side by side, spines toward the trench.
            float midX = (BooksMinX + BooksMaxX) * 0.5f, depth = BackZ - BandZ;
            stage.Book(cover, trim, new Vector3((BooksMinX + midX) * 0.5f, BooksTop * 0.5f, AxisZ), new Vector3(midX - BooksMinX, BooksTop, depth), 0, Level15Set.Side.NegZ, Quaternion.identity, 1f);
            stage.Book(cover, trim, new Vector3((midX + BooksMaxX) * 0.5f, BooksTop * 0.5f - 0.06f, AxisZ + 0.05f), new Vector3(BooksMaxX - midX, BooksTop - 0.12f, depth - 0.1f), 0, Level15Set.Side.NegZ, Quaternion.identity, 0.8f);
        }

        // ---- The control tower: hoop, tube and switch on its west side, the garage in its front, the duct to the lane --

        void BuildControlTower(LevelContext ctx)
        {
            // Four blocks round the garage niche.
            Block(ctx, masonry, new Vector3(ControlMinX, 0f, BandZ), new Vector3(GarageMinX, ControlTop, BackZ));
            Block(ctx, masonry, new Vector3(GarageMaxX, 0f, BandZ), new Vector3(ControlMaxX, ControlTop, BackZ));
            Block(ctx, masonry, new Vector3(GarageMinX, GarageTop, BandZ), new Vector3(GarageMaxX, ControlTop, BackZ));
            Block(ctx, masonry, new Vector3(GarageMinX, 0f, GarageBackZ), new Vector3(GarageMaxX, GarageTop, BackZ));
            // A gabled roof: nothing rests on it.
            float ridge = (ControlMinX + ControlMaxX) * 0.5f, half = (ControlMaxX - ControlMinX) * 0.5f;
            const float roof = 1.7f;
            Wedge(ctx, cover, half, roof, BackZ - BandZ, new Vector3(ControlMinX + half * 0.5f, ControlTop + roof * 0.5f, AxisZ), Quaternion.Euler(0f, 90f, 0f));
            Wedge(ctx, cover, half, roof, BackZ - BandZ, new Vector3(ridge + half * 0.5f, ControlTop + roof * 0.5f, AxisZ), Quaternion.Euler(0f, -90f, 0f));

            // The garage is lit from inside: its walls and ceiling, a frame round its door.
            const float skin = 0.02f;
            stage.Span(glow, new Vector3(GarageMinX, 0f, GarageBackZ - skin), new Vector3(GarageMaxX, GarageTop, GarageBackZ), 0.9f);
            stage.Span(glow, new Vector3(GarageMinX, 0f, BandZ + 0.02f), new Vector3(GarageMinX + skin, GarageTop, GarageBackZ), 0.62f);
            stage.Span(glow, new Vector3(GarageMaxX - skin, 0f, BandZ + 0.02f), new Vector3(GarageMaxX, GarageTop, GarageBackZ), 0.62f);
            stage.Span(glow, new Vector3(GarageMinX, GarageTop - skin, BandZ + 0.02f), new Vector3(GarageMaxX, GarageTop, GarageBackZ), 0.75f);
            stage.Span(trim, new Vector3(GarageMinX - 0.14f, 0f, BandZ - 0.06f), new Vector3(GarageMinX, GarageTop + 0.14f, BandZ), 0.85f);
            stage.Span(trim, new Vector3(GarageMaxX, 0f, BandZ - 0.06f), new Vector3(GarageMaxX + 0.14f, GarageTop + 0.14f, BandZ), 0.85f);
            stage.Span(trim, new Vector3(GarageMinX, GarageTop, BandZ - 0.06f), new Vector3(GarageMaxX, GarageTop + 0.14f, BandZ), 0.85f);
            // Its windows.
            foreach (float y in new[] { 3.3f, 5.1f })
                foreach (float x in new[] { ControlMinX + 0.8f, ridge, ControlMaxX - 0.8f })
                {
                    stage.Span(glow, new Vector3(x - 0.34f, y - 0.45f, BandZ - 0.02f), new Vector3(x + 0.34f, y + 0.45f, BandZ), 0.8f);
                    stage.Span(trim, new Vector3(x - 0.42f, y - 0.53f, BandZ - 0.012f), new Vector3(x + 0.42f, y + 0.53f, BandZ - 0.004f), 0.6f);
                }

            // The hoop on its arm: eight short bars to whatever is held against it.
            const int sides = 8;
            float side = 2f * HoopRadius * Mathf.Tan(Mathf.PI / sides);
            for (int i = 0; i < sides; i++)
            {
                float a = Mathf.PI * 2f * (i + 0.5f) / sides;
                var at = new Vector3(HoopCentre.x + Mathf.Cos(a) * HoopRadius, HoopCentre.y, HoopCentre.z + Mathf.Sin(a) * HoopRadius);
                ctx.AddStatic(Solid("Hoop", new Vector3(0.16f, 0.16f, side)), at, Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)), Vector3.up));
            }
            Hidden(ctx, "Hoop Arm", new Vector3(HoopCentre.x + HoopRadius, HoopCentre.y - 0.1f, AxisZ - 0.1f), new Vector3(ControlMinX, HoopCentre.y + 0.1f, AxisZ + 0.1f));
            stage.Ring(metal, HoopCentre, HoopRadius - 0.08f, HoopRadius + 0.08f, 0.14f, Quaternion.identity);
            stage.Rod(metal, new Vector3(HoopCentre.x + HoopRadius, HoopCentre.y, AxisZ), new Vector3(ControlMinX, HoopCentre.y, AxisZ), 0.07f);
            // The switch box at the foot of the tube, on the tower's corner.
            stage.Soft(body, SwitchPoint + new Vector3(0.1f, -0.05f, 0.3f), new Vector3(0.5f, 0.7f, 0.7f), Quaternion.identity, 0.06f);

            // The duct: out of the tower's side, up, and along to the nozzle over the lane's first domino.
            const float ductRadius = 0.4f;
            float riser = ControlMaxX + 0.5f;
            Hidden(ctx, "Duct Riser", new Vector3(ControlMaxX, 0.6f, AxisZ - ductRadius), new Vector3(riser + ductRadius, NozzleTip.y + ductRadius, AxisZ + ductRadius));
            Hidden(ctx, "Duct", new Vector3(riser, NozzleTip.y - ductRadius, AxisZ - ductRadius), new Vector3(NozzleTip.x - 0.1f, NozzleTip.y + ductRadius, AxisZ + ductRadius));
            // A bendy straw with ribs at its elbow, and a nozzle.
            var path = new List<Vector3>();
            path.Add(new Vector3(ControlMaxX - 0.1f, 1f, AxisZ));
            path.Add(new Vector3(riser - 0.2f, 1f, AxisZ));
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.PI * 0.5f * i / 6f;
                path.Add(new Vector3(riser - 0.2f + 0.2f * Mathf.Sin(a), 1.2f - 0.2f * Mathf.Cos(a), AxisZ));
            }
            const float bend = 0.7f;
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.PI * 0.5f * i / 8f;
                path.Add(new Vector3(riser + bend - bend * Mathf.Cos(a), NozzleTip.y - bend + bend * Mathf.Sin(a), AxisZ));
            }
            path.Add(new Vector3(NozzleTip.x - 0.6f, NozzleTip.y, AxisZ));
            Mesh straw = ToyGeo.Tube(path, ductRadius * 0.9f, ductRadius * 0.9f, 14, Vector3.forward);
            ctx.OnDispose(() => MeshKit.Release(straw));
            stage.Shape(trim, straw, Vector3.zero, Quaternion.identity, 0.72f);
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.PI * 0.5f * (i + 0.5f) / 5f;
                var at = new Vector3(riser + bend - bend * Mathf.Cos(a), NozzleTip.y - bend + bend * Mathf.Sin(a), AxisZ);
                var along = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                stage.Disc(trim, at, ductRadius, 0.09f, Quaternion.FromToRotation(Vector3.up, along), 0.55f, 16);
            }
            Mesh nozzle = MeshKit.Cached(MeshKit.Key("Level15/Nozzle", ductRadius), () => MeshKit.Lathe(new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(ductRadius, 0f), new Vector2(ductRadius, 0.15f), new Vector2(ductRadius * 0.62f, 0.6f), new Vector2(0f, 0.6f),
            }, 16));
            stage.Shape(body, nozzle, new Vector3(NozzleTip.x - 0.6f, NozzleTip.y, AxisZ), Quaternion.FromToRotation(Vector3.up, Vector3.right));

            // The net under the hoop and the tube down to the switch: glass, so the marble's light shows on its way.
            var glass = new GameObject("Tube");
            Mesh net = MeshKit.Cached(MeshKit.Key("Level15/Net", HoopRadius), () => MeshKit.Lathe(new List<Vector2>
            {
                new Vector2(0.2f, -0.72f), new Vector2(0.26f, -0.6f), new Vector2(HoopRadius - 0.1f, -0.02f),
            }, 24));
            var tubePoints = new List<Vector3> { new Vector3(HoopCentre.x, HoopCentre.y - 0.66f, HoopCentre.z) };
            tubePoints.AddRange(TubePath);
            Mesh tube = ToyGeo.Tube(tubePoints, 0.2f, 0.2f, 10, Vector3.right);
            ctx.OnDispose(() => MeshKit.Release(tube));
            Material[] glassSet = Materials.ToySet(ToyRecipe.Glass, Palette.Steel);
            MeshRenderer netRenderer = GadgetKit.Visual(glass.transform, "Net", net, glassSet[glassSet.Length - 1], HoopCentre);
            netRenderer.sharedMaterials = glassSet;
            netRenderer.shadowCastingMode = ShadowCastingMode.Off;
            MeshRenderer tubeRenderer = GadgetKit.Visual(glass.transform, "Tube", tube, glassSet[glassSet.Length - 1]);
            tubeRenderer.sharedMaterials = glassSet;
            tubeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ctx.AddStatic(glass, Vector3.zero);

            // The switch's lamp: amber while it waits for the marble, green once the marble has closed it.
            var holder = new GameObject("Switch");
            ctx.AddStatic(holder, SwitchPoint);
            Mesh bulb = MeshKit.Cached(MeshKit.Key("Level15/SwitchLamp"), () => MeshKit.Sphere(0.24f, 14, 10));
            switchLamp = GadgetKit.Lamp(holder.transform, bulb, Palette.Amber, Vector3.zero);
        }

        // ---- Plinth, lane, end stop, shelf --------------------------------------------------------------------------------

        void BuildPlinth(LevelContext ctx)
        {
            Block(ctx, masonry, new Vector3(PlinthMinX, 0f, BandZ), new Vector3(PlinthMaxX, LaneTop, BackZ));
            Block(ctx, trim, new Vector3(LaneEndX, LaneTop, BandZ), new Vector3(LaneEndX + 0.4f, LaneTop + 0.6f, BackZ), 0.82f);
            // The shelf plank with the baseball on it.
            float shelfMinZ = AxisZ - 1f, shelfMaxZ = AxisZ + 1f;
            GameObject plank = ToyFactory.Plank(new Vector3(ShelfMaxX - ShelfMinX, 0.2f, shelfMaxZ - shelfMinZ), null, false);
            ctx.AddStatic(plank, new Vector3((ShelfMinX + ShelfMaxX) * 0.5f, ShelfTop - 0.1f, AxisZ));
            stage.Absorb(plank);

            // Two brackets from the skirting board, and the ring of putty the baseball sits in.
            foreach (float x in new[] { ShelfMinX + 0.5f, ShelfMaxX - 0.5f })
            {
                stage.Span(metal, new Vector3(x - 0.08f, ShelfTop - 0.36f, AxisZ - 0.6f), new Vector3(x + 0.08f, ShelfTop - 0.2f, BackZ));
                stage.Span(metal, new Vector3(x - 0.08f, ShelfTop - 1.6f, BackZ - 0.16f), new Vector3(x + 0.08f, ShelfTop - 0.2f, BackZ));
                stage.Rod(metal, new Vector3(x, ShelfTop - 1.5f, BackZ - 0.1f), new Vector3(x, ShelfTop - 0.3f, AxisZ - 0.4f), 0.05f);
            }
            stage.Ring(kraft, new Vector3(BaseballCentre.x, ShelfTop + 0.05f, AxisZ), 0.3f, 0.52f, 0.1f, Quaternion.identity);
        }

        // ---- The bell jar over the alarm clock ------------------------------------------------------------------------------

        void BuildJar(LevelContext ctx)
        {
            // What it is to the simulation: a funnel into a closed chamber. Its floor is a hair under the floorboards.
            FunnelResult funnel = Funnel.Build(ctx, new FunnelOptions
            {
                Name = "bell jar", Axis = new Vector2(JarX, AxisZ), RimY = JarMouthY, MouthRadius = JarMouthRadius, ThroatRadius = JarThroatRadius,
                ConeDepth = JarMouthY - JarTop, TubeLength = 0f, ChamberHeight = JarTop + 0.05f, ChamberRadius = JarRadius, Friction = 0.3f,
            });
            MeshRenderer shell = funnel.GameObject.GetComponent<MeshRenderer>();
            if (shell != null) Sim.Destroy(shell);

            // What it looks like: glass, seen from outside.
            var jar = new GameObject("Bell Jar");
            Mesh glass = MeshKit.Cached(MeshKit.Key("Level15/Jar", JarRadius, JarTop, JarMouthY), () => MeshKit.Lathe(new List<Vector2>
            {
                new Vector2(JarRadius, 0f), new Vector2(JarRadius, JarTop - 0.5f), new Vector2(JarRadius - 0.12f, JarTop - 0.16f), new Vector2(JarRadius - 0.45f, JarTop),
                new Vector2(JarThroatRadius + 0.03f, JarTop), new Vector2(JarMouthRadius, JarMouthY), new Vector2(JarMouthRadius + 0.08f, JarMouthY),
                new Vector2(JarThroatRadius + 0.12f, JarTop + 0.02f),
            }, 32));
            Material[] glassSet = Materials.ToySet(ToyRecipe.Glass, Palette.Steel);
            MeshRenderer renderer = GadgetKit.Visual(jar.transform, "Glass", glass, glassSet[glassSet.Length - 1]);
            renderer.sharedMaterials = glassSet;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ctx.AddStatic(jar, new Vector3(JarX, 0f, AxisZ));

            stage.Ring(kraft, new Vector3(JarX, 0.07f, AxisZ), JarRadius + 0.02f, JarRadius + 0.28f, 0.14f, Quaternion.identity);
        }

        // The alarm clock, built round the middle of its box: one collider, and looks that are squashed with it.
        GameObject BuildClock()
        {
            var clock = new GameObject("Alarm Clock") { layer = Layers.Default };
            clock.AddComponent<BoxCollider>().size = ClockSize;
            Transform root = clock.transform;
            float foot = -ClockSize.y * 0.5f;
            // (The review, second pass: the case was 0.95 round and its bells ended 0.56 under the top of the box,
            // which only the thin bell button reached - so the baseball came to rest a quarter of a unit above
            // the flattened clock, on nothing one could see. The case fills its box now: the bells end just
            // under the button's top, and the baseball lies on them. It is also a bigger thing to see from
            // the pedal, 24 units away. Looks only: the box the baseball strikes is the same.)
            const float round = 1.2f, depth = 0.8f, bellRadius = 0.42f;
            var bodyAt = new Vector3(0f, foot + 0.15f + round, 0f);
            Quaternion facing = Quaternion.Euler(90f, 0f, 0f);
            Mesh looks = MeshKit.Cached(MeshKit.Key("Level15/ClockMetal", round, depth, bellRadius), () =>
            {
                Mesh shell = MeshKit.Cylinder(round, depth, 28, 0.08f);
                Mesh bell = MeshKit.Sphere(bellRadius, 16, 10);
                var parts = new List<MeshPart> { new MeshPart(shell, Matrix4x4.TRS(bodyAt, facing, Vector3.one)) };
                foreach (float side in new[] { -1f, 1f }) parts.Add(new MeshPart(bell, bodyAt + new Vector3(side * 0.72f, round * 0.82f, 0f)));
                Mesh merged = MeshKit.Merge("Level15 Clock Metal", parts);
                MeshKit.Release(shell);
                MeshKit.Release(bell);
                return merged;
            });
            Mesh dark = MeshKit.Cached(MeshKit.Key("Level15/ClockInk", round, depth), () =>
            {
                Mesh hand = MeshKit.Box(new Vector3(0.07f, 0.5f, 0.03f));
                Mesh leg = MeshKit.Cylinder(0.09f, 0.5f, 8);
                Mesh button = MeshKit.Cylinder(0.16f, 0.5f, 12, 0.04f);
                var parts = new List<MeshPart>
                {
                    new MeshPart(hand, Matrix4x4.TRS(bodyAt + new Vector3(-0.13f, 0.13f, -depth * 0.5f - 0.03f), Quaternion.Euler(0f, 0f, 45f), Vector3.one)),
                    new MeshPart(hand, Matrix4x4.TRS(bodyAt + new Vector3(0.17f, 0.1f, -depth * 0.5f - 0.03f), Quaternion.Euler(0f, 0f, -60f), Vector3.one)),
                    // The bell button: its top is the top of the box.
                    new MeshPart(button, new Vector3(0f, ClockSize.y * 0.5f - 0.25f, 0f)),
                };
                foreach (float side in new[] { -1f, 1f })
                    parts.Add(new MeshPart(leg, Matrix4x4.TRS(new Vector3(side * 0.7f, foot + 0.2f, 0f), Quaternion.Euler(0f, 0f, side * 24f), Vector3.one)));
                Mesh merged = MeshKit.Merge("Level15 Clock Ink", parts);
                MeshKit.Release(hand);
                MeshKit.Release(leg);
                MeshKit.Release(button);
                return merged;
            });
            Mesh face = MeshKit.Cached(MeshKit.Key("Level15/ClockFace", round), () => MeshKit.Cylinder(round - 0.14f, 0.05f, 28));
            GadgetKit.Visual(root, "Metal", looks, metal);
            GadgetKit.Visual(root, "Ink", dark, body);
            GadgetKit.Visual(root, "Face", face, trim, bodyAt + Vector3.back * (depth * 0.5f), facing);
            return clock;
        }

        // ---- What the toys start on ------------------------------------------------------------------------------------------

        void BuildPedestals(LevelContext ctx)
        {
            var bookSize = new Vector3(2.2f, PedestalHeight, 0.6f);
            Vector3 bookAt = BookPedestal + Vector3.up * (PedestalHeight * 0.5f);
            ctx.AddStatic(Solid("Book Pedestal", bookSize), bookAt);
            // A book standing on its fore-edge: the dominoes stand on its spine.
            stage.Book(cover, trim, bookAt, bookSize, 2, Level15Set.Side.NegZ, Quaternion.identity, 1f);
            GameObject spool = ToyFactory.ThreadSpool(0.4f, PedestalHeight, grabbable: false);
            ctx.AddStatic(spool, SpoolPedestal + Vector3.up * (PedestalHeight * 0.5f));
            stage.Absorb(spool);
        }

        // ---- Paint: where each toy goes (dashes in the toy's colour) and where to stand (shoe prints) ----------------

        void BuildPaint(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            Vector3 x = Vector3.right, y = Vector3.up, z = Vector3.forward;
            // At night paint that only took the room's light would be black on the skirting board: it is unlit,
            // the toy's own colour for the dashes and one dim wash under all four.
            Color wash = Palette.Lin(Palette.Mix(dip.Deep, dip.Light, 0.55f));

            // The dominoes, on the skirting board above the lane: the seven of them at the size that works.
            Color dominoLine = Line(ToyCatalog.Get(ToyId.DominoSet).ColorIn(dip));
            Pose seat = DominoSeat(DominoOutlineScale);
            float s = DominoOutlineScale;
            for (int i = 0; i < ToyFactory.DominoSetCount; i++)
            {
                float h = ToyFactory.DominoSetHeight(i) * s, thick = 0.15f * h;
                Vector3 centre = seat.position + ToyFactory.DominoSetPieceCenter(i) * s + Vector3.up * DominoOutlineLift;
                centre.z = BackZ;
                paint.Box(wash, centre + Vector3.back * 0.012f, new Vector3(thick, h, 0.016f));
                if (thick > 0.3f) paint.DashedRect(dominoLine, centre, x, y, thick, h, 0.09f, 0.45f, 0.022f);
                else paint.Box(dominoLine, centre + Vector3.back * 0.02f, new Vector3(Mathf.Max(0.06f, thick * 0.5f), h, 0.016f));
            }
            float stripY = LaneTop + DominoOutlineLift + 0.08f, stripFrom = seat.position.x - 1.6f * s;
            paint.Dashes(dominoLine, new Vector3(stripFrom, stripY, BackZ), new Vector3(LaneEndX, stripY, BackZ), Vector3.back, 0.1f, 0.6f, 0.6f, 0.022f);
            // Chevrons from the outline down onto the lane: that is where the set ends up.
            for (int i = 0; i < 4; i++)
                for (int row = 0; row < 3; row++)
                {
                    float cx = Mathf.Lerp(stripFrom + 1.2f, LaneEndX - 1.2f, i / 3f), top = LaneTop + DominoOutlineLift - 0.3f - row * 0.55f;
                    paint.Stroke(dominoLine, new Vector3(cx - 0.4f, top, BackZ), new Vector3(cx, top - 0.32f, BackZ), Vector3.back, 0.11f, 0.022f);
                    paint.Stroke(dominoLine, new Vector3(cx + 0.4f, top, BackZ), new Vector3(cx, top - 0.32f, BackZ), Vector3.back, 0.11f, 0.022f);
                }

            // The ball, on the skirting board above the hopper: a ball that covers the ring is heavy enough.
            Color ballLine = Line(ToyCatalog.Get(ToyId.BouncyBall).ColorIn(dip));
            paint.Disc(wash, BallAim + Vector3.back * 0.012f, BallRingRadius, 0.016f, Quaternion.Euler(90f, 0f, 0f));
            paint.DashedCircle(ballLine, BallAim, x, y, BallRingRadius, 0.14f, 16, 0.62f, 0.022f);
            // Three chevrons from the ring down into the hopper: that is where it ends up.
            for (int i = 0; i < 3; i++)
            {
                float top = BallAim.y - BallRingRadius - 0.35f - i * 0.6f;
                paint.Stroke(ballLine, new Vector3(HopperX - 0.45f, top, BackZ), new Vector3(HopperX, top - 0.35f, BackZ), Vector3.back, 0.12f, 0.022f);
                paint.Stroke(ballLine, new Vector3(HopperX + 0.45f, top, BackZ), new Vector3(HopperX, top - 0.35f, BackZ), Vector3.back, 0.12f, 0.022f);
            }

            // The ruler, on the floorboards under the marker: its footprint, with the cap's round at the hoop's end.
            Color rulerLine = Line(ToyCatalog.Get(ToyId.CatapultRuler).ColorIn(dip));
            float longArm = LongArmPerScale * RulerOutlineScale, shortArm = ShortArmPerScale * RulerOutlineScale;
            var rulerAt = new Vector3(Pivot.x + (longArm - shortArm) * 0.5f, 0f, AxisZ);
            float rulerWidth = ToyFactory.CatapultRulerPlank.x * RulerOutlineScale;
            paint.Box(wash, rulerAt + Vector3.up * 0.012f, new Vector3(longArm + shortArm, 0.016f, rulerWidth));
            paint.DashedRect(rulerLine, rulerAt, x, z, longArm + shortArm, rulerWidth, 0.09f, 0.45f, 0.022f);
            var capAt = new Vector3(Pivot.x + longArm - 0.35f * RulerOutlineScale, 0f, AxisZ);
            paint.Disc(rulerLine, capAt + Vector3.up * 0.03f, 0.3f * RulerOutlineScale, 0.016f, Quaternion.identity);

            // The fan, on the back of its garage.
            Color fanLine = Line(ToyCatalog.Get(ToyId.DeskFan).ColorIn(dip));
            float f = FanOutlineScale;
            var hub = new Vector3(FanSeat.x, ToyFactory.DeskFanHub.y * f, GarageBackZ - 0.02f);
            paint.DashedCircle(fanLine, hub, x, y, ToyFactory.DeskFanGuardRadius * f, 0.07f, 12, 0.6f, 0.012f);
            paint.Box(fanLine, new Vector3(FanSeat.x, 0.05f * f, GarageBackZ - 0.04f), new Vector3(0.7f * f, 0.1f * f, 0.016f));

            // The wind: stripes on the lane under the nozzle, and along the plinth's front edge where they show from the quilt.
            Color stripe = Palette.Lin(Palette.Mix(dip.Light, Palette.Paper, 0.6f));
            for (float sx = LaneMinX + 0.25f; sx < WindMaxX - 0.5f; sx += 0.75f)
            {
                paint.Stroke(stripe, new Vector3(sx, LaneTop, AxisZ - 1f), new Vector3(sx + 0.5f, LaneTop, AxisZ + 1f), Vector3.up, 0.2f, 0.01f);
                paint.Stroke(stripe, new Vector3(sx, LaneTop - 0.45f, BandZ), new Vector3(sx + 0.3f, LaneTop - 0.05f, BandZ), Vector3.back, 0.16f, 0.01f);
            }

            // Where to stand: a pad with a pair of shoe prints, in the room's own tones.
            Color pad = Palette.Lin(dip.Deep) * 0.8f;
            Color print = Palette.Lin(Palette.Mix(dip.Light, Palette.Paper, 0.75f));
            foreach (Vector3 at in new[] { SpawnPoint, DominoStand, BallPickup, BallStand, RulerStand, FanStand })
                StandMark(pad, print, at, 0f);
        }

        static Color Line(Color hero) => Palette.Lin(hero) * 0.9f;

        const float PadRadius = 0.62f;

        // Looks only: a round pad and a pair of shoe prints on it, toes a little apart.
        void StandMark(Color pad, Color print, Vector3 at, float yaw)
        {
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            paint.Disc(pad, at + Vector3.up * 0.012f, PadRadius, 0.004f, turn, 1f, 40);
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level15/Sole"), () =>
            {
                // A sole in the XY plane, toe up: half a circle for the toe, a smaller half for the heel.
                var outline = new List<Vector2>();
                for (int i = 0; i <= 12; i++)
                {
                    float a = Mathf.PI * i / 12f;
                    outline.Add(new Vector2(Mathf.Cos(a) * 0.16f, 0.17f + Mathf.Sin(a) * 0.16f));
                }
                for (int i = 0; i <= 12; i++)
                {
                    float a = Mathf.PI + Mathf.PI * i / 12f;
                    outline.Add(new Vector2(Mathf.Cos(a) * 0.115f, -0.2f + Mathf.Sin(a) * 0.115f));
                }
                return MeshKit.Extrude(outline, 0.004f);
            });
            for (int side = -1; side <= 1; side += 2)
            {
                Quaternion flat = turn * Quaternion.Euler(0f, side * 9f, 0f) * Quaternion.Euler(90f, 0f, 0f);
                paint.Shape(print, sole, at + Vector3.up * 0.02f + turn * new Vector3(side * 0.24f, 0f, 0f), flat);
            }
        }

        // ---- Nobody's toys: a little clutter well away from every line of throw; then everything is merged ----------

        void BuildDressing(LevelContext ctx)
        {
            // On the quilt, in the corners behind the start.
            Clutter(ctx, new Vector3(1.8f, 1.8f, 1.8f), new Vector3(25.2f, GalleryTop, -3.6f), 12f);
            Clutter(ctx, new Vector3(1.1f, 1.1f, 1.1f), new Vector3(25f, GalleryTop + 1.8f, -3.5f), -20f);
            Clutter(ctx, new Vector3(2.2f, 1.1f, 1.1f), new Vector3(22.6f, GalleryTop, -4.2f), -8f);
            Clutter(ctx, new Vector3(1.5f, 1.5f, 1.5f), new Vector3(-25.3f, GalleryTop, -3.8f), -14f);
            Clutter(ctx, new Vector3(1f, 1f, 2f), new Vector3(-23.2f, GalleryTop, -4.4f), 64f);
            // In the trench, under the shelf and beside the slide tower.
            Clutter(ctx, new Vector3(1.4f, 1.4f, 1.4f), new Vector3(22.4f, 0f, 19f), 9f);
            Clutter(ctx, new Vector3(0.9f, 0.9f, 0.9f), new Vector3(23.8f, 0f, 18.7f), -31f);
            Clutter(ctx, new Vector3(1.6f, 1.6f, 1.6f), new Vector3(-26.4f, 0f, 18.9f), 7f);
            Clutter(ctx, new Vector3(1f, 1f, 1f), new Vector3(-26.3f, 1.6f, 18.8f), 33f);

            BuildLamps(ctx);
            stage.Finish(ctx, "Stage");
            paint.Finish(ctx, "Paint", castShadows: false);
            stage = null;
            paint = null;
        }

        // Night-lights high on the skirting board, one over each part of the machine, out of everybody's reach
        // and above every line of throw. None is a light (the room has one, the moon): each has a pool of its
        // own warm colour painted on the board behind the machine and on the floorboards in front of it, so
        // the machine stands against light instead of in the dark. Looks only.
        void BuildLamps(LevelContext ctx)
        {
            Color warm = Palette.Lin(Palette.Mix(Palette.WarmWhite, Palette.Tangerine, 0.18f));
            var glows = new List<MeshPart>();
            const float lampY = 13.4f;
            foreach (float x in new[] { HopperX, -5f, 6f, 15f, 22f })
            {
                var onWall = new Vector3(x, lampY, BackZ);
                paint.Box(Palette.Lin(ctx.Dip.Deep) * 0.6f, onWall + new Vector3(0f, -0.3f, -0.08f), new Vector3(0.18f, 0.5f, 0.16f));
                paint.Soft(warm * 1.35f, onWall + new Vector3(0f, 0.08f, -0.24f), new Vector3(0.42f, 0.56f, 0.42f), Quaternion.identity, 0.08f);
                // (Each pool ends inside the surface it lies on: none hangs over an edge.)
                Pool(glows, new Vector3(x, 10f, BackZ), Vector3.back, 5.8f);
                Pool(glows, new Vector3(x, 0f, 14.6f), Vector3.up, 4.6f);
            }
            // And on the quilt: the lamp the level is named for is behind the player; its light lies on the start and on the pedal.
            Pool(glows, new Vector3(SpawnPoint.x, GalleryTop, 4.8f), Vector3.up, 4.9f);
            Pool(glows, PedalPoint, Vector3.up, 1.9f);

            Mesh merged = MeshKit.Merge("Level15 Glows", glows);
            var glow = new GameObject("Glows");
            glow.AddComponent<MeshFilter>().sharedMesh = merged;
            MeshRenderer renderer = glow.AddComponent<MeshRenderer>();
            Color pool = warm * 0.16f;
            pool.a = 1f;
            renderer.sharedMaterial = Materials.Flat(new FlatRecipe { Name = "Level15 Glow", Color = pool, Shape = FlatShape.SoftDisc, Soft = 1f, Blend = FlatBlend.Additive });
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ctx.OnDispose(() => MeshKit.Release(merged));
            ctx.AddStatic(glow, Vector3.zero);
        }

        // A soft disc of light lying on a surface, just proud of whatever is painted there.
        static void Pool(List<MeshPart> glows, Vector3 onSurface, Vector3 normal, float radius)
        {
            Mesh quad = MeshKit.Cached(MeshKit.Key("Level15/Glow"), () =>
            {
                var mesh = new Mesh { name = "Level15 Glow Quad", hideFlags = HideFlags.DontSave };
                mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
                mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
                mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                mesh.RecalculateBounds();
                return mesh;
            });
            Vector3 up = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
            glows.Add(new MeshPart(quad, onSurface + normal * 0.06f, Quaternion.LookRotation(-normal, up), Vector3.one * (2f * radius)));
        }

        void Clutter(LevelContext ctx, Vector3 size, Vector3 foot, float yaw)
        {
            GameObject piece = ToyFactory.WoodenBlock(size, grabbable: false);
            ctx.AddStatic(piece, foot + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, yaw, 0f));
            stage.Absorb(piece);
        }
    }
}

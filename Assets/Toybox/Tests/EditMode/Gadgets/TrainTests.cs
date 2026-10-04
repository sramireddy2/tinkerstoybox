using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The set of Level 9: station, lighthouse, doorstep ring and the train on its circle.</summary>
    public abstract class TrainSetTest : SimTest
    {
        protected Train Train;
        protected PropCarrier Carrier;
        protected Prop Plank;

        protected void BuildSet(LevelContext ctx, float startBearing = -150f)
        {
            TestHelpers.Box(ctx, new Vector3(0f, -7f, -3.35f), new Vector3(18f, 14f, 13.3f));            // station: x -9..9, z -10..3.3, top 0
            ctx.AddStatic(BasicToys.Cylinder(2f, 30f), new Vector3(0f, 1f, 20f));                        // lighthouse, radius 2
            ctx.AddStatic(BasicToys.Cylinder(3.5f, 0.5f), new Vector3(0f, -1.25f, 20f));                 // doorstep ring, top -1
            ctx.AddStatic(BasicToys.Cylinder(0.4f, 1.1f), new Vector3(2.5f, 0.55f, -1f));                // spool
            Train = new Train(ctx, new TrainOptions { Center = new Vector2(0f, 20f), Radius = 14f, DeckY = -1f, AngularSpeed = 24f, StartBearing = startBearing });
            Carrier = new PropCarrier(ctx, new PropCarrierOptions
            {
                Beds = Train, AcceptTag = "plank", FlatDot = 0.94f, MinRadius = 1.95f, MaxRadius = 16.5f, RadiusCenter = new Vector2(0f, 20f),
            });
            Plank = ToyCatalog.Add(ctx, ToyId.Plank, new Vector3(2.5f, 1.14f, -1f), 0.65f, null, o => o.Tags = new[] { "plank" });
            ctx.SetSpawn(new Vector3(0f, 0f, -7f), 0f);
        }

        protected float RadiusOf(Vector3 point) => new Vector2(point.x, point.z - 20f).magnitude;
    }

    public class TrainTests : TrainSetTest
    {
        [Test]
        public void TheTrain_RunsItsCircle_AsAFunctionOfTime()
        {
            var laps = new List<int>();
            Build(ctx =>
            {
                BuildSet(ctx);
                Train.TrainAtStation += laps.Add;
            }, -12f);
            Assert.AreEqual(8, Train.Cars);
            Assert.AreEqual(15f, Train.LapSeconds, 1e-3f);
            Assert.AreEqual(5.86f, Train.DeckSpeed, 0.01f, "5.9 units a second at the deck");
            Assert.AreEqual(-150f, Train.EngineBearing, 1e-3f);
            Assert.AreEqual(-150f - 15f - 14f * 3f, Train.CarBearing(4), 1e-3f, "wagon i is 15 + 14 (i - 1) degrees behind the engine");

            RunSeconds(3.75f);
            Assert.AreEqual(-60f, Train.EngineBearing, 0.01f, "a quarter of a lap in 3.75 s");
            Train.PoseAt(-60f, out Vector3 expected, out Quaternion heading);
            Assert.Less(Vector3.Distance(expected, Train.Engine.Position), 1e-3f, "the engine is where its bearing says");
            Assert.AreEqual(new Vector3(-14f * Mathf.Sin(60f * Mathf.Deg2Rad), -1f, 20f - 7f).ToString("0.00"), Train.Engine.Position.ToString("0.00"));
            Assert.Less(Quaternion.Angle(heading, Train.Engine.Rotation), 0.01f, "and faces along the track");
            Vector3 travel = Train.Engine.Rotation * Vector3.forward;
            Assert.Greater(Vector3.Dot(travel, Train.Engine.Velocity.normalized), 0.999f, "it moves the way it faces");
            Assert.AreEqual(5.86f, Train.Engine.Velocity.magnitude, 0.02f);

            for (int car = 1; car <= 8; car++)
            {
                Assert.AreEqual(14f, RadiusOf(Train.CarMover(car).Position), 1e-3f, "wagon " + car + " is on the circle");
                Assert.AreEqual(-1f, Train.CarMover(car).Position.y, 1e-4f);
                Assert.IsTrue(Train.BedBox(car).Contains(Train.CarMover(car).Position + Vector3.up * 0.1f));
            }
            // Neighbouring decks (3.2 long on a 14 degree pitch) leave a coupling gap a capsule cannot fall through.
            float pitch = Vector3.Distance(Train.CarMover(1).Position, Train.CarMover(2).Position);
            Assert.Less(pitch - ToyFactory.TrainCarLength, 0.3f, "coupling gap " + (pitch - ToyFactory.TrainCarLength));

            Assert.AreEqual(0, laps.Count);
            RunSeconds(2.6f);
            CollectionAssert.AreEqual(new[] { 0 }, laps, "the engine passes the station 6.25 s after the start");
            RunSeconds(15f);
            CollectionAssert.AreEqual(new[] { 0, 1 }, laps, "and once a lap after that");
        }

        [Test]
        public void ThePlayer_RidesAWagon_AllTheWayRound()
        {
            Build(ctx => BuildSet(ctx), -12f);
            Mover car = Train.CarMover(4);
            Game.Player.Teleport(car.Position + Vector3.up * 0.02f, 0f);
            // Put down on a deck that moves at 5.9, the player slips a quarter of a unit before they move with it.
            RunSeconds(1f);
            Vector3 start = Quaternion.Inverse(car.Rotation) * (Game.Player.Position - car.Position);
            Assert.Less(new Vector2(start.x, start.z).magnitude, 0.4f, "the slip while being picked up (" + start + ")");
            float worst = 0f;
            bool always = true;
            for (int i = 0; i < 30 * 60; i++)
            {
                Game.Tick();
                Vector3 offset = Quaternion.Inverse(car.Rotation) * (Game.Player.Position - car.Position) - start;
                offset.y = 0f;
                worst = Mathf.Max(worst, offset.magnitude);
                always &= Game.Player.Grounded;
            }
            Assert.IsTrue(always, "grounded on the wagon for two whole laps");
            // Without GadgetKit.SteadyRider the rider drifts outward by 0.68 a lap and leaves the 3-wide deck in the second.
            Assert.Less(worst, 0.1f, "and carried round without drifting on the deck (worst " + worst + ")");
            Assert.AreEqual(-1f, Game.Player.Position.y, 0.02f);
            Assert.AreEqual(car.Body, Game.Player.GroundCollider.attachedRigidbody);
        }

        [Test]
        public void ALooseCrate_OnAWagon_RidesAlong()
        {
            // The wagons are kinematic bodies: friction carries what lies on them round the bend.
            Prop rider = null;
            Build(ctx =>
            {
                BuildSet(ctx);
                rider = ctx.AddProp(BasicToys.Block(1f), Train.CarMover(2).Position + Vector3.up * 0.6f, new PropOptions { Name = "Rider", Friction = 0.9f });
            }, -12f);
            RunSeconds(5f);
            Vector3 offset = rider.Center - Train.CarMover(2).Position;
            Assert.Less(new Vector2(offset.x, offset.z).magnitude, 1.6f, "a crate on a wagon is carried along by friction (offset " + offset + ")");
            Assert.AreEqual(-0.5f, rider.Center.y, 0.05f);
        }
    }
}

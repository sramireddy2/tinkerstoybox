using System;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Gadgets
{
    /// <summary>Moving beds a <see cref="PropCarrier"/> can grip props on: the train's wagons, or anything else a level moves.</summary>
    public interface ICarrierBeds
    {
        int BedCount { get; }
        /// <summary>The body the bed belongs to.</summary>
        Mover BedMover(int index);
        /// <summary>
        /// Where the bed's top is told to be after this tick's step: the middle of the deck and its
        /// orientation. (A carrier is constructed after what moves its beds, so it reads this tick's pose.)
        /// </summary>
        void BedPose(int index, out Vector3 position, out Quaternion rotation);
        /// <summary>Width (x), gripping height (y) and length (z) of a bed.</summary>
        Vector3 BedSize { get; }
    }

    public sealed class TrainOptions
    {
        public string Name = "Train";
        /// <summary>The middle of the circle: x and z.</summary>
        public Vector2 Center;
        public float Radius = 14f;
        /// <summary>Height of the decks' tops.</summary>
        public float DeckY;
        /// <summary>Degrees per second (24: a lap in 15 s).</summary>
        public float AngularSpeed = 24f;
        /// <summary>The engine's bearing at the start. Bearing 0 is the point nearest to -Z; it grows toward +X.</summary>
        public float StartBearing;
        /// <summary>Degrees of the circle the engine and each wagon take up.</summary>
        public float EngineArc = 16f, CarArc = 14f;
        public int Cars = 8;
        /// <summary>The bearing at which TrainAtStation is raised.</summary>
        public float StationBearing;
        /// <summary>The beds: this much above each wagon's deck counts as lying on it.</summary>
        public float BedHeight = 0.3f;
        /// <summary>A wagon's deck: width and length (the toy catalog's wagon unless a factory says otherwise).</summary>
        public Vector2 DeckSize = new Vector2(ToyFactory.TrainWidth, ToyFactory.TrainCarLength);
        /// <summary>Builders of the engine and of a wagon: origin in the middle of the deck's top, +Z the way it travels.</summary>
        public Func<GameObject> EngineFactory, CarFactory;
    }

    /// <summary>
    /// A kinematic train on a circular track (LEVELS 2.2, Level 9). The bearing is a pure function of the
    /// gadget's age; the engine and every wagon are one Mover each, moved to their place on the circle
    /// every tick with the tangent as their heading. They carry the player and push loose props.
    /// </summary>
    public sealed class Train : Gadget, ICarrierBeds
    {
        readonly TrainOptions options;
        readonly Mover engine;
        readonly Mover[] cars;
        int lap;

        public Train(LevelContext ctx, TrainOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            int count = Mathf.Max(0, options.Cars);
            Func<GameObject> makeEngine = options.EngineFactory ?? (() => ToyFactory.TrainEngine());
            Func<GameObject> makeCar = options.CarFactory ?? (() => ToyFactory.TrainCar());

            PoseAt(BearingAt(0), out Vector3 position, out Quaternion rotation);
            GameObject engineObject = makeEngine();
            engineObject.name = Name + " Engine";
            engine = ctx.AddKinematic(engineObject, position, rotation);
            cars = new Mover[count];
            for (int i = 0; i < count; i++)
            {
                PoseAt(BearingAt(0) - CarOffset(i + 1), out position, out rotation);
                GameObject car = makeCar();
                car.name = Name + " Car " + (i + 1);
                cars[i] = ctx.AddKinematic(car, position, rotation);
            }
            lap = LapOf(BearingAt(0));
        }

        public int Cars => cars.Length;
        public Mover Engine => engine;
        public float Radius => options.Radius;
        public Vector2 Center => options.Center;
        public float DeckY => options.DeckY;
        /// <summary>The bearing at which TrainAtStation is raised: where the station is on the track.</summary>
        public float StationBearing => options.StationBearing;
        /// <summary>Seconds a lap takes.</summary>
        public float LapSeconds => 360f / Mathf.Abs(options.AngularSpeed);
        /// <summary>Speed of the decks along the track.</summary>
        public float DeckSpeed => Mathf.Abs(options.AngularSpeed) * Mathf.Deg2Rad * options.Radius;

        /// <summary>The engine's bearing now, in degrees (it keeps growing lap after lap).</summary>
        public float EngineBearing => BearingAt(Age);

        /// <summary>The bearing of wagon <paramref name="car"/> (1 is the one behind the engine) now.</summary>
        public float CarBearing(int car) => EngineBearing - CarOffset(car);

        /// <summary>The mover of wagon <paramref name="car"/> (1..Cars).</summary>
        public Mover CarMover(int car) => cars[car - 1];

        /// <summary>The box on the deck of wagon <paramref name="car"/> (1..Cars) in which a prop counts as lying on it, where the wagon is now.</summary>
        public Zone BedBox(int car)
        {
            Mover mover = cars[car - 1];
            Vector3 size = BedSize;
            return Zone.Box(mover.Position + Vector3.up * (size.y * 0.5f), size, mover.Rotation);
        }

        /// <summary>Where a bearing is on the track, and the heading of something travelling there.</summary>
        public void PoseAt(float bearing, out Vector3 position, out Quaternion rotation)
        {
            float radians = bearing * Mathf.Deg2Rad;
            position = new Vector3(options.Center.x + options.Radius * Mathf.Sin(radians), options.DeckY, options.Center.y - options.Radius * Mathf.Cos(radians));
            // Travelling toward growing bearings; backward if the speed is negative.
            rotation = Quaternion.Euler(0f, (options.AngularSpeed >= 0f ? 90f : -90f) - bearing, 0f);
        }

        /// <summary>Inside the tick: the engine passed the station; the argument counts the laps.</summary>
        public event Action<int> TrainAtStation;

        int ICarrierBeds.BedCount => cars.Length;
        Mover ICarrierBeds.BedMover(int index) => cars[index];
        public Vector3 BedSize => new Vector3(options.DeckSize.x, options.BedHeight, options.DeckSize.y);

        void ICarrierBeds.BedPose(int index, out Vector3 position, out Quaternion rotation) =>
            PoseAt(BearingAt(Age) - CarOffset(index + 1), out position, out rotation);

        float BearingAt(int age) => options.StartBearing + options.AngularSpeed * (age * Sim.Dt);

        float CarOffset(int car) => (options.EngineArc + options.CarArc) * 0.5f + options.CarArc * (car - 1);

        int LapOf(float bearing) => Mathf.FloorToInt((bearing - options.StationBearing) / 360f);

        protected override void Tick(float dt)
        {
            // Where everything has to be after this step.
            float bearing = BearingAt(Age + 1);
            PoseAt(bearing, out Vector3 enginePosition, out Quaternion rotation);
            engine.MoveTo(enginePosition, rotation);
            for (int i = 0; i < cars.Length; i++)
            {
                PoseAt(bearing - CarOffset(i + 1), out Vector3 position, out rotation);
                cars[i].MoveTo(position, rotation);
                GadgetKit.SteadyRider(Game, cars[i]);
            }
            GadgetKit.SteadyRider(Game, engine);

            int now = LapOf(bearing);
            if (now == lap) return;
            lap = now;
            TrainAtStation?.Invoke(now);
            Game.Events.RaiseTrainAtStation(Event(enginePosition, null, bearing, 0f, now));
        }

        public override void Reset()
        {
            base.Reset();
            PoseAt(BearingAt(0), out Vector3 position, out Quaternion rotation);
            engine.Teleport(position, rotation);
            for (int i = 0; i < cars.Length; i++)
            {
                PoseAt(BearingAt(0) - CarOffset(i + 1), out position, out rotation);
                cars[i].Teleport(position, rotation);
            }
            lap = LapOf(BearingAt(0));
        }
    }
}

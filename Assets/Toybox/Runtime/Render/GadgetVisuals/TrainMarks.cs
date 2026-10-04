using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// What a train runs on (LEVELS 2.2, Level 9). The gadget moves an engine and its wagons round a
    /// circle; this lays the circle down so the eye knows where a wagon will be before it gets there:
    ///
    /// - the track: two rails and their sleepers under the wheels, one merged mesh in gadget Ink;
    /// - the station: the sleepers round the station bearing are capped in Paper, and a bollard beside
    ///   the track carries the one signal - amber and pulsing while the track is empty there, green
    ///   while the engine stands at the station.
    ///
    /// Three draws and a lamp per train; the track casts no shadow of its own.
    /// </summary>
    [Presenter(244, ProvidesLook = true)]
    public sealed class TrainMarks : GadgetVisual
    {
        /// <summary>The wheels of the toy train: how far inside the deck's edge they run, and how far below its top they end.</summary>
        public const float WheelInset = 0.3f, WheelDrop = ToyFactory.TrainDeckThickness + 0.1f + 0.34f;
        public const float RailWidth = 0.14f, RailHeight = 0.1f, SleeperHeight = 0.08f, SleeperWidth = 0.34f;
        /// <summary>The station is this long along the track, in wagon lengths; the lamp stays green this long after the engine left.</summary>
        public const float StationLengths = 1.6f, HoldSeconds = 0.6f;

        sealed class Line
        {
            public Train Train;
            public MeshRenderer Track, Station, Post;
            public SignalLamp Lamp;
            public Vector3 StationPoint;
            public float Reach;
            public float GreenFor;
            public bool AtStation;
        }

        readonly List<Line> lines = new List<Line>();

        public int Count => lines.Count;
        public Renderer TrackOf(int index) => lines[index].Track;
        public Renderer StationOf(int index) => lines[index].Station;
        public Renderer LampOf(int index) => lines[index].Lamp.Renderer;
        /// <summary>True while the i-th station's lamp is green: the engine stands there (or has just left).</summary>
        public bool AtStation(int index) => lines[index].AtStation;
        /// <summary>Where the i-th station is on its track.</summary>
        public Vector3 StationPointOf(int index) => lines[index].StationPoint;

        protected override void Adopt(Gadget gadget)
        {
            if (!(gadget is Train train) || train.Radius <= 0.5f) return;
            Vector3 bed = train.BedSize;
            float gauge = Mathf.Max(0.4f, bed.x * 0.5f - WheelInset);
            float railTop = train.DeckY - WheelDrop;
            var centre = new Vector3(train.Center.x, 0f, train.Center.y);
            float radius = train.Radius;
            int segments = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * radius / 1.3f), 28, 96);
            if (segments % 2 == 1) segments++;
            float step = 360f / segments;

            train.PoseAt(train.StationBearing, out Vector3 station, out _);
            float stationArc = StationLengths * bed.z / radius * Mathf.Rad2Deg * 0.5f;

            var track = new List<MeshPart>();
            var caps = new List<MeshPart>();
            for (int i = 0; i < segments; i++)
            {
                float bearing = i * step;
                float radians = bearing * Mathf.Deg2Rad;
                // The same circle the train uses: bearing 0 is the point nearest to -Z, growing toward +X.
                var radial = new Vector3(Mathf.Sin(radians), 0f, -Mathf.Cos(radians));
                Quaternion heading = Quaternion.LookRotation(Vector3.Cross(Vector3.up, radial), Vector3.up);
                for (int side = -1; side <= 1; side += 2)
                {
                    float r = radius + side * gauge;
                    float chord = 2f * r * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) + 0.02f;
                    track.Add(new MeshPart(MeshKit.Box(new Vector3(RailWidth, RailHeight, chord)), centre + radial * r + Vector3.up * (railTop - RailHeight * 0.5f), heading));
                }
                if (i % 2 == 1) continue;
                var sleeper = new Vector3(SleeperWidth, SleeperHeight, bed.x * 0.9f);
                Quaternion across = Quaternion.LookRotation(radial, Vector3.up);
                Vector3 at = centre + radial * radius + Vector3.up * (railTop - RailHeight - SleeperHeight * 0.5f);
                track.Add(new MeshPart(MeshKit.Box(sleeper), at, across));
                if (Mathf.Abs(Mathf.DeltaAngle(bearing, train.StationBearing)) > stationArc) continue;
                // The station: this sleeper wears a Paper cap between the rails.
                caps.Add(new MeshPart(MeshKit.Box(new Vector3(SleeperWidth * 0.8f, 0.03f, gauge * 2f - RailWidth * 2f)), at + Vector3.up * (SleeperHeight * 0.5f + 0.015f), across));
            }
            if (caps.Count == 0)
            {
                // A track too small to have a sleeper inside the arc still shows where its station is.
                Vector3 outward = (station - new Vector3(centre.x, station.y, centre.z)).normalized;
                caps.Add(new MeshPart(MeshKit.Box(new Vector3(SleeperWidth * 0.8f, 0.03f, gauge * 2f - RailWidth * 2f)),
                    new Vector3(station.x, railTop - RailHeight + 0.015f, station.z), Quaternion.LookRotation(outward, Vector3.up)));
            }

            var line = new Line { Train = train, StationPoint = station, Reach = bed.z * StationLengths * 0.5f };
            Mesh trackMesh = Keep(Merge("Train Track", track));
            Mesh capMesh = Keep(Merge("Train Station", caps));
            line.Track = Visual("Train Track " + train.Name, trackMesh, Materials.Gadget(GadgetPart.Body));
            line.Track.receiveShadows = true;
            line.Station = Visual("Train Station " + train.Name, capMesh, Materials.Toy(ToyRecipe.PlainProp, Palette.Paper));
            line.Station.receiveShadows = true;

            // The bollard: outside the track at the station, its lamp just below the deck.
            Vector3 away = new Vector3(station.x - centre.x, 0f, station.z - centre.z).normalized;
            float postHeight = Mathf.Max(0.3f, WheelDrop - 0.25f);
            Vector3 foot = new Vector3(station.x, railTop - RailHeight - SleeperHeight, station.z) + away * (bed.x * 0.5f + 0.45f);
            line.Post = Visual("Train Station Post " + train.Name, GadgetKit.DiscMesh(0.13f, postHeight, 12), Materials.Gadget(GadgetPart.Body));
            line.Post.transform.position = foot + Vector3.up * (postHeight * 0.5f);
            Mesh bulb = MeshKit.Cached(MeshKit.Key("GadgetLamp", 0.2f), () => MeshKit.Sphere(0.2f, 12, 8));
            MeshRenderer lamp = Visual("Signal", bulb, Materials.Gadget(Palette.Amber), line.Post.transform);
            lamp.transform.localPosition = Vector3.up * (postHeight * 0.5f + 0.12f);
            line.Lamp = new SignalLamp(lamp, Palette.Amber);
            lines.Add(line);
        }

        // MeshKit.Merge, with the merged boxes released.
        static Mesh Merge(string name, List<MeshPart> parts)
        {
            Mesh merged = MeshKit.Merge(name, parts);
            for (int i = 0; i < parts.Count; i++) MeshKit.Release(parts[i].Mesh);
            merged.UploadMeshData(true);
            return merged;
        }

        protected override void Draw(float dt, float alpha)
        {
            float time = Now;
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];
                if (line.Lamp.Renderer == null) continue;
                Train train = line.Train;
                bool here = false;
                if (!train.Disposed && train.Engine != null)
                {
                    Vector3 engine = train.Engine.Position;
                    float dx = engine.x - line.StationPoint.x, dz = engine.z - line.StationPoint.z;
                    here = dx * dx + dz * dz <= line.Reach * line.Reach;
                }
                line.GreenFor = here ? HoldSeconds : Mathf.Max(0f, line.GreenFor - dt);
                line.AtStation = line.GreenFor > 0f;
                line.Lamp.Set(line.AtStation ? Palette.Go : Palette.Amber);
                line.Lamp.Pulse(time);
            }
        }

        protected override void Forget() => lines.Clear();
    }
}

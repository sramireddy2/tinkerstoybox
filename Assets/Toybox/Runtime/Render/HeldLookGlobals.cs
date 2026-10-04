using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// A stand-in for the lighting rig, for the globals the toy shader reads (ART_BIBLE 3.7, 5.1):
    /// ambient, kicker, the glint basis and colour, the StudioEnv bands and the toy glow gain.
    ///
    /// The lighting rig owns these and keeps them current. This only fills in for it where it is absent
    /// (a presenter list without it, a tool, the time before it existed) - without them every toy would
    /// be lit by the sun alone. The rule that keeps the two from ever fighting: a global is written here
    /// only while it is still zero or still holds exactly what this class wrote last. The moment anybody
    /// else sets it, it is theirs.
    /// </summary>
    public sealed class ToyLookStandIn
    {
        const int Count = 12;
        static readonly int[] Ids =
        {
            Shader.PropertyToID("_AmbSky"), Shader.PropertyToID("_AmbEquator"), Shader.PropertyToID("_AmbGround"),
            Shader.PropertyToID("_KickDir"), Shader.PropertyToID("_KickColor"),
            Shader.PropertyToID("_WinDir"), Shader.PropertyToID("_WinRight"), Shader.PropertyToID("_WinUp"),
            Shader.PropertyToID("_GlintColor"),
            Shader.PropertyToID("_EnvCeil"), Shader.PropertyToID("_EnvWall"), Shader.PropertyToID("_EnvFloor"),
        };
        static readonly int GlowId = Shader.PropertyToID("_ToyGlowGain");

        // What was written last, for all instances: globals outlive the presenter that set them.
        static readonly Vector4[] Written = new Vector4[Count];
        static float writtenGlow;

        readonly Vector4[] values = new Vector4[Count];

        /// <summary>How many of the globals the last <see cref="Apply"/> wrote (0: somebody else has them all).</summary>
        public int Filled { get; private set; }

        /// <summary>The values of ART_BIBLE 5.1 for an environment, written where nobody else has set a global.</summary>
        public void Apply(EnvironmentDescriptor environment)
        {
            Filled = 0;
            if (environment == null) return;
            EnvironmentPreset preset = environment.Preset;
            Dip dip = preset.Dip ?? Palette.Mint;
            Color light = Palette.Lin(dip.Light), mid = Palette.Lin(dip.Mid), deep = Palette.Lin(dip.Deep);

            if (preset.Night)
            {
                Color sky = Palette.Lin(Palette.NightAmbient);
                values[0] = Rgb(sky * 0.34f);
                values[1] = Rgb(Color.LerpUnclamped(sky, light, 0.5f) * 0.30f);
                values[2] = Rgb(deep * 0.35f);
            }
            else
            {
                values[0] = Rgb(Color.white * 0.45f);
                values[1] = Rgb(Color.LerpUnclamped(Color.white, light, 0.5f) * 0.42f);
                values[2] = Rgb(deep * 0.40f);
            }
            // The kicker comes from sun azimuth + 145 degrees at an elevation of 20, in the dip's light tone.
            Vector3 kick = EnvironmentSolver.SunDirection(20f, environment.SunAzimuth + 145f);
            values[3] = new Vector4(kick.x, kick.y, kick.z, 0f);
            values[4] = Rgb(light * 0.20f);
            values[5] = Direction(environment.GlintDir);
            values[6] = Direction(environment.GlintRight);
            values[7] = Direction(environment.GlintUp);
            values[8] = Rgb(Palette.Lin(preset.Night ? Palette.Moon : Palette.GlintDay));
            values[9] = Rgb(Color.white * 1.6f);
            values[10] = Rgb(light);
            values[11] = Rgb(mid * 0.7f);

            for (int i = 0; i < Count; i++)
            {
                Vector4 current = Shader.GetGlobalVector(Ids[i]);
                if (current != Vector4.zero && current != Written[i]) continue;
                Shader.SetGlobalVector(Ids[i], values[i]);
                Written[i] = values[i];
                Filled++;
            }
            float glow = Shader.GetGlobalFloat(GlowId);
            if (glow == 0f || glow == writtenGlow)
            {
                writtenGlow = preset.ToyGlowGain;
                Shader.SetGlobalFloat(GlowId, writtenGlow);
                Filled++;
            }
        }

        /// <summary>Takes back whatever is still this class's own.</summary>
        public void Release()
        {
            for (int i = 0; i < Count; i++)
            {
                if (Written[i] == Vector4.zero || Shader.GetGlobalVector(Ids[i]) != Written[i]) continue;
                Shader.SetGlobalVector(Ids[i], Vector4.zero);
                Written[i] = Vector4.zero;
            }
            if (writtenGlow != 0f && Shader.GetGlobalFloat(GlowId) == writtenGlow) Shader.SetGlobalFloat(GlowId, 0f);
            writtenGlow = 0f;
            Filled = 0;
        }

        static Vector4 Rgb(Color c) => new Vector4(c.r, c.g, c.b, 1f);
        static Vector4 Direction(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
    }
}

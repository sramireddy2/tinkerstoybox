using UnityEngine;

namespace Toybox
{
    /// <summary>
    /// Entry point of the single scene. For now it builds a smoke scene that proves the
    /// Unity + URP + PhysX + WebGL pipeline end to end; the real game bootstrap replaces it.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        static readonly Color[] Palette =
        {
            new Color(0.94f, 0.28f, 0.44f), new Color(0.02f, 0.84f, 0.63f), new Color(0.07f, 0.54f, 0.70f),
            new Color(1.00f, 0.82f, 0.40f), new Color(0.97f, 0.55f, 0.42f), new Color(0.61f, 0.36f, 0.90f),
        };

        Transform pivot;

        void Start()
        {
            var lit = Resources.Load<Material>("Materials/ToyLit");

            var camGo = new GameObject("Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.10f, 0.37f);
            pivot = new GameObject("CameraPivot").transform;
            camGo.transform.SetParent(pivot, false);
            camGo.transform.localPosition = new Vector3(0f, 5f, -11f);
            camGo.transform.LookAt(new Vector3(0f, 1f, 0f));

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(1.0f, 0.95f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.45f, 0.75f);
            RenderSettings.ambientGroundColor = new Color(0.29f, 0.23f, 0.55f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(20f, 0.2f, 20f);
            floor.GetComponent<Renderer>().sharedMaterial = new Material(lit) { color = Palette[3] };

            for (int i = 0; i < 24; i++)
            {
                float s = 0.6f + (i % 5) * 0.24f;
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Block" + i;
                block.transform.position = new Vector3((i % 4) - 1.5f, 3f + i * 0.9f, ((i * 7) % 5) * 0.4f - 1f);
                block.transform.localScale = Vector3.one * s;
                block.GetComponent<Renderer>().sharedMaterial = new Material(lit) { color = Palette[i % Palette.Length] };
                block.AddComponent<Rigidbody>().mass = s * s * s;
            }
        }

        void Update()
        {
            if (pivot != null) pivot.Rotate(0f, 12f * Time.deltaTime, 0f);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Art
{
    public enum ToyMaterialKind
    {
        Plastic,
        Matte,
        Rubber,
        Metal,
    }

    /// <summary>
    /// Shared materials by (kind, color), cloned from Resources/Materials/ToyLit so the shader ships in the
    /// build. Instances are cached for the lifetime of the process; do not modify or destroy them.
    /// </summary>
    public static class ToyMaterials
    {
        public static readonly Color Red = new Color(0.94f, 0.28f, 0.44f);
        public static readonly Color Teal = new Color(0.02f, 0.84f, 0.63f);
        public static readonly Color Blue = new Color(0.07f, 0.54f, 0.70f);
        public static readonly Color Yellow = new Color(1.00f, 0.82f, 0.40f);
        public static readonly Color Orange = new Color(0.97f, 0.55f, 0.42f);
        public static readonly Color Purple = new Color(0.61f, 0.36f, 0.90f);
        public static readonly Color White = new Color(0.96f, 0.95f, 0.92f);
        public static readonly Color Ink = new Color(0.16f, 0.10f, 0.37f);
        public static readonly Color Floor = new Color(0.93f, 0.87f, 0.74f);
        public static readonly Color Wall = new Color(0.80f, 0.84f, 0.93f);

        /// <summary>The toy colors, for picking by index.</summary>
        public static readonly Color[] Palette = { Red, Teal, Blue, Yellow, Orange, Purple };

        const string TemplatePath = "Materials/ToyLit";
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");

        static readonly Dictionary<(ToyMaterialKind, Color32), Material> Cache = new Dictionary<(ToyMaterialKind, Color32), Material>();
        static Material template;

        public static Material Get(Color color) => Get(ToyMaterialKind.Plastic, color);

        public static Material Get(ToyMaterialKind kind, Color color)
        {
            var key = (kind, (Color32)color);
            // A cached entry can be a destroyed object if something cleared all assets; rebuild it then.
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;

            Material source = Template();
            if (source == null) return null;
            var material = new Material(source)
            {
                name = "Toy " + kind + " #" + ColorUtility.ToHtmlStringRGB(color),
                hideFlags = HideFlags.DontSave,
            };
            material.SetColor(BaseColor, color);
            material.color = color;
            switch (kind)
            {
                case ToyMaterialKind.Plastic:
                    Surface(material, 0.55f, 0f);
                    break;
                case ToyMaterialKind.Matte:
                    Surface(material, 0.15f, 0f);
                    break;
                case ToyMaterialKind.Rubber:
                    Surface(material, 0.3f, 0f);
                    break;
                case ToyMaterialKind.Metal:
                    Surface(material, 0.8f, 0.9f);
                    break;
            }
            Cache[key] = material;
            return material;
        }

        static void Surface(Material material, float smoothness, float metallic)
        {
            material.SetFloat(Smoothness, smoothness);
            material.SetFloat(Metallic, metallic);
        }

        static Material Template()
        {
            if (template != null) return template;
            template = Resources.Load<Material>(TemplatePath);
            if (template == null)
            {
                // Only reachable if ProjectSetup has not run; in a player build the shader would be missing too.
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) return null;
                template = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            return template;
        }
    }
}

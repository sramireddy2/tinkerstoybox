// Unlit, vertex-coloured, HDR tint, procedural shape mask from UV, blend state from properties
// (ART_BIBLE 3.5): the sky card and its clouds, hull shadows, the light shaft, dust, confetti, callout
// lines, laser cores, the exit mark. No lighting, no haze, no keywords. Works on a
// ParticleSystemRenderer (billboards and non-instanced meshes: colour in COLOR, the quad in TEXCOORD0).
//
// Output = _Color * vertex colour * shape mask. How the mask and the alpha reach the screen depends on
// the blend preset, which the shader reads from the same properties the render state uses:
//   opaque   (One, Zero)                   colour as it is; a shape other than the quad is cut out
//   alpha    (SrcAlpha, OneMinusSrcAlpha)  alpha = colour alpha * mask
//   additive (One, One)                    colour * alpha * mask
//   multiply (DstColor, Zero)              lerp(white, colour, alpha * mask)
Shader "Toybox/Flat"
{
    Properties
    {
        // Linear and HDR, declared as Vector: Unity would convert a Color property from sRGB.
        _Color ("Colour (linear, HDR)", Vector) = (1, 1, 1, 1)
        _Shape ("Shape (0 quad, 1 soft disc, 2 ring, 3 four-pane mark, 4 gloss disc)", Float) = 0
        _Soft ("Edge softness (0..1)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FlatVert
            #pragma fragment FlatFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Shape;
                float _Soft;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _ZTest;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings FlatVert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 FlatFrag(Varyings i) : SV_Target
            {
                // The shape lives in the unit square of the UVs: p runs -1..1 across it.
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float aa = max(fwidth(r), 1e-4);

                // Four panes: rounded squares with a mullion between them (the brand mark).
                float2 q = abs(abs(p) - 0.5) - 0.34;
                float pane = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.08;
                float paneAa = max(fwidth(pane), 1e-4);

                float soft = max(_Soft, aa);
                half disc = 1.0 - smoothstep(1.0 - soft, 1.0, r);
                half hardDisc = 1.0 - smoothstep(1.0 - aa, 1.0, r);
                float ringSoft = max(_Soft * 0.2, aa);
                half ring = smoothstep(0.8 - ringSoft, 0.8, r) * (1.0 - smoothstep(1.0 - ringSoft, 1.0, r));
                float paneSoft = max(_Soft * 0.5, paneAa);
                half panes = 1.0 - smoothstep(-paneSoft, paneSoft, pane);

                half mask = 1.0;
                if (_Shape > 3.5) mask = hardDisc;
                else if (_Shape > 2.5) mask = panes;
                else if (_Shape > 1.5) mask = ring;
                else if (_Shape > 0.5) mask = disc;

                half4 c = _Color * i.color;
                // The gloss disc: a highlight up and to the left, as on a sequin.
                if (_Shape > 3.5) c.rgb *= 1.0 + 1.2 * (1.0 - smoothstep(0.0, 0.45, distance(p, float2(-0.35, 0.4))));

                half a = saturate(c.a) * mask;
                if (abs(_SrcBlend - 2.0) < 0.5) return half4(lerp(half3(1, 1, 1), c.rgb, a), 1.0);        // multiply
                if (abs(_SrcBlend - 1.0) < 0.5 && abs(_DstBlend - 1.0) < 0.5) return half4(c.rgb * a, a);     // additive
                if (abs(_SrcBlend - 1.0) < 0.5 && _DstBlend < 0.5)                                            // opaque
                {
                    clip(_Shape > 0.5 ? mask - 0.5 : 1.0);
                    return half4(c.rgb, 1.0);
                }
                return half4(c.rgb, a);                                                                        // alpha
            }
            ENDHLSL
        }
    }

    Fallback Off
}

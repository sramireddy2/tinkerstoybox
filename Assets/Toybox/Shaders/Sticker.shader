// Toybox/Sticker - the held toy's die-cut border and peel shadow (ART_BIBLE 3.4, 8).
//
// An override material only: the sticker pass (Render/StickerFeature) draws the Held layer with pass 0,
// then with pass 1, then with the toys' own materials on top. Both passes push every vertex outward by
// a constant number of pixels along its outline normal (TEXCOORD3, the average of the normals that meet
// at the vertex), so the border is as wide at 3 units as at 90. The stencil lets each pass touch a
// pixel once: concave toys are not darkened twice and the shadow never draws over the border.
//
// No material properties. Globals: _StickerPx, _StickerColor, _PeelColor (and the sticker pass's
// _ToyHeld / _ToyHeldScale, through ToyWorldToHClip).
Shader "Toybox/Sticker"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 3.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "ToyboxCommon.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float3 outlineNormal : TEXCOORD3;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        // The clip position of a vertex of the border: moved by _StickerPx.x pixels along the direction
        // its outline normal has on screen, plus offsetPx (x right, y up, in pixels).
        float4 StickerClip(Attributes v, float2 offsetPx)
        {
            float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
            // A mesh that never went through MeshUtil.BakeOutlineNormals has no TEXCOORD3: its own normal then.
            float3 outline = dot(v.outlineNormal, v.outlineNormal) > 1e-6 ? v.outlineNormal : v.normalOS;
            float3 normalWS = TransformObjectToWorldNormal(outline);

            // Where the vertex goes on screen when it moves along the normal: the derivative of xy / w.
            float4 at = TransformWorldToHClip(positionWS);
            float4 along = mul(GetWorldToHClipMatrix(), float4(normalWS, 0.0));
            float2 direction = (along.xy * at.w - at.xy * along.w) * _ScaledScreenParams.xy;
            float size = length(direction);
            direction = size > 1e-6 ? direction / size : float2(0.0, 0.0);

            float4 clip = ToyWorldToHClip(positionWS);
            float2 pixel = 2.0 / _ScaledScreenParams.xy;
            clip.xy += direction * (_StickerPx.x * pixel) * clip.w;
            // Up on screen is down in clip space where the projection is flipped.
            clip.xy += float2(offsetPx.x, offsetPx.y * _ProjectionParams.x) * pixel * clip.w;
            return clip;
        }
        ENDHLSL

        Pass
        {
            Name "Border"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest Always
            ZWrite Off
            Cull Off
            Stencil
            {
                Ref 0
                Comp Equal
                Pass IncrSat
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = StickerClip(v, float2(0.0, 0.0));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(_StickerColor.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PeelShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest Always
            ZWrite Off
            Cull Off
            // Multiply what is there by _PeelColor. Written as Zero SrcColor on purpose: the equivalent
            // DstColor Zero came out brighter instead of darker on Direct3D 12 with MSAA (Intel Arc).
            Blend Zero SrcColor
            Stencil
            {
                Ref 0
                Comp Equal
                Pass IncrSat
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = StickerClip(v, _StickerPx.yz);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(_PeelColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

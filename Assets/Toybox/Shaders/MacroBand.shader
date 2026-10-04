// Toybox/MacroBand (ART_BIBLE 3.6): the lens blur of a macro photograph. It depends on screen y alone -
// sharp across the middle of the picture, soft toward the top and bottom edges - and never reads depth,
// so it cannot tell how far away anything is (ART_BIBLE 8). The crosshair always sits in the sharp band.
//
// Drawn by URP's stock FullScreenPassRendererFeature ("MacroBand" on ToyboxRenderer.asset) before
// post-processing, on the HDR colour, with the colour buffer fetched into _BlitTexture.
//
// The three numbers are GLOBAL uniforms, written every frame by Toybox.Render.PostLook:
//   _RadiusPx  blur radius at the picture's edge in pixels of the render target (already scaled by
//              height / 1080 and by the player's lens-blur strength)
//   _Taps      samples on the Vogel disc (8 on Medium, 12 on High, at most 16)
//   _Full      0 in play; 1 blurs the whole picture (the pause blur, ART_BIBLE 10.5)
// They are not material properties because the renderer feature draws with the MacroBand.mat asset
// itself: per-frame values written into that material would dirty the asset in the editor. With nothing
// set (no PostLook alive) all three are 0 and the pass copies the picture unchanged.
Shader "Toybox/MacroBand"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "MacroBand"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MACRO_MAX_TAPS 16

            float _RadiusPx;
            float _Taps;
            float _Full;

            // Point k of n on a Vogel disc of radius 1: the golden angle between neighbours, and the
            // radius growing with the square root so that the points cover the disc evenly.
            float2 VogelOffset(int k, float n)
            {
                float angle = k * 2.39996;
                float radius = sqrt((k + 0.5) / n);
                return float2(cos(angle), sin(angle)) * radius;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float band = max(smoothstep(0.24, 0.5, abs(uv.y - 0.5)), saturate(_Full));
                float r = band * _RadiusPx;
                float taps = clamp(floor(_Taps + 0.5), 1.0, MACRO_MAX_TAPS);
                // The sharp band, and a blur too small to see: the picture as it is.
                if (band < 0.02 || r < 0.01)
                    return half4(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb, 1);

                float2 step = r * _BlitTexture_TexelSize.xy;
                float3 acc = 0;
                for (int k = 0; k < MACRO_MAX_TAPS; k++)
                {
                    if (k >= taps) break;
                    acc += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + VogelOffset(k, taps) * step, 0).rgb;
                }
                return half4(acc / taps, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

// Every static surface of the game (ART_BIBLE 3.3): the shell, level statics, backdrop furniture.
// Dip tones by the top-light rule, analytic world-space patterns, the corner gradient, colour pools, the
// window patch, the splash ring and haze. Matte: there is no specular term at all.
//
// No shader_feature: every option is a uniform, so a material made in code can never ask for a variant
// that was stripped from the build. The only keywords are URP's main-light shadow ones.
Shader "Toybox/RoomLit"
{
    Properties
    {
        // Colours are linear and declared as Vector: Unity would convert a Color property from sRGB.
        _ColorTop ("Top tone (linear)", Vector) = (0.456, 0.791, 0.644, 1)
        _ColorSide ("Side tone (linear)", Vector) = (0.723, 0.913, 0.823, 1)
        _ColorDado ("Dado tone (linear)", Vector) = (0.456, 0.791, 0.644, 1)
        _DadoY ("Dado line (world y; -100000 is off)", Float) = -100000
        _Pattern ("Pattern (0 none, 1 dots, 2 planks, 3 tiles, 4 stripes, 5 quilt, 6 pegboard, 7 corrugated)", Float) = 0
        _PatternA ("Pattern (pitch, param 1, param 2, gain)", Vector) = (8, 1.4, 0, 0.04)
        _Corner ("Corner gradient (1 on shell surfaces)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RoomVert
            #pragma fragment RoomFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RoomLitLib.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings RoomVert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 RoomFrag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 P = i.positionWS;

                // Derivatives first, outside every branch.
                float2 uv = abs(N.y) > 0.7 ? P.xz : (abs(N.x) > abs(N.z) ? P.zy : P.xy);
                float2 fw = fwidth(uv);
                float w = max(fw.x, fw.y);
                float dadoWidth = max(fwidth(P.y), 1e-4);

                // 1. Tone: the top-light rule, and the dado below its line.
                half top = smoothstep(0.5, 0.8, N.y);
                half dado = saturate((_DadoY - P.y) / dadoWidth + 0.5);
                half3 side = lerp(_ColorSide.rgb, _ColorDado.rgb, dado);
                half3 albedo = lerp(side, _ColorTop.rgb, top);

                // 2. Pattern: analytic, world space, the plane picked by the dominant normal axis.
                float2 pattern = RoomPattern(uv, N, w);
                albedo = lerp(albedo, _ColorSide.rgb, pattern.y * top);    // tiles: the checker's other tone
                albedo *= max(1.0 + pattern.x * _PatternA.w, 0.0);
                albedo *= i.color.rgb;                                     // baked ambient occlusion

                // 3. Corner gradient (shell only).
                albedo *= 1.0 - 0.22 * _Corner * exp(-RoomCornerDistance(P, N) / 6.0);

                // 4. Colour pools: every resting toy darkens its contact and bounces its colour.
                half core;
                half3 glow;
                RoomPools(P, N, core, glow);
                albedo *= 1.0 - 0.65 * core;

                // 5. Light: the sun with its shadow, the kicker, the three-colour ambient.
                Light sun = GetMainLight(TransformWorldToShadowCoord(P), P, half4(1, 1, 1, 1));
                half lit = saturate(dot(N, sun.direction)) * sun.shadowAttenuation;
                half3 c = albedo * (sun.color * lit + _KickColor.rgb * saturate(dot(N, _KickDir.xyz)) + Tri(N));

                // 6. Emissive adds: pools, the window patch, the splash ring.
                // A toy's colour lands on the floor partly as its own hue, not only as what the floor's
                // albedo lets through: each room's hero candy is the complement of its dip, and a purely
                // multiplied Cherry pool on a Mint rug comes out as nothing at all.
                half3 poolAlbedo = lerp(albedo, half3(1.0, 1.0, 1.0), ROOM_POOL_OWN);
                c += poolAlbedo * glow * _PoolGain;
                c += albedo * _PatchColor.rgb * (RoomPatch(P, sun.direction) * lit);
                half ring = 1.0 - smoothstep(0.0, max(_Splash.w * 0.25, 1e-4), abs(distance(P, _Splash.xyz) - _SplashTint.a));
                c += poolAlbedo * _SplashTint.rgb * (ring * ROOM_RING_BOOST);

                // Nothing renders darker than Ink (ART_BIBLE 2.5): in the night room the deep tones would.
                // A toe rather than a clamp, so what is down there keeps its shape: black comes out as Ink,
                // anything above four times Ink's luminance is left alone.
                half toe = saturate(1.0 - dot(c, half3(0.2126, 0.7152, 0.0722)) / (4.0 * ROOM_INK_LUMINANCE));
                c += ROOM_INK * (toe * toe);

                // 7. Haze: distance and height.
                c = lerp(c, _HazeColor.rgb, RoomHaze(P));
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "RoomLitLib.hlsl"

            // Set by URP for the light that is being rendered; the game only has the directional sun.
            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Unused by the shipped pipeline (no depth texture); here so the shader stays correct if someone
        // turns on depth priming or a depth texture while debugging.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RoomLitLib.hlsl"

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 DepthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

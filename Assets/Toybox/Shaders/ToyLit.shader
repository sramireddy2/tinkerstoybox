// Toybox/ToyLit - every toy, non-grabbable prop and gadget (ART_BIBLE 3.2).
//
// Product lighting: wrap diffuse, a GGX key highlight, the analytic StudioEnv reflection, the four-pane
// window glint and the kicker rim. Everything except the received shadow depends only on the normal,
// the view vector and directions at infinity, which is why a held toy cannot betray its distance
// (ART_BIBLE 8); the sticker pass switches that one term off through _ToyHeld. This shader never
// applies haze, pools, the window patch or anything else that reads the world position.
//
// Rules (3.1): no shader_feature - options are uniforms; colours are Vector properties holding linear
// values; one UnityPerMaterial block shared by every pass; no depth or opaque texture.
Shader "Toybox/ToyLit"
{
    Properties
    {
        _BaseColor ("Base colour (linear rgb)", Vector) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.6
        _Wrap ("Diffuse wrap", Range(0, 1)) = 0.2
        _Env ("StudioEnv gain", Range(0, 2)) = 0.5
        _Coat ("Clear coat", Range(0, 1)) = 0
        _Streak ("Streak (brushed metal)", Range(0, 0.3)) = 0
        _Glint ("Glint gain", Range(0, 2)) = 1
        _GlintSoft ("Glint softness", Float) = 0.02
        _GlintStretch ("Glint stretch", Float) = 1
        _Rim ("Rim", Range(0, 1)) = 0.55
        _RimPow ("Rim power", Float) = 3
        _RimColor ("Rim colour (linear rgb)", Vector) = (1, 0.9823, 0.9302, 1)
        _SelfGlow ("Self glow", Range(0, 1)) = 0.06
        _Emission ("Emission (linear HDR rgb)", Vector) = (0, 0, 0, 0)
        _Translucency ("Translucency", Range(0, 1)) = 0
        _DetailMap ("Detail (R albedo, G smoothness, B height; 128 neutral)", 2D) = "gray" {}
        _DetailAlbedo ("Detail albedo", Range(0, 1)) = 0
        _DetailSmooth ("Detail smoothness", Range(0, 1)) = 0
        _DetailBump ("Detail bump (High tier only)", Range(0, 1)) = 0
        _AlphaFace ("Alpha, facing", Range(0, 1)) = 1
        _AlphaEdge ("Alpha, edge", Range(0, 1)) = 1
        _AlphaPow ("Alpha power", Float) = 2.5
        _ShadowDither ("Shadow dither (0 or 1)", Float) = 0
        _SrcBlend ("Src blend", Float) = 1
        _DstBlend ("Dst blend", Float) = 0
        _ZWrite ("Z write", Float) = 1
        _Cull ("Cull", Float) = 2
        // Per renderer, through a MaterialPropertyBlock; the material's own values stay 0.
        _Sweep ("Focus sweep (centre.xy viewport, position, gain)", Vector) = (0, 0, 0, 0)
        _SquashA ("Squash (centre.x, pivot.y, centre.z, amount)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 3.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _RimColor;
            float4 _Emission;
            float4 _DetailMap_ST;
            float4 _Sweep;
            float4 _SquashA;
            half _Metallic;
            half _Smoothness;
            half _Wrap;
            half _Env;
            half _Coat;
            half _Streak;
            half _Glint;
            half _GlintSoft;
            half _GlintStretch;
            half _Rim;
            half _RimPow;
            half _SelfGlow;
            half _Translucency;
            half _DetailAlbedo;
            half _DetailSmooth;
            half _DetailBump;
            half _AlphaFace;
            half _AlphaEdge;
            half _AlphaPow;
            half _ShadowDither;
            half _SrcBlend;
            half _DstBlend;
            half _ZWrite;
            half _Cull;
        CBUFFER_END

        #include "ToyboxCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // Exactly these (ART_BIBLE 3.1 rule 6): what makes main-light shadows work on WebGL.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DetailMap);
            SAMPLER(sampler_DetailMap);

            // A detail value of 1 (the full swing of the texture) is this much height, in authored units,
            // per unit of _DetailBump.
            #define TOYBOX_BUMP_UNIT 0.07
            // A surface whose normal turns by 1 / TOYBOX_CURVE_GAIN radians over one screen height counts
            // as fully curved; a flat face is lit as if seen at no less than this N.V (see Frag).
            #define TOYBOX_CURVE_GAIN 2.0
            #define TOYBOX_FLAT_NDV 0.6
            // The window glint on a flat face: there the whole face mirrors one pane at once and goes
            // white, which is a flash, not the four-pane mark. It keeps this share of its strength.
            #define TOYBOX_FLAT_GLINT 0.2

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 uvScale : TEXCOORD2;     // xy: detail uv, z: the object's uniform scale
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = ToySquash(TransformObjectToWorld(v.positionOS.xyz), _SquashA);
                o.positionWS = positionWS;
                o.positionCS = ToyWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uvScale = float3(v.uv * _DetailMap_ST.xy + _DetailMap_ST.zw, ToyObjectScale());
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 P = i.positionWS;
                float3 N = normalize(i.normalWS);
                // Two-sided sheets (paper, feather) and the glass back shell are lit on the side that is seen.
                N = IS_FRONT_VFACE(facing, N, -N);
                float3 V = GetWorldSpaceNormalizeViewDir(P);
                // How curved the surface is under this pixel: 0 on a flat face, 1 on a bevel, a ball, a
                // silhouette. It is the change of the (un-bumped) normal across the screen, per screen
                // height, so it is the same at every resolution and - the normal of a held toy's pixel
                // never changes - at every hold distance.
                half curved = saturate(length(fwidth(N)) * _ScaledScreenParams.y * TOYBOX_CURVE_GAIN);

                float2 uv = i.uvScale.xy;
                float2 uvDx = ddx(uv), uvDy = ddy(uv);
                float3 sigmaS = ddx(P), sigmaT = ddy(P);
                half3 det = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv).rgb - TOYBOX_DETAIL_NEUTRAL;

                // High tier only (the material's _DetailBump is 0 below it): tangent-free bump mapping
                // (Mikkelsen). The height is in world units and grows with the object, so the perturbed
                // normal is the same at every hold distance.
                UNITY_BRANCH
                if (_DetailBump > 0.0)
                {
                    float hx = SAMPLE_TEXTURE2D_GRAD(_DetailMap, sampler_DetailMap, uv + uvDx, uvDx, uvDy).b - TOYBOX_DETAIL_NEUTRAL;
                    float hy = SAMPLE_TEXTURE2D_GRAD(_DetailMap, sampler_DetailMap, uv + uvDy, uvDx, uvDy).b - TOYBOX_DETAIL_NEUTRAL;
                    float unit = _DetailBump * TOYBOX_BUMP_UNIT * i.uvScale.z;
                    float dBs = (hx - det.b) * unit, dBt = (hy - det.b) * unit;
                    float3 r1 = cross(sigmaT, N), r2 = cross(N, sigmaS);
                    float d = dot(sigmaS, r1);
                    float3 gradient = sign(d) * (dBs * r1 + dBt * r2);
                    N = normalize(abs(d) * N - gradient);
                }

                half3 albedo = _BaseColor.rgb * i.color.rgb * (1.0 + det.r * 2.0 * _DetailAlbedo);
                half smoothness = saturate(_Smoothness * (1.0 + det.g * 2.0 * _DetailSmooth));
                BRDFData brdf;
                half one = 1.0;
                InitializeBRDFData(albedo, _Metallic, half3(0.0, 0.0, 0.0), smoothness, one, brdf);

                Light sun = GetMainLight(TransformWorldToShadowCoord(P), P, half4(1.0, 1.0, 1.0, 1.0));
                // The only term that depends on where the toy is.
                half shadow = lerp(sun.shadowAttenuation, 1.0, _ToyHeld);
                half ndl = dot(N, sun.direction);
                half ndv = saturate(dot(N, V));

                half3 c = brdf.diffuse * (sun.color * (saturate((ndl + _Wrap) / (1.0 + _Wrap)) * shadow)
                                          + _KickColor.rgb * saturate(dot(N, _KickDir.xyz)) + Tri(N));
                half3 highlight = brdf.specular * DirectBRDFSpecular(brdf, N, sun.direction, V) * sun.color * (saturate(ndl) * shadow);

                float3 R = reflect(-V, N);
                // The Fresnel terms (reflection, coat, rim) peak where the surface turns away from the eye.
                // On a ball or a bevel that is a thin edge. A flat top seen from a figure's eye height is
                // at a grazing angle all over and would turn white from end to end - and candy colour is the
                // one thing that says "you can lift this" (ART_BIBLE 2.6). So a flat face is shaded as if it
                // were seen from no lower than TOYBOX_FLAT_NDV; its bevels still catch the light.
                half ndvF = max(ndv, (1.0 - curved) * TOYBOX_FLAT_NDV);
                half f5 = pow(1.0 - ndvF, 5.0);
                half fr = pow(max(1.0 - ndvF, 1e-4), _RimPow);
                c += StudioEnv(R, brdf.perceptualRoughness, det.g * _Streak) * EnvironmentBRDFSpecular(brdf, f5) * _Env;
                highlight += StudioEnv(R, 0.06, 0.0) * ((0.04 + 0.96 * f5) * _Coat * 0.6);
                highlight += _GlintColor.rgb * (1.8 * _Glint * (0.6 + 0.4 * fr) * lerp(TOYBOX_FLAT_GLINT, 1.0, curved) * WindowPane(R, _GlintSoft, _GlintStretch));
                c += highlight;
                c += _RimColor.rgb * (_Rim * (1.0 + _ToyRimBoost) * fr * (0.35 + 0.65 * saturate(N.y * 0.5 + 0.5)));
                c += albedo * (_SelfGlow * _ToyGlowGain) + _Emission.rgb;
                c += sun.color * albedo * (saturate(-ndl) * _Translucency * shadow);

                // The focus sweep (9.1): a 45 degree band through a point of the screen. _Sweep.xy is that
                // point in viewport units, .z the band's place along the diagonal in screen heights.
                float2 pixel = i.positionCS.xy;
                #if UNITY_UV_STARTS_AT_TOP
                    if (_ProjectionParams.x > 0.0) pixel.y = _ScaledScreenParams.y - pixel.y;
                #endif
                float2 fromCentre = (pixel - _Sweep.xy * _ScaledScreenParams.xy) / _ScaledScreenParams.y;
                half sweep = smoothstep(0.06, 0.0, abs(dot(fromCentre, float2(0.7071, 0.7071)) - _Sweep.z));
                c += 0.5 * _Sweep.w * sweep;

                // The shaded details of a toy (a sponge's pores, a thimble's dimples) and unlit metal at night
                // would go to black; nothing does (2.5 rule 7). Depends on the colour alone.
                c = InkToe(c);

                half alpha = lerp(_AlphaFace, _AlphaEdge, pow(max(1.0 - ndv, 1e-4), _AlphaPow));
                // Glass: a highlight is light added on top of whatever is behind, so it is not thinned out
                // with the body of the glass. (Opaque materials have alpha 1 anyway.)
                alpha = saturate(max(alpha, Luminance(highlight)));
                return half4(c, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP's shadow pass: the direction toward the (directional) light. The game has no
            // punctual lights, so there is no punctual variant.
            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = ToySquash(TransformObjectToWorld(v.positionOS.xyz), _SquashA);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                o.positionCS = ApplyShadowClamping(positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // Glass casts a half-density shadow: a checker of shadow-map texels.
                if (_ShadowDither > 0.5)
                {
                    float2 cell = floor(i.positionCS.xy);
                    clip(frac((cell.x + cell.y) * 0.5) - 0.25);
                }
                return 0;
            }
            ENDHLSL
        }

        // Unused in the shipped pipeline (there is no depth texture); here so the shader stays correct if
        // someone turns on depth priming or a depth texture while debugging.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = ToyWorldToHClip(ToySquash(TransformObjectToWorld(v.positionOS.xyz), _SquashA));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

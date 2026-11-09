// Matte printed-polymer look from the art bible: soft wrapped diffuse, gentle rim, faint layer banding.
// The stencil block lets hands mark their pixels and aircraft clear them (see OccludedSilhouette).
Shader "MiniPlanes/Matte"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap ("Base Map", 2D) = "white" {}
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 1
        _Wrap ("Diffuse Wrap", Range(0,1)) = 0.45
        _Rim ("Rim Light", Range(0,1)) = 0.12
        _LayerStrength ("Layer Line Strength", Range(0,0.2)) = 0.035
        _LayerBands ("Layer Bands Per Metre", Float) = 450
        _Flash ("Flash", Range(0,1)) = 0
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _HueShift ("Paint Hue Shift", Range(-0.5,0.5)) = 0
        _SatScale ("Paint Saturation", Range(0,2)) = 1
        _ValScale ("Paint Brightness", Range(0,2)) = 1
        _HueSource ("Paint Only This Hue (-1 for all)", Float) = -1
        _HueWidth ("Paint Hue Width", Range(0.02,0.5)) = 0.12
        _StencilRef ("Stencil Ref", Int) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil Pass", Int) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Stencil { Ref [_StencilRef] Comp Always Pass [_StencilPass] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _UseVertexColor;
                half _Wrap;
                half _Rim;
                half _LayerStrength;
                float _LayerBands;
                half _Flash;
                half4 _FlashColor;
                half _HueShift;
                half _SatScale;
                half _ValScale;
                half _HueSource;
                half _HueWidth;
            CBUFFER_END

            half3 MpRgbToHsv(half3 c)
            {
                half4 K = half4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
                half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
                half d = q.x - min(q.w, q.y);
                return half3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-4)), d / (q.x + 1e-4), q.x);
            }

            half3 MpHsvToRgb(half3 c)
            {
                half3 p = abs(frac(c.xxx + half3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                return c.z * lerp(half3(1, 1, 1), saturate(p - 1.0), c.y);
            }

            // Repaints only the colourful parts, so cream trim and dark canopies keep their look.
            half3 Repaint(half3 albedo)
            {
                half3 hsv = MpRgbToHsv(albedo);
                half w = smoothstep(0.18, 0.4, hsv.y);
                if (_HueSource >= 0.0)
                {
                    half dh = abs(hsv.x - _HueSource);
                    dh = min(dh, 1.0 - dh);
                    w *= 1.0 - smoothstep(_HueWidth, _HueWidth * 1.6, dh);
                }
                hsv.x = frac(hsv.x + _HueShift * w);
                hsv.y = saturate(hsv.y * lerp(1.0, _SatScale, w));
                hsv.z = hsv.z * lerp(1.0, _ValScale, w);
                return MpHsvToRgb(hsv);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                // Vertex colours are authored in sRGB and imported untouched.
                half3 vc = pow(max(i.color.rgb, 0.0001), 2.2);
                albedo *= lerp(half3(1,1,1), vc, _UseVertexColor);
                if (abs(_HueShift) > 0.001 || abs(_SatScale - 1.0) > 0.001 || abs(_ValScale - 1.0) > 0.001)
                    albedo = Repaint(albedo);

                Light l = GetMainLight();
                half ndl = saturate((dot(n, l.direction) + _Wrap) / (1.0 + _Wrap));
                half3 lit = SampleSH(n) + l.color * ndl;

                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _Rim;

                half bands = 1.0 + _LayerStrength * sin(i.positionWS.y * _LayerBands * 6.2831853);
                half3 c = albedo * lit * bands + rim;
                c = lerp(c, _FlashColor.rgb, _Flash);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}

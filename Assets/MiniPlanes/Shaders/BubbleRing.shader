// Scoring portal: a glassy soap-bubble rim with a thin membrane, per the scoring ring storyboard.
// uv.x = angle around the ring (0..1, clockwise from the top). uv.y = membrane radius (0..1).
// Vertex colour red: 1 on the rim torus, 0 on the membrane.
// _Fill drives the radial fill, _Peanut pinches the ring while two rings merge, _Wobble makes it jelly.
// _Contour holds the outline radius at 16 angles, so a single ring can follow the shape of the fingers.
Shader "MiniPlanes/BubbleRing"
{
    Properties
    {
        _RimColor ("Rim Color", Color) = (0.17,0.72,0.60,1)
        _GlassColor ("Glass Color", Color) = (0.72,0.96,0.86,1)
        _HighlightColor ("Highlight Color", Color) = (1,0.98,0.90,1)
        _MembraneAlpha ("Membrane Alpha", Range(0,1)) = 0.2
        _GhostAlpha ("Unfilled Ghost Alpha", Range(0,1)) = 0.16
        _Fill ("Radial Fill", Range(0,1)) = 1
        _Peanut ("Peanut Pinch", Range(0,0.6)) = 0
        _Wobble ("Wobble", Range(0,0.2)) = 0.03
        _WobbleSpeed ("Wobble Speed", Float) = 2.6
        _Flash ("Score Flash", Range(0,1)) = 0
        _ContourOn ("Use Contour", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "Bubble"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half4 _GlassColor;
                half4 _HighlightColor;
                half _MembraneAlpha;
                half _GhostAlpha;
                half _Fill;
                half _Peanut;
                half _Wobble;
                float _WobbleSpeed;
                half _Flash;
                float _ContourOn;
            CBUFFER_END
            float _Contour[16];

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
                half isRim : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float a = v.uv.x * 6.2831853;
                float t = _Time.y * _WobbleSpeed;
                float scale = 1.0 - _Peanut * cos(2.0 * a)
                    + _Wobble * (sin(3.0 * a + t) + 0.5 * sin(5.0 * a - t * 1.4));
                if (_ContourOn > 0.5)
                {
                    float s = frac(v.uv.x) * 16.0;
                    int s0 = (int)floor(s);
                    int s1 = s0 >= 15 ? 0 : s0 + 1;
                    scale *= lerp(_Contour[s0], _Contour[s1], s - s0);
                }
                float3 p = v.positionOS.xyz;
                p.xy *= scale;
                p.z += _Wobble * 0.6 * sin(2.0 * a + t * 0.8);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.isRim = v.color.r;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fres = pow(1.0 - abs(dot(n, view)), 2.0);

                // Soft leading edge on the radial fill.
                half edge = 0.02;
                half filled = 1.0 - smoothstep(_Fill - edge, _Fill + edge, i.uv.x);
                if (_Fill >= 0.999) filled = 1.0;

                half4 c;
                if (i.isRim > 0.5)
                {
                    half3 body = lerp(_RimColor.rgb, _GlassColor.rgb, fres * 0.75);
                    // A cream sliver of specular on the upper left, as drawn in the storyboard.
                    half3 lightDir = normalize(view + half3(-0.35, 0.8, 0.0));
                    half spec = pow(saturate(dot(n, lightDir)), 28.0);
                    body = lerp(body, _HighlightColor.rgb, saturate(spec * 1.4));
                    c = half4(body, lerp(_GhostAlpha, 0.94, filled));
                }
                else
                {
                    half r = i.uv.y;
                    half film = 0.35 + 0.65 * r * r * r;
                    half shimmer = 0.5 + 0.5 * sin(_Time.y * 1.3 + i.uv.x * 12.566 + r * 6.0);
                    half3 body = lerp(_GlassColor.rgb, _HighlightColor.rgb, shimmer * 0.25);
                    c = half4(body, _MembraneAlpha * film * filled);
                }
                c.rgb = lerp(c.rgb, _HighlightColor.rgb, _Flash);
                c.a = saturate(c.a + _Flash * 0.25 * filled);
                return c;
            }
            ENDHLSL
        }
    }
}

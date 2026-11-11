// Solid, comic-book panels and buttons for the menu, pause, wrist and splash screens.
//
// The sprite (RoundedComic) is a rounded box that carries the comic shading in its channels rather than as
// colour, so one sprite serves every tint: red is the cel shade (1 lit, lower in the band along the bottom),
// green is the highlight band along the top, blue is the ink outline, alpha the shape. The Image's colour
// is the paint; the ink stays ink whatever the paint, and the highlight goes toward white, which a plain
// tinted sprite can't do. The drop shadows are separate Images behind (see UiComic).
Shader "MiniPlanes/UIComic"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite (r shade, g highlight, b ink, a shape)", 2D) = "white" {}
        _Ink ("Ink", Color) = (0.16, 0.13, 0.2, 1)
        _Highlight ("Highlight strength", Range(0, 1)) = 0.55
        _ShadeDepth ("Shade depth", Range(0, 1)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" "RenderPipeline"="UniversalPipeline" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Comic"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Ink;
                half _Highlight;
                half _ShadeDepth;
                float4 _ClipRect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 local : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.local = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 paint = i.color.rgb;
                half3 col = paint * lerp(1.0, s.r, _ShadeDepth);
                col = lerp(col, half3(1, 1, 1), s.g * _Highlight);
                col = lerp(col, _Ink.rgb, s.b);
                half alpha = s.a * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(_ClipRect.xy, i.local.xy) * step(i.local.xy, _ClipRect.zw);
                alpha *= inside.x * inside.y;
                #endif
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}

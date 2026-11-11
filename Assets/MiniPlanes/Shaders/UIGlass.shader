// Frosted, refracting glass for the menu, pause and wrist panels and their buttons.
//
// The sprite (RoundedGlass) carries the panel's shape in alpha and, in red, how far each pixel is from the
// edge, so the glass can bevel: the scene behind bends most at the rim, which also catches a highlight.
// What it bends is the URP opaque texture, so 3D objects behind a panel show through, distorted. The
// passthrough camera image is composited by the headset outside Unity, so over plain passthrough the glass
// is simply a translucent tint.
Shader "MiniPlanes/UIGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite (alpha shape, red edge distance)", 2D) = "white" {}
        _TintStrength ("Tint over the scene", Range(0, 1)) = 0.5
        _Opacity ("Opacity over passthrough", Range(0, 1)) = 0.6
        _Refraction ("Ripple refraction", Range(0, 0.03)) = 0.006
        _EdgeRefraction ("Edge refraction", Range(0, 0.1)) = 0.035
        _RimLight ("Rim highlight", Range(0, 1)) = 0.45
        _Sheen ("Diagonal sheen", Range(0, 0.5)) = 0.12

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
            Name "Glass"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _TintStrength;
                half _Opacity;
                half _Refraction;
                half _EdgeRefraction;
                half _RimLight;
                half _Sheen;
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
                float4 screen : TEXCOORD1;
                float3 local : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screen = ComputeScreenPos(o.positionCS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.local = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half shape = s.a;
                // 1 at the rim, 0 once well inside: the bevel.
                half edge = saturate(1.0 - s.r);
                half bevel = edge * edge;

                // Bend toward the outside at the bevel, plus a faint ripple across the whole pane.
                float2 grad = float2(ddx(s.r), ddy(s.r));
                float2 outward = dot(grad, grad) > 1e-8 ? -normalize(grad) : float2(0, 0);
                float2 ripple = float2(sin(i.local.x * 0.045 + i.local.y * 0.021), cos(i.local.y * 0.05 - i.local.x * 0.017));
                float2 screenUV = i.screen.xy / i.screen.w;
                float2 bentUV = clamp(screenUV + outward * bevel * _EdgeRefraction + ripple * _Refraction, 0.001, 0.999);
                half4 behind = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(bentUV));

                half3 tint = i.color.rgb;
                // Over 3D objects: show them, bent and tinted, and cover what is really there. Over passthrough
                // (nothing rendered, alpha 0): a translucent tint the headset's camera image shows through.
                half covered = saturate(behind.a);
                half3 overScene = lerp(behind.rgb, tint, _TintStrength);
                half3 col = lerp(tint, overScene, covered);
                half alpha = lerp(_Opacity, 1.0, covered);

                // A highlight round the rim and a soft diagonal sheen sell the glass.
                half sheen = smoothstep(0.35, 0.0, abs(frac((screenUV.x + screenUV.y) * 0.9 + 0.2) - 0.5)) * _Sheen;
                col += (bevel * _RimLight + sheen) * (1.0 - col * 0.5);
                alpha = saturate(alpha + bevel * _RimLight * 0.5);

                alpha *= shape * i.color.a;
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

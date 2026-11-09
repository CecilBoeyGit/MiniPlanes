// The faint flight-path preview. Soft across the width, with a slow dash drift. Everything before _Trim,
// the part of the path the plane has already flown, is hidden, and the line fades in just after it.
Shader "MiniPlanes/PathLine"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.55,0.92,0.85,1)
        _Alpha ("Alpha", Range(0,1)) = 0.4
        _DashCount ("Dash Count", Float) = 90
        _DashFloor ("Gap Brightness", Range(0,1)) = 0.35
        _DashSpeed ("Dash Drift Speed", Float) = 0.6
        _Trim ("Hidden Up To (0 to 1 along the line)", Range(0,1)) = 0
        _TrimFade ("Start Fade Length (0 to 1 along the line)", Range(0.0001,1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" }
        Pass
        {
            Name "Path"
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
                half4 _BaseColor;
                half _Alpha;
                float _DashCount;
                half _DashFloor;
                float _DashSpeed;
                float _Trim;
                float _TrimFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                half start = saturate((i.uv.x - _Trim) / _TrimFade);
                half end = saturate((1.0 - i.uv.x) * 40.0);
                half dash = lerp(_DashFloor, 1.0, 0.5 + 0.5 * sin((i.uv.x * _DashCount - _Time.y * _DashSpeed) * 6.2831853));
                return half4(_BaseColor.rgb, _BaseColor.a * _Alpha * saturate(across * 2.0) * start * end * dash);
            }
            ENDHLSL
        }
    }
}

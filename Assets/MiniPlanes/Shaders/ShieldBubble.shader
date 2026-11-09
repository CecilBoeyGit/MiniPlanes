// Thin fresnel shell around the plane while it carries a one-hit shield.
Shader "MiniPlanes/ShieldBubble"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.42,0.75,1,1)
        _Alpha ("Alpha", Range(0,1)) = 0.85
        _Power ("Fresnel Power", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" }
        Pass
        {
            Name "Shield"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Alpha;
                half _Power;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half f = pow(1.0 - saturate(dot(n, view)), _Power);
                half pulse = 0.85 + 0.15 * sin(_Time.y * 4.0);
                return half4(_BaseColor.rgb, saturate(f * _Alpha * pulse + 0.05));
            }
            ENDHLSL
        }
    }
}

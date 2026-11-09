// Solid reveal of the plane where it passes behind a hand.
// Hands write stencil 1 and are drawn first. Aircraft clear it where they are visible. This pass only
// draws where the fragment is behind existing depth AND the stencil says that depth belongs to a hand.
Shader "MiniPlanes/OccludedSilhouette"
{
    Properties
    {
        _BaseColor ("Silhouette Color", Color) = (0.18,0.72,0.66,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+490" }
        Pass
        {
            Name "Silhouette"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZTest Greater
            ZWrite Off
            Stencil { Ref 1 Comp Equal Pass Keep }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(_BaseColor.rgb, 1); }
            ENDHLSL
        }
    }
}

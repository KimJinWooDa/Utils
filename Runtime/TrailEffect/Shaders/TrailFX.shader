Shader "TelleR/Trail"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0, 0.5, 1, 0.6)
        _Alpha ("Alpha", Float) = 1
        _FresnelPower ("Fresnel Power", Float) = 3
        _FresnelIntensity ("Fresnel Intensity", Float) = 0
        _UseTexStamp ("Use TexStamp", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _StencilRef ("Stencil Ref", Int) = 0
        _StencilComp ("Stencil Comp", Float) = 8
        _StencilOp ("Stencil Op", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "TrailFXPass"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass [_StencilOp]
            }

            HLSLPROGRAM
            #pragma vertex TrailVert
            #pragma fragment TrailFrag

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct TrailInstanceData
            {
                float4 color;
                float alpha;
                float fresnelPower;
                float fresnelIntensity;
                float padding;
            };

            StructuredBuffer<TrailInstanceData> _TrailBuffer;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half3  normalWS    : TEXCOORD1;
                half3  viewDirWS   : TEXCOORD2;
                nointerpolation uint instanceIndex : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Alpha;
                half   _FresnelPower;
                half   _FresnelIntensity;
                half   _UseTexStamp;
            CBUFFER_END

            Varyings TrailVert(Attributes input, uint svInstanceID : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.instanceIndex = svInstanceID;

                float3 posOS = input.positionOS.xyz;

                VertexPositionInputs vInput = GetVertexPositionInputs(posOS);
                output.positionCS = vInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                VertexNormalInputs nInput = GetVertexNormalInputs(input.normalOS);
                output.normalWS = (half3)nInput.normalWS;
                output.viewDirWS = (half3)GetWorldSpaceNormalizeViewDir(vInput.positionWS);

                return output;
            }

            half4 TrailFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                TrailInstanceData data = _TrailBuffer[input.instanceIndex];

                half4 col = half4(data.color);
                half a = (half)data.alpha;

                half fresnelInt = (half)data.fresnelIntensity;
                half fresnelPow = (half)data.fresnelPower;

                if (_UseTexStamp > 0.5h)
                {
                    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                    // 텍스쳐가 없으면(white) UV 기반 원형 마스크 적용
                    half2 centeredUV = input.uv - 0.5h;
                    half uvDist = dot(centeredUV, centeredUV) * 4.0h;
                    half circleMask = saturate(1.0h - uvDist);

                    // 텍스쳐 알파와 원형 마스크 중 더 작은 값 사용
                    half maskAlpha = min(tex.a, circleMask);
                    clip(maskAlpha - 0.01h);

                    col.rgb *= tex.rgb;
                    col.a *= maskAlpha;
                }
                else
                {
                    half ndotv = saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                    col.rgb += pow(1.0h - ndotv, fresnelPow) * fresnelInt;
                }

                col.a *= a;
                return col;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}

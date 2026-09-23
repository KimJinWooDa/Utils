Shader "TelleR/Trail"
{
    // 인스턴스별 색/알파/프레넬은 MaterialPropertyBlock 배열(_TrailColor, _TrailParams)로 받는다.
    // StructuredBuffer를 쓰지 않으므로 컴퓨트 셰이더가 없는 WebGL2/GLES3에서도 동작한다.
    // SubShader 순서: URP(패키지가 설치된 경우에만 컴파일) → Built-in(항상). Fallback은 두지 않는다.
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

    // ─────────────────────────────────────────────
    //  URP
    // ─────────────────────────────────────────────
    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal"
        }

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
            #pragma target 3.5
            #pragma vertex TrailVert
            #pragma fragment TrailFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half3  viewDirWS  : TEXCOORD2;
                half4  color      : TEXCOORD3;
                half4  params     : TEXCOORD4;   // x=alpha, y=fresnelPower, z=fresnelIntensity
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

            UNITY_INSTANCING_BUFFER_START(TrailProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TrailColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TrailParams)
            UNITY_INSTANCING_BUFFER_END(TrailProps)

            Varyings TrailVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS;
                float3 normalWS;
                if (_UseTexStamp > 0.5)
                {
                    // 빌보드: 인스턴스 행렬의 위치·배율만 쓰고 방향은 현재 렌더 중인 카메라(뷰 행렬)에서 가져온다.
                    // SceneView·분할 화면·VR 양안 모두 각자 카메라를 향한다.
                    float4x4 objectToWorld = GetObjectToWorldMatrix();
                    float4x4 worldToView = GetWorldToViewMatrix();
                    float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                    float scale = length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20));
                    float3 camRight = worldToView[0].xyz;
                    float3 camUp = worldToView[1].xyz;
                    positionWS = centerWS + (camRight * input.positionOS.x + camUp * input.positionOS.y) * scale;
                    normalWS = worldToView[2].xyz;
                }
                else
                {
                    positionWS = TransformObjectToWorld(input.positionOS.xyz);
                    normalWS = TransformObjectToWorldNormal(input.normalOS);
                }

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = (half3)normalWS;
                // 직교 카메라는 화면 전체가 같은 시선 방향 (URP 12+ 함수가 원근/직교를 모두 처리)
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
                output.color = (half4)UNITY_ACCESS_INSTANCED_PROP(TrailProps, _TrailColor);
                output.params = (half4)UNITY_ACCESS_INSTANCED_PROP(TrailProps, _TrailParams);
                return output;
            }

            half4 TrailFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 col = input.color;

                if (_UseTexStamp > 0.5h)
                {
                    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                    // 텍스쳐가 없으면(white) UV 기반 원형 마스크 적용
                    half2 centeredUV = (half2)input.uv - 0.5h;
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
                    col.rgb += pow(1.0h - ndotv, input.params.y) * input.params.z;
                }

                col.a *= input.params.x;
                return col;
            }
            ENDHLSL
        }
    }

    // ─────────────────────────────────────────────
    //  Built-in Render Pipeline (URP가 없거나 URP 에셋이 설정되지 않은 프로젝트)
    // ─────────────────────────────────────────────
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
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

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex TrailVert
            #pragma fragment TrailFrag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half3  viewDirWS  : TEXCOORD2;
                half4  color      : TEXCOORD3;
                half4  params     : TEXCOORD4;   // x=alpha, y=fresnelPower, z=fresnelIntensity
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4  _Color;
            half   _Alpha;
            half   _FresnelPower;
            half   _FresnelIntensity;
            half   _UseTexStamp;

            UNITY_INSTANCING_BUFFER_START(TrailProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TrailColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TrailParams)
            UNITY_INSTANCING_BUFFER_END(TrailProps)

            Varyings TrailVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS;
                float3 normalWS;
                if (_UseTexStamp > 0.5)
                {
                    // 빌보드: 방향은 현재 렌더 중인 카메라(뷰 행렬)에서 가져온다.
                    float3 centerWS = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                    float scale = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                    float3 camRight = UNITY_MATRIX_V[0].xyz;
                    float3 camUp = UNITY_MATRIX_V[1].xyz;
                    positionWS = centerWS + (camRight * input.positionOS.x + camUp * input.positionOS.y) * scale;
                    normalWS = UNITY_MATRIX_V[2].xyz;
                }
                else
                {
                    positionWS = mul(unity_ObjectToWorld, float4(input.positionOS.xyz, 1)).xyz;
                    normalWS = UnityObjectToWorldNormal(input.normalOS);
                }

                output.positionCS = UnityWorldToClipPos(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = (half3)normalWS;
                // 직교 카메라는 화면 전체가 같은 시선 방향(뷰 행렬의 Z축, 카메라 쪽)을 쓴다
                float3 viewDirWS = unity_OrthoParams.w > 0.5
                    ? UNITY_MATRIX_V[2].xyz
                    : normalize(_WorldSpaceCameraPos.xyz - positionWS);
                output.viewDirWS = (half3)viewDirWS;
                output.color = (half4)UNITY_ACCESS_INSTANCED_PROP(TrailProps, _TrailColor);
                output.params = (half4)UNITY_ACCESS_INSTANCED_PROP(TrailProps, _TrailParams);
                return output;
            }

            half4 TrailFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 col = input.color;

                if (_UseTexStamp > 0.5)
                {
                    half4 tex = tex2D(_MainTex, input.uv);

                    // 텍스쳐가 없으면(white) UV 기반 원형 마스크 적용
                    half2 centeredUV = (half2)input.uv - 0.5;
                    half uvDist = dot(centeredUV, centeredUV) * 4.0;
                    half circleMask = saturate(1.0 - uvDist);

                    // 텍스쳐 알파와 원형 마스크 중 더 작은 값 사용
                    half maskAlpha = min(tex.a, circleMask);
                    clip(maskAlpha - 0.01);

                    col.rgb *= tex.rgb;
                    col.a *= maskAlpha;
                }
                else
                {
                    half ndotv = saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                    col.rgb += pow(1.0 - ndotv, input.params.y) * input.params.z;
                }

                col.a *= input.params.x;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}

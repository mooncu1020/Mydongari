// 소행성 맵 배경 행성 (T2): 해(메인 라이트) 방향으로 계단 툰 셰이딩 + 가장자리 대기 림 라이트.
// 아주 멀리 있어 그림자를 만들거나 받지 않는다. 색·림은 머티리얼 값(맵 생성 메뉴가 AsteroidArenaConfig로 채움).
Shader "MyDongari/ToonPlanet"
{
    Properties
    {
        _BaseColor ("밝은 쪽 색", Color) = (0.32, 0.45, 0.62, 1)
        _ShadowColor ("어두운 쪽 색", Color) = (0.05, 0.07, 0.12, 1)
        _AtmosphereColor ("대기 림 색", Color) = (0.45, 0.7, 1, 1)
        _RimIntensity ("대기 림 세기", Float) = 1.6
        _RimPower ("대기 림 좁기 (클수록 가장자리에만)", Float) = 3
        _ShadeSteps ("명암 계단 수", Range(1, 6)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ToonPlanet"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _AtmosphereColor;
                float _RimIntensity;
                float _RimPower;
                float _ShadeSteps;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                Light sun = GetMainLight();
                float lit = saturate(dot(normal, sun.direction) * 0.5 + 0.5);
                float steps = max(1.0, _ShadeSteps);
                float toon = floor(lit * steps) / steps;
                float3 color = lerp(_ShadowColor.rgb, _BaseColor.rgb, toon);

                // 대기 림: 시선과 거의 수직인 가장자리, 해가 비추는 쪽에서 더 밝게
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rim = pow(1.0 - saturate(dot(normal, view)), max(0.5, _RimPower)) * _RimIntensity;
                color += _AtmosphereColor.rgb * rim * saturate(lit + 0.2);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}

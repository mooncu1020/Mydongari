// 화면 전체 외곽선 (S3). URP FullScreenPassRendererFeature가 카메라마다 그린다 (화면 분할 두 카메라 모두).
// 깊이(선형 거리 상대 차이)와 노멀(각도 차이)로 모서리를 찾아 선 색을 섞는다. 하늘(깊이 = 먼 평면) 픽셀엔 선을 그리지 않는다 —
// 기체 실루엣 선은 기체 쪽 픽셀에 그려진다. 굵기·색·민감도는 이 셰이더를 쓰는 머티리얼(M_ScreenOutline) 값.
Shader "Hidden/MyDongari/ScreenOutline"
{
    Properties
    {
        _OutlineColor ("선 색 (알파 = 진하기)", Color) = (0.06, 0.06, 0.08, 1)
        _OutlineThickness ("선 굵기 (px, 화면 높이 1080 기준)", Range(0.5, 4)) = 1.5
        _DepthSensitivity ("깊이 민감도 (클수록 작은 깊이 차이에도 선)", Range(0, 1)) = 0.5
        _NormalSensitivity ("노멀 민감도 (클수록 완만한 꺾임에도 선)", Range(0, 1)) = 0.5
        _FadeStart ("이 거리(m)부터 선이 옅어짐", Float) = 40
        _FadeRange ("옅어지는 구간(m)", Float) = 40
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "ScreenOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineThickness;
                float _DepthSensitivity;
                float _NormalSensitivity;
                float _FadeStart;
                float _FadeRange;
            CBUFFER_END

            // 깊이 버퍼가 비어 있는(하늘·스카이박스) 픽셀인지
            bool IsSky(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth <= 1e-5;
            #else
                return rawDepth >= 1.0 - 1e-5;
            #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                float rawCenter = SampleSceneDepth(uv);
                // 하늘엔 선을 그리지 않는다
                if (IsSky(rawCenter)) return color;

                float depthCenter = LinearEyeDepth(rawCenter, _ZBufferParams);
                float3 normalCenter = SampleSceneNormals(uv);

                // 굵기는 이 카메라 화면 높이에 비례 (화면 분할이어도 같은 두께로 보이게)
                float2 texel = _ScreenSize.zw * max(0.5, _OutlineThickness * _ScreenSize.y / 1080.0);
                float2 offsets[4] = { float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1) };

                float depthEdge = 0;
                float normalEdge = 0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float2 uvN = uv + offsets[i] * texel;
                    float rawN = SampleSceneDepth(uvN);
                    // 이웃이 하늘이면 실루엣 — 깊이 차이를 최대로
                    float depthN = IsSky(rawN) ? depthCenter * 100.0 : LinearEyeDepth(rawN, _ZBufferParams);
                    depthEdge = max(depthEdge, abs(depthN - depthCenter) / max(depthCenter, 0.001));
                    float3 normalN = SampleSceneNormals(uvN);
                    normalEdge = max(normalEdge, 1.0 - saturate(dot(normalCenter, normalN)));
                }

                // 민감도 0 → 큰 차이에만, 1 → 아주 작은 차이에도
                float depthThreshold = lerp(0.25, 0.01, saturate(_DepthSensitivity));
                float normalThreshold = lerp(0.7, 0.05, saturate(_NormalSensitivity));
                float edge = max(step(depthThreshold, depthEdge), step(normalThreshold, normalEdge));

                // 먼 곳은 선이 옅어진다 (배경이 선으로 지저분해지지 않게)
                edge *= 1.0 - saturate((depthCenter - _FadeStart) / max(_FadeRange, 0.01));

                color.rgb = lerp(color.rgb, _OutlineColor.rgb, edge * _OutlineColor.a);
                return color;
            }
            ENDHLSL
        }
    }
}

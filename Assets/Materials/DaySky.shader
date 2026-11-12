// 벨포레스트 하늘 (V7b): 밝은 낮 하늘(지평선 → 천정 그라데이션) + 해 원반 + 갑판 아래 구름층. 텍스처 없음.
// 지평선 아래를 보면 아래쪽 평면에 깔린 구름(노이즈)이 보인다 — 구름 위에 뜬 공중 항모.
// 스카이박스는 깊이를 쓰지 않아 화면 외곽선(ScreenOutline)이 "하늘"로 보고 선을 그리지 않는다.
Shader "MyDongari/DaySky"
{
    Properties
    {
        _ZenithColor ("천정 색", Color) = (0.20, 0.47, 0.90, 1)
        _HorizonColor ("지평선 색", Color) = (0.78, 0.90, 1.0, 1)
        _SkyExponent ("그라데이션 굽음 (작을수록 지평선 색이 좁다)", Range(0.1, 2)) = 0.55
        _SunColor ("해 색", Color) = (1.0, 0.97, 0.88, 1)
        _SunSize ("해 크기 (각도 코사인 기준)", Range(0.0001, 0.01)) = 0.0012
        _SunGlow ("해 주변 번짐", Range(0, 1)) = 0.25
        _CloudColor ("구름 밝은 색", Color) = (1.0, 1.0, 1.0, 1)
        _CloudShadeColor ("구름 그늘 색", Color) = (0.72, 0.80, 0.92, 1)
        _CloudGapColor ("구름 사이로 보이는 아래쪽 색", Color) = (0.40, 0.62, 0.90, 1)
        _CloudScale ("구름 크기 (클수록 잘다)", Float) = 0.9
        _CloudCoverage ("구름 양 (작을수록 많다)", Range(0, 1)) = 0.42
        _CloudSoftness ("구름 가장자리 부드러움", Range(0.01, 0.5)) = 0.12
        _HorizonFade ("지평선 근처 구름이 흐려지는 폭", Range(0.01, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                float _SkyExponent;
                half4 _SunColor;
                float _SunSize;
                float _SunGlow;
                half4 _CloudColor;
                half4 _CloudShadeColor;
                half4 _CloudGapColor;
                float _CloudScale;
                float _CloudCoverage;
                float _CloudSoftness;
                float _HorizonFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    sum += ValueNoise(p) * amplitude;
                    p = p * 2.03 + float2(17.3, 9.1);
                    amplitude *= 0.5;
                }
                return sum / 0.9375;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float height = direction.y;

                // 위쪽: 지평선 → 천정
                half3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(height), _SkyExponent));

                // 해: 메인 라이트 방향의 원반 + 번짐
                float sunDot = dot(direction, _MainLightPosition.xyz);
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.5, sunDot);
                float glow = pow(saturate(sunDot), 64.0) * _SunGlow;
                color = lerp(color, _SunColor.rgb, saturate(disc + glow) * step(0.0, height));

                // 아래쪽: 시선을 아래 평면에 내려 구름 무늬를 읽는다. 지평선 가까이는 지평선 색으로 흐려진다
                if (height < 0.0)
                {
                    float2 uv = direction.xz / max(-height, 0.02) * _CloudScale;
                    float n = Fbm(uv);
                    float cover = smoothstep(_CloudCoverage - _CloudSoftness, _CloudCoverage + _CloudSoftness, n);
                    half3 cloud = lerp(_CloudShadeColor.rgb, _CloudColor.rgb, smoothstep(_CloudCoverage, 1.0, n + 0.25));
                    half3 below = lerp(_CloudGapColor.rgb, cloud, cover);
                    color = lerp(_HorizonColor.rgb, below, saturate(-height / _HorizonFade));
                }
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}

// 소행성 맵 하늘 (T2): 검은 배경 + 해시로 만든 별(크기·밝기·색 다양) + 옅은 은하수 띠. 텍스처 없음.
// 스카이박스는 깊이를 쓰지 않아 화면 외곽선(ScreenOutline)이 "하늘"로 보고 선을 그리지 않는다.
Shader "MyDongari/AsteroidSky"
{
    Properties
    {
        _StarDensity ("별 격자 밀도 (클수록 별이 작고 많음)", Float) = 140
        _StarChance ("칸마다 별이 있을 확률", Range(0, 1)) = 0.12
        _StarSize ("별 크기 (칸 비율)", Range(0.02, 0.5)) = 0.18
        _StarBrightness ("별 밝기", Float) = 1.6
        _StarColorVariation ("별 색 편차 (푸른 별 ~ 붉은 별)", Range(0, 1)) = 0.35
        _MilkyColor ("은하수 색", Color) = (0.55, 0.6, 0.8, 1)
        _MilkyIntensity ("은하수 세기", Range(0, 1)) = 0.12
        _MilkyWidth ("은하수 폭", Range(0.02, 1)) = 0.22
        _MilkyAxis ("은하수 축 (띠에 수직인 방향)", Vector) = (0.3, 0.85, 0.4, 0)
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
                float _StarDensity;
                float _StarChance;
                float _StarSize;
                float _StarBrightness;
                float _StarColorVariation;
                half4 _MilkyColor;
                float _MilkyIntensity;
                float _MilkyWidth;
                float4 _MilkyAxis;
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

            float3 Hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123);
            }

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i), n100 = Hash13(i + float3(1, 0, 0)), n010 = Hash13(i + float3(0, 1, 0)), n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1)), n101 = Hash13(i + float3(1, 0, 1)), n011 = Hash13(i + float3(0, 1, 1)), n111 = Hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0;
                float amplitude = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    sum += ValueNoise(p) * amplitude;
                    p *= 2.03;
                    amplitude *= 0.5;
                }
                return sum;
            }

            // 방향을 3D 격자로 나눠 칸마다 확률로 별 하나 (칸 안 위치·크기·밝기·색은 칸 해시)
            float3 StarLayer(float3 direction, float density, float chance)
            {
                float3 p = direction * density;
                float3 cell = floor(p);
                float3 local = frac(p);
                float3 h = Hash33(cell);
                float present = step(h.x, chance);
                float3 center = 0.25 + 0.5 * Hash33(cell + 17.0);
                float size = _StarSize * lerp(0.3, 1.0, h.y * h.y);
                float brightness = pow(h.z, 3.0) * _StarBrightness + 0.15;
                float star = present * brightness * smoothstep(size, 0.0, length(local - center));
                // 푸른 별 ~ 흰 별 ~ 붉은 별
                float temperature = (h.y - 0.5) * 2.0 * _StarColorVariation;
                float3 tint = float3(1.0 + temperature * 0.4, 1.0, 1.0 - temperature * 0.4);
                return star * tint;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);

                // 크고 드문 별 + 작고 많은 별 두 겹
                float3 color = StarLayer(direction, _StarDensity, _StarChance);
                color += StarLayer(direction, _StarDensity * 2.3, _StarChance * 1.5) * 0.6;

                // 은하수: 축에 수직인 큰 원을 따라 옅은 구름 띠
                float3 axis = normalize(_MilkyAxis.xyz + float3(0, 0, 0.0001));
                float d = dot(direction, axis);
                float band = exp(-(d * d) / max(_MilkyWidth * _MilkyWidth, 0.0001));
                float clouds = Fbm(direction * 6.0) * Fbm(direction * 13.0 + 3.1);
                color += _MilkyColor.rgb * _MilkyIntensity * band * saturate(clouds * 2.5);

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}

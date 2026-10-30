using UnityEngine;

namespace MyDongari.Feedback
{
    /// <summary>
    /// 타격 이펙트(불꽃 + 섬광)와 착지 먼지를 코드로 만든 ParticleSystem으로 띄운다 (N1). 프리팹·텍스처 에셋 없음.
    /// 히트스톱(timeScale 0) 중에도 불꽃이 튀도록 실제 시간으로 돈다. 다 끝나면 스스로 지운다.
    /// 머티리얼은 빌드에 항상 들어가는 Sprites/Default + 코드로 만든 둥근 텍스처 하나를 같이 쓴다.
    /// </summary>
    public static class HitSparkFx
    {
        private static Material _material;
        private static Texture2D _softDot;

        /// <summary>파티클 공용 모양·크기 수치 (BernardTuning.HitFeel에서 넘겨받는다).</summary>
        public struct Style
        {
            public Color Spark;
            public Color Flash;
            public float SparkSize;
            public int SparkCount;
            public float SparkSpeed;
            public float FlashSize;
            public float FlashTime;
        }

        public struct DustStyle
        {
            public Color Color;
            public float Size;
            public int Count;
            public float Speed;
            public float Lifetime;
            /// <summary>중력 배율 (1 = Physics.gravity 그대로).</summary>
            public float Gravity;
            /// <summary>위로 튀는 원뿔의 반각(°). 0 = 똑바로 위, 90 = 바닥과 평행.</summary>
            public float ConeAngle;
            /// <summary>바닥·바위에 닿으면 튕기고 멈춘다.</summary>
            public bool Collide;
        }

        /// <summary>타격 지점에 불꽃(direction 쪽으로 튐) + 섬광. scale = 등급 배율.</summary>
        public static void SpawnHit(Vector3 position, Vector3 direction, float scale, Style style)
        {
            if (scale <= 0f) return;
            var root = new GameObject("HitSparkFx");
            root.transform.position = position;
            root.transform.rotation = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity;

            // 불꽃: 원뿔로 튀어 나가 길게 늘어지며 떨어진다
            ParticleSystem sparks = Create(root, "Sparks");
            var main = sparks.main;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(style.SparkSpeed * 0.5f * scale, style.SparkSpeed * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(style.SparkSize * 0.6f * scale, style.SparkSize * scale);
            main.startColor = style.Spark;
            main.gravityModifier = 1.5f;
            var emission = sparks.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, Mathf.RoundToInt(style.SparkCount * scale))) });
            var shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.05f * scale;
            FadeOut(sparks);
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.03f;
            renderer.lengthScale = 1f;
            sparks.Play();

            // 섬광: 큰 점 하나가 아주 잠깐
            ParticleSystem flash = Create(root, "Flash");
            var flashMain = flash.main;
            flashMain.duration = style.FlashTime;
            flashMain.startLifetime = Mathf.Max(0.02f, style.FlashTime);
            flashMain.startSpeed = 0f;
            flashMain.startSize = style.FlashSize * scale;
            flashMain.startColor = style.Flash;
            var flashEmission = flash.emission;
            flashEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            FadeOut(flash);
            flash.Play();

            Object.Destroy(root, 1.5f);
        }

        /// <summary>
        /// 진공 먼지 (T3): 공기가 없어 퍼지며 부풀거나 떠다니지 않는다 — 알갱이가 발 주변에서 위·바깥으로 튀어
        /// 공기 저항 없이(속도 감쇠 모듈 끔) 중력만 받아 포물선으로 떨어지고, 바닥에 닿으면 조금 튀고 멈춘다. strength = 0~1.
        /// </summary>
        public static void SpawnDust(Vector3 position, float strength, DustStyle style)
        {
            if (strength <= 0f) return;
            var root = new GameObject("DustFx");
            root.transform.position = position;
            ParticleSystem dust = Create(root, "Dust");
            var main = dust.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(style.Lifetime * 0.7f, style.Lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(style.Speed * 0.5f * strength, style.Speed * strength);
            main.startSize = new ParticleSystem.MinMaxCurve(style.Size * 0.5f, style.Size);
            main.startColor = style.Color;
            main.gravityModifier = style.Gravity;
            var emission = dust.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, Mathf.RoundToInt(style.Count * strength))) });
            var shape = dust.shape;
            shape.enabled = true;
            // 위를 향한 원뿔(기본 원뿔은 +Z라 X축으로 -90° 돌림)에서 위·바깥으로
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = Mathf.Clamp(style.ConeAngle, 0f, 89f);
            shape.radius = 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            // 공기 저항 0: 속도 제한·감쇠(LimitVelocityOverLifetime)는 쓰지 않는다(기본 꺼짐). 크기도 그대로(부풀지 않음)
            if (style.Collide)
            {
                var collision = dust.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.quality = ParticleSystemCollisionQuality.Low;
                collision.dampen = 0.5f;
                collision.bounce = 0.15f;
                collision.lifetimeLoss = 0.35f;
                collision.radiusScale = 0.5f;
            }
            FadeOut(dust);
            dust.Play();
            Object.Destroy(root, style.Lifetime + 0.5f);
        }

        /// <summary>계속 뿜는 이펙트(스러스터 분사·발밑 불꽃) 모양.</summary>
        public struct LoopStyle
        {
            public Color Color;
            public float Size;
            public float Speed;
            public float Lifetime;
            public float ConeAngle;
            public float Gravity;
            public bool Stretch;
        }

        /// <summary>
        /// 계속 뿜는 이펙트를 하나 만든다 (O1). 처음엔 0개/초 — 쓰는 쪽이 매 프레임 emission.rateOverTime과 위치·방향을 정하고,
        /// 한꺼번에 뿜을 땐 Emit. 입자는 월드 공간이라 기체가 움직이면 뒤로 꼬리가 남는다. 다 쓰면 쓰는 쪽이 지운다.
        /// </summary>
        public static ParticleSystem CreateLoop(string name, LoopStyle style)
        {
            var root = new GameObject(name);
            ParticleSystem ps = Create(root, "Particles");
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            // 히트스톱 동안은 같이 멈춘다 (게임 시간)
            main.useUnscaledTime = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(style.Lifetime * 0.6f, style.Lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(style.Speed * 0.6f, style.Speed);
            main.startSize = new ParticleSystem.MinMaxCurve(style.Size * 0.6f, style.Size);
            main.startColor = style.Color;
            main.gravityModifier = style.Gravity;
            main.maxParticles = 512;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = style.ConeAngle;
            shape.radius = 0.05f;
            FadeOut(ps);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            if (style.Stretch)
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.03f;
                renderer.lengthScale = 1f;
            }
            ps.Play();
            return ps;
        }

        private static ParticleSystem Create(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            // 붙이는 순간 기본 설정으로 재생이 시작되므로 멈추고 비운 뒤 설정한다 (재생 중엔 duration을 못 바꾼다)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 128;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static void FadeOut(ParticleSystem ps)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }

        private static Material SharedMaterial
        {
            get
            {
                if (_material != null) return _material;
                // Sprites/Default는 빌드에 항상 포함되는 셰이더(GraphicsSettings)라 런타임 생성 머티리얼에 쓴다 (AnchorClaw와 같음)
                _material = new Material(Shader.Find("Sprites/Default")) { name = "HitSparkFx (runtime)" };
                _material.mainTexture = SoftDot;
                return _material;
            }
        }

        /// <summary>가장자리가 부드러운 흰 원 (네모난 파티클이 안 보이게).</summary>
        private static Texture2D SoftDot
        {
            get
            {
                if (_softDot != null) return _softDot;
                const int size = 32;
                _softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SoftDot (runtime)", wrapMode = TextureWrapMode.Clamp };
                float half = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                        float a = Mathf.Clamp01(1f - d);
                        _softDot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                }
                _softDot.Apply();
                return _softDot;
            }
        }
    }
}

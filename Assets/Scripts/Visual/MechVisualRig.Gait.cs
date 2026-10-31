using System;
using UnityEngine;
using MyDongari.Feedback;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>
    /// 이동 다리 모션 (N3 → O1 → P2, 하체 뼈가 있는 모델 + 전신 모션 + Gait.cadenceGait). 외형만 — 물리 이동 속도는 그대로.
    /// 상태가 아니라 실제 수평 속도로 고른다 (경계 ± speedBlendHalfWidth에서 섞음). 전력질주 상태만 따로 호버.
    ///   0 ~ strideStartSpeed(3m/s): 진짜 걸음. 보폭 walkStride(1.0m, 다리 최대 보폭까지), 체공 없음, 디딘 발 고정.
    ///   ~ leapStartSpeed(12m/s): 스러스터 디딤걸음. 초당 걸음 수 strideMaxCadence(2.5)까지, 다리로 못 닿는 거리는 착지한 발이
    ///     지면을 긁으며 미끄러져(몸 속도 → 0) 멈추는 것으로 채운다. 걸음마다 등 스러스터 짧은 분사, 미끄러지는 동안 발밑 불꽃, 착지 먼지.
    ///   leapStartSpeed 이상: 스러스터 보조 도약 (O1 달리기).
    ///   전력질주 상태: 호버 활주 (O1).
    /// 걸음·디딤걸음은 같은 다리 계산(StepLeg)을 쓰고 디딤 구간의 발 위치 곡선만 다르다. 엉덩이 각도는 발목 앞뒤 위치에서 역산.
    /// </summary>
    public partial class MechVisualRig
    {
        [Header("디버그")]
        [Tooltip("시작할 때 다리 길이·최대 보폭·속도 구간별 걸음 수·미끄러짐 거리를 콘솔에 한 번 출력")]
        [SerializeField] private bool logGaitNumbers;

        private BernardTuning.GaitValues G => _tuning.Get(this).Gait;
        private bool UseCadenceGait => UseMotions && G.cadenceGait;

        private float _hoverMode;
        // 걸음·디딤걸음 위상(0~1, 한 주기 = 두 걸음). 걸음은 _gaitPhase를 쓴다
        private float _stridePhase;
        private bool _strideWasSwingL, _strideWasSwingR;
        private float _leapPhase;
        private bool _leapWasAir;
        private bool _leapActive;
        private float _hoverTime;
        // 착지 출렁임 깊이 (걸음·디딤걸음 중 주된 쪽)
        private float _landDepthScale;
        // 디딤걸음에서 발이 지면을 긁는 정도 (0~1, 미끄러지는 속도 ÷ 몸 속도 × 디딤걸음 비중)
        private float _scrapeL, _scrapeR;

        /// <summary>디딤걸음에서 발이 지면을 긁는 정도(0~1). 긁는 소리 루프용.</summary>
        public float ScrapeAmount => Mathf.Max(_scrapeL, _scrapeR);

        // 스러스터·발밑 불꽃 (처음 쓸 때 만든다. 기체 자식이 아니라 월드에 — 틴트·접지 측정에 섞이지 않게)
        private ParticleSystem _thrusterFx, _sparkFxL, _sparkFxR;

        /// <summary>달리기 도약에서 박차는 순간 (스러스터 분사 위치). 효과음용.</summary>
        public event Action<Vector3> OnThrusterBurst;

        /// <summary>엉덩이 → 발목 길이(m), 무릎 굽힘 반영.</summary>
        private float HipToAnkle(float kneeDeg)
        {
            float a = (_thighL + _thighR) * 0.5f;
            float b = (_shinLenL + _shinLenR) * 0.5f;
            if (a < 0.01f || b < 0.01f) return Mathf.Max(0.3f, (_legLengthL + _legLengthR) * 0.5f);
            float theta = kneeDeg * Mathf.Deg2Rad;
            return Mathf.Sqrt(a * a + b * b + 2f * a * b * Mathf.Cos(theta));
        }

        /// <summary>기본 자세(LowGuard) 포즈의 무릎 굽힘(°) — 걸음의 유효 다리 길이 계산용.</summary>
        private float StanceKneeDeg()
        {
            MechPoseLibrary.Baked baked = PoseLibrary.Get(PoseNames.LowGuard, false);
            if (baked == null) return 0f;
            float l = baked.Has[MechPoseLibrary.ShinLIndex] ? baked.Rotation[MechPoseLibrary.ShinLIndex].x : 0f;
            float r = baked.Has[MechPoseLibrary.ShinRIndex] ? baked.Rotation[MechPoseLibrary.ShinRIndex].x : 0f;
            return (l + r) * 0.5f;
        }

        private float MaxStride(BernardTuning.GaitValues g, float kneeBend)
        {
            return 2f * HipToAnkle(StanceKneeDeg() + kneeBend) * Mathf.Sin(g.maxStrideAngle * Mathf.Deg2Rad);
        }

        /// <summary>속도가 경계 edge를 넘어간 정도(0~1). edge ± halfWidth에서 부드럽게.</summary>
        private static float SpeedBand(float speed, float edge, float halfWidth)
        {
            if (halfWidth <= 0f) return speed >= edge ? 1f : 0f;
            return Mathf.SmoothStep(0f, 1f, (speed - (edge - halfWidth)) / (2f * halfWidth));
        }

        private void PoseGaitCadence(BernardTuning.VisualValues v, float dt)
        {
            BernardTuning.GaitValues g = G;
            // 넉다운 중엔 걸음·도약·호버·스러스터 없음 (날아가는 속도로 다리가 뛰지 않게)
            if (IsKnockedDownVisual)
            {
                _gaitWeight = 0f;
                _hoverMode = 0f;
                _leapActive = false;
                _scrapeL = 0f;
                _scrapeR = 0f;
                UpdateMoveFx(g, 0f, 0f, false);
                return;
            }
            Vector3 velocity = HorizontalVelocity();
            float speed = velocity.magnitude;
            bool moving = speed >= v.gaitMinSpeed;
            float fade = moving ? v.gaitFadeInTime : v.stopBlendTime;
            _gaitWeight = fade > 0f ? Mathf.MoveTowards(_gaitWeight, moving ? 1f : 0f, dt / fade) : (moving ? 1f : 0f);
            float gw = _gaitWeight;

            PoseAccelLean(v, dt, velocity);

            // ○ 대시 상태만 호버 활주(상태로), 나머지는 실제 수평 속도로
            bool sprinting = _stateMachine != null && _stateMachine.IsDashMotion;
            _hoverMode = g.modeBlendTime > 0f
                ? Mathf.MoveTowards(_hoverMode, sprinting ? 1f : 0f, dt / g.modeBlendTime)
                : (sprinting ? 1f : 0f);
            float ground = gw * (1f - _hoverMode);
            float toStride = SpeedBand(speed, g.strideStartSpeed, g.speedBlendHalfWidth);
            float toLeap = SpeedBand(speed, g.leapStartSpeed, g.speedBlendHalfWidth);
            float walk = ground * (1f - toStride);
            float stride = ground * toStride * (1f - toLeap);
            float leap = ground * toLeap;

            // 발소리·착지 연출은 더 큰 쪽 하나만 (섞이는 구간에서 두 번 울리지 않게)
            PoseStepping(v, g, dt, speed, velocity, walk, slide: false, events: walk >= stride);
            PoseStepping(v, g, dt, speed, velocity, stride, slide: true, events: stride > walk);
            PoseLeap(g, dt, leap);
            PoseHover(g, dt, gw * _hoverMode);
            UpdateMoveFx(g, leap, gw * _hoverMode, stride > 0f);

            PoseTurnStep(v);
            PoseSkirts(v);
        }

        // ---------------- 걸음 · 스러스터 디딤걸음 ----------------

        /// <summary>
        /// 걸음(slide = false) 또는 디딤걸음(slide = true) 한 벌을 w만큼 더한다. 두 벌은 위상이 따로라 섞여도 서로 끊기지 않는다.
        /// 걸음: 보폭 walkStride(다리 최대까지), 걸음 수 = 속도 ÷ 보폭. 디딤걸음: 걸음 수 = min(strideMaxCadence, 속도 ÷ 다리 최대 보폭),
        /// 보폭 = min(다리 최대, 속도 × 한 걸음 시간) — 남는 거리는 디딤 구간에서 미끄러진다.
        /// </summary>
        private void PoseStepping(BernardTuning.VisualValues v, BernardTuning.GaitValues g, float dt, float speed, Vector3 velocity,
            float w, bool slide, bool events)
        {
            if (slide)
            {
                _scrapeL = 0f;
                _scrapeR = 0f;
            }
            if (w <= 0.001f) return;

            float kneeBend = slide ? g.strideKneeBend : g.walkKneeBend;
            float baseKnee = StanceKneeDeg() + kneeBend * w;
            float leg = HipToAnkle(baseKnee);
            float maxStride = 2f * leg * Mathf.Sin(g.maxStrideAngle * Mathf.Deg2Rad);
            float cadence, stride;
            if (!slide)
            {
                // 체공 없는 걸음: 보폭은 다리가 닿는 만큼까지만, 걸음 수가 속도에 맞춰진다
                stride = Mathf.Max(0.1f, Mathf.Min(g.walkStride, maxStride));
                cadence = speed / stride;
            }
            else
            {
                cadence = Mathf.Clamp(speed / Mathf.Max(0.1f, maxStride), 0.5f, Mathf.Max(0.5f, g.strideMaxCadence));
                stride = Mathf.Min(maxStride, speed / cadence);
            }

            ref float phase = ref slide ? ref _stridePhase : ref _gaitPhase;
            phase = Mathf.Repeat(phase + cadence * dt * 0.5f, 1f);
            if (events) _landDepthScale = slide ? g.strideLandDrop : g.walkLandDrop;

            // 하체 정면 기준으로 앞으로 가는지(뒷걸음이면 스윙 방향을 뒤집는다)
            Vector3 pelvisForward = Quaternion.Euler(0f, _pelvisYaw, 0f) * Vector3.forward;
            float dir = Vector3.Dot(velocity, pelvisForward) >= 0f ? 1f : -1f;

            var step = new StepParams
            {
                Speed = speed,
                StepTime = 1f / Mathf.Max(0.01f, cadence),
                Stride = stride,
                Leg = leg,
                KneeBend = kneeBend,
                BaseKnee = baseKnee,
                SwingKnee = slide ? g.strideSwingKnee : g.walkSwingKnee,
                Slide = slide,
                Dir = dir,
                Weight = w,
                Events = events
            };
            float sigL = StepLeg(v, g, true, phase, step, out float bumpL, out float scrapeL);
            StepLeg(v, g, false, Mathf.Repeat(phase + 0.5f, 1f), step, out float bumpR, out float scrapeR);
            if (slide)
            {
                _scrapeL = scrapeL * w;
                _scrapeR = scrapeR * w;
            }

            // 골반·어깨 반대 비틀림: 왼발이 앞이면 골반은 오른쪽으로(Y+), 어깨는 반대로. Body는 Pelvis 자식이라 상대값
            float body = slide ? g.strideBodyScale : 1f;
            float pelvisTwist = g.walkPelvisTwist * body * sigL * w * dir;
            float shoulderTwist = g.walkShoulderTwist * body * sigL * w * dir;
            AddRotation(_pelvis, new Vector3(0f, pelvisTwist, 0f));
            AddRotation(_body, new Vector3(0f, -(pelvisTwist + shoulderTwist), 0f));
            // 로우 가드 팔이 어깨를 따라: 오른 어깨가 앞이면 오른팔 앞(X-), 왼팔 뒤(X+)
            float armSwing = g.armShoulderSwing * body * sigL * w * dir * ArmSwingScale;
            AddRotation(_armRUpper, new Vector3(-armSwing, 0f, 0f));
            AddRotation(_armLUpper, new Vector3(armSwing, 0f, 0f));

            // 디딘 발 쪽으로 골반 옆 이동(+ = 왼발 디딤 → 기체 왼쪽)과 기울임(왼쪽 = Z+)
            float lateral = (bumpL - bumpR) * w;
            if (_pelvis != null)
            {
                _pelvis.DirectPosition += Quaternion.Euler(0f, _pelvis.Yaw, 0f) * new Vector3(-lateral * g.walkSideShift * body, 0f, 0f);
                AddRotation(_pelvis, new Vector3(0f, 0f, lateral * g.walkSideTilt * body));
            }

            // 착지 출렁임: 상체는 골반보다 늦게 내려갔다 올라온다
            if (events)
            {
                float drop = LandBounceAt(g, _clock);
                float lagged = LandBounceAt(g, _clock - v.landBodyLag);
                AddPosition(_body, new Vector3(0f, (drop - lagged) * w, 0f));
            }

            // 머리 수평: 몸의 비틀림·좌우 기울임을 되돌린다 (Head는 Body 자식)
            AddRotation(_head, new Vector3(0f, shoulderTwist, -lateral * g.walkSideTilt * body) * g.headStabilize);
        }

        private struct StepParams
        {
            public float Speed;
            public float StepTime;
            public float Stride;
            public float Leg;
            public float KneeBend;
            public float BaseKnee;
            public float SwingKnee;
            public bool Slide;
            public float Dir;
            public float Weight;
            public bool Events;
        }

        /// <summary>
        /// 다리 하나 (u = 이 다리 위상 0~1, 앞 절반 스윙·뒤 절반 디딤). 발목 앞뒤 위치 x를 정하고 엉덩이 = asin(x / 유효 길이)로 역산.
        /// 걸음: 디딤 동안 x가 몸 속도로 일정하게 뒤로 → 미끄러지지 않음.
        /// 디딤걸음: 착지 순간 발이 몸과 같은 속도로 지면을 긁다가(x 거의 그대로) 점점 느려져 멈추고, 마지막 stridePlantFraction 동안은 고정.
        ///   디딤 동안 x가 움직이는 거리는 보폭과 같고, 몸이 간 나머지 거리가 미끄러진 거리다.
        /// 반환 = 발 앞뒤 정도(-1~1), bump = 디딤 중 무게가 실리는 정도, scrape = 지면을 긁는 속도 ÷ 몸 속도(0~1).
        /// </summary>
        private float StepLeg(BernardTuning.VisualValues v, BernardTuning.GaitValues g, bool left, float u, StepParams s,
            out float bump, out float scrape)
        {
            Joint thigh = left ? _legL : _legR;
            Joint shin = left ? _shinL : _shinR;
            float w = s.Weight;
            float half = s.Stride * 0.5f;
            bool swinging = u < 0.5f;
            float x, knee;
            scrape = 0f;
            if (swinging)
            {
                float p = u * 2f;
                x = Mathf.Lerp(-half, half, Mathf.SmoothStep(0f, 1f, p));
                // 들기는 천천히, 정점을 지나면 빠르게 내려놓는다 (G3와 같은 모양). 0·1이면 0으로 나누므로 자른다 (Q2)
                float peak = Mathf.Clamp(v.swingLiftPeak, 0.05f, 0.95f);
                float lift = p < peak ? (p / peak) * (p / peak) : 1f - (p - peak) / (1f - peak);
                knee = s.SwingKnee * lift;
                bump = 0f;
            }
            else
            {
                float p = (u - 0.5f) * 2f;
                x = s.Slide ? half - SlideTravel(g, s, p, out scrape) : Mathf.Lerp(half, -half, p);
                knee = 0f;
                bump = Mathf.Sin(Mathf.PI * p);
            }

            float hipDeg = Mathf.Asin(Mathf.Clamp(x / Mathf.Max(0.05f, s.Leg), -0.99f, 0.99f)) * Mathf.Rad2Deg;
            // 엉덩이 앞으로 = X-. 무릎을 굽힌 만큼 허벅지를 조금 앞으로 내서 골반이 수직으로 내려가게
            AddRotation(thigh, new Vector3((-hipDeg * s.Dir - s.KneeBend * 0.5f) * w, 0f, 0f));
            AddRotation(shin, new Vector3((knee + s.KneeBend) * w, 0f, 0f));

            ref bool wasSwinging = ref s.Slide ? ref (left ? ref _strideWasSwingL : ref _strideWasSwingR) : ref (left ? ref _wasSwingL : ref _wasSwingR);
            if (wasSwinging && !swinging && s.Events && w > 0.05f)
            {
                if (s.Slide) OnStrideLanded(g, left, w);
                else Land(left, w * g.walkStepIntensity);
            }
            wasSwinging = swinging;

            // 착지 출렁임: 방금 디딘 다리 무릎으로 내려앉았다(+) 밀고 올라온다(-)
            if (s.Events && !swinging && _landLeft == left)
            {
                float drop = LandBounceAt(g, _clock);
                if (Mathf.Abs(drop) > 0.0001f) AddRotation(shin, new Vector3(KneeBendForDrop(left, s.BaseKnee, drop) * w, 0f, 0f));
            }
            return half > 0.0001f ? x / half : 0f;
        }

        /// <summary>
        /// 디딤걸음 디딤 구간(p 0~1)에서 발목이 몸 기준으로 뒤로 간 거리(0 ~ 보폭).
        /// 끝 stridePlantFraction 동안은 고정(몸 속도로 뒤로), 그 앞은 착지 미끄러짐: 발이 지면에 대해 몸 속도로 시작해
        /// 고정 순간 0이 되도록 몸 기준 속도가 0 → 몸 속도로 커진다(거듭제곱 곡선, 지수는 거리가 맞도록 계산).
        /// </summary>
        private static float SlideTravel(BernardTuning.GaitValues g, StepParams s, float p, out float scrape)
        {
            float speed = Mathf.Max(0.01f, s.Speed);
            float stanceTime = s.StepTime;
            float t = p * stanceTime;
            float plant = Mathf.Min(speed * g.stridePlantFraction * stanceTime, s.Stride * 0.9f);
            float slideDistance = s.Stride - plant;
            float slideTime = stanceTime - plant / speed;
            if (slideDistance <= 0.0001f || slideTime <= 0.0001f || t >= slideTime)
            {
                scrape = 0f;
                return Mathf.Min(s.Stride, slideDistance + speed * Mathf.Max(0f, t - slideTime));
            }
            // 거리 d(t) = L (t/T)^k, 몸 기준 속도 = speed (t/T)^(k-1) → t = T에서 몸 속도(고정)와 이어진다
            float k = Mathf.Max(1f, speed * slideTime / slideDistance);
            float r = t / slideTime;
            scrape = 1f - Mathf.Pow(r, k - 1f);
            return slideDistance * Mathf.Pow(r, k);
        }

        /// <summary>디딤걸음 착지: 출렁임·작은 흔들림, 등 스러스터 짧은 분사, 발밑 먼지(+ 디딤걸음 발소리, CombatFeedback).</summary>
        private void OnStrideLanded(BernardTuning.GaitValues g, bool left, float w)
        {
            _landTime = _clock;
            _landLeft = left;
            _landIntensity = w;
            ShakeCameras(g.strideLandShake * w, 0.12f, bothPlayers: false);
            if (_thrusterFx != null) _thrusterFx.Emit(Mathf.Max(0, Mathf.RoundToInt(g.strideBurstCount * w)));
            Transform foot = left ? _footL : _footR;
            Vector3[] sole = left ? _soleL : _soleR;
            OnMotionImpact?.Invoke("StrideLand", foot != null ? SolePoint(foot, sole) : transform.position, w * g.strideDustStrength);
        }

        /// <summary>착지 후 time 시점 골반 높이 변화(m, + = 내려앉음): 앞부분은 내려앉고, 뒷부분은 밀고 올라온다(-).</summary>
        private float LandBounceAt(BernardTuning.GaitValues g, float time)
        {
            float t = time - _landTime;
            float total = g.walkBounceTime;
            if (total <= 0f || t < 0f || t >= total) return 0f;
            float depth = _landDepthScale * _landIntensity;
            float compress = total * g.bounceCompressRatio;
            if (t < compress) return depth * Mathf.Sin(Mathf.PI * t / compress);
            return -depth * g.bouncePushRatio * Mathf.Sin(Mathf.PI * (t - compress) / Mathf.Max(0.0001f, total - compress));
        }

        // ---------------- 스러스터 보조 도약 (12m/s 이상) ----------------

        /// <summary>
        /// 위상 0 = 착지. [0, leapContact) 땅(착지 RunLeapLand → 박차기 RunLeapPush), 그 뒤 공중(RunLeapAir, 골반 뜸·접지 보정 멈춤) → 1에서 다시 착지 자세.
        /// 마지막 키가 첫 키와 같은 포즈라 도약이 이어져도 튀지 않는다 (왼발 앞 고정).
        /// </summary>
        private void PoseLeap(BernardTuning.GaitValues g, float dt, float w)
        {
            if (w <= 0.001f)
            {
                _leapActive = false;
                return;
            }
            float contact = Mathf.Clamp(g.leapContact, 0.05f, 0.6f);
            if (!_leapActive)
            {
                // 도약 구간에 들어서면 박차기부터
                _leapActive = true;
                _leapPhase = contact * 0.6f;
                _leapWasAir = false;
            }
            _leapPhase += dt * Mathf.Max(0.1f, g.leapRate);
            if (_leapPhase >= 1f) _leapPhase -= 1f;
            bool inAir = _leapPhase >= contact;

            // 섞이는 구간에선 도약이 주된 쪽일 때만 착지·박차기 연출
            bool events = w >= 0.5f * _gaitWeight;
            if (events && !inAir && _leapWasAir) OnLeapLanded(g, w);
            if (events && inAir && !_leapWasAir) OnLeapPushed(g);
            _leapWasAir = inAir;

            BeginMotion();
            Key(0f, PoseNames.RunLeapLand);
            Key(contact, PoseNames.RunLeapPush, MechPoseLibrary.EaseSnap);
            Key(contact + (1f - contact) * 0.35f, PoseNames.RunLeapAir, MechPoseLibrary.EaseSmooth);
            Key(0.85f, PoseNames.RunLeapAir, MechPoseLibrary.EaseLinear);
            Key(1f, PoseNames.RunLeapLand, MechPoseLibrary.EaseIn);
            PlayMotion(_leapPhase, w, false);

            if (inAir)
            {
                float fp = (_leapPhase - contact) / (1f - contact);
                // 섞이는 구간에서 다른 모션 발이 땅을 디디고 있으면 접지 보정은 그대로 둔다
                if (events) _groundingHold = true;
                if (_pelvis != null) _pelvis.DirectPosition += new Vector3(0f, g.leapHeight * Mathf.Sin(Mathf.PI * fp) * w, 0f);
            }
        }

        private void OnLeapLanded(BernardTuning.GaitValues g, float w)
        {
            // 발소리·약한 흔들림(OnFootstep) + 착지 흔들림 + 앞발 먼지
            Land(true, w);
            ShakeCameras(g.leapLandShake * w, g.leapLandShakeTime, bothPlayers: false);
            OnMotionImpact?.Invoke("RunLand", _footL != null ? _footL.position : transform.position, w);
        }

        private void OnLeapPushed(BernardTuning.GaitValues g)
        {
            if (_thrusterFx == null) return;
            _thrusterFx.Emit(Mathf.Max(0, g.leapBurstCount));
            OnThrusterBurst?.Invoke(_thrusterFx.transform.position);
        }

        // ---------------- 전력질주: 호버 활주 ----------------

        private void PoseHover(BernardTuning.GaitValues g, float dt, float w)
        {
            if (w <= 0.001f) return;
            _hoverTime += dt;
            BeginMotion();
            Key(0f, PoseNames.HoverGlide);
            PlayMotion(0f, w, false);

            // 가끔 좌우 체중 이동: 불규칙한 느린 흔들림 (-1~1)
            float sway = (Mathf.PerlinNoise(_hoverTime * g.hoverSwayFrequency, 0.37f) * 2f - 1f) * w;
            if (_pelvis != null)
            {
                _pelvis.DirectPosition += Quaternion.Euler(0f, _pelvis.Yaw, 0f) * new Vector3(-sway * g.hoverSwayShift, 0f, 0f);
                AddRotation(_pelvis, new Vector3(0f, 0f, sway * g.hoverSwayTilt));
            }
            AddRotation(_head, new Vector3(0f, 0f, -sway * g.hoverSwayTilt * g.headStabilize));
        }

        // ---------------- 스러스터·발밑 불꽃 ----------------

        private void UpdateMoveFx(BernardTuning.GaitValues g, float leap, float hover, bool striding)
        {
            if (_body == null || _body.Target == null) return;
            if (_thrusterFx == null && leap <= 0f && hover <= 0f && !striding) return;
            EnsureMoveFx(g);

            // 분사구: Body 뼈를 따라가는 기체 축 기준 오프셋, 뒤·약간 아래로 뿜는다
            Quaternion bodyFrame = _body.Target.rotation * _body.BoneFromMech;
            // 뼈가 잘못된 프레임이면 이펙트 위치를 건드리지 않는다 (Q2)
            if (!IsFinite(_body.Target.position) || !IsFinite(new Vector3(bodyFrame.x, bodyFrame.y, bodyFrame.z)) || !IsFinite(bodyFrame.w)) return;
            _thrusterFx.transform.SetPositionAndRotation(
                _body.Target.position + bodyFrame * g.thrusterOffset,
                Quaternion.LookRotation(bodyFrame * new Vector3(0f, -0.3f, -1f)));
            SetRate(_thrusterFx, g.runThrusterRate * leap + g.sprintThrusterRate * hover);

            // 발밑 불꽃: 발바닥 근처에서 뒤로 튄다 (호버 중, 디딤걸음에서 지면을 긁는 동안)
            Vector3 back = -(Quaternion.Euler(0f, _pelvisYaw, 0f) * Vector3.forward);
            Quaternion sparkRotation = Quaternion.LookRotation(back + Vector3.up * 0.3f);
            if (_footL != null) _sparkFxL.transform.SetPositionAndRotation(SolePoint(_footL, _soleL), sparkRotation);
            if (_footR != null) _sparkFxR.transform.SetPositionAndRotation(SolePoint(_footR, _soleR), sparkRotation);
            SetRate(_sparkFxL, g.footSparkRate * hover + g.strideSparkRate * _scrapeL);
            SetRate(_sparkFxR, g.footSparkRate * hover + g.strideSparkRate * _scrapeR);
        }

        private static Vector3 SolePoint(Transform foot, Vector3[] sole)
        {
            if (!TrySoleHeight(foot, sole, out float y)) return foot.position;
            Vector3 p = foot.position;
            p.y = y;
            return p;
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }

        private void EnsureMoveFx(BernardTuning.GaitValues g)
        {
            if (_thrusterFx != null) return;
            _thrusterFx = HitSparkFx.CreateLoop($"{name}_ThrusterFx", new HitSparkFx.LoopStyle
            {
                Color = g.thrusterColor,
                Size = g.thrusterSize,
                Speed = g.thrusterSpeed,
                Lifetime = g.thrusterLifetime,
                ConeAngle = 12f,
                Gravity = 0f,
                Stretch = false
            });
            var spark = new HitSparkFx.LoopStyle
            {
                Color = g.footSparkColor,
                Size = g.footSparkSize,
                Speed = g.footSparkSpeed,
                Lifetime = 0.25f,
                ConeAngle = 30f,
                Gravity = 1.5f,
                Stretch = true
            };
            _sparkFxL = HitSparkFx.CreateLoop($"{name}_FootSparkL", spark);
            _sparkFxR = HitSparkFx.CreateLoop($"{name}_FootSparkR", spark);
        }

        /// <summary>기체가 꺼지거나(모드 전환) 지워질 때 이펙트도 같이.</summary>
        private void SetMoveFxActive(bool active)
        {
            if (!active)
            {
                _scrapeL = 0f;
                _scrapeR = 0f;
            }
            foreach (ParticleSystem ps in new[] { _thrusterFx, _sparkFxL, _sparkFxR })
            {
                if (ps == null) continue;
                SetRate(ps, 0f);
                ps.transform.parent.gameObject.SetActive(active);
            }
        }

        private void DestroyMoveFx()
        {
            foreach (ParticleSystem ps in new[] { _thrusterFx, _sparkFxL, _sparkFxR })
            {
                if (ps != null) Destroy(ps.transform.parent.gameObject);
            }
        }

        /// <summary>확인용: 다리 길이·최대 보폭·속도 구간별 걸음 수·미끄러짐 거리 (logGaitNumbers).</summary>
        private void LogGaitNumbers()
        {
            if (!logGaitNumbers || !HasLowerBody) return;
            BernardTuning tuning = _tuning.Get(this);
            BernardTuning.GaitValues g = tuning.Gait;
            BernardTuning.MovementValues move = tuning.Movement;
            float walkMax = MaxStride(g, g.walkKneeBend);
            float strideMax = MaxStride(g, g.strideKneeBend);
            float walkStride = Mathf.Min(g.walkStride, walkMax);
            string text = $"[MechVisualRig] {name} 이동: 엉덩이~발바닥 {(_legLengthL + _legLengthR) * 0.5f:0.00}m, 허벅지 {(_thighL + _thighR) * 0.5f:0.00}m, " +
                          $"정강이 {(_shinLenL + _shinLenR) * 0.5f:0.00}m, 최대 보폭 걸음 {walkMax:0.00}m / 디딤걸음 {strideMax:0.00}m" +
                          $"\n  걸음(~{g.strideStartSpeed:0.#}m/s): 보폭 {walkStride:0.00}m, {g.strideStartSpeed:0.#}m/s에서 {g.strideStartSpeed / walkStride:0.0}걸음/초";
            foreach (float speed in new[] { g.strideStartSpeed, g.leapStartSpeed })
            {
                float cadence = Mathf.Clamp(speed / strideMax, 0.5f, g.strideMaxCadence);
                float stepDistance = speed / cadence;
                text += $"\n  디딤걸음 {speed:0.#}m/s: {cadence:0.0}걸음/초, 걸음당 {stepDistance:0.00}m 중 미끄러짐 {Mathf.Max(0f, stepDistance - strideMax):0.00}m";
            }
            text += $"\n  도약({g.leapStartSpeed:0.#}m/s~): {g.leapRate:0.0}회/초, 앞 걷기 {move.walkSpeed.front:0.#}m/s에서 한 번에 {move.walkSpeed.front / g.leapRate:0.0}m" +
                    $"\n  전력질주 상태: 호버 활주 (걸음 없음)";
            Debug.Log(text, this);
        }
    }
}

using System;
using UnityEngine;
using MyDongari.Feedback;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>
    /// 상태별 이동 모션 (N3 → O1, 하체 뼈가 있는 모델 + 전신 모션 + Gait.cadenceGait). 외형만 — 물리 이동 속도는 그대로.
    /// 걷기: 체공 없는 걸음. 보폭 walkStride(다리 최대 보폭보다 길면 줄임), 초당 걸음 수 = 속도 ÷ 보폭.
    ///   디딘 발목은 몸 속도로 뒤로 가도록 엉덩이 각도를 역산해 미끄러지지 않는다. 골반 낮춤·골반/어깨 반대 비틀림·옆 이동·착지 출렁임·머리 수평.
    /// 달리기: 스러스터 보조 도약. 초당 leapRate번, 착지(무릎 깊게·먼지·발소리·흔들림) → 박차기(등 스러스터 분사) → 낮고 긴 공중(다리 앞뒤로 벌림).
    ///   땅에 닿는 짧은 동안은 미끄러지며 착지한다(스러스터로 미는 기체라 의도).
    /// 전력질주: 호버 활주. 걸음 없음, 두 발을 땅에 붙인 채 앞뒤로 엇갈린 자세로 미끄러지고, 발밑 불꽃·스러스터 최대 분사, 가끔 좌우 체중 이동.
    /// 세 방식은 modeBlendTime에 걸쳐 섞인다.
    /// </summary>
    public partial class MechVisualRig
    {
        [Header("디버그")]
        [Tooltip("시작할 때 다리 길이·최대 보폭·걷기 걸음 수·달리기 도약 거리를 콘솔에 한 번 출력")]
        [SerializeField] private bool logGaitNumbers;

        private BernardTuning.GaitValues G => _tuning.Get(this).Gait;
        private bool UseCadenceGait => UseMotions && G.cadenceGait;

        // 이동 방식 섞기 (걷기·도약·호버, 합 ≤ 1)
        private float _walkMode = 1f, _leapMode, _hoverMode;
        private float _leapPhase;
        private bool _leapWasAir;
        private bool _leapActive;
        private float _hoverTime;
        // 착지 출렁임 깊이·시간 (걷기)
        private float _landDepthScale;

        // 스러스터·발밑 불꽃 (처음 쓸 때 만든다. 기체 자식이 아니라 월드에 — 틴트·접지 측정에 섞이지 않게)
        private ParticleSystem _thrusterFx, _sparkFxL, _sparkFxR;

        /// <summary>달리기에서 박차는 순간 (스러스터 분사 위치). 효과음용.</summary>
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

        /// <summary>0 걷기, 1 달리기, 2 전력질주. 상태 머신이 없는 봇은 걷기만.</summary>
        private int MoveMode()
        {
            if (_stateMachine == null) return 0;
            if (_stateMachine.IsSprinting) return 2;
            return _stateMachine.IsRunning ? 1 : 0;
        }

        private void PoseGaitCadence(BernardTuning.VisualValues v, float dt)
        {
            BernardTuning.GaitValues g = G;
            Vector3 velocity = HorizontalVelocity();
            float speed = velocity.magnitude;
            bool moving = speed >= v.gaitMinSpeed;
            float fade = moving ? v.gaitFadeInTime : v.stopBlendTime;
            _gaitWeight = fade > 0f ? Mathf.MoveTowards(_gaitWeight, moving ? 1f : 0f, dt / fade) : (moving ? 1f : 0f);
            float gw = _gaitWeight;

            PoseAccelLean(v, dt, velocity);

            int mode = MoveMode();
            float step = g.modeBlendTime > 0f ? dt / g.modeBlendTime : 1f;
            _walkMode = Mathf.MoveTowards(_walkMode, mode == 0 ? 1f : 0f, step);
            _leapMode = Mathf.MoveTowards(_leapMode, mode == 1 ? 1f : 0f, step);
            _hoverMode = Mathf.MoveTowards(_hoverMode, mode == 2 ? 1f : 0f, step);

            PoseWalk(v, g, dt, speed, velocity, gw * _walkMode);
            PoseLeap(g, dt, gw * _leapMode);
            PoseHover(g, dt, gw * _hoverMode);
            UpdateMoveFx(g, gw * _leapMode, gw * _hoverMode);

            PoseTurnStep(v);
            PoseSkirts(v);
        }

        // ---------------- 걷기 ----------------

        private void PoseWalk(BernardTuning.VisualValues v, BernardTuning.GaitValues g, float dt, float speed, Vector3 velocity, float w)
        {
            if (w <= 0.001f) return;
            float baseKnee = StanceKneeDeg() + g.walkKneeBend * w;
            float leg = HipToAnkle(baseKnee);
            float maxStride = 2f * leg * Mathf.Sin(g.maxStrideAngle * Mathf.Deg2Rad);
            // 체공 없는 걸음: 보폭은 다리가 닿는 만큼까지만, 걸음 수가 속도에 맞춰진다
            float stride = Mathf.Max(0.1f, Mathf.Min(g.walkStride, maxStride));
            float cadence = speed / stride;
            _gaitPhase = Mathf.Repeat(_gaitPhase + cadence * dt * 0.5f, 1f);
            _landDepthScale = g.walkLandDrop;

            // 하체 정면 기준으로 앞으로 가는지(뒷걸음이면 스윙 방향을 뒤집는다)
            Vector3 pelvisForward = Quaternion.Euler(0f, _pelvisYaw, 0f) * Vector3.forward;
            float dir = Vector3.Dot(velocity, pelvisForward) >= 0f ? 1f : -1f;

            float sigL = WalkLeg(v, g, true, _gaitPhase, stride, leg, baseKnee, dir, w, out float bumpL);
            WalkLeg(v, g, false, Mathf.Repeat(_gaitPhase + 0.5f, 1f), stride, leg, baseKnee, dir, w, out float bumpR);

            // 골반·어깨 반대 비틀림: 왼발이 앞이면 골반은 오른쪽으로(Y+), 어깨는 반대로. Body는 Pelvis 자식이라 상대값
            float pelvisTwist = g.walkPelvisTwist * sigL * w * dir;
            float shoulderTwist = g.walkShoulderTwist * sigL * w * dir;
            AddRotation(_pelvis, new Vector3(0f, pelvisTwist, 0f));
            AddRotation(_body, new Vector3(0f, -(pelvisTwist + shoulderTwist), 0f));
            // 로우 가드 팔이 어깨를 따라: 오른 어깨가 앞이면 오른팔 앞(X-), 왼팔 뒤(X+)
            float armSwing = g.armShoulderSwing * sigL * w * dir * ArmSwingScale;
            AddRotation(_armRUpper, new Vector3(-armSwing, 0f, 0f));
            AddRotation(_armLUpper, new Vector3(armSwing, 0f, 0f));

            // 디딘 발 쪽으로 골반 옆 이동(+ = 왼발 디딤 → 기체 왼쪽)과 기울임(왼쪽 = Z+)
            float lateral = (bumpL - bumpR) * w;
            if (_pelvis != null)
            {
                _pelvis.DirectPosition += Quaternion.Euler(0f, _pelvis.Yaw, 0f) * new Vector3(-lateral * g.walkSideShift, 0f, 0f);
                AddRotation(_pelvis, new Vector3(0f, 0f, lateral * g.walkSideTilt));
            }

            // 착지 출렁임: 상체는 골반보다 늦게 내려갔다 올라온다
            float drop = LandBounceAt(g, _clock);
            float lagged = LandBounceAt(g, _clock - v.landBodyLag);
            AddPosition(_body, new Vector3(0f, (drop - lagged) * w, 0f));

            // 머리 수평: 몸의 비틀림·좌우 기울임을 되돌린다 (Head는 Body 자식)
            AddRotation(_head, new Vector3(0f, shoulderTwist, -lateral * g.walkSideTilt) * g.headStabilize);
        }

        /// <summary>
        /// 걷기 다리 하나 (u = 이 다리 위상 0~1, 앞 절반 스윙·뒤 절반 디딤). 발목 앞뒤 위치 x를 정하고 엉덩이 = asin(x / 유효 길이)로 역산 —
        /// 디딤 동안 x가 몸 속도로 일정하게 뒤로 가서 미끄러지지 않는다. 반환 = 발 앞뒤 정도(-1~1), bump = 디딤 중 무게가 실리는 정도.
        /// </summary>
        private float WalkLeg(BernardTuning.VisualValues v, BernardTuning.GaitValues g, bool left, float u, float stride,
            float leg, float baseKnee, float dir, float w, out float bump)
        {
            Joint thigh = left ? _legL : _legR;
            Joint shin = left ? _shinL : _shinR;
            float half = stride * 0.5f;
            bool swinging = u < 0.5f;
            float x, knee;
            if (swinging)
            {
                float s = u * 2f;
                x = Mathf.Lerp(-half, half, Mathf.SmoothStep(0f, 1f, s));
                // 들기는 천천히, 정점을 지나면 빠르게 내려놓는다 (G3와 같은 모양)
                float peak = v.swingLiftPeak;
                float lift = s < peak ? (s / peak) * (s / peak) : 1f - (s - peak) / (1f - peak);
                knee = g.walkSwingKnee * lift;
                bump = 0f;
            }
            else
            {
                float p = (u - 0.5f) * 2f;
                x = Mathf.Lerp(half, -half, p);
                knee = 0f;
                bump = Mathf.Sin(Mathf.PI * p);
            }

            float hipDeg = Mathf.Asin(Mathf.Clamp(x / Mathf.Max(0.05f, leg), -0.99f, 0.99f)) * Mathf.Rad2Deg;
            // 엉덩이 앞으로 = X-. 무릎을 굽힌 만큼 허벅지를 조금 앞으로 내서 골반이 수직으로 내려가게
            AddRotation(thigh, new Vector3((-hipDeg * dir - g.walkKneeBend * 0.5f) * w, 0f, 0f));
            AddRotation(shin, new Vector3((knee + g.walkKneeBend) * w, 0f, 0f));

            bool wasSwinging = left ? _wasSwingL : _wasSwingR;
            if (wasSwinging && !swinging && w > 0.05f) Land(left, w * g.walkStepIntensity);
            if (left) _wasSwingL = swinging;
            else _wasSwingR = swinging;

            // 착지 출렁임: 방금 디딘 다리 무릎으로 내려앉았다(+) 밀고 올라온다(-)
            if (!swinging && _landLeft == left)
            {
                float drop = LandBounceAt(g, _clock);
                if (Mathf.Abs(drop) > 0.0001f) AddRotation(shin, new Vector3(KneeBendForDrop(left, baseKnee, drop) * w, 0f, 0f));
            }
            return half > 0.0001f ? x / half : 0f;
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

        // ---------------- 달리기: 스러스터 보조 도약 ----------------

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
                // 달리기에 들어서면 박차기부터 (서 있다가 뛰어오르는 첫 도약)
                _leapActive = true;
                _leapPhase = contact * 0.6f;
                _leapWasAir = false;
            }
            _leapPhase += dt * Mathf.Max(0.1f, g.leapRate);
            if (_leapPhase >= 1f) _leapPhase -= 1f;
            bool inAir = _leapPhase >= contact;

            if (!inAir && _leapWasAir) OnLeapLanded(g, w);
            if (inAir && !_leapWasAir) OnLeapPushed(g);
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
                _groundingHold = true;
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

        private void UpdateMoveFx(BernardTuning.GaitValues g, float leap, float hover)
        {
            if (_body == null || _body.Target == null) return;
            if (_thrusterFx == null && leap <= 0f && hover <= 0f) return;
            EnsureMoveFx(g);

            // 분사구: Body 뼈를 따라가는 기체 축 기준 오프셋, 뒤·약간 아래로 뿜는다
            Quaternion bodyFrame = _body.Target.rotation * _body.BoneFromMech;
            _thrusterFx.transform.SetPositionAndRotation(
                _body.Target.position + bodyFrame * g.thrusterOffset,
                Quaternion.LookRotation(bodyFrame * new Vector3(0f, -0.3f, -1f)));
            SetRate(_thrusterFx, g.runThrusterRate * leap + g.sprintThrusterRate * hover);

            // 발밑 불꽃: 발바닥 근처에서 뒤로 튄다 (호버 중만)
            Vector3 back = -(Quaternion.Euler(0f, _pelvisYaw, 0f) * Vector3.forward);
            Quaternion sparkRotation = Quaternion.LookRotation(back + Vector3.up * 0.3f);
            if (_footL != null) _sparkFxL.transform.SetPositionAndRotation(SolePoint(_footL, _soleL), sparkRotation);
            if (_footR != null) _sparkFxR.transform.SetPositionAndRotation(SolePoint(_footR, _soleR), sparkRotation);
            SetRate(_sparkFxL, g.footSparkRate * hover);
            SetRate(_sparkFxR, g.footSparkRate * hover);
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

        /// <summary>확인용: 다리 길이·최대 보폭·걷기 걸음 수·달리기 도약 거리 (logGaitNumbers).</summary>
        private void LogGaitNumbers()
        {
            if (!logGaitNumbers || !HasLowerBody) return;
            BernardTuning tuning = _tuning.Get(this);
            BernardTuning.GaitValues g = tuning.Gait;
            BernardTuning.MovementValues move = tuning.Movement;
            float leg = HipToAnkle(StanceKneeDeg() + g.walkKneeBend);
            float maxStride = 2f * leg * Mathf.Sin(g.maxStrideAngle * Mathf.Deg2Rad);
            float stride = Mathf.Min(g.walkStride, maxStride);
            Debug.Log($"[MechVisualRig] {name} 이동: 엉덩이~발바닥 {(_legLengthL + _legLengthR) * 0.5f:0.00}m, 허벅지 {(_thighL + _thighR) * 0.5f:0.00}m, " +
                      $"정강이 {(_shinLenL + _shinLenR) * 0.5f:0.00}m, 걷기 최대 보폭 {maxStride:0.00}m\n" +
                      $"  걷기 {move.maxWalkSpeed:0.#}m/s: 보폭 {stride:0.00}m → {move.maxWalkSpeed / stride:0.0}걸음/초 (체공 없음)\n" +
                      $"  달리기 {move.maxRunSpeed:0.#}m/s: 도약 {g.leapRate:0.0}회/초 → 한 번에 {move.maxRunSpeed / g.leapRate:0.0}m, 공중 {(1f - g.leapContact) * 100f:0}%\n" +
                      $"  전력질주 {move.maxSprintSpeed:0.#}m/s: 호버 활주 (걸음 없음)", this);
        }
    }
}

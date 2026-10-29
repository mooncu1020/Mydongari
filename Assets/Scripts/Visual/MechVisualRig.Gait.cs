using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>
    /// 초당 걸음 수 기준 걸음 (N3, 하체 뼈가 있는 모델 + 전신 모션 + Gait.cadenceGait).
    /// 걸음 수(walk 1.4 / run 2.0 / sprint 2.4 걸음/초)로 위상을 시간에 따라 돌리고, 보폭 = 속도 ÷ 걸음 수.
    /// 디딘 발은 발목이 몸 속도와 똑같이 뒤로 가도록 엉덩이 각도를 발 위치에서 역산한다(미끄러짐 없음).
    /// 다리로 닿을 수 있는 보폭(maxStrideAngle)보다 길면 기본은 걸음마다 짧은 체공으로 나머지 거리를 날아가고(디딤 비율이 0.5 아래로),
    /// 스위치를 바꾸면 걸음 수를 올려 보폭을 맞춘다. 이동 속도 자체는 건드리지 않는다.
    /// 그 위에: 골반을 낮추는 무릎 굽힘, 골반·어깨 반대 비틀림(팔은 어깨를 따라), 디딘 발 쪽 골반 옆 이동·기울임,
    /// 착지 때 내려앉았다 밀고 올라오는 출렁임, 머리 수평 유지.
    /// </summary>
    public partial class MechVisualRig
    {
        [Header("디버그")]
        [Tooltip("시작할 때 다리 길이·최대 보폭·상태별 걸음 수·보폭·체공 비율을 콘솔에 한 번 출력 (N3 확인용)")]
        [SerializeField] private bool logGaitNumbers;

        private BernardTuning.GaitValues G => _tuning.Get(this).Gait;
        private bool UseCadenceGait => UseMotions && G.cadenceGait;

        // 0 = 걷기, 1 = 달리기·전력질주 (출렁임·비틀림 크기 섞기)
        private float _runBlend;
        private float _landDepthScale;

        /// <summary>엉덩이 → 발목 길이(m)와 무릎 굽힘에 따른 유효 길이.</summary>
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

        private float Cadence(BernardTuning.GaitValues g, float speed, out int state)
        {
            // 0 걷기, 1 달리기, 2 전력질주. 상태 머신이 없는 봇은 속도로 걷기·달리기만
            if (_stateMachine != null)
            {
                state = _stateMachine.IsSprinting ? 2 : _stateMachine.IsRunning ? 1 : 0;
            }
            else
            {
                state = speed > V.walkReferenceSpeed * 1.2f ? 1 : 0;
            }
            return state == 2 ? g.sprintCadence : state == 1 ? g.runCadence : g.walkCadence;
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

            float cadence = Cadence(g, speed, out int state);
            _runBlend = g.runBlendTime > 0f ? Mathf.MoveTowards(_runBlend, state > 0 ? 1f : 0f, dt / g.runBlendTime) : (state > 0 ? 1f : 0f);
            float r = Mathf.SmoothStep(0f, 1f, _runBlend);

            float kneeBend = Mathf.Lerp(g.walkKneeBend, g.runKneeBend, r);
            float baseKnee = StanceKneeDeg() + kneeBend * gw;
            float leg = HipToAnkle(baseKnee);
            float maxStride = 2f * leg * Mathf.Sin(g.maxStrideAngle * Mathf.Deg2Rad);

            // 보폭 = 속도 ÷ 걸음 수. 다리로 못 닿으면 체공(디딤 비율 < 0.5) 또는 걸음 수를 올림
            float stride = cadence > 0f ? speed / cadence : 0f;
            if (g.strideMode == BernardTuning.StrideMode.RaiseCadence && stride > maxStride && maxStride > 0.01f)
            {
                cadence = speed / maxStride;
                stride = maxStride;
            }
            float stance = Mathf.Min(stride, maxStride);
            float duty = stride > 0.001f ? Mathf.Clamp(0.5f * stance / stride, 0.05f, 0.5f) : 0.5f;

            if (gw > 0f) _gaitPhase = Mathf.Repeat(_gaitPhase + cadence * dt * 0.5f, 1f);

            // 하체 정면 기준으로 앞으로 가는지(뒷걸음이면 스윙 방향을 뒤집는다)
            Vector3 pelvisForward = Quaternion.Euler(0f, _pelvisYaw, 0f) * Vector3.forward;
            float dir = Vector3.Dot(velocity, pelvisForward) >= 0f ? 1f : -1f;
            float swingKnee = Mathf.Lerp(g.walkSwingKnee, g.runSwingKnee, r);
            _landDepthScale = Mathf.Lerp(g.walkLandDrop, g.runLandDrop, r);

            float sigL = CadenceLeg(v, g, true, _gaitPhase, duty, stance, leg, swingKnee, kneeBend, baseKnee, dir, r, out float bumpL);
            CadenceLeg(v, g, false, Mathf.Repeat(_gaitPhase + 0.5f, 1f), duty, stance, leg, swingKnee, kneeBend, baseKnee, dir, r, out float bumpR);

            // 골반·어깨 반대 비틀림: 왼발이 앞이면 골반은 오른쪽으로(Y+), 어깨는 반대로. Body는 Pelvis 자식이라 상대값
            float pelvisTwist = Mathf.Lerp(g.walkPelvisTwist, g.runPelvisTwist, r) * sigL * gw * dir;
            float shoulderTwist = Mathf.Lerp(g.walkShoulderTwist, g.runShoulderTwist, r) * sigL * gw * dir;
            AddRotation(_pelvis, new Vector3(0f, pelvisTwist, 0f));
            AddRotation(_body, new Vector3(0f, -(pelvisTwist + shoulderTwist), 0f));
            // 로우 가드 팔이 어깨를 따라: 오른 어깨가 앞이면 오른팔 앞(X-), 왼팔 뒤(X+). 돌격 자세면 작게
            float armSwing = g.armShoulderSwing * Mathf.Lerp(1f, g.runArmSwingScale, r) * sigL * gw * dir * ArmSwingScale;
            AddRotation(_armRUpper, new Vector3(-armSwing, 0f, 0f));
            AddRotation(_armLUpper, new Vector3(armSwing, 0f, 0f));

            // 디딘 발 쪽으로 골반 옆 이동(+ = 왼발 디딤 → 기체 왼쪽)과 기울임(왼쪽 = Z+)
            float lateral = (bumpL - bumpR) * gw;
            float side = Mathf.Lerp(g.walkSideShift, g.runSideShift, r);
            float tilt = Mathf.Lerp(g.walkSideTilt, g.runSideTilt, r);
            if (_pelvis != null)
            {
                _pelvis.DirectPosition += Quaternion.Euler(0f, _pelvis.Yaw, 0f) * new Vector3(-lateral * side, 0f, 0f);
                AddRotation(_pelvis, new Vector3(0f, 0f, lateral * tilt));
            }

            // 착지 출렁임: 상체는 골반보다 늦게 내려갔다 올라온다
            float drop = LandBounceAt(g, _clock, r);
            float lagged = LandBounceAt(g, _clock - v.landBodyLag, r);
            AddPosition(_body, new Vector3(0f, drop - lagged, 0f));

            // 체공: 두 발 다 떠 있는 동안 골반이 살짝 뜨고, 접지 보정은 그동안 멈춘다
            float flight = 0.5f - duty;
            if (flight > 0.001f && gw > 0f)
            {
                float fp = Mathf.Repeat(_gaitPhase, 0.5f);
                if (fp < flight)
                {
                    _groundingHold = true;
                    float lift = Mathf.Lerp(g.walkFlightHeight, g.runFlightHeight, r) * Mathf.Sin(Mathf.PI * fp / flight) * gw;
                    if (_pelvis != null) _pelvis.DirectPosition += new Vector3(0f, lift, 0f);
                }
            }

            // 머리 수평: 몸의 비틀림·좌우 기울임을 되돌린다 (Head는 Body 자식)
            AddRotation(_head, new Vector3(0f, shoulderTwist, -lateral * tilt) * g.headStabilize);

            PoseTurnStep(v);
            PoseSkirts(v);
        }

        /// <summary>
        /// 다리 하나 (u = 이 다리 위상 0~1). [0, 1 - duty) 스윙, [1 - duty, 1) 디딤.
        /// 발목의 앞뒤 위치 x를 정하고 엉덩이 각도 = asin(x / 유효 다리 길이)로 역산한다 — 디딤 동안 x는 몸 속도로 일정하게 뒤로 가서 미끄러지지 않는다.
        /// 반환 = 발 앞뒤 정도(-1 뒤 ~ 1 앞), bump = 디딤 중 무게가 실리는 정도(0~1, 디딤 가운데서 최대).
        /// </summary>
        private float CadenceLeg(BernardTuning.VisualValues v, BernardTuning.GaitValues g, bool left, float u, float duty, float stance,
            float leg, float swingKnee, float kneeBend, float baseKnee, float dir, float r, out float bump)
        {
            Joint thigh = left ? _legL : _legR;
            Joint shin = left ? _shinL : _shinR;
            float gw = _gaitWeight;
            float half = stance * 0.5f;
            bool swinging = u < 1f - duty;
            float x, knee;
            if (swinging)
            {
                float s = u / Mathf.Max(0.0001f, 1f - duty);
                x = Mathf.Lerp(-half, half, Mathf.SmoothStep(0f, 1f, s));
                // 들기는 천천히, 정점을 지나면 빠르게 내려놓는다 (G3와 같은 모양)
                float peak = v.swingLiftPeak;
                float lift = s < peak ? (s / peak) * (s / peak) : 1f - (s - peak) / (1f - peak);
                knee = swingKnee * lift;
                bump = 0f;
            }
            else
            {
                float p = (u - (1f - duty)) / Mathf.Max(0.0001f, duty);
                x = Mathf.Lerp(half, -half, p);
                knee = 0f;
                bump = Mathf.Sin(Mathf.PI * p);
            }

            float hipDeg = Mathf.Asin(Mathf.Clamp(x / Mathf.Max(0.05f, leg), -0.99f, 0.99f)) * Mathf.Rad2Deg;
            // 엉덩이 앞으로 = X-. 무릎을 굽힌 만큼 허벅지를 조금 앞으로 내서 골반이 수직으로 내려가게
            AddRotation(thigh, new Vector3((-hipDeg * dir - kneeBend * 0.5f) * gw, 0f, 0f));
            AddRotation(shin, new Vector3((knee + kneeBend) * gw, 0f, 0f));

            bool wasSwinging = left ? _wasSwingL : _wasSwingR;
            if (wasSwinging && !swinging && gw > 0.05f) Land(left, gw * Mathf.Lerp(g.walkStepIntensity, 1f, r));
            if (left) _wasSwingL = swinging;
            else _wasSwingR = swinging;

            // 착지 출렁임: 방금 디딘 다리 무릎으로 내려앉았다(+) 밀고 올라온다(-)
            if (!swinging && _landLeft == left)
            {
                float drop = LandBounceAt(g, _clock, r);
                if (Mathf.Abs(drop) > 0.0001f) AddRotation(shin, new Vector3(KneeBendForDrop(left, baseKnee, drop), 0f, 0f));
            }
            return half > 0.0001f ? x / half : 0f;
        }

        /// <summary>착지 후 time 시점 골반 높이 변화(m, + = 내려앉음): 앞부분은 내려앉고, 뒷부분은 밀고 올라온다(-).</summary>
        private float LandBounceAt(BernardTuning.GaitValues g, float time, float r)
        {
            float t = time - _landTime;
            float total = Mathf.Lerp(g.walkBounceTime, g.runBounceTime, r);
            if (total <= 0f || t < 0f || t >= total) return 0f;
            float depth = _landDepthScale * _landIntensity;
            float compress = total * g.bounceCompressRatio;
            if (t < compress) return depth * Mathf.Sin(Mathf.PI * t / compress);
            return -depth * g.bouncePushRatio * Mathf.Sin(Mathf.PI * (t - compress) / Mathf.Max(0.0001f, total - compress));
        }

        /// <summary>N3 확인용: 다리 길이·최대 보폭·상태별 걸음 수·보폭·체공 비율 (logGaitNumbers).</summary>
        private void LogGaitNumbers()
        {
            if (!logGaitNumbers || !HasLowerBody) return;
            BernardTuning tuning = _tuning.Get(this);
            BernardTuning.GaitValues g = tuning.Gait;
            BernardTuning.MovementValues move = tuning.Movement;
            string text = $"[MechVisualRig] {name} 걸음: 엉덩이~발바닥 {(_legLengthL + _legLengthR) * 0.5f:0.00}m, 허벅지 {(_thighL + _thighR) * 0.5f:0.00}m, 정강이 {(_shinLenL + _shinLenR) * 0.5f:0.00}m";
            float[] speeds = { move.maxWalkSpeed, move.maxRunSpeed, move.maxSprintSpeed };
            float[] cadences = { g.walkCadence, g.runCadence, g.sprintCadence };
            float[] knees = { g.walkKneeBend, g.runKneeBend, g.runKneeBend };
            string[] names = { "걷기", "달리기", "전력질주" };
            for (int i = 0; i < 3; i++)
            {
                float leg = HipToAnkle(StanceKneeDeg() + knees[i]);
                float maxStride = 2f * leg * Mathf.Sin(g.maxStrideAngle * Mathf.Deg2Rad);
                float stride = speeds[i] / cadences[i];
                float duty = Mathf.Clamp(0.5f * Mathf.Min(stride, maxStride) / stride, 0.05f, 0.5f);
                text += $"\n  {names[i]} {speeds[i]:0.#}m/s: {cadences[i]:0.0}걸음/초 → 보폭 {stride:0.00}m, 다리 최대 {maxStride:0.00}m, " +
                        $"체공 {(1f - 2f * duty) * 100f:0}% (걸음 수로 맞추면 {speeds[i] / maxStride:0.0}걸음/초)";
            }
            Debug.Log(text, this);
        }
    }
}

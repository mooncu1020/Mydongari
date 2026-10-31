using System;
using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>
    /// 하체(Pelvis)가 있는 모델(bernard, H1 구조 Root → Pelvis → Body)의 이동 연출.
    /// G2 상하체 분리: 하체는 이동 방향을 묵직하게 따라 돌고, 상체는 카메라(하드 락온이면 상대)를 향한다.
    /// 둘의 비틀림이 한계를 넘으면 하체가 끌려 돈다. 머리는 상체 위에서 한 번 더 늦게 따라본다.
    /// 방향은 전부 월드 yaw(°)로 들고 있다가 기체 루트 기준 오프셋으로 관절 Yaw에 넣는다 (Body는 Pelvis 자식이라 상대 yaw).
    /// G3 무게감 걸음: 위상은 이동 거리로 진행(보폭 = 한 걸음 거리), 발 들기는 느리게·내려놓기는 빠르게,
    /// 착지 때 디딘 다리 무릎을 굽혀 골반을 꺼뜨리고(발은 접지 보정으로 땅에 붙은 채) 상체는 늦게 따라온다.
    /// 하체 뼈가 없는 임시 상자에서는 아무것도 하지 않는다 (예전 동작 그대로).
    /// </summary>
    public partial class MechVisualRig
    {
        private float _pelvisYaw, _bodyYaw, _headYaw;
        private bool _facingReady;
        private Vector3 _lastRootPosition;

        // 걸음 주기 위상(0~1, 1주기 = 두 걸음). 왼발 = 위상, 오른발 = 위상 + 0.5. 0~0.5 = 스윙(발 듦), 0.5~1 = 디딤
        private float _gaitPhase;
        private bool _wasSwingL, _wasSwingR;
        private float _landTime = None;
        private bool _landLeft;
        private float _landIntensity;
        private Vector3 _lastFixedVelocity;
        private Vector3 _physicsAccel;
        private Vector3 _smoothedAccel;
        // 허벅지(엉덩이 → 무릎)·정강이(무릎 → 발목) 길이(m, 쉴 때 자세). 착지 꺼짐 깊이를 무릎 각도로 바꿀 때 쓴다
        private float _thighL, _thighR, _shinLenL, _shinLenR;

        /// <summary>발이 땅에 닿은 순간 (왼발인지, 세기 0~1). 발소리·카메라 흔들림용.</summary>
        public event Action<bool, float> OnFootstep;

        /// <summary>상하체를 나눠 상체 방향을 따로 계산하는 중인지 (공격 방향 = 상체 방향, MechFacing).</summary>
        public bool HasUpperBodyFacing => _facingReady && HasLowerBody && isActiveAndEnabled;
        /// <summary>상체가 향한 수평 방향(월드).</summary>
        public Vector3 UpperBodyForward => Quaternion.Euler(0f, _bodyYaw, 0f) * Vector3.forward;

        private float RootYaw => transform.eulerAngles.y;

        // ---------------- G2 상하체 방향 ----------------

        /// <summary>
        /// 상체가 볼 방향: 대시 중이면 대시 방향(V3.1 — 겉모습만 몸을 튼다, 기체 정면·카메라는 그대로),
        /// 하드 락온이면 상대, 아니면 기체 정면 (V3: 카메라가 몸을 따라오므로 정면 = 몸 정면).
        /// </summary>
        private float LookYaw()
        {
            Vector3 dir;
            if (_stateMachine != null && _stateMachine.IsDashing) dir = _stateMachine.ActiveDash.Heading;
            else if (_lockOn != null && _lockOn.IsHardLocked && _lockOn.Target != null) dir = _lockOn.Target.position - transform.position;
            else dir = transform.forward;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : RootYaw;
        }

        private Vector3 HorizontalVelocity()
        {
            if (_rb == null) return Vector3.zero;
            Vector3 velocity = Movement.MotionBridge.GetVelocity(_rb);
            velocity.y = 0f;
            // 물리가 잘못된 값을 내도 외형 계산으로 퍼지지 않게 (Q2)
            return velocity.x == velocity.x && velocity.z == velocity.z && !float.IsInfinity(velocity.x) && !float.IsInfinity(velocity.z) ? velocity : Vector3.zero;
        }

        private void ResetFacing()
        {
            _turnStepsLeft = 0;
            float yaw = RootYaw;
            _pelvisYaw = yaw;
            _bodyYaw = yaw;
            _headYaw = yaw;
            _lastRootPosition = transform.position;
            _lastFixedVelocity = HorizontalVelocity();
            _physicsAccel = Vector3.zero;
            _smoothedAccel = Vector3.zero;
            _facingReady = true;
        }

        private void UpdateFacing(BernardTuning.VisualValues v, float dt)
        {
            if (!HasLowerBody) return;

            // 처음, 또는 라운드 리셋처럼 순간이동했으면 전부 기체 정면으로 맞춘다 (먼 방향에서 천천히 돌아오지 않게)
            if (!_facingReady || (transform.position - _lastRootPosition).sqrMagnitude > v.facingSnapDistance * v.facingSnapDistance)
            {
                ResetFacing();
            }
            _lastRootPosition = transform.position;

            // 넉다운 중엔 상하체를 기체 정면에 붙인다 (날아가는 속도를 이동 방향으로 보고 하체가 돌지 않게)
            if (IsKnockedDownVisual)
            {
                _pelvisYaw = RootYaw;
                _bodyYaw = RootYaw;
                _headYaw = RootYaw;
                _pelvis.Yaw = 0f;
                if (_body != null) _body.Yaw = 0f;
                if (_head != null) _head.Yaw = 0f;
                return;
            }

            float lookYaw = LookYaw();
            _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, lookYaw, v.upperBodyTurnSpeed * dt);

            // 하체: 움직이면 이동 방향(뒤로 가면 뒷걸음이라 반대 방향), 회전 속도 상한으로 묵직하게.
            // 서 있을 때는 제자리 회전 스텝(G4)이 돌린다
            Vector3 velocity = HorizontalVelocity();
            if (!UpdateTurnSteps(v, dt) && velocity.magnitude >= v.pelvisMoveMinSpeed)
            {
                float moveYaw = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(_bodyYaw, moveYaw)) > v.pelvisBackpedalAngle) moveYaw += 180f;
                _pelvisYaw = Mathf.MoveTowardsAngle(_pelvisYaw, moveYaw, v.pelvisTurnSpeed * dt);
            }

            // 비틀림 한계: 넘으면 하체가 끌려 돈다
            float twist = Mathf.DeltaAngle(_pelvisYaw, _bodyYaw);
            if (Mathf.Abs(twist) > v.twistLimit) _pelvisYaw = _bodyYaw - Mathf.Sign(twist) * v.twistLimit;

            // 머리: 볼 방향을 headLookLag만큼 늦게 따라가고, 상체 기준 ±headLookLimit 안에서만
            float k = v.headLookLag > 0f ? 1f - Mathf.Exp(-dt / v.headLookLag) : 1f;
            _headYaw = Mathf.LerpAngle(_headYaw, lookYaw, k);
            float headOffset = Mathf.Clamp(Mathf.DeltaAngle(_bodyYaw, _headYaw), -v.headLookLimit, v.headLookLimit);

            // 계층이 Root → Pelvis → Body → Head라 각 관절엔 부모 기준 yaw
            _pelvis.Yaw = Mathf.DeltaAngle(RootYaw, _pelvisYaw);
            if (_body != null) _body.Yaw = Mathf.DeltaAngle(_pelvisYaw, _bodyYaw);
            if (_head != null) _head.Yaw = headOffset;
        }

        // ---------------- G4 제자리 회전 스텝 ----------------

        private int _turnStepsLeft;
        private float _turnStepT;
        private float _turnFrom, _turnTo;
        private bool _turnStepLeftLeg;

        /// <summary>
        /// 서 있을 때 상체가 하체와 turnStepTrigger(60°) 이상 벌어지면 하체가 두세 걸음으로 끊어 돌아 따라온다.
        /// 걸음마다 남은 각도를 남은 걸음 수로 나눠 돌고, 도는 쪽 발부터 번갈아 든다. 하체를 돌리는 중이면 true.
        /// 움직이기 시작하면 바로 그만두고 걸음(G3)에 넘긴다.
        /// </summary>
        private bool UpdateTurnSteps(BernardTuning.VisualValues v, float dt)
        {
            bool standing = HorizontalVelocity().magnitude < v.pelvisMoveMinSpeed;
            if (!standing)
            {
                _turnStepsLeft = 0;
                return false;
            }

            if (_turnStepsLeft <= 0)
            {
                float gap = Mathf.DeltaAngle(_pelvisYaw, _bodyYaw);
                if (Mathf.Abs(gap) < v.turnStepTrigger) return false;
                _turnStepsLeft = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(gap) / Mathf.Max(1f, v.turnStepMaxAngle)), 2, 3);
                // 오른쪽(+)으로 돌면 오른발부터
                _turnStepLeftLeg = gap > 0f;
                BeginTurnStep();
            }

            _turnStepT += v.turnStepTime > 0f ? dt / v.turnStepTime : 1f;
            _pelvisYaw = Mathf.LerpAngle(_turnFrom, _turnTo, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, _turnStepT)));
            if (_turnStepT >= 1f)
            {
                Land(_turnStepLeftLeg, v.turnStepIntensity);
                _turnStepsLeft--;
                if (_turnStepsLeft > 0) BeginTurnStep();
            }
            return true;
        }

        private void BeginTurnStep()
        {
            // 남은 각도(그 사이 상체가 더 돌았을 수 있음)를 남은 걸음 수로 나눈다. 발은 번갈아
            float gap = Mathf.DeltaAngle(_pelvisYaw, _bodyYaw);
            _turnFrom = _pelvisYaw;
            _turnTo = _pelvisYaw + gap / Mathf.Max(1, _turnStepsLeft);
            _turnStepLeftLeg = !_turnStepLeftLeg;
            _turnStepT = 0f;
        }

        /// <summary>회전 스텝 중 드는 발: 무릎을 굽히고 엉덩이를 살짝 앞으로 (반 사인으로 들었다 내림).</summary>
        private void PoseTurnStep(BernardTuning.VisualValues v)
        {
            if (_turnStepsLeft <= 0) return;
            float lift = Mathf.Sin(Mathf.PI * Mathf.Clamp01(_turnStepT));
            Joint leg = _turnStepLeftLeg ? _legL : _legR;
            Joint shin = _turnStepLeftLeg ? _shinL : _shinR;
            AddRotation(leg, new Vector3(-v.turnStepHipLift * lift, 0f, 0f));
            AddRotation(shin, new Vector3(v.turnStepKnee * lift, 0f, 0f));
        }

        // ---------------- G3 걸음 ----------------

        private void SetupGait()
        {
            _thighL = BoneDistance(_legL, _shinL);
            _thighR = BoneDistance(_legR, _shinR);
            _shinLenL = BoneDistance(_shinL, _ankleL);
            _shinLenR = BoneDistance(_shinR, _ankleR);
        }

        private static float BoneDistance(Joint a, Joint b)
        {
            if (a == null || b == null || a.Target == null || b.Target == null) return 0f;
            return Vector3.Distance(a.Target.position, b.Target.position);
        }

        /// <summary>지금 상태의 한 걸음 거리. 상태 머신이 없는 봇은 속도로 걷기·달리기만.</summary>
        private float StepLength(BernardTuning.VisualValues v, float speed)
        {
            if (_stateMachine != null)
            {
                if (_stateMachine.IsDashing) return v.sprintStepLength;
                if (_stateMachine.IsRunning) return v.runStepLength;
                return v.walkStepLength;
            }
            return speed > v.walkReferenceSpeed * 1.2f ? v.runStepLength : v.walkStepLength;
        }

        private void PoseGait(BernardTuning.VisualValues v, float dt)
        {
            // 전신 모션 모델은 초당 걸음 수 기준 걸음 (N3). 끄면 아래 예전 거리 기준
            if (UseCadenceGait)
            {
                PoseGaitCadence(v, dt);
                return;
            }
            Vector3 velocity = HorizontalVelocity();
            float speed = velocity.magnitude;
            bool moving = speed >= v.gaitMinSpeed;
            float fade = moving ? v.gaitFadeInTime : v.stopBlendTime;
            float gaitTarget = moving ? 1f : 0f;
            _gaitWeight = fade > 0f ? Mathf.MoveTowards(_gaitWeight, gaitTarget, dt / fade) : gaitTarget;

            PoseAccelLean(v, dt, velocity);

            float step = Mathf.Max(0.1f, StepLength(v, speed));
            // 위상은 시간이 아니라 이동 거리로: 한 주기(두 걸음) = 2 × 보폭
            _gaitPhase = Mathf.Repeat(_gaitPhase + speed * dt / (2f * step), 1f);

            // 하체 정면 기준으로 앞으로 가는지(뒷걸음이면 스윙 방향을 뒤집는다)
            Vector3 pelvisForward = Quaternion.Euler(0f, _pelvisYaw, 0f) * Vector3.forward;
            float dir = Vector3.Dot(velocity, pelvisForward) >= 0f ? 1f : -1f;

            float uL = _gaitPhase;
            float uR = Mathf.Repeat(_gaitPhase + 0.5f, 1f);
            float hipL = PoseLeg(v, true, uL, dir, step);
            float hipR = PoseLeg(v, false, uR, dir, step);

            // 팔: 반대쪽 다리를 작게 따라 흔들고, 팔꿈치는 반 걸음 늦게. 돌격 자세(M2 전력질주)면 주먹을 붙인 채라 더 작게
            float g = _gaitWeight * ArmSwingScale;
            AddRotation(_armLUpper, new Vector3(hipR * v.armSwingRatio * ArmSwingScale, 0f, 0f));
            AddRotation(_armRUpper, new Vector3(hipL * v.armSwingRatio * ArmSwingScale, 0f, 0f));
            AddRotation(_armLLower, new Vector3(-ElbowSwing(v, uR) * g, 0f, 0f));
            AddRotation(_armRLower, new Vector3(-ElbowSwing(v, uL) * g, 0f, 0f));

            // 착지: 상체는 골반보다 landBodyLag 늦게 내려갔다 올라온다(골반은 무릎 굽힘으로 꺼짐), 골반은 디딘 발 쪽으로 기운다
            float drop = LandDropAt(v, _clock);
            float lagged = LandDropAt(v, _clock - v.landBodyLag);
            AddPosition(_body, new Vector3(0f, drop - lagged, 0f));
            if (v.landPelvisDrop > 0f)
            {
                float tilt = (_landLeft ? 1f : -1f) * v.landPelvisTilt * (drop / v.landPelvisDrop);
                AddRotation(_pelvis, new Vector3(0f, 0f, tilt));
            }

            PoseTurnStep(v);
            PoseSkirts(v);
        }

        /// <summary>
        /// 다리 하나의 엉덩이·무릎 각도를 얹고, 스윙 → 디딤으로 넘어가는 순간을 착지로 알린다. 반환 = 엉덩이 각도(팔 스윙용).
        /// 엉덩이 앞으로 = X-, 무릎 굽힘 = 정강이 X+.
        /// </summary>
        private float PoseLeg(BernardTuning.VisualValues v, bool left, float u, float dir, float step)
        {
            Joint leg = left ? _legL : _legR;
            Joint shin = left ? _shinL : _shinR;
            float legLength = left ? _legLengthL : _legLengthR;
            float g = _gaitWeight;

            // 보폭의 절반만큼 발이 앞뒤로 가는 엉덩이 각도
            float amp = legLength > 0.01f
                ? Mathf.Min(v.maxHipSwing, Mathf.Atan2(step * 0.5f, legLength) * Mathf.Rad2Deg)
                : v.maxHipSwing * 0.5f;

            bool swinging = u < 0.5f;
            float hip, knee;
            if (swinging)
            {
                float s = u * 2f;
                hip = Mathf.Lerp(amp, -amp, Mathf.SmoothStep(0f, 1f, s));
                // 들기는 천천히(ease-in), 정점(swingLiftPeak)을 지나면 남은 짧은 구간에 빠르게 내려놓는다. 0·1이면 0으로 나누므로 자른다 (Q2)
                float peak = Mathf.Clamp(v.swingLiftPeak, 0.05f, 0.95f);
                float lift = s < peak ? (s / peak) * (s / peak) : 1f - (s - peak) / (1f - peak);
                knee = v.swingKneeAngle * lift;
            }
            else
            {
                // 디딘 발은 땅에 붙은 채 몸이 지나가므로 앞 → 뒤로 일정하게
                hip = Mathf.Lerp(-amp, amp, (u - 0.5f) * 2f);
                knee = 0f;
            }
            hip *= dir;

            bool wasSwinging = left ? _wasSwingL : _wasSwingR;
            if (wasSwinging && !swinging && g > 0.05f) Land(left, g);
            if (left) _wasSwingL = swinging;
            else _wasSwingR = swinging;

            AddRotation(leg, new Vector3(hip * g, 0f, 0f));
            AddRotation(shin, new Vector3(knee * g, 0f, 0f));

            // 착지 흡수: 방금 디딘 다리 무릎을 골반 꺼짐 깊이만큼 더 굽힌다 (접지 보정이 발을 붙여 두므로 골반이 내려간다).
            // 깊이에 이미 착지 세기가 들어 있어 걸음 세기를 다시 곱하지 않는다
            if (!swinging && _landLeft == left)
            {
                float drop = LandDropAt(v, _clock);
                if (drop > 0f)
                {
                    float baseKnee = knee * g + v.idleKneeBend * (1f - g);
                    AddRotation(shin, new Vector3(KneeBendForDrop(left, baseKnee, drop), 0f, 0f));
                }
            }
            return hip * g;
        }

        private void Land(bool left, float intensity)
        {
            _landTime = _clock;
            _landLeft = left;
            _landIntensity = intensity;
            OnFootstep?.Invoke(left, intensity);
        }

        /// <summary>착지 후 time 시점의 골반 꺼짐 깊이(m): 반 사인으로 내려갔다 돌아온다.</summary>
        private float LandDropAt(BernardTuning.VisualValues v, float time)
        {
            float t = time - _landTime;
            if (v.landDropTime <= 0f || t < 0f || t >= v.landDropTime) return 0f;
            return v.landPelvisDrop * _landIntensity * Mathf.Sin(Mathf.PI * t / v.landDropTime);
        }

        /// <summary>
        /// 엉덩이~발목 거리를 drop만큼 줄이는 데 필요한 추가 무릎 각도(°). 허벅지 a, 정강이 b, 무릎 굽힘 θ(0 = 곧음)이면
        /// 거리² = a² + b² + 2ab·cosθ.
        /// </summary>
        private float KneeBendForDrop(bool left, float currentKneeDeg, float drop)
        {
            float a = left ? _thighL : _thighR;
            float b = left ? _shinLenL : _shinLenR;
            if (a < 0.01f || b < 0.01f) return 0f;
            float theta0 = currentKneeDeg * Mathf.Deg2Rad;
            float length0 = Mathf.Sqrt(a * a + b * b + 2f * a * b * Mathf.Cos(theta0));
            // drop이 음수면(착지 뒤 밀고 올라옴, N3) 다리를 편다 — 지금 굽힌 만큼까지만
            float length1 = Mathf.Clamp(length0 - drop, Mathf.Abs(a - b) + 0.01f, a + b - 0.001f);
            float cos1 = Mathf.Clamp((length1 * length1 - a * a - b * b) / (2f * a * b), -1f, 1f);
            return Mathf.Max(-currentKneeDeg, Mathf.Acos(cos1) * Mathf.Rad2Deg - currentKneeDeg);
        }

        private static float ElbowSwing(BernardTuning.VisualValues v, float legPhase)
        {
            // 0~1을 오가는 굽힘. 다리 위상보다 elbowSwingLag(주기 비율)만큼 늦다
            return v.elbowSwingAngle * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * (legPhase - v.elbowSwingLag)));
        }

        private void FixedUpdate()
        {
            // 가속도는 물리 스텝마다 잰다. 속도는 물리 스텝에서만 바뀌어서, 렌더 프레임마다 재면
            // 스텝이 없는 프레임은 0, 있는 프레임은 크게 튀어(프레임이 물리 주기에 가까운 화면 분할에서 특히) 상체가 떨린다 (J3)
            if (!HasLowerBody) return;
            Vector3 velocity = HorizontalVelocity();
            if (Time.fixedDeltaTime > 0.0001f) _physicsAccel = (velocity - _lastFixedVelocity) / Time.fixedDeltaTime;
            _lastFixedVelocity = velocity;
        }

        /// <summary>가속하면 상체를 숙이고, 브레이크(감속)하면 뒤로 젖힌다. 가속도는 상체 기준으로 본다.</summary>
        private void PoseAccelLean(BernardTuning.VisualValues v, float dt, Vector3 velocity)
        {
            if (dt > 0f)
            {
                float k = v.accelSmoothTime > 0f ? 1f - Mathf.Exp(-dt / v.accelSmoothTime) : 1f;
                _smoothedAccel += (_physicsAccel - _smoothedAccel) * k;
            }

            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, _bodyYaw, 0f)) * _smoothedAccel;
            float leanForward = Mathf.Clamp(local.z * v.accelLeanPerMs2, -v.maxAccelLean, v.maxAccelLean);
            float leanSide = Mathf.Clamp(-local.x * v.accelLeanPerMs2, -v.maxAccelLean, v.maxAccelLean);
            AddRotation(_body, new Vector3(leanForward, 0f, leanSide));
        }

        /// <summary>스커트는 허벅지 회전을 목표로 삼고, 따로 느슨한 스프링(skirtSpring*)으로 늦게 따라간다.</summary>
        private void PoseSkirts(BernardTuning.VisualValues v)
        {
            Vector3 l = _legL != null ? _legL.Rotation : Vector3.zero;
            Vector3 r = _legR != null ? _legR.Rotation : Vector3.zero;
            float f = v.skirtFollow;
            AddRotation(_skirtFront, new Vector3(Mathf.Min(l.x, r.x), 0f, 0f) * f);
            AddRotation(_skirtBack, new Vector3(Mathf.Max(l.x, r.x), 0f, 0f) * f);
            AddRotation(_skirtSideL, new Vector3(l.x * 0.5f, 0f, l.z) * f);
            AddRotation(_skirtSideR, new Vector3(r.x * 0.5f, 0f, r.z) * f);
        }

        private bool IsSkirt(Joint joint) =>
            joint == _skirtFront || joint == _skirtBack || joint == _skirtSideL || joint == _skirtSideR;
    }
}

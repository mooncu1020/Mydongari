using System;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Core;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>
    /// 포즈 라이브러리로 만드는 기본 자세와 기술 모션 (M2~M7). 하체 뼈가 있는 모델(UseMotions)에서만 불린다.
    /// 시간은 전부 게임플레이 시간(docs/MOTION_TIMING.md)에 맞추고, 각도는 MechPoseLibrary 포즈에 있다.
    /// </summary>
    public partial class MechVisualRig
    {
        // ---------------- M2 기본 자세 ----------------

        // 전력질주 돌격 자세 섞기 (0 = 로우 가드, 1 = 돌격)
        private float _sprintStance;

        /// <summary>돌격 자세일 때 걸음 팔 스윙 배율 (전신 모션이 아니면 1).</summary>
        private float ArmSwingScale => UseMotions ? Mathf.Lerp(1f, M.sprintArmSwingScale, _sprintStance) : 1f;

        /// <summary>
        /// 대기·걷기·달리기 = 로우 가드(주먹 가슴 높이, 팔꿈치 몸통에 붙임, 상체 약간 숙임, 무릎 굽힘),
        /// 전력질주 = 돌격 자세. 걸음·숨쉬기·가감속 숙임은 이 위에 더해진다 (이 층은 더하기).
        /// </summary>
        private void PoseStance(BernardTuning.VisualValues v, float dt)
        {
            BernardTuning.MotionValues m = M;
            float target = _stateMachine != null && _stateMachine.IsSprinting ? 1f : 0f;
            _sprintStance = m.sprintStanceBlendTime > 0f
                ? Mathf.MoveTowards(_sprintStance, target, dt / m.sprintStanceBlendTime)
                : target;
            float s = Mathf.SmoothStep(0f, 1f, _sprintStance);
            AddPose(PoseNames.LowGuard, 1f - s);
            AddPose(PoseNames.SprintGuard, s);
        }

        // ---------------- 공통: 타격 연출 ----------------

        /// <summary>
        /// 모션의 타격·착지 순간 (종류, 월드 위치, 세기 0~1). 먼지·효과음 연결용 — 지금은 화면 흔들림만 리그가 직접 건다.
        /// 종류: "LungePlant", "PileDriver".
        /// </summary>
        public event Action<string, Vector3, float> OnMotionImpact;

        /// <summary>내 카메라(와 bothPlayers면 상대 카메라)를 흔든다.</summary>
        private void ShakeCameras(float amplitude, float duration, bool bothPlayers)
        {
            if (_identity == null) return;
            MyDongari.Camera.MechCamera own = _identity.MechCamera;
            if (own != null) own.AddShake(amplitude, duration);
            if (!bothPlayers || _identity.Opponent == null) return;
            MyDongari.Camera.MechCamera other = _identity.Opponent.MechCamera;
            if (other != null && other != own) other.AddShake(amplitude, duration);
        }

        // ---------------- M3 기본 펀치 ----------------

        private float _punchGait;

        /// <summary>펀치 외형 길이: 전신 모션이면 쿨다운 끝(회복 포함), 아니면 예전 뻗기·유지.</summary>
        private float PunchVisualLength(BernardTuning.VisualValues v)
        {
            if (!UseMotions) return PunchWindup + v.punchExtendTime + v.punchHoldTime;
            BernardTuning.MotionValues m = M;
            return Mathf.Max(PunchWindup + m.punchFollowTime + m.punchFollowHold + 0.01f, _tuning.Get(this).Punch.cooldown);
        }

        /// <summary>
        /// 펀치: 0 → 예비(살짝 젖힘) → 판정 순간(발동 0.1초) 뻗음 → 팔로스루(상체가 더 밀고 들어감) → 쿨다운 끝까지 로우 가드로 묵직하게.
        /// 뻗는 구간은 뒷발 → 앞발·골반 → 허리·머리·반대 팔 → 때리는 팔 순서로 punchChainDelay씩 먼저 도착한다(주먹이 판정 순간에 도착).
        /// 오른 주먹 기준 포즈, 왼 주먹(원투 두 번째)은 좌우 반전. 달리는 중이면 하체는 걸음에 맡기고 앞발 디딤만 더한다.
        /// </summary>
        private void PosePunch()
        {
            BernardTuning.VisualValues v = V;
            float t = _clock - _punchStart;
            float length = PunchVisualLength(v);
            if (t < 0f || t >= length) return;

            BernardTuning.MotionValues m = M;
            float hit = PunchWindup;
            float follow = hit + m.punchFollowTime;
            float hold = follow + m.punchFollowHold;
            bool mirror = !_punchRight;
            float d = m.punchChainDelay;

            BeginMotion();
            LeadAnchor(hit);
            Key(0f, null);
            Key(hit * m.punchWindupRatio, PoseNames.PunchWindup, MechPoseLibrary.EaseSmooth);
            Key(hit, PoseNames.PunchStrike, MechPoseLibrary.EaseSnap);
            Key(follow, PoseNames.PunchFollow, MechPoseLibrary.EaseSmooth);
            Key(hold, PoseNames.PunchFollow, MechPoseLibrary.EaseLinear);
            Key(length, null, MechPoseLibrary.EaseHeavy);
            // 뒷발(오른발) 밀기가 제일 먼저, 그다음 앞발·골반, 허리·머리·반대 팔, 마지막이 때리는 팔
            Lead(MechPoseLibrary.HipRIndex, d * 3f, mirror);
            Lead(MechPoseLibrary.LegRIndex, d * 3f, mirror);
            Lead(MechPoseLibrary.ShinRIndex, d * 3f, mirror);
            Lead(MechPoseLibrary.HipLIndex, d * 2f, mirror);
            Lead(MechPoseLibrary.LegLIndex, d * 2f, mirror);
            Lead(MechPoseLibrary.ShinLIndex, d * 2f, mirror);
            Lead(MechPoseLibrary.PelvisIndex, d * 2f, mirror);
            Lead(MechPoseLibrary.BodyIndex, d, mirror);
            Lead(MechPoseLibrary.HeadIndex, d, mirror);
            Lead(MechPoseLibrary.ArmLUpperIndex, d, mirror);
            Lead(MechPoseLibrary.ArmLLowerIndex, d, mirror);
            Lead(MechPoseLibrary.FistLIndex, d, mirror);
            // 걷거나 달리는 중엔 하체를 걸음에 양보한다
            MaskLowerBody(1f - _gaitWeight);
            PlayMotion(t, 1f, mirror);

            if (_punchGait > 0.01f)
            {
                // 달리는 중 펀치: 앞발 디딤 (하체만, 시작 순간 걸음 세기만큼)
                BeginMotion();
                MaskAll(0f);
                MaskLowerBody(1f);
                Key(0f, null);
                Key(hit, PoseNames.PunchStepIn, MechPoseLibrary.EaseSnap);
                Key(hold, PoseNames.PunchStepIn, MechPoseLibrary.EaseLinear);
                Key(length, null, MechPoseLibrary.EaseHeavy);
                PlayMotion(t, _punchGait, mirror);
            }
        }
    
        // ---------------- M4 스러스터 런지 ----------------

        private float _lungeStart = None;
        private float _lungePlant = None;
        private float _lungeEnd = None;

        private void OnLungeStartedVisual()
        {
            _lungeStart = _clock;
            _lungePlant = None;
            _lungeEnd = None;
        }

        private void OnLungePlantVisual(bool inRange)
        {
            ErrorLog.Run(() =>
            {
                _lungePlant = _clock;
                if (!UseMotions) return;
                BernardTuning.MotionValues m = M;
                ShakeCameras(m.lungePlantShake, m.lungePlantShakeTime, bothPlayers: false);
                // 앞발(왼발) 위치에서 먼지
                Vector3 foot = _footL != null ? _footL.position : transform.position;
                OnMotionImpact?.Invoke("LungePlant", foot, inRange ? 1f : 0.7f);
            }, "MechVisualRig.OnLungePlant");
        }

        private void OnLungeEndedVisual(HitResult? result) => _lungeEnd = _clock;

        /// <summary>
        /// 런지: 웅크림 → 낮게 도약 → 비행(골반이 뜨고 접지 보정 멈춤) → [새 판정] 앞발 박기(무릎 깊게, 흔들림) → 묵직한 스트레이트 → 복귀.
        /// 돌진은 발생 시간이 없어(누르면 바로 출발) 웅크림·도약은 출발과 동시에 아주 짧게 지나간다.
        /// 박기·뻗기 시간은 ThrusterLunge 판정 시간(Lunge.plantTime·strikeExtendTime)을 그대로 쓴다.
        /// 예전 판정 방식이면 박기 없이 비행 자세에서 바로 복귀.
        /// </summary>
        private void PoseLunge()
        {
            if (_lungeStart == None) return;
            BernardTuning.MotionValues m = M;

            if (_lungePlant != None && _lungePlant >= _lungeStart)
            {
                BernardTuning.LungeValues lunge = _tuning.Get(this).Lunge;
                float tp = _clock - _lungePlant;
                float plant = lunge.plantTime;
                float strike = plant + lunge.strikeExtendTime;
                float hold = strike + m.lungeStrikeHold;
                float end = hold + m.lungeRecoverTime;
                if (tp >= end)
                {
                    _lungeStart = None;
                    return;
                }

                BeginMotion();
                LeadAnchor(strike);
                Key(0f, PoseNames.LungeFlight);
                Key(plant, PoseNames.LungePlant, MechPoseLibrary.EaseSnap);
                Key(strike, PoseNames.LungeStrike, MechPoseLibrary.EaseSnap);
                Key(hold, PoseNames.LungeStrike, MechPoseLibrary.EaseLinear);
                Key(end, null, MechPoseLibrary.EaseHeavy);
                // 스트레이트도 펀치처럼 뒷발 → 골반 → 허리 → 어깨 순서
                float d = m.punchChainDelay;
                Lead(MechPoseLibrary.LegRIndex, d * 3f, false);
                Lead(MechPoseLibrary.ShinRIndex, d * 3f, false);
                Lead(MechPoseLibrary.HipRIndex, d * 3f, false);
                Lead(MechPoseLibrary.PelvisIndex, d * 2f, false);
                Lead(MechPoseLibrary.BodyIndex, d, false);
                Lead(MechPoseLibrary.HeadIndex, d, false);
                PlayMotion(tp, 1f, false);
                return;
            }

            float t = _clock - _lungeStart;
            float crouch = m.lungeCrouchTime;
            float flight = crouch + m.lungeLaunchTime;
            BeginMotion();
            Key(0f, null);
            Key(crouch, PoseNames.LungeCrouch, MechPoseLibrary.EaseIn);
            Key(flight, PoseNames.LungeFlight, MechPoseLibrary.EaseSnap);
            if (_lungeEnd != None && _lungeEnd >= _lungeStart)
            {
                // 박기 없이 끝남(예전 판정 방식·스턴·앵커 클로에 잡힘): 비행 자세에서 바로 복귀
                float endT = _lungeEnd - _lungeStart;
                if (t >= endT + m.lungeRecoverTime)
                {
                    _lungeStart = None;
                    return;
                }
                Key(endT, PoseNames.LungeFlight, MechPoseLibrary.EaseLinear);
                Key(endT + m.lungeRecoverTime, null, MechPoseLibrary.EaseHeavy);
            }
            else if (t >= crouch)
            {
                // 비행: 골반이 뜨는 동안 접지 보정이 발을 땅에 끌어내리지 않게
                _groundingHold = true;
            }
            PlayMotion(t, 1f, false);
        }

        // ---------------- M5 가드 계열 ----------------

        // 가드 자세 섞기(0~1). 가드 자체는 버튼 즉시라, 외형만 아주 짧게 올리고 조금 천천히 내린다
        private float _guardWeight;
        private float _blockStart = None;
        private Vector3 _blockDirection;
        private float _parryStart = None;
        private float _breakStart = None;
        private float _breakLength;

        private void OnDefenseResolved(HitInfo info, HitResult result)
        {
            switch (result)
            {
                case HitResult.Guarded:
                    _blockStart = _clock;
                    // 때린 쪽에서 멀어지는 방향(기체 축)으로 미끄러진다
                    Vector3 away = info.Attacker != null ? transform.position - info.Attacker.transform.position : -transform.forward;
                    away.y = 0f;
                    _blockDirection = away.sqrMagnitude > 0.0001f ? transform.InverseTransformDirection(away.normalized) : Vector3.back;
                    break;
                case HitResult.Parried:
                    _parryStart = _clock;
                    break;
                case HitResult.GuardBroken:
                    _breakStart = _clock;
                    // MeleeSystem과 같은 길이: 가드 브레이크 스턴과 공격 스턴 중 긴 쪽
                    _breakLength = Mathf.Max(_tuning.Get(this).Guard.breakStun, info.StunDuration);
                    _guardWeight = 0f;
                    break;
            }
        }

        private bool IsDefenseMotionActive()
        {
            BernardTuning.MotionValues m = M;
            return _guardWeight > 0f
                || InWindow(_blockStart, m.blockPushTime + m.blockRecoverTime)
                || InWindow(_parryStart, m.parryTime)
                || InWindow(_breakStart, _breakLength);
        }

        /// <summary>
        /// 가드: 무릎 굽혀 낮추고 허리 숙임, 두 팔뚝을 세워 얼굴·가슴 앞 벽, 팔꿈치로 배를 덮고 머리 숙임 (걸으면 하체는 걸음).
        /// 막음: 팔이 몸 쪽으로 밀리고 기체가 맞은 반대쪽으로 미끄러지는 외형 반동(물리 위치는 그대로).
        /// 패리 성공: 오른 팔뚝으로 바깥으로 쳐내는 짧은 동작. 가드 브레이크: 팔이 벌어지고 상체가 젖혀져 스턴 동안 휘청.
        /// </summary>
        private void PoseDefense(float dt)
        {
            BernardTuning.MotionValues m = M;
            bool guarding = _melee != null && _melee.IsGuarding;
            float fade = guarding ? m.guardRaiseTime : m.guardLowerTime;
            float target = guarding ? 1f : 0f;
            _guardWeight = fade > 0f ? Mathf.MoveTowards(_guardWeight, target, dt / fade) : target;

            if (_guardWeight > 0f)
            {
                BeginMotion();
                MaskLowerBody(1f - _gaitWeight);
                Key(0f, PoseNames.Guard);
                PlayMotion(0f, Mathf.SmoothStep(0f, 1f, _guardWeight), false);
            }

            float t = _clock - _blockStart;
            if (t >= 0f && t < m.blockPushTime + m.blockRecoverTime)
            {
                BeginMotion();
                MaskLowerBody(1f - _gaitWeight);
                Key(0f, null);
                Key(m.blockPushTime, PoseNames.GuardBlocked, MechPoseLibrary.EaseSnap);
                Key(m.blockPushTime + m.blockRecoverTime, null, MechPoseLibrary.EaseHeavy);
                PlayMotion(t, 1f, false);
                // 미끄러짐: 밀릴 때 빠르게, 돌아올 때 천천히 (스프링 없이 정확한 시간)
                float slide = t < m.blockPushTime
                    ? PoseLibrary.Evaluate(MechPoseLibrary.EaseSnap, t / Mathf.Max(0.0001f, m.blockPushTime))
                    : 1f - PoseLibrary.Evaluate(MechPoseLibrary.EaseHeavy, (t - m.blockPushTime) / Mathf.Max(0.0001f, m.blockRecoverTime));
                if (_pelvis != null) _pelvis.DirectPosition += _blockDirection * (m.blockSlideDistance * slide);
            }

            t = _clock - _parryStart;
            if (t >= 0f && t < m.parryTime)
            {
                BeginMotion();
                MaskLowerBody(1f - _gaitWeight);
                Key(0f, null);
                Key(m.parryTime * 0.35f, PoseNames.ParryDeflect, MechPoseLibrary.EaseSnap);
                Key(m.parryTime, null, MechPoseLibrary.EaseHeavy);
                PlayMotion(t, 1f, false);
            }

            t = _clock - _breakStart;
            if (t >= 0f && t < _breakLength)
            {
                float recover = Mathf.Min(m.guardBreakRecoverTime, _breakLength * 0.5f);
                BeginMotion();
                Key(0f, null);
                Key(m.guardBreakFlingTime, PoseNames.GuardBreak, MechPoseLibrary.EaseSnap);
                Key(_breakLength - recover, PoseNames.GuardBreak, MechPoseLibrary.EaseLinear);
                Key(_breakLength, null, MechPoseLibrary.EaseHeavy);
                PlayMotion(t, 1f, false);
                // 휘청: 앞뒤·좌우로 줄어드는 흔들림
                float decay = 1f - t / _breakLength;
                float phase = t * m.guardBreakWobbleFrequency * 2f * Mathf.PI;
                float a = m.guardBreakWobbleAngle * decay;
                AddRotation(_body, new Vector3(Mathf.Sin(phase) * a, 0f, Mathf.Sin(phase * 0.7f + 1f) * a));
                AddRotation(_head, new Vector3(Mathf.Sin(phase * 1.3f + 2f) * a, 0f, 0f));
            }
        }

        // ---------------- M6 앵커 클로 → 파일 드라이버 ----------------

        private AnchorClaw.ClawPhase _lastClawPhase = AnchorClaw.ClawPhase.Ready;
        private float _clawPhaseStart = None;
        private float _clawMissEnd = None;
        private float _pullEnd = None;
        private bool _pileFromReel;
        private bool _pileImpactFired;
        private float _pileImpactRealTime = None;

        private bool IsClawMotionActive()
        {
            BernardTuning.MotionValues m = M;
            return _clawStart != None
                || InWindow(_clawMissEnd, m.clawReturnTime)
                || InWindow(_pileStart, PileDriverLength(m));
        }

        private float PileDriverLength(BernardTuning.MotionValues m) => m.uppercutRiseTime + m.uppercutHold + m.uppercutRecoverTime;

        /// <summary>
        /// 앵커 클로: 예비(왼팔 뒤로, 발동 windupTime) → 던지기(왼팔 뻗음, 날아가는·감겨 오는 동안) → 빗나가면 복귀.
        /// 잡으면: 클로 팔 잡아채기·상체 뒤로·뒷발 버팀 → 끌려오는 동안 골반 깊게 낮추고 허리 비틀고 오른 주먹 허리춤(어퍼컷 준비).
        /// 당기기가 끝나도 파일 드라이버 준비 중이면 clawReelHold 동안 그 자세로 기다렸다가 풀린다.
        /// </summary>
        private void PoseClaw()
        {
            if (_claw == null) return;
            BernardTuning.MotionValues m = M;
            AnchorClaw.ClawPhase phase = _claw.Phase;
            if (phase != _lastClawPhase)
            {
                if (_lastClawPhase == AnchorClaw.ClawPhase.Pulling) _pullEnd = _clock;
                if (_lastClawPhase == AnchorClaw.ClawPhase.Retracting && phase == AnchorClaw.ClawPhase.Ready) _clawMissEnd = _clock;
                _clawPhaseStart = _clock;
                _lastClawPhase = phase;
            }
            float tp = _clock - _clawPhaseStart;

            // 던지기 단계
            if (phase == AnchorClaw.ClawPhase.Windup)
            {
                BeginMotion();
                Key(0f, null);
                Key(_tuning.Get(this).AnchorClaw.windupTime, PoseNames.ClawWindup, MechPoseLibrary.EaseSmooth);
                PlayMotion(tp, 1f, false);
            }
            else if (phase == AnchorClaw.ClawPhase.Flying)
            {
                BeginMotion();
                Key(0f, PoseNames.ClawWindup);
                Key(m.clawThrowTime, PoseNames.ClawThrow, MechPoseLibrary.EaseSnap);
                PlayMotion(tp, 1f, false);
            }
            else if (phase == AnchorClaw.ClawPhase.Retracting)
            {
                BeginMotion();
                Key(0f, PoseNames.ClawThrow);
                PlayMotion(0f, 1f, false);
            }
            else if (phase == AnchorClaw.ClawPhase.Ready && InWindow(_clawMissEnd, m.clawReturnTime))
            {
                BeginMotion();
                Key(0f, PoseNames.ClawThrow);
                Key(m.clawReturnTime, null, MechPoseLibrary.EaseHeavy);
                PlayMotion(_clock - _clawMissEnd, 1f, false);
            }

            // 잡은 뒤: 잡아채기 → 끌어당김 자세 유지 → (파일 드라이버를 안 쓰면) 풀림
            if (_clawStart == None) return;
            float tg = _clock - _clawStart;
            float yank = m.clawYankTime;
            float yankHold = yank + m.clawYankHold;
            float reel = yankHold + m.clawReelBlendTime;
            BeginMotion();
            LeadAnchor(yank);
            Key(0f, PoseNames.ClawThrow);
            Key(yank, PoseNames.ClawYank, MechPoseLibrary.EaseSnap);
            Key(yankHold, PoseNames.ClawYank, MechPoseLibrary.EaseLinear);
            Key(reel, PoseNames.ClawReel, MechPoseLibrary.EaseSmooth);
            // 뒷발 버팀이 먼저, 그다음 상체, 마지막에 클로 팔
            Lead(MechPoseLibrary.LegRIndex, m.punchChainDelay * 2f, false);
            Lead(MechPoseLibrary.ShinRIndex, m.punchChainDelay * 2f, false);
            Lead(MechPoseLibrary.BodyIndex, m.punchChainDelay, false);

            bool holding = phase == AnchorClaw.ClawPhase.Pulling
                || (_claw.IsPileDriverReady && (_pullEnd == None || _clock - _pullEnd < m.clawReelHold));
            if (!holding)
            {
                float release = _pullEnd != None ? Mathf.Min(_clock, _pullEnd + m.clawReelHold) : _clock;
                float releaseT = Mathf.Max(reel, release - _clawStart);
                if (tg >= releaseT + m.clawReleaseTime)
                {
                    _clawStart = None;
                    return;
                }
                Key(releaseT, PoseNames.ClawReel, MechPoseLibrary.EaseLinear);
                Key(releaseT + m.clawReleaseTime, null, MechPoseLibrary.EaseHeavy);
            }
            PlayMotion(tg, 1f, false);
        }

        /// <summary>
        /// 파일 드라이버(오른 어퍼컷): 다리 펴기 → 골반 상승 → 허리 회전 → 주먹이 아래에서 위로 (uppercutRiseTime 안, 관절 사슬 지연).
        /// 판정·피해는 누른 순간에 이미 끝났다(시간 그대로) — 외형 타격 순간(올리기 끝)에 Fist_R 피스톤(실제 시간),
        /// 긴 히트스톱, 두 화면 큰 흔들림을 건다. 접지 보정은 그대로 켜 둔다.
        /// </summary>
        private void PosePileDriver()
        {
            BernardTuning.MotionValues m = M;
            float t = _clock - _pileStart;
            float length = PileDriverLength(m);
            if (_pileStart == None || t < 0f || t >= length)
            {
                ApplyPiston(m);
                return;
            }

            float rise = m.uppercutRiseTime;
            float hold = rise + m.uppercutHold;
            float d = m.punchChainDelay;
            BeginMotion();
            LeadAnchor(rise);
            Key(0f, _pileFromReel ? PoseNames.ClawReel : null);
            Key(rise * 0.45f, PoseNames.UppercutRise, MechPoseLibrary.EaseIn);
            Key(rise, PoseNames.UppercutStrike, MechPoseLibrary.EaseSnap);
            Key(hold, PoseNames.UppercutStrike, MechPoseLibrary.EaseLinear);
            Key(length, null, MechPoseLibrary.EaseHeavy);
            // 다리 → 골반 → 허리 → 주먹
            Lead(MechPoseLibrary.LegLIndex, d * 3f, false);
            Lead(MechPoseLibrary.LegRIndex, d * 3f, false);
            Lead(MechPoseLibrary.ShinLIndex, d * 3f, false);
            Lead(MechPoseLibrary.ShinRIndex, d * 3f, false);
            Lead(MechPoseLibrary.HipLIndex, d * 3f, false);
            Lead(MechPoseLibrary.HipRIndex, d * 3f, false);
            Lead(MechPoseLibrary.PelvisIndex, d * 2f, false);
            Lead(MechPoseLibrary.BodyIndex, d, false);
            Lead(MechPoseLibrary.HeadIndex, d, false);
            Lead(MechPoseLibrary.ArmLUpperIndex, d, false);
            Lead(MechPoseLibrary.ArmLLowerIndex, d, false);
            PlayMotion(t, 1f, false);

            if (!_pileImpactFired && t >= rise)
            {
                _pileImpactFired = true;
                OnPileDriverImpact(m);
            }
            ApplyPiston(m);
        }

        private void OnPileDriverImpact(BernardTuning.MotionValues m)
        {
            _pileImpactRealTime = Time.unscaledTime;
            ShakeCameras(m.pileDriverShake, m.pileDriverShakeTime, bothPlayers: true);
            BernardTuning.HitStopValues hitStop = _tuning.Get(this).HitStop;
            if (hitStop.enabled && GameManager.Instance != null)
            {
                HitStop stop = GameManager.Instance.GetComponent<HitStop>();
                if (stop != null) stop.Request(hitStop.pileDriver);
            }
            Vector3 fist = GetPunchPoint(true);
            OnMotionImpact?.Invoke("PileDriver", fist, 1f);
        }

        /// <summary>
        /// 타격 순간 오른 주먹(없으면 아래팔)이 팔뚝 축 방향으로 튀어나갔다 돌아온다. 히트스톱 중에도 보이게 실제 시간으로 잰다.
        /// 스프링 없이 이번 프레임 위치에만 더한다.
        /// </summary>
        private void ApplyPiston(BernardTuning.MotionValues m)
        {
            if (_pileImpactRealTime == None) return;
            float t = Time.unscaledTime - _pileImpactRealTime;
            float total = m.pistonOutTime + m.pistonBackTime;
            if (t < 0f || t >= total)
            {
                _pileImpactRealTime = None;
                return;
            }
            Joint fist = _fistR != null && _fistR.Target != null ? _fistR : _armRLower;
            if (fist == null || fist.Target == null || _armRLower == null || _armRLower.Target == null) return;

            // 팔뚝 축 = 팔꿈치 → 주먹 (주먹이 없으면 아래팔 끝 추정)
            Vector3 elbow = _armRLower.Target.position;
            Vector3 hand = fist == _fistR ? _fistR.Target.position : GetPunchPoint(true);
            Vector3 axis = hand - elbow;
            if (axis.sqrMagnitude < 0.0001f) return;
            float out01 = t < m.pistonOutTime
                ? t / Mathf.Max(0.0001f, m.pistonOutTime)
                : 1f - PoseLibrary.Evaluate(MechPoseLibrary.EaseSmooth, (t - m.pistonOutTime) / Mathf.Max(0.0001f, m.pistonBackTime));
            fist.DirectPosition += transform.InverseTransformDirection(axis.normalized) * (m.pistonDistance * out01);
        }

        // ---------------- M7 대시 공격·태클·슬램 ----------------

        /// <summary>대시 어택·태클 외형 길이: 전신 모션이면 발동 + 공격자 경직(회복), 아니면 예전 길이.</summary>
        private float TackleVisualLength(BernardTuning.VisualValues v)
        {
            if (!UseMotions) return TackleWindup + v.tackleTime;
            return TackleWindup + Mathf.Max(M.tackleHold + 0.01f, _tuning.Get(this).Tackle.recoveryStun);
        }

        /// <summary>
        /// 대시 어택(질주 1.5초 미만) = 달리던 자세에서 오른 스트레이트, 차징 태클 = 어깨를 앞세운 돌격.
        /// 발동 시간(Startup.dashAttack 0.12 / chargedTackle 0.1초)에 타격 포즈 도착, 공격자 경직(Tackle.recoveryStun) 동안 복귀.
        /// 발동 중엔 아직 달리는 중이라 하체는 걸음에 양보한다(판정 뒤 멈추면 자연히 포즈 다리로).
        /// </summary>
        private void PoseTackle()
        {
            BernardTuning.VisualValues v = V;
            float t = _clock - _tackleStart;
            float length = TackleVisualLength(v);
            if (t < 0f || t >= length) return;

            BernardTuning.MotionValues m = M;
            bool charged = _tackleType == AttackType.Tackle;
            float hit = TackleWindup;
            float hold = hit + m.tackleHold;
            float d = m.punchChainDelay;
            BeginMotion();
            LeadAnchor(hit);
            Key(0f, null);
            Key(hit * 0.5f, charged ? PoseNames.TackleWindup : PoseNames.DashWindup, MechPoseLibrary.EaseSmooth);
            Key(hit, charged ? PoseNames.TackleShoulder : PoseNames.DashStrike, MechPoseLibrary.EaseSnap);
            Key(hold, charged ? PoseNames.TackleShoulder : PoseNames.DashStrike, MechPoseLibrary.EaseLinear);
            Key(length, null, MechPoseLibrary.EaseHeavy);
            Lead(MechPoseLibrary.LegRIndex, d * 3f, false);
            Lead(MechPoseLibrary.ShinRIndex, d * 3f, false);
            Lead(MechPoseLibrary.HipRIndex, d * 3f, false);
            Lead(MechPoseLibrary.PelvisIndex, d * 2f, false);
            Lead(MechPoseLibrary.BodyIndex, d, false);
            Lead(MechPoseLibrary.HeadIndex, d, false);
            MaskLowerBody(1f - _gaitWeight);
            PlayMotion(t, 1f, false);
        }

        private float SlamMotionLength(BernardTuning.MotionValues m) => m.slamRaiseTime + m.slamDownTime + m.slamHold + m.slamRecoverTime;

        /// <summary>
        /// 임팩트 슬램: 두 주먹을 머리 위로 들었다가 내려찍기. 판정은 누른 순간(발동 0초)이라 외형은 짧게 들고 바로 내려찍는다.
        /// </summary>
        private void PoseSlam()
        {
            BernardTuning.MotionValues m = M;
            float t = _clock - _slamStart;
            float length = SlamMotionLength(m);
            if (t < 0f || t >= length) return;

            float raise = m.slamRaiseTime;
            float down = raise + m.slamDownTime;
            float hold = down + m.slamHold;
            BeginMotion();
            LeadAnchor(down);
            Key(0f, null);
            Key(raise, PoseNames.SlamRaise, MechPoseLibrary.EaseSmooth);
            Key(down, PoseNames.SlamDown, MechPoseLibrary.EaseSnap);
            Key(hold, PoseNames.SlamDown, MechPoseLibrary.EaseLinear);
            Key(length, null, MechPoseLibrary.EaseHeavy);
            // 내려찍을 땐 무릎·골반이 먼저 꺼지고 팔이 마지막에 떨어진다
            Lead(MechPoseLibrary.ShinLIndex, m.punchChainDelay * 2f, false);
            Lead(MechPoseLibrary.ShinRIndex, m.punchChainDelay * 2f, false);
            Lead(MechPoseLibrary.PelvisIndex, m.punchChainDelay * 2f, false);
            Lead(MechPoseLibrary.BodyIndex, m.punchChainDelay, false);
            PlayMotion(t, 1f, false);
        }
}
}

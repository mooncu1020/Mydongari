using UnityEngine;
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
    }
}

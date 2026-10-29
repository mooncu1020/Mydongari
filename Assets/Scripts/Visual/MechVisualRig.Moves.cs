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
    }
}

using UnityEngine;

namespace MyDongari.Core
{
    /// <summary>상체 방향을 따로 가진 외형(상하체 분리 리그)이 구현한다. 전투 코드는 이 인터페이스만 안다.</summary>
    public interface IUpperBodyFacing
    {
        /// <summary>지금 상체 방향을 따로 계산하고 있는지 (하체 뼈가 없는 임시 상자 등은 false).</summary>
        bool HasUpperBodyFacing { get; }
        /// <summary>상체가 향한 수평 방향(월드).</summary>
        Vector3 UpperBodyForward { get; }
    }

    /// <summary>
    /// 공격 방향 = 상체 방향 (G2). 펀치·태클·슬램 부채꼴, 앵커 클로·런지의 기본 방향이 전부 이 함수 하나를 쓴다.
    /// 상하체를 나누는 외형이면 그 상체 방향, 아니면(임시 상자·봇) 예전처럼 기체 정면(transform.forward).
    /// </summary>
    public static class MechFacing
    {
        public static Vector3 AttackForward(Transform mech)
        {
            if (mech == null) return Vector3.forward;
            IUpperBodyFacing facing = mech.GetComponent<IUpperBodyFacing>();
            Vector3 forward = facing != null && facing.HasUpperBodyFacing ? facing.UpperBodyForward : mech.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = mech.forward;
                forward.y = 0f;
            }
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }
}

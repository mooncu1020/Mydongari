using UnityEngine;

namespace MyDongari.Mech
{
    /// <summary>
    /// 기체 루트에 붙는 튜닝 에셋 참조 하나. 기체의 모든 컴포넌트(이동·게이지·전투·스킬·락온)가 이걸 통해 같은 에셋을 읽는다.
    /// 컴포넌트마다 에셋 필드를 두면 기체 하나에 열 군데를 연결해야 해서 한 곳으로 모았다.
    /// </summary>
    public class MechTuning : MonoBehaviour
    {
        [Tooltip("이 기체가 쓸 수치 에셋. 비워 두면 Resources/BernardTuning, 그것도 없으면 코드 기본값")]
        [SerializeField] private BernardTuning tuning;

        /// <summary>실제로 쓸 값. 연결이 없으면 공용 기본 에셋.</summary>
        public BernardTuning Values => tuning != null ? tuning : BernardTuning.Fallback;
        public BernardTuning Asset => tuning;
    }

    /// <summary>
    /// 컴포넌트가 들고 있는 튜닝 조회 핸들. 처음 쓸 때 한 번만 MechTuning을 찾는다
    /// (런타임에 붙는 컴포넌트나 Awake 순서와 상관없게). 값은 캐시하지 않으므로 에셋 변경이 바로 반영된다.
    /// 구조체라 반드시 필드로 들고 써야 한다 (프로퍼티로 감싸면 조회 결과가 저장되지 않음).
    /// </summary>
    public struct TuningRef
    {
        private MechTuning _source;
        private bool _resolved;

        public BernardTuning Get(Component owner)
        {
            if (!_resolved)
            {
                _source = owner.GetComponentInParent<MechTuning>();
                _resolved = true;
            }
            return _source != null ? _source.Values : BernardTuning.Fallback;
        }
    }
}

using UnityEngine;
using MyDongari.Camera;
using MyDongari.Movement;

namespace MyDongari.Core
{
    /// <summary>
    /// 기체 하나의 "나는 누구고 상대는 누구인가" 정보.
    /// 화면 분할 2인에서는 태그(Player/Enemy)나 MainCamera로 상대·카메라를 찾으면
    /// 두 기체가 같은 대상을 잡거나 자기 자신을 때리게 되므로, 모든 전투 코드는 이 참조를 쓴다.
    /// </summary>
    public class MechIdentity : MonoBehaviour
    {
        [Header("식별")]
        [Tooltip("1 = P1, 2 = P2. GameManager가 라운드 시작 전에 덮어쓴다")]
        [SerializeField] private int playerIndex = 1;

        [Header("참조")]
        [Tooltip("상대 기체. 비워 두면 GameManager가 player1/player2를 서로 연결한다. 더미 1인 테스트 때 직접 지정해도 된다")]
        [SerializeField] private MechIdentity opponent;
        [Tooltip("이 기체를 비추는 카메라. 비워 두면 MechController의 Camera Transform을 쓴다")]
        [SerializeField] private Transform cameraTransform;

        public int PlayerIndex => playerIndex;
        public MechIdentity Opponent => opponent;

        public Transform CameraTransform
        {
            get
            {
                // 카메라는 MechController에 이미 연결돼 있는 경우가 대부분이라 그쪽을 기본값으로 쓴다
                if (cameraTransform == null)
                {
                    MechController controller = GetComponent<MechController>();
                    if (controller != null) cameraTransform = controller.CameraTransform;
                }
                return cameraTransform;
            }
        }

        public MechCamera MechCamera => CameraTransform != null ? CameraTransform.GetComponent<MechCamera>() : null;
        public UnityEngine.Camera ViewCamera => CameraTransform != null ? CameraTransform.GetComponent<UnityEngine.Camera>() : null;

        public void SetPlayerIndex(int index) => playerIndex = index;
        public void SetOpponent(MechIdentity other) => opponent = other;

        /// <summary>콜라이더가 내 상대 기체(자식 콜라이더 포함)의 것인지. 자기 자신은 절대 true가 아니다.</summary>
        public bool IsOpponentCollider(Collider col)
        {
            if (col == null || opponent == null) return false;
            MechIdentity owner = col.GetComponentInParent<MechIdentity>();
            return owner != null && owner == opponent && owner != this;
        }

        /// <summary>콜라이더가 내 기체(자식 포함)의 것인지.</summary>
        public bool IsOwnCollider(Collider col)
        {
            return col != null && col.GetComponentInParent<MechIdentity>() == this;
        }

        /// <summary>더미처럼 씬에서 안 붙여 둔 기체도 동작하게, 없으면 런타임에 붙인다.</summary>
        public static MechIdentity GetOrAdd(GameObject go)
        {
            MechIdentity id = go.GetComponent<MechIdentity>();
            if (id == null) id = go.AddComponent<MechIdentity>();
            return id;
        }
    }
}

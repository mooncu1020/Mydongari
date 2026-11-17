using UnityEngine;

namespace MyDongari.Core
{
    /// <summary>
    /// 레이어 기반 지형 구분 (V5). "Walkable" 레이어이면서 위를 향한 면(법선 y ≥ 기준)이면 땅, "Wall" 레이어는 벽.
    /// 레이어와 지정은 에디터 메뉴 Mydongari/Map/…(Editor/GroundLayerSetup)이 만든다.
    /// 메뉴를 아직 안 돌린 씬(Walkable 레이어가 없거나, 있어도 그 레이어 콜라이더가 하나도 없음)에서는
    /// 예전처럼 리지드바디 없는 정적 콜라이더의 위를 향한 면을 땅으로 본다 — 접지를 못 찾아 기체가 계속 공중 상태가 되는 걸 막는다.
    /// </summary>
    public static class GroundLayers
    {
        public const string WalkableLayerName = "Walkable";
        public const string WallLayerName = "Wall";

        private static bool _initialized;
        private static int _walkableLayer = -1;
        private static int _wallLayer = -1;
        private static bool _useWalkableLayer;

        /// <summary>Walkable 레이어로 땅을 가리는 중인지 (false = 정적 콜라이더 대체 규칙).</summary>
        public static bool UsesWalkableLayer
        {
            get
            {
                Initialize();
                return _useWalkableLayer;
            }
        }

        /// <summary>씬이 바뀌면(맵 교체) 다시 확인하도록 비운다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _initialized = false;
        }

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _walkableLayer = LayerMask.NameToLayer(WalkableLayerName);
            _wallLayer = LayerMask.NameToLayer(WallLayerName);
            _useWalkableLayer = _walkableLayer >= 0 && AnyColliderOnLayer(_walkableLayer);
            if (_walkableLayer >= 0 && !_useWalkableLayer)
            {
                Debug.LogWarning("[GroundLayers] Walkable 레이어에 콜라이더가 없습니다. 메뉴 Mydongari/Map/Assign Arena Ground Layers를 실행하고 씬을 저장하세요. 지금은 정적 콜라이더의 윗면을 땅으로 봅니다.");
            }
        }

        private static bool AnyColliderOnLayer(int layer)
        {
            foreach (Collider c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c.gameObject.layer == layer) return true;
            }
            return false;
        }

        /// <summary>
        /// 이 콜라이더의 이 면이 설 수 있는 땅인지: 트리거 아님, 법선 y ≥ minNormalY, 그리고
        /// Walkable 레이어를 쓰면 그 레이어, 아니면 리지드바디가 없는 정적 콜라이더.
        /// </summary>
        public static bool IsGround(Collider collider, Vector3 normal, float minNormalY)
        {
            if (collider == null || collider.isTrigger || normal.y < minNormalY) return false;
            Initialize();
            if (_useWalkableLayer) return collider.gameObject.layer == _walkableLayer;
            return collider.attachedRigidbody == null;
        }

        /// <summary>Wall 레이어 콜라이더인지 (면 방향과 상관없이 벽).</summary>
        public static bool IsWallLayer(Collider collider)
        {
            if (collider == null) return false;
            Initialize();
            return _wallLayer >= 0 && collider.gameObject.layer == _wallLayer;
        }
    }
}

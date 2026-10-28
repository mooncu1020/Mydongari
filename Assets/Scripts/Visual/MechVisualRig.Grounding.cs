using System;
using System.Collections.Generic;
using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>
    /// 접지 보정 (H3). 포즈를 다 적용한 뒤 두 발바닥 중 낮은 쪽이 기체 물리 콜라이더 바닥에 닿도록
    /// 비주얼 루트(기체 바로 아래, 모델을 담은 오브젝트)의 Y만 부드럽게 옮긴다. 무릎을 굽히거나 다리를 들어도 발이 땅에 붙는다.
    /// 발바닥 = 발목 뼈(없으면 다리 뼈) 아래 메시 경계 상자 꼭짓점 중 가장 낮은 점. 꼭짓점은 시작할 때 그 뼈 기준으로 한 번 재 둔다
    /// (렌더러가 꺼진 임시 부품도 잴 수 있게 Renderer.bounds 대신 메시 경계를 쓴다).
    /// </summary>
    public partial class MechVisualRig
    {
        private Transform _visualRoot;
        private Vector3 _visualRootRest;
        private Transform _footL, _footR;
        private Vector3[] _soleL, _soleR;
        private Collider[] _groundColliders;
        private float _groundOffset;
        private float _groundLogTimer;
        // 일부러 내린 높이(m, 착지 꺼짐 등 G3). 접지 보정이 이것까지 되돌리지 않게 목표에서 뺀다
        private float _intentionalDrop;

        /// <summary>지금 접지 보정량(m, +면 비주얼을 올림).</summary>
        public float GroundOffset => _groundOffset;
        /// <summary>groundLogInterval마다 보정량을 알린다 (PlayLogger가 ground_offset으로 기록).</summary>
        public event Action<float> OnGroundSample;

        private void SetupGrounding()
        {
            ResetGrounding();
            _groundColliders = GetComponents<Collider>();
            _visualRoot = FindVisualRoot();
            if (_visualRoot != null) _visualRootRest = _visualRoot.localPosition;

            _footL = _ankleL != null && _ankleL.Target != null ? _ankleL.Target : _legL != null ? _legL.Target : null;
            _footR = _ankleR != null && _ankleR.Target != null ? _ankleR.Target : _legR != null ? _legR.Target : null;
            _soleL = MeasureSole(_footL);
            _soleR = MeasureSole(_footR);
        }

        private void ResetGrounding()
        {
            if (_visualRoot != null) _visualRoot.localPosition = _visualRootRest;
            _groundOffset = 0f;
            _intentionalDrop = 0f;
        }

        /// <summary>몸통(없으면 다리) 뼈에서 위로 올라가 기체 바로 아래 자식을 찾는다 = 모델 전체를 담은 비주얼 루트.</summary>
        private Transform FindVisualRoot()
        {
            Transform start = body != null && body.target != null ? body.target : legL != null ? legL.target : null;
            if (start == null || start == transform) return null;
            Transform t = start;
            while (t.parent != null && t.parent != transform) t = t.parent;
            return t.parent == transform ? t : null;
        }

        /// <summary>발 뼈 아래 메시 경계 상자 꼭짓점을 그 뼈 기준 로컬 좌표로.</summary>
        private static Vector3[] MeasureSole(Transform foot)
        {
            if (foot == null) return null;
            var points = new List<Vector3>();
            foreach (MeshFilter filter in foot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    points.Add(foot.InverseTransformPoint(filter.transform.TransformPoint(corner)));
                }
            }
            return points.Count > 0 ? points.ToArray() : null;
        }

        private static bool TrySoleHeight(Transform foot, Vector3[] sole, out float height)
        {
            height = float.MaxValue;
            if (foot == null || sole == null) return false;
            foreach (Vector3 p in sole)
            {
                float y = foot.TransformPoint(p).y;
                if (y < height) height = y;
            }
            return true;
        }

        /// <summary>기체 물리 콜라이더(루트에 붙은 것, 트리거 제외)의 월드 바닥 높이.</summary>
        private bool TryGroundHeight(out float height)
        {
            height = float.MaxValue;
            if (_groundColliders == null) return false;
            foreach (Collider col in _groundColliders)
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                if (col.bounds.min.y < height) height = col.bounds.min.y;
            }
            return height < float.MaxValue;
        }

        private void UpdateGrounding(BernardTuning.VisualValues v, float dt)
        {
            if (_visualRoot == null) return;
            if (!v.groundingEnabled)
            {
                if (_groundOffset != 0f) ResetGrounding();
                return;
            }

            bool hasL = TrySoleHeight(_footL, _soleL, out float soleL);
            bool hasR = TrySoleHeight(_footR, _soleR, out float soleR);
            if ((!hasL && !hasR) || !TryGroundHeight(out float ground)) return;
            float lowest = Mathf.Min(hasL ? soleL : float.MaxValue, hasR ? soleR : float.MaxValue);

            // 지금 발바닥은 이미 현재 보정이 들어간 위치라, 목표 = 현재 보정 + 남은 차이. 일부러 내린 만큼은 되돌리지 않는다
            float target = Mathf.Clamp(_groundOffset + (ground - lowest) - _intentionalDrop, -v.groundingMaxOffset, v.groundingMaxOffset);
            float k = v.groundingSmoothTime > 0f ? 1f - Mathf.Exp(-dt / v.groundingSmoothTime) : 1f;
            _groundOffset += (target - _groundOffset) * k;

            Transform parent = _visualRoot.parent;
            Vector3 up = parent != null ? parent.InverseTransformVector(Vector3.up * _groundOffset) : Vector3.up * _groundOffset;
            _visualRoot.localPosition = _visualRootRest + up;

            if (v.groundLogInterval > 0f)
            {
                _groundLogTimer += dt;
                if (_groundLogTimer >= v.groundLogInterval)
                {
                    _groundLogTimer = 0f;
                    OnGroundSample?.Invoke(_groundOffset);
                }
            }
        }
    }
}

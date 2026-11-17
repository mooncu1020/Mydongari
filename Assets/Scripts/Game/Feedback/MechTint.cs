using System.Collections.Generic;
using UnityEngine;

namespace MyDongari.Game.Feedback
{
    /// <summary>
    /// 기체 전체 색 틴트. 머티리얼(공유 에셋)은 절대 건드리지 않고 MaterialPropertyBlock으로 색만 덮어쓴다.
    /// 그래서 Clear()로 블록을 비우면 언제든 원래 머티리얼 색으로 돌아간다 (에디터에서 머티리얼 에셋이 오염될 일도 없음).
    /// 바탕색(SetBase — 1P·2P 팀 색)을 정해 두면 "원래 색"이 그만큼 팀 색 쪽으로 옮겨진다: Clear()는 바탕색으로 돌아가고,
    /// 상태 틴트(Apply)는 바탕색에서 틴트 쪽으로 섞는다.
    /// </summary>
    public class MechTint
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        // 셀 셰이딩(ToonMech)은 그늘 쪽이 어두워서 기본색만 하얗게 섞으면 번쩍임이 약하다 — 이미시브를 같이 켠다 (S2)
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private struct Slot
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public Color Original;
            public bool HasBaseColor;
            public bool HasColor;
            public bool HasEmission;
            public Color OriginalEmission;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private bool _applied;
        // 바탕색 (팀 색). 세기 0이면 없음
        private Color _base;
        private float _baseStrength;

        /// <summary>
        /// 바탕색을 정한다 (1P·2P 구분). strength = 원래 색에서 바탕색 쪽으로 섞는 비율, 0이면 바탕색 없음. 같은 값이면 아무것도 안 한다.
        /// </summary>
        public void SetBase(Color color, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (Mathf.Approximately(strength, _baseStrength) && (strength <= 0f || color == _base)) return;
            _base = color;
            _baseStrength = strength;
            // 상태 틴트가 덮고 있지 않으면 지금 바로 보이게 (덮고 있으면 다음 Apply·Clear가 반영한다)
            if (!_applied) WriteBase();
        }

        private Color BaseOf(Slot slot)
        {
            if (_baseStrength <= 0f) return slot.Original;
            Color color = Color.Lerp(slot.Original, _base, _baseStrength);
            color.a = slot.Original.a;
            return color;
        }

        /// <summary>상태 틴트 없이 바탕색만 칠한다. 바탕색이 없으면 블록을 비워 머티리얼 원래 색으로.</summary>
        private void WriteBase()
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Renderer == null) continue;
                _block.Clear();
                if (_baseStrength > 0f)
                {
                    Color color = BaseOf(slot);
                    if (slot.HasBaseColor) _block.SetColor(BaseColorId, color);
                    if (slot.HasColor) _block.SetColor(ColorId, color);
                }
                slot.Renderer.SetPropertyBlock(_block, slot.MaterialIndex);
            }
        }

        public MechTint(GameObject mech)
        {
            foreach (Renderer renderer in mech.GetComponentsInChildren<Renderer>(true))
            {
                // 기체 몸체만 (파티클·트레일·라인·텍스트 제외)
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (renderer.GetComponent<TextMesh>() != null) continue;

                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) continue;
                    bool hasBase = material.HasProperty(BaseColorId);
                    bool hasColor = material.HasProperty(ColorId);
                    if (!hasBase && !hasColor) continue;

                    _slots.Add(new Slot
                    {
                        Renderer = renderer,
                        MaterialIndex = i,
                        Original = hasBase ? material.GetColor(BaseColorId) : material.GetColor(ColorId),
                        HasBaseColor = hasBase,
                        HasColor = hasColor,
                        HasEmission = material.HasProperty(EmissionColorId),
                        OriginalEmission = material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color.black
                    });
                }
            }
        }

        /// <summary>
        /// 원래 색에서 tint 쪽으로 strength(0~1)만큼 섞은 색으로 칠한다. emission > 0이면 이미시브가 있는 머티리얼은
        /// 원래 이미시브에 tint × emission을 더해 그늘 쪽까지 밝힌다 (흰색 번쩍임용).
        /// </summary>
        public void Apply(Color tint, float strength, float emission = 0f)
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Renderer == null) continue;
                Color color = Color.Lerp(BaseOf(slot), tint, strength);
                color.a = slot.Original.a;

                _block.Clear();
                if (slot.HasBaseColor) _block.SetColor(BaseColorId, color);
                if (slot.HasColor) _block.SetColor(ColorId, color);
                if (slot.HasEmission && emission > 0f) _block.SetColor(EmissionColorId, slot.OriginalEmission + tint * emission);
                slot.Renderer.SetPropertyBlock(_block, slot.MaterialIndex);
            }
            _applied = true;
        }

        /// <summary>상태 틴트를 지워 바탕색(없으면 머티리얼 원래 색)으로 되돌린다. 이미 그 상태면 아무것도 안 한다.</summary>
        public void Clear()
        {
            if (!_applied) return;
            // 바탕색이 있으면 바탕색으로, 없으면 빈 블록(머티리얼 원래 색)으로
            WriteBase();
            _applied = false;
        }
    }
}

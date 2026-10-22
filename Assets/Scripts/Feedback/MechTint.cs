using System.Collections.Generic;
using UnityEngine;

namespace MyDongari.Feedback
{
    /// <summary>
    /// 기체 전체 색 틴트. 머티리얼(공유 에셋)은 절대 건드리지 않고 MaterialPropertyBlock으로 색만 덮어쓴다.
    /// 그래서 Clear()로 블록을 비우면 언제든 원래 머티리얼 색으로 돌아간다 (에디터에서 머티리얼 에셋이 오염될 일도 없음).
    /// </summary>
    public class MechTint
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private struct Slot
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public Color Original;
            public bool HasBaseColor;
            public bool HasColor;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private bool _applied;

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
                        HasColor = hasColor
                    });
                }
            }
        }

        /// <summary>원래 색에서 tint 쪽으로 strength(0~1)만큼 섞은 색으로 칠한다.</summary>
        public void Apply(Color tint, float strength)
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Renderer == null) continue;
                Color color = Color.Lerp(slot.Original, tint, strength);
                color.a = slot.Original.a;

                _block.Clear();
                if (slot.HasBaseColor) _block.SetColor(BaseColorId, color);
                if (slot.HasColor) _block.SetColor(ColorId, color);
                slot.Renderer.SetPropertyBlock(_block, slot.MaterialIndex);
            }
            _applied = true;
        }

        /// <summary>덮어쓴 색을 지워 머티리얼 원래 색으로 되돌린다. 이미 원래 색이면 아무것도 안 한다.</summary>
        public void Clear()
        {
            if (!_applied) return;
            _block.Clear();
            foreach (Slot slot in _slots)
            {
                // 빈 블록을 넣으면 덮어쓴 값이 전부 사라진다
                if (slot.Renderer != null) slot.Renderer.SetPropertyBlock(_block, slot.MaterialIndex);
            }
            _applied = false;
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace MyDongari.UI
{
    public class LinearGauge : MaskableGraphic
    {
        [Range(0f, 1f)]
        public float fillAmount = 1f;
        public float thickness = 10f;
        public bool isBackground = false;
        public bool isVertical = false;
        public Color backgroundColor = new Color(1f, 1f, 1f, 0.15f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Color c = isBackground ? backgroundColor : color;
            float fill = isBackground ? 1f : fillAmount;

            float w = rectTransform.rect.width;
            float h = rectTransform.rect.height;

            Vector2 v0, v1, v2, v3;

            if (isVertical)
            {
                float filled = h * fill;
                v0 = new Vector2(-w * 0.5f, -h * 0.5f);
                v1 = new Vector2(w * 0.5f, -h * 0.5f);
                v2 = new Vector2(w * 0.5f, -h * 0.5f + filled);
                v3 = new Vector2(-w * 0.5f, -h * 0.5f + filled);
            }
            else
            {
                float filled = w * fill;
                v0 = new Vector2(-w * 0.5f, -thickness * 0.5f);
                v1 = new Vector2(-w * 0.5f + filled, -thickness * 0.5f);
                v2 = new Vector2(-w * 0.5f + filled, thickness * 0.5f);
                v3 = new Vector2(-w * 0.5f, thickness * 0.5f);
            }

            vh.AddVert(v0, c, Vector2.zero);
            vh.AddVert(v1, c, Vector2.zero);
            vh.AddVert(v2, c, Vector2.zero);
            vh.AddVert(v3, c, Vector2.zero);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }

        public void SetFill(float value)
        {
            fillAmount = Mathf.Clamp01(value);
            SetVerticesDirty();
        }

        public void SetColor(Color c)
        {
            color = c;
            SetVerticesDirty();
        }
    }
}
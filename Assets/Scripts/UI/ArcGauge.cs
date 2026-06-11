using UnityEngine;
using UnityEngine.UI;

namespace MyDongari.UI
{
    public class ArcGauge : MaskableGraphic
    {
        [Range(0f, 1f)]
        public float fillAmount = 1f;

        public float startAngle = 120f;
        public float endAngle = 420f;
        public float thickness = 12f;
        public int segments = 64;

        public bool isBackground = false;
        public bool reverseDirection = false;
        public Color backgroundColor = new Color(1f, 1f, 1f, 0.15f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            float total = endAngle - startAngle;

            if (isBackground)
            {
                DrawArc(vh, startAngle, endAngle, 1f);
            }
            else
            {
                if (reverseDirection)
                {
                    DrawArc(vh, endAngle - total * fillAmount, endAngle, 1f);
                }
                else
                {
                    DrawArc(vh, startAngle, startAngle + total * fillAmount, 1f);
                }
            }
        }

        private void DrawArc(VertexHelper vh, float from, float to, float alpha)
        {
            Color c = isBackground ? backgroundColor : color;

            float outerRX = rectTransform.rect.width * 0.5f;
            float outerRY = rectTransform.rect.height * 0.5f;
            float innerRX = outerRX - thickness;
            float innerRY = outerRY - thickness;

            for (int i = 0; i < segments; i++)
            {
                float t0 = (float)i / segments;
                float t1 = (float)(i + 1) / segments;

                float angle0 = Mathf.Lerp(from, to, t0) * Mathf.Deg2Rad;
                float angle1 = Mathf.Lerp(from, to, t1) * Mathf.Deg2Rad;

                Vector2 outer0 = new Vector2(Mathf.Cos(angle0) * outerRX, Mathf.Sin(angle0) * outerRY);
                Vector2 inner0 = new Vector2(Mathf.Cos(angle0) * innerRX, Mathf.Sin(angle0) * innerRY);
                Vector2 outer1 = new Vector2(Mathf.Cos(angle1) * outerRX, Mathf.Sin(angle1) * outerRY);
                Vector2 inner1 = new Vector2(Mathf.Cos(angle1) * innerRX, Mathf.Sin(angle1) * innerRY);

                int idx = vh.currentVertCount;

                vh.AddVert(inner0, c, Vector2.zero);
                vh.AddVert(outer0, c, Vector2.zero);
                vh.AddVert(outer1, c, Vector2.zero);
                vh.AddVert(inner1, c, Vector2.zero);

                vh.AddTriangle(idx, idx + 1, idx + 2);
                vh.AddTriangle(idx, idx + 2, idx + 3);
            }
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
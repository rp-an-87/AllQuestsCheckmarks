using UnityEngine;
using UnityEngine.UI;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// A dashed border drawn as one mesh: short dashes with short gaps along the edge, strongest at one corner and fading toward the
    /// opposite one (like the game's own wishlist outline). One component per item instead of one image per dash.
    /// </summary>
    internal class DashedBorder : MaskableGraphic
    {
        private float _dash = 3f;
        private float _gap = 2f;
        private float _thickness = 1f;
        private float _fade = 0.25f;
        private Settings.EDashCorner _corner = Settings.EDashCorner.BottomLeft;

        public void Configure(Color tint, float dash, float gap, float thickness, float fade, Settings.EDashCorner corner)
        {
            bool changed = _dash != dash || _gap != gap || _thickness != thickness || _fade != fade || _corner != corner;

            _dash = dash;
            _gap = gap;
            _thickness = thickness;
            _fade = fade;
            _corner = corner;

            if (color != tint)
            {
                color = tint;
            }

            if (changed)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            float w = rect.width;
            float h = rect.height;

            if (w < 4f || h < 4f)
            {
                return;
            }

            float thickness = Mathf.Min(_thickness, Mathf.Min(w, h) / 2f);
            float period = Mathf.Max(1f, _dash + _gap);
            float perimeter = 2f * (w + h);
            // the fade is measured to half the diagonal: the far corners (and the middle of the far edges) are at the faintest
            float reach = Mathf.Max(1f, Mathf.Sqrt(w * w + h * h) / 2f);
            Vector2 strongA = StrongPoint(rect, false);
            Vector2 strongB = StrongPoint(rect, true);

            // the border as one line: bottom edge (left to right), right edge (up), top edge (right to left), left edge (down)
            float[] edges = [0f, w, w + h, 2f * w + h, perimeter];

            for (float start = 0f; start < perimeter; start += period)
            {
                float end = Mathf.Min(start + _dash, perimeter);

                // a dash that runs around a corner is cut at the corner
                for (int i = 0; i < 4; i++)
                {
                    float from = Mathf.Max(start, edges[i]) - edges[i];
                    float to = Mathf.Min(end, edges[i + 1]) - edges[i];

                    if (to <= from)
                    {
                        continue;
                    }

                    float x0, y0, x1, y1;

                    switch (i)
                    {
                        case 0:
                            x0 = rect.xMin + from; x1 = rect.xMin + to; y0 = rect.yMin; y1 = rect.yMin + thickness;
                            break;
                        case 1:
                            y0 = rect.yMin + from; y1 = rect.yMin + to; x0 = rect.xMax - thickness; x1 = rect.xMax;
                            break;
                        case 2:
                            x1 = rect.xMax - from; x0 = rect.xMax - to; y0 = rect.yMax - thickness; y1 = rect.yMax;
                            break;
                        default:
                            y1 = rect.yMax - from; y0 = rect.yMax - to; x0 = rect.xMin; x1 = rect.xMin + thickness;
                            break;
                    }

                    Vector2 middle = new((x0 + x1) / 2f, (y0 + y1) / 2f);
                    float distance = _corner == Settings.EDashCorner.None
                        ? 0f
                        : Mathf.Min(Vector2.Distance(middle, strongA), Vector2.Distance(middle, strongB)) / reach;
                    float factor = Mathf.Lerp(1f, _fade, Mathf.Clamp01(distance));

                    Color32 tint = color;
                    tint.a = (byte)Mathf.Clamp(tint.a * factor, 0f, 255f);
                    AddQuad(vh, x0, y0, x1, y1, tint);
                }
            }
        }

        // The strong corner(s): one point, or two opposite ones (the second call gives the second one, or the same point for a single corner)
        private Vector2 StrongPoint(Rect rect, bool second)
        {
            return _corner switch
            {
                Settings.EDashCorner.Diagonal => second ? new Vector2(rect.xMax, rect.yMin) : new Vector2(rect.xMin, rect.yMax),
                Settings.EDashCorner.AntiDiagonal => second ? new Vector2(rect.xMin, rect.yMin) : new Vector2(rect.xMax, rect.yMax),
                Settings.EDashCorner.BottomRight => new Vector2(rect.xMax, rect.yMin),
                Settings.EDashCorner.TopLeft => new Vector2(rect.xMin, rect.yMax),
                Settings.EDashCorner.TopRight => new Vector2(rect.xMax, rect.yMax),
                _ => new Vector2(rect.xMin, rect.yMin),
            };
        }

        private static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 tint)
        {
            int index = vh.currentVertCount;

            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = tint;

            vertex.position = new Vector3(x0, y0);
            vh.AddVert(vertex);
            vertex.position = new Vector3(x0, y1);
            vh.AddVert(vertex);
            vertex.position = new Vector3(x1, y1);
            vh.AddVert(vertex);
            vertex.position = new Vector3(x1, y0);
            vh.AddVert(vertex);

            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}

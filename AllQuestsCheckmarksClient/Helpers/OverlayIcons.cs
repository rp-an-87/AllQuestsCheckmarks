using System;
using UnityEngine;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// The icons the game has no picture for, drawn once in code as white glyphs (so they take a color like the game's own icons):
    /// a clock (needed later), a hooded figure (Collector) and an arrow into a tray (hand in).
    /// </summary>
    internal static class OverlayIcons
    {
        private const int Size = 64;
        private const int Grid = 256;
        private const int Scale = Grid / Size;

        private static Sprite? _clock;
        private static Sprite? _collector;
        private static Sprite? _handIn;

        public static Sprite Clock => _clock ??= Make(DrawClock);
        public static Sprite Collector => _collector ??= Make(DrawCollector);
        public static Sprite HandIn => _handIn ??= Make(DrawHandIn);

        // A drawing surface at 4x the size: shapes are added or cut out with a test for "is this point inside", then it is scaled down for smooth edges
        private class Surface
        {
            private readonly bool[] _cells = new bool[Grid * Grid];

            public void Add(Func<float, float, bool> inside) => Apply(inside, true);

            public void Cut(Func<float, float, bool> inside) => Apply(inside, false);

            private void Apply(Func<float, float, bool> inside, bool value)
            {
                for (int y = 0; y < Grid; y++)
                {
                    for (int x = 0; x < Grid; x++)
                    {
                        if (inside((x + 0.5f) / Scale, (y + 0.5f) / Scale))
                        {
                            _cells[y * Grid + x] = value;
                        }
                    }
                }
            }

            public Texture2D ToTexture()
            {
                Color32[] pixels = new Color32[Size * Size];

                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int covered = 0;

                        for (int sy = 0; sy < Scale; sy++)
                        {
                            for (int sx = 0; sx < Scale; sx++)
                            {
                                if (_cells[(y * Scale + sy) * Grid + x * Scale + sx])
                                {
                                    covered++;
                                }
                            }
                        }

                        pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(covered * 255 / (Scale * Scale)));
                    }
                }

                Texture2D texture = new(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels32(pixels);
                texture.Apply();
                return texture;
            }
        }

        private static Sprite Make(Action<Surface> draw)
        {
            Surface surface = new();
            draw(surface);
            return Sprite.Create(surface.ToTexture(), new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---- shapes (y goes up)

        private static Func<float, float, bool> Circle(float cx, float cy, float r) =>
            (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

        private static Func<float, float, bool> Ring(float cx, float cy, float r, float width) =>
            (x, y) =>
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                return Mathf.Abs(d - r) <= width / 2f;
            };

        private static Func<float, float, bool> Ellipse(float cx, float cy, float rx, float ry) =>
            (x, y) => ((x - cx) * (x - cx)) / (rx * rx) + ((y - cy) * (y - cy)) / (ry * ry) <= 1f;

        private static Func<float, float, bool> Segment(float x1, float y1, float x2, float y2, float width) =>
            (x, y) =>
            {
                float dx = x2 - x1;
                float dy = y2 - y1;
                float t = Mathf.Clamp01(((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy));
                float px = x1 + t * dx - x;
                float py = y1 + t * dy - y;
                return px * px + py * py <= (width / 2f) * (width / 2f);
            };

        private static Func<float, float, bool> Polygon(params float[] points) =>
            (x, y) =>
            {
                bool inside = false;
                int count = points.Length / 2;

                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    float xi = points[i * 2], yi = points[i * 2 + 1], xj = points[j * 2], yj = points[j * 2 + 1];

                    if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    {
                        inside = !inside;
                    }
                }

                return inside;
            };

        private static void DrawClock(Surface s)
        {
            s.Add(Ring(32, 32, 27, 6));
            s.Add(Segment(32, 32, 32, 49, 5));
            s.Add(Segment(32, 32, 45, 32, 5));
            s.Add(Circle(32, 32, 4));
        }

        // a hood over a dark face, on a cloak
        private static void DrawCollector(Surface s)
        {
            s.Add(Polygon(8, 4, 56, 4, 47, 34, 17, 34));
            s.Add(Circle(32, 38, 19));
            s.Cut(Ellipse(32, 37, 9.5f, 11.5f));
            s.Add(Circle(27.5f, 39, 2.2f));
            s.Add(Circle(36.5f, 39, 2.2f));
        }

        // an arrow down into a tray
        private static void DrawHandIn(Surface s)
        {
            s.Add(Segment(32, 60, 32, 32, 9));
            s.Add(Polygon(16, 34, 48, 34, 32, 14));
            s.Add(Segment(9, 20, 9, 6, 5.5f));
            s.Add(Segment(9, 6, 55, 6, 5.5f));
            s.Add(Segment(55, 6, 55, 20, 5.5f));
        }
    }
}

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniPlanes.Editor
{
    /// <summary>
    /// Draws the starter UI sprites into Assets/MiniPlanes/Art/UI: panels, a coin, a heart, a lock, the
    /// balloon icons and the crosshair. A file that already exists is never redrawn, so replace any of
    /// them with your own art (same name) and it stays.
    /// </summary>
    public static class MiniPlanesUiSprites
    {
        public const string Dir = "Assets/MiniPlanes/Art/UI";

        static readonly Color Ink = new Color(0.2f, 0.16f, 0.14f, 1f);
        static readonly Color Cream = new Color(0.98f, 0.95f, 0.88f, 1f);

        delegate float Sdf(Vector2 p);

        struct Layer
        {
            public Sdf shape;
            public Color fill;
            public float outline;
        }

        public static void EnsureAll()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/MiniPlanes/Art", "UI");
            Write("White", 8, p => -1f, Color.white, 0f, 0);
            Write("Rounded", 96, p => RoundedBox(p, Vector2.zero, new Vector2(1f, 1f), 0.5f), Color.white, 0f, 24);
            Write("Circle", 64, p => Circle(p, Vector2.zero, 0.97f), Color.white, 0f, 0);
            Write("Ring", 64, p => Mathf.Abs(Circle(p, Vector2.zero, 0.84f)) - 0.12f, Color.white, 0f, 0);
            Icon("Heart", 128, new[]
            {
                L(p => Mathf.Min(Mathf.Min(Circle(p, new Vector2(-0.3f, 0.22f), 0.36f), Circle(p, new Vector2(0.3f, 0.22f), 0.36f)),
                    RotatedBox(p, new Vector2(0f, -0.14f), 0.46f, 45f)), new Color(0.94f, 0.36f, 0.33f), 0.07f),
                L(p => Circle(p, new Vector2(-0.32f, 0.3f), 0.1f), new Color(1f, 0.75f, 0.7f), 0f)
            });
            Icon("Coin", 128, new[]
            {
                L(p => Circle(p, Vector2.zero, 0.82f), new Color(0.98f, 0.76f, 0.2f), 0.07f),
                L(p => Mathf.Abs(Circle(p, Vector2.zero, 0.56f)) - 0.05f, new Color(0.86f, 0.58f, 0.1f), 0f),
                L(p => RoundedBox(p, new Vector2(0f, 0f), new Vector2(0.1f, 0.34f), 0.08f), new Color(0.86f, 0.58f, 0.1f), 0f),
                L(p => Circle(p, new Vector2(-0.34f, 0.36f), 0.1f), new Color(1f, 0.93f, 0.62f), 0f)
            });
            Icon("Lock", 128, new[]
            {
                L(p => p.y < 0.05f ? 9f : Mathf.Abs(Circle(p, new Vector2(0f, 0.22f), 0.34f)) - 0.08f, new Color(0.56f, 0.52f, 0.47f), 0.05f),
                L(p => RoundedBox(p, new Vector2(0f, -0.22f), new Vector2(0.52f, 0.4f), 0.12f), new Color(0.56f, 0.52f, 0.47f), 0.06f),
                L(p => Circle(p, new Vector2(0f, -0.18f), 0.09f), Cream, 0f)
            });
            Icon("ShieldIcon", 256, Badge(new Color(0.83f, 0.9f, 1f), new[]
            {
                L(p => Polygon(p * 1.6f, ShieldOutline) / 1.6f, new Color(0.25f, 0.55f, 0.95f), 0.05f),
                L(p => Polygon(p * 2.6f, ShieldOutline) / 2.6f, new Color(0.62f, 0.82f, 1f), 0f)
            }));
            Icon("BombIcon", 256, Badge(new Color(1f, 0.86f, 0.82f), new[]
            {
                L(p => Segment(p, new Vector2(0.18f, 0.24f), new Vector2(0.34f, 0.44f), 0.06f), new Color(0.45f, 0.33f, 0.24f), 0.04f),
                L(p => Circle(p, new Vector2(-0.06f, -0.08f), 0.42f), new Color(0.22f, 0.2f, 0.22f), 0.05f),
                L(p => RoundedBox(p, new Vector2(0.15f, 0.18f), new Vector2(0.1f, 0.08f), 0.03f), new Color(0.4f, 0.4f, 0.44f), 0.03f),
                L(p => Circle(p, new Vector2(-0.22f, 0.06f), 0.09f), new Color(0.55f, 0.55f, 0.6f), 0f),
                L(p => Star(p, new Vector2(0.4f, 0.52f), 0.17f, 0.08f, 5), new Color(1f, 0.8f, 0.2f), 0.035f)
            }));
            Icon("TurretIcon", 256, Badge(new Color(1f, 0.93f, 0.7f), new[]
            {
                L(p => RoundedBox(p, new Vector2(0.24f, 0.02f), new Vector2(0.3f, 0.1f), 0.06f), new Color(0.3f, 0.3f, 0.33f), 0.04f),
                L(p => RoundedBox(p, new Vector2(0.52f, 0.02f), new Vector2(0.07f, 0.14f), 0.05f), new Color(0.98f, 0.74f, 0.14f), 0.04f),
                L(p => RoundedBox(p, new Vector2(-0.12f, -0.26f), new Vector2(0.42f, 0.08f), 0.06f), new Color(0.3f, 0.3f, 0.33f), 0.04f),
                L(p => Mathf.Max(Ellipse(p, new Vector2(-0.12f, -0.12f), new Vector2(0.46f, 0.38f)), -0.16f - p.y), new Color(0.97f, 0.92f, 0.82f), 0.045f),
                L(p => RoundedBox(p, new Vector2(-0.2f, 0.12f), new Vector2(0.1f, 0.035f), 0.03f), new Color(0.98f, 0.74f, 0.14f), 0f),
                L(p => RoundedBox(p, new Vector2(-0.04f, 0.16f), new Vector2(0.1f, 0.035f), 0.03f), new Color(0.98f, 0.74f, 0.14f), 0f)
            }));
            Write("Crosshair", 128, p =>
            {
                float ring = Mathf.Abs(Circle(p, Vector2.zero, 0.72f)) - 0.05f;
                float ticks = Mathf.Min(RoundedBox(p, Vector2.zero, new Vector2(0.04f, 0.98f), 0f), RoundedBox(p, Vector2.zero, new Vector2(0.98f, 0.04f), 0f));
                ticks = Mathf.Max(ticks, 0.46f - p.magnitude);
                return Mathf.Min(Mathf.Min(ring, ticks), Circle(p, Vector2.zero, 0.09f));
            }, Color.white, 0f, 0);
            AssetDatabase.Refresh();
            ConfigureImports();
        }

        static readonly Vector2[] ShieldOutline =
        {
            new Vector2(-0.55f, 0.62f), new Vector2(0.55f, 0.62f), new Vector2(0.55f, 0.08f), new Vector2(0.45f, -0.28f),
            new Vector2(0.24f, -0.56f), new Vector2(0f, -0.72f), new Vector2(-0.24f, -0.56f), new Vector2(-0.45f, -0.28f),
            new Vector2(-0.55f, 0.08f)
        };

        static Layer L(Sdf shape, Color fill, float outline)
        {
            return new Layer { shape = shape, fill = fill, outline = outline };
        }

        /// <summary>A round cream badge with an ink rim, and the symbol drawn on top of it.</summary>
        static Layer[] Badge(Color tint, Layer[] symbol)
        {
            var layers = new Layer[symbol.Length + 1];
            layers[0] = L(p => Circle(p, Vector2.zero, 0.9f), Color.Lerp(Cream, tint, 0.55f), 0.07f);
            for (int i = 0; i < symbol.Length; i++)
            {
                var s = symbol[i];
                layers[i + 1] = L(p => s.shape(p / 0.92f) * 0.92f, s.fill, s.outline);
            }
            return layers;
        }

        static void Write(string name, int size, Sdf shape, Color color, float outline, int border)
        {
            Icon(name, size, new[] { L(shape, color, outline) }, border);
        }

        static void Icon(string name, int size, Layer[] layers, int border = 0)
        {
            string path = Dir + "/" + name + ".png";
            if (File.Exists(path)) return;
            var px = new Color[size * size];
            float pixel = 2f / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);
                    Color c = new Color(Ink.r, Ink.g, Ink.b, 0f);
                    foreach (var layer in layers)
                    {
                        float d = layer.shape(p);
                        if (layer.outline > 0f)
                        {
                            float rim = Mathf.Clamp01(0.5f - (d - layer.outline) / pixel);
                            c = Over(c, Ink, rim);
                        }
                        float fill = Mathf.Clamp01(0.5f - d / pixel);
                        c = Over(c, layer.fill, fill);
                    }
                    px[y * size + x] = c;
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (border > 0)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteBorder = new Vector4(border, border, border, border);
                importer.SaveAndReimport();
            }
        }

        static Color Over(Color under, Color over, float coverage)
        {
            float a = over.a * coverage;
            float outA = a + under.a * (1f - a);
            if (outA < 1e-5f) return new Color(over.r, over.g, over.b, 0f);
            Color rgb = (over * a + under * under.a * (1f - a)) / outA;
            rgb.a = outA;
            return rgb;
        }

        static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool dirty = importer.textureType != TextureImporterType.Sprite || !importer.alphaIsTransparency ||
                             importer.wrapMode != TextureWrapMode.Clamp || !importer.mipmapEnabled;
                if (Path.GetFileNameWithoutExtension(path) == "Rounded" && importer.spriteBorder == Vector4.zero) dirty = true;
                if (!dirty) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                if (Path.GetFileNameWithoutExtension(path) == "Rounded") importer.spriteBorder = new Vector4(24, 24, 24, 24);
                importer.SaveAndReimport();
            }
        }

        public static Sprite Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(Dir + "/" + name + ".png");
        }

        // ---------------------------------------------------------------- signed distances

        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
        {
            Vector2 q = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
            return (q.magnitude - 1f) * Mathf.Min(r.x, r.y);
        }

        static float RoundedBox(Vector2 p, Vector2 c, Vector2 half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + Vector2.one * radius;
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static float RotatedBox(Vector2 p, Vector2 c, float half, float deg)
        {
            float a = -deg * Mathf.Deg2Rad;
            Vector2 d = p - c;
            Vector2 r = new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
            return RoundedBox(r, Vector2.zero, new Vector2(half, half), 0.06f);
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude - r;
        }

        static float Star(Vector2 p, Vector2 c, float outer, float inner, int points)
        {
            Vector2 d = p - c;
            float a = Mathf.Atan2(d.y, d.x);
            float sector = Mathf.PI * 2f / points;
            float f = Mathf.Abs(Mathf.Repeat(a + sector * 0.25f, sector) / sector - 0.5f) * 2f;
            return d.magnitude - Mathf.Lerp(inner, outer, f);
        }

        static float Polygon(Vector2 p, Vector2[] v)
        {
            float d = (p - v[0]).sqrMagnitude;
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i];
                Vector2 w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / e.sqrMagnitude);
                d = Mathf.Min(d, b.sqrMagnitude);
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }
    }
}

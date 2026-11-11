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
            WriteComic("RoundedComic", 96, 24);
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
            Icon("LifeIcon", 256, Badge(new Color(1f, 0.8f, 0.84f), new[]
            {
                L(p => Mathf.Min(Mathf.Min(Circle(p, new Vector2(-0.24f, 0.16f), 0.29f), Circle(p, new Vector2(0.24f, 0.16f), 0.29f)),
                    RotatedBox(p, new Vector2(0f, -0.12f), 0.37f, 45f)), new Color(0.94f, 0.33f, 0.4f), 0.05f),
                L(p => Circle(p, new Vector2(-0.26f, 0.24f), 0.08f), new Color(1f, 0.75f, 0.78f), 0f)
            }));
            // The drag handle on a booster tube: a ring with arrows pointing four ways.
            Write("DragCursor", 128, p =>
            {
                float ring = Mathf.Abs(Circle(p, Vector2.zero, 0.5f)) - 0.05f;
                float arrows = 9f;
                for (int i = 0; i < 4; i++)
                {
                    var rot = Quaternion.Euler(0f, 0f, 90f * i);
                    Vector2 a = rot * new Vector3(0.72f, 0f, 0f), b = rot * new Vector3(0.95f, 0f, 0f);
                    Vector2 s1 = rot * new Vector3(0.72f, 0.14f, 0f), s2 = rot * new Vector3(0.72f, -0.14f, 0f);
                    arrows = Mathf.Min(arrows, Polygon(p, new[] { b, s1, s2 }));
                    arrows = Mathf.Min(arrows, Segment(p, rot * new Vector3(0.55f, 0f, 0f), a, 0.04f));
                }
                return Mathf.Min(Mathf.Min(ring, arrows), Circle(p, Vector2.zero, 0.12f));
            }, Color.white, 0f, 0);

            // Over a bomb balloon: don't touch it (a hand with a red no-entry slash), then cup it with two hands.
            Icon("NoTouchIcon", 256, Badge(new Color(1f, 0.9f, 0.86f), new[]
            {
                L(p => Hand(p * 1.05f) / 1.05f, new Color(0.93f, 0.64f, 0.48f), 0.05f),
                L(p => Mathf.Abs(Circle(p, Vector2.zero, 0.74f)) - 0.075f, new Color(0.88f, 0.2f, 0.18f), 0.03f),
                L(p => Segment(p, new Vector2(-0.52f, 0.52f), new Vector2(0.52f, -0.52f), 0.075f), new Color(0.88f, 0.2f, 0.18f), 0.03f)
            }));
            Icon("CupHand", 128, new[]
            {
                L(p => Hand(new Vector2(p.y, -p.x) * 1.05f) / 1.05f, new Color(0.93f, 0.64f, 0.48f), 0.07f)
            });
            Icon("CupBadge", 256, Badge(new Color(1f, 0.82f, 0.78f), new Layer[0]));

            // Over a jamming tower: a wi-fi signal, struck through.
            Icon("JamIcon", 256, Badge(new Color(0.85f, 0.88f, 0.95f), new[]
            {
                L(p => Mathf.Min(Mathf.Min(SignalArc(p, 0.66f), SignalArc(p, 0.43f)), Mathf.Min(SignalArc(p, 0.2f), Circle(p, new Vector2(0f, -0.4f), 0.09f))),
                    new Color(0.2f, 0.36f, 0.62f), 0f),
                L(p => Segment(p, new Vector2(-0.52f, 0.52f), new Vector2(0.52f, -0.52f), 0.075f), new Color(0.88f, 0.2f, 0.18f), 0.03f)
            }));

            // The plane knob and finish flag on the HUD's progress bar.
            Icon("PlaneBadge", 256, Badge(new Color(0.75f, 0.95f, 0.9f), new[]
            {
                L(p => RoundedBox(p, new Vector2(0.06f, 0f), new Vector2(0.14f, 0.56f), 0.1f), Teal, 0.045f),
                L(p => RoundedBox(p, new Vector2(-0.44f, 0f), new Vector2(0.08f, 0.24f), 0.06f), Teal, 0.045f),
                L(p => RoundedBox(p, new Vector2(0f, 0f), new Vector2(0.56f, 0.12f), 0.12f), Teal, 0.045f),
                L(p => Ellipse(p, new Vector2(0.2f, 0f), new Vector2(0.13f, 0.07f)), Plum, 0f),
                L(p => RoundedBox(p, new Vector2(0.6f, 0f), new Vector2(0.03f, 0.2f), 0.03f), Cream, 0.03f)
            }));
            Icon("FlagBadge", 256, Badge(new Color(0.9f, 0.9f, 0.9f), new[]
            {
                L(p => Segment(p, new Vector2(-0.34f, -0.6f), new Vector2(-0.34f, 0.56f), 0.045f), new Color(0.45f, 0.36f, 0.3f), 0.03f),
                L(p => RoundedBox(p, new Vector2(0.06f, 0.24f), new Vector2(0.38f, 0.27f), 0.03f), Color.white, 0.045f),
                L(p => Checker(p, new Vector2(-0.32f, -0.03f), 4, 2, 0.19f, 0.27f), Ink, 0f)
            }));

            // Pictures used in the tutorials' intro text.
            Icon("FighterIcon", 256, Badge(new Color(1f, 0.82f, 0.76f), new[]
            {
                L(p => Polygon(p, FighterOutline), Coral, 0.045f),
                L(p => Ellipse(p, new Vector2(0.24f, 0f), new Vector2(0.13f, 0.065f)), Plum, 0f)
            }));
            Icon("BomberIcon", 256, Badge(new Color(1f, 0.8f, 0.74f), new[]
            {
                L(p => RoundedBox(p, new Vector2(0.04f, 0f), new Vector2(0.15f, 0.66f), 0.08f), DarkCoral, 0.045f),
                L(p => RoundedBox(p, new Vector2(-0.5f, 0f), new Vector2(0.08f, 0.28f), 0.05f), DarkCoral, 0.045f),
                L(p => RoundedBox(p, new Vector2(0f, 0f), new Vector2(0.6f, 0.14f), 0.14f), DarkCoral, 0.045f),
                L(p => Mathf.Min(Circle(p, new Vector2(0.14f, 0.36f), 0.1f), Circle(p, new Vector2(0.14f, -0.36f), 0.1f)), Cream, 0.035f),
                L(p => Ellipse(p, new Vector2(0.32f, 0f), new Vector2(0.12f, 0.07f)), Plum, 0f)
            }));
            Icon("ZeppelinIcon", 256, Badge(new Color(1f, 0.84f, 0.78f), new[]
            {
                L(p => Polygon(p, ZeppelinFin), Coral, 0.04f),
                L(p => RoundedBox(p, new Vector2(0.05f, -0.27f), new Vector2(0.2f, 0.08f), 0.06f), Cream, 0.04f),
                L(p => Ellipse(p, new Vector2(0.02f, 0.08f), new Vector2(0.66f, 0.3f)), Coral, 0.045f),
                L(p => Mathf.Max(Ellipse(p, new Vector2(0.02f, 0.08f), new Vector2(0.64f, 0.28f)), Mathf.Abs(p.x + 0.36f) - 0.045f), Cream, 0f),
                L(p => Mathf.Max(Ellipse(p, new Vector2(0.02f, 0.08f), new Vector2(0.64f, 0.28f)), Mathf.Abs(p.x + 0.5f) - 0.03f), Cream, 0f),
                L(p => Mathf.Min(RoundedBox(p, new Vector2(-0.02f, -0.27f), new Vector2(0.04f, 0.03f), 0.01f),
                    RoundedBox(p, new Vector2(0.12f, -0.27f), new Vector2(0.04f, 0.03f), 0.01f)), Plum, 0f)
            }));
            Icon("TubeIcon", 256, Badge(new Color(1f, 0.88f, 0.72f), new[]
            {
                L(p => RoundedBox(p, Vector2.zero, new Vector2(0.5f, 0.27f), 0.04f), new Color(1f, 0.93f, 0.8f), 0.04f),
                L(p => Mathf.Abs(Ellipse(p, new Vector2(-0.5f, 0f), new Vector2(0.1f, 0.3f))) - 0.05f, Orange, 0.03f),
                L(p => Mathf.Abs(Ellipse(p, new Vector2(0.5f, 0f), new Vector2(0.1f, 0.3f))) - 0.05f, Orange, 0.03f),
                L(p => Mathf.Abs(Ellipse(p, Vector2.zero, new Vector2(0.08f, 0.27f))) - 0.025f, Orange, 0f),
                L(p => Segment(p, new Vector2(-0.28f, 0.1f), new Vector2(0.28f, 0.1f), 0.02f), Color.white, 0f)
            }));
            Icon("MissileIcon", 256, Badge(new Color(1f, 0.86f, 0.78f), new[]
            {
                L(p => Ellipse(p, new Vector2(-0.62f, 0f), new Vector2(0.14f, 0.08f)), new Color(1f, 0.78f, 0.25f), 0.03f),
                L(p => Polygon(p, MissileFins), Coral, 0.04f),
                L(p => RoundedBox(p, new Vector2(-0.06f, 0f), new Vector2(0.44f, 0.13f), 0.1f), Cream, 0.045f),
                L(p => Ellipse(p, new Vector2(0.4f, 0f), new Vector2(0.22f, 0.13f)), Coral, 0.045f),
                L(p => RoundedBox(p, new Vector2(0.12f, 0f), new Vector2(0.03f, 0.13f), 0f), Coral, 0f)
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

        static readonly Color Teal = new Color(0.24f, 0.62f, 0.56f, 1f);
        static readonly Color Coral = new Color(0.9f, 0.42f, 0.32f, 1f);
        static readonly Color DarkCoral = new Color(0.74f, 0.28f, 0.24f, 1f);
        static readonly Color Plum = new Color(0.42f, 0.24f, 0.3f, 1f);
        static readonly Color Orange = new Color(1f, 0.56f, 0.22f, 1f);

        static readonly Vector2[] FighterOutline =
        {
            new Vector2(0.64f, 0f), new Vector2(0.4f, 0.09f), new Vector2(0.08f, 0.11f), new Vector2(-0.22f, 0.6f),
            new Vector2(-0.36f, 0.58f), new Vector2(-0.2f, 0.11f), new Vector2(-0.46f, 0.09f), new Vector2(-0.6f, 0.3f),
            new Vector2(-0.7f, 0.28f), new Vector2(-0.62f, 0f), new Vector2(-0.7f, -0.28f), new Vector2(-0.6f, -0.3f),
            new Vector2(-0.46f, -0.09f), new Vector2(-0.2f, -0.11f), new Vector2(-0.36f, -0.58f), new Vector2(-0.22f, -0.6f),
            new Vector2(0.08f, -0.11f), new Vector2(0.4f, -0.09f)
        };

        static readonly Vector2[] ZeppelinFin =
        {
            new Vector2(-0.46f, 0.22f), new Vector2(-0.7f, 0.5f), new Vector2(-0.8f, 0.46f), new Vector2(-0.72f, 0.08f),
            new Vector2(-0.8f, -0.24f), new Vector2(-0.7f, -0.26f), new Vector2(-0.46f, -0.04f)
        };

        static readonly Vector2[] MissileFins =
        {
            new Vector2(-0.3f, 0.1f), new Vector2(-0.5f, 0.32f), new Vector2(-0.58f, 0.3f), new Vector2(-0.5f, 0f),
            new Vector2(-0.58f, -0.3f), new Vector2(-0.5f, -0.32f), new Vector2(-0.3f, -0.1f)
        };

        /// <summary>One wi-fi arc: a ring segment opening upward, centred low in the badge.</summary>
        static float SignalArc(Vector2 p, float radius)
        {
            Vector2 c = new Vector2(0f, -0.4f);
            Vector2 d = p - c;
            float ring = Mathf.Abs(d.magnitude - radius) - 0.06f;
            // Keep only the upward-facing 90 degrees.
            float angle = Mathf.Atan2(Mathf.Abs(d.x), d.y);
            float wedge = (angle - 0.785f) * d.magnitude;
            return Mathf.Max(ring, wedge);
        }

        /// <summary>An open hand, fingers up, palm toward the viewer.</summary>
        static float Hand(Vector2 p)
        {
            float d = RoundedBox(p, new Vector2(0f, -0.2f), new Vector2(0.27f, 0.27f), 0.12f);
            d = Mathf.Min(d, Segment(p, new Vector2(-0.19f, 0f), new Vector2(-0.22f, 0.4f), 0.07f));
            d = Mathf.Min(d, Segment(p, new Vector2(-0.065f, 0.02f), new Vector2(-0.07f, 0.52f), 0.07f));
            d = Mathf.Min(d, Segment(p, new Vector2(0.065f, 0.02f), new Vector2(0.07f, 0.48f), 0.07f));
            d = Mathf.Min(d, Segment(p, new Vector2(0.19f, 0f), new Vector2(0.21f, 0.32f), 0.065f));
            d = Mathf.Min(d, Segment(p, new Vector2(0.22f, -0.28f), new Vector2(0.44f, -0.02f), 0.075f));
            return d;
        }

        /// <summary>A grid of alternating filled squares, starting at the bottom-left corner.</summary>
        static float Checker(Vector2 p, Vector2 corner, int cols, int rows, float w, float h)
        {
            float d = 9f;
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    if ((x + y) % 2 == 0)
                        d = Mathf.Min(d, RoundedBox(p, corner + new Vector2((x + 0.5f) * w, (y + 0.5f) * h), new Vector2(w * 0.5f, h * 0.5f), 0f));
            return d;
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
                if (Path.GetFileNameWithoutExtension(path) == "RoundedComic") continue;
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

        /// <summary>
        /// The comic panel sprite: the same rounded box as "Rounded" (and the same 9-slice border), with the
        /// comic shading stored as amounts for the UIComic shader: red the cel shade (lower in a band along the
        /// bottom), green a highlight band along the top, blue the ink outline. The bands sit inside the top and
        /// bottom borders, so they keep their thickness however tall the panel is.
        /// </summary>
        static void WriteComic(string name, int size, int border)
        {
            string path = Dir + "/" + name + ".png";
            if (File.Exists(path)) return;
            var px = new Color[size * size];
            float pixel = 2f / size;
            const float ink = 0.13f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);
                    float d = RoundedBox(p, Vector2.zero, new Vector2(1f, 1f), 0.5f);
                    float alpha = Mathf.Clamp01(0.5f - d / pixel);
                    float depth = -d;   // how far inside the edge
                    float outline = Mathf.Clamp01(0.5f - (depth - ink) / pixel);
                    // A glossy band just inside the outline along the top.
                    float top = Mathf.Clamp01((p.y - 0.55f) / 0.05f);
                    float gloss = Mathf.Clamp01(0.5f - (depth - (ink + 0.15f)) / pixel) * Mathf.Clamp01(0.5f + (depth - (ink + 0.04f)) / pixel) * top;
                    // A cel shade along the bottom.
                    float bottom = Mathf.Clamp01((-0.55f - p.y) / 0.05f);
                    float shade = Mathf.Clamp01(0.5f - (depth - (ink + 0.26f)) / pixel) * bottom;
                    px[y * size + x] = new Color(1f - 0.24f * shade, gloss, outline, alpha);
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.sRGBTexture = false;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes.Editor
{
    /// <summary>
    /// Creates the editable UI prefabs in Assets/MiniPlanes/Prefabs/UI: the menu (with Hangar), the cockpit
    /// HUD, the pause panel, the wrist button, balloon icons, the crosshair and the fuel gauge. A prefab that
    /// already exists is left exactly as it is, so your edits survive rebuilding the scene. Delete a prefab
    /// to get the generated version back.
    /// </summary>
    public static class MiniPlanesUiBuilder
    {
        public const string PrefabDir = "Assets/MiniPlanes/Prefabs/UI";
        const float CanvasScale = 0.0005f;   // metres per canvas unit

        static readonly Color Paper = new Color(0.98f, 0.95f, 0.89f, 0.97f);
        static readonly Color Ink = new Color(0.25f, 0.2f, 0.17f, 1f);
        static readonly Color Muted = new Color(0.56f, 0.52f, 0.47f, 1f);
        static readonly Color Teal = new Color(0.24f, 0.58f, 0.54f, 1f);
        static readonly Color TealDark = new Color(0.16f, 0.44f, 0.41f, 1f);
        static readonly Color Seafoam = new Color(0.56f, 0.82f, 0.68f, 1f);
        static readonly Color Coral = new Color(0.86f, 0.42f, 0.33f, 1f);
        static readonly Color CoralDark = new Color(0.68f, 0.3f, 0.23f, 1f);
        static readonly Color Slate = new Color(0.36f, 0.42f, 0.42f, 1f);
        static readonly Color SlateDark = new Color(0.24f, 0.3f, 0.3f, 1f);
        static readonly Color Locked = new Color(0.88f, 0.85f, 0.79f, 1f);
        static readonly Color Gold = new Color(0.86f, 0.6f, 0.12f, 1f);
        static readonly Color HudPlate = new Color(0.07f, 0.17f, 0.17f, 0.55f);
        static readonly Color HudCream = new Color(0.98f, 0.94f, 0.86f, 1f);
        static readonly Color HudAccent = new Color(0.36f, 0.85f, 0.72f, 1f);

        static Font _font, _heading;
        static Sprite _rounded, _white, _circle;

        public class Prefabs
        {
            public GameObject menu, hud, pause, wrist, shieldIcon, bombIcon, turretIcon, crosshair, fuelGauge;
        }

        public static Prefabs EnsureAll(GameArt art)
        {
            MiniPlanesUiSprites.EnsureAll();
            if (!AssetDatabase.IsValidFolder("Assets/MiniPlanes/Prefabs")) AssetDatabase.CreateFolder("Assets/MiniPlanes", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/MiniPlanes/Prefabs", "UI");

            _font = art.hudFont != null ? art.hudFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _heading = art.headingFont != null ? art.headingFont : _font;
            _rounded = MiniPlanesUiSprites.Load("Rounded");
            _white = MiniPlanesUiSprites.Load("White");
            _circle = MiniPlanesUiSprites.Load("Circle");

            return new Prefabs
            {
                menu = Ensure("MenuPanel", BuildMenu),
                hud = Ensure("CockpitHUD", BuildHud),
                pause = Ensure("PausePanel", BuildPause),
                wrist = Ensure("WristMenuButton", BuildWrist),
                shieldIcon = Ensure("ShieldIcon", () => BuildIcon("ShieldIcon", "ShieldIcon")),
                bombIcon = Ensure("BombIcon", () => BuildIcon("BombIcon", "BombIcon")),
                turretIcon = Ensure("TurretIcon", () => BuildIcon("TurretIcon", "TurretIcon")),
                crosshair = Ensure("Crosshair", BuildCrosshair),
                fuelGauge = Ensure("FuelGauge", BuildFuelGauge)
            };
        }

        static GameObject Ensure(string name, Func<GameObject> build)
        {
            string path = PrefabDir + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var go = build();
            foreach (var curved in go.GetComponentsInChildren<CurvedCanvas>(true)) curved.EnsureEffects();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("[MiniPlanes] Created UI prefab " + path);
            return prefab;
        }

        // ---------------------------------------------------------------- shared pieces

        /// <summary>A follow root, and a curved poke canvas on it at the given distance and height.</summary>
        static RectTransform CurvedPanel(GameObject root, string name, float distance, float height, Vector2 size, int order,
            out LazyFollow follow, out PokeCanvas poke)
        {
            var followGo = new GameObject("Follow");
            followGo.transform.SetParent(root.transform, false);
            follow = followGo.AddComponent<LazyFollow>();

            var canvasGo = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)canvasGo.transform;
            rect.SetParent(followGo.transform, false);
            rect.localPosition = new Vector3(0f, height, distance);
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * CanvasScale;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = order;
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2.5f;
            var curved = canvasGo.AddComponent<CurvedCanvas>();
            curved.radius = distance;
            poke = canvasGo.AddComponent<PokeCanvas>();
            return rect;
        }

        static RectTransform FlatCanvas(Transform parent, string name, Vector2 size, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * CanvasScale;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = order;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2.5f;
            return rect;
        }

        static void AddCursors(RectTransform canvas, PokeCanvas poke)
        {
            var cursors = new Graphic[2];
            for (int i = 0; i < 2; i++)
            {
                var c = Img(canvas, "Cursor" + i, _circle, new Color(Teal.r, Teal.g, Teal.b, 0.8f), r =>
                {
                    r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                    r.sizeDelta = new Vector2(26f, 26f);
                });
                c.enabled = false;
                cursors[i] = c;
            }
            poke.cursors = cursors;
        }

        static RectTransform Page(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            Stretch(r);
            return r;
        }

        static Image Img(RectTransform parent, string name, Sprite sprite, Color color, Action<RectTransform> layout)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
            layout(r);
            return image;
        }

        static Text Txt(RectTransform parent, string name, string text, int size, Font font, Color color, TextAnchor anchor, Action<RectTransform> layout)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            layout(r);
            return t;
        }

        static PokeButton Btn(RectTransform parent, string id, string label, int size, Color normal, Color pressed, Action<RectTransform> layout, out Text labelText)
        {
            var bg = Img(parent, "Button_" + id, _rounded, normal, layout);
            var b = bg.gameObject.AddComponent<PokeButton>();
            b.id = id;
            b.background = bg;
            b.normalColor = normal;
            b.pressedColor = pressed;
            b.disabledColor = Locked;
            labelText = null;
            if (!string.IsNullOrEmpty(label))
                labelText = Txt(bg.rectTransform, "Label", label, size, _heading, Color.white, TextAnchor.MiddleCenter, Stretch);
            return b;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        /// <summary>Pins by distance from the top-left corner, in canvas units.</summary>
        static Action<RectTransform> At(float x, float y, float w, float h)
        {
            return r =>
            {
                r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = new Vector2(x, -y);
                r.sizeDelta = new Vector2(w, h);
            };
        }

        /// <summary>Places by centre, relative to the parent's centre, in canvas units.</summary>
        static Action<RectTransform> Centre(float x, float y, float w, float h)
        {
            return r =>
            {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.anchoredPosition = new Vector2(x, y);
                r.sizeDelta = new Vector2(w, h);
            };
        }

        static void CoinSlot(RectTransform page, float x, float y, out Text text)
        {
            Img(page, "CoinIcon", MiniPlanesUiSprites.Load("Coin"), Color.white, At(x, y, 46f, 46f));
            text = Txt(page, "Coins", "0", 38, _heading, Gold, TextAnchor.MiddleLeft, At(x + 54f, y - 4f, 120f, 54f));
        }

        // ---------------------------------------------------------------- menu

        const float MenuW = 1000f, MenuH = 660f;

        static GameObject BuildMenu()
        {
            var root = new GameObject("MenuPanel");
            var menu = root.AddComponent<MenuPanel>();
            var canvas = CurvedPanel(root, "Canvas", 0.46f, -0.07f, new Vector2(MenuW, MenuH), 10, out var follow, out var poke);
            follow.deadzoneDeg = 18f;
            follow.followSpeed = 2f;
            menu.follow = follow;
            menu.poke = poke;

            Img(canvas, "Paper", _rounded, Paper, Stretch);

            BuildStartPage(menu, canvas);
            BuildIntroPage(menu, canvas);
            BuildResultPage(menu, canvas);
            BuildHangarPage(menu, canvas, follow.transform);
            AddCursors(canvas, poke);
            return root;
        }

        static void BuildStartPage(MenuPanel menu, RectTransform canvas)
        {
            var page = Page(canvas, "StartPage");
            menu.startPage = page.gameObject;
            Txt(page, "Title", "MINIPLANES", 60, _heading, Ink, TextAnchor.MiddleCenter, At(0f, 18f, MenuW, 84f));
            CoinSlot(page, 830f, 32f, out menu.startCoins);

            Txt(page, "TutorialHead", "TUTORIAL", 28, _heading, Muted, TextAnchor.MiddleLeft, At(40f, 108f, 300f, 40f));
            var viewport = Img(page, "LevelList", null, new Color(1f, 1f, 1f, 0.01f), At(40f, 152f, 520f, 470f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var area = viewport.gameObject.AddComponent<PokeScrollArea>();
            var content = new GameObject("Content", typeof(RectTransform));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(viewport.rectTransform, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            const float rowH = 92f, gap = 10f;
            contentRect.sizeDelta = new Vector2(0f, Progression.TutorialLevelCount * (rowH + gap) - gap);
            area.content = contentRect;

            var rows = new List<MenuPanel.LevelRow>();
            for (int i = 0; i < Progression.TutorialLevelCount; i++)
            {
                float y = i * (rowH + gap);
                var b = Btn(contentRect, "level" + i, "", 1, Seafoam, Teal, r =>
                {
                    r.anchorMin = new Vector2(0f, 1f);
                    r.anchorMax = new Vector2(1f, 1f);
                    r.pivot = new Vector2(0.5f, 1f);
                    r.anchoredPosition = new Vector2(0f, -y);
                    r.sizeDelta = new Vector2(0f, rowH);
                }, out _);
                var row = new MenuPanel.LevelRow { button = b };
                row.number = Txt(b.Rect, "Number", (i + 1).ToString(), 44, _heading, Ink, TextAnchor.MiddleCenter, r =>
                {
                    r.anchorMin = new Vector2(0f, 0f);
                    r.anchorMax = new Vector2(0f, 1f);
                    r.pivot = new Vector2(0f, 0.5f);
                    r.sizeDelta = new Vector2(84f, 0f);
                });
                row.name = Txt(b.Rect, "Name", "Level " + (i + 1), 32, _font, Ink, TextAnchor.MiddleLeft, r =>
                {
                    Stretch(r);
                    r.offsetMin = new Vector2(88f, 0f);
                    r.offsetMax = new Vector2(-126f, 0f);
                });
                row.status = Txt(b.Rect, "Status", "", 22, _font, Muted, TextAnchor.MiddleRight, r =>
                {
                    r.anchorMin = new Vector2(1f, 0f);
                    r.anchorMax = new Vector2(1f, 1f);
                    r.pivot = new Vector2(1f, 0.5f);
                    r.sizeDelta = new Vector2(120f, 0f);
                    r.anchoredPosition = new Vector2(-14f, 0f);
                });
                rows.Add(row);
            }
            menu.levelRows = rows.ToArray();
            menu.levelList = area;

            var track = Img(page, "ScrollTrack", _rounded, new Color(0.82f, 0.79f, 0.73f, 1f), At(572f, 152f, 12f, 470f));
            var thumb = Img(track.rectTransform, "ScrollThumb", _rounded, Teal, r =>
            {
                r.anchorMin = new Vector2(0f, 1f);
                r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.sizeDelta = new Vector2(0f, 200f);
            });
            area.track = track.rectTransform;
            area.thumb = thumb.rectTransform;

            Txt(page, "InfiniteHead", "INFINITE", 28, _heading, Muted, TextAnchor.MiddleLeft, At(612f, 108f, 300f, 40f));
            menu.infiniteButton = Btn(page, "infinite", "PLAY", 46, Teal, TealDark, At(612f, 152f, 348f, 160f), out menu.infiniteLabel);
            menu.infiniteBest = Txt(page, "Best", "BEST  0", 30, _font, Ink, TextAnchor.MiddleLeft, At(612f, 318f, 348f, 44f));
            menu.infiniteLock = Txt(page, "Lock", "", 22, _font, Muted, TextAnchor.UpperLeft, At(612f, 362f, 348f, 64f));

            Txt(page, "HangarHead", "HANGAR", 28, _heading, Muted, TextAnchor.MiddleLeft, At(612f, 446f, 300f, 40f));
            menu.hangarButton = Btn(page, "hangar", "PLANES", 40, Coral, CoralDark, At(612f, 490f, 348f, 132f), out _);
        }

        static void BuildIntroPage(MenuPanel menu, RectTransform canvas)
        {
            var page = Page(canvas, "IntroPage");
            menu.introPage = page.gameObject;
            menu.introBack = Btn(page, "back", "BACK", 30, Slate, SlateDark, At(36f, 30f, 170f, 70f), out _);
            menu.introNumber = Txt(page, "Number", "LEVEL 1", 32, _heading, Muted, TextAnchor.MiddleCenter, At(0f, 40f, MenuW, 50f));
            menu.introTitle = Txt(page, "Title", "First Flight", 64, _heading, Ink, TextAnchor.MiddleCenter, At(0f, 92f, MenuW, 90f));
            menu.introCaption = Txt(page, "Caption", "", 30, _font, Muted, TextAnchor.UpperCenter, At(70f, 196f, MenuW - 140f, 140f));
            Txt(page, "LivesLabel", "LIVES", 26, _heading, Muted, TextAnchor.MiddleRight, At(300f, 352f, 150f, 56f));
            var hearts = new List<Image>();
            for (int i = 0; i < 5; i++)
                hearts.Add(Img(page, "Life" + i, MiniPlanesUiSprites.Load("Heart"), Color.white, At(470f + i * 62f, 352f, 54f, 54f)));
            menu.introLives = hearts.ToArray();
            menu.introStart = Btn(page, "start", "START", 54, Teal, TealDark, At(300f, 448f, 400f, 150f), out _);
        }

        static void BuildResultPage(MenuPanel menu, RectTransform canvas)
        {
            var page = Page(canvas, "ResultPage");
            menu.resultPage = page.gameObject;
            menu.resultTitle = Txt(page, "Title", "LEVEL PASSED", 66, _heading, Ink, TextAnchor.MiddleCenter, At(0f, 44f, MenuW, 100f));
            menu.resultDetail = Txt(page, "Detail", "", 30, _font, Muted, TextAnchor.MiddleCenter, At(60f, 150f, MenuW - 120f, 110f));
            menu.resultScore = Txt(page, "Score", "SCORE  0", 52, _heading, Ink, TextAnchor.MiddleCenter, At(0f, 266f, MenuW, 80f));
            menu.resultCoins = Txt(page, "Coins", "", 34, _heading, Gold, TextAnchor.MiddleCenter, At(0f, 346f, MenuW, 56f));
            menu.nextButton = Btn(page, "next", "NEXT", 36, Teal, TealDark, At(44f, 448f, 276f, 150f), out menu.nextLabel);
            Btn(page, "retry", "RETRY", 36, Seafoam, Teal, At(362f, 448f, 276f, 150f), out _);
            Btn(page, "menu", "MENU", 36, Slate, SlateDark, At(680f, 448f, 276f, 150f), out _);
        }

        static void BuildHangarPage(MenuPanel menu, RectTransform canvas, Transform follow)
        {
            var page = Page(canvas, "HangarPage");
            menu.hangarPage = page.gameObject;
            Btn(page, "hangar_back", "BACK", 30, Slate, SlateDark, At(30f, 26f, 160f, 68f), out _);
            Txt(page, "Title", "HANGAR", 50, _heading, Ink, TextAnchor.MiddleCenter, At(0f, 20f, MenuW, 76f));
            CoinSlot(page, 830f, 32f, out menu.hangarCoins);

            var rows = new List<MenuPanel.PlaneRow>();
            for (int i = 0; i < 3; i++)
            {
                var b = Btn(page, "plane" + i, "", 1, Seafoam, Teal, At(30f, 120f + i * 134f, 280f, 120f), out _);
                var row = new MenuPanel.PlaneRow { button = b };
                row.name = Txt(b.Rect, "Name", "Plane", 32, _heading, Ink, TextAnchor.MiddleLeft, r =>
                {
                    Stretch(r);
                    r.offsetMin = new Vector2(22f, 40f);
                    r.offsetMax = new Vector2(-10f, -8f);
                });
                row.status = Txt(b.Rect, "Status", "", 24, _font, Muted, TextAnchor.MiddleLeft, r =>
                {
                    Stretch(r);
                    r.offsetMin = new Vector2(22f, 8f);
                    r.offsetMax = new Vector2(-10f, -72f);
                });
                rows.Add(row);
            }
            menu.planeRows = rows.ToArray();

            menu.planeName = Txt(page, "PlaneName", "", 40, _heading, Ink, TextAnchor.MiddleCenter, At(330f, 104f, 340f, 60f));
            Img(page, "PreviewStage", _rounded, new Color(0.9f, 0.86f, 0.78f, 0.6f), At(340f, 170f, 320f, 270f));
            menu.planeInfo = Txt(page, "PlaneInfo", "", 22, _font, Muted, TextAnchor.UpperCenter, At(330f, 448f, 340f, 60f));
            menu.planeAction = Btn(page, "plane_action", "FLY THIS", 36, Teal, TealDark, At(350f, 516f, 300f, 110f), out menu.planeActionLabel);

            var palette = Img(page, "PaintPanel", _rounded, new Color(0.93f, 0.9f, 0.83f, 1f), At(690f, 112f, 280f, 514f));
            menu.palettePanel = palette.gameObject;
            Txt(palette.rectTransform, "Head", "PAINT", 32, _heading, Muted, TextAnchor.MiddleCenter, At(0f, 16f, 280f, 50f));
            var swatches = new List<PokeButton>();
            for (int i = 0; i < 4; i++)
            {
                float x = 30f + (i % 2) * 120f;
                float y = 90f + (i / 2) * 140f;
                var bg = Img(palette.rectTransform, "Swatch" + i, MiniPlanesUiSprites.Load("Circle"), Color.white, At(x, y, 100f, 100f));
                var b = bg.gameObject.AddComponent<PokeButton>();
                b.id = "palette" + i;
                b.background = bg;
                swatches.Add(b);
                Img(bg.rectTransform, "Rim", MiniPlanesUiSprites.Load("Ring"), new Color(Ink.r, Ink.g, Ink.b, 0.5f), Stretch);
            }
            menu.paletteButtons = swatches.ToArray();
            menu.paletteName = Txt(palette.rectTransform, "Name", "", 28, _font, Ink, TextAnchor.MiddleCenter, At(0f, 380f, 280f, 50f));

            // The spinning preview floats just in front of the middle of the panel.
            var anchor = new GameObject("PlanePreview").transform;
            anchor.SetParent(follow, false);
            float stageCentre = MenuH * 0.5f - (170f + 135f);
            anchor.localPosition = new Vector3(0f, -0.07f + stageCentre * CanvasScale, 0.46f - 0.07f);
            anchor.localRotation = Quaternion.Euler(-15f, 200f, 0f);
            menu.previewAnchor = anchor;
        }

        // ---------------------------------------------------------------- HUD

        static GameObject BuildHud()
        {
            var root = new GameObject("CockpitHUD");
            var hud = root.AddComponent<CurvedHud>();
            var canvas = CurvedPanel(root, "Canvas", 0.8f, -0.2f, new Vector2(2000f, 700f), 5, out var follow, out var poke);
            UnityEngine.Object.DestroyImmediate(poke);
            hud.follow = follow;

            var strip = Page(canvas, "Strip");
            hud.strip = strip.gameObject;
            Img(strip, "Plate", _rounded, HudPlate, Centre(0f, 0f, 1880f, 96f));
            Img(strip, "ProgressTrack", _rounded, new Color(HudCream.r, HudCream.g, HudCream.b, 0.22f), Centre(0f, 0f, 1060f, 20f));
            var fill = Img(strip, "ProgressFill", _white, HudAccent, Centre(0f, 0f, 1048f, 12f));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            hud.progressFill = fill;
            hud.score = Txt(strip, "Score", "0", 56, _heading, HudCream, TextAnchor.MiddleCenter, Centre(-800f, 0f, 200f, 90f));
            hud.level = Txt(strip, "Level", "LV 1", 52, _heading, HudCream, TextAnchor.MiddleCenter, Centre(800f, 0f, 220f, 90f));
            var hearts = new List<Image>();
            for (int i = 0; i < 5; i++)
                hearts.Add(Img(strip, "Life" + i, MiniPlanesUiSprites.Load("Heart"), Color.white, Centre(-660f + i * 52f, 0f, 46f, 46f)));
            hud.lives = hearts.ToArray();
            Img(strip, "CoinIcon", MiniPlanesUiSprites.Load("Coin"), Color.white, Centre(590f, 0f, 46f, 46f));
            hud.coins = Txt(strip, "Coins", "0", 44, _heading, new Color(1f, 0.82f, 0.36f, 1f), TextAnchor.MiddleLeft, Centre(680f, 0f, 120f, 80f));

            var caption = Img(canvas, "CaptionPlate", _rounded, HudPlate, Centre(0f, 236f, 1080f, 190f));
            hud.captionPlate = caption.gameObject;
            hud.caption = Txt(caption.rectTransform, "Caption", "", 38, _font, HudCream, TextAnchor.MiddleCenter, r =>
            {
                Stretch(r);
                r.offsetMin = new Vector2(30f, 10f);
                r.offsetMax = new Vector2(-30f, -10f);
            });
            return root;
        }

        // ---------------------------------------------------------------- pause

        static GameObject BuildPause()
        {
            var root = new GameObject("PausePanel");
            var pause = root.AddComponent<PausePanel>();
            var canvas = CurvedPanel(root, "Canvas", 0.46f, -0.05f, new Vector2(560f, 600f), 20, out var follow, out var poke);
            follow.unscaledTime = true;
            pause.follow = follow;
            pause.poke = poke;
            Img(canvas, "Plate", _rounded, new Color(0.07f, 0.17f, 0.17f, 0.85f), Stretch);
            Txt(canvas, "Title", "PAUSED", 44, _heading, HudCream, TextAnchor.MiddleCenter, At(0f, 36f, 560f, 64f));
            Btn(canvas, "resume", "RESUME", 40, Teal, TealDark, At(60f, 128f, 440f, 116f), out _);
            Btn(canvas, "restart", "RESTART", 40, Slate, SlateDark, At(60f, 264f, 440f, 116f), out _);
            Btn(canvas, "mainmenu", "MAIN MENU", 40, Coral, CoralDark, At(60f, 400f, 440f, 116f), out _);
            AddCursors(canvas, poke);
            return root;
        }

        // ---------------------------------------------------------------- small world UI

        static GameObject BuildWrist()
        {
            var root = new GameObject("WristMenuButton");
            var wrist = root.AddComponent<WristMenuButton>();
            var canvas = FlatCanvas(root.transform, "Button", new Vector2(140f, 56f), 30);
            wrist.button = canvas;
            wrist.fade = canvas.gameObject.AddComponent<CanvasGroup>();
            Img(canvas, "Background", _rounded, new Color(TealDark.r, TealDark.g, TealDark.b, 0.95f), Stretch);
            Txt(canvas, "Label", "MENU", 30, _heading, HudCream, TextAnchor.MiddleCenter, Stretch);
            return root;
        }

        static GameObject BuildIcon(string name, string sprite)
        {
            var root = new GameObject(name);
            root.AddComponent<Billboard>();
            var canvas = FlatCanvas(root.transform, "Canvas", new Vector2(56f, 56f), 6);
            Img(canvas, "Icon", MiniPlanesUiSprites.Load(sprite), Color.white, Stretch);
            return root;
        }

        static GameObject BuildCrosshair()
        {
            var root = new GameObject("Crosshair");
            root.AddComponent<Billboard>();
            var canvas = FlatCanvas(root.transform, "Canvas", new Vector2(110f, 110f), 6);
            canvas.gameObject.AddComponent<CanvasGroup>();
            Img(canvas, "Ring", MiniPlanesUiSprites.Load("Crosshair"), Color.white, Stretch);
            return root;
        }

        static GameObject BuildFuelGauge()
        {
            var root = new GameObject("FuelGauge");
            var view = root.AddComponent<FuelGaugeView>();
            var canvas = FlatCanvas(root.transform, "Canvas", new Vector2(72f, 12f), 6);
            Img(canvas, "Track", _rounded, new Color(0.12f, 0.2f, 0.2f, 0.6f), Stretch);
            var fill = Img(canvas, "Fill", _white, new Color(0.36f, 0.85f, 0.72f, 1f), r =>
            {
                Stretch(r);
                r.offsetMin = new Vector2(3f, 3f);
                r.offsetMax = new Vector2(-3f, -3f);
            });
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            view.fill = fill;
            return root;
        }
    }
}

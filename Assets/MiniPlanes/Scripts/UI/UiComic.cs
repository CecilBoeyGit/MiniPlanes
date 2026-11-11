using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// Turns a UI panel's rounded plates and buttons into solid, comic-book ones: full opacity, an ink
    /// outline, a highlight along the top and a shade along the bottom (the UIComic shader), and a hard ink
    /// drop shadow behind each. Light button labels get a small text shadow too. It only touches Images
    /// still using the stock "Rounded" sprite and the default material, so anything you restyle with your
    /// own sprite or material is left as you made it. Each keeps its colour as the paint.
    /// </summary>
    public static class UiComic
    {
        public const string RoundedSprite = "Rounded";
        public static readonly Color ShadowColor = new Color(0.16f, 0.13f, 0.2f, 0.7f);

        /// <summary>Applies the comic look under a root. Returns how many Images changed.</summary>
        public static int Apply(Component root, Material comic, Sprite comicSprite, Sprite shadowSprite)
        {
            if (root == null || comic == null || comicSprite == null) return 0;
            int changed = 0;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null || img.sprite.name != RoundedSprite) continue;
                if (img.GetComponent<ComicShadow>() != null) continue;
                if (img.material != null && img.material != img.defaultMaterial) continue;
                img.sprite = comicSprite;
                img.material = comic;
                img.color = Opaque(img.color);
                var button = img.GetComponent<PokeButton>();
                if (button != null && button.background == img)
                {
                    button.normalColor = Opaque(button.normalColor);
                    button.pressedColor = Opaque(button.pressedColor);
                    button.disabledColor = Opaque(button.disabledColor);
                }
                if (shadowSprite != null) AddShadow(img, shadowSprite);
                changed++;
            }
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.GetComponent<Shadow>() != null) continue;
                Color c = text.color;
                if (c.r + c.g + c.b < 2.5f) continue;   // only light labels on coloured buttons
                var s = text.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, 0.75f);
                s.effectDistance = new Vector2(2f, -3f);
            }
            if (changed > 0)
                foreach (var curved in root.GetComponentsInChildren<CurvedCanvas>(true))
                {
                    curved.EnsureEffects();
                    curved.MarkDirty();
                }
            return changed;
        }

        static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        static void AddShadow(Image img, Sprite shadowSprite)
        {
            var target = img.rectTransform;
            if (target.parent == null) return;
            var go = new GameObject(img.name + "Shadow", typeof(RectTransform), typeof(Image), typeof(ComicShadow));
            var rt = (RectTransform)go.transform;
            rt.SetParent(target.parent, false);
            rt.SetSiblingIndex(target.GetSiblingIndex());
            var shadow = go.GetComponent<Image>();
            shadow.sprite = shadowSprite;
            shadow.type = img.type;
            shadow.pixelsPerUnitMultiplier = img.pixelsPerUnitMultiplier;
            shadow.color = ShadowColor;
            shadow.raycastTarget = false;
            var follow = go.GetComponent<ComicShadow>();
            Vector2 size = target.rect.size;
            float d = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.07f, 5f, 16f);
            follow.offset = new Vector2(d * 0.7f, -d);
            follow.target = target;
            follow.Follow();
        }

        /// <summary>Whether an Image is drawn in the comic style. For tests.</summary>
        public static bool IsComic(Image img)
        {
            return img != null && img.material != null && img.material.shader != null && img.material.shader.name == "MiniPlanes/UIComic";
        }
    }
}

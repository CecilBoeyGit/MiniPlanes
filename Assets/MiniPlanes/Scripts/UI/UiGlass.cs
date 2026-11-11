using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// Turns a UI panel's rounded plates and buttons into tinted, refracting glass (the UIGlass shader). It
    /// only touches Images still using the stock "Rounded" sprite and the default material, so anything you
    /// restyle with your own sprite or material is left as you made it. Each keeps its colour as the tint.
    /// </summary>
    public static class UiGlass
    {
        public const string RoundedSprite = "Rounded";

        /// <summary>Applies the glass under a root. Returns how many Images changed.</summary>
        public static int Apply(Component root, Material glass, Sprite glassSprite)
        {
            if (root == null || glass == null || glassSprite == null) return 0;
            int changed = 0;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null || img.sprite.name != RoundedSprite) continue;
                if (img.material != null && img.material != img.defaultMaterial) continue;
                img.sprite = glassSprite;
                img.material = glass;
                changed++;
            }
            if (changed > 0)
                foreach (var curved in root.GetComponentsInChildren<CurvedCanvas>(true)) curved.MarkDirty();
            return changed;
        }

        /// <summary>Whether an Image is drawn as glass. For tests.</summary>
        public static bool IsGlass(Image img)
        {
            return img != null && img.material != null && img.material.shader != null && img.material.shader.name == "MiniPlanes/UIGlass";
        }
    }
}

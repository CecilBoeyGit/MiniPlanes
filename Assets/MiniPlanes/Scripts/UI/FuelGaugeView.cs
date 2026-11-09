using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>The fuel bar that floats above the plane. Its look is the FuelGauge prefab.</summary>
    public class FuelGaugeView : MonoBehaviour
    {
        [Tooltip("A Filled, horizontal Image.")]
        public Image fill;
        public Color normalColor = new Color(0.36f, 0.85f, 0.72f, 1f);
        public Color lowColor = new Color(1f, 0.62f, 0.25f, 1f);
        public Color flashColor = new Color(1f, 0.32f, 0.22f, 1f);
        [Tooltip("Flashes per second when the tank is low.")]
        public float flashRate = 4f;

        public void Show(float fraction, bool low)
        {
            if (fill == null) return;
            fill.fillAmount = Mathf.Clamp01(fraction);
            bool flash = low && Mathf.Repeat(Time.time * flashRate, 1f) < 0.5f;
            fill.color = low ? (flash ? flashColor : lowColor) : normalColor;
        }
    }
}

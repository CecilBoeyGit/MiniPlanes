using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Animates the "cup it with both hands" icon over an armed bomb balloon: two hands close in on the bomb
    /// and open again, and the bomb blinks. Lives on the CupIcon prefab; restyle the images freely.
    /// </summary>
    public class CupIconMotion : MonoBehaviour
    {
        public RectTransform leftHand;
        public RectTransform rightHand;
        [Tooltip("Blinks with the fuse.")]
        public UnityEngine.UI.Graphic bomb;
        [Tooltip("How far each hand sits from the middle when open and when closed, in canvas pixels.")]
        public float openOffset = 22f;
        public float closedOffset = 14f;
        [Tooltip("Cupping motions per second.")]
        public float rate = 1.6f;
        public Color bombColor = new Color(0.22f, 0.2f, 0.22f, 1f);
        public Color bombFlash = new Color(1f, 0.25f, 0.2f, 1f);

        void Update()
        {
            float t = Time.time * rate;
            float k = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
            float x = Mathf.Lerp(openOffset, closedOffset, k);
            if (leftHand != null) leftHand.anchoredPosition = new Vector2(-x, leftHand.anchoredPosition.y);
            if (rightHand != null) rightHand.anchoredPosition = new Vector2(x, rightHand.anchoredPosition.y);
            if (bomb != null) bomb.color = Mathf.Repeat(Time.time * 4f, 1f) < 0.5f ? bombFlash : bombColor;
        }
    }
}

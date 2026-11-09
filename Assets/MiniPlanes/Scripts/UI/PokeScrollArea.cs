using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// A vertically scrolling list for poke input: press anywhere in it and drag up or down. Put it on the
    /// viewport (with a RectMask2D) and point <see cref="content"/> at the moving list inside it.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PokeScrollArea : MonoBehaviour
    {
        [Tooltip("The list that moves. Anchor it to the top of the viewport.")]
        public RectTransform content;
        [Tooltip("Optional scroll bar track and thumb.")]
        public RectTransform track;
        public RectTransform thumb;

        public float Scroll { get; private set; }
        public RectTransform Viewport => (RectTransform)transform;
        public float ScrollMax => content == null ? 0f : Mathf.Max(0f, content.rect.height - Viewport.rect.height);

        public void SetScroll(float value)
        {
            if (content == null) return;
            Scroll = Mathf.Clamp(value, 0f, ScrollMax);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Scroll);
            UpdateThumb();
            // The rows moved, so their bend has to be worked out again.
            CurvedCanvas.MarkDirty(content);
            if (thumb != null) CurvedCanvas.MarkDirty(thumb);
        }

        void UpdateThumb()
        {
            if (track == null || thumb == null) return;
            float view = Viewport.rect.height;
            float size = Mathf.Max(view, content.rect.height);
            float trackHeight = track.rect.height;
            float thumbHeight = Mathf.Max(40f, trackHeight * view / size);
            float t = ScrollMax > 0f ? Scroll / ScrollMax : 0f;
            thumb.sizeDelta = new Vector2(thumb.sizeDelta.x, thumbHeight);
            thumb.anchoredPosition = new Vector2(thumb.anchoredPosition.x, -(trackHeight - thumbHeight) * t);
        }

        void OnEnable()
        {
            UpdateThumb();
        }
    }
}

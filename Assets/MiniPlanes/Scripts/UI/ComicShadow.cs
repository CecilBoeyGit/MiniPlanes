using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// A comic drop shadow: sits behind its panel or button, offset down and right, and keeps up with it as it
    /// moves, scales, hides or fades.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class ComicShadow : MonoBehaviour
    {
        public RectTransform target;
        public Vector2 offset = new Vector2(7f, -10f);

        Image _image;
        CanvasGroup _group;
        float _alpha = -1f;

        void LateUpdate() { Follow(); }

        public void Follow()
        {
            if (target == null)
            {
                Destroy(gameObject);
                return;
            }
            if (_image == null)
            {
                _image = GetComponent<Image>();
                _alpha = _image.color.a;
                _group = target.GetComponent<CanvasGroup>();
            }
            var rt = (RectTransform)transform;
            rt.anchorMin = target.anchorMin;
            rt.anchorMax = target.anchorMax;
            rt.pivot = target.pivot;
            rt.sizeDelta = target.sizeDelta;
            rt.localRotation = target.localRotation;
            rt.localScale = target.localScale;
            rt.anchoredPosition = target.anchoredPosition + offset;
            bool show = target.gameObject.activeSelf;
            if (_image.enabled != show) _image.enabled = show;
            if (_group != null)
            {
                Color c = _image.color;
                float a = _alpha * _group.alpha;
                if (!Mathf.Approximately(c.a, a))
                {
                    c.a = a;
                    _image.color = c;
                }
            }
        }
    }
}

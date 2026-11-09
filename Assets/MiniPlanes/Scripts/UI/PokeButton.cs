using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// A button pressed by poking it with an index fingertip. Put it on any UI element with a
    /// RectTransform; the <see cref="PokeCanvas"/> above it does the touching. The id tells the panel's
    /// script which button it is, so keep ids when you restyle.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PokeButton : MonoBehaviour
    {
        [Tooltip("What the panel's script calls this button. Keep it when you restyle.")]
        public string id;
        [Tooltip("The graphic that changes colour when pressed or disabled. Usually the button's background.")]
        public Graphic background;
        public Color normalColor = Color.white;
        public Color pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        public Color disabledColor = new Color(0.88f, 0.85f, 0.79f, 1f);
        [SerializeField] bool interactable = true;
        public UnityEvent onClick = new UnityEvent();

        public event Action<PokeButton> Clicked;

        bool _pressed;

        public RectTransform Rect => (RectTransform)transform;

        public bool Interactable
        {
            get => interactable;
            set
            {
                interactable = value;
                Refresh();
            }
        }

        public bool Pressed
        {
            get => _pressed;
            set
            {
                _pressed = value;
                Refresh();
            }
        }

        /// <summary>Changes the resting colour, for example to mark the selected row.</summary>
        public void SetNormalColor(Color color)
        {
            normalColor = color;
            Refresh();
        }

        public void Click()
        {
            onClick.Invoke();
            Clicked?.Invoke(this);
        }

        void OnEnable()
        {
            _pressed = false;
            Refresh();
        }

        void Refresh()
        {
            if (background == null) return;
            background.color = !interactable ? disabledColor : _pressed ? pressedColor : normalColor;
        }
    }
}

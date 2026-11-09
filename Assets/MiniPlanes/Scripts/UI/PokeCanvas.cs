using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// Fingertip input for a world-space canvas, flat or curved: shows a cursor dot under each index tip,
    /// presses <see cref="PokeButton"/>s and drags <see cref="PokeScrollArea"/>s. A button clicks when the
    /// finger comes back off it. A finger already in the panel when it opens must leave before it can press.
    /// </summary>
    [RequireComponent(typeof(CurvedCanvas))]
    public class PokeCanvas : MonoBehaviour
    {
        class State
        {
            public bool pressing;
            public bool armed;
            public bool dragging;
            public PokeButton target;
            public PokeScrollArea area;
            public float startY;
            public float startScroll;
        }

        public HandSource[] hands;

        [Header("Poking (metres)")]
        [Tooltip("The fingertip counts as pressing within this distance in front of the panel.")]
        public float pressDepth = 0.012f;
        [Tooltip("Pull back this far in front of the panel to release a press.")]
        public float releaseDepth = 0.028f;
        [Tooltip("A fingertip pushed further than this through the panel stops counting.")]
        public float maxBehindPanel = 0.08f;
        [Tooltip("The cursor dot appears this close in front of the panel.")]
        public float hoverDepth = 0.07f;
        [Tooltip("Finger movement that turns a press on a list into a scroll, in canvas units.")]
        public float dragThreshold = 18f;

        [Header("Cursor dots (optional)")]
        public Graphic[] cursors;
        public float cursorFarSize = 40f;
        public float cursorNearSize = 18f;

        public event Action<PokeButton> Clicked;

        /// <summary>A finger is pressing the panel right now.</summary>
        public bool IsPressing
        {
            get
            {
                foreach (var s in _states) if (s.pressing) return true;
                return false;
            }
        }

        CurvedCanvas _curved;
        readonly State[] _states = { new State(), new State() };
        readonly List<PokeButton> _buttons = new List<PokeButton>();
        int _blockFrames;

        CurvedCanvas Curved => _curved != null ? _curved : _curved = GetComponent<CurvedCanvas>();

        /// <summary>Ignores input for a few frames and makes any finger already there leave first.</summary>
        public void BlockInput(int frames)
        {
            _blockFrames = Mathf.Max(_blockFrames, frames);
            foreach (var s in _states)
            {
                if (s.target != null) s.target.Pressed = false;
                s.pressing = false;
                s.armed = false;
                s.dragging = false;
                s.target = null;
                s.area = null;
            }
        }

        /// <summary>Where a button's centre is in the world, for automated tests.</summary>
        public Vector3 WorldCentre(PokeButton button)
        {
            return Curved.CentreOf(button.Rect);
        }

        void OnDisable()
        {
            BlockInput(0);
            if (cursors != null) foreach (var c in cursors) if (c != null) c.enabled = false;
        }

        void LateUpdate()
        {
            if (_blockFrames > 0) _blockFrames--;
            for (int i = 0; i < _states.Length; i++)
                UpdateHand(i, hands != null && i < hands.Length ? hands[i] : null);
        }

        void UpdateHand(int i, HandSource hand)
        {
            var state = _states[i];
            var cursor = cursors != null && i < cursors.Length ? cursors[i] : null;
            if (hand == null || !hand.IsTracked || !hand.TryGetJoint(HandJoint.IndexTip, out var tip))
            {
                if (cursor != null) cursor.enabled = false;
                Release(state, false);
                state.armed = true;
                return;
            }

            Vector3 flat = Curved.ToFlat(tip);
            float depth = flat.z;
            Rect panel = Curved.Rect.rect;
            bool over = panel.Contains(new Vector2(flat.x, flat.y));

            bool near = over && depth > -hoverDepth && depth < maxBehindPanel;
            if (cursor != null)
            {
                cursor.enabled = near;
                if (near)
                {
                    var r = cursor.rectTransform;
                    r.localPosition = new Vector3(flat.x, flat.y, 0f);
                    float closeness = Mathf.InverseLerp(-hoverDepth, -pressDepth, depth);
                    r.sizeDelta = Vector2.one * Mathf.Lerp(cursorFarSize, cursorNearSize, closeness);
                    var c = cursor.color;
                    c.a = Mathf.Lerp(0.25f, 0.9f, closeness);
                    cursor.color = c;
                    cursor.SetVerticesDirty();
                }
            }

            bool contact = over && depth > -pressDepth && depth < maxBehindPanel;
            if (!state.pressing)
            {
                if (!contact) state.armed = true;
                if (contact && state.armed && _blockFrames <= 0)
                {
                    state.pressing = true;
                    state.dragging = false;
                    state.target = HitButton(flat);
                    state.area = HitArea(flat);
                    state.startY = flat.y;
                    state.startScroll = state.area != null ? state.area.Scroll : 0f;
                    if (state.target != null) state.target.Pressed = true;
                }
                return;
            }

            if (state.area != null)
            {
                float dy = flat.y - state.startY;
                if (!state.dragging && Mathf.Abs(dy) > dragThreshold)
                {
                    state.dragging = true;
                    if (state.target != null) state.target.Pressed = false;
                }
                if (state.dragging) state.area.SetScroll(state.startScroll + dy);
            }

            bool released = depth < -releaseDepth || !over || depth > maxBehindPanel;
            if (released) Release(state, over);
        }

        void Release(State state, bool click)
        {
            if (!state.pressing) return;
            var target = state.target;
            bool wasDrag = state.dragging;
            state.pressing = false;
            state.dragging = false;
            state.target = null;
            state.area = null;
            state.armed = false;
            if (target == null) return;
            target.Pressed = false;
            if (click && !wasDrag && target.Interactable && target.isActiveAndEnabled)
            {
                target.Click();
                Clicked?.Invoke(target);
            }
        }

        Vector3 FlatToLocalOf(RectTransform rect, Vector3 flat)
        {
            return rect.InverseTransformPoint(transform.TransformPoint(new Vector3(flat.x, flat.y, 0f)));
        }

        bool Contains(RectTransform rect, Vector3 flat)
        {
            Vector3 p = FlatToLocalOf(rect, flat);
            return rect.rect.Contains(new Vector2(p.x, p.y));
        }

        PokeButton HitButton(Vector3 flat)
        {
            GetComponentsInChildren(false, _buttons);
            // Later in the hierarchy draws on top, so search from the end.
            for (int i = _buttons.Count - 1; i >= 0; i--)
            {
                var b = _buttons[i];
                if (!b.Interactable || !Contains(b.Rect, flat)) continue;
                var area = b.GetComponentInParent<PokeScrollArea>();
                if (area != null && !Contains(area.Viewport, flat)) continue;
                return b;
            }
            return null;
        }

        PokeScrollArea HitArea(Vector3 flat)
        {
            foreach (var a in GetComponentsInChildren<PokeScrollArea>(false))
                if (Contains(a.Viewport, flat)) return a;
            return null;
        }
    }
}

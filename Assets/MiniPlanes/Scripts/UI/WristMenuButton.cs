using System;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// A small wrist-watch Menu button. It only appears during a flight, on the wrist of a hand whose palm
    /// faces the player's face, and it is pressed with the other hand's index finger. A hand making a C
    /// sign never shows it. Its look is the "Button" canvas inside the WristMenuButton prefab.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public class WristMenuButton : MonoBehaviour
    {
        [Header("Scene links (set by the scene builder)")]
        public GameManager game;
        public Transform head;
        public HandSource[] hands;
        public HandRing[] rings;

        [Header("Prefab parts")]
        [Tooltip("The button's canvas. It is moved onto the wrist and faded in and out.")]
        public RectTransform button;
        public CanvasGroup fade;

        [Header("When it shows")]
        [Range(0f, 1f), Tooltip("How directly the palm must face the head to bring the button up. 0.75 is about 40 degrees.")]
        public float facingEnterDot = 0.75f;
        [Range(0f, 1f), Tooltip("How far the palm can turn before the button goes away again.")]
        public float facingExitDot = 0.6f;
        [Tooltip("Seconds the palm must face the head before the button appears, so it can't flicker on in passing.")]
        public float showDelaySeconds = 0.12f;
        [Tooltip("A hand further than this from the head is ignored, in metres.")]
        public float maxHeadDistance = 0.75f;

        [Header("Placement (metres)")]
        [Tooltip("How far the button floats out from the wrist, toward the face.")]
        public float liftFromWrist = 0.035f;
        [Tooltip("How far down the forearm from the wrist joint it sits.")]
        public float towardForearm = 0.015f;

        [Header("Pressing (metres)")]
        [Tooltip("Extra room around the button that still counts as touching it.")]
        public float touchMargin = 0.008f;
        [Tooltip("How far in front of or behind the button a fingertip still counts as touching it.")]
        public float touchDepth = 0.02f;

        public event Action Pressed;

        public bool IsShowing => _alpha > 0.5f;
        public int ActiveHand => _active;
        public Vector3 ButtonPosition => button != null ? button.position : Vector3.zero;

        int _active = -1;
        float _held;
        float _alpha;
        bool _armed;

        void Awake()
        {
            Apply();
        }

        /// <summary>The direction out of the palm, or false if the hand's joints can't give one.</summary>
        public static bool TryGetPalmNormal(HandSource hand, out Vector3 normal, out Vector3 wrist, out Vector3 fingers)
        {
            normal = Vector3.zero;
            fingers = Vector3.zero;
            wrist = Vector3.zero;
            if (hand == null || !hand.IsTracked ||
                !hand.TryGetJoint(HandJoint.Wrist, out wrist) ||
                !hand.TryGetJoint(HandJoint.MiddleProximal, out var middle) ||
                !hand.TryGetJoint(HandJoint.IndexProximal, out var index) ||
                !hand.TryGetJoint(HandJoint.LittleProximal, out var little))
                return false;

            fingers = middle - wrist;
            Vector3 across = index - little;
            Vector3 n = Vector3.Cross(fingers, across);
            if (n.sqrMagnitude < 1e-8f || fingers.sqrMagnitude < 1e-6f) return false;
            // Knuckles run opposite ways across the two hands, so the same cross product flips.
            normal = (hand.side == HandSide.Right ? n : -n).normalized;
            fingers.Normalize();
            return true;
        }

        void LateUpdate()
        {
            if (button == null) return;

            bool allowed = game != null && !GameManager.IsPaused &&
                           (game.CurrentFlow == GameManager.Flow.Flying || game.CurrentFlow == GameManager.Flow.PreFlight);
            int pick = allowed ? PickHand() : -1;
            if (pick != _active)
            {
                _active = pick;
                _held = 0f;
                _armed = false;
            }
            if (_active >= 0) _held += Time.deltaTime;

            bool wanted = _active >= 0 && _held >= showDelaySeconds;
            _alpha = allowed ? Mathf.MoveTowards(_alpha, wanted ? 1f : 0f, Time.deltaTime * 9f) : 0f;

            if (_active >= 0 && TryGetPalmNormal(hands[_active], out var normal, out var wrist, out var fingers))
            {
                button.position = wrist + normal * liftFromWrist - fingers * towardForearm;
                Vector3 look = button.position - head.position;
                if (look.sqrMagnitude > 1e-6f) button.rotation = Quaternion.LookRotation(look, head.up);
            }
            Apply();

            if (wanted && IsShowing) CheckTouch();
        }

        int PickHand()
        {
            if (hands == null || head == null) return -1;
            int best = -1;
            float bestDot = 0f;
            for (int i = 0; i < hands.Length; i++)
            {
                if (!TryGetPalmNormal(hands[i], out var normal, out var wrist, out _)) continue;
                if (rings != null && i < rings.Length && rings[i] != null && rings[i].GestureHeld) continue;
                Vector3 toHead = head.position - wrist;
                float distance = toHead.magnitude;
                if (distance > maxHeadDistance || distance < 1e-4f) continue;
                float dot = Vector3.Dot(normal, toHead / distance);
                if (dot < (i == _active ? facingExitDot : facingEnterDot)) continue;
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }
            return best;
        }

        void CheckTouch()
        {
            var other = hands[_active == 0 ? 1 : 0];
            if (other == null || !other.IsTracked || !other.TryGetJoint(HandJoint.IndexTip, out var tip))
            {
                _armed = true;
                return;
            }
            // In the button's own units, which follow the canvas scale.
            Vector3 p = button.InverseTransformPoint(tip);
            Rect r = button.rect;
            float margin = touchMargin / Mathf.Max(1e-6f, button.lossyScale.x);
            float depth = Mathf.Abs(p.z) * button.lossyScale.x;
            bool touching = p.x > r.xMin - margin && p.x < r.xMax + margin &&
                            p.y > r.yMin - margin && p.y < r.yMax + margin && depth < touchDepth;
            // A finger already resting there when the button appears doesn't count until it has moved away.
            if (!touching)
            {
                _armed = true;
                return;
            }
            if (!_armed) return;
            _armed = false;
            Pressed?.Invoke();
        }

        void Apply()
        {
            if (button == null) return;
            bool show = _alpha > 0.001f;
            if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
            if (fade != null) fade.alpha = _alpha;
        }
    }
}

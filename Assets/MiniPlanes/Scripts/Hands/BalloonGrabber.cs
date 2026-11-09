using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Lets one hand pinch a balloon's crosshair and drag it around the room. The balloon then drifts
    /// slowly to wherever the crosshair was left. While holding, the hand's scoring ring is suppressed.
    /// </summary>
    [DefaultExecutionOrder(45)]
    public class BalloonGrabber : MonoBehaviour
    {
        public HandSource hand;
        public HandRing ring;
        [Tooltip("How close a pinch must start to a crosshair to pick it up, in metres.")]
        public float grabRadius = 0.07f;

        public PowerBalloon Held { get; private set; }

        bool _wasPinching;

        void LateUpdate()
        {
            // Treat a pinch during the pause as old, so it can't pick anything up on resume.
            if (GameManager.IsPaused)
            {
                _wasPinching = true;
                return;
            }

            bool tracked = hand != null && hand.IsTracked;
            bool pinching = tracked && hand.IsIndexPinching;
            Vector3 pinchPoint = Vector3.zero;
            if (tracked)
            {
                bool gotThumb = hand.TryGetJoint(HandJoint.ThumbTip, out var thumb);
                bool gotIndex = hand.TryGetJoint(HandJoint.IndexTip, out var index);
                if (gotThumb && gotIndex) pinchPoint = (thumb + index) * 0.5f;
                else pinching = false;
            }

            if (pinching && !_wasPinching && Held == null) TryGrab(pinchPoint);

            if (Held != null)
            {
                if (!pinching || !Held.IsAlive)
                {
                    Held.Release(this);
                    Held = null;
                }
                else
                {
                    Held.DragTo(pinchPoint);
                }
            }

            if (ring != null) ring.Suppressed = Held != null;
            _wasPinching = pinching;
        }

        void TryGrab(Vector3 point)
        {
            PowerBalloon best = null;
            float bestDist = grabRadius;
            foreach (var b in PowerBalloon.Active)
            {
                if (b == null || !b.IsAlive || b.IsHeld) continue;
                float d = Vector3.Distance(point, b.CrosshairPosition);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = b;
                }
            }
            if (best == null) return;
            best.Grab(this);
            Held = best;
        }

        void OnDisable()
        {
            if (Held != null)
            {
                Held.Release(this);
                Held = null;
            }
            if (ring != null) ring.Suppressed = false;
        }
    }
}

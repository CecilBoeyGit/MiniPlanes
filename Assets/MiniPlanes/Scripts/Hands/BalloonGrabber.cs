using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Lets one hand pinch a powerup and move it. A balloon's crosshair follows the pinch and the balloon
    /// drifts slowly after it. A booster tube is held like a real object: it moves and turns with the hand,
    /// keeping the grip it was picked up with. While holding, the hand's scoring ring is suppressed.
    /// </summary>
    [DefaultExecutionOrder(45)]
    public class BalloonGrabber : MonoBehaviour
    {
        public HandSource hand;
        public HandRing ring;
        [Tooltip("How close a pinch must start to a crosshair to pick it up, in metres.")]
        public float grabRadius = 0.07f;

        public PowerBalloon Held { get; private set; }
        public BoosterTube HeldTube { get; private set; }
        public bool IsHolding => Held != null || HeldTube != null;

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

            if (pinching && !_wasPinching && !IsHolding) TryGrab(pinchPoint);

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

            if (HeldTube != null)
            {
                if (!pinching || !HeldTube.IsAlive)
                {
                    HeldTube.Release(this);
                    HeldTube = null;
                }
                else
                {
                    HeldTube.Drag(pinchPoint, HandRotation(hand, pinchPoint));
                }
            }

            if (ring != null) ring.Suppressed = IsHolding;
            _wasPinching = pinching;
        }

        /// <summary>
        /// Which way the hand faces: forward from the wrist to the pinch, up out of the back of the hand.
        /// </summary>
        public static Quaternion HandRotation(HandSource hand, Vector3 pinchPoint)
        {
            Vector3 forward = Vector3.forward;
            if (hand != null && hand.TryGetJoint(HandJoint.Wrist, out var wrist))
            {
                Vector3 f = pinchPoint - wrist;
                if (f.sqrMagnitude > 1e-6f) forward = f.normalized;
            }
            Vector3 up = Vector3.up;
            if (WristMenuButton.TryGetPalmNormal(hand, out var palmNormal, out _, out _)) up = -palmNormal;
            if (Mathf.Abs(Vector3.Dot(up.normalized, forward)) > 0.97f) up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up.normalized, forward)) > 0.97f) up = Vector3.right;
            return Quaternion.LookRotation(forward, up);
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

            BoosterTube bestTube = null;
            float bestTubeDist = float.PositiveInfinity;
            foreach (var t in BoosterTube.Active)
            {
                if (t == null || !t.CanGrabAt(point)) continue;
                float d = Mathf.Max(0f, t.SurfaceDistance(point));
                if (d < bestTubeDist)
                {
                    bestTubeDist = d;
                    bestTube = t;
                }
            }

            if (bestTube != null && (best == null || bestTubeDist <= bestDist))
            {
                bestTube.Grab(this, point, HandRotation(hand, point));
                HeldTube = bestTube;
                Sfx.Play(SoundKind.BalloonPop, point);
                return;
            }
            if (best == null) return;
            best.Grab(this);
            Held = best;
            Sfx.Play(SoundKind.BalloonPop, point);
        }

        void OnDisable()
        {
            if (Held != null)
            {
                Held.Release(this);
                Held = null;
            }
            if (HeldTube != null)
            {
                HeldTube.Release(this);
                HeldTube = null;
            }
            if (ring != null) ring.Suppressed = false;
        }
    }
}

using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// A scriptable hand for automated tests and desktop play. Poses are set directly: a C sign around a
    /// point, a pointing finger, a pinch, or a palm position.
    /// </summary>
    public class FakeHandSource : HandSource
    {
        readonly Vector3[] _joints = new Vector3[(int)HandJoint.Count];
        bool _tracked;
        bool _pinching;

        public override bool IsTracked => _tracked;
        public override bool IsHighConfidence => _tracked;
        public override float HandScale => 1f;
        public override bool IsIndexPinching => _tracked && _pinching;

        public override bool TryGetJoint(HandJoint joint, out Vector3 position)
        {
            position = _joints[(int)joint];
            return _tracked;
        }

        public void SetUntracked()
        {
            _tracked = false;
            _pinching = false;
        }

        /// <summary>Puts every joint at one far-away point, so this hand cannot touch anything.</summary>
        public void Park(Vector3 where)
        {
            _tracked = true;
            _pinching = false;
            for (int i = 0; i < _joints.Length; i++) _joints[i] = where;
        }

        /// <summary>
        /// A C sign in the plane perpendicular to <paramref name="normal"/>, with the hole around
        /// <paramref name="center"/> and the opening toward <paramref name="open"/>. A stretch above 1
        /// makes the hole taller across the opening than it is deep.
        /// </summary>
        public void PoseC(Vector3 center, Vector3 normal, Vector3 open, float radius = 0.034f, float stretch = 1f)
        {
            _tracked = true;
            _pinching = false;
            normal.Normalize();
            open = (open - normal * Vector3.Dot(open, normal)).normalized;
            Vector3 up = Vector3.Cross(normal, open);
            Vector3 across = up * stretch;

            Set(HandJoint.IndexProximal, center, open, across, radius, 200f);
            Set(HandJoint.IndexIntermediate, center, open, across, radius, 140f);
            Set(HandJoint.IndexDistal, center, open, across, radius, 80f);
            Set(HandJoint.IndexTip, center, open, across, radius, 35f);
            Set(HandJoint.ThumbTip, center, open, across, radius, -35f);
            Set(HandJoint.ThumbDistal, center, open, across, radius, -85f);
            Set(HandJoint.ThumbProximal, center, open, across, radius, -150f);

            // Palm and the unused fingers sit well behind the C, on the far side from the opening.
            Vector3 palm = center - open * (radius * 2.6f);
            _joints[(int)HandJoint.Palm] = palm;
            _joints[(int)HandJoint.Wrist] = palm - open * 0.05f;
            _joints[(int)HandJoint.ThumbMetacarpal] = palm + up * -0.015f;
            _joints[(int)HandJoint.IndexMetacarpal] = palm + up * 0.015f;
            FoldFinger(HandJoint.MiddleMetacarpal, palm, up, open, 0.005f);
            FoldFinger(HandJoint.RingMetacarpal, palm, up, open, -0.005f);
            FoldFinger(HandJoint.LittleMetacarpal, palm, up, open, -0.015f);
        }

        /// <summary>A hand pointing its index finger along <paramref name="direction"/> with the tip at a point.</summary>
        public void PosePoint(Vector3 tip, Vector3 direction)
        {
            _tracked = true;
            _pinching = false;
            direction.Normalize();
            Vector3 side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();

            _joints[(int)HandJoint.IndexTip] = tip;
            _joints[(int)HandJoint.IndexDistal] = tip - direction * 0.02f;
            _joints[(int)HandJoint.IndexIntermediate] = tip - direction * 0.045f;
            _joints[(int)HandJoint.IndexProximal] = tip - direction * 0.085f;
            Vector3 palm = tip - direction * 0.12f;
            _joints[(int)HandJoint.IndexMetacarpal] = palm;
            _joints[(int)HandJoint.Palm] = palm;
            _joints[(int)HandJoint.Wrist] = palm - direction * 0.05f;

            _joints[(int)HandJoint.ThumbMetacarpal] = palm + side * 0.02f;
            _joints[(int)HandJoint.ThumbProximal] = palm + side * 0.03f;
            _joints[(int)HandJoint.ThumbDistal] = palm + side * 0.035f;
            _joints[(int)HandJoint.ThumbTip] = palm + side * 0.04f;
            FoldFinger(HandJoint.MiddleMetacarpal, palm, -side, -direction, 0.015f);
            FoldFinger(HandJoint.RingMetacarpal, palm, -side, -direction, 0.03f);
            FoldFinger(HandJoint.LittleMetacarpal, palm, -side, -direction, 0.045f);
        }

        /// <summary>A pinch with thumb and index meeting at a point.</summary>
        public void PosePinch(Vector3 point, Vector3 toward)
        {
            PosePoint(point, toward);
            _joints[(int)HandJoint.ThumbTip] = point;
            _pinching = true;
        }

        /// <summary>An open flat hand with its palm facing along <paramref name="palmFacing"/>.</summary>
        public void PoseOpenPalm(Vector3 wrist, Vector3 fingerDir, Vector3 palmFacing)
        {
            _tracked = true;
            _pinching = false;
            fingerDir.Normalize();
            palmFacing = (palmFacing - fingerDir * Vector3.Dot(palmFacing, fingerDir)).normalized;
            // Which way the thumb side lies depends on the hand: this is what makes a left hand a left hand.
            Vector3 thumbSide = side == HandSide.Right ? Vector3.Cross(palmFacing, fingerDir) : Vector3.Cross(fingerDir, palmFacing);

            _joints[(int)HandJoint.Wrist] = wrist;
            _joints[(int)HandJoint.Palm] = wrist + fingerDir * 0.05f;
            float[] across = { 0.025f, 0.005f, -0.012f, -0.028f };
            HandJoint[] metacarpals = { HandJoint.IndexMetacarpal, HandJoint.MiddleMetacarpal, HandJoint.RingMetacarpal, HandJoint.LittleMetacarpal };
            for (int f = 0; f < 4; f++)
            {
                int m = (int)metacarpals[f];
                Vector3 root = wrist + thumbSide * across[f];
                _joints[m] = root + fingerDir * 0.03f;
                _joints[m + 1] = root + fingerDir * 0.09f;
                _joints[m + 2] = root + fingerDir * 0.13f;
                _joints[m + 3] = root + fingerDir * 0.155f;
                _joints[m + 4] = root + fingerDir * 0.18f;
            }
            _joints[(int)HandJoint.ThumbMetacarpal] = wrist + thumbSide * 0.03f + fingerDir * 0.02f;
            _joints[(int)HandJoint.ThumbProximal] = wrist + thumbSide * 0.05f + fingerDir * 0.045f;
            _joints[(int)HandJoint.ThumbDistal] = wrist + thumbSide * 0.07f + fingerDir * 0.07f;
            _joints[(int)HandJoint.ThumbTip] = wrist + thumbSide * 0.085f + fingerDir * 0.09f;
        }

        /// <summary>An open hand with the palm at a point, fingers tucked close around it.</summary>
        public void PosePalm(Vector3 palm)
        {
            _tracked = true;
            _pinching = false;
            for (int i = 0; i < _joints.Length; i++) _joints[i] = palm;
        }

        void Set(HandJoint joint, Vector3 center, Vector3 open, Vector3 up, float radius, float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            _joints[(int)joint] = center + (open * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
        }

        void FoldFinger(HandJoint metacarpal, Vector3 palm, Vector3 sideways, Vector3 back, float offset)
        {
            int m = (int)metacarpal;
            Vector3 root = palm + sideways * offset;
            for (int i = 0; i < 5; i++) _joints[m + i] = root - back * (0.004f * i);
        }
    }
}

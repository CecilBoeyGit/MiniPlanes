using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Smoothed hand speed, worked out once a frame per hand and shared by anything that needs to tell a
    /// gentle touch from a smack. The faster of the palm and the index tip counts.
    /// </summary>
    public static class HandMotion
    {
        const float Smoothing = 0.04f;

        class Track
        {
            public int frame = -1;
            public bool valid;
            public Vector3 palm, index;
            public Vector3 velPalm, velIndex;
            public float speed;
        }

        static readonly Dictionary<HandSource, Track> Tracks = new Dictionary<HandSource, Track>();

        /// <summary>The hand's speed in metres per second, or 0 when it isn't tracked.</summary>
        public static float Speed(HandSource hand)
        {
            if (hand == null) return 0f;
            if (!Tracks.TryGetValue(hand, out var t))
            {
                t = new Track();
                Tracks[hand] = t;
            }
            if (t.frame == Time.frameCount) return t.speed;
            // A gap in readings would turn the hand's whole journey since then into one frame's speed.
            if (t.frame != Time.frameCount - 1) t.valid = false;
            t.frame = Time.frameCount;

            if (!hand.IsTracked || !hand.TryGetJoint(HandJoint.Palm, out var palm) || !hand.TryGetJoint(HandJoint.IndexTip, out var index))
            {
                t.valid = false;
                t.speed = 0f;
                return 0f;
            }
            float dt = Time.deltaTime;
            if (t.valid && dt > 0f)
            {
                float k = 1f - Mathf.Exp(-dt / Smoothing);
                t.velPalm = Vector3.Lerp(t.velPalm, (palm - t.palm) / dt, k);
                t.velIndex = Vector3.Lerp(t.velIndex, (index - t.index) / dt, k);
            }
            else
            {
                t.velPalm = Vector3.zero;
                t.velIndex = Vector3.zero;
            }
            t.palm = palm;
            t.index = index;
            t.valid = true;
            t.speed = Mathf.Max(t.velPalm.magnitude, t.velIndex.magnitude);
            return t.speed;
        }

        /// <summary>Forgets the hand's last position, so the next reading starts from rest. For tests.</summary>
        public static void Reset(HandSource hand)
        {
            if (hand != null && Tracks.TryGetValue(hand, out var t))
            {
                t.valid = false;
                t.frame = -1;
                t.speed = 0f;
            }
        }
    }
}

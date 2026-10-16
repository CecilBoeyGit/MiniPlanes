using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    public enum HandSide { Left, Right }

    /// <summary>Joint names mirror the OpenXR 26-joint hand layout.</summary>
    public enum HandJoint
    {
        Wrist, Palm,
        ThumbMetacarpal, ThumbProximal, ThumbDistal, ThumbTip,
        IndexMetacarpal, IndexProximal, IndexIntermediate, IndexDistal, IndexTip,
        MiddleMetacarpal, MiddleProximal, MiddleIntermediate, MiddleDistal, MiddleTip,
        RingMetacarpal, RingProximal, RingIntermediate, RingDistal, RingTip,
        LittleMetacarpal, LittleProximal, LittleIntermediate, LittleDistal, LittleTip,
        Count
    }

    /// <summary>A capsule approximating one bone of the hand, in world space.</summary>
    public struct BoneSegment
    {
        public Vector3 Start;
        public Vector3 End;
        public float Radius;
        public HandJoint From;
        public HandJoint To;

        /// <summary>Distance from a point to the capsule surface (negative when inside).</summary>
        public float SurfaceDistance(Vector3 point)
        {
            Vector3 ab = End - Start;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-10f ? Mathf.Clamp01(Vector3.Dot(point - Start, ab) / len2) : 0f;
            Vector3 closest = Start + ab * t;
            return Vector3.Distance(point, closest) - Radius;
        }
    }

    /// <summary>
    /// Abstract hand-tracking input. Gameplay scripts only talk to this, so the Meta adapter
    /// can be swapped for a fake hand in the editor or for another tracking stack.
    /// </summary>
    public abstract class HandSource : MonoBehaviour
    {
        public HandSide side;

        public abstract bool IsTracked { get; }
        public abstract bool IsHighConfidence { get; }
        /// <summary>Hand size relative to a reference adult hand (1 = reference).</summary>
        public abstract float HandScale { get; }
        public abstract bool IsIndexPinching { get; }
        public abstract bool TryGetJoint(HandJoint joint, out Vector3 position);

        static readonly HandJoint[][] Chains =
        {
            new[] { HandJoint.Wrist, HandJoint.ThumbMetacarpal, HandJoint.ThumbProximal, HandJoint.ThumbDistal, HandJoint.ThumbTip },
            new[] { HandJoint.Wrist, HandJoint.IndexMetacarpal, HandJoint.IndexProximal, HandJoint.IndexIntermediate, HandJoint.IndexDistal, HandJoint.IndexTip },
            new[] { HandJoint.Wrist, HandJoint.MiddleMetacarpal, HandJoint.MiddleProximal, HandJoint.MiddleIntermediate, HandJoint.MiddleDistal, HandJoint.MiddleTip },
            new[] { HandJoint.Wrist, HandJoint.RingMetacarpal, HandJoint.RingProximal, HandJoint.RingIntermediate, HandJoint.RingDistal, HandJoint.RingTip },
            new[] { HandJoint.Wrist, HandJoint.LittleMetacarpal, HandJoint.LittleProximal, HandJoint.LittleIntermediate, HandJoint.LittleDistal, HandJoint.LittleTip },
        };

        // Radius per link index along a chain: wrist->metacarpal, metacarpal->proximal, proximal->intermediate, ...
        static readonly float[] LinkRadius = { 0.014f, 0.012f, 0.0095f, 0.0085f, 0.0075f };

        /// <summary>Fills the buffer with capsules for every bone of the hand. Returns false when untracked.</summary>
        public virtual bool GetBoneSegments(List<BoneSegment> buffer)
        {
            buffer.Clear();
            if (!IsTracked) return false;
            float scale = Mathf.Clamp(HandScale, 0.6f, 1.5f);

            foreach (var chain in Chains)
            {
                for (int i = 0; i < chain.Length - 1; i++)
                {
                    if (!TryGetJoint(chain[i], out var a) || !TryGetJoint(chain[i + 1], out var b)) continue;
                    buffer.Add(new BoneSegment
                    {
                        Start = a,
                        End = b,
                        Radius = LinkRadius[Mathf.Min(i, LinkRadius.Length - 1)] * scale,
                        From = chain[i],
                        To = chain[i + 1]
                    });
                }
            }

            // Thick capsule through the palm so the gaps between metacarpals are solid rock too.
            if (TryGetJoint(HandJoint.Wrist, out var wrist) && TryGetJoint(HandJoint.Palm, out var palm))
            {
                Vector3 dir = palm - wrist;
                buffer.Add(new BoneSegment
                {
                    Start = wrist + dir * 0.2f,
                    End = palm + dir * 0.6f,
                    Radius = 0.022f * scale,
                    From = HandJoint.Wrist,
                    To = HandJoint.Palm
                });
            }
            return buffer.Count > 0;
        }

        /// <summary>Smallest surface distance from a point to any bone capsule, or +infinity when untracked.</summary>
        public static float MinSurfaceDistance(List<BoneSegment> segments, Vector3 point)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < segments.Count; i++)
            {
                float d = segments[i].SurfaceDistance(point);
                if (d < best) best = d;
            }
            return best;
        }
    }
}

using System;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// When both hands hold a full C and come close together, the two bubbles squeeze into one peanut
    /// and round out into a big O. The O is easier to hit, so it scores less than a single C.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public class RingMerger : MonoBehaviour
    {
        public HandRing left;
        public HandRing right;
        public Material bubbleMaterial;

        [Header("Merging")]
        [Tooltip("The rings merge when their centres are closer than this, in metres.")]
        public float mergeEnterDistance = 0.17f;
        [Tooltip("The big ring splits when the centres move further apart than this, in metres.")]
        public float mergeExitDistance = 0.22f;
        [Tooltip("Seconds the peanut takes to round out into a circle.")]
        public float mergeSeconds = 0.4f;
        [Tooltip("How far through the merge the big ring starts scoring, 0 to 1.")]
        [Range(0f, 1f)] public float scoringFromMerge = 0.6f;
        [Tooltip("How pinched the outline starts, at the beginning of a merge.")]
        public float startPeanut = 0.34f;

        [Header("Big ring")]
        [Tooltip("Big ring radius as a fraction of the mean distance to both hands' loop joints.")]
        public float radiusScale = 0.92f;
        public float minRadius = 0.04f;
        public float maxRadius = 0.13f;
        public float zoneHalfDepth = 0.035f;
        public float zoneMargin = 0.012f;
        public float scoreCooldown = 0.5f;
        public float scoredFlashSeconds = 0.35f;

        public bool IsMerged { get; private set; }
        public float MergeProgress { get; private set; }
        public bool CanScore => IsMerged && MergeProgress >= scoringFromMerge;
        public Vector3 Center { get; private set; }
        public Vector3 Normal { get; private set; } = Vector3.forward;
        public float Radius { get; private set; }

        public event Action<RingMerger, Vector3> Scored;
        public event Action<RingMerger> PlaneHitRing;

        BubbleRingVisual _visual;
        MiniPlane _plane;
        Vector3 _axis = Vector3.right;
        bool _hadPrev;
        float _prevSigned;
        Vector3 _prevPos;
        float _lastScoreTime = -10f;

        public void Track(MiniPlane plane)
        {
            _plane = plane;
            _hadPrev = false;
        }

        void Awake()
        {
            _visual = BubbleRingVisual.Create("Ring_Merged", bubbleMaterial);
            _visual.idleWobble = 0.016f;
        }

        void OnDestroy()
        {
            if (_visual != null) _visual.DestroySelf();
        }

        void LateUpdate()
        {
            if (left == null || right == null) return;

            bool bothFull = left.IsActive && right.IsActive;
            float separation = bothFull ? Vector3.Distance(left.Center, right.Center) : float.PositiveInfinity;

            bool wasMerged = IsMerged;
            if (!IsMerged && bothFull && separation < mergeEnterDistance) IsMerged = true;
            else if (IsMerged && (!bothFull || separation > mergeExitDistance)) IsMerged = false;

            left.MergedHidden = IsMerged;
            right.MergedHidden = IsMerged;

            if (!IsMerged)
            {
                MergeProgress = 0f;
                _hadPrev = false;
                _visual.Hide();
                return;
            }

            if (!wasMerged)
            {
                MergeProgress = 0f;
                _hadPrev = false;
                _visual.Kick();
            }
            MergeProgress = Mathf.Clamp01(MergeProgress + Time.deltaTime / Mathf.Max(0.01f, mergeSeconds));

            UpdateGeometry();

            // Ease out: the peanut relaxes quickly, then settles into the circle.
            float eased = 1f - (1f - MergeProgress) * (1f - MergeProgress);
            float peanut = Mathf.Lerp(startPeanut, 0f, eased);
            _visual.Show(Center, Normal, _axis, Radius, 1f, peanut);

            if (CanScore && _plane != null && _plane.IsFlying) CheckCrossing();
            else _hadPrev = false;
        }

        void UpdateGeometry()
        {
            Vector3 n = left.Normal;
            Vector3 n2 = right.Normal;
            if (Vector3.Dot(n, n2) < 0f) n2 = -n2;
            n = (n + n2).normalized;
            if (n.sqrMagnitude < 1e-6f) n = left.Normal;
            if (Vector3.Dot(n, Normal) < 0f) n = -n;

            Vector3 c = (left.Center + right.Center) * 0.5f;

            float sum = 0f;
            int count = 0;
            AddRadii(left, c, ref sum, ref count);
            AddRadii(right, c, ref sum, ref count);
            float r = count > 0 ? sum / count : minRadius;

            Center = c;
            Normal = n;
            Radius = Mathf.Clamp(r * radiusScale, minRadius, maxRadius);
            Vector3 axis = right.Center - left.Center;
            if (axis.sqrMagnitude > 1e-6f) _axis = axis.normalized;
        }

        static void AddRadii(HandRing ring, Vector3 c, ref float sum, ref int count)
        {
            var pts = ring.LoopPoints;
            for (int i = 0; i < pts.Count; i++)
            {
                sum += Vector3.Distance(pts[i], c);
                count++;
            }
        }

        float DistanceToFingers(Vector3 p)
        {
            return Mathf.Min(left.DistanceToLoopFingers(p), right.DistanceToLoopFingers(p));
        }

        void CheckCrossing()
        {
            Vector3 pos = _plane.Position;
            float signed = Vector3.Dot(pos - Center, Normal);

            if (_hadPrev && _prevSigned * signed <= 0f && Mathf.Abs(signed) < 0.08f && Mathf.Abs(_prevSigned) < 0.08f)
            {
                float denom = _prevSigned - signed;
                float t = Mathf.Abs(denom) > 1e-6f ? Mathf.Clamp01(_prevSigned / denom) : 0.5f;
                Vector3 x = Vector3.Lerp(_prevPos, pos, t);
                Vector3 d = x - Center;
                float radial = (d - Normal * Vector3.Dot(d, Normal)).magnitude;

                if (DistanceToFingers(x) < left.FingerBand + _plane.Radius)
                {
                    PlaneHitRing?.Invoke(this);
                }
                else if (radial < Radius && Time.time - _lastScoreTime > scoreCooldown)
                {
                    _lastScoreTime = Time.time;
                    _visual.Flash(scoredFlashSeconds);
                    Scored?.Invoke(this, x);
                }
            }

            _prevSigned = signed;
            _prevPos = pos;
            _hadPrev = true;
        }

        /// <summary>Inside this slab the merged ring's own test decides, rather than the bone capsules.</summary>
        public bool IsPointInRingZone(Vector3 p)
        {
            if (!IsMerged) return false;
            Vector3 d = p - Center;
            float s = Vector3.Dot(d, Normal);
            if (Mathf.Abs(s) > zoneHalfDepth) return false;
            float radial = (d - Normal * s).magnitude;
            return radial < Radius + zoneMargin;
        }
    }
}

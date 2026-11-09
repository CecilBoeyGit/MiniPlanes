using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Detects a C sign made with the index finger and thumb and fills a bubble scoring ring inside it.
    /// The bubble's outline follows the shape of the finger loop. A closed OK sign counts too (a C with no
    /// gap). The plane scores by flying through the bubble or the finger loop, from either side. Touching
    /// one of the loop's fingers is a hit.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class HandRing : MonoBehaviour
    {
        const int ContourSamples = BubbleRingVisual.ContourSamples;

        public HandSource hand;

        [Header("C-sign detection (metres and degrees, scaled by hand size)")]
        [Tooltip("Largest thumb-tip to index-tip gap that still counts as a C.")]
        public float maxGapEnter = 0.085f;
        [Tooltip("Gap above which an active C releases (hysteresis).")]
        public float maxGapExit = 0.105f;
        [Tooltip("Index finger curl window for a C: 0 is straight, 180 is fully curled.")]
        public float minCurlDeg = 25f;
        public float maxCurlDeg = 135f;
        [Tooltip("Minimum area enclosed by the thumb-index loop, in square metres. Rejects pinches and fists.")]
        public float minLoopArea = 0.0003f;
        [Tooltip("Seconds the C can drop out, from a tracking glitch, before the ring lets go.")]
        public float releaseGraceSeconds = 0.1f;

        [Header("Radial fill")]
        [Tooltip("Seconds the bubble takes to fill. It scores only once full.")]
        public float fillSeconds = 0.24f;
        [Tooltip("Seconds the bubble takes to collapse after the hand opens.")]
        public float unfillSeconds = 0.12f;

        [Header("Ring geometry")]
        [Tooltip("Approximate finger radius used for the crash band around the loop's fingers.")]
        public float fingerRadius = 0.0075f;
        [Tooltip("Half depth of the slab around the ring in which hand collisions are left to the ring test.")]
        public float zoneHalfDepth = 0.03f;
        [Tooltip("Extra margin around the loop fingers inside which the ring test takes over from bone capsules.")]
        public float zoneMargin = 0.012f;
        public float scoreCooldown = 0.5f;
        [Tooltip("Bubble size as a fraction of the finger loop. Below 1 it sits just inside the fingers.")]
        public float visualRadiusScale = 0.935f;
        [Range(0f, 1f), Tooltip("0 is a plain circle. 1 follows the finger loop exactly.")]
        public float shapeFollow = 0.8f;
        [Tooltip("How quickly the outline catches up with the fingers. Lower hides more tracking jitter.")]
        public float shapeFollowRate = 20f;
        public float scoredFlashSeconds = 0.35f;

        [Header("Visuals")]
        public Material bubbleMaterial;

        /// <summary>The ring is full and can score.</summary>
        public bool IsActive { get; private set; }
        public bool GestureHeld { get; private set; }
        public float Fill { get; private set; }
        public float Gap { get; private set; }
        public float IndexCurlDeg { get; private set; }
        public Vector3 Center { get; private set; }
        public Vector3 Normal { get; private set; } = Vector3.forward;
        /// <summary>Mean distance from the centre to the loop's finger joints.</summary>
        public float Radius { get; private set; }
        public float LoopArea { get; private set; }
        public float BubbleRadius => Radius * visualRadiusScale;
        public IReadOnlyList<Vector3> LoopPoints => _loop;

        /// <summary>Off while menus are open.</summary>
        public bool GameplayEnabled { get; set; } = true;
        /// <summary>This hand is holding a balloon crosshair, so its pinch must not make a ring.</summary>
        public bool Suppressed { get; set; }
        /// <summary>This hand's ring has merged into the big O, which takes over visuals and scoring.</summary>
        public bool MergedHidden { get; set; }

        public event Action<HandRing, Vector3> Scored;
        public event Action<HandRing> PlaneHitRing;

        MiniPlane _plane;
        bool _hadPrev;
        float _prevSigned;
        Vector3 _prevPos;
        float _lastScoreTime = -10f;
        float _lastPoseTime = -10f;
        BubbleRingVisual _visual;
        Vector3 _tangent, _bitangent;
        readonly List<Vector3> _loop = new List<Vector3>(8);
        readonly List<Vector2> _loop2D = new List<Vector2>(8);
        readonly float[] _contour = new float[ContourSamples];
        readonly float[] _contourRaw = new float[ContourSamples];
        readonly float[] _contourTemp = new float[ContourSamples];

        // Loop order: up the index finger to its tip, across the gap to the thumb tip, down the thumb,
        // then across the palm web back to the index knuckle. Only the tip-to-tip edge is open air.
        static readonly HandJoint[] LoopJoints =
        {
            HandJoint.IndexProximal, HandJoint.IndexIntermediate, HandJoint.IndexDistal, HandJoint.IndexTip,
            HandJoint.ThumbTip, HandJoint.ThumbDistal, HandJoint.ThumbProximal
        };
        const int GapEdge = 3;   // edge from loop[3] (index tip) to loop[4] (thumb tip)

        public void Track(MiniPlane plane)
        {
            _plane = plane;
            _hadPrev = false;
        }

        void Awake()
        {
            for (int i = 0; i < ContourSamples; i++) _contour[i] = 1f;
            // The hand root is scaled by hand size, so the visual lives at the scene root.
            _visual = BubbleRingVisual.Create("Ring_" + (hand != null ? hand.side.ToString() : name), bubbleMaterial);
        }

        void OnDestroy()
        {
            if (_visual != null) _visual.DestroySelf();
        }

        void LateUpdate()
        {
            UpdatePose();
            UpdateVisual();

            if (IsActive && !MergedHidden && _plane != null && _plane.IsFlying)
                CheckCrossing();
            else
                _hadPrev = false;
        }

        float Scale => Mathf.Clamp(hand != null ? hand.HandScale : 1f, 0.6f, 1.5f);

        void UpdatePose()
        {
            bool allowed = GameplayEnabled && !Suppressed;
            bool pose = allowed && EvaluateGesture();
            if (pose)
            {
                _lastPoseTime = Time.time;
                UpdateContour();
            }
            GestureHeld = allowed && (pose || (GestureHeld && Time.time - _lastPoseTime <= releaseGraceSeconds));

            bool wasFull = Fill >= 1f;
            float rate = GestureHeld ? 1f / Mathf.Max(0.01f, fillSeconds) : -1f / Mathf.Max(0.01f, unfillSeconds);
            Fill = Mathf.Clamp01(Fill + rate * Time.deltaTime);
            IsActive = GestureHeld && Fill >= 1f;

            if (IsActive && !wasFull)
            {
                _hadPrev = false;
                if (_visual != null) _visual.Kick();
            }
        }

        bool EvaluateGesture()
        {
            if (hand == null || !hand.IsTracked) return false;

            if (!hand.TryGetJoint(HandJoint.ThumbTip, out var thumbTip) ||
                !hand.TryGetJoint(HandJoint.IndexTip, out var indexTip) ||
                !hand.TryGetJoint(HandJoint.IndexProximal, out var indexProximal) ||
                !hand.TryGetJoint(HandJoint.IndexIntermediate, out var indexIntermediate) ||
                !hand.TryGetJoint(HandJoint.IndexDistal, out var indexDistal))
                return false;

            float scale = Scale;
            Gap = Vector3.Distance(thumbTip, indexTip);
            IndexCurlDeg = Vector3.Angle(indexIntermediate - indexProximal, indexTip - indexDistal);

            // The wider release limit only helps hold a ring that already exists.
            bool gapOk = Gap <= (GestureHeld ? maxGapExit : maxGapEnter) * scale;

            float slack = IsActive ? 8f : 0f;
            bool curlOk = IndexCurlDeg >= minCurlDeg - slack && IndexCurlDeg <= maxCurlDeg + slack;

            return gapOk && curlOk && UpdateGeometry() && LoopArea >= minLoopArea * scale * scale;
        }

        bool UpdateGeometry()
        {
            _loop.Clear();
            foreach (var j in LoopJoints)
            {
                if (!hand.TryGetJoint(j, out var p)) return false;
                _loop.Add(p);
            }

            Vector3 c = Vector3.zero;
            foreach (var p in _loop) c += p;
            c /= _loop.Count;

            // Newell's method for the best-fit plane normal of the loop polygon.
            Vector3 n = Vector3.zero;
            for (int i = 0; i < _loop.Count; i++)
            {
                Vector3 a = _loop[i], b = _loop[(i + 1) % _loop.Count];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
            }
            if (n.sqrMagnitude < 1e-12f) return false;
            n.Normalize();
            if (Vector3.Dot(n, Normal) < 0f) n = -n;   // keep orientation stable frame to frame

            Vector3 helper = Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right;
            _tangent = Vector3.Cross(n, helper).normalized;
            _bitangent = Vector3.Cross(n, _tangent);

            _loop2D.Clear();
            float area = 0f, radiusSum = 0f;
            for (int i = 0; i < _loop.Count; i++)
            {
                Vector3 d = _loop[i] - c;
                _loop2D.Add(new Vector2(Vector3.Dot(d, _tangent), Vector3.Dot(d, _bitangent)));
                radiusSum += d.magnitude;
            }
            for (int i = 0; i < _loop2D.Count; i++)
            {
                Vector2 a = _loop2D[i], b = _loop2D[(i + 1) % _loop2D.Count];
                area += a.x * b.y - b.x * a.y;
            }

            Center = c;
            Normal = n;
            Radius = radiusSum / _loop.Count;
            LoopArea = Mathf.Abs(area) * 0.5f;
            return true;
        }

        Vector3 RingAxis()
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(Normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            return Vector3.Cross(up, Normal);
        }

        /// <summary>Measures the finger loop in each direction around the ring, so the bubble can follow its shape.</summary>
        void UpdateContour()
        {
            if (Radius <= 1e-5f) return;
            BubbleRingVisual.Frame(Normal, RingAxis(), out Vector3 x, out Vector3 y);
            for (int i = 0; i < ContourSamples; i++)
            {
                float a = i / (float)ContourSamples * Mathf.PI * 2f;
                Vector3 dir = x * Mathf.Sin(a) + y * Mathf.Cos(a);
                float r = RayToLoop(new Vector2(Vector3.Dot(dir, _tangent), Vector3.Dot(dir, _bitangent)));
                _contourRaw[i] = r > 0f ? r / Radius : 1f;
            }

            // Knuckles and finger tips are corners. Round them off, so it reads as a bubble and not a polygon.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < ContourSamples; i++)
                {
                    float before = _contourRaw[(i + ContourSamples - 1) % ContourSamples];
                    float after = _contourRaw[(i + 1) % ContourSamples];
                    _contourTemp[i] = 0.25f * before + 0.5f * _contourRaw[i] + 0.25f * after;
                }
                Array.Copy(_contourTemp, _contourRaw, ContourSamples);
            }

            float k = Fill <= 0f ? 1f : 1f - Mathf.Exp(-shapeFollowRate * Time.deltaTime);
            for (int i = 0; i < ContourSamples; i++)
            {
                float target = Mathf.Clamp(Mathf.Lerp(1f, _contourRaw[i], shapeFollow), 0.45f, 1.7f);
                _contour[i] = Mathf.Lerp(_contour[i], target, k);
            }
        }

        /// <summary>Distance from the loop's centre to its outline in a direction, or 0 if there is no hit.</summary>
        float RayToLoop(Vector2 dir)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < _loop2D.Count; i++)
            {
                Vector2 a = _loop2D[i], e = _loop2D[(i + 1) % _loop2D.Count] - a;
                float denom = dir.x * e.y - dir.y * e.x;
                if (Mathf.Abs(denom) < 1e-8f) continue;
                float t = (a.x * e.y - a.y * e.x) / denom;
                float u = (a.x * dir.y - a.y * dir.x) / denom;
                if (t > 0f && u >= 0f && u <= 1f && t < best) best = t;
            }
            return float.IsInfinity(best) ? 0f : best;
        }

        /// <summary>Where the bubble's rim sits in one of its sample directions, before any wobble.</summary>
        public Vector3 RimPoint(int index)
        {
            BubbleRingVisual.Frame(Normal, RingAxis(), out Vector3 x, out Vector3 y);
            float a = index / (float)ContourSamples * Mathf.PI * 2f;
            return Center + (x * Mathf.Sin(a) + y * Mathf.Cos(a)) * (BubbleRadius * _contour[index % ContourSamples]);
        }

        Vector2 Project(Vector3 p)
        {
            Vector3 d = p - Center;
            return new Vector2(Vector3.Dot(d, _tangent), Vector3.Dot(d, _bitangent));
        }

        bool InsideLoop(Vector2 q)
        {
            bool inside = false;
            int count = _loop2D.Count;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Vector2 a = _loop2D[i], b = _loop2D[j];
                if ((a.y > q.y) != (b.y > q.y))
                {
                    float x = (b.x - a.x) * (q.y - a.y) / (b.y - a.y) + a.x;
                    if (q.x < x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>3D distance from a point to the nearest real finger of the loop (the open gap is skipped).</summary>
        public float DistanceToLoopFingers(Vector3 p)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < _loop.Count; i++)
            {
                if (i == GapEdge) continue;
                float d = DistancePointSegment(p, _loop[i], _loop[(i + 1) % _loop.Count]);
                if (d < best) best = d;
            }
            return best;
        }

        public float FingerBand => fingerRadius * Scale;

        public static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            return Vector3.Distance(p, a + ab * t);
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
                Vector2 q = Project(x);
                bool inHole = q.magnitude < BubbleRadius || InsideLoop(q);

                // Inside the hole only a real touch on a finger counts, so a close pass still scores.
                if (DistanceToLoopFingers(x) < FingerBand + (inHole ? 0f : _plane.Radius))
                {
                    PlaneHitRing?.Invoke(this);
                }
                else if (inHole && Time.time - _lastScoreTime > scoreCooldown)
                {
                    _lastScoreTime = Time.time;
                    if (_visual != null) _visual.Flash(scoredFlashSeconds);
                    Scored?.Invoke(this, x);
                }
            }

            _prevSigned = signed;
            _prevPos = pos;
            _hadPrev = true;
        }

        /// <summary>True when the point sits in the thin slab around the loop, inside it or near its
        /// fingers, where the crossing test takes precedence over bone-capsule collisions.</summary>
        public bool IsPointInRingZone(Vector3 p)
        {
            if (!IsActive) return false;
            float s = Vector3.Dot(p - Center, Normal);
            if (Mathf.Abs(s) > zoneHalfDepth) return false;
            Vector2 q = Project(p);
            if (q.magnitude < BubbleRadius || InsideLoop(q)) return true;
            return DistanceToLoopFingers(p) < FingerBand + zoneMargin;
        }

        void UpdateVisual()
        {
            if (_visual == null) return;
            if (MergedHidden || Fill <= 0f || !GameplayEnabled)
            {
                _visual.Hide();
                return;
            }
            _visual.Show(Center, Normal, RingAxis(), BubbleRadius, Fill, 0f, _contour);
        }
    }
}

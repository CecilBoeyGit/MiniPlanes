using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Detects a C sign made with the index finger and thumb and turns the enclosed loop into a
    /// scoring ring. A closed OK sign counts too (a C with no gap). Scoring and ring collisions use
    /// the real finger polygon: crossing inside it scores, touching one of its fingers crashes.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class HandRing : MonoBehaviour
    {
        public HandSource hand;

        [Header("C-sign detection (metres and degrees, scaled by hand size)")]
        [Tooltip("Largest thumb-tip to index-tip gap that still counts as a C. A closed OK sign (gap 0) also counts.")]
        public float maxGapEnter = 0.075f;
        [Tooltip("Gap above which an active C releases (hysteresis).")]
        public float maxGapExit = 0.09f;
        [Tooltip("Index finger curl window for a C: 0 is straight, 180 is fully curled.")]
        public float minCurlDeg = 30f;
        public float maxCurlDeg = 125f;
        [Tooltip("Minimum area enclosed by the thumb-index loop, in square metres. Rejects pinches and fists.")]
        public float minLoopArea = 0.0004f;
        [Tooltip("Pose must be held this long before the ring activates, in seconds.")]
        public float activateDelay = 0.08f;

        [Header("Ring geometry")]
        [Tooltip("Approximate finger radius used for the crash band around the loop's fingers.")]
        public float fingerRadius = 0.0075f;
        [Tooltip("Half depth of the slab around the ring in which hand collisions are left to the ring test.")]
        public float zoneHalfDepth = 0.03f;
        [Tooltip("Extra margin around the loop fingers inside which the ring test takes over from bone capsules.")]
        public float zoneMargin = 0.012f;
        public float scoreCooldown = 0.5f;

        [Header("Visuals")]
        public MeshRenderer ringVisual;
        public Material activeMaterial;
        public Material scoredMaterial;
        public float scoredFlashSeconds = 0.35f;
        [Tooltip("Visual ring radius as a fraction of the loop's mean joint radius, so it sits inside the fingers.")]
        public float visualRadiusScale = 0.85f;

        public bool IsActive { get; private set; }
        public float Gap { get; private set; }
        public float IndexCurlDeg { get; private set; }
        public Vector3 Center { get; private set; }
        public Vector3 Normal { get; private set; } = Vector3.forward;
        /// <summary>Mean distance from the centre to the loop's finger joints.</summary>
        public float Radius { get; private set; }
        public float LoopArea { get; private set; }

        public event Action<HandRing> Scored;
        public event Action<HandRing> PlaneHitRing;

        MiniPlane _plane;
        bool _hadPrev;
        float _prevSigned;
        Vector3 _prevPos;
        float _lastScoreTime = -10f;
        float _poseTimer;
        float _flashUntil;
        bool _gapClosed;
        Vector3 _tangent, _bitangent;
        readonly List<Vector3> _loop = new List<Vector3>(8);
        readonly List<Vector2> _loop2D = new List<Vector2>(8);

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
            if (ringVisual != null) return;
            // The hand root is scaled by hand size, so the visual lives at the scene root.
            var go = new GameObject("RingVisual_" + (hand != null ? hand.side.ToString() : name));
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = TorusMesh.Build();
            ringVisual = go.AddComponent<MeshRenderer>();
            ringVisual.sharedMaterial = activeMaterial;
            ringVisual.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringVisual.receiveShadows = false;
            ringVisual.enabled = false;
        }

        void OnDestroy()
        {
            if (ringVisual != null && ringVisual.transform.parent == null) Destroy(ringVisual.gameObject);
        }

        void LateUpdate()
        {
            UpdatePose();
            UpdateVisual();

            if (IsActive && _plane != null && _plane.IsFlying)
                CheckCrossing();
            else
                _hadPrev = false;
        }

        float Scale => Mathf.Clamp(hand != null ? hand.HandScale : 1f, 0.6f, 1.5f);

        void UpdatePose()
        {
            if (hand == null || !hand.IsTracked)
            {
                Deactivate();
                return;
            }

            if (!hand.TryGetJoint(HandJoint.ThumbTip, out var thumbTip) ||
                !hand.TryGetJoint(HandJoint.IndexTip, out var indexTip) ||
                !hand.TryGetJoint(HandJoint.IndexProximal, out var indexProximal) ||
                !hand.TryGetJoint(HandJoint.IndexIntermediate, out var indexIntermediate) ||
                !hand.TryGetJoint(HandJoint.IndexDistal, out var indexDistal))
            {
                Deactivate();
                return;
            }

            float scale = Scale;
            Gap = Vector3.Distance(thumbTip, indexTip);
            IndexCurlDeg = Vector3.Angle(indexIntermediate - indexProximal, indexTip - indexDistal);

            if (!_gapClosed && Gap <= maxGapEnter * scale) _gapClosed = true;
            else if (_gapClosed && Gap > maxGapExit * scale) _gapClosed = false;

            float slack = IsActive ? 8f : 0f;
            bool curlOk = IndexCurlDeg >= minCurlDeg - slack && IndexCurlDeg <= maxCurlDeg + slack;

            if (!_gapClosed || !curlOk || !UpdateGeometry() || LoopArea < minLoopArea * scale * scale)
            {
                Deactivate();
                return;
            }

            _poseTimer += Time.deltaTime;
            bool wasActive = IsActive;
            IsActive = _poseTimer >= activateDelay;
            if (IsActive && !wasActive) _hadPrev = false;
        }

        void Deactivate()
        {
            _poseTimer = 0f;
            IsActive = false;
            _hadPrev = false;
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
        float DistanceToLoopFingers(Vector3 p)
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

        static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
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

                if (DistanceToLoopFingers(x) < fingerRadius * Scale + _plane.radius)
                {
                    PlaneHitRing?.Invoke(this);
                }
                else if (InsideLoop(Project(x)) && Time.time - _lastScoreTime > scoreCooldown)
                {
                    _lastScoreTime = Time.time;
                    _flashUntil = Time.time + scoredFlashSeconds;
                    Scored?.Invoke(this);
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
            if (InsideLoop(Project(p))) return true;
            return DistanceToLoopFingers(p) < fingerRadius * Scale + zoneMargin;
        }

        void UpdateVisual()
        {
            if (ringVisual == null) return;
            if (!IsActive)
            {
                ringVisual.enabled = false;
                return;
            }
            ringVisual.enabled = true;
            var t = ringVisual.transform;
            t.position = Center;
            t.rotation = Quaternion.LookRotation(Normal, Mathf.Abs(Vector3.Dot(Normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up);
            t.localScale = Vector3.one * (Radius * visualRadiusScale);
            bool flash = Time.time < _flashUntil && scoredMaterial != null;
            var want = flash ? scoredMaterial : activeMaterial;
            if (want != null && ringVisual.sharedMaterial != want) ringVisual.sharedMaterial = want;
        }
    }

    /// <summary>Builds a torus in the XY plane with its axis along +Z (major radius 1).</summary>
    public static class TorusMesh
    {
        public static Mesh Build(float minorRadius = 0.14f, int majorSegments = 36, int minorSegments = 10)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= majorSegments; i++)
            {
                float u = i / (float)majorSegments * Mathf.PI * 2f;
                Vector3 ringCentre = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= minorSegments; j++)
                {
                    float v = j / (float)minorSegments * Mathf.PI * 2f;
                    Vector3 n = ringCentre * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
                    verts.Add(ringCentre + n * minorRadius);
                    norms.Add(n);
                }
            }
            int stride = minorSegments + 1;
            for (int i = 0; i < majorSegments; i++)
            {
                for (int j = 0; j < minorSegments; j++)
                {
                    int a = i * stride + j;
                    int b = (i + 1) * stride + j;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
                }
            }
            var mesh = new Mesh { name = "Torus" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

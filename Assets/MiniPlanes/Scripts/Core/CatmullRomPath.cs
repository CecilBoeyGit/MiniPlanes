using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Arc-length parameterised Catmull-Rom spline through a list of control points.
    /// The curve is densely sampled once so that PositionAt(distance) moves at constant speed.
    /// </summary>
    public class CatmullRomPath
    {
        readonly List<Vector3> _samples = new List<Vector3>();
        readonly List<float> _cumulative = new List<float>();

        public float Length { get; private set; }
        public IReadOnlyList<Vector3> ControlPoints { get; private set; }
        public IReadOnlyList<Vector3> Samples => _samples;

        public CatmullRomPath(IReadOnlyList<Vector3> points, int samplesPerSegment = 24)
        {
            Rebuild(points, samplesPerSegment);
        }

        public void Rebuild(IReadOnlyList<Vector3> points, int samplesPerSegment = 24)
        {
            ControlPoints = points;
            _samples.Clear();
            _cumulative.Clear();
            Length = 0f;
            if (points == null || points.Count < 2) return;

            int n = points.Count;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = points[Mathf.Max(i - 1, 0)];
                Vector3 p1 = points[i];
                Vector3 p2 = points[i + 1];
                Vector3 p3 = points[Mathf.Min(i + 2, n - 1)];
                int steps = (i == n - 2) ? samplesPerSegment + 1 : samplesPerSegment;
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)samplesPerSegment;
                    Vector3 p = Evaluate(p0, p1, p2, p3, t);
                    if (_samples.Count > 0) Length += Vector3.Distance(_samples[_samples.Count - 1], p);
                    _samples.Add(p);
                    _cumulative.Add(Length);
                }
            }
        }

        static Vector3 Evaluate(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        public Vector3 PositionAt(float distance)
        {
            if (_samples.Count == 0) return Vector3.zero;
            if (distance <= 0f) return _samples[0];
            if (distance >= Length) return _samples[_samples.Count - 1];

            int lo = 0, hi = _cumulative.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_cumulative[mid] <= distance) lo = mid; else hi = mid;
            }
            float segLen = _cumulative[hi] - _cumulative[lo];
            float t = segLen > 1e-6f ? (distance - _cumulative[lo]) / segLen : 0f;
            return Vector3.LerpUnclamped(_samples[lo], _samples[hi], t);
        }

        public Vector3 TangentAt(float distance, float delta = 0.01f)
        {
            Vector3 a = PositionAt(Mathf.Max(0f, distance - delta));
            Vector3 b = PositionAt(Mathf.Min(Length, distance + delta));
            Vector3 d = b - a;
            return d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.forward;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Builds the hidden flight path for a round. The plane enters from outside the player's reach,
    /// circles the seated player for several laps at arm's length, drifting in and out, climbing and
    /// diving, occasionally doubling back on itself, and then leaves again.
    /// </summary>
    public class FlightPathGenerator : MonoBehaviour
    {
        [Tooltip("Distance from the head at which the plane enters and leaves, in metres.")]
        public float entryRadius = 1.6f;
        [Tooltip("Extra angular lead-in before the first waypoint and after the last, in degrees.")]
        public float entryLeadDeg = 35f;
        [Tooltip("Never place a waypoint closer than this to the head, in metres.")]
        public float headClearance = 0.3f;
        [Tooltip("Largest vertical change allowed between consecutive waypoints, in metres.")]
        public float maxHeightStep = 0.12f;
        [Tooltip("Radius and height offset applied while the plane doubles back, so the two legs do not overlap.")]
        public float reversalOffset = 0.08f;

        [Header("Debug")]
        [Tooltip("Draw the hidden path in the scene view and, if a LineRenderer is attached, in play mode.")]
        public bool debugDraw = false;

        public CatmullRomPath CurrentPath { get; private set; }
        public Vector3 LastHeadPosition { get; private set; }

        readonly List<Vector3> _points = new List<Vector3>();
        LineRenderer _line;

        void Awake()
        {
            _line = GetComponent<LineRenderer>();
            if (_line != null) _line.enabled = false;
        }

        public CatmullRomPath Generate(LevelConfig cfg, Vector3 headPosition, Vector3 headForward, int seed)
        {
            var rng = new System.Random(seed);
            LastHeadPosition = headPosition;

            Vector3 fwd = headForward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            float spacing = Mathf.Clamp(cfg.waypointSpacingDeg, 12f, 60f);
            float totalSweep = Mathf.Max(cfg.laps, 0.5f) * 360f;
            int count = Mathf.Max(Mathf.CeilToInt(totalSweep / spacing), 4);
            float dir = rng.NextDouble() < 0.5 ? 1f : -1f;

            // Slow waves over the laps make the radius breathe in and out and the height climb and dive.
            float radiusWaves = Lerp(1.5f, 3.5f, rng);
            float radiusPhase = (float)rng.NextDouble() * Mathf.PI * 2f;
            float heightWaves = Lerp(0.8f, 2.2f, rng);
            float heightPhase = (float)rng.NextDouble() * Mathf.PI * 2f;

            _points.Clear();

            // Start somewhere in the front half so the player sees the plane arrive.
            float az = Lerp(-70f, 70f, rng);
            float height = Mathf.Lerp(cfg.minHeight, cfg.maxHeight, 0.5f + 0.5f * Mathf.Sin(heightPhase));
            _points.Add(PolarPoint(headPosition, fwd, az - dir * entryLeadDeg, entryRadius, height + 0.05f));

            int reversalLeft = 0;
            float reversalRadius = 0f, reversalHeight = 0f;
            float travelled = 0f;

            for (int i = 0; i < count; i++)
            {
                if (reversalLeft > 0)
                {
                    reversalLeft--;
                    if (reversalLeft == 0)
                    {
                        dir = -dir;
                        reversalRadius = 0f;
                        reversalHeight = 0f;
                    }
                }
                else if (i > 1 && i < count - 3 && rng.NextDouble() < cfg.reversalChance)
                {
                    reversalLeft = 2 + rng.Next(3);
                    dir = -dir;
                    reversalRadius = (rng.NextDouble() < 0.5 ? -1f : 1f) * reversalOffset;
                    reversalHeight = (rng.NextDouble() < 0.5 ? -1f : 1f) * reversalOffset * 0.9f;
                }

                az += dir * (spacing + Lerp(-cfg.azimuthJitterDeg, cfg.azimuthJitterDeg, rng));
                travelled += spacing;
                float lapT = travelled / 360f;

                float radius = Mathf.Lerp(cfg.minRadius, cfg.maxRadius,
                                   0.5f + 0.5f * Mathf.Sin(lapT * radiusWaves * Mathf.PI * 2f + radiusPhase))
                               + reversalRadius + Lerp(-0.03f, 0.03f, rng);
                radius = Mathf.Clamp(radius, headClearance, cfg.maxRadius + reversalOffset + 0.05f);

                float targetHeight = Mathf.Lerp(cfg.minHeight, cfg.maxHeight,
                                         0.5f + 0.5f * Mathf.Sin(lapT * heightWaves * Mathf.PI * 2f + heightPhase))
                                     + reversalHeight + Lerp(-0.03f, 0.03f, rng);
                height = Mathf.MoveTowards(height, targetHeight, maxHeightStep);

                _points.Add(PolarPoint(headPosition, fwd, az, radius, height));
            }

            _points.Add(PolarPoint(headPosition, fwd, az + dir * entryLeadDeg, entryRadius, height + 0.05f));

            CurrentPath = new CatmullRomPath(_points);
            UpdateDebugLine();
            return CurrentPath;
        }

        static float Lerp(float a, float b, System.Random rng)
        {
            return Mathf.Lerp(a, b, (float)rng.NextDouble());
        }

        static Vector3 PolarPoint(Vector3 head, Vector3 fwd, float azimuthDeg, float radius, float height)
        {
            Vector3 dir = Quaternion.AngleAxis(azimuthDeg, Vector3.up) * fwd;
            return head + dir * radius + Vector3.up * height;
        }

        public void SetDebugDraw(bool on)
        {
            debugDraw = on;
            UpdateDebugLine();
        }

        void UpdateDebugLine()
        {
            if (_line == null) return;
            if (!debugDraw || CurrentPath == null)
            {
                _line.enabled = false;
                return;
            }
            var samples = CurrentPath.Samples;
            _line.positionCount = samples.Count;
            for (int i = 0; i < samples.Count; i++) _line.SetPosition(i, samples[i]);
            _line.enabled = true;
        }

        void OnDrawGizmos()
        {
            if (!debugDraw || CurrentPath == null) return;
            Gizmos.color = Color.cyan;
            var s = CurrentPath.Samples;
            for (int i = 1; i < s.Count; i++) Gizmos.DrawLine(s[i - 1], s[i]);
            Gizmos.color = Color.yellow;
            foreach (var p in _points) Gizmos.DrawSphere(p, 0.008f);
        }
    }
}

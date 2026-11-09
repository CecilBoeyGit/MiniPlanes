using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Builds the flight path for one flight. The plane enters from outside the player's reach, flies a
    /// random string of manoeuvres in front of them, then leaves. It never goes behind the player.
    ///
    /// The path is flown rather than drawn: a virtual pilot with a limited turning circle steers toward
    /// one target after another, so no flight repeats and the line can never kink. Steering happens in
    /// an unrolled space (sideways, height, distance from the player) that wraps around the player, so
    /// the plane sweeps around them and a straight run can't cut through their head.
    ///
    /// Targets come from a random mix of manoeuvres. Open targets prefer space the path hasn't visited
    /// yet. Higher randomness turns tighter and adds corkscrews, loops and orbits.
    /// </summary>
    public static class FlightPathGenerator
    {
        /// <summary>Hard limit either side of straight ahead, in degrees. Includes entry and exit legs.</summary>
        public const float MaxAzimuthDeg = 85f;

        /// <summary>A generated path never turns tighter than this, in metres.</summary>
        public const float MinTurnRadius = 0.03f;

        const float EntryRadius = 1.4f;
        const float HeadClearance = 0.3f;
        const int MaxAttempts = 12;

        public static CatmullRomPath Build(LevelConfig cfg, Vector3 head, Vector3 forward, int seed)
        {
            Vector3 fwd = forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            CatmullRomPath best = null;
            float bestTurn = -1f;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var path = new Pilot(cfg, head, fwd, seed + attempt * 101).Fly();
                float turn = TightestTurn(path);
                if (turn >= MinTurnRadius) return path;
                if (turn > bestTurn)
                {
                    bestTurn = turn;
                    best = path;
                }
            }
            return best;
        }

        /// <summary>Largest angle away from straight ahead reached anywhere along a path, in degrees.</summary>
        public static float MaxAzimuthOf(CatmullRomPath path, Vector3 head, Vector3 forward)
        {
            Vector3 fwd = forward;
            fwd.y = 0f;
            fwd.Normalize();
            float worst = 0f;
            foreach (var s in path.Samples)
            {
                Vector3 d = s - head;
                d.y = 0f;
                if (d.sqrMagnitude < 1e-6f) continue;
                worst = Mathf.Max(worst, Mathf.Abs(Vector3.SignedAngle(fwd, d, Vector3.up)));
            }
            return worst;
        }

        /// <summary>Smallest turning radius along a path, measured over 2 cm steps, in metres.</summary>
        public static float TightestTurn(CatmullRomPath path)
        {
            const float step = 0.02f;
            float best = float.PositiveInfinity;
            for (float d = step; d < path.Length - step; d += step)
            {
                float a = Vector3.Angle(path.TangentAt(d - step * 0.5f, 0.005f), path.TangentAt(d + step * 0.5f, 0.005f)) * Mathf.Deg2Rad;
                if (a > 1e-3f) best = Mathf.Min(best, step / a);
            }
            return best;
        }

        /// <summary>
        /// One flight. Positions are (x sideways in metres around the player, y height, z distance from
        /// the player), and only ToWorld knows about the real curved space.
        /// </summary>
        sealed class Pilot
        {
            enum Move { Sweep, Wander, Slalom, Climb, Corkscrew, Loop, Orbit }

            const float Step = 0.01f;
            const int StepsPerControlPoint = 4;
            const float TurnSmoothing = 0.14f;
            const int MaxSteps = 60000;
            const int RecentTargets = 24;

            readonly System.Random _rng;
            readonly Vector3 _head, _fwd;
            readonly float _randomness, _targetLength;
            readonly bool _allowLoops;
            readonly float _refRadius, _halfArc, _azSoft;
            readonly float _xMax, _yMin, _yMax, _zMin, _zMax;
            readonly float _nearSoft;
            readonly float _baseTurn;
            readonly Queue<Vector3> _targets = new Queue<Vector3>();
            readonly List<Vector3> _recent = new List<Vector3>();

            Vector3 _pos, _heading = Vector3.back, _turn;
            float _turnRadius, _capture;
            bool _limitFar;
            Move _lastMove = Move.Sweep;

            public Pilot(LevelConfig cfg, Vector3 head, Vector3 fwd, int seed)
            {
                _rng = new System.Random(seed);
                _head = head;
                _fwd = fwd;
                _randomness = Mathf.Clamp01(cfg.randomness);
                _allowLoops = cfg.allowLoops;
                _targetLength = Mathf.Max(cfg.planeSpeed * cfg.pathDurationSeconds, 1f);

                _zMin = Mathf.Max(HeadClearance + 0.01f, Mathf.Min(cfg.minRadius, cfg.maxRadius));
                _zMax = Mathf.Max(_zMin + 0.05f, cfg.maxRadius);
                _yMin = Mathf.Min(cfg.minHeight, cfg.maxHeight);
                _yMax = Mathf.Max(cfg.minHeight, cfg.maxHeight);
                _refRadius = 0.5f * (_zMin + _zMax);

                // Everything beyond the arc is squeezed into a soft margin, which ends at the hard limit.
                _azSoft = 7f;
                _halfArc = Mathf.Clamp(cfg.frontArcDeg * 0.5f, 20f, MaxAzimuthDeg - _azSoft);
                _xMax = _halfArc * Mathf.Deg2Rad * _refRadius;
                _nearSoft = Mathf.Min(0.06f, _zMin - HeadClearance);

                _baseTurn = cfg.turnRadius > 0f ? Mathf.Max(0.06f, cfg.turnRadius) : Mathf.Lerp(0.13f, 0.065f, _randomness);
            }

            public CatmullRomPath Fly()
            {
                float side = Chance(0.5f) ? 1f : -1f;
                _pos = new Vector3(side * Rand(0.45f, 1f) * _xMax, Rand(_yMin, _yMax), EntryRadius);
                _heading = new Vector3(-side * Rand(0f, 0.3f), Rand(-0.08f, 0.08f), -1f).normalized;
                _turn = Vector3.zero;
                _limitFar = false;
                UseTurn(_baseTurn, 1f);

                var points = new List<Vector3>();
                Vector3 world = ToWorld(_pos);
                points.Add(world);
                float length = 0f;
                float exitReserve = EntryRadius - _zMax;
                bool exiting = false;
                int stepsOnTarget = 0, stepsAllowed = 0;

                for (int step = 1; step <= MaxSteps; step++)
                {
                    if (_targets.Count == 0)
                    {
                        NextMove();
                        stepsOnTarget = 0;
                        stepsAllowed = AllowedSteps(_targets.Peek());
                    }

                    Vector3 target = _targets.Peek();
                    if ((target - _pos).magnitude < _capture || stepsOnTarget > stepsAllowed)
                    {
                        _targets.Dequeue();
                        if (_targets.Count > 0) stepsAllowed = AllowedSteps(_targets.Peek());
                        stepsOnTarget = 0;
                        if (_targets.Count == 0) continue;
                        target = _targets.Peek();
                    }
                    stepsOnTarget++;

                    Steer(target);
                    _pos += _heading * Step;
                    if (!exiting && !_limitFar && _pos.z <= _zMax) _limitFar = true;

                    Vector3 next = ToWorld(_pos);
                    length += Vector3.Distance(world, next);
                    world = next;
                    if (step % StepsPerControlPoint == 0) points.Add(world);

                    if (!exiting && _limitFar && _pos.z <= _zMax && length + exitReserve >= _targetLength)
                    {
                        exiting = true;
                        _limitFar = false;
                        _targets.Clear();
                        _targets.Enqueue(new Vector3(Mathf.Clamp(_pos.x + _heading.x * 0.25f, -_xMax, _xMax),
                            Mathf.Clamp(_pos.y + _heading.y * 0.25f, _yMin, _yMax), EntryRadius + 0.2f));
                        UseTurn(_baseTurn, 1f);
                        stepsOnTarget = 0;
                        stepsAllowed = MaxSteps;
                    }
                    if (exiting && _pos.z >= EntryRadius) break;
                }

                points.Add(world);
                return new CatmullRomPath(points, 10);
            }

            int AllowedSteps(Vector3 target)
            {
                return Mathf.CeilToInt(2.5f * (target - _pos).magnitude / Step) + 120;
            }

            void UseTurn(float radius, float captureScale)
            {
                _turnRadius = Mathf.Max(radius, 0.05f);
                _capture = Mathf.Max(0.035f, _turnRadius * 1.15f * captureScale);
            }

            /// <summary>Turns the heading toward the target, with a limited and smoothly changing turn rate.</summary>
            void Steer(Vector3 target)
            {
                Vector3 want = (target - _pos).normalized;

                bool urgent = AvoidWall(ref want, Vector3.right, _xMax - _pos.x);
                urgent |= AvoidWall(ref want, Vector3.left, _pos.x + _xMax);
                urgent |= AvoidWall(ref want, Vector3.up, _yMax - _pos.y);
                urgent |= AvoidWall(ref want, Vector3.down, _pos.y - _yMin);
                urgent |= AvoidWall(ref want, Vector3.back, _pos.z - _zMin);
                if (_limitFar) urgent |= AvoidWall(ref want, Vector3.forward, _zMax - _pos.z);

                float angle = Vector3.Angle(_heading, want) * Mathf.Deg2Rad;
                Vector3 axis = Vector3.Cross(_heading, want);
                if (axis.sqrMagnitude < 1e-6f)
                {
                    if (angle < 1f) axis = Vector3.zero;
                    else axis = Vector3.Cross(_heading, Mathf.Abs(_heading.y) < 0.9f ? Vector3.up : Vector3.right);
                }
                if (axis != Vector3.zero) axis.Normalize();

                float rate = Step / _turnRadius;
                Vector3 wanted = axis * (urgent ? rate : Mathf.Min(angle * 0.25f, rate));
                _turn = Vector3.Lerp(_turn, wanted, TurnSmoothing);
                float amount = _turn.magnitude;
                if (amount > 1e-6f)
                    _heading = (Quaternion.AngleAxis(amount * Mathf.Rad2Deg, _turn / amount) * _heading).normalized;
            }

            /// <summary>
            /// Starts turning away from a limit of the flying space while there is still room for a full
            /// turning circle. Without this the plane overshoots, and squeezing that overshoot back inside
            /// the limit crushes its U-turn into a sharp point.
            /// </summary>
            bool AvoidWall(ref Vector3 want, Vector3 outward, float distance)
            {
                float closing = Vector3.Dot(_heading, outward);
                if (closing <= 0f) return false;
                float needed = _turnRadius * (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - closing * closing))) * 1.3f + 0.03f;
                if (distance > needed) return false;
                want -= outward * (Mathf.Max(0f, Vector3.Dot(want, outward)) + 0.35f);
                want.Normalize();
                return true;
            }

            // ---------------------------------------------------------------- manoeuvres

            void NextMove()
            {
                Move move = PickMove();
                _lastMove = move;
                UseTurn(_baseTurn * Rand(0.9f, 1.2f), 1f);
                switch (move)
                {
                    case Move.Sweep: Add(OpenSpot(Mathf.Max(_xMax * 0.8f, 0.3f), _xMax * 2f)); break;
                    case Move.Wander: Add(OpenSpot(0.16f, 0.32f)); break;
                    case Move.Slalom: AddSlalom(); break;
                    case Move.Climb: AddClimb(); break;
                    case Move.Corkscrew: AddCorkscrew(); break;
                    case Move.Loop: AddCircle(true); break;
                    default: AddCircle(false); break;
                }
            }

            Move PickMove()
            {
                // The fancy moves only start once a level has some randomness.
                // Never two in a row: the plane travels somewhere else first, so they can't pile into a knot.
                bool wasFancy = _lastMove == Move.Corkscrew || _lastMove == Move.Loop || _lastMove == Move.Orbit;
                float fancy = !_allowLoops || wasFancy ? 0f : Mathf.Max(0.15f, _randomness);
                float[] weights = { 1f, 0.9f, 0.8f, 0.7f, 0.7f * fancy, 0.6f * fancy, 0.6f * fancy };
                weights[(int)_lastMove] = 0f;
                float total = 0f;
                foreach (float w in weights) total += w;
                float pick = Rand(0f, total);
                for (int i = 0; i < weights.Length; i++)
                {
                    pick -= weights[i];
                    if (pick <= 0f && weights[i] > 0f) return (Move)i;
                }
                return Move.Sweep;
            }

            void AddSlalom()
            {
                int gates = _rng.Next(3, 6);
                float dir = Mathf.Abs(_pos.x) > _xMax * 0.35f ? -Mathf.Sign(_pos.x) : (Chance(0.5f) ? 1f : -1f);
                float x = _pos.x, updown = Chance(0.5f) ? 1f : -1f;
                float midY = Rand(Mathf.Lerp(_yMin, _yMax, 0.35f), Mathf.Lerp(_yMin, _yMax, 0.65f));
                float amp = Rand(0.07f, 0.15f);
                for (int i = 0; i < gates; i++)
                {
                    x += dir * Rand(0.13f, 0.22f);
                    Add(new Vector3(x, midY + updown * amp, Rand(_zMin, _zMax)));
                    updown = -updown;
                }
            }

            void AddClimb()
            {
                float y = _pos.y > 0.5f * (_yMin + _yMax) ? Rand(_yMin, Mathf.Lerp(_yMin, _yMax, 0.25f)) : Rand(Mathf.Lerp(_yMin, _yMax, 0.75f), _yMax);
                Add(new Vector3(_pos.x + Rand(-0.14f, 0.14f), y, Rand(_zMin, _zMax)));
            }

            /// <summary>A helix travelling sideways, winding through height and distance.</summary>
            void AddCorkscrew()
            {
                float rc = Mathf.Min(Rand(0.065f, 0.09f), 0.5f * (_zMax - _zMin) + 0.02f);
                UseTurn(Mathf.Min(_baseTurn, rc * 0.85f), 0.6f);
                float dir = Mathf.Abs(_pos.x) > _xMax * 0.3f ? -Mathf.Sign(_pos.x) : (Chance(0.5f) ? 1f : -1f);
                float spin = Chance(0.5f) ? 1f : -1f;
                float cy = Mathf.Clamp(_pos.y, _yMin + rc, _yMax - rc);
                float cz = 0.5f * (_zMin + _zMax);
                Remember(new Vector3(_pos.x, cy, cz));
                int count = _rng.Next(6, 13);
                float advance = Rand(0.025f, 0.04f);
                float a = Rand(0f, Mathf.PI * 2f);
                float x = _pos.x;
                for (int i = 0; i < count; i++)
                {
                    a += spin * Mathf.PI / 3f;
                    x += dir * advance;
                    Add(new Vector3(x, cy + Mathf.Sin(a) * rc, cz + Mathf.Cos(a) * rc), false);
                }
            }

            /// <summary>A full circle on the spot: a vertical loop, or a flat orbit swinging in and out.</summary>
            void AddCircle(bool vertical)
            {
                float room = vertical ? 0.5f * (_yMax - _yMin) : 0.5f * (_zMax - _zMin) + 0.02f;
                float rc = Mathf.Min(Rand(0.07f, 0.11f), room);
                UseTurn(Mathf.Min(_baseTurn, rc * 0.85f), 0.6f);
                float spin = Chance(0.5f) ? 1f : -1f;
                float ahead = _heading.x >= 0f ? 1f : -1f;
                float cx = Mathf.Clamp(_pos.x + ahead * rc * 0.8f, -_xMax + rc, _xMax - rc);
                float cv = vertical
                    ? Mathf.Clamp(_pos.y + (Chance(0.5f) ? rc : -rc), _yMin + rc, _yMax - rc)
                    : 0.5f * (_zMin + _zMax);
                float other = vertical ? 0f : Mathf.Clamp(_pos.y, _yMin, _yMax);
                Remember(vertical ? new Vector3(cx, cv, _pos.z) : new Vector3(cx, other, cv));
                float a = Mathf.Atan2(vertical ? _pos.y - cv : _pos.z - cv, _pos.x - cx);
                int count = _rng.Next(6, 9);
                for (int i = 0; i < count; i++)
                {
                    a += spin * Mathf.PI / 3f;
                    float sx = cx + Mathf.Cos(a) * rc;
                    float sv = cv + Mathf.Sin(a) * rc;
                    Add(vertical ? new Vector3(sx, sv, Mathf.Clamp(_pos.z, _zMin, _zMax)) : new Vector3(sx, other, sv), false);
                }
            }

            void Add(Vector3 target, bool remember = true)
            {
                target.x = Mathf.Clamp(target.x, -_xMax, _xMax);
                target.y = Mathf.Clamp(target.y, _yMin, _yMax);
                target.z = Mathf.Clamp(target.z, _zMin, _zMax);
                _targets.Enqueue(target);
                if (remember) Remember(target);
            }

            void Remember(Vector3 spot)
            {
                _recent.Add(spot);
                if (_recent.Count > RecentTargets) _recent.RemoveAt(0);
            }

            /// <summary>A random spot the right distance away, as far as possible from recent targets.</summary>
            Vector3 OpenSpot(float minDistance, float maxDistance)
            {
                Vector3 best = _pos;
                float bestScore = float.NegativeInfinity;
                for (int i = 0; i < 10; i++)
                {
                    var c = new Vector3(Rand(-_xMax, _xMax), Rand(_yMin, _yMax), Rand(_zMin, _zMax));
                    float away = (c - _pos).magnitude;
                    float clearance = float.PositiveInfinity;
                    foreach (var r in _recent) clearance = Mathf.Min(clearance, (c - r).magnitude);
                    if (_recent.Count == 0) clearance = 0.5f;
                    float penalty = Mathf.Max(0f, minDistance - away) + Mathf.Max(0f, away - maxDistance);
                    float score = clearance - penalty * 2f + Rand(0f, 0.05f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                return best;
            }

            // ---------------------------------------------------------------- mapping

            Vector3 ToWorld(Vector3 s)
            {
                float az = Soft(s.x / _refRadius * Mathf.Rad2Deg, -_halfArc, _halfArc, _azSoft);
                float height = Soft(s.y, _yMin, _yMax, 0.04f);
                float radius = Soft(s.z, _zMin, _limitFar ? _zMax : float.PositiveInfinity, _limitFar ? 0.05f : 1f);
                if (radius < _zMin) radius = Soft(s.z, _zMin, float.PositiveInfinity, Mathf.Max(0.001f, _nearSoft));
                Vector3 dir = Quaternion.AngleAxis(az, Vector3.up) * _fwd;
                return _head + dir * radius + Vector3.up * height;
            }

            /// <summary>Unchanged inside the limits; beyond them it eases toward a hard stop, with no kink.</summary>
            static float Soft(float x, float lo, float hi, float margin)
            {
                if (x > hi) return hi + margin * Tanh((x - hi) / margin);
                if (x < lo) return lo - margin * Tanh((lo - x) / margin);
                return x;
            }

            static float Tanh(float x)
            {
                float e = Mathf.Exp(Mathf.Min(x, 10f));
                return (e - 1f / e) / (e + 1f / e);
            }

            float Rand(float a, float b)
            {
                return Mathf.Lerp(a, b, (float)_rng.NextDouble());
            }

            bool Chance(float p)
            {
                return _rng.NextDouble() < p;
            }
        }
    }
}

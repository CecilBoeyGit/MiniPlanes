using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Places the floating islands of an Infinite wave around the player and the flight path, never on the
    /// path itself. The islands, and the shards they break into, live in <see cref="FloatingIsland"/> and
    /// <see cref="IslandDebris"/>.
    /// </summary>
    public class IslandField : MonoBehaviour
    {
        [Tooltip("Islands sit between these distances from the head, in metres.")]
        public float minHeadDistance = 0.3f;
        public float maxHeadDistance = 0.8f;
        [Tooltip("Islands sit within this angle either side of straight ahead, in degrees. Above 90 wraps round the player.")]
        public float halfArcDeg = 110f;
        [Tooltip("Island heights relative to the eyes, in metres.")]
        public float minHeight = -0.4f;
        public float maxHeight = 0.15f;

        GameArt _art;
        HandSource[] _hands;
        MiniPlane _plane;

        public int Count => FloatingIsland.Active.Count;

        public void Init(GameArt art, HandSource[] hands, MiniPlane plane)
        {
            _art = art;
            _hands = hands;
            _plane = plane;
        }

        /// <summary>Clears the last wave's islands and scatters this wave's, keeping them off the path.</summary>
        public void Build(LevelConfig cfg, CatmullRomPath path, Vector3 headPos, Vector3 forward, int seed)
        {
            Clear();
            if (cfg == null || cfg.islandCount <= 0) return;
            var rng = new System.Random(seed * 31 + 7);
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();

            var placed = new List<(Vector3 pos, float size)>();
            int attempts = cfg.islandCount * 60;
            for (int a = 0; a < attempts && placed.Count < cfg.islandCount; a++)
            {
                float az = Mathf.Lerp(-halfArcDeg, halfArcDeg, (float)rng.NextDouble());
                float dist = Mathf.Lerp(minHeadDistance, maxHeadDistance, (float)rng.NextDouble());
                float height = Mathf.Lerp(minHeight, maxHeight, (float)rng.NextDouble());
                float size = Mathf.Lerp(cfg.islandMinSize, cfg.islandMaxSize, (float)rng.NextDouble());
                Vector3 pos = headPos + Quaternion.AngleAxis(az, Vector3.up) * forward * dist + Vector3.up * height;

                if (path != null && DistanceToPath(path, pos) < size * 0.6f + cfg.islandPathClearance) continue;
                bool crowded = false;
                foreach (var p in placed)
                    if (Vector3.Distance(p.pos, pos) < (p.size + size) * 0.55f + 0.03f) { crowded = true; break; }
                if (crowded) continue;

                var go = new GameObject("FloatingIsland");
                go.transform.SetParent(transform, false);
                var island = go.AddComponent<FloatingIsland>();
                island.Setup(size, rng.Next(), _art, _plane, _hands, pos);
                placed.Add((pos, size));
            }
        }

        public static float DistanceToPath(CatmullRomPath path, Vector3 point)
        {
            float best = float.PositiveInfinity;
            var s = path.Samples;
            for (int i = 0; i < s.Count - 1; i++)
                best = Mathf.Min(best, HandRing.DistancePointSegment(point, s[i], s[i + 1]));
            return best;
        }

        public void Clear()
        {
            FloatingIsland.DestroyAll();
            IslandDebris.DestroyAll();
        }
    }

    /// <summary>
    /// A small floating island: a grassy top over a rocky cone. The plane flying into it crashes. A hand
    /// touching it shatters it into shards that drift slowly away in every direction.
    /// </summary>
    public class FloatingIsland : MonoBehaviour
    {
        public static readonly List<FloatingIsland> Active = new List<FloatingIsland>();
        /// <summary>An island broke apart.</summary>
        public static event Action<FloatingIsland> AnyShattered;

        public float Size { get; private set; }
        public bool IsAlive { get; private set; }
        public Vector3 Position => transform.position;

        MiniPlane _plane;
        HandSource[] _hands;
        GameArt _art;
        Vector3 _anchor;
        float _bobPhase;
        int _seed;
        readonly List<BoneSegment> _segments = new List<BoneSegment>(32);

        public void Setup(float size, int seed, GameArt art, MiniPlane plane, HandSource[] hands, Vector3 position)
        {
            Size = size;
            _seed = seed;
            _art = art;
            _plane = plane;
            _hands = hands;
            _anchor = position;
            var rng = new System.Random(seed);
            _bobPhase = (float)rng.NextDouble() * 6.28f;
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            var go = new GameObject("Mesh");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = IslandMeshes.Island(size * 0.5f, rng);
            var r = go.AddComponent<MeshRenderer>();
            if (art != null) r.sharedMaterial = art.planeMatte;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            IsAlive = true;
        }

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        /// <summary>Whether a point lies within the island's rough shape, grown by a margin.</summary>
        public bool Contains(Vector3 world, float margin)
        {
            float r = Size * 0.5f;
            Vector3 p = world - transform.position;
            // An ellipsoid covering the flat grassy top and the cone beneath it.
            Vector3 q = new Vector3(p.x / (r + margin), (p.y + r * 0.3f) / (r * 0.65f + margin), p.z / (r + margin));
            return q.sqrMagnitude < 1f;
        }

        /// <summary>Moves the island somewhere else at once. For tests.</summary>
        public void MoveTo(Vector3 position)
        {
            _anchor = position;
            transform.position = position;
        }

        void Update()
        {
            if (!IsAlive) return;
            transform.position = _anchor + Vector3.up * (Mathf.Sin(Time.time * 0.7f + _bobPhase) * 0.005f);
            transform.Rotate(0f, 4f * Time.deltaTime, 0f, Space.World);
            if (GameManager.IsPaused) return;

            if (_plane != null && _plane.IsFlying && Contains(_plane.Position, _plane.Radius))
            {
                _plane.Damage("hit a floating island");
                Shatter(_plane.Velocity.normalized);
                return;
            }

            if (_hands == null) return;
            foreach (var h in _hands)
            {
                if (h == null || !h.IsTracked || !h.GetBoneSegments(_segments)) continue;
                foreach (var seg in _segments)
                {
                    Vector3 closest = ClosestOnSegment(transform.position, seg.Start, seg.End);
                    if (Contains(closest, seg.Radius))
                    {
                        Shatter((transform.position - closest).normalized);
                        return;
                    }
                }
            }
        }

        static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len2 = d.sqrMagnitude;
            float t = len2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, d) / len2) : 0f;
            return a + d * t;
        }

        /// <summary>Breaks the island into slowly drifting shards. The push leans their drift one way.</summary>
        public void Shatter(Vector3 push)
        {
            if (!IsAlive) return;
            IsAlive = false;
            var rng = new System.Random(_seed ^ 0x5f3759);
            int pieces = 5 + rng.Next(4);
            float r = Size * 0.5f;
            for (int i = 0; i < pieces; i++)
            {
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                Vector3 at = transform.position + new Vector3(dir.x * r * 0.6f, dir.y * r * 0.3f - r * 0.15f, dir.z * r * 0.6f);
                float speed = Mathf.Lerp(0.025f, 0.06f, (float)rng.NextDouble());
                Vector3 velocity = (dir + push * 0.6f).normalized * speed;
                var go = new GameObject("IslandDebris");
                go.transform.SetParent(transform.parent, false);
                var debris = go.AddComponent<IslandDebris>();
                debris.Setup(Mathf.Lerp(0.18f, 0.3f, (float)rng.NextDouble()) * Size, rng.Next(), _art, _plane, _hands, at, velocity);
            }
            ComicVfx.Play(ComicKind.Crash, transform.position, Size * 1.4f);
            AnyShattered?.Invoke(this);
            Destroy(gameObject);
        }

        public static void DestroyAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }
    }

    /// <summary>
    /// A shard of a shattered island, drifting slowly. It crashes the plane if they meet. A hand touching
    /// it turns it to dust.
    /// </summary>
    public class IslandDebris : MonoBehaviour
    {
        public static readonly List<IslandDebris> Active = new List<IslandDebris>();
        /// <summary>A hand turned a shard to dust.</summary>
        public static event Action<IslandDebris> AnyDusted;

        [Tooltip("Seconds after breaking off before a hand can dust a shard, so the hand that broke the island doesn't clear it at once.")]
        public float handGraceSeconds = 0.35f;
        public float lifetime = 14f;
        [Tooltip("Shards farther than this from the head disappear, in metres.")]
        public float maxDistanceFromHead = 1.6f;

        public float Radius { get; private set; }
        public bool IsAlive { get; private set; }
        public Vector3 Position => transform.position;
        public Vector3 Velocity => _velocity;

        MiniPlane _plane;
        HandSource[] _hands;
        Vector3 _velocity;
        Vector3 _spin;
        Vector3 _origin;
        float _age;
        float _size;
        readonly List<BoneSegment> _segments = new List<BoneSegment>(32);

        public void Setup(float size, int seed, GameArt art, MiniPlane plane, HandSource[] hands, Vector3 position, Vector3 velocity)
        {
            _size = size;
            Radius = size * 0.5f;
            _plane = plane;
            _hands = hands;
            _velocity = velocity;
            _origin = position;
            transform.position = position;
            var rng = new System.Random(seed);
            _spin = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 160f;

            var go = new GameObject("Mesh");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = IslandMeshes.Rock(Radius, rng);
            var r = go.AddComponent<MeshRenderer>();
            if (art != null) r.sharedMaterial = art.planeMatte;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            IsAlive = true;
        }

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        /// <summary>Sends the shard a new way. For tests.</summary>
        public void SetVelocity(Vector3 velocity) { _velocity = velocity; }

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;
            transform.position += _velocity * dt;
            transform.Rotate(_spin * dt, Space.Self);

            float fade = Mathf.Clamp01((lifetime - _age) / 1f);
            transform.localScale = Vector3.one * fade;
            if (_age > lifetime || Vector3.Distance(transform.position, _origin) > maxDistanceFromHead)
            {
                Destroy(gameObject);
                return;
            }
            if (GameManager.IsPaused) return;

            if (_plane != null && _plane.IsFlying && Vector3.Distance(_plane.Position, transform.position) < Radius + _plane.Radius)
            {
                _plane.Damage("hit by island debris");
                Dust();
                return;
            }

            if (_age < handGraceSeconds || _hands == null) return;
            foreach (var h in _hands)
            {
                if (h == null || !h.IsTracked || !h.GetBoneSegments(_segments)) continue;
                if (HandSource.MinSurfaceDistance(_segments, transform.position) < Radius)
                {
                    Dust();
                    AnyDusted?.Invoke(this);
                    return;
                }
            }
        }

        void Dust()
        {
            IsAlive = false;
            ComicVfx.Play(ComicKind.Poof, transform.position, _size * 2.2f);
            Destroy(gameObject);
        }

        public static void DestroyAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }
    }

    /// <summary>Procedural low-poly meshes with vertex colours for the islands and their shards.</summary>
    public static class IslandMeshes
    {
        static readonly Color Grass = new Color(0.56f, 0.78f, 0.44f);
        static readonly Color GrassDark = new Color(0.42f, 0.66f, 0.36f);
        static readonly Color Dirt = new Color(0.66f, 0.48f, 0.34f);
        static readonly Color Stone = new Color(0.58f, 0.54f, 0.52f);
        static readonly Color Trunk = new Color(0.52f, 0.36f, 0.26f);
        static readonly Color Pine = new Color(0.3f, 0.58f, 0.4f);

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();

            // Each triangle gets its own vertices: flat facets, like a carved toy.
            public void Tri(Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d);
                c.Add(ca); c.Add(cb); c.Add(cd);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>A grassy disc on a rocky cone, about two radii across, its top at y = 0.</summary>
        public static Mesh Island(float radius, System.Random rng)
        {
            var b = new Builder();
            int n = 10;
            var rim = new Vector3[n];
            var lip = new Vector3[n];
            var mid = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float k = Mathf.Lerp(0.78f, 1f, (float)rng.NextDouble());
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                rim[i] = dir * radius * k + Vector3.up * (radius * 0.04f * (float)rng.NextDouble());
                lip[i] = dir * radius * k * 0.96f + Vector3.down * radius * 0.12f;
                mid[i] = dir * radius * k * Mathf.Lerp(0.5f, 0.65f, (float)rng.NextDouble()) + Vector3.down * radius * Mathf.Lerp(0.45f, 0.55f, (float)rng.NextDouble());
            }
            Vector3 top = Vector3.up * radius * 0.08f;
            Vector3 tip = new Vector3(radius * 0.08f * ((float)rng.NextDouble() - 0.5f), -radius * 1.05f, radius * 0.08f * ((float)rng.NextDouble() - 0.5f));
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                b.Tri(top, rim[j], rim[i], Grass, GrassDark, Grass);
                b.Tri(rim[i], rim[j], lip[i], GrassDark, GrassDark, Dirt);
                b.Tri(rim[j], lip[j], lip[i], GrassDark, Dirt, Dirt);
                b.Tri(lip[i], lip[j], mid[i], Dirt, Dirt, Dirt);
                b.Tri(lip[j], mid[j], mid[i], Dirt, Stone, Dirt);
                b.Tri(mid[i], mid[j], tip, Stone, Stone, Stone);
            }

            int trees = rng.Next(3);
            for (int k = 0; k < trees; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * Mathf.Lerp(0.15f, 0.5f, (float)rng.NextDouble()) + top * 0.5f;
                float h = radius * Mathf.Lerp(0.5f, 0.8f, (float)rng.NextDouble());
                Cone(b, at, radius * 0.05f, h * 0.3f, Trunk, Trunk, 5);
                Cone(b, at + Vector3.up * h * 0.25f, radius * 0.2f, h * 0.75f, Pine, GrassDark, 7);
            }
            return b.Build("FloatingIsland");
        }

        static void Cone(Builder b, Vector3 basePoint, float radius, float height, Color side, Color tipColor, int sides)
        {
            Vector3 apex = basePoint + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 p0 = basePoint + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 p1 = basePoint + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                b.Tri(apex, p1, p0, tipColor, side, side);
                b.Tri(basePoint, p0, p1, side, side, side);
            }
        }

        /// <summary>A lumpy rock shard about one radius in size, part grass, part stone.</summary>
        public static Mesh Rock(float radius, System.Random rng)
        {
            var b = new Builder();
            // A squashed, jittered octahedron.
            var p = new[]
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
            };
            for (int i = 0; i < p.Length; i++)
                p[i] = p[i] * radius * Mathf.Lerp(0.7f, 1.1f, (float)rng.NextDouble());
            bool grassy = rng.NextDouble() < 0.5;
            Color topColor = grassy ? Grass : Dirt;
            int[,] faces = { { 0, 4, 3 }, { 0, 3, 5 }, { 0, 5, 2 }, { 0, 2, 4 }, { 1, 3, 4 }, { 1, 5, 3 }, { 1, 2, 5 }, { 1, 4, 2 } };
            for (int f = 0; f < 8; f++)
            {
                Color col = f < 4 ? topColor : (f % 2 == 0 ? Stone : Dirt);
                b.Tri(p[faces[f, 0]], p[faces[f, 1]], p[faces[f, 2]], col, col, col);
            }
            return b.Build("IslandShard");
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    public enum TubeShape { StraightShort, StraightLong, CurvedShort, CurvedLong }

    /// <summary>
    /// A booster tube powerup: a glass tube about as wide as a C ring, straight or curved, short or long.
    /// Pinch it to pick it up; it keeps its angle in your hand, like a real object, so you can line it up
    /// with where you remember the path goes. The plane flying in one end and out the other without
    /// touching the wall gets a speed boost, and the tube is used up. The plane touching the wall breaks the
    /// tube with no boost. Hands never break it: it only goes when its time runs out or the plane meets it.
    /// </summary>
    public class BoosterTube : MonoBehaviour
    {
        public static readonly List<BoosterTube> Active = new List<BoosterTube>();

        /// <summary>The plane flew cleanly through a tube.</summary>
        public static event Action<BoosterTube> AnyBoosted;
        /// <summary>The plane touched a tube's wall and broke it.</summary>
        public static event Action<BoosterTube> AnyWallHit;

        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Inside radius of the tube, in metres. About the size of a C ring.")]
        public float innerRadius = 0.036f;
        public float wallThickness = 0.006f;
        [Tooltip("Share of the plane's collision radius that counts against the wall. Below 1 forgives wingtips.")]
        public float wallMarginPerPlaneRadius = 0.6f;
        [Tooltip("A pinch this close to the tube's surface picks it up, in metres.")]
        public float grabReach = 0.035f;
        [Tooltip("How quickly a held tube catches up with the hand. Higher is stiffer.")]
        public float followSharpness = 25f;
        public Color rimColor = new Color(1f, 0.56f, 0.22f, 1f);
        public Color glassColor = new Color(1f, 0.78f, 0.45f, 1f);

        public TubeShape Shape { get; private set; }
        public bool IsAlive { get; private set; }
        public bool IsHeld => _holder != null;
        /// <summary>Length along the tube's centre, in metres.</summary>
        public float Length { get; private set; }
        /// <summary>Bend radius of a curved tube; 0 for a straight one.</summary>
        public float BendRadius { get; private set; }
        public bool PlaneInside => _state == PassState.Inside;
        /// <summary>Seconds before the tube vanishes by itself. Holding it pauses the count.</summary>
        public float TimeLeft => _life;
        public Vector3 Position => transform.position;

        enum PassState { Outside, Inside }

        LevelConfig _cfg;
        MiniPlane _plane;
        object _holder;
        Vector3 _grabPosOffset;
        Quaternion _grabRotOffset;
        Vector3 _targetPos;
        Quaternion _targetRot;
        float _life;
        readonly List<Vector3> _points = new List<Vector3>();
        readonly List<float> _along = new List<float>();
        PassState _state;
        int _entryEnd = -1;
        bool _prevValid;
        int _prevEnd = -1;
        MeshRenderer _glass;
        MaterialPropertyBlock _glassBlock;
        float _glassAlpha = 0.85f;


        /// <summary>Length and bend radius of each shape, in metres.</summary>
        public static void Dimensions(TubeShape shape, out float length, out float bendRadius)
        {
            switch (shape)
            {
                case TubeShape.StraightShort: length = 0.09f; bendRadius = 0f; break;
                case TubeShape.StraightLong: length = 0.17f; bendRadius = 0f; break;
                case TubeShape.CurvedShort: length = 0.12f; bendRadius = 0.14f; break;
                default: length = 0.21f; bendRadius = 0.16f; break;
            }
        }

        public void Setup(TubeShape shape, LevelConfig cfg, MiniPlane plane, GameArt art, Vector3 position, Quaternion rotation,
            HandSource[] hands = null)
        {
            Shape = shape;
            _cfg = cfg;
            _plane = plane;
            _life = cfg != null ? cfg.boosterTubeLifetime : 24f;
            transform.SetPositionAndRotation(position, rotation);
            _targetPos = position;
            _targetRot = rotation;

            Dimensions(shape, out float length, out float bend);
            Length = length;
            BendRadius = bend;
            BuildCentreline();
            BuildVisuals(art);
            IsAlive = true;
        }

        void BuildCentreline()
        {
            _points.Clear();
            _along.Clear();
            const int n = 17;
            for (int i = 0; i < n; i++)
            {
                float u = i / (n - 1f) - 0.5f;
                Vector3 p;
                if (BendRadius <= 0f)
                {
                    p = new Vector3(0f, 0f, u * Length);
                }
                else
                {
                    // An arc in the local XZ plane, its middle at the origin heading +Z, bending toward +X.
                    float a = u * Length / BendRadius;
                    p = new Vector3(BendRadius - BendRadius * Mathf.Cos(a), 0f, BendRadius * Mathf.Sin(a));
                }
                _points.Add(p);
                _along.Add(i == 0 ? 0f : _along[i - 1] + Vector3.Distance(_points[i - 1], p));
            }
        }

        /// <summary>The tube's centre line in its own space, from one end to the other.</summary>
        public IReadOnlyList<Vector3> LocalCentreline => _points;

        /// <summary>The centre of one end in the world: 0 or 1.</summary>
        public Vector3 EndPosition(int end)
        {
            return transform.TransformPoint(end == 0 ? _points[0] : _points[_points.Count - 1]);
        }

        Vector3 LocalTangent(int i)
        {
            int a = Mathf.Max(0, i - 1);
            int b = Mathf.Min(_points.Count - 1, i + 1);
            return (_points[b] - _points[a]).normalized;
        }

        // ---------------------------------------------------------------- visuals

        void BuildVisuals(GameArt art)
        {
            float wallMid = innerRadius + wallThickness * 0.5f;

            var glassGo = new GameObject("Glass");
            glassGo.transform.SetParent(transform, false);
            glassGo.AddComponent<MeshFilter>().sharedMesh = SweepMesh(wallMid, 20);
            _glass = glassGo.AddComponent<MeshRenderer>();
            _glass.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _glass.receiveShadows = false;
            if (art != null && art.tubeGlass != null) _glass.sharedMaterial = art.tubeGlass;
            _glassBlock = new MaterialPropertyBlock();
            _glassBlock.SetColor(BaseColorId, glassColor);
            _glassBlock.SetFloat(AlphaId, _glassAlpha);
            _glass.SetPropertyBlock(_glassBlock);

            // Solid rims at both ends and bands along the length, so the tube reads clearly in passthrough.
            var rings = new List<CombineInstance>();
            int last = _points.Count - 1;
            AddRing(rings, 0, wallMid, wallThickness * 0.9f);
            AddRing(rings, last, wallMid, wallThickness * 0.9f);
            int bands = Shape == TubeShape.StraightShort ? 1 : Shape == TubeShape.CurvedShort ? 2 : 3;
            for (int b = 1; b <= bands; b++)
                AddRing(rings, Mathf.RoundToInt(last * b / (bands + 1f)), wallMid, wallThickness * 0.45f);
            var rimMesh = new Mesh { name = "TubeRims" };
            rimMesh.CombineMeshes(rings.ToArray(), true, true);
            foreach (var c in rings) Destroy(c.mesh);

            var rimGo = new GameObject("Rims");
            rimGo.transform.SetParent(transform, false);
            rimGo.AddComponent<MeshFilter>().sharedMesh = rimMesh;
            var rim = rimGo.AddComponent<MeshRenderer>();
            rim.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rim.receiveShadows = false;
            if (art != null) rim.sharedMaterial = art.planeMatte;
            var block = new MaterialPropertyBlock();
            block.SetFloat("_UseVertexColor", 0f);
            block.SetColor(BaseColorId, rimColor);
            rim.SetPropertyBlock(block);
        }

        void AddRing(List<CombineInstance> list, int index, float major, float minor)
        {
            Vector3 t = LocalTangent(index);
            var m = TorusMesh(major, minor, 24, 8);
            list.Add(new CombineInstance
            {
                mesh = m,
                transform = Matrix4x4.TRS(_points[index], Quaternion.LookRotation(t, Vector3.up), Vector3.one)
            });
        }

        Mesh SweepMesh(float radius, int around)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < _points.Count; i++)
            {
                Vector3 t = LocalTangent(i);
                Vector3 n = Vector3.Cross(Vector3.up, t).normalized;
                for (int k = 0; k <= around; k++)
                {
                    float phi = k / (float)around * Mathf.PI * 2f;
                    Vector3 dir = n * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi);
                    verts.Add(_points[i] + dir * radius);
                    normals.Add(dir);
                }
            }
            int row = around + 1;
            for (int i = 0; i < _points.Count - 1; i++)
            {
                for (int k = 0; k < around; k++)
                {
                    int a = i * row + k, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }
            var mesh = new Mesh { name = "TubeGlass" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A torus lying across local Z, centred on the origin.</summary>
        public static Mesh TorusMesh(float major, float minor, int around, int tubeSides)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= around; i++)
            {
                float a = i / (float)around * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                for (int j = 0; j <= tubeSides; j++)
                {
                    float b = j / (float)tubeSides * Mathf.PI * 2f;
                    Vector3 n = dir * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    verts.Add(dir * major + n * minor);
                    normals.Add(n);
                }
            }
            int row = tubeSides + 1;
            for (int i = 0; i < around; i++)
            {
                for (int j = 0; j < tubeSides; j++)
                {
                    int a = i * row + j, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
            var mesh = new Mesh { name = "Torus" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- holding

        /// <summary>How far a point is from the tube's outer surface, in metres. Negative inside the wall.</summary>
        public float SurfaceDistance(Vector3 world)
        {
            Closest(transform.InverseTransformPoint(world), out _, out float r, out _);
            return Mathf.Abs(r - (innerRadius + wallThickness * 0.5f)) - wallThickness * 0.5f;
        }

        public bool CanGrabAt(Vector3 world)
        {
            return IsAlive && !IsHeld && SurfaceDistance(world) < grabReach;
        }

        /// <summary>Picks the tube up, keeping where it sits relative to the hand.</summary>
        public void Grab(object holder, Vector3 handPos, Quaternion handRot)
        {
            _holder = holder;
            Quaternion inv = Quaternion.Inverse(handRot);
            _grabPosOffset = inv * (transform.position - handPos);
            _grabRotOffset = inv * transform.rotation;
            _targetPos = transform.position;
            _targetRot = transform.rotation;
        }

        public void Drag(Vector3 handPos, Quaternion handRot)
        {
            _targetPos = handPos + handRot * _grabPosOffset;
            _targetRot = handRot * _grabRotOffset;
        }

        public void Release(object holder)
        {
            if (_holder == holder) _holder = null;
        }

        /// <summary>Puts the tube somewhere at once. For tests and scripted placement.</summary>
        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            _targetPos = position;
            _targetRot = rotation;
            _prevValid = false;
            _state = PassState.Outside;
        }

        // ---------------------------------------------------------------- flying through

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;

            if (IsHeld)
            {
                float k = 1f - Mathf.Exp(-followSharpness * dt);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, _targetPos, k),
                    Quaternion.Slerp(transform.rotation, _targetRot, k));
            }
            else
            {
                _life -= dt;
                if (_life <= 0f)
                {
                    Break(ComicKind.Poof);
                    return;
                }
            }

            float alpha = _life < 2f && !IsHeld && Mathf.Repeat(_life * 4f, 1f) < 0.4f ? 0.25f : _glassAlpha;
            if (_glass != null)
            {
                _glassBlock.SetFloat(AlphaId, alpha);
                _glass.SetPropertyBlock(_glassBlock);
            }

            CheckPlane();
        }


        void CheckPlane()
        {
            if (_plane == null || !_plane.IsFlying)
            {
                _prevValid = false;
                _state = PassState.Outside;
                return;
            }

            Closest(transform.InverseTransformPoint(_plane.Position), out _, out float r, out int end);
            float margin = _plane.Radius * wallMarginPerPlaneRadius;
            bool atEnd = end >= 0;
            bool inBore = !atEnd && r < innerRadius - margin;
            bool touchingWall = !atEnd && !inBore && r <= innerRadius + wallThickness + margin;

            if (touchingWall)
            {
                HitWall();
                return;
            }

            if (_state == PassState.Outside)
            {
                // In through an open end: last frame beyond a cap, this frame inside the bore.
                if (inBore && _prevValid && _prevEnd >= 0)
                {
                    _state = PassState.Inside;
                    _entryEnd = _prevEnd;
                }
            }
            else if (atEnd)
            {
                if (end != _entryEnd)
                {
                    Pass();
                    return;
                }
                _state = PassState.Outside;
            }

            _prevValid = true;
            _prevEnd = end;
        }

        /// <summary>
        /// The nearest point of the centre line to a local point: its distance along the tube, the distance
        /// off the line, and which end it lies beyond (0 or 1), or -1 when it is level with the tube.
        /// </summary>
        void Closest(Vector3 local, out float along, out float offLine, out int beyondEnd)
        {
            float best = float.PositiveInfinity;
            along = 0f;
            beyondEnd = -1;
            int last = _points.Count - 2;
            for (int i = 0; i <= last; i++)
            {
                Vector3 a = _points[i];
                Vector3 d = _points[i + 1] - a;
                float len2 = d.sqrMagnitude;
                float raw = len2 > 1e-12f ? Vector3.Dot(local - a, d) / len2 : 0f;
                float t = Mathf.Clamp01(raw);
                float dist = (local - (a + d * t)).sqrMagnitude;
                if (dist < best)
                {
                    best = dist;
                    along = _along[i] + Mathf.Sqrt(len2) * t;
                    beyondEnd = i == 0 && raw < 0f ? 0 : i == last && raw > 1f ? 1 : -1;
                }
            }
            offLine = Mathf.Sqrt(best);
        }

        void Pass()
        {
            if (_cfg != null) _plane.Boost(_cfg.boostSeconds, _cfg.boostSpeedMultiplier);
            ComicVfx.Play(ComicKind.RingPass, _plane.Position);
            IsAlive = false;
            AnyBoosted?.Invoke(this);
            Destroy(gameObject);
        }

        void HitWall()
        {
            ComicVfx.Play(ComicKind.Spark, _plane.Position, 0.06f);
            Sfx.Play(SoundKind.TubeBreak, transform.position);
            AnyWallHit?.Invoke(this);
            Break(ComicKind.Poof);
        }

        void Break(ComicKind vfx)
        {
            IsAlive = false;
            ComicVfx.Play(vfx, transform.position);
            Destroy(gameObject);
        }

        public static void DestroyAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }
    }
}

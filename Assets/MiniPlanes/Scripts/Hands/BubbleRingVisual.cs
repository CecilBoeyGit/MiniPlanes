using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The soap-bubble scoring portal: seeds small and fills radially, floats and wobbles while held,
    /// follows the shape of the fingers, pinches like a peanut during a merge and pulses when the plane
    /// passes through.
    /// </summary>
    public class BubbleRingVisual : MonoBehaviour
    {
        /// <summary>How many outline radii the shader takes. Must match _Contour in BubbleRing.shader.</summary>
        public const int ContourSamples = 16;

        static readonly int ContourId = Shader.PropertyToID("_Contour");
        static readonly int ContourOnId = Shader.PropertyToID("_ContourOn");
        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int PeanutId = Shader.PropertyToID("_Peanut");
        static readonly int WobbleId = Shader.PropertyToID("_Wobble");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        static Mesh _sharedMesh;

        [Tooltip("Height of the idle floating motion, in metres.")]
        public float floatAmplitude = 0.0022f;
        public float floatSpeed = 1.7f;
        [Tooltip("Resting wobble strength of the outline.")]
        public float idleWobble = 0.022f;
        [Tooltip("Extra wobble just after the ring forms or scores, fading out quickly.")]
        public float kickWobble = 0.07f;

        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        float _flashUntil;
        float _flashSeconds = 0.3f;
        float _kick;
        float _phase;

        public static BubbleRingVisual Create(string name, Material material)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = SharedMesh();
            var v = go.AddComponent<BubbleRingVisual>();
            v._renderer = go.AddComponent<MeshRenderer>();
            v._renderer.sharedMaterial = material;
            v._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            v._renderer.receiveShadows = false;
            v._renderer.enabled = false;
            v._block = new MaterialPropertyBlock();
            v._phase = Random.value * 10f;
            return v;
        }

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        public void Kick()
        {
            _kick = 1f;
        }

        public void Flash(float seconds)
        {
            _flashSeconds = Mathf.Max(0.05f, seconds);
            _flashUntil = Time.time + _flashSeconds;
            _kick = 1f;
        }

        /// <param name="axis">Direction the peanut stretches along, in the ring's plane.</param>
        /// <summary>The ring's in-plane axes. Sample i of the outline points along x * sin(a) + y * cos(a).</summary>
        public static void Frame(Vector3 normal, Vector3 axis, out Vector3 x, out Vector3 y)
        {
            x = axis - normal * Vector3.Dot(axis, normal);
            if (x.sqrMagnitude < 1e-6f) x = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right);
            x.Normalize();
            y = Vector3.Cross(normal, x);
        }

        /// <param name="fill">Radial fill from 0 to 1. Also grows the ring from its seed size.</param>
        /// <param name="contour">Outline radius in each sample direction, 1 being a plain circle. Null for a circle.</param>
        public void Show(Vector3 center, Vector3 normal, Vector3 axis, float radius, float fill, float peanut, float[] contour = null)
        {
            if (_renderer == null) return;
            _renderer.enabled = true;

            Frame(normal, axis, out Vector3 x, out Vector3 y);

            float t = Time.time + _phase;
            Vector3 drift = y * (Mathf.Sin(t * floatSpeed) * floatAmplitude)
                            + x * (Mathf.Sin(t * floatSpeed * 0.73f + 1.1f) * floatAmplitude * 0.6f)
                            + normal * (Mathf.Sin(t * floatSpeed * 1.21f) * floatAmplitude * 0.5f);
            transform.position = center + drift;
            transform.rotation = Quaternion.LookRotation(normal, y);

            // Grows from a tiny seed with a springy overshoot as the fill completes.
            float f = Mathf.Clamp01(fill);
            float grow = Mathf.Lerp(0.18f, 1f, 1f - (1f - f) * (1f - f));
            if (f > 0.75f && f < 1f) grow += Mathf.Sin((f - 0.75f) * 4f * Mathf.PI) * 0.05f;
            transform.localScale = Vector3.one * (radius * grow);

            _kick = Mathf.MoveTowards(_kick, 0f, Time.deltaTime * 2.2f);
            float flash = Time.time < _flashUntil ? (_flashUntil - Time.time) / _flashSeconds : 0f;

            _block.SetFloat(FillId, f);
            _block.SetFloat(PeanutId, peanut);
            _block.SetFloat(WobbleId, idleWobble + kickWobble * _kick);
            _block.SetFloat(FlashId, flash);
            _block.SetFloat(ContourOnId, contour != null ? 1f : 0f);
            if (contour != null) _block.SetFloatArray(ContourId, contour);
            _renderer.SetPropertyBlock(_block);
        }

        public void DestroySelf()
        {
            if (this != null) Destroy(gameObject);
        }

        static Mesh SharedMesh()
        {
            if (_sharedMesh == null) _sharedMesh = BuildMesh(0.085f, 56, 8);
            return _sharedMesh;
        }

        /// <summary>Unit-radius ring in the XY plane, plus a membrane disc. See the shader for the channel layout.</summary>
        static Mesh BuildMesh(float minor, int majorSegments, int minorSegments)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var tris = new List<int>();

            // Membrane first, so it draws behind the rim.
            for (int i = 0; i < majorSegments; i++)
            {
                float a0 = i / (float)majorSegments;
                float a1 = (i + 1) / (float)majorSegments;
                float mid = (a0 + a1) * 0.5f;
                int b = verts.Count;
                verts.Add(Vector3.zero);
                verts.Add(OnCircle(a0));
                verts.Add(OnCircle(a1));
                uvs.Add(new Vector2(mid, 0f));
                uvs.Add(new Vector2(a0, 1f));
                uvs.Add(new Vector2(a1, 1f));
                for (int k = 0; k < 3; k++)
                {
                    norms.Add(Vector3.forward);
                    colors.Add(Color.black);
                }
                tris.Add(b);
                tris.Add(b + 1);
                tris.Add(b + 2);
            }

            int start = verts.Count;
            for (int i = 0; i <= majorSegments; i++)
            {
                float a = i / (float)majorSegments;
                Vector3 c = OnCircle(a);
                for (int j = 0; j <= minorSegments; j++)
                {
                    float v = j / (float)minorSegments * Mathf.PI * 2f;
                    Vector3 n = c * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
                    verts.Add(c + n * minor);
                    norms.Add(n);
                    uvs.Add(new Vector2(a, 1f));
                    colors.Add(Color.white);
                }
            }
            int stride = minorSegments + 1;
            for (int i = 0; i < majorSegments; i++)
            {
                for (int j = 0; j < minorSegments; j++)
                {
                    int a = start + i * stride + j;
                    int b = start + (i + 1) * stride + j;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
                }
            }

            var mesh = new Mesh { name = "BubbleRing" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            // The shader pushes vertices outward, so leave generous bounds.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }

        // Angle 0 at the top, increasing clockwise when viewed from the -Z side.
        static Vector3 OnCircle(float a)
        {
            float rad = a * Mathf.PI * 2f;
            return new Vector3(Mathf.Sin(rad), Mathf.Cos(rad), 0f);
        }
    }
}

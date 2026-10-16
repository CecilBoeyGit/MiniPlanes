using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>Randomisable dimensions (metres) and colours for a tiny aeroplane.</summary>
    [System.Serializable]
    public struct PlaneShapeParams
    {
        public float length;
        public float fuselageRadius;
        public float wingSpan;
        public float rootChord;
        public float tipChordRatio;
        public float sweep;
        public float dihedralDeg;
        public float wingPosition;      // -0.5..0.5 along the fuselage, 0 = centre
        public float stabilizerSpan;
        public float finHeight;
        public bool canopy;
        public Color bodyColor;
        public Color wingColor;

        public static PlaneShapeParams Friendly(System.Random rng)
        {
            float hue = (float)rng.NextDouble();
            // Deliberately tiny: the whole plane must fit through a C-sign hole (roughly 4-6 cm).
            return new PlaneShapeParams
            {
                length = Range(rng, 0.032f, 0.042f),
                fuselageRadius = Range(rng, 0.0036f, 0.0046f),
                wingSpan = Range(rng, 0.036f, 0.046f),
                rootChord = Range(rng, 0.010f, 0.013f),
                tipChordRatio = Range(rng, 0.55f, 0.85f),
                sweep = Range(rng, 0f, 0.003f),
                dihedralDeg = Range(rng, 2f, 8f),
                wingPosition = Range(rng, -0.05f, 0.15f),
                stabilizerSpan = Range(rng, 0.015f, 0.020f),
                finHeight = Range(rng, 0.006f, 0.009f),
                canopy = rng.NextDouble() < 0.7,
                bodyColor = Color.HSVToRGB(hue, 0.55f, 0.95f),
                wingColor = Color.HSVToRGB((hue + 0.5f) % 1f, 0.35f, 0.9f),
            };
        }

        public static PlaneShapeParams Enemy(System.Random rng)
        {
            return new PlaneShapeParams
            {
                length = Range(rng, 0.028f, 0.036f),
                fuselageRadius = Range(rng, 0.0030f, 0.0040f),
                wingSpan = Range(rng, 0.030f, 0.038f),
                rootChord = Range(rng, 0.011f, 0.014f),
                tipChordRatio = Range(rng, 0.35f, 0.55f),
                sweep = Range(rng, 0.005f, 0.008f),
                dihedralDeg = Range(rng, -6f, 0f),
                wingPosition = Range(rng, -0.15f, 0f),
                stabilizerSpan = Range(rng, 0.012f, 0.016f),
                finHeight = Range(rng, 0.007f, 0.010f),
                canopy = true,
                bodyColor = new Color(0.22f, 0.05f, 0.06f),
                wingColor = new Color(0.75f, 0.12f, 0.12f),
            };
        }

        static float Range(System.Random rng, float a, float b)
        {
            return Mathf.Lerp(a, b, (float)rng.NextDouble());
        }
    }

    /// <summary>
    /// Builds a flat-shaded two-submesh aeroplane mesh (0 = fuselage, 1 = wings, tail, canopy).
    /// Local axes: +Z nose, +Y up, +X right wing.
    /// </summary>
    public static class ProceduralPlaneMesh
    {
        class Builder
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<int>[] Tris = { new List<int>(), new List<int>() };

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outwardHint)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, outwardHint) < 0f)
                {
                    var tmp = b; b = c; c = tmp;
                }
                int i = Verts.Count;
                Verts.Add(a); Verts.Add(b); Verts.Add(c);
                Tris[sub].Add(i); Tris[sub].Add(i + 1); Tris[sub].Add(i + 2);
            }

            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outwardHint)
            {
                Tri(sub, a, b, c, outwardHint);
                Tri(sub, a, c, d, outwardHint);
            }

            /// <summary>c8: four bottom corners in loop order, then the four top corners in the same order.</summary>
            public void Hex(int sub, Vector3[] c)
            {
                Vector3 centre = Vector3.zero;
                for (int i = 0; i < 8; i++) centre += c[i];
                centre /= 8f;

                QuadOut(sub, c[0], c[1], c[2], c[3], centre);
                QuadOut(sub, c[4], c[5], c[6], c[7], centre);
                QuadOut(sub, c[0], c[1], c[5], c[4], centre);
                QuadOut(sub, c[1], c[2], c[6], c[5], centre);
                QuadOut(sub, c[2], c[3], c[7], c[6], centre);
                QuadOut(sub, c[3], c[0], c[4], c[7], centre);
            }

            void QuadOut(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 centre)
            {
                Vector3 faceCentre = (a + b + c + d) * 0.25f;
                Quad(sub, a, b, c, d, faceCentre - centre);
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh { name = "ProceduralPlane" };
                mesh.SetVertices(Verts);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(Tris[0], 0);
                mesh.SetTriangles(Tris[1], 1);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        public static Mesh Build(PlaneShapeParams p)
        {
            var b = new Builder();
            float L = p.length;
            float R = p.fuselageRadius;

            // ---- Fuselage: tapered tube made of rings ----
            float[] zf = { -0.5f, -0.28f, -0.02f, 0.28f, 0.5f };
            float[] rf = { 0.18f, 0.68f, 1f, 0.82f, 0.14f };
            float[] yf = { 0.25f, 0.08f, 0f, 0f, -0.05f };   // slight upward tail
            const int segs = 10;
            var rings = new Vector3[zf.Length][];
            for (int i = 0; i < zf.Length; i++)
            {
                rings[i] = new Vector3[segs];
                for (int k = 0; k < segs; k++)
                {
                    float ang = k / (float)segs * Mathf.PI * 2f;
                    rings[i][k] = new Vector3(Mathf.Cos(ang) * rf[i] * R, Mathf.Sin(ang) * rf[i] * R * 1.08f + yf[i] * R, zf[i] * L);
                }
            }
            for (int i = 0; i < rings.Length - 1; i++)
            {
                Vector3 axisA = new Vector3(0f, yf[i] * R, zf[i] * L);
                Vector3 axisB = new Vector3(0f, yf[i + 1] * R, zf[i + 1] * L);
                for (int k = 0; k < segs; k++)
                {
                    int k1 = (k + 1) % segs;
                    Vector3 a = rings[i][k], c = rings[i][k1], d = rings[i + 1][k1], e = rings[i + 1][k];
                    Vector3 faceCentre = (a + c + d + e) * 0.25f;
                    Vector3 axisMid = (axisA + axisB) * 0.5f;
                    b.Quad(0, a, c, d, e, faceCentre - axisMid);
                }
            }
            Vector3 tailCentre = new Vector3(0f, yf[0] * R, zf[0] * L);
            Vector3 noseCentre = new Vector3(0f, yf[zf.Length - 1] * R, zf[zf.Length - 1] * L + R * 0.3f);
            for (int k = 0; k < segs; k++)
            {
                int k1 = (k + 1) % segs;
                b.Tri(0, tailCentre, rings[0][k], rings[0][k1], Vector3.back);
                b.Tri(0, noseCentre, rings[rings.Length - 1][k], rings[rings.Length - 1][k1], Vector3.forward);
            }

            // ---- Main wing ----
            float wingZ = p.wingPosition * L;
            float plate = Mathf.Max(0.0008f, R * 0.32f);
            AddWing(b, 1, wingZ, p.rootChord, p.rootChord * p.tipChordRatio, p.wingSpan * 0.5f, p.sweep, p.dihedralDeg, plate, R * 0.55f, -R * 0.25f);

            // ---- Horizontal stabiliser ----
            float stabChord = p.rootChord * 0.6f;
            AddWing(b, 1, -0.40f * L, stabChord, stabChord * 0.6f, p.stabilizerSpan * 0.5f, p.sweep * 0.6f, 0f, plate * 0.85f, R * 0.25f, R * 0.25f);

            // ---- Vertical fin ----
            {
                float zRootLE = -0.26f * L, zRootTE = -0.5f * L;
                float zTipLE = -0.38f * L, zTipTE = -0.5f * L;
                float yBase = R * 0.3f, yTop = yBase + p.finHeight;
                float th = plate * 0.5f;
                var c = new Vector3[8];
                c[0] = new Vector3(-th, yBase, zRootLE); c[1] = new Vector3(-th, yBase, zRootTE);
                c[2] = new Vector3(-th, yTop, zTipTE); c[3] = new Vector3(-th, yTop, zTipLE);
                for (int i = 0; i < 4; i++) c[i + 4] = c[i] + Vector3.right * (2f * th);
                b.Hex(1, c);
            }

            // ---- Canopy ----
            if (p.canopy)
            {
                float z0 = wingZ + p.rootChord * 0.2f, z1 = z0 + L * 0.2f;
                float x = R * 0.5f, y0 = R * 0.75f, y1 = R * 1.45f;
                var c = new Vector3[8];
                c[0] = new Vector3(-x, y0, z1); c[1] = new Vector3(x, y0, z1);
                c[2] = new Vector3(x, y0, z0); c[3] = new Vector3(-x, y0, z0);
                c[4] = new Vector3(-x * 0.6f, y1, z1 - L * 0.06f); c[5] = new Vector3(x * 0.6f, y1, z1 - L * 0.06f);
                c[6] = new Vector3(x * 0.6f, y1, z0 + L * 0.03f); c[7] = new Vector3(-x * 0.6f, y1, z0 + L * 0.03f);
                b.Hex(1, c);
            }

            return b.ToMesh();
        }

        static void AddWing(Builder b, int sub, float zCentre, float rootChord, float tipChord, float halfSpan,
            float sweep, float dihedralDeg, float thickness, float rootHalfWidth, float yRoot)
        {
            float rise = Mathf.Tan(dihedralDeg * Mathf.Deg2Rad) * (halfSpan - rootHalfWidth);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 rootLE = new Vector3(s * rootHalfWidth, yRoot, zCentre + rootChord * 0.5f);
                Vector3 rootTE = new Vector3(s * rootHalfWidth, yRoot, zCentre - rootChord * 0.5f);
                Vector3 tipLE = new Vector3(s * halfSpan, yRoot + rise, zCentre + tipChord * 0.5f - sweep);
                Vector3 tipTE = new Vector3(s * halfSpan, yRoot + rise, zCentre - tipChord * 0.5f - sweep);
                Vector3 up = Vector3.up * (thickness * 0.5f);
                var c = new Vector3[8];
                c[0] = rootLE - up; c[1] = tipLE - up; c[2] = tipTE - up; c[3] = rootTE - up;
                c[4] = rootLE + up; c[5] = tipLE + up; c[6] = tipTE + up; c[7] = rootTE + up;
                b.Hex(sub, c);
            }
        }
    }
}

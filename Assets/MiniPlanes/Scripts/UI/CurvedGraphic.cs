using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// Wraps one uGUI Graphic around the cylinder of the <see cref="CurvedCanvas"/> above it. Long pieces
    /// are cut into thin slices first so they bend smoothly. Added automatically; you don't need to add it.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CurvedGraphic : BaseMeshEffect
    {
        static readonly List<UIVertex> In = new List<UIVertex>(256);
        static readonly List<UIVertex> Out = new List<UIVertex>(1024);

        CurvedCanvas _canvas;
        Matrix4x4 _toCanvas, _fromCanvas;
        float _segment;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            if (_canvas == null) _canvas = GetComponentInParent<CurvedCanvas>(true);
            if (_canvas == null || !_canvas.isActiveAndEnabled || !_canvas.IsCurved) return;

            _toCanvas = _canvas.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            _fromCanvas = _toCanvas.inverse;
            _segment = _canvas.CanvasRadius * Mathf.Max(0.2f, _canvas.maxSegmentDeg) * Mathf.Deg2Rad;

            In.Clear();
            Out.Clear();
            vh.GetUIVertexStream(In);
            for (int i = 0; i + 2 < In.Count; i += 3) Split(In[i], In[i + 1], In[i + 2], 0);

            for (int i = 0; i < Out.Count; i++)
            {
                var v = Out[i];
                Vector3 p = _canvas.Bend(_toCanvas.MultiplyPoint3x4(v.position));
                v.position = _fromCanvas.MultiplyPoint3x4(p);
                Out[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(Out);
        }

        float X(UIVertex v)
        {
            return _toCanvas.MultiplyPoint3x4(v.position).x;
        }

        void Split(UIVertex a, UIVertex b, UIVertex c, int depth)
        {
            float xa = X(a), xb = X(b), xc = X(c);
            float ab = Mathf.Abs(xa - xb), bc = Mathf.Abs(xb - xc), ca = Mathf.Abs(xc - xa);
            float widest = Mathf.Max(ab, Mathf.Max(bc, ca));
            if (widest <= _segment || depth > 8)
            {
                Out.Add(a);
                Out.Add(b);
                Out.Add(c);
                return;
            }
            // Halve the widest edge and recurse on the two halves, keeping the winding.
            if (widest == ab)
            {
                var m = Mid(a, b);
                Split(a, m, c, depth + 1);
                Split(m, b, c, depth + 1);
            }
            else if (widest == bc)
            {
                var m = Mid(b, c);
                Split(a, b, m, depth + 1);
                Split(a, m, c, depth + 1);
            }
            else
            {
                var m = Mid(c, a);
                Split(a, b, m, depth + 1);
                Split(m, b, c, depth + 1);
            }
        }

        static UIVertex Mid(UIVertex a, UIVertex b)
        {
            var m = new UIVertex
            {
                position = (a.position + b.position) * 0.5f,
                normal = (a.normal + b.normal) * 0.5f,
                tangent = (a.tangent + b.tangent) * 0.5f,
                color = Color32.Lerp(a.color, b.color, 0.5f),
                uv0 = (a.uv0 + b.uv0) * 0.5f,
                uv1 = (a.uv1 + b.uv1) * 0.5f,
                uv2 = (a.uv2 + b.uv2) * 0.5f,
                uv3 = (a.uv3 + b.uv3) * 0.5f
            };
            return m;
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            _canvas = null;
        }
    }
}

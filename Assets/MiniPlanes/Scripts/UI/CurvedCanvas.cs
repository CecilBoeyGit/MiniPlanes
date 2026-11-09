using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// Bends a world-space uGUI canvas onto a cylinder around the player, like a cockpit display. Lay the
    /// canvas out flat as usual: every Graphic under it gets a <see cref="CurvedGraphic"/> that wraps its
    /// mesh around a vertical axis <see cref="radius"/> metres behind the canvas, which is where the head
    /// should be. Images and text you add to the prefab curve automatically.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class CurvedCanvas : MonoBehaviour
    {
        [Tooltip("Distance from the axis of the curve to the canvas, in metres. Match it to the panel's distance from the head. 0 keeps it flat.")]
        public float radius = 0.5f;
        [Tooltip("Largest angle one straight piece of a curved element may span, in degrees. Smaller is smoother.")]
        public float maxSegmentDeg = 2.5f;

        public RectTransform Rect => (RectTransform)transform;
        public bool IsCurved => radius > 0.001f;
        float Scale => Mathf.Max(1e-6f, Mathf.Abs(transform.lossyScale.x));
        /// <summary>The radius in this canvas's own units.</summary>
        public float CanvasRadius => radius / Scale;

        void OnEnable()
        {
            EnsureEffects();
            MarkDirty();
        }

        void OnTransformChildrenChanged()
        {
            EnsureEffects();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (isActiveAndEnabled) MarkDirty();
        }
#endif

        /// <summary>Gives every Graphic under the canvas the bending effect.</summary>
        public void EnsureEffects()
        {
            foreach (var g in GetComponentsInChildren<Graphic>(true))
                if (g.GetComponent<CurvedGraphic>() == null) g.gameObject.AddComponent<CurvedGraphic>();
        }

        /// <summary>Re-bends everything. Needed after laying out again by hand.</summary>
        public void MarkDirty()
        {
            MarkDirty(transform);
        }

        /// <summary>Re-bends one part, for example the rows of a list that has scrolled.</summary>
        public static void MarkDirty(Transform part)
        {
            foreach (var g in part.GetComponentsInChildren<Graphic>(true)) g.SetVerticesDirty();
        }

        /// <summary>Bends a point given in this canvas's local space.</summary>
        public Vector3 Bend(Vector3 local)
        {
            if (!IsCurved) return local;
            float r = CanvasRadius;
            float a = local.x / r;
            float rr = r + local.z;
            return new Vector3(Mathf.Sin(a) * rr, local.y, Mathf.Cos(a) * rr - r);
        }

        /// <summary>
        /// Where a world point is relative to the flat layout: x and y in canvas units, matching
        /// RectTransform positions, and z its distance behind the curved surface in metres (negative in
        /// front of it, on the player's side).
        /// </summary>
        public Vector3 ToFlat(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world);
            if (!IsCurved) return new Vector3(p.x, p.y, p.z * Scale);
            float r = CanvasRadius;
            float dz = p.z + r;
            float angle = Mathf.Atan2(p.x, dz);
            float distance = Mathf.Sqrt(p.x * p.x + dz * dz);
            return new Vector3(angle * r, p.y, (distance - r) * Scale);
        }

        /// <summary>The world position of a point on the flat layout, after bending.</summary>
        public Vector3 FlatToWorld(Vector2 flat)
        {
            return transform.TransformPoint(Bend(new Vector3(flat.x, flat.y, 0f)));
        }

        /// <summary>Where the centre of a RectTransform under this canvas ends up in the world.</summary>
        public Vector3 CentreOf(RectTransform rect)
        {
            Vector3 local = transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            return FlatToWorld(new Vector2(local.x, local.y));
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// A signal jammer standing on a floating island. While any tower stands, the warning that sounds when an
    /// enemy plane arrives is silenced. It has no strength of its own: it goes when its island shatters, so
    /// the player smacks the island (or the tower) to clear the jamming. A blocked-signal icon floats above it.
    /// </summary>
    public class JammingTower : MonoBehaviour
    {
        public static readonly List<JammingTower> Active = new List<JammingTower>();

        /// <summary>A tower is up somewhere, so arrival warnings are jammed.</summary>
        public static bool AnyActive
        {
            get
            {
                foreach (var t in Active) if (t != null) return true;
                return false;
            }
        }

        /// <summary>A tower was destroyed along with its island.</summary>
        public static event Action<JammingTower> AnyDestroyed;

        [Tooltip("Tower height as a share of its island's width.")]
        public float heightPerIsland = 0.55f;
        [Tooltip("Height of the icon above the tower's top, as a share of the tower's height.")]
        public float iconLift = 0.35f;
        [Tooltip("Size of the blocked-signal icon, relative to the other floating icons.")]
        public float iconScale = 1.7f;
        [Tooltip("How fast the dish sweeps round, in degrees per second.")]
        public float sweepDegPerSecond = 40f;

        public float Height { get; private set; }
        public Vector3 Top => transform.position + transform.up * Height;
        public FloatingIsland Island { get; private set; }

        Transform _model;
        Transform _icon;

        public void Setup(FloatingIsland island, GameArt art, float islandSize)
        {
            Island = island;
            Height = islandSize * heightPerIsland;
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
            if (art != null && art.jammingTower != null)
            {
                var m = ModelUtil.Attach(_model, art.jammingTower, art.JammingTowerMaterial, Height, ModelUtil.FitAxis.Height);
                m.transform.localPosition += Vector3.up * Height * 0.5f;
            }
            else
            {
                BuildStandIn(art != null ? art.planeMatte : null);
            }
            if (art != null && art.jamIcon != null)
            {
                _icon = Instantiate(art.jamIcon, transform, false).transform;
                _icon.name = art.jamIcon.name;
                _icon.localScale = Vector3.one * iconScale * Mathf.Clamp(islandSize / 0.18f, 0.8f, 1.3f);
            }
        }

        /// <summary>A tower from simple shapes, used until a model is assigned in GameArt.</summary>
        void BuildStandIn(Material mat)
        {
            float h = Height;
            var coral = new Color(0.9f, 0.42f, 0.32f);
            var cream = new Color(0.97f, 0.9f, 0.78f);
            var slate = new Color(0.27f, 0.27f, 0.32f);
            ModelUtil.Primitive(PrimitiveType.Cylinder, _model, Vector3.up * h * 0.03f, new Vector3(h * 0.62f, h * 0.03f, h * 0.62f), Quaternion.identity, mat, slate);
            ModelUtil.Primitive(PrimitiveType.Cylinder, _model, Vector3.up * h * 0.38f, new Vector3(h * 0.42f, h * 0.33f, h * 0.42f), Quaternion.identity, mat, coral);
            ModelUtil.Primitive(PrimitiveType.Cylinder, _model, Vector3.up * h * 0.36f, new Vector3(h * 0.44f, h * 0.06f, h * 0.44f), Quaternion.identity, mat, cream);
            ModelUtil.Primitive(PrimitiveType.Sphere, _model, Vector3.up * h * 0.86f, new Vector3(h * 0.5f, h * 0.14f, h * 0.5f), Quaternion.Euler(-25f, 0f, 0f), mat, cream);
            ModelUtil.Primitive(PrimitiveType.Sphere, _model, Vector3.up * h * 0.93f, Vector3.one * h * 0.08f, Quaternion.identity, mat, new Color(1f, 0.66f, 0.18f));
        }

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        void OnDestroy()
        {
            AnyDestroyed?.Invoke(this);
        }

        void Update()
        {
            if (_model != null) _model.Rotate(0f, sweepDegPerSecond * Time.deltaTime, 0f, Space.Self);
            if (_icon != null) _icon.position = Top + Vector3.up * Height * iconLift;
        }

        /// <summary>Whether a point lies within the tower's rough shape, grown by a margin.</summary>
        public bool Contains(Vector3 world, float margin)
        {
            Vector3 local = world - transform.position;
            float up = Vector3.Dot(local, transform.up);
            if (up < -margin || up > Height + margin) return false;
            Vector3 flat = local - transform.up * up;
            float radius = Height * Mathf.Lerp(0.32f, 0.26f, Mathf.Clamp01(up / Mathf.Max(0.001f, Height)));
            return flat.magnitude < radius + margin;
        }

        public static void DestroyAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }
    }
}

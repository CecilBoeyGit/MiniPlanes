using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>A straight-flying enemy bullet. Crashes the mini plane on contact; blocked by hands.</summary>
    public class Projectile : MonoBehaviour
    {
        public static readonly List<Projectile> Active = new List<Projectile>();

        public float radius = 0.006f;
        public float lifetime = 3f;

        public Vector3 Position => transform.position;

        Vector3 _velocity;
        float _age;
        MiniPlane _target;

        public static Projectile Create(Material material, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.localScale = Vector3.one * (radius * 2f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var p = go.AddComponent<Projectile>();
            p.radius = radius;
            return p;
        }

        public void Fire(Vector3 position, Vector3 velocity, MiniPlane target)
        {
            transform.position = position;
            _velocity = velocity;
            _target = target;
            _age = 0f;
        }

        void OnEnable()
        {
            Active.Add(this);
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            transform.position += _velocity * dt;
            _age += dt;

            if (_target != null && _target.IsFlying &&
                Vector3.Distance(transform.position, _target.Position) < radius + _target.radius)
            {
                _target.Crash("shot down");
                Destroy(gameObject);
                return;
            }

            if (_age > lifetime) Destroy(gameObject);
        }

        public void Block()
        {
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

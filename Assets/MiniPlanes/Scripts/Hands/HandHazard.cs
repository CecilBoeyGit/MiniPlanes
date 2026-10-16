using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Treats the whole hand as rock: any bone capsule touching the mini plane crashes it, and
    /// enemy projectiles that hit the hand are blocked. Inside an active ring's zone the ring's
    /// own crossing test decides instead.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class HandHazard : MonoBehaviour
    {
        public HandSource hand;
        public HandRing ring;
        [Tooltip("Ignore collisions while tracking confidence is low, to avoid phantom crashes.")]
        public bool requireHighConfidence = false;

        public event Action<HandHazard, MiniPlane> PlaneHit;
        public event Action<HandHazard, Projectile> ProjectileBlocked;

        MiniPlane _plane;
        readonly List<BoneSegment> _segments = new List<BoneSegment>(32);

        public void Track(MiniPlane plane)
        {
            _plane = plane;
        }

        void LateUpdate()
        {
            if (hand == null || !hand.IsTracked) return;
            if (requireHighConfidence && !hand.IsHighConfidence) return;
            if (!hand.GetBoneSegments(_segments)) return;

            if (_plane != null && _plane.IsFlying)
            {
                bool ringZone = ring != null && ring.IsActive && ring.IsPointInRingZone(_plane.Position);
                if (!ringZone)
                {
                    float d = HandSource.MinSurfaceDistance(_segments, _plane.Position);
                    if (d < _plane.radius) PlaneHit?.Invoke(this, _plane);
                }
            }

            var projectiles = Projectile.Active;
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                if (p == null) continue;
                float d = HandSource.MinSurfaceDistance(_segments, p.Position);
                if (d < p.radius)
                {
                    ProjectileBlocked?.Invoke(this, p);
                    p.Block();
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Treats the whole hand as rock: any bone capsule touching the plane is a hit, bullets are blocked,
    /// and red bomb balloons go off on contact. Inside an active ring's zone the ring's own crossing
    /// test decides instead.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class HandHazard : MonoBehaviour
    {
        public HandSource hand;
        public HandRing ring;
        public RingMerger merger;
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
            if (GameManager.IsPaused) return;
            if (hand == null || !hand.IsTracked) return;
            if (requireHighConfidence && !hand.IsHighConfidence) return;
            if (!hand.GetBoneSegments(_segments)) return;

            if (_plane != null && _plane.IsFlying)
            {
                bool inRingZone = (ring != null && ring.IsActive && !ring.MergedHidden && ring.IsPointInRingZone(_plane.Position))
                                  || (merger != null && merger.IsPointInRingZone(_plane.Position));
                if (!inRingZone)
                {
                    float d = HandSource.MinSurfaceDistance(_segments, _plane.Position);
                    if (d < _plane.Radius) PlaneHit?.Invoke(this, _plane);
                }
            }

            var projectiles = Projectile.Active;
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                if (p == null) continue;
                if (HandSource.MinSurfaceDistance(_segments, p.Position) < p.radius)
                {
                    ProjectileBlocked?.Invoke(this, p);
                    p.Block();
                }
            }

            var bombs = BombBalloon.Active;
            for (int i = bombs.Count - 1; i >= 0; i--)
            {
                var b = bombs[i];
                if (b == null) continue;
                if (HandSource.MinSurfaceDistance(_segments, b.Position) < b.radius) b.TouchedByHand();
            }
        }
    }
}

using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Level 2 enemy. Flies to a point just off the plane's upcoming path, releases a red bomb balloon,
    /// then turns away and flies off out of the room. Flicking it scores extra. Destroyed within explosion
    /// range of its own balloon, it sets the balloon off.
    /// </summary>
    public class BomberPlane : EnemyPlane
    {
        public enum Phase { ToDrop, Leaving }

        [Tooltip("How close to the drop point the bomber must get before releasing the balloon, in metres.")]
        public float dropReach = 0.03f;
        [Tooltip("How far away from the player the bomber heads once it has dropped its balloon, in metres.")]
        public float exitDistance = 3.5f;

        public Phase CurrentPhase { get; private set; } = Phase.ToDrop;
        public BombBalloon Balloon { get; private set; }
        public Vector3 DropPoint { get; private set; }

        /// <summary>Extra tuning the balloon needs that is not in the level.</summary>
        public HandSource[] Hands { get; set; }

        Vector3 _exitPoint;

        protected override void BuildModel(System.Random rng)
        {
            wingspan = 0.055f;
            radius = 0.016f;
            turnRateDeg = 100f;
            if (Art != null && Art.enemyBomber != null)
                ModelUtil.Attach(transform, Art.enemyBomber, Art.BomberMaterial, wingspan, ModelUtil.FitAxis.Width);
            else
                base.BuildModel(rng);
        }

        protected override float StartSpeed() { return Cfg.bomberSpeed; }

        protected override void OnSetup(System.Random rng)
        {
            DropPoint = ChooseDropPoint(rng);
        }

        Vector3 ChooseDropPoint(System.Random rng)
        {
            if (Target == null || Target.Path == null) return transform.position;

            var path = Target.Path;
            float approach = Vector3.Distance(transform.position, Target.Position) / Mathf.Max(0.05f, Speed);
            float lead = Target.Speed * (approach + Cfg.bombLeadSeconds);
            // Keep off the entry and exit legs, which are out of the player's reach.
            float along = Mathf.Clamp(Target.Distance + lead, 0.6f, Mathf.Max(0.6f, path.Length - 1f));

            Vector3 onPath = path.PositionAt(along);
            Vector3 tangent = path.TangentAt(along);
            Vector3 side = Vector3.Cross(tangent, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
            return onPath + side * (sign * Cfg.bombPathOffset);
        }

        protected override void Tick(float dt)
        {
            if (CurrentPhase == Phase.ToDrop)
            {
                Vector3 to = DropPoint - transform.position;
                if (to.magnitude < dropReach + Speed * dt)
                {
                    ReleaseBalloon();
                    return;
                }
                Steer(to.normalized, dt);
                return;
            }

            // Leaving: head out and up, away from the player, until it is far enough to despawn.
            Vector3 away = _exitPoint - transform.position;
            Steer(away.sqrMagnitude > 1e-6f ? away.normalized : Forward, dt);
        }

        /// <summary>Drops the balloon where the bomber is and turns away.</summary>
        public void ReleaseBalloon()
        {
            if (CurrentPhase != Phase.ToDrop) return;
            var go = new GameObject("BombBalloon");
            go.transform.SetParent(transform.parent, false);
            Balloon = go.AddComponent<BombBalloon>();
            Balloon.Setup(Cfg, Target, Hands, Art, transform.position);
            CurrentPhase = Phase.Leaving;
            Speed = Cfg.bomberSpeed * Mathf.Max(1f, Cfg.bomberExitMultiplier);
            turnRateDeg = 120f;

            Vector3 centre = Head != null ? Head.position : transform.position - Forward;
            Vector3 outward = transform.position - centre;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Forward;
            _exitPoint = centre + outward.normalized * exitDistance + Vector3.up * 0.5f;
        }

        protected override void OnFlicked()
        {
            ChainBalloon();
        }

        void ChainBalloon()
        {
            if (Balloon != null && Balloon.IsAlive &&
                Vector3.Distance(transform.position, Balloon.Position) < Cfg.explosionRange)
            {
                Balloon.Detonate();
            }
        }
    }
}

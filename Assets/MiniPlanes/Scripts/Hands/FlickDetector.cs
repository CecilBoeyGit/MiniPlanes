using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Detects a fast hand flick and knocks away any enemy plane the hand is touching.
    /// Velocity is taken from the palm and the index tip, whichever is faster.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class FlickDetector : MonoBehaviour
    {
        public HandSource hand;
        [Tooltip("Hand speed that counts as a flick, in metres per second.")]
        public float flickSpeed = 1.6f;
        [Tooltip("Extra reach beyond the bone capsules, in metres, so a near miss still connects.")]
        public float reach = 0.035f;
        public float cooldown = 0.25f;
        [Tooltip("Velocity smoothing time constant, in seconds. Keep small: flicks are brief.")]
        public float smoothing = 0.025f;

        public event Action<FlickDetector, EnemyPlane> Flicked;

        public float CurrentSpeed { get; private set; }

        Vector3 _prevPalm, _prevIndex;
        Vector3 _velPalm, _velIndex;
        bool _hasPrev;
        float _lastFlick = -10f;
        readonly List<BoneSegment> _segments = new List<BoneSegment>(32);

        void LateUpdate()
        {
            if (hand == null || !hand.IsTracked ||
                !hand.TryGetJoint(HandJoint.Palm, out var palm) ||
                !hand.TryGetJoint(HandJoint.IndexTip, out var index))
            {
                _hasPrev = false;
                CurrentSpeed = 0f;
                return;
            }

            float dt = Time.deltaTime;
            if (_hasPrev && dt > 0f)
            {
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(smoothing, 1e-3f));
                _velPalm = Vector3.Lerp(_velPalm, (palm - _prevPalm) / dt, k);
                _velIndex = Vector3.Lerp(_velIndex, (index - _prevIndex) / dt, k);
            }
            _prevPalm = palm;
            _prevIndex = index;
            _hasPrev = true;

            Vector3 vel = _velIndex.sqrMagnitude > _velPalm.sqrMagnitude ? _velIndex : _velPalm;
            CurrentSpeed = vel.magnitude;
            if (CurrentSpeed < flickSpeed || Time.time - _lastFlick < cooldown) return;
            if (EnemyPlane.Active.Count == 0) return;
            if (!hand.GetBoneSegments(_segments)) return;

            Vector3 dir = vel / CurrentSpeed;
            var enemies = EnemyPlane.Active;
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                if (e == null || !e.IsAlive) continue;
                float d = HandSource.MinSurfaceDistance(_segments, e.Position);
                if (d < e.radius + reach)
                {
                    e.Flick(dir * CurrentSpeed);
                    _lastFlick = Time.time;
                    Flicked?.Invoke(this, e);
                }
            }
        }
    }
}

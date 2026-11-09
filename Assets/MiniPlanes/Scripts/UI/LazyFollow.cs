using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Keeps a panel in front of the player without sticking to their face: it stays put while the head
    /// turns a little, then glides round smoothly once the head turns past a dead zone. Put it on the
    /// panel's root and place the panel itself as a child, at its distance in front (+z) of this object.
    /// </summary>
    public class LazyFollow : MonoBehaviour
    {
        public Transform head;
        [Tooltip("The panel only starts to turn when the head has turned further than this from it, in degrees.")]
        public float deadzoneDeg = 14f;
        [Tooltip("How quickly the panel catches up once it moves. Higher is snappier.")]
        public float followSpeed = 2.2f;
        [Range(0f, 1f), Tooltip("How much of the head's up and down tilt the panel follows.")]
        public float pitchFollow = 0.45f;
        [Tooltip("How quickly the panel's centre follows the head's position.")]
        public float positionSpeed = 8f;
        [Tooltip("Keep following while the game is paused, on real time.")]
        public bool unscaledTime;

        /// <summary>Stops the panel moving, for example while a finger is pressing it.</summary>
        public bool Hold { get; set; }

        float _yaw, _pitch;
        bool _placed;

        /// <summary>Puts the panel straight in front of the head at once.</summary>
        public void Snap()
        {
            if (head == null) return;
            Vector3 e = head.eulerAngles;
            _yaw = e.y;
            _pitch = Mathf.DeltaAngle(0f, e.x) * pitchFollow;
            transform.SetPositionAndRotation(head.position, Quaternion.Euler(_pitch, _yaw, 0f));
            _placed = true;
        }

        void OnEnable()
        {
            _placed = false;
        }

        void LateUpdate()
        {
            if (head == null) return;
            if (!_placed)
            {
                Snap();
                return;
            }
            if (Hold) return;

            float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            dt = Mathf.Min(dt, 0.05f);
            Vector3 e = head.eulerAngles;
            float k = 1f - Mathf.Exp(-followSpeed * dt);
            float delta = Mathf.DeltaAngle(_yaw, e.y);
            float beyond = Mathf.Abs(delta) - deadzoneDeg;
            if (beyond > 0f) _yaw += Mathf.Sign(delta) * beyond * k;
            _pitch = Mathf.Lerp(_pitch, Mathf.DeltaAngle(0f, e.x) * pitchFollow, k);

            transform.position = Vector3.Lerp(transform.position, head.position, 1f - Mathf.Exp(-positionSpeed * dt));
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }
}

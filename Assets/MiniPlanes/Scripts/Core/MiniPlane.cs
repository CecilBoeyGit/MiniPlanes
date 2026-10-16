using System;
using System.Collections;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The player's little aeroplane. Follows a CatmullRomPath at constant speed, banks into turns,
    /// and tumbles to the ground when it crashes.
    /// </summary>
    public class MiniPlane : MonoBehaviour
    {
        public enum State { Idle, Flying, Crashed, Finished }

        [Tooltip("Collision radius in metres. Set automatically from the wingspan when the plane is built.")]
        public float radius = 0.01f;
        [Tooltip("Collision radius as a fraction of the wingspan, so the visible size matches what collides.")]
        public float collisionRadiusPerSpan = 0.25f;
        public float maxBankDeg = 60f;
        public float bankSmoothing = 8f;
        public float crashDuration = 1.4f;

        public State CurrentState { get; private set; } = State.Idle;
        public bool IsFlying => CurrentState == State.Flying;
        public Vector3 Position => transform.position;
        public Vector3 Velocity { get; private set; }
        public float Speed { get; private set; }
        public float Distance { get; private set; }
        public CatmullRomPath Path { get; private set; }
        public float Progress => Path == null || Path.Length <= 0f ? 0f : Mathf.Clamp01(Distance / Path.Length);

        public event Action<MiniPlane> Finished;
        public event Action<MiniPlane, string> Crashed;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MeshFilter _filter;
        MeshRenderer _renderer;
        ParticleSystem _smoke;
        Vector3 _prevTangent;
        float _bank;
        Coroutine _crashRoutine;

        public void Build(PlaneShapeParams shape, Material bodyMaterial, Material wingMaterial)
        {
            _filter = GetComponent<MeshFilter>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            if (_filter.sharedMesh != null) Destroy(_filter.sharedMesh);
            _filter.sharedMesh = ProceduralPlaneMesh.Build(shape);
            radius = Mathf.Clamp(shape.wingSpan * collisionRadiusPerSpan, 0.004f, 0.02f);
            _renderer.sharedMaterials = new[] { bodyMaterial, wingMaterial };

            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, shape.bodyColor);
            _renderer.SetPropertyBlock(block, 0);
            block.SetColor(BaseColorId, shape.wingColor);
            _renderer.SetPropertyBlock(block, 1);

            _smoke = GetComponentInChildren<ParticleSystem>(true);
            _renderer.enabled = false;
        }

        public void Launch(CatmullRomPath path, float speed)
        {
            if (_crashRoutine != null)
            {
                StopCoroutine(_crashRoutine);
                _crashRoutine = null;
            }
            Path = path;
            Speed = speed;
            Distance = 0f;
            _bank = 0f;
            transform.localScale = Vector3.one;
            transform.position = path.PositionAt(0f);
            _prevTangent = path.TangentAt(0f);
            transform.rotation = Quaternion.LookRotation(_prevTangent, Vector3.up);
            Velocity = _prevTangent * speed;
            if (_renderer != null) _renderer.enabled = true;
            CurrentState = State.Flying;
        }

        public void Hide()
        {
            if (_crashRoutine != null)
            {
                StopCoroutine(_crashRoutine);
                _crashRoutine = null;
            }
            CurrentState = State.Idle;
            if (_renderer != null) _renderer.enabled = false;
        }

        void Update()
        {
            if (!IsFlying || Path == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Distance += Speed * dt;
            Vector3 pos = Path.PositionAt(Distance);
            Vector3 tangent = Path.TangentAt(Distance);

            // Bank into turns: signed yaw rate -> roll.
            Vector3 flatPrev = new Vector3(_prevTangent.x, 0f, _prevTangent.z);
            Vector3 flatCur = new Vector3(tangent.x, 0f, tangent.z);
            float yawRate = 0f;
            if (flatPrev.sqrMagnitude > 1e-6f && flatCur.sqrMagnitude > 1e-6f)
                yawRate = Vector3.SignedAngle(flatPrev, flatCur, Vector3.up) / dt;
            float targetBank = Mathf.Clamp(-yawRate * 0.45f, -maxBankDeg, maxBankDeg);
            _bank = Mathf.Lerp(_bank, targetBank, 1f - Mathf.Exp(-bankSmoothing * dt));

            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, _bank);
            Velocity = tangent * Speed;
            _prevTangent = tangent;

            if (Distance >= Path.Length)
            {
                CurrentState = State.Finished;
                Finished?.Invoke(this);
            }
        }

        public void Crash(string reason)
        {
            if (!IsFlying) return;
            CurrentState = State.Crashed;
            if (_smoke != null)
            {
                _smoke.transform.position = transform.position;
                _smoke.Play(true);
            }
            _crashRoutine = StartCoroutine(CrashRoutine());
            Crashed?.Invoke(this, reason);
        }

        IEnumerator CrashRoutine()
        {
            Vector3 v = Velocity * 0.5f + Vector3.down * 0.15f;
            Vector3 spin = new Vector3(UnityEngine.Random.Range(-360f, 360f), UnityEngine.Random.Range(-180f, 180f), UnityEngine.Random.Range(-720f, 720f));
            float t = 0f;
            while (t < crashDuration)
            {
                float dt = Time.deltaTime;
                t += dt;
                v += Physics.gravity * 0.3f * dt;
                transform.position += v * dt;
                transform.Rotate(spin * dt, Space.Self);
                float k = t / crashDuration;
                transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.15f, k * k);
                yield return null;
            }
            if (_renderer != null) _renderer.enabled = false;
            transform.localScale = Vector3.one;
            _crashRoutine = null;
        }
    }
}

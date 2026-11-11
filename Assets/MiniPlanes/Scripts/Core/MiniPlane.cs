using System;
using System.Collections;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The player's plane: follows the path at constant speed, banks into turns, burns fuel, carries a
    /// one-hit shield and an optional turret, can boost for a few seconds, takes hits and tumbles out of the
    /// sky when it crashes. After
    /// a crash it can respawn where it fell, if the level has lives left.
    /// </summary>
    public class MiniPlane : MonoBehaviour
    {
        public enum State { Idle, Waiting, Flying, Crashed, Finished }

        [Tooltip("Wingspan in metres. Small enough to fit through a C sign.")]
        public float wingspan = 0.042f;
        [Tooltip("Collision radius as a fraction of the wingspan, so what you see is what collides.")]
        public float collisionRadiusPerSpan = 0.25f;
        public float maxBankDeg = 60f;
        public float bankSmoothing = 8f;
        public float crashDuration = 1.4f;
        public float propellerSpeedDeg = 1800f;
        [Tooltip("Seconds of immunity after a hit that did not crash the plane.")]
        public float hitGraceSeconds = 1.2f;
        [Tooltip("Seconds of immunity after coming back from a crash.")]
        public float respawnGraceSeconds = 2.5f;
        [Tooltip("Turret size, as a fraction of the wingspan.")]
        public float turretSizePerSpan = 0.22f;
        [Tooltip("How fast a boost ramps the speed up and back down, in multiplier per second.")]
        public float boostRampPerSecond = 4f;

        public float Radius { get; private set; } = 0.01f;
        public State CurrentState { get; private set; } = State.Idle;
        public bool IsFlying => CurrentState == State.Flying;
        public Vector3 Position => transform.position;
        /// <summary>The way the nose points.</summary>
        public Vector3 Forward => transform.forward;
        public Vector3 Velocity { get; private set; }
        /// <summary>Current speed along the path, including any boost.</summary>
        public float Speed => BaseSpeed * SpeedMultiplier;
        /// <summary>The level's speed for this plane, without boosts.</summary>
        public float BaseSpeed { get; private set; }
        public float SpeedMultiplier { get; private set; } = 1f;
        public bool IsBoosting => _boostLeft > 0f;
        public float Distance { get; private set; }
        public CatmullRomPath Path { get; private set; }
        public float Progress => Path == null || Path.Length <= 0f ? 0f : Mathf.Clamp01(Distance / Path.Length);
        public bool HasShield { get; private set; }
        public bool HasTurret => _turret != null;
        public PlaneTurret Turret => _turret;
        /// <summary>The second turret, for planes with twin turrets.</summary>
        public PlaneTurret TwinTurret => _twin;
        public int TurretCount => (_turret != null ? 1 : 0) + (_twin != null ? 1 : 0);
        public int HitsTaken { get; private set; }
        public bool FuelEnabled { get; private set; }
        public float Fuel { get; private set; }
        public float FuelMax { get; private set; } = 1f;
        public float Fuel01 => FuelMax > 0f ? Mathf.Clamp01(Fuel / FuelMax) : 0f;
        public string LastCrashReason { get; private set; } = "";
        /// <summary>The plane design being flown.</summary>
        public PlaneDefinition Definition { get; private set; }
        /// <summary>The crash animation has finished and the plane can respawn.</summary>
        public bool CrashSettled => CurrentState == State.Crashed && _crashRoutine == null;
        /// <summary>For automated tests: nothing can hurt the plane while set.</summary>
        public bool DebugInvulnerable { get; set; }

        public event Action<MiniPlane> Finished;
        public event Action<MiniPlane, string> Crashed;
        public event Action<MiniPlane, string> Hit;
        public event Action<MiniPlane> ShieldLost;

        GameArt _art;
        Transform _head;
        Transform _modelRoot;
        Transform _propeller;
        Renderer[] _modelRenderers = new Renderer[0];
        MeshRenderer _shieldRenderer;
        FuelGaugeView _gauge;
        PlaneTurret _turret;
        PlaneTurret _twin;
        LevelConfig _cfg;
        Vector3 _prevTangent = Vector3.forward;
        float _bank;
        float _graceUntil;
        float _shieldGrace = 1f;
        Coroutine _crashRoutine;
        bool _visible;
        float _boostLeft;
        float _boostMultiplier = 1f;
        TrailRenderer _trail;
        AudioSource _engine;
        float _engineVolume;

        public void SetHead(Transform head) { _head = head; }

        public void Init(GameArt art, Transform head)
        {
            _art = art;
            _head = head;
            Radius = Mathf.Clamp(wingspan * collisionRadiusPerSpan, 0.004f, 0.02f);

            _modelRoot = new GameObject("Model").transform;
            _modelRoot.SetParent(transform, false);
            SetAppearance(null, null);

            if (art != null && art.shieldBubble != null)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Shield";
                var col = sphere.GetComponent<Collider>();
                if (col != null) Destroy(col);
                sphere.transform.SetParent(transform, false);
                sphere.transform.localScale = Vector3.one * (wingspan * 1.9f);
                _shieldRenderer = sphere.GetComponent<MeshRenderer>();
                _shieldRenderer.sharedMaterial = art.shieldBubble;
                _shieldRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _shieldRenderer.receiveShadows = false;
                _shieldRenderer.enabled = false;
            }

            if (art != null && art.fuelGauge != null)
            {
                var gauge = Instantiate(art.fuelGauge, transform, false);
                gauge.name = "FuelGauge";
                _gauge = gauge.GetComponent<FuelGaugeView>();
                gauge.SetActive(false);
            }
            _engine = Sfx.CreateLoop(SoundKind.EngineLoop, transform);
            _engineVolume = Sfx.BaseVolume(SoundKind.EngineLoop);

            if (art != null && art.boostTrail != null)
            {
                var trailGo = new GameObject("BoostTrail");
                trailGo.transform.SetParent(transform, false);
                trailGo.transform.localPosition = Vector3.back * (wingspan * 0.4f);
                _trail = trailGo.AddComponent<TrailRenderer>();
                _trail.sharedMaterial = art.boostTrail;
                _trail.time = 0.45f;
                _trail.minVertexDistance = 0.004f;
                _trail.widthCurve = new AnimationCurve(new Keyframe(0f, wingspan * 0.5f), new Keyframe(1f, 0f));
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.2f), 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                _trail.colorGradient = gradient;
                _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _trail.receiveShadows = false;
                _trail.emitting = false;
            }
            SetVisible(false);
        }

        /// <summary>Swaps in a plane design and paint. Null uses the game's default player plane.</summary>
        public void SetAppearance(PlaneDefinition definition, PlanePalette palette)
        {
            Definition = definition;
            // Detach first: Destroy only happens at the end of the frame, and the old model must not be found before then.
            for (int i = _modelRoot.childCount - 1; i >= 0; i--)
            {
                var old = _modelRoot.GetChild(i);
                old.SetParent(null, false);
                Destroy(old.gameObject);
            }

            GameObject prefab = definition != null && definition.model != null ? definition.model : _art != null ? _art.playerPlane : null;
            Material material = definition != null && definition.material != null ? definition.material : _art != null ? _art.PlayerMaterial : null;
            GameObject model;
            if (prefab != null)
            {
                model = ModelUtil.Attach(_modelRoot, prefab, material, wingspan, ModelUtil.FitAxis.Width);
            }
            else
            {
                var shape = PlaneShapeParams.Friendly(new System.Random(7));
                model = ModelUtil.AttachProcedural(_modelRoot, shape, _art != null ? _art.planeMatte : null);
            }
            _propeller = FindDeep(model.transform, "Propeller");
            if (_art != null) ModelUtil.AddSilhouette(model, _art.silhouette);
            PlanePaint.Apply(model, palette);
            _modelRenderers = _modelRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _modelRenderers) if (r != null) r.enabled = _visible;
            if (_turret != null) _turret.transform.localPosition = TurretMount;
            if (_twin != null) _twin.transform.localPosition = Mirror(TurretMount);
        }

        bool HasTwinTurrets => Definition != null && Definition.Has(PlanePerk.TwinTurrets);

        static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        Vector3 TurretMount => (Definition != null ? Definition.turretMount : new Vector3(0f, 0.16f, -0.08f)) * wingspan;

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        /// <summary>Holds the plane at the start of its path during the pre-flight countdown.</summary>
        public void PrepareAtStart(CatmullRomPath path, LevelConfig cfg, bool keepShield)
        {
            StopCrash();
            _cfg = cfg;
            Path = path;
            BaseSpeed = cfg.planeSpeed;
            ResetBoost();
            Distance = 0f;
            _bank = 0f;
            HitsTaken = 0;
            LastCrashReason = "";
            _graceUntil = 0f;
            _shieldGrace = cfg.shieldGraceSeconds;
            if (!keepShield) HasShield = false;
            FuelEnabled = cfg.useFuel;
            FuelMax = Mathf.Max(1f, cfg.fuelMax);
            Fuel = FuelMax * cfg.startFuelFraction;

            _modelRoot.localScale = Vector3.one;
            _modelRoot.localRotation = Quaternion.identity;
            transform.position = path.PositionAt(0f);
            _prevTangent = path.TangentAt(0f);
            transform.rotation = Quaternion.LookRotation(_prevTangent, Vector3.up);
            Velocity = Vector3.zero;
            CurrentState = State.Waiting;
            SetVisible(true);
        }

        public void TakeOff()
        {
            if (CurrentState != State.Waiting) return;
            Velocity = _prevTangent * Speed;
            CurrentState = State.Flying;
        }

        /// <summary>
        /// Brings a crashed plane back onto its path where it fell, with fresh hits, some fuel and a few
        /// seconds of immunity. Returns false if it isn't crashed.
        /// </summary>
        public bool Respawn()
        {
            if (CurrentState != State.Crashed || Path == null) return false;
            StopCrash();
            HitsTaken = 0;
            LastCrashReason = "";
            if (FuelEnabled && _cfg != null) Fuel = Mathf.Max(Fuel, FuelMax * Mathf.Max(0.5f, _cfg.startFuelFraction));
            _modelRoot.localScale = Vector3.one;
            _modelRoot.localRotation = Quaternion.identity;
            Distance = Mathf.Clamp(Distance, 0f, Path.Length);
            transform.position = Path.PositionAt(Distance);
            _prevTangent = Path.TangentAt(Distance);
            transform.rotation = Quaternion.LookRotation(_prevTangent, Vector3.up);
            _bank = 0f;
            _graceUntil = Time.time + respawnGraceSeconds;
            Velocity = _prevTangent * Speed;
            CurrentState = State.Flying;
            SetVisible(true);
            return true;
        }

        public void Hide()
        {
            StopCrash();
            RemoveTurret();
            CurrentState = State.Idle;
            SetVisible(false);
        }

        void StopCrash()
        {
            if (_crashRoutine == null) return;
            StopCoroutine(_crashRoutine);
            _crashRoutine = null;
        }

        void SetVisible(bool visible)
        {
            _visible = visible;
            foreach (var r in _modelRenderers) if (r != null) r.enabled = visible;
            if (_turret != null) _turret.gameObject.SetActive(visible);
            if (_twin != null) _twin.gameObject.SetActive(visible);
            if (!visible)
            {
                if (_shieldRenderer != null) _shieldRenderer.enabled = false;
                if (_gauge != null) _gauge.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            UpdateEngine();
            if (_propeller != null && (CurrentState == State.Flying || CurrentState == State.Waiting))
            {
                Vector3 axis = _propeller.InverseTransformDirection(transform.forward);
                _propeller.Rotate(axis, propellerSpeedDeg * Time.deltaTime, Space.Self);
            }
            if (!IsFlying || Path == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_boostLeft > 0f) _boostLeft = Mathf.Max(0f, _boostLeft - dt);
            float wantMultiplier = _boostLeft > 0f ? _boostMultiplier : 1f;
            SpeedMultiplier = Mathf.MoveTowards(SpeedMultiplier, wantMultiplier, boostRampPerSecond * dt);
            if (_trail != null) _trail.emitting = SpeedMultiplier > 1.05f && _visible;

            Distance += Speed * dt;
            Vector3 pos = Path.PositionAt(Distance);
            Vector3 tangent = Path.TangentAt(Distance);

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

            if (FuelEnabled && _cfg != null)
            {
                Fuel -= _cfg.fuelDrainPerSecond * dt;
                if (Fuel <= 0f)
                {
                    Fuel = 0f;
                    Crash("ran out of fuel");
                    return;
                }
            }

            if (Distance >= Path.Length)
            {
                CurrentState = State.Finished;
                SetVisible(false);
                Finished?.Invoke(this);
            }
        }

        void LateUpdate()
        {
            if (!_visible) return;

            // Blink through the grace period, so the player sees the plane was just hit.
            bool blink = Time.time < _graceUntil && Mathf.Repeat(Time.time * 10f, 1f) < 0.4f;
            foreach (var r in _modelRenderers) if (r != null) r.enabled = !blink;

            if (_shieldRenderer != null) _shieldRenderer.enabled = HasShield && CurrentState != State.Crashed;

            if (_gauge != null)
            {
                bool showGauge = FuelEnabled && (IsFlying || CurrentState == State.Waiting);
                if (_gauge.gameObject.activeSelf != showGauge) _gauge.gameObject.SetActive(showGauge);
                if (showGauge)
                {
                    var t = _gauge.transform;
                    t.position = transform.position + Vector3.up * (wingspan * 0.85f);
                    if (_head != null)
                    {
                        Vector3 look = t.position - _head.position;
                        if (look.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(look, Vector3.up);
                    }
                    _gauge.Show(Fuel01, _cfg != null && Fuel01 < _cfg.lowFuelFraction);
                }
            }
        }

        public void AddFuel(float amount)
        {
            if (!FuelEnabled) return;
            Fuel = Mathf.Clamp(Fuel + amount, 0f, FuelMax);
        }

        public void GiveShield()
        {
            HasShield = true;
            Sfx.Play(SoundKind.ShieldOn, transform.position);
        }

        /// <summary>The propeller hum: on while the plane is out, higher and louder when boosting.</summary>
        void UpdateEngine()
        {
            if (_engine == null) return;
            bool running = _visible && (CurrentState == State.Flying || CurrentState == State.Waiting);
            if (running && !_engine.isPlaying) _engine.Play();
            else if (!running && _engine.isPlaying) _engine.Stop();
            if (!running) return;
            float boost = Mathf.Clamp01(SpeedMultiplier - 1f);
            _engine.pitch = (CurrentState == State.Waiting ? 0.85f : 1f) + 0.35f * boost;
            _engine.volume = _engineVolume * (1f + 0.4f * boost);
        }

        /// <summary>Speeds the plane up by a multiplier for some seconds. A longer or stronger boost wins.</summary>
        public void Boost(float seconds, float multiplier)
        {
            if (seconds <= 0f || multiplier <= 1f) return;
            Sfx.Play(SoundKind.Boost, transform.position);
            _boostMultiplier = IsBoosting ? Mathf.Max(_boostMultiplier, multiplier) : multiplier;
            _boostLeft = Mathf.Max(_boostLeft, seconds);
        }

        void ResetBoost()
        {
            _boostLeft = 0f;
            _boostMultiplier = 1f;
            SpeedMultiplier = 1f;
            if (_trail != null)
            {
                _trail.emitting = false;
                _trail.Clear();
            }
        }

        /// <summary>
        /// Fits the automatic turret for the level's turret time. Fitting it while one is on restarts its time.
        /// </summary>
        public PlaneTurret EquipTurret(LevelConfig cfg)
        {
            if (_turret != null)
            {
                _turret.Refresh();
                if (_twin != null) _twin.Refresh();
                Sfx.Play(SoundKind.TurretOn, transform.position);
                return _turret;
            }
            _turret = MakeTurret("Turret", TurretMount, cfg);
            if (HasTwinTurrets)
            {
                _twin = MakeTurret("TwinTurret", Mirror(TurretMount), cfg);
                _turret.Partner = _twin;
                _twin.Partner = _turret;
            }
            Sfx.Play(SoundKind.TurretOn, transform.position);
            return _turret;
        }

        PlaneTurret MakeTurret(string name, Vector3 mount, LevelConfig cfg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = mount;
            var turret = go.AddComponent<PlaneTurret>();
            turret.Setup(this, cfg, _art, wingspan * turretSizePerSpan);
            go.SetActive(_visible);
            return turret;
        }

        /// <summary>Takes the turret off, and its twin with it.</summary>
        public void RemoveTurret()
        {
            if (_turret != null) Destroy(_turret.gameObject);
            if (_twin != null) Destroy(_twin.gameObject);
            _turret = null;
            _twin = null;
        }

        /// <summary>
        /// The one entry point for anything that hurts the plane. A shield absorbs it, then remaining
        /// hits on low-risk levels; otherwise the plane crashes. Returns true if it crashed.
        /// </summary>
        public bool Damage(string reason)
        {
            if (!IsFlying || Time.time < _graceUntil || DebugInvulnerable) return false;

            if (HasShield)
            {
                HasShield = false;
                _graceUntil = Time.time + _shieldGrace;
                ShieldLost?.Invoke(this);
                return false;
            }

            HitsTaken++;
            int allowed = _cfg != null ? Mathf.Max(1, _cfg.hitsToCrash) : 1;
            if (HitsTaken < allowed)
            {
                _graceUntil = Time.time + hitGraceSeconds;
                Hit?.Invoke(this, reason);
                return false;
            }

            Crash(reason);
            return true;
        }

        /// <summary>Crashes immediately, past any shield. Damage is the normal route; this is for running dry.</summary>
        public void Crash(string reason)
        {
            if (!IsFlying) return;
            CurrentState = State.Crashed;
            LastCrashReason = reason;
            ResetBoost();
            _crashRoutine = StartCoroutine(CrashRoutine());
            Crashed?.Invoke(this, reason);
        }

        IEnumerator CrashRoutine()
        {
            Vector3 v = Velocity * 0.5f + Vector3.down * 0.15f;
            Vector3 spin = new Vector3(UnityEngine.Random.Range(-360f, 360f), UnityEngine.Random.Range(-180f, 180f), UnityEngine.Random.Range(-720f, 720f));
            Vector3 start = transform.position;
            float t = 0f;
            while (t < crashDuration)
            {
                float dt = Time.deltaTime;
                t += dt;
                v += Physics.gravity * 0.3f * dt;
                transform.position += v * dt;
                transform.Rotate(spin * dt, Space.Self);
                float k = t / crashDuration;
                _modelRoot.localScale = Vector3.one * Mathf.Lerp(1f, 0.15f, k * k);
                yield return null;
            }
            SetVisible(false);
            _modelRoot.localScale = Vector3.one;
            transform.position = start;
            _crashRoutine = null;
        }
    }
}

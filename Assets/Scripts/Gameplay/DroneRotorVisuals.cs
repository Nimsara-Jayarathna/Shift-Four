using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Profiling;

namespace ShiftFour
{
    /// <summary>
    /// Sole presentation owner: model-local pose, independent rotors and property-block feedback.
    /// Runs after DroneBody has accepted the physical movement. Never writes the agent root.
    /// </summary>
    [DefaultExecutionOrder(200), DisallowMultipleComponent]
    public sealed class DroneRotorVisuals : MonoBehaviour
    {
        private static readonly ProfilerMarker AnimationMarker = new ProfilerMarker("ShiftFour.DroneAnimation");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        [SerializeField, Min(0f)] private float rotorRpm = 390f;
        [SerializeField] private bool alternateDirection = true;
        [SerializeField, Range(0f, .02f)] private float hoverAmplitude = .02f;
        [SerializeField, Range(0f, 4f)] private float maximumBank = 4f;
        [SerializeField, Range(.08f, .15f)] private float muzzleFlashDuration = .11f;
        [SerializeField, Range(.04f, .25f)] private float tracerDuration = .13f;
        [SerializeField, Range(.02f, .25f)] private float criticalFailureDuration = .12f;
        [SerializeField, Range(10f, 100f)] private float wreckTumbleSpeed = 65f;
        private Health health;
        private DroneBrain brain;
        private NavMeshAgent agent;
        private DroneMotor motor;
        private Transform root;
        private Vector3 restPosition, previousPosition;
        private Quaternion restRotation;
        private float previousYaw;
        private bool bound, sampled;
        private bool dying;
        private DroneFlightRole flightRole;
        private OutdoorScout scout;
        private bool responding;
        private float responsePulse;
        private Transform viewer;
        private float nextDistantTint, pendingRotorDegrees;
        internal bool OutdoorFlight => flightRole != DroneFlightRole.Default;
        private float failureAge;
        private DroneWeaponEffects effects;
        private DroneWreckMotion wreck;
        private Vector3 previousTint = Vector3.negativeInfinity;
        private Color previousAlert;
        private Vector3 previousMovementVelocity;
        private bool sampledAcceleration;
        private DroneAnimationPose pose = new DroneAnimationPose();
        private readonly List<Transform> pivots = new List<Transform>();
        private readonly List<Quaternion> pivotRest = new List<Quaternion>();
        private readonly List<TintSlot> tints = new List<TintSlot>();
        public DroneVisualState State => wreck != null ? wreck.State : dying ? DroneVisualState.CriticalFailure : pose.State;
        private InterceptorAirframe airframe;
        private InterceptorBrain interceptorBrain;
        internal GameplayAudio AudioBus { private get; set; }
        internal bool IsAirframeDetail(Transform part) => airframe != null && part.IsChildOf(airframe.Root);
        private float yawSpeed, forwardAcceleration, lastDetailUpdate;
        private Vector3 AppliedAngles => (transform.localRotation * Quaternion.Inverse(restRotation)).eulerAngles;
        internal string AnimationReport => $"Flight {State}   |   Sensor {airframe?.SensorStatus}   |   Rotor {rotorRpm * pose.RotorSpeed:F0} rpm\n" +
            $"Forward accel {forwardAcceleration:F1} m/s²   |   Yaw {yawSpeed:F0}°/s   |   Hover {pose.Hover*100:F1} cm\n" +
            $"Pose pitch {Mathf.DeltaAngle(0, AppliedAngles.x):F1}° / bank {Mathf.DeltaAngle(0, AppliedAngles.z):F1}°   |   Hit {pose.HitFlash:P0}   |   Wreck {wreck?.State.ToString() ?? (dying ? "Critical" : "Live")}";
        public DroneWeaponEffects Effects => effects;
        public DroneWeaponMount WeaponMount { get; private set; }
        public DroneWreckMotion Wreck => wreck;
        public int DestructionCount { get; private set; }
        public Vector3 MovementVelocity { get; private set; }
        public float MovementSpeed => MovementVelocity.magnitude;
        public float AlertStrength => pose.AlertBlend;
        public float ScanYaw => pose.ScanYaw;
        public DroneAwarenessState AwarenessState => pose.AwarenessState;

        private sealed class TintSlot
        {
            public Renderer Renderer;
            public int Index, Property;
            public Color Color;
            public bool HasEmission, Sensor;
            public Color Emission;
            public MaterialPropertyBlock Original, Animated;
        }

        private void Awake()
        {
            health = GetComponentInParent<Health>();
            brain = GetComponentInParent<DroneBrain>();
            agent = GetComponentInParent<NavMeshAgent>();
            motor = GetComponentInParent<DroneMotor>();
            root = agent != null ? agent.transform : health != null ? health.transform : transform.parent;
            // A misplaced component must never animate a navigation root or a collider root.
            if (root == null || root == transform) { enabled = false; return; }
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            WeaponMount = new DroneWeaponMount(transform, root, root.GetComponent<DroneBody>());
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("Rotor", System.StringComparison.Ordinal) || filter.sharedMesh == null) continue;
                Transform mesh = filter.transform;
                Transform pivot = mesh.parent;
                if (pivot == null || pivot.parent != transform || pivot.name != "Animated " + filter.name)
                {
                    Vector3 center = mesh.TransformPoint(filter.sharedMesh.bounds.center);
                    pivot = new GameObject("Animated " + filter.name).transform;
                    pivot.SetParent(transform, false);
                    pivot.position = center;
                    mesh.SetParent(pivot, true);
                }
                pivots.Add(pivot);
                pivotRest.Add(pivot.localRotation);
            }
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials; // Once at binding; never instantiate materials.
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) continue;
                    int property = material.HasProperty(BaseColor) ? BaseColor : LegacyColor;
                    if (!material.HasProperty(property)) continue;
                    var original = new MaterialPropertyBlock();
                    var animated = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original, i);
                    renderer.GetPropertyBlock(animated, i);
                    tints.Add(new TintSlot { Renderer = renderer, Index = i, Property = property,
                        Color = original.HasColor(property) ? original.GetColor(property) : material.GetColor(property),
                        Sensor = material.name.IndexOf("Glow", System.StringComparison.OrdinalIgnoreCase) >= 0,
                        HasEmission = material.HasProperty(EmissionColor),
                        Emission = original.HasColor(EmissionColor) ? original.GetColor(EmissionColor) :
                            material.HasProperty(EmissionColor) ? material.GetColor(EmissionColor) : Color.black,
                        Original = original, Animated = animated });
                }
            }
            interceptorBrain = brain as InterceptorBrain;
            if (interceptorBrain != null) airframe = new InterceptorAirframe(transform, pivots);
            effects = new DroneWeaponEffects(root);
        }

        internal static DroneFlightRole Identify(DroneBrain source) => source switch
        {
            GuardBrain => DroneFlightRole.Guard,
            ScoutBrain => DroneFlightRole.Scout,
            FlankerBrain => DroneFlightRole.Flanker,
            InterceptorBrain => DroneFlightRole.Interceptor,
            _ => DroneFlightRole.Default
        };

        internal void ConfigureOutdoorFlight(Camera camera, int slot, Font font, Material lettering)
        {
            if (OutdoorFlight || root == null || root == transform) return;
            flightRole = Identify(brain);
            if (!OutdoorFlight) return;
            if (font != null && lettering != null)
            {
                // The imported fuselages have flat end plates. Clear their faces by 3 mm
                // in model space. The Interceptor carries its stencil on the Phase C sensor brow.
                (Vector3 at, float width, float height) = flightRole switch
                {
                    DroneFlightRole.Guard => (new Vector3(0, .09f, .703f), .70f, .12f),
                    DroneFlightRole.Flanker => (new Vector3(0, .09f, .683f), .36f, .095f),
                    DroneFlightRole.Scout => (new Vector3(0, .09f, .653f), .28f, .09f),
                    _ => (new Vector3(0, .237f, .929f), .46f, .052f)
                };
                var stamp = PrototypeGeometry.Label(flightRole.ToString().ToUpperInvariant(), transform, at,
                    .08f, font, width, height, lettering);
                stamp.name = "Chassis stencil";
                stamp.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var ink = new MaterialPropertyBlock();
                var renderer = stamp.GetComponent<Renderer>();
                renderer.GetPropertyBlock(ink);
                ink.SetColor(BaseColor, new Color(.72f, .78f, .76f));
                renderer.SetPropertyBlock(ink);
            }
            viewer = camera != null ? camera.transform : null;
            nextDistantTint = Time.time + slot * .004f;
            pose.Role = flightRole;
            effects.ConfigureOutdoorPresentation();
            scout = root.GetComponent<OutdoorScout>();
            // The FBXs carry gameplay MeshColliders on the animated chassis and rotor meshes.
            // Keep their exact rest transforms, layers and shared meshes under the simulation root.
            Transform hitboxes = new GameObject("Flight hitboxes").transform;
            hitboxes.SetParent(root, false);
            foreach (var original in GetComponentsInChildren<MeshCollider>())
            {
                if (!original.enabled) continue;
                var part = new GameObject(original.name);
                part.layer = original.gameObject.layer;
                part.transform.SetParent(original.transform, false);
                part.transform.SetParent(hitboxes, true);
                var fixedShape = part.AddComponent<MeshCollider>();
                fixedShape.cookingOptions = original.cookingOptions;
                fixedShape.convex = original.convex;
                fixedShape.sharedMesh = original.sharedMesh;
                fixedShape.sharedMaterial = original.sharedMaterial;
                fixedShape.isTrigger = original.isTrigger;
                fixedShape.includeLayers = original.includeLayers;
                fixedShape.excludeLayers = original.excludeLayers;
                original.enabled = false;
                Destroy(original);
            }
        }

        private void OnEnable()
        {
            if (root == null || root == transform) return;
            pendingRotorDegrees = 0f;
            sampled = false;
            sampledAcceleration = false;
            if (!dying) pose = new DroneAnimationPose { Role = flightRole };
            if (health != null && !health.IsAlive) BeginDeath();
            previousTint = Vector3.negativeInfinity;
            if (bound) return;
            if (health != null) { health.Damaged += OnDamaged; health.Died += OnDied; }
            if (brain != null) brain.ShotFired += OnFired;
            bound = true;
        }

        private void OnDisable()
        {
            if (bound)
            {
                if (health != null) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
                if (brain != null) brain.ShotFired -= OnFired;
                bound = false;
            }
            if (root == null || root == transform) return;
            effects?.Clear();
            if (dying) return; // Never move a falling/settled wreck back to its flying rest pose.
            if (OutdoorFlight) WeaponMount?.SetRecoil(0f);
            transform.SetLocalPositionAndRotation(restPosition, restRotation);
            for (int i = 0; i < pivots.Count; i++) if (pivots[i] != null) pivots[i].localRotation = pivotRest[i];
            foreach (TintSlot tint in tints)
                if (tint.Renderer != null) tint.Renderer.SetPropertyBlock(tint.Original.isEmpty ? null : tint.Original, tint.Index);
        }

        private void OnFired(DroneBrain source)
        {
            if (effects == null || dying || (health != null && !health.IsAlive)) return;
            pose.Fire();
            if (OutdoorFlight) WeaponMount?.SetRecoil(1f);
            effects.Fire(source.LastShotOrigin, source.LastShotHit, muzzleFlashDuration, tracerDuration);
        }
        private void OnDamaged(DamageRecord hit) { if (hit.Fatal) BeginDeath(); else pose.Hit(false); }
        private void OnDied(Health source) => BeginDeath();
        private void BeginDeath()
        {
            if (dying) return;
            dying = true; DestructionCount++;
            pose.Shutdown(); effects?.CancelShot();
            if (airframe != null)
            {
                effects?.Impact(transform.position + transform.up * .15f, transform.up);
                AudioBus?.InterceptorFailure(transform.position);
            }
        }

        public void GroundImpact(Vector3 point, Vector3 normal)
        { if (isActiveAndEnabled)
            {
                effects?.Impact(point, normal);
                if (airframe != null) AudioBus?.InterceptorImpact(point);
            } }

        public void WreckSettled()
        {
            pose.StopRotors();
            airframe?.Stop();
            if (!OutdoorFlight) return;
            foreach (TintSlot tint in tints)
            {
                if (!tint.Sensor || !tint.HasEmission) continue;
                tint.Animated.SetColor(EmissionColor, Color.black);
                tint.Renderer.SetPropertyBlock(tint.Animated, tint.Index);
            }
            enabled = false;
        }

        private void OnDestroy() { effects?.Dispose(); WeaponMount?.Dispose(); airframe?.Dispose(); }

        private void LateUpdate()
        {
            using var sample = AnimationMarker.Auto();
            float dt = Time.deltaTime;
            if (dt <= 0f || root == null || (airframe != null && Time.timeScale == 0f)) return;
            Vector3 position = root.position;
            float yaw = root.eulerAngles.y;
            Vector3 velocity = sampled ? (position - previousPosition) / dt : Vector3.zero;
            float yawRate = sampled ? Mathf.DeltaAngle(previousYaw, yaw) / dt : 0f;
            previousPosition = position;
            previousYaw = yaw;
            sampled = true;
            velocity.y = 0f;
            // Teleports/restarts are not flight acceleration. Sample again on the next frame.
            bool teleported = velocity.sqrMagnitude > 400f;
            if (teleported) { velocity = Vector3.zero; yawRate = 0f; }
            if (!dying && motor != null && motor.isActiveAndEnabled) velocity = motor.ActualVelocity;
            if (!dying || !OutdoorFlight) MovementVelocity = velocity;
            // A stable reference preserves acceleration/braking cues as the speed limit changes.
            float speed = motor != null && motor.HasMovementProfile ? motor.AnimationReferenceSpeed :
                agent != null ? Mathf.Max(.1f, agent.speed) : 3.5f;
            if (health != null && !health.IsAlive) BeginDeath();
            Vector3 acceleration = sampledAcceleration && !teleported && dt <= .25f ?
                root.InverseTransformDirection((velocity - previousMovementVelocity) / dt) / speed : Vector3.zero;
            previousMovementVelocity = velocity;
            sampledAcceleration = !teleported;
            pose.SetAcceleration(acceleration.z);
            forwardAcceleration = acceleration.z * speed;
            yawSpeed = yawRate;
            var awareness = brain != null && brain.Awareness != null && brain.Awareness.IsEnabled ?
                brain.Awareness.State : DroneAwarenessState.Patrol;
            bool preparing = OutdoorFlight && brain is FlankerBrain flanker && flanker.IsAmbushWarning;
            if (scout != null)
                awareness = scout.State switch
                {
                    OutdoorScout.Activity.Investigate => DroneAwarenessState.Search,
                    OutdoorScout.Activity.Chase or OutdoorScout.Activity.Attack or OutdoorScout.Activity.Alert => DroneAwarenessState.Combat,
                    _ => DroneAwarenessState.Patrol
                };
            bool response = OutdoorFlight && brain is InterceptorBrain interceptor && interceptor.IsResponding;
            if (response && !responding && !dying)
            {
                responsePulse = .45f;
                AudioBus?.InterceptorAlert(transform.position);
            }
            responding = response;
            responsePulse = Mathf.Max(0f, responsePulse - dt);
            pose.SetAwareness(awareness);
            pose.SetFlightCues(acceleration.x, preparing || scout != null && scout.State == OutdoorScout.Activity.Alert);
            pose.HoverAmplitude = Mathf.Clamp(hoverAmplitude, 0f, .02f) * (flightRole == DroneFlightRole.Guard ? .45f : 1f);
            pose.BankDegrees = Mathf.Clamp(maximumBank, 0f, 4f);
            pose.Step(dt, root.InverseTransformDirection(velocity) / speed, yawRate, health == null || health.IsAlive);
            if (dying)
            {
                failureAge += dt;
                // Defer construction until after all Health.Died handlers stop navigation/disable hitboxes.
                if (wreck == null) wreck = DroneWreckMotion.Create(this, root, criticalFailureDuration, wreckTumbleSpeed);
            }
            else
            {
                Quaternion tilt = Quaternion.RotateTowards(Quaternion.identity, Quaternion.Euler(pose.Pitch, pose.ScanYaw, pose.Bank), pose.TiltLimit);
                transform.SetLocalPositionAndRotation(restPosition + Vector3.up * pose.Hover, tilt * restRotation);
            }
            if (!dying && scout == null) WeaponMount?.AimAt(brain != null ? brain.WeaponAimPoint : null);
            if (OutdoorFlight && !dying && (pose.FireFlash > 0f || previousTint.x > 0f))
                WeaponMount?.SetRecoil(pose.FireFlash);
            effects?.Tick(dt);
            float degrees = rotorRpm * 6f * pose.RotorTravel;
            if (State == DroneVisualState.Disabled) { pose.StopRotors(); degrees = 0f; }
            // Keep phase while distant and behind the player. Flush on camera turns, hits and shutdown.
            pendingRotorDegrees = (pendingRotorDegrees + degrees) % 360f;
            bool distant = viewer != null && (position - viewer.position).sqrMagnitude > 40f * 40f;
            if (!distant || dying || pose.FireFlash > 0f || pose.HitFlash > 0f ||
                Time.time >= nextDistantTint || Vector3.Dot(viewer.forward, position - viewer.position) >= -2f)
            {
                var spin = Quaternion.AngleAxis(pendingRotorDegrees, Vector3.up);
                var reverse = Quaternion.Inverse(spin);
                for (int i = 0; i < pivots.Count; i++)
                    pivots[i].localRotation *= alternateDirection && i % 2 == 1 ? reverse : spin;
                pendingRotorDegrees = 0f;
            }
            float failureFlash = dying && failureAge < criticalFailureDuration ?
                .6f + .4f * Mathf.Sin(failureAge * 100f) : 0f;
            var tintState = new Vector3(pose.FireFlash + failureFlash, pose.HitFlash, pose.ShutdownBlend);
            float alertWeight = dying ? 0f : pose.AlertBlend * (1f - Mathf.Max(pose.FireFlash, pose.HitFlash));
            bool combatPulse = !dying && (brain != null && brain.CombatAlertPulse || preparing ||
                responsePulse > .32f || responsePulse > .08f && responsePulse < .22f);
            Color alertColor = combatPulse ? new Color(1f, .13f, .09f) : pose.AlertColor;
            if (combatPulse) alertWeight = Mathf.Max(alertWeight, .55f);
            Color alert = alertWeight > 0f ? alertColor : Color.clear;
            alert.a = alertWeight;
            // Distant sensor colors can update at 10 Hz. Muzzle events, spin and aim keep full timing.
            if (!dying && distant &&
                pose.FireFlash == 0f && pose.HitFlash == 0f)
            {
                if (Time.time < nextDistantTint) return;
                nextDistantTint = Time.time + .1f;
            }
            if (airframe != null && State != DroneVisualState.Disabled)
            {
                float elapsed = Mathf.Max(dt, Time.time - lastDetailUpdate);
                airframe.Step(interceptorBrain, pose, Mathf.Min(.2f, elapsed), dying, failureFlash);
                lastDetailUpdate = Time.time;
            }
            if (tintState == previousTint && alert == previousAlert) return;
            previousTint = tintState;
            previousAlert = alert;
            foreach (TintSlot tint in tints)
            {
                float alarm = !OutdoorFlight || tint.Sensor ? alertWeight : alertWeight * .16f;
                Color color = Color.Lerp(tint.Color, alertColor, alarm);
                color = Color.Lerp(color, Color.white, pose.FireFlash * .12f);
                color = Color.Lerp(color, new Color(1f, .24f, .12f, tint.Color.a), pose.HitFlash * .8f);
                color = Color.Lerp(color, new Color(.28f, .28f, .28f, tint.Color.a), pose.ShutdownBlend);
                color = Color.Lerp(color, new Color(2f, 2.4f, 3f, tint.Color.a), OutdoorFlight && !tint.Sensor ? 0f : failureFlash);
                color.a = tint.Color.a;
                tint.Animated.SetColor(tint.Property, color);
                if (tint.HasEmission)
                    tint.Animated.SetColor(EmissionColor, combatPulse && (!OutdoorFlight || tint.Sensor) ? tint.Emission + alertColor * .45f :
                        Color.Lerp(tint.Emission, Color.black, pose.ShutdownBlend));
                tint.Renderer.SetPropertyBlock(tint.Animated, tint.Index);
            }
        }
    }
}

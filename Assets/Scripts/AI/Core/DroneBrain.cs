using UnityEngine;
using Unity.Profiling;

namespace ShiftFour
{
    public struct DecisionOption
    {
        public string Name;
        public Vector3 Destination;
        public float Score;
        public DecisionOption(string name, Vector3 destination, float score)
        { Name = name; Destination = destination; Score = score; }
    }

    [RequireComponent(typeof(Health), typeof(DroneMotor))]
    public abstract class DroneBrain : MonoBehaviour
    {
        private static readonly ProfilerMarker FireProfile=new ProfilerMarker("ShiftFour.Fire");

        private static readonly ProfilerMarker ShotQueryMarker = new ProfilerMarker("ShiftFour.ClearShot");
        [SerializeField] private string agentName;
        [SerializeField] private Vector3 home;
        [SerializeField, Tooltip("Optional override. Existing drones load their matching default asset at startup.")]
        private DroneDifficultyProfile difficultyProfile;
        [SerializeField] private AwarenessSettings awarenessSettings = new AwarenessSettings();
        [SerializeField, Range(.02f, .3f)] private float decisionInterval = .08f;
        [SerializeField] private bool rapidSoundResponse = true;
        [SerializeField] private float visionDistance = 15f;
        [SerializeField] private float attackRange = 11f;
        [SerializeField] private float attackInterval = 1.1f;
        [SerializeField, Min(0f)] private float combatOriginHeight = 1.25f;

        private Health health;
        private float prototypeMaximumHealth;
        private OutdoorScoutEncounter outdoorAttackBudget;
        private Health playerHealth;
        private DroneCombatReadiness combatReadiness;
        private DroneMotor motor;
        private PlayerController player;
        private WaypointGraph outdoorGraph;
        private Vector3[] outdoorPatrol;
        private int outdoorPost = 1;
        private float outdoorArrivalDistance;
        private float nextDecision;
        private float nextShot;
        public bool HasCommittedToAttack { get; private set; }
        protected virtual float ShotInterval => attackInterval;
        protected virtual float VisionDistance => visionDistance;
        protected virtual float AttackRange => attackRange;
        protected virtual float DamagePerShot => ShotDamage;
        protected virtual bool CanTarget(Vector3 position) => true;
        protected virtual bool TryAuthorizeShot() => true;
        protected virtual void OnDefeated() { }
        protected virtual string GeneratedPatrolLabel => "Patrol outdoor post ";
        private float lastSeenAt = float.NegativeInfinity;
        private Vector3 lastKnown;
        private Vector3 observedVelocity;
        private Vector3 previousObservation;
        private float previousObservationTime;
        private DroneWeaponMount weaponMount;
        public Vector3? WeaponAimPoint { get; private set; }

        // Presentation observes only shots accepted by the existing fire-time checks.
        public event System.Action<DroneBrain> ShotFired;
        public Vector3 LastShotOrigin { get; private set; }
        public Vector3 LastShotHit { get; private set; }

        public string AgentName => agentName;
        [field: SerializeField] public string CurrentDecision { get; private set; } = "Idle";
        public Vector3 Home => home;
        public DroneDifficultyProfile DifficultyProfile => difficultyProfile;
        public float ShotDamage => (difficultyProfile != null ? DifficultySettings.shotDamage : 10f) /
            GameSession.CombatDurationMultiplier;
        public DroneDifficultySettings DifficultySettings { get; private set; }
        internal DroneDifficultySettings MovementSettings { get; private set; }
        internal bool CombatAlertPulse => Alive && combatReadiness != null && combatReadiness.Pulse;
        internal bool IsAggressive => Alive && combatReadiness != null && combatReadiness.Aggressive;
        protected bool HasPrototypeTempo => combatReadiness != null;
        internal float PrototypeDecisionOffset { get; private set; }
        internal float ReactionSeconds(float previous) => combatReadiness?.Tuning.reactionSeconds ?? previous;
        internal bool ReadyForPrototypeShot => combatReadiness == null || combatReadiness.ReadyToFire;
        public DroneHearingSensor Hearing { get; private set; }
        public DroneAwareness Awareness { get; private set; }
        protected Vector3 LastKnown => lastKnown;
        protected float LastSeenAt => lastSeenAt;
        protected Vector3 ObservedVelocity => observedVelocity;
        protected PlayerController Player => player;
        protected DroneMotor Motor => motor;
        protected WaypointGraph NavigationGraph => outdoorGraph != null ? outdoorGraph :
            (GameSession.Instance != null ? GameSession.Instance.Graph : null);
        protected bool Alive => health != null && health.IsAlive;
        protected virtual bool OwnsSoundInvestigation => false;
        protected virtual bool UsesImmediateSoundInvestigation => rapidSoundResponse;
        public float DecisionInterval => float.IsNaN(decisionInterval) || float.IsInfinity(decisionInterval)
            ? .08f : Mathf.Clamp(decisionInterval, .02f, .3f);
        private Vector3 CombatOrigin => weaponMount != null ? weaponMount.MuzzlePosition :
            transform.position + Vector3.up * combatOriginHeight;

        public void Configure(string label, Vector3 post)
        {
            agentName = label;
            home = post;
        }

        internal void ConfigurePrototypeCombat(DroneCombatTempo tuning, int slot, float maximumHealth,
            OutdoorScoutEncounter attackBudget)
        {
            combatReadiness = new DroneCombatReadiness(tuning);
            prototypeMaximumHealth = maximumHealth;
            outdoorAttackBudget = attackBudget;
            PrototypeDecisionOffset = slot * .009f;
            nextDecision = Time.time + PrototypeDecisionOffset;
        }
        internal void ReactToThreat() { if (Alive) combatReadiness?.React(); }
        internal void ConfirmPrototypeSight() { if (Alive) combatReadiness?.ConfirmSight(); }
        internal virtual bool CanReactToGunfire(Vector3 position) => true;

        internal void ConfigureOutdoor(PlayerController target, WaypointGraph graph, Vector3[] patrol)
        {
            player = target;
            outdoorGraph = graph;
            outdoorPatrol = patrol;
            outdoorArrivalDistance = GetComponent<UnityEngine.AI.NavMeshAgent>().stoppingDistance + .2f;
            GetComponent<DroneMotor>().BindOutdoorNavigation(graph);
        }

        protected virtual void Start()
        {
            health = GetComponent<Health>();
            motor = GetComponent<DroneMotor>();
            if (outdoorPatrol == null)
                player = GameSession.Instance != null ? GameSession.Instance.Player : null;
            playerHealth = player != null ? player.GetComponent<Health>() : null;
            weaponMount = GetComponentInChildren<DroneRotorVisuals>()?.WeaponMount;
            if (outdoorPatrol != null && (outdoorGraph == null || outdoorPatrol.Length < 2 ||
                playerHealth == null || weaponMount == null))
            {
                Debug.LogWarning($"Open World: {agentName} could not initialize. Check patrol, graph, player Health and weapon mount.", this);
                enabled = false;
                return;
            }
            health.Died += OnDied;
            lastKnown = home;
            InitializeDifficulty();
            if (difficultyProfile != null) motor.InitializeMovement(MovementSettings);
            var response = awarenessSettings.Validated();
            if (UsesImmediateSoundInvestigation)
            {
                response.ImmediateSoundInvestigation = true;
                response.minimumNoiseStrength = .01f;
                response.minimumDwellSeconds = .05f;
            }
            Awareness = new DroneAwareness(response,
                difficultyProfile != null ? DifficultySettings : DroneDifficultySettings.Default);
            if (!health.IsAlive) Awareness.Disable();
            Hearing = GetComponent<DroneHearingSensor>();
            if (Hearing == null) Hearing = gameObject.AddComponent<DroneHearingSensor>();
            Hearing.Configure(NavigationGraph,
                difficultyProfile != null ? DifficultySettings.hearingSensitivity : 1f);
            if (rapidSoundResponse) Hearing.EnableRapidResponse();
            Hearing.ObservationAccepted += OnHeard;
        }

        private void OnHeard(SoundObservation observation)
        {
            if (rapidSoundResponse)
                nextDecision = HasPrototypeTempo ? Mathf.Min(nextDecision, Time.time + PrototypeDecisionOffset) : 0f;
        }

        // Health initialization is one-shot. Movement is bound separately in Start so profile
        // reapplication during combat cannot reset health, speed or navigation.
        public bool InitializeDifficulty()
        {
            if (difficultyProfile == null)
            {
                if (this is ScoutBrain) difficultyProfile = DroneDifficultyProfile.LoadDefault(DroneArchetype.Scout);
                else if (this is GuardBrain) difficultyProfile = DroneDifficultyProfile.LoadDefault(DroneArchetype.Guard);
                else if (this is InterceptorBrain) difficultyProfile = DroneDifficultyProfile.LoadDefault(DroneArchetype.Interceptor);
                else if (this is FlankerBrain) difficultyProfile = DroneDifficultyProfile.LoadDefault(DroneArchetype.Flanker);
            }
            if (difficultyProfile == null) return false; // Custom/unconfigured brains retain legacy tuning.
            DifficultySettings = difficultyProfile.Settings;
            MovementSettings = combatReadiness != null ? combatReadiness.Tuning.Apply(DifficultySettings) : DifficultySettings;
            if (health == null) health = GetComponent<Health>();
            float maximum = prototypeMaximumHealth > 0f ? prototypeMaximumHealth : DifficultySettings.maximumHealth;
            if (gameObject.scene.name == "MainLab") maximum *= GameSession.CombatDurationMultiplier;
            return health != null && health.TryInitializeMaximum(maximum);
        }

        private void Update()
        {
            if (!Alive || player == null || player.MenuBlocksInput) return;
            var session = outdoorPatrol == null ? GameSession.Instance : null;
            if (session != null && !session.CanPlay) return;
            if (playerHealth == null || !playerHealth.IsAlive || (session != null && session.Ended))
            {
                WeaponAimPoint = null;
                motor.Stop();
                return;
            }
            if (Time.time < nextDecision) return;
            bool visible = CanSeePlayer();
            if (visible) ConfirmPrototypeSight();
            nextDecision = Time.time + (combatReadiness == null ? DecisionInterval :
                (IsAggressive ? .07f : .13f) + PrototypeDecisionOffset * .11f);
            if (HasPrototypeTempo) motor.AimAt(null);
            WeaponAimPoint = visible ? player.EyePosition : (Vector3?)null;
            weaponMount?.AimAt(WeaponAimPoint);
            Vector3 position = player.transform.position;
            float distance = Vector3.Distance(transform.position, position);
            if (visible)
            {
                float elapsed = Time.time - previousObservationTime;
                // Accumulate sightings across rapid ticks to preserve a useful velocity time baseline.
                if (elapsed >= .1f && elapsed < 3f && previousObservationTime > 0f)
                {
                    observedVelocity = (position - previousObservation) / elapsed;
                    previousObservation = position;
                    previousObservationTime = Time.time;
                }
                else if (previousObservationTime <= 0f || elapsed >= 3f)
                {
                    observedVelocity = Vector3.zero;
                    previousObservation = position;
                    previousObservationTime = Time.time;
                }
                lastKnown = position;
                lastSeenAt = Time.time;
            }
            SoundObservation sound = default;
            bool hasSound = Hearing != null && Hearing.TryGetLatest(out sound);
            Awareness.Step(Time.time, visible, visible ? position : default, hasSound, sound,
                Vector3.Distance(transform.position, home) <= Awareness.PostArrivalRadius);
            // A drone that has opened fire keeps pursuing until the encounter ends.
            // Navigation still respects closed gates, and every shot still needs clear cover checks.
            // Keep each archetype's close-range tactics; pursue instead of disengaging
            // when cover or distance would otherwise let the player end the encounter.
            if (outdoorPatrol == null && HasCommittedToAttack && (!visible || distance > attackRange))
            {
                CurrentDecision = "Pursue engaged player";
                motor.SetMovementMode(DroneMovementMode.Chase);
                motor.GoToCombat(position, player, visible);
                return;
            }
            motor.SetMovementMode(DroneMovementSpeeds.FromAwareness(Awareness.State));
            if (outdoorPatrol != null && !visible && ShouldResumePatrol(position))
            {
                if (Vector3.Distance(transform.position, outdoorPatrol[outdoorPost]) <= outdoorArrivalDistance)
                    outdoorPost = (outdoorPost + 1) % outdoorPatrol.Length;
                CurrentDecision = GeneratedPatrolLabel + (outdoorPost + 1);
                motor.SetMovementMode(DroneMovementMode.Patrol);
                motor.GoTo(outdoorPatrol[outdoorPost]);
                return;
            }
            DecisionOption choice = Decide(visible, position, distance);
            if (UsesImmediateSoundInvestigation && !OwnsSoundInvestigation && !visible && hasSound &&
                Awareness.State == DroneAwarenessState.Investigate && Reachable(sound.Position))
            {
                choice = Option("Investigate " + sound.Category, sound.Position, 85f);
                motor.SetMovementMode(DroneMovementMode.Investigate);
            }
            CurrentDecision = choice.Name;
            if (HasPrototypeTempo && visible && (choice.Destination - transform.position).sqrMagnitude < .02f)
            {
                motor.HoldAtFiringPost();
                motor.AimAt(player.EyePosition);
            }
            else MoveToDecision(choice, visible);
            if (visible && distance <= AttackRange) TryShoot();
        }

        protected virtual bool ShouldResumePatrol(Vector3 targetPosition) => Awareness.State == DroneAwarenessState.Patrol;
        protected virtual bool InDetectionArc(Vector3 offset) => true;

        protected void ResumePatrolAt(int post) => outdoorPost = post;

        protected abstract DecisionOption Decide(bool visible, Vector3 playerPosition, float distance);

        // Policies with validated tactical destinations retain their chosen endpoint.
        protected virtual void MoveToDecision(DecisionOption choice, bool visible)
            => motor.GoToCombat(choice.Destination, player, visible);

        protected static DecisionOption Best(params DecisionOption[] options)
        {
            DecisionOption winner = options[0];
            foreach (DecisionOption option in options)
                if (option.Score > winner.Score) winner = option;
            return winner;
        }

        protected static DecisionOption Option(string name, Vector3 target, float score)
            => new DecisionOption(name, target, score);

        protected bool Reachable(Vector3 target)
        {
            WaypointGraph graph = NavigationGraph;
            return graph != null && graph.FindRoute(transform.position, target).Count > 0;
        }

        private bool CanSeePlayer()
        {
            if (HasPrototypeTempo && !player.isActiveAndEnabled) return false;
            if (!CanTarget(player.transform.position)) return false;
            Vector3 origin = CombatOrigin;
            Vector3 vector = player.EyePosition - origin;
            if (vector.sqrMagnitude > VisionDistance * VisionDistance || !InDetectionArc(vector)) return false;
            // Stationed drones watch their entire surroundings rather than waiting to face the player.
            return ClearShot(origin, vector, VisionDistance);
        }

        private void TryShoot()
        {
            using var profileSample=FireProfile.Auto();
            if (!Alive || playerHealth == null || !playerHealth.IsAlive ||
                (outdoorPatrol == null && GameSession.Instance != null && GameSession.Instance.Ended) || Time.time < nextShot) return;
            if (!ReadyForPrototypeShot || (outdoorAttackBudget != null && !outdoorAttackBudget.CanFire(health))) return;
            float range = AttackRange;
            if (Vector3.Distance(CombatOrigin, player.EyePosition) > range ||
                (combatReadiness == null ? !CanSeePlayer() : !player.isActiveAndEnabled || !CanTarget(player.transform.position))) return;
            weaponMount?.AimAt(player.EyePosition);
            // A protruding barrel cannot bypass cover between its mounting point and tip.
            if (weaponMount != null && !ClearShotAtFire(weaponMount.PivotPosition,
                player.EyePosition - weaponMount.PivotPosition, range, out _)) return;
            Vector3 origin = CombatOrigin;
            Vector3 vector = player.EyePosition - origin;
            // A second raycast at fire time makes solid shelves and pillars real cover.
            bool clear = ClearShotAtFire(origin, vector, range, out Vector3 hitPoint);
            if (clear && !TryAuthorizeShot()) return;
            nextShot = Time.time + ShotInterval;
            if (clear)
            {
                outdoorAttackBudget?.RecordFire(health);
                HasCommittedToAttack = true;
                if (outdoorPatrol == null) Awareness?.CommitToCombat(Time.time);
                WeaponAimPoint = player.EyePosition;
                playerHealth.ApplyDamage(DamagePerShot, this, "Drone shot: " + agentName);
                PublishAcceptedShot(origin, hitPoint);
                SoundEvents.ReportNoise(origin, NoiseCategory.Gunshot, 1f, this);
            }
        }

        internal void PublishAcceptedShot(Vector3 origin, Vector3 hit)
        {
            LastShotOrigin = origin;
            LastShotHit = hit;
            ShotFired?.Invoke(this);
        }

        private bool ClearShot(Vector3 origin, Vector3 vector, float range)
            => ClearShotAtFire(origin, vector, range, out _);

        private bool ClearShotAtFire(Vector3 origin, Vector3 vector, float range, out Vector3 hitPoint)
        {
            hitPoint = origin;
            using var sample = ShotQueryMarker.Auto();
            // Raycasts alone miss solids containing the origin. Reject embedded muzzle positions.
            foreach (Collider overlap in Physics.OverlapSphere(origin, .025f, ~0, QueryTriggerInteraction.Ignore))
                if (!overlap.transform.IsChildOf(transform)) return false;
            RaycastHit closest = default;
            float distance = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, vector.normalized,
                Mathf.Min(range, vector.magnitude + .05f), ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
                closest = hit;
                distance = hit.distance;
            }
            if (closest.collider == null || closest.collider.GetComponentInParent<PlayerController>() != player) return false;
            hitPoint = closest.point;
            return true;
        }

        private void OnDied(Health dead)
        {
            WeaponAimPoint = null;
            Awareness?.Disable();
            if (Hearing != null) Hearing.enabled = false;
            motor.Stop();
            // Generated drones leave avoidance immediately. The separate wreck keeps its physics rig.
            if (outdoorPatrol != null && TryGetComponent(out UnityEngine.AI.NavMeshAgent agent)) agent.enabled = false;
            foreach (Collider body in GetComponentsInChildren<Collider>()) body.enabled = false;
            OnDefeated();
            if (outdoorPatrol == null) GameSession.Instance?.DroneDestroyed(this);
        }

        private void OnDestroy()
        {
            if (Hearing != null) Hearing.ObservationAccepted -= OnHeard;
            Awareness?.Disable();
            if (health != null) health.Died -= OnDied;
        }
    }
}

using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    // MainLab's policy owns mission graph and engagement rules. The outdoor adapter uses
    // the same prefab, difficulty, movement, damage and presentation with a local patrol.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(210)]
    public sealed class OutdoorScout : MonoBehaviour
    {
        public enum Activity { Patrol, Alert, Chase, Attack, Investigate, ReturnToPatrol, Destroyed }

        private const float AttackRange = 8f;
        private const float ShotInterval = 2.15f;
        private const float FacingTolerance = 12f;
        private ScoutBrain source;
        private PlayerController player;
        private Vector3[] patrol;
        private Transform marker;
        private OutdoorScoutEncounter encounter;
        private float detectionRange, viewCosine, leash, searchTime;
        private NavMeshAgent agent;
        private NavMeshPath route;
        private DroneMotor motor;
        private DroneBody body;
        private DroneWeaponMount weapon;
        private DroneWeaponEffects effects;
        private Health health, playerHealth;
        private Vector3 lastSeen, destination, contactOrigin;
        private Vector3? aimPoint;
        private bool hasDestination;
        private bool warnedAboutNavigation;
        private bool engaged, reportedCover, reportedDamage;
        private float nextDecision, nextShot, lastSeenAt = float.NegativeInfinity, attackAllowedAt;
        private float nextRouteAttempt;
        private Vector3 heardPosition;
        private float investigateSoundUntil;
        private Vector3 searchGoal;
        private int searchStep;
        private float nextSearchStep;
        private bool haveSearchGoal;
        private static readonly Vector3[] SearchOffsets =
            { Vector3.zero, Vector3.right * 3f, Vector3.back * 3f, Vector3.left * 3f, Vector3.forward * 3f };
        private Renderer statusStripe;
        private MaterialPropertyBlock statusTint;
        private static readonly int TintColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color AlertColor = new Color(1f, .37f, .07f);
        private int post = 1;

        [field: SerializeField] public Activity State { get; private set; }
        public int PatrolPost => post + 1;

        internal Transform Marker => marker;

        internal void Configure(ScoutBrain prefabBrain, PlayerController target, Vector3[] posts,
            OutdoorScoutEncounter owner, float range, float fieldOfView, float limit, float searchDuration,
            Transform sensor, Renderer stripe)
        {
            source = prefabBrain;
            player = target;
            patrol = posts;
            encounter = owner;
            detectionRange = range;
            viewCosine = Mathf.Cos(fieldOfView * .5f * Mathf.Deg2Rad);
            leash = limit;
            searchTime = searchDuration;
            marker = sensor;
            statusStripe = stripe;
            statusTint = new MaterialPropertyBlock();
        }

        private void Start()
        {
            agent = GetComponent<NavMeshAgent>();
            motor = GetComponent<DroneMotor>();
            body = GetComponent<DroneBody>();
            health = GetComponent<Health>();
            var visuals = GetComponentInChildren<DroneRotorVisuals>();
            weapon = visuals?.WeaponMount;
            effects = visuals?.Effects;
            playerHealth = player != null ? player.GetComponent<Health>() : null;
            if (source == null || playerHealth == null || agent == null || !agent.isOnNavMesh ||
                motor == null || body == null || health == null || patrol == null || patrol.Length < 2)
            {
                Debug.LogWarning("Outdoor Scout AI could not initialize: check player, Scout components, NavMesh and patrol posts.", this);
                enabled = false;
                return;
            }
            route = new NavMeshPath();
            if (weapon == null || effects == null)
                Debug.LogWarning("Outdoor Scout: the prefab weapon mount or shot effects are missing.", this);
            source.Configure(name, patrol[0]);
            source.InitializeDifficulty();
            if (source.DifficultyProfile != null) motor.InitializeMovement(source.MovementSettings);
            nextDecision = Time.time + source.PrototypeDecisionOffset;
            health.Died += OnDied;
            Debug.Log($"Outdoor Scout AI initialized with player '{player.name}', {detectionRange:0.#} m detection, and {patrol.Length} posts. " +
                $"Starting patrol toward Post {PatrolPost}.", this);
        }

        private void Update()
        {
            if (!health.IsAlive || Time.time < nextDecision) return;
            nextDecision = Time.time + (source.IsAggressive ? .085f : .14f) + source.PrototypeDecisionOffset * .11f;
            if (!agent.isOnNavMesh)
            {
                WarnNavigation("lost its NavMesh binding");
                return;
            }
            Vector3 origin = transform.position + body.EnvelopeCenter;
            Vector3 target = player.transform.position;
            bool canPursue = player.isActiveAndEnabled && playerHealth.IsAlive && encounter.AllowsTarget(target) &&
                (!engaged || WithinLeash(target));
            float distance = Vector3.Distance(origin, player.EyePosition);
            Vector3 heading = HeadingToPlayer();
            bool inView = heading.sqrMagnitude < .01f || Vector3.Dot(transform.forward, heading.normalized) >= viewCosine;
            bool visible = canPursue && distance <= detectionRange && inView && PlayerHit(origin, out _);
            if (visible)
            {
                source.ConfirmPrototypeSight();
                if (!engaged)
                {
                    engaged = true;
                    contactOrigin = transform.position;
                    attackAllowedAt = Time.time + source.ReactionSeconds(.65f);
                    SetAlertStripe(true);
                    encounter.PublishAlert(this, target);
                    Debug.Log($"{name} detected {player.name} at {distance:0.0} m.", this);
                }
                lastSeen = target;
                lastSeenAt = Time.time;
                haveSearchGoal = false;
                searchStep = 0;
                nextSearchStep = 0f;
                aimPoint = player.EyePosition;
                if (Time.time < attackAllowedAt)
                {
                    State = Activity.Alert;
                    if (distance <= AttackRange) Hold();
                    else
                    {
                        motor.SetMovementMode(DroneMovementMode.Chase);
                        Travel(lastSeen);
                    }
                    return;
                }
                if (distance <= AttackRange && source.ReadyForPrototypeShot && TryAttack()) return;
                State = Activity.Chase;
                motor.SetMovementMode(DroneMovementMode.Chase);
                Travel(lastSeen);
                return;
            }

            aimPoint = null;
            if (engaged && playerHealth.IsAlive && Time.time - lastSeenAt <= searchTime)
            {
                State = Activity.Investigate;
                motor.SetMovementMode(DroneMovementMode.Investigate);
                SearchLastSighting();
                return;
            }
            if (engaged)
            {
                engaged = false;
                State = Activity.ReturnToPatrol;
                reportedCover = reportedDamage = false;
                post = ClosestPost();
                SetAlertStripe(false);
            }
            if (!engaged && playerHealth.IsAlive && Time.time < investigateSoundUntil && player.isActiveAndEnabled)
            {
                State = Activity.Investigate;
                motor.SetMovementMode(DroneMovementMode.Investigate);
                Travel(heardPosition);
                return;
            }
            if (State == Activity.Investigate)
            {
                State = Activity.ReturnToPatrol;
                post = ClosestPost();
            }

            motor.SetMovementMode(DroneMovementMode.Patrol);
            if (Vector3.Distance(transform.position, patrol[post]) <= agent.stoppingDistance + .35f)
            {
                State = Activity.Patrol;
                post = (post + 1) % patrol.Length;
            }
            Travel(patrol[post]);
        }

        private bool WithinLeash(Vector3 point)
            => OutdoorScoutEncounter.FlatDistanceSquared(point, contactOrigin) <= leash * leash;

        private void SearchLastSighting()
        {
            if (Time.time >= nextSearchStep && (!haveSearchGoal ||
                Vector3.Distance(transform.position, searchGoal) < agent.stoppingDistance + .45f))
            {
                nextSearchStep = Time.time + .5f;
                haveSearchGoal = false;
                while (searchStep < SearchOffsets.Length)
                {
                    Vector3 probe = lastSeen + SearchOffsets[searchStep++];
                    if (!encounter.AllowsTarget(probe) || !WithinLeash(probe) ||
                        !NavMesh.SamplePosition(probe, out NavMeshHit hit, 1f, agent.areaMask)) continue;
                    if (!encounter.AllowsTarget(hit.position) || !WithinLeash(hit.position) || !body.CanOccupy(hit.position)) continue;
                    searchGoal = hit.position;
                    haveSearchGoal = true;
                    break;
                }
            }
            if (haveSearchGoal && !Travel(searchGoal) && !hasDestination) haveSearchGoal = false;
            else if (!haveSearchGoal) Hold();
        }

        internal void HearPlayerGunfire(Vector3 position)
        {
            if (health == null || !health.IsAlive || engaged || !encounter.AllowsTarget(position)) return;
            contactOrigin = transform.position;
            heardPosition = position;
            investigateSoundUntil = Time.time + 2.2f;
        }

        private void SetAlertStripe(bool alert)
        {
            if (statusStripe == null) return;
            statusTint.Clear();
            if (alert) statusTint.SetColor(TintColor, AlertColor);
            statusStripe.SetPropertyBlock(alert ? statusTint : null);
        }

        private bool TryAttack()
        {
            if (weapon == null || effects == null) return false;
            Vector3 heading = HeadingToPlayer();
            if (heading.sqrMagnitude > .01f && Vector3.Angle(transform.forward, heading) > FacingTolerance)
            {
                State = Activity.Attack;
                Hold();
                return true;
            }

            weapon.AimAt(aimPoint);
            Vector3 muzzle = weapon.MuzzlePosition;
            // Check both ends of the barrel so a protruding muzzle cannot bypass solid cover.
            if (!ClearWeaponLine(weapon.PivotPosition, out _, out string obstruction) ||
                !ClearWeaponLine(muzzle, out RaycastHit hit, out obstruction))
            {
                if (!reportedCover)
                {
                    Debug.Log($"Outdoor Scout held fire ({obstruction}); approaching for a clear shot.", this);
                    reportedCover = true;
                }
                return false;
            }

            State = Activity.Attack;
            Hold();
            if (Time.time < nextShot || !encounter.CanFire(health) || !encounter.TryClaimShot()) return true;
            nextShot = Time.time + ShotInterval;
            float damage = source.ShotDamage * .55f;
            float before = playerHealth.Current;
            if (float.IsFinite(damage) && damage > 0f)
            {
                encounter.RecordFire(health);
                playerHealth.ApplyDamage(damage, source, "Drone shot: Outdoor Scout");
                source.PublishAcceptedShot(muzzle, hit.point);
                SoundEvents.ReportNoise(muzzle, NoiseCategory.Gunshot, 1f, source);
            }
            if (!reportedDamage)
            {
                if (playerHealth.Current < before)
                    Debug.Log($"Outdoor Scout hit {player.name}: HP {before:0.#} → {playerHealth.Current:0.#}.", this);
                else
                    Debug.LogWarning($"Outdoor Scout had a clear shot but {player.name} lost no health (damage {damage}).", this);
                reportedDamage = true;
            }
            return true;
        }

        private void LateUpdate()
        {
            if (health == null || !health.IsAlive) return;
            if ((State == Activity.Alert || State == Activity.Attack || State == Activity.Chase) && aimPoint.HasValue)
            {
                Vector3 heading = HeadingToPlayer();
                if (heading.sqrMagnitude > .01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading),
                        agent.angularSpeed * Time.deltaTime);
            }
            weapon?.AimAt(aimPoint);
        }

        private Vector3 HeadingToPlayer()
            => Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);

        private bool ClearWeaponLine(Vector3 origin, out RaycastHit hit, out string obstruction)
        {
            hit = default;
            foreach (Collider solid in Physics.OverlapSphere(origin, .025f, WeaponController.GameplayRaycastMask,
                QueryTriggerInteraction.Ignore))
            {
                if (solid.transform.IsChildOf(transform)) continue;
                obstruction = $"weapon overlaps {solid.name}";
                return false;
            }
            if (PlayerHit(origin, out hit) && hit.distance <= AttackRange)
            {
                obstruction = null;
                return true;
            }
            obstruction = hit.collider != null ? $"first hit is {hit.collider.name}, or outside attack range" : "no player hitbox in the shot path";
            return false;
        }

        private bool PlayerHit(Vector3 origin, out RaycastHit playerHit)
        {
            // The muzzle may point through the Scout's own hull when aiming backward.
            Vector3 ray = player.EyePosition - origin;
            float nearest = ray.magnitude + .05f;
            playerHit = default;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, ray.normalized, nearest,
                WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= nearest || hit.transform.IsChildOf(transform)) continue;
                nearest = hit.distance;
                playerHit = hit;
            }
            return playerHit.collider != null && playerHit.collider.GetComponentInParent<PlayerController>() == player;
        }

        private bool Travel(Vector3 target)
        {
            if (hasDestination && (target - destination).sqrMagnitude < .36f &&
                (agent.hasPath || Vector3.Distance(transform.position, target) < agent.stoppingDistance + .3f)) return true;
            if (Time.time < nextRouteAttempt) return true;
            nextRouteAttempt = Time.time + (source.IsAggressive ? .25f : .35f);
            if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 1.25f, agent.areaMask) ||
                !agent.CalculatePath(hit.position, route) || route.status != NavMeshPathStatus.PathComplete || !RouteAllowed())
            {
                Hold();
                if (!engaged) WarnNavigation($"cannot reach patrol post {PatrolPost} at {target}");
                return false;
            }
            destination = target;
            hasDestination = agent.SetPath(route);
            agent.isStopped = !hasDestination;
            if (hasDestination) warnedAboutNavigation = false;
            else if (!engaged) WarnNavigation($"could not follow the path to patrol post {PatrolPost}");
            return hasDestination;
        }

        private bool RouteAllowed()
        {
            foreach (Vector3 point in route.corners)
                if (!encounter.InsidePerimeter(point) ||
                    ((engaged || State == Activity.Investigate) &&
                        (!WithinLeash(point) || !encounter.AllowsTarget(point)))) return false;
            return true;
        }

        private void WarnNavigation(string problem)
        {
            if (warnedAboutNavigation) return;
            warnedAboutNavigation = true;
            Debug.LogWarning($"Outdoor Scout navigation: {problem}.", this);
        }

        private void Hold()
        {
            hasDestination = false;
            agent.isStopped = true;
            if (agent.hasPath) agent.ResetPath();
        }

        private int ClosestPost()
        {
            int best = 0;
            for (int i = 1; i < patrol.Length; i++)
                if ((patrol[i] - transform.position).sqrMagnitude < (patrol[best] - transform.position).sqrMagnitude) best = i;
            return best;
        }

        private void OnDied(Health _)
        {
            State = Activity.Destroyed;
            engaged = false;
            aimPoint = null;
            if (marker != null) marker.gameObject.SetActive(false);
            motor.Stop();
            agent.enabled = false;
            foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        }

        private void OnDestroy()
        {
            if (health != null) health.Died -= OnDied;
        }
    }
}

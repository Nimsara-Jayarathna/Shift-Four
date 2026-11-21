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
        public enum Activity { Patrol, Chase, Attack, ReturnToPatrol, Destroyed }

        private const float AttackRange = 8f;
        private const float ShotInterval = 1.05f;
        private const float SearchTime = 2.5f;
        private const float FacingTolerance = 12f;
        private ScoutBrain source;
        private PlayerController player;
        private Vector3[] patrol;
        private Transform marker;
        private Vector3 patrolCenter;
        private float detectionRange, leash;
        private NavMeshAgent agent;
        private NavMeshPath route;
        private DroneMotor motor;
        private DroneBody body;
        private DroneWeaponMount weapon;
        private DroneWeaponEffects effects;
        private Health health, playerHealth;
        private Vector3 lastSeen, destination;
        private Vector3? aimPoint;
        private bool hasDestination;
        private bool warnedAboutNavigation;
        private bool seesPlayer, reportedSight, reportedCover, reportedDamage;
        private float nextDecision, nextShot, lastSeenAt = float.NegativeInfinity;
        private int post = 1;

        public Activity State { get; private set; }
        public int PatrolPost => post + 1;

        internal void Configure(ScoutBrain prefabBrain, PlayerController target, Vector3[] posts,
            Vector3 center, float range, float limit, Transform nameplate)
        {
            source = prefabBrain;
            player = target;
            patrol = posts;
            patrolCenter = center;
            detectionRange = range;
            leash = limit;
            marker = nameplate;
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
            source.Configure("Outdoor Scout", patrol[0]);
            source.InitializeDifficulty();
            if (source.DifficultyProfile != null) motor.InitializeMovement(source.DifficultySettings);
            health.Died += OnDied;
            Debug.Log($"Outdoor Scout AI initialized with player '{player.name}', {detectionRange:0.#} m detection, and {patrol.Length} posts. " +
                $"Starting patrol toward Post {PatrolPost}.", this);
        }

        private void Update()
        {
            if (!health.IsAlive || Time.time < nextDecision) return;
            nextDecision = Time.time + .18f;
            if (!agent.isOnNavMesh)
            {
                WarnNavigation("lost its NavMesh binding");
                return;
            }
            Vector3 origin = transform.TransformPoint(body.EnvelopeCenter);
            Vector3 offset = player != null ? player.transform.position - patrolCenter : Vector3.zero;
            offset.y = 0f;
            bool hasTarget = player != null && player.isActiveAndEnabled && playerHealth != null && playerHealth.IsAlive
                && offset.sqrMagnitude <= leash * leash;
            float distance = hasTarget ? Vector3.Distance(origin, player.EyePosition) : float.PositiveInfinity;
            bool inRange = distance <= detectionRange;
            RaycastHit sightHit = default;
            bool visible = inRange && PlayerHit(origin, out sightHit);
            if (inRange && !visible && !reportedSight)
                Debug.Log(sightHit.collider != null ? $"Outdoor Scout sight blocked by {sightHit.collider.name}." :
                    "Outdoor Scout: no player hitbox on the sight ray.", this);
            reportedSight = inRange && !visible;
            if (visible)
            {
                if (!seesPlayer)
                    Debug.Log($"Outdoor Scout detected {player.name} at {distance:0.0} m.", this);
                lastSeen = player.transform.position;
                lastSeenAt = Time.time;
                aimPoint = player.EyePosition;
                seesPlayer = true;
                if (distance <= AttackRange && TryAttack()) return;
            }
            else
            {
                aimPoint = null;
                seesPlayer = false;
            }

            if (hasTarget && Time.time - lastSeenAt <= SearchTime)
            {
                State = Activity.Chase;
                motor.SetMovementMode(DroneMovementMode.Chase);
                Travel(lastSeen);
                return;
            }
            if (State == Activity.Chase || State == Activity.Attack)
            {
                State = Activity.ReturnToPatrol;
                reportedCover = reportedDamage = false;
                post = ClosestPost();
                Debug.Log("Outdoor Scout lost the player and is returning to patrol.", this);
            }
            motor.SetMovementMode(DroneMovementMode.Patrol);
            if (Vector3.Distance(transform.position, patrol[post]) <= agent.stoppingDistance + .35f)
            {
                State = Activity.Patrol;
                post = (post + 1) % patrol.Length;
            }
            Travel(patrol[post]);
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
            if (Time.time < nextShot) return true;
            nextShot = Time.time + ShotInterval;
            float damage = source.ShotDamage;
            float before = playerHealth.Current;
            if (float.IsFinite(damage) && damage > 0f)
            {
                playerHealth.ApplyDamage(damage, source, "Drone shot: Outdoor Scout");
                effects.Fire(muzzle, hit.point, .11f, .13f);
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
            if (State == Activity.Attack && aimPoint.HasValue && player != null)
            {
                Vector3 heading = HeadingToPlayer();
                if (heading.sqrMagnitude > .01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading),
                        agent.angularSpeed * Time.deltaTime);
            }
            weapon?.AimAt(aimPoint);
            if (marker != null && player != null && player.ViewCamera != null)
            {
                Vector3 facing = marker.position - player.ViewCamera.transform.position;
                if (facing.sqrMagnitude > .001f) marker.rotation = Quaternion.LookRotation(facing);
            }
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

        private void Travel(Vector3 target)
        {
            if (hasDestination && (target - destination).sqrMagnitude < .36f && agent.hasPath) return;
            if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 1.25f, agent.areaMask) ||
                !agent.CalculatePath(hit.position, route) || route.status != NavMeshPathStatus.PathComplete)
            {
                Hold();
                if (State != Activity.Chase) WarnNavigation($"cannot reach patrol post {PatrolPost} at {target}");
                return;
            }
            destination = target;
            hasDestination = agent.SetPath(route);
            agent.isStopped = !hasDestination;
            if (hasDestination) warnedAboutNavigation = false;
            else if (State != Activity.Chase) WarnNavigation($"could not follow the path to patrol post {PatrolPost}");
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

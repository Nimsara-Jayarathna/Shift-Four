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
        private ScoutBrain source;
        private PlayerController player;
        private Vector3[] patrol;
        private Transform marker;
        private Vector3 checkpoint;
        private float detectionRange, leash;
        private NavMeshAgent agent;
        private NavMeshPath route;
        private DroneMotor motor;
        private DroneBody body;
        private DroneWeaponMount weapon;
        private Health health, playerHealth;
        private Vector3 lastSeen, destination;
        private Vector3? aimPoint;
        private bool hasDestination;
        private bool warnedAboutNavigation;
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
            checkpoint = center;
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
            weapon = GetComponentInChildren<DroneRotorVisuals>()?.WeaponMount;
            playerHealth = player != null ? player.GetComponent<Health>() : null;
            if (source == null || playerHealth == null || agent == null || !agent.isOnNavMesh ||
                motor == null || body == null || health == null || patrol == null || patrol.Length < 2)
            {
                Debug.LogWarning("Outdoor Scout AI could not initialize: check player, Scout components, NavMesh and patrol posts.", this);
                enabled = false;
                return;
            }
            route = new NavMeshPath();
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
            bool hasTarget = player != null && playerHealth != null && playerHealth.IsAlive
                && (player.transform.position - checkpoint).sqrMagnitude <= leash * leash;
            float distance = hasTarget ? Vector3.Distance(origin, player.EyePosition) : float.PositiveInfinity;
            if (distance <= detectionRange &&
                PlayerHit(origin, out RaycastHit hit))
            {
                lastSeen = player.transform.position;
                lastSeenAt = Time.time;
                aimPoint = player.EyePosition;
                weapon?.AimAt(aimPoint);
                if (distance <= AttackRange)
                {
                    State = Activity.Attack;
                    Hold();
                    if (Time.time >= nextShot && (weapon == null || PlayerHit(weapon.MuzzlePosition, out _)))
                    {
                        nextShot = Time.time + ShotInterval;
                        playerHealth.ApplyDamage(source.ShotDamage, source, "Drone shot: Outdoor Scout");
                        SoundEvents.ReportNoise(weapon != null ? weapon.MuzzlePosition : hit.point, NoiseCategory.Gunshot, 1f, source);
                    }
                    return;
                }
            }
            else aimPoint = null;

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

        private void LateUpdate()
        {
            if (health == null || !health.IsAlive) return;
            weapon?.AimAt(aimPoint);
            if (marker != null && player != null && player.ViewCamera != null)
            {
                Vector3 facing = marker.position - player.ViewCamera.transform.position;
                if (facing.sqrMagnitude > .001f) marker.rotation = Quaternion.LookRotation(facing);
            }
        }

        private bool PlayerHit(Vector3 origin, out RaycastHit playerHit)
        {
            // The muzzle may point through the Scout's own hull when aiming backward.
            Vector3 ray = player.EyePosition - origin;
            float nearest = ray.magnitude;
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

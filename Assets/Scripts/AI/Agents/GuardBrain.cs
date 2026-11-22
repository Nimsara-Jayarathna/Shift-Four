using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    // Member 3: defend authored Server cover and investigate local, uncertain disturbances.
    public sealed class GuardBrain : DroneBrain
    {
        public enum IndoorActivity { Patrol, Investigate, Defend, Attack, Return }

        protected override bool OwnsSoundInvestigation => true;
        [SerializeField] private Vector3[] coverPoints;
        [SerializeField, Tooltip("Root destination bounds around Home; default stays inside MainLab Server walls/gates.")]
        private Vector2 protectedHalfExtents = new Vector2(8.5f, 8.5f);
        [SerializeField, Min(.1f)] private float coverCommitSeconds = 1.5f;
        [SerializeField, Min(0f)] private float coverSwitchAdvantage = 6f;
        [SerializeField, Range(.01f, 1f)] private float minimumDisturbanceStrength = .15f;
        [SerializeField, Min(2.8f)] private float defensiveSpacing = 3f;
        private readonly RaycastHit[] protectionHits = new RaycastHit[32];
        private NavMeshPath validationPath;
        private NavMeshAgent agent;
        private DroneBody body;
        private bool haveCover, coverWasProtected, havePost, haveDisturbance;
        private float disturbanceAt;
        private Vector3 disturbanceEstimate, disturbanceGoal;
        private Vector3 defensivePost, postAnchor;
        private static readonly Vector3[] PostDirections = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            new Vector3(1f,0f,1f).normalized, new Vector3(-1f,0f,1f).normalized,
            new Vector3(1f,0f,-1f).normalized, new Vector3(-1f,0f,-1f).normalized };
        private static readonly float[] PostRadii = { 1.5f, 2.5f, 4f };
        private Vector3 committedCover;
        private float coverCommittedAt;
        private DroneMovementMode decisionMode;
        private IndoorGuardZone indoorZone;
        private int indoorSlot;
        private NavMeshQueryFilter indoorFilter;
        private float nextApproach;
        private float aimReadyAt;
        private bool aiming;
        private Vector3 indoorGoal, indoorThreat;
        private Vector3 securityPost;
        private Vector3 returnPost;
        private float fieldOfViewCosine;
        [field: SerializeField] public IndoorActivity IndoorState { get; private set; }
        private static readonly float[] ApproachAngles = { 0f, 36f, -36f, 72f, -72f };
        private static readonly float[] ApproachDistances = { 4.8f, 8f };

        protected override string GeneratedPatrolLabel => indoorZone == null ? base.GeneratedPatrolLabel : "Patrol indoor post ";
        protected override float VisionDistance => indoorZone?.Detection ?? base.VisionDistance;
        protected override float AttackRange => indoorZone?.Attack ?? base.AttackRange;
        protected override float ShotInterval => indoorZone?.Cooldown ?? base.ShotInterval;
        protected override bool CanTarget(Vector3 position) => indoorZone == null || indoorZone.CanTrack(position);
        internal override bool CanReactToGunfire(Vector3 position) => indoorZone != null && indoorZone.CanTrack(position);
        protected override bool TryAuthorizeShot() => indoorZone == null ||
            (Time.time >= aimReadyAt && indoorZone.TryClaimShot(indoorSlot));

        private void Awake()
        {
            validationPath = new NavMeshPath(); agent = GetComponent<NavMeshAgent>(); body = GetComponent<DroneBody>();
            ValidateSettings();
        }
        private void OnValidate() => ValidateSettings();
        private void ValidateSettings()
        {
            protectedHalfExtents.x = Bound(protectedHalfExtents.x, 8.5f, .5f, 20f);
            protectedHalfExtents.y = Bound(protectedHalfExtents.y, 8.5f, .5f, 20f);
            coverCommitSeconds = Bound(coverCommitSeconds, 1.5f, .1f, 5f);
            coverSwitchAdvantage = Bound(coverSwitchAdvantage, 6f, 0f, 30f);
            minimumDisturbanceStrength = Bound(minimumDisturbanceStrength, .15f, .01f, 1f);
            defensiveSpacing = Bound(defensiveSpacing, 3f, 2.8f, 5f);
        }
        private static float Bound(float value, float fallback, float low, float high)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, low, high);

        public void SetCover(Vector3 first, Vector3 second)
        { coverPoints = new[] { first, second }; haveCover = false; }

        internal void ConfigureZone(IndoorGuardZone zone, int slot, float fieldOfView)
        {
            indoorZone = zone;
            indoorSlot = slot;
            securityPost = zone.SecurityPost(slot);
            fieldOfViewCosine = Mathf.Cos(fieldOfView * .5f * Mathf.Deg2Rad);
            var template = GetComponent<NavMeshAgent>();
            indoorFilter = new NavMeshQueryFilter { agentTypeID = template.agentTypeID, areaMask = template.areaMask };
        }

        protected override bool InDetectionArc(Vector3 direction)
        {
            if (indoorZone == null) return true;
            Vector3 level = Vector3.ProjectOnPlane(direction, Vector3.up);
            return level.sqrMagnitude < .01f || Vector3.Dot(transform.forward, level.normalized) >= fieldOfViewCosine;
        }

        protected override bool ShouldResumePatrol(Vector3 targetPosition)
        {
            if (indoorZone == null) return base.ShouldResumePatrol(targetPosition);
            aiming = false;
            if ((indoorZone.CanTrack(targetPosition) && TryGetIndoorThreat(out _)) || indoorZone.TryGetScoutAlert(out _))
                return false;
            indoorZone.ReleaseApproach(indoorSlot);
            nextApproach = 0f;
            if (IndoorState != IndoorActivity.Patrol && IndoorState != IndoorActivity.Return)
                BeginIndoorReturn();
            if (IndoorState == IndoorActivity.Return && Vector3.Distance(transform.position, returnPost) > 1.45f)
            {
                return false;
            }
            IndoorState = IndoorActivity.Patrol;
            return true;
        }

        private void BeginIndoorReturn()
        {
            IndoorState = IndoorActivity.Return;
            Vector3[] patrol = indoorZone.Patrol(indoorSlot);
            returnPost = Home;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < patrol.Length; i++)
            {
                float distance = (transform.position - patrol[i]).sqrMagnitude;
                if (distance >= nearest || !Reachable(patrol[i])) continue;
                nearest = distance;
                returnPost = patrol[i];
                ResumePatrolAt(i);
            }
        }

        protected override DecisionOption Decide(bool visible, Vector3 playerPosition, float distance)
        {
            if (indoorZone != null) return DecideIndoors(visible, playerPosition, distance);
            decisionMode = DroneMovementMode.Patrol;
            if (!Alive || Awareness == null || !Awareness.IsEnabled) return Post();
            if (Awareness.State == DroneAwarenessState.ReturnToPost ||
                (!visible && Awareness.State == DroneAwarenessState.Patrol))
            { haveCover = false; return Post(); }

            Vector3 threat = default;
            bool haveThreat = visible || Awareness.TryGetLastSeen(Time.time, out threat);
            if (visible) threat = playerPosition;
            if (haveThreat)
            {
                decisionMode = visible ? DroneMovementMode.Chase : DroneMovementMode.Investigate;
                DecisionOption defense = ChooseCover(threat);
                // Cover takes priority even when the visible threat is close.
                if (haveCover) return defense;
                // Without cover, keep close-range pressure bounded and spaced.
                // Never send a hidden player root or a point outside Server to the motor.
                if (visible && distance < 4f && InProtectedArea(threat))
                {
                    Vector3 away = transform.position - threat; away.y = 0f;
                    if (away.sqrMagnitude < .01f) away = Home - threat;
                    if (away.sqrMagnitude < .01f) away = -transform.forward;
                    Vector3 point = threat + away.normalized * defensiveSpacing;
                    point.y = Home.y;
                    if (ValidDestination(point))
                    {
                        decisionMode = DroneMovementMode.Chase;
                        return Option("Pressure visible player", point, 75f);
                    }
                }
                return defense;
            }

            haveCover = false;
            var state = Awareness.State;
            if (Awareness.TryGetLastHeard(Time.time, out SoundObservation sound) &&
                (sound.Category == NoiseCategory.Gunshot || sound.Category == NoiseCategory.ConsoleInteraction) &&
                sound.Strength >= minimumDisturbanceStrength &&
                (state == DroneAwarenessState.Investigate || state == DroneAwarenessState.Search ||
                    (state == DroneAwarenessState.Suspicious && sound.Category == NoiseCategory.ConsoleInteraction)) &&
                TryDisturbanceGoal(sound, out Vector3 disturbance))
            {
                // A successful console can emit only once; credible local console noise may
                // warrant a cautious check without pretending it confirms a visual threat.
                decisionMode = DroneMovementMode.Investigate;
                return Option(state == DroneAwarenessState.Search ? "Search server disturbance" :
                    "Investigate server disturbance", disturbance, 35f);
            }
            return Post();
        }

        private DecisionOption DecideIndoors(bool visible, Vector3 playerPosition, float distance)
        {
            Vector3 threat = playerPosition;
            if (visible)
            {
                indoorZone.ShareSighting(threat);
                if (!aiming) aimReadyAt = Time.time + ReactionSeconds(.45f);
                aiming = true;
            }
            else if (!(indoorZone.CanTrack(playerPosition) && TryGetIndoorThreat(out threat)) &&
                !indoorZone.TryGetScoutAlert(out threat))
            {
                decisionMode = DroneMovementMode.Patrol;
                if (IndoorState != IndoorActivity.Return) BeginIndoorReturn();
                return Option("Return to protected post", returnPost, 10f);
            }

            decisionMode = visible ? DroneMovementMode.Chase : DroneMovementMode.Investigate;
            if (!visible && indoorZone.TryGetScoutAlert(out _)) ReactToThreat();
            float attackMargin = IndoorState == IndoorActivity.Attack ? .2f : 1f;
            bool defendingObjective = Vector3.Distance(transform.position, securityPost) < 7f;
            if (visible && distance >= 3.1f && distance <= AttackRange - attackMargin &&
                (defendingObjective || distance < 5f || IndoorState == IndoorActivity.Attack))
            {
                IndoorState = IndoorActivity.Attack;
                nextApproach = 0f;
                indoorZone.ReleaseApproach(indoorSlot);
                return Option("Hold and engage visible player", transform.position, 75f);
            }

            IndoorState = visible ? IndoorActivity.Defend : IndoorActivity.Investigate;
            if (Time.time >= nextApproach && ((indoorThreat - threat).sqrMagnitude > 1.3f * 1.3f ||
                indoorGoal == transform.position || !Motor.HasRoute || body.MotionBlocked))
            {
                nextApproach = Time.time + (IsAggressive ? .26f : .42f);
                indoorThreat = threat;
                indoorGoal = FindIndoorApproach(threat, visible);
            }
            if (indoorGoal != transform.position && !indoorZone.TryClaimApproach(indoorSlot, indoorGoal))
                indoorGoal = transform.position;
            return Option(visible ? "Pursue within security zone" : "Investigate disturbance in security zone",
                indoorGoal, 65f);
        }

        private bool TryGetIndoorThreat(out Vector3 point)
        {
            if (indoorZone.TryGetSighting(out point) || Awareness.TryGetLastSeen(Time.time, out point))
                return indoorZone.CanTrack(point);
            if (Awareness.TryGetLastHeard(Time.time, out SoundObservation sound) &&
                sound.Category == NoiseCategory.Gunshot && sound.Strength >= minimumDisturbanceStrength &&
                indoorZone.CanTrack(sound.Position))
            {
                point = sound.Position;
                return true;
            }
            return false;
        }

        private Vector3 FindIndoorApproach(Vector3 threat, bool visible)
        {
            Vector3 direction = Vector3.ProjectOnPlane(securityPost - threat, Vector3.up);
            if (direction.sqrMagnitude < .01f) direction = -transform.forward;
            Vector3 best = transform.position;
            float bestScore = float.NegativeInfinity;
            foreach (float radius in ApproachDistances)
            foreach (float angle in ApproachAngles)
            {
                Vector3 point = threat + Quaternion.Euler(0f, angle, 0f) * direction.normalized * radius;
                point.y = Home.y;
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, .65f, indoorFilter) ||
                    !indoorZone.CanTrack(hit.position) || !body.CanOccupy(hit.position)) continue;
                bool clearLane = !ProtectsFrom(hit.position, threat);
                float screen = Mathf.Max(0f, Vector3.Dot(
                    Vector3.ProjectOnPlane(hit.position - securityPost, Vector3.up).normalized, -direction.normalized));
                float score = (clearLane == visible ? 12f : 0f) -
                    Vector3.Distance(hit.position, securityPost) * 1.35f -
                    Vector3.Distance(hit.position, transform.position) * .45f -
                    Mathf.Abs(Vector3.Distance(hit.position, threat) - 5f) + screen * 3f;
                if (score <= bestScore || !Reachable(hit.position) ||
                    !indoorZone.TryClaimApproach(indoorSlot, hit.position)) continue;
                best = hit.position;
                bestScore = score;
            }
            if (float.IsNegativeInfinity(bestScore))
            {
                if (body.CanOccupy(securityPost) && Reachable(securityPost) &&
                    indoorZone.TryClaimApproach(indoorSlot, securityPost)) return securityPost;
                indoorZone.ReleaseApproach(indoorSlot);
            }
            return best;
        }

        // Guard destinations already include spacing and defensive route validation. The shared
        // combat offset helper must not move them to an unvalidated side of a wall or doorway.
        protected override void MoveToDecision(DecisionOption choice, bool visible)
        {
            Motor.SetMovementMode(decisionMode);
            if (visible && indoorZone != null) Motor.AimAt(Player.EyePosition);
            Motor.GoTo(choice.Destination);
        }

        private DecisionOption Post()
        {
            decisionMode = DroneMovementMode.Patrol;
            if (havePost && postAnchor == Home && ValidDestination(defensivePost))
                return Option("Guard server room", defensivePost, 10f);
            havePost = false;
            if (ValidDestination(Home)) { defensivePost = Home; havePost = true; }
            else
                foreach (float radius in PostRadii)
                {
                    foreach (Vector3 direction in PostDirections)
                    {
                        Vector3 candidate = Home + direction * radius;
                        if (!ValidDestination(candidate)) continue;
                        defensivePost = candidate; havePost = true; break;
                    }
                    if (havePost) break;
                }
            postAnchor = Home;
            return Option("Guard server room", havePost ? defensivePost : transform.position, 10f);
        }

        private DecisionOption ChooseCover(Vector3 threat)
        {
            float currentScore = float.NegativeInfinity; bool currentProtected = false;
            bool currentValid = haveCover && IsAuthoredCover(committedCover) &&
                TryScoreCover(committedCover, threat, out currentScore, out currentProtected);
            Vector3 bestPoint = default; float bestScore = float.NegativeInfinity; bool bestProtected = false;
            if (coverPoints != null)
                foreach (Vector3 point in coverPoints)
                    if (TryScoreCover(point, threat, out float score, out bool protection) && score > bestScore)
                    { bestPoint = point; bestScore = score; bestProtected = protection; }
            if (float.IsNegativeInfinity(bestScore)) { haveCover = false; return Post(); }
            bool lostProtection = currentValid && coverWasProtected && !currentProtected && bestProtected;
            if (!currentValid || lostProtection ||
                (Time.time - coverCommittedAt >= coverCommitSeconds && bestScore > currentScore + coverSwitchAdvantage))
            { committedCover = bestPoint; haveCover = true; coverCommittedAt = Time.time; currentProtected = bestProtected; currentScore = bestScore; }
            coverWasProtected = currentProtected;
            return Option(currentProtected ? "Take protected cover" : "Reposition", committedCover, currentScore);
        }

        private bool IsAuthoredCover(Vector3 point)
        {
            if (coverPoints == null) return false;
            foreach (Vector3 candidate in coverPoints) if (candidate == point) return true;
            return false;
        }
        private bool TryScoreCover(Vector3 point, Vector3 threat, out float score, out bool protection)
        {
            score = float.NegativeInfinity; protection = false;
            if (!ValidDestination(point) || !Finite(threat)) return false;
            protection = ProtectsFrom(point, threat);
            // Preserve the owner's original protection bonus and travel/threat distance weights.
            score = 40f + (protection ? 22f : 0f)
                - Vector3.Distance(transform.position, point) * .3f - Vector3.Distance(threat, point) * .1f;
            return true;
        }
        private bool ProtectsFrom(Vector3 point, Vector3 threat)
        {
            Vector3 origin = point + Vector3.up * 1.15f;
            Vector3 delta = threat + Vector3.up * 1.4f - origin;
            if (delta.sqrMagnitude < .0001f) return false;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, protectionHits, delta.magnitude, ~0,
                QueryTriggerInteraction.Ignore);
            if (count == protectionHits.Length) return false; // Crowded queries cannot assert protection.
            for (int i = 0; i < count; i++)
            {
                Collider hit = protectionHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform) || hit.GetComponentInParent<Health>() != null ||
                    hit.GetComponentInParent<ConsoleSwitch>() != null) continue;
                return true;
            }
            return false;
        }
        private bool TryDisturbanceGoal(SoundObservation sound, out Vector3 point)
        {
            point = default;
            if (!InProtectedArea(sound.Position)) return false;
            if (haveDisturbance && disturbanceAt == sound.EmittedAt && disturbanceEstimate == sound.Position &&
                ValidDestination(disturbanceGoal)) { point = disturbanceGoal; return true; }
            haveDisturbance = false;
            if (ValidDestination(sound.Position)) point = sound.Position;
            else
            {
                // The uncertain point can overlap a cabinet even when the sound anchor is
                // reachable. Try a bounded approach within the reported uncertainty area.
                bool found = false;
                foreach (Vector3 direction in PostDirections)
                {
                    Vector3 candidate = sound.Position + direction * Mathf.Min(sound.UncertaintyRadius, 1.75f);
                    if (!ValidDestination(candidate)) continue;
                    point = candidate; found = true; break;
                }
                if (!found) return false;
            }
            haveDisturbance = true; disturbanceAt = sound.EmittedAt;
            disturbanceEstimate = sound.Position; disturbanceGoal = point; return true;
        }
        private bool InProtectedArea(Vector3 point)
            => Finite(point) && Mathf.Abs(point.x - Home.x) <= protectedHalfExtents.x &&
                Mathf.Abs(point.z - Home.z) <= protectedHalfExtents.y && Mathf.Abs(point.y - Home.y) <= 1f;
        private bool ValidDestination(Vector3 point)
        {
            if (!InProtectedArea(point) || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh ||
                !NavMesh.SamplePosition(point, out NavMeshHit hit, .15f, agent.areaMask) ||
                (hit.position - point).sqrMagnitude > .15f * .15f || (body != null && !body.CanOccupy(point)) ||
                !NavMesh.CalculatePath(transform.position, point, agent.areaMask, validationPath) ||
                validationPath.status != NavMeshPathStatus.PathComplete) return false;
            foreach (Vector3 corner in validationPath.corners) if (!InProtectedArea(corner)) return false;
            WaypointGraph graph = NavigationGraph;
            if (graph == null) return false;
            var route = graph.FindRoute(transform.position, point);
            if (route.Count == 0) return false;
            Vector3 from = transform.position;
            foreach (Vector3 waypoint in route)
            {
                if (!InProtectedArea(waypoint) || !NavMesh.CalculatePath(from, waypoint, agent.areaMask, validationPath) ||
                    validationPath.status != NavMeshPathStatus.PathComplete) return false;
                foreach (Vector3 corner in validationPath.corners) if (!InProtectedArea(corner)) return false;
                from = waypoint;
            }
            return true;
        }
        private static bool Finite(Vector3 point) => !float.IsNaN(point.sqrMagnitude) && !float.IsInfinity(point.sqrMagnitude);
    }
}

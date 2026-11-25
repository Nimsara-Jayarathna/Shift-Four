using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    // Outdoor policy for the existing FlankerBrain, FlankerRoutes and DroneMotor.
    internal sealed class OutdoorFlankerAmbush
    {
        internal enum Phase { Waiting, Flanking, Warning, Attack, Reposition, Return, Destroyed }

        private static readonly float[] RearAngles = { 155f, -155f, 112f, -112f, 90f, -90f };
        private static readonly float[] SideAngles = { 100f, -100f, 140f, -140f, 166f, -166f };
        private readonly OutdoorFlankerAmbushes owner;
        private readonly int slot;
        private readonly NavMeshQueryFilter filter;
        private readonly NavMeshPath connection = new NavMeshPath();
        private Vector3 post, goal, targetAtPlan, movementAtPlan, lastPosition, lastBurstPosition, failedGoal;
        private Vector3 lastSeenPosition, lastSeenHeading;
        private Vector3 previousBurstPosition;
        private bool havePreviousBurst;
        private bool haveGoal, haveBurst, haveFailedGoal;
        private int shots, bursts;
        private float planAt, progressAt, lastSeenAt, warnUntil, recoverUntil, attackUntil, failedUntil, lastPlanSuccess;
        internal readonly Vector3 Center;
        internal readonly FlankerAmbushSettings Settings;
        internal Phase State { get; private set; }
        internal WaypointGraph Graph { get; private set; }
        internal Vector3[] Posts { get; private set; }
        internal float AttackRange => Settings.AttackRange * (slot == 2 ? 1.1f : 1f);
        internal float ShotDamage => Settings.Damage * (slot == 2 ? 1.15f : 1f);

        internal OutdoorFlankerAmbush(OutdoorFlankerAmbushes owner, Vector3 center, int slot,
            FlankerAmbushSettings settings, NavMeshQueryFilter filter)
        {
            this.owner = owner;
            Center = center;
            Settings = settings;
            this.slot = slot;
            this.filter = filter;
        }

        internal void SetPost(Vector3 position) => post = position;

        internal void SetGraph(WaypointGraph graph, Vector3 secondPost)
        {
            Graph = graph;
            Posts = new[] { post, secondPost };
        }

        private static float DistanceSquared(Vector3 a, Vector3 b) => OutdoorScoutEncounter.FlatDistanceSquared(a, b);
        private static Vector3 Flat(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);

        internal bool CanTravel(Vector3 point)
            => owner.IsOutdoorTarget(point) && DistanceSquared(point, Center) <= Settings.Pursuit * Settings.Pursuit;

        internal bool CanConnect(Vector3 start, Vector3 end)
        {
            if (!CanTravel(start) || !CanTravel(end) ||
                !NavMesh.CalculatePath(start, end, filter, connection) || connection.status != NavMeshPathStatus.PathComplete)
                return false;
            Vector3 from = start;
            foreach (Vector3 corner in connection.corners)
            {
                if (!CanTravel(corner)) return false;
                // Keep path legs out of protected pockets as well as checking their endpoints.
                int steps = Mathf.CeilToInt(Vector3.Distance(from, corner) / 2f);
                for (int i = 1; i < steps; i++)
                    if (!CanTravel(Vector3.Lerp(from, corner, (float)i / steps))) return false;
                from = corner;
            }
            return true;
        }

        internal bool CanObserve(FlankerBrain brain, Vector3 target)
        {
            if (State == Phase.Return || !owner.CanEngage(brain) || !CanTravel(target)) return false;
            float radius = State == Phase.Waiting ? Settings.Radius : Settings.Pursuit;
            return DistanceSquared(Center, target) <= radius * radius;
        }

        internal DecisionOption Decide(FlankerBrain brain, FlankerRoutes routes, bool visible,
            Vector3 target, Vector3 movement, Vector3 facing)
        {
            float now = Time.time;
            Vector3 position = brain.transform.position;
            if (State == Phase.Return)
            {
                if (DistanceSquared(position, post) < 1.35f * 1.35f) State = Phase.Waiting;
                return HoldOrReturn("Return to ambush post");
            }
            if (State == Phase.Waiting)
            {
                if (!visible || !owner.Claim(brain)) return HoldOrReturn("Waiting at ambush post");
                State = Phase.Flanking;
                bursts = 0;
                shots = 0;
                haveGoal = haveBurst = havePreviousBurst = haveFailedGoal = false;
                planAt = 0f;
                lastPlanSuccess = now;
            }
            if (visible)
            {
                lastSeenAt = now;
                lastSeenPosition = target;
                lastSeenHeading = movement.sqrMagnitude < .36f ? Flat(facing).normalized :
                    Vector3.Lerp(movement.normalized, Flat(facing).normalized, .25f).normalized;
            }
            if (!CanObserve(brain, target) || now - lastSeenAt > 3.2f)
            {
                return Disengage(brain);
            }

            if (State == Phase.Warning || State == Phase.Attack)
            {
                if (!visible || DistanceSquared(position, target) > AttackRange * AttackRange ||
                    (State == Phase.Attack && now >= attackUntil))
                {
                    BeginReposition(position, now);
                    return Option("Reposition after lost firing line", position);
                }
                if (State == Phase.Warning && now >= warnUntil)
                {
                    State = Phase.Attack;
                    attackUntil = now + 2f;
                }
                return Option(State == Phase.Warning ? "Flanker threat warning" : "Fire ambush burst", position);
            }

            if (!visible)
            {
                if (!haveGoal && now >= planAt)
                {
                    planAt = now + .72f;
                    haveGoal = TryFindGoal(routes, position, lastSeenPosition, lastSeenPosition, lastSeenHeading, now, out goal);
                }
                return Option("Investigate last sighting from flank", haveGoal ? goal : position);
            }
            if (haveGoal && DistanceSquared(position, lastPosition) >= .3f * .3f)
            {
                lastPosition = position;
                progressAt = now;
            }
            if (haveGoal && DistanceSquared(position, goal) > 1.45f * 1.45f && now - progressAt > 2.2f)
            {
                failedGoal = goal;
                failedUntil = now + 4f;
                haveFailedGoal = true;
                haveGoal = false;
            }

            if (now >= planAt && (!haveGoal || DistanceSquared(targetAtPlan, target) > 2.25f * 2.25f ||
                Vector3.Dot(movementAtPlan, lastSeenHeading) < .55f))
            {
                planAt = now + Settings.ReplanInterval * (brain.IsAggressive ? 1f : 1.3f);
                Vector3 lead = target + Vector3.ClampMagnitude(Flat(movement) * Settings.LeadSeconds, 3f);
                haveGoal = TryFindGoal(routes, position, target, lead, lastSeenHeading, now, out goal) ||
                    (lead != target && TryFindGoal(routes, position, target, target, lastSeenHeading, now, out goal));
                targetAtPlan = target;
                if (haveGoal)
                {
                    movementAtPlan = lastSeenHeading;
                    lastPosition = position;
                    progressAt = lastPlanSuccess = now;
                }
            }
            if (!haveGoal && now - lastPlanSuccess > 4f) return Disengage(brain);
            if (!haveGoal) return Option("Check for a reachable flank", position);

            if (DistanceSquared(position, goal) <= 1.45f * 1.45f && now >= recoverUntil &&
                DistanceSquared(position, target) <= (AttackRange - .4f) * (AttackRange - .4f) &&
                Vector3.Dot(Flat(position - target).normalized, lastSeenHeading) <= .35f)
            {
                State = Phase.Warning;
                shots = 0;
                warnUntil = now + Mathf.Max(.55f, brain.ReactionSeconds(slot == 2 ? .75f : .9f));
                return Option("Flanker threat warning", position);
            }
            return Option(State == Phase.Reposition ? "Reposition to a new flank" : "Approach player flank", goal);
        }

        private bool TryFindGoal(FlankerRoutes routes, Vector3 position, Vector3 target, Vector3 anchor, Vector3 heading,
            float now, out Vector3 best)
        {
            routes.Begin(post);
            var angles = slot == 1 ? SideAngles : RearAngles;
            float cost = float.PositiveInfinity;
            best = default;
            float radius = Mathf.Clamp(AttackRange * .7f, 3.5f, 8f);
            int sign = (slot + bursts) % 2 == 0 ? 1 : -1;
            for (int i = 0; i < angles.Length * 2; i++)
            {
                float distance = i < angles.Length ? radius : radius * .78f;
                Vector3 candidate = anchor + Quaternion.Euler(0f, angles[i % angles.Length] * sign, 0f) * heading * distance;
                candidate.y = post.y;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.15f, filter) ||
                    (haveFailedGoal && now < failedUntil && DistanceSquared(hit.position, failedGoal) < 2.3f * 2.3f) ||
                    (haveBurst && DistanceSquared(hit.position, lastBurstPosition) < 3.4f * 3.4f) ||
                    (havePreviousBurst && DistanceSquared(hit.position, previousBurstPosition) < 2.4f * 2.4f) ||
                    !CanTravel(hit.position) || !routes.ClearFiringLine(hit.position, target)) continue;

                float facingDot = Vector3.Dot(Flat(hit.position - target).normalized, heading);
                if (facingDot > .24f) continue;
                Vector3[] path = routes.NativeRoute(position, hit.position);
                if (!FlankerPolicy.Valid(path)) continue;
                float rating = FlankerPolicy.Length(path) + i * 1.7f +
                    Mathf.Abs(Vector3.Distance(hit.position, target) - radius) * .6f +
                    (slot == 1 ? Mathf.Abs(facingDot) : facingDot + 1f) * 4f;
                if (rating >= cost) continue;
                cost = rating;
                best = hit.position;
            }
            return float.IsFinite(cost);
        }

        internal bool AuthorizeShot(Vector3 position, Vector3 forward, Vector3 target)
        {
            if (State != Phase.Attack || Vector3.Dot(forward, Flat(target - position).normalized) < .96f)
                return false;
            if (++shots >= (slot == 2 ? 3 : 2)) BeginReposition(position, Time.time);
            return true;
        }

        private void BeginReposition(Vector3 position, float now)
        {
            State = Phase.Reposition;
            previousBurstPosition = lastBurstPosition;
            havePreviousBurst = haveBurst;
            lastBurstPosition = position;
            haveBurst = true;
            haveGoal = false;
            lastPlanSuccess = now;
            bursts++;
            recoverUntil = now + Settings.Recovery * (slot == 2 ? .88f : 1f);
        }

        internal void Release(FlankerBrain brain) => owner.Release(brain);

        internal void OnDefeated(FlankerBrain brain)
        {
            State = Phase.Destroyed;
            owner.Release(brain);
        }

        private DecisionOption Disengage(FlankerBrain brain)
        {
            State = Phase.Return;
            owner.Release(brain);
            return HoldOrReturn("Disengage and return to ambush post");
        }

        private DecisionOption HoldOrReturn(string label) => Option(label, post);
        private static DecisionOption Option(string label, Vector3 destination) => new DecisionOption(label, destination, 50f);
    }
}

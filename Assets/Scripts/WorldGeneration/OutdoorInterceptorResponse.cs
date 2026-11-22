using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // Owns the outdoor dispatch lifecycle. Prediction, flight and fire remain on the original Interceptor.
    internal sealed class OutdoorInterceptorResponse
    {
        internal enum Phase { Standby, Alerted, Intercept, Attack, Search, Return, Destroyed }

        internal const float AttackDistance = 10f;
        private static readonly float[] ApproachAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        private static readonly float[] ApproachRadii = { 4f, 6.5f };
        private readonly OutdoorScoutEncounter outdoors;
        private readonly NavMeshQueryFilter filter;
        private readonly NavMeshPath connection = new NavMeshPath();
        private readonly Vector3 station;
        private readonly List<Vector3> roads = new List<Vector3>();
        private readonly float pursuitTime, pursuitDistance;
        private Vector3 lastKnown, goal, motion;
        private float alertEnds, responseEnds, reportExpires, planAt, searchEnds, burstReadyAt;
        private int fired, searchStep;
        private bool seen, ahead;

        internal Phase State { get; private set; }
        internal bool IsResponding => State is Phase.Alerted or Phase.Intercept or Phase.Attack or Phase.Search;
        internal WaypointGraph Graph { get; private set; }
        internal Vector3[] Posts { get; }

        internal OutdoorInterceptorResponse(OutdoorScoutEncounter outdoors, NavMeshQueryFilter filter, Vector3 station,
            List<Vector3> roadPosts, float pursuitTime, float pursuitDistance)
        {
            this.outdoors = outdoors;
            this.filter = filter;
            this.station = station;
            this.pursuitTime = pursuitTime;
            this.pursuitDistance = pursuitDistance;
            Posts = new[] { station, station };
            foreach (Vector3 point in roadPosts)
                if (Allows(point)) roads.Add(point);
        }

        internal void BuildGraph(Transform parent)
        {
            Transform root = Group("Response Navigation", parent);
            Graph = root.gameObject.AddComponent<WaypointGraph>();
            Graph.Configure(new[] { parent }, System.Array.Empty<GraphEdge>());
            Graph.SetConnectionValidator(CanConnect);
        }

        private static float DistanceSquared(Vector3 a, Vector3 b) => OutdoorScoutEncounter.FlatDistanceSquared(a, b);
        private static Vector3 Flat(Vector3 point) => Vector3.ProjectOnPlane(point, Vector3.up);

        private bool Allows(Vector3 point)
            => outdoors.AllowsTarget(point) && DistanceSquared(station, point) <= pursuitDistance * pursuitDistance;

        internal bool CanSee(Vector3 playerPosition) => IsResponding && Allows(playerPosition);

        private bool CanConnect(Vector3 start, Vector3 end)
        {
            if (!Allows(start) || !Allows(end) ||
                !NavMesh.CalculatePath(start, end, filter, connection) || connection.status != NavMeshPathStatus.PathComplete)
                return false;
            Vector3 from = start;
            foreach (Vector3 corner in connection.corners)
            {
                if (!Allows(corner)) return false;
                int steps = Mathf.CeilToInt(Vector3.Distance(from, corner) / 2f);
                for (int i = 1; i < steps; i++)
                    if (!Allows(Vector3.Lerp(from, corner, (float)i / steps))) return false;
                from = corner;
            }
            return true;
        }

        internal bool TryPlanDispatch(Vector3 origin, Vector3 report, ControlTacticalRouteValidator routes, float speed,
            out Vector3 destination, out float distance)
        {
            destination = default;
            distance = float.PositiveInfinity;
            if (State != Phase.Standby || !Allows(report)) return false;
            if (!FindStandoff(origin, report, routes, out destination, out distance)) return false;
            return distance / Mathf.Max(1f, speed) + 2f < pursuitTime;
        }

        internal void Dispatch(Vector3 report, Vector3 destination, float distance, float speed)
        {
            float now = Time.time;
            State = Phase.Alerted;
            goal = destination;
            lastKnown = report;
            alertEnds = now + .8f;
            responseEnds = now + pursuitTime;
            reportExpires = now + Mathf.Min(pursuitTime, Mathf.Max(7f, distance / Mathf.Max(1f, speed) + 4f));
            burstReadyAt = now + 1f;
            planAt = searchEnds = 0f;
            fired = searchStep = 0;
            seen = ahead = false;
        }

        internal DecisionOption Decide(InterceptorBrain brain, InterceptorPolicy prediction, ControlTacticalRouteValidator routes,
            bool visible, Vector3 playerPosition, Vector3 observedMotion, float speed, Vector3 ownVelocity, float acceleration)
        {
            Vector3 position = brain.transform.position;
            float now = Time.time;
            if (State == Phase.Return || State == Phase.Standby)
            {
                if (DistanceSquared(position, station) < 1.45f * 1.45f) State = Phase.Standby;
                return Option(State == Phase.Standby ? "Standby" : "Return to station", station);
            }
            if (now >= responseEnds || (!seen && now >= reportExpires))
                return Return();

            if (visible)
            {
                lastKnown = playerPosition;
                seen = true;
            }
            if (now < alertEnds) return Option("Alerted and moving to reported area", goal);

            if (visible)
            {
                searchEnds = 0f;
                if (now >= planAt)
                {
                    planAt = now + .36f;
                    PlanIntercept(prediction, routes, position, playerPosition, observedMotion, speed, ownVelocity, acceleration);
                }
                float distance = DistanceSquared(position, playerPosition);
                if (distance <= AttackDistance * AttackDistance &&
                    ((!ahead || distance <= 5.5f * 5.5f) || fired > 0))
                {
                    State = Phase.Attack;
                    return Option("Face player and fire response burst", position);
                }

                State = Phase.Intercept;
                return Option(ahead ? "Intercept ahead on road" : "Pursue last known area", goal);
            }

            if (!seen && State != Phase.Search && DistanceSquared(position, goal) > 1.5f * 1.5f)
            {
                State = Phase.Intercept;
                return Option("Approach Scout sighting", goal);
            }

            if (State != Phase.Search)
            {
                State = Phase.Search;
                if (fired > 0) burstReadyAt = now + 2.2f;
                fired = 0;
                searchStep = 0;
                searchEnds = now + 3.8f;
                planAt = 0f;
            }
            if (now >= searchEnds) return Return();
            if (now >= planAt)
            {
                planAt = now + 1.1f;
                // Search only positions around the last actual sighting or the original Scout report.
                Vector3 focus = lastKnown + Quaternion.Euler(0f, searchStep++ * 90f, 0f) * Vector3.forward * 2.4f;
                goal = FindStandoff(position, focus, routes, out Vector3 search, out _) ? search : position;
            }
            return Option("Search last known area", goal);
        }

        private void PlanIntercept(InterceptorPolicy policy, ControlTacticalRouteValidator routes, Vector3 position,
            Vector3 playerPosition, Vector3 observedMotion, float speed, Vector3 ownVelocity, float acceleration)
        {
            motion = Flat(observedMotion);
            routes.BeginDecision();
            try
            {
                DecisionOption prediction = policy.StepWithMotion(Time.time, true, playerPosition, position, station,
                    speed, ownVelocity, acceleration, Graph.Version, roads, routes.Route,
                    (from, to) => PossibleRoad(from, to) ? routes.PlayerRoute(from, to) : null);
                ahead = policy.State == InterceptorState.InterceptAhead &&
                    InterceptorPolicy.ValidRoute(policy.PlannedRoute);
                if (ahead)
                    goal = prediction.Destination;
                else if (FindStandoff(position, playerPosition, routes, out Vector3 approach, out _))
                    goal = approach;
                else
                    goal = position;
            }
            finally { routes.EndDecision(); }
        }

        private bool PossibleRoad(Vector3 from, Vector3 to)
        {
            Vector3 offset = Flat(to - from);
            return offset.sqrMagnitude < 26f * 26f && (motion.sqrMagnitude < .3f ||
                Vector3.Dot(offset.normalized, motion.normalized) > .35f);
        }

        private bool FindStandoff(Vector3 origin, Vector3 location, ControlTacticalRouteValidator validator,
            out Vector3 destination, out float distance)
        {
            destination = default;
            distance = float.PositiveInfinity;
            Vector3 toward = Flat(origin - location).normalized;
            if (toward.sqrMagnitude < .01f) toward = Vector3.forward;
            foreach (float radius in ApproachRadii)
            foreach (float angle in ApproachAngles)
            {
                Vector3 candidate = location + Quaternion.Euler(0f, angle, 0f) * toward * radius;
                candidate.y = station.y;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1f, filter) || !Allows(hit.position)) continue;
                Vector3[] route = validator.Route(origin, hit.position);
                if (!InterceptorPolicy.ValidRoute(route)) continue;
                float length = InterceptorPolicy.Length(route);
                if (length >= distance) continue;
                distance = length;
                destination = hit.position;
            }
            return float.IsFinite(distance);
        }

        internal bool AuthorizeShot(Vector3 position, Vector3 forward, Vector3 playerPosition)
        {
            if (State != Phase.Attack || Time.time < burstReadyAt ||
                Vector3.Dot(forward, Flat(playerPosition - position).normalized) < .96f) return false;
            if (++fired < 2) return true;
            fired = 0;
            burstReadyAt = Time.time + 2.2f;
            return true;
        }

        internal void Defeat() => State = Phase.Destroyed;

        private DecisionOption Return()
        {
            State = Phase.Return;
            return Option("Threat expired, return to station", station);
        }

        private static DecisionOption Option(string label, Vector3 point) => new DecisionOption(label, point, 50f);
    }
}

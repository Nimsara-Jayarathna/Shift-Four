using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    /// <summary>
    /// IT24103464 - Flanker checkpoint 2.
    /// Extends the tactical candidates with actual A* route cost, firing-line
    /// quality and route-aware fallbacks.
    /// </summary>
    public sealed class FlankerBrain : DroneBrain
    {
        [Header("Target memory")]
        [SerializeField, Min(0.5f)] private float memorySeconds = 6f;

        [Header("Candidate generation")]
        [SerializeField, Min(2f)] private float flankRadius = 5f;
        [SerializeField, Min(0.5f)] private float navMeshSnapRadius = 2.2f;
        [SerializeField, Min(1f)] private float preferredAttackDistance = 5f;

        [Header("Utility weights")]
        [SerializeField, Min(0f)] private float lateralWeight = 30f;
        [SerializeField, Min(0f)] private float firingLineWeight = 14f;
        [SerializeField, Min(0f)] private float rangeWeight = 12f;
        [SerializeField, Min(0f)] private float routeCostWeight = 1.15f;

        private readonly List<Candidate> candidates = new List<Candidate>(4);

        private struct Candidate
        {
            public string Name;
            public Vector3 Destination;
            public float Score;
            public float RouteCost;
            public float LateralQuality;
            public float RangeQuality;
            public bool ClearFiringLine;
            public bool Valid;

            public DecisionOption ToOption()
                => new DecisionOption(Name, Destination, Score);
        }

        protected override DecisionOption Decide(bool visible, Vector3 playerPosition, float distance)
        {
            bool remembersTarget = visible || Time.time - LastSeenAt <= memorySeconds;
            Vector3 target = visible ? playerPosition : LastKnown;

            candidates.Clear();

            if (!remembersTarget)
                return new DecisionOption("Hold storage", Home, 18f);

            if (visible && distance <= 3.5f)
                return new DecisionOption("Engage close target", target, 95f);

            Vector3 fromAgentToTarget = target - transform.position;
            fromAgentToTarget.y = 0f;
            if (fromAgentToTarget.sqrMagnitude < 0.05f)
                fromAgentToTarget = transform.forward.sqrMagnitude > 0.05f
                    ? transform.forward
                    : Vector3.forward;

            Vector3 direct = fromAgentToTarget.normalized;
            Vector3 lateral = new Vector3(-direct.z, 0f, direct.x);

            candidates.Add(EvaluateFlankCandidate(
                "Flank left",
                target + lateral * flankRadius,
                target));

            candidates.Add(EvaluateFlankCandidate(
                "Flank right",
                target - lateral * flankRadius,
                target));

            candidates.Add(EvaluateFallbackRoute(
                visible ? "Pursue visible target" : "Pursue last sighting",
                target,
                visible ? 48f : 36f));

            candidates.Add(EvaluateFallbackRoute("Hold storage", Home, 16f));

            return BestValidCandidate(candidates).ToOption();
        }

        private Candidate EvaluateFlankCandidate(string label, Vector3 rawPosition, Vector3 target)
        {
            Candidate result = new Candidate
            {
                Name = label,
                Destination = rawPosition,
                Score = float.NegativeInfinity,
                Valid = false
            };

            if (!TrySnapToNavMesh(rawPosition, out Vector3 snapped))
                return result;

            result.Destination = snapped;
            if (!TryMeasureRoute(snapped, out float routeCost))
                return result;

            result.RouteCost = routeCost;
            result.LateralQuality = LateralQuality(snapped, target);
            result.RangeQuality = RangeQuality(snapped, target);
            result.ClearFiringLine = HasClearLineToRememberedTarget(snapped, target);

            result.Score = 42f
                + result.LateralQuality * lateralWeight
                + result.RangeQuality * rangeWeight
                + (result.ClearFiringLine ? firingLineWeight : 0f)
                - result.RouteCost * routeCostWeight;

            result.Valid = true;
            return result;
        }

        private Candidate EvaluateFallbackRoute(string label, Vector3 destination, float baseScore)
        {
            Candidate result = new Candidate
            {
                Name = label,
                Destination = destination,
                Score = float.NegativeInfinity,
                Valid = false
            };

            if (!TrySnapToNavMesh(destination, out Vector3 snapped))
                return result;

            result.Destination = snapped;
            if (!TryMeasureRoute(snapped, out float routeCost))
                return result;

            result.RouteCost = routeCost;
            result.Score = baseScore - routeCost * 0.55f;
            result.Valid = true;
            return result;
        }

        private Candidate BestValidCandidate(List<Candidate> options)
        {
            Candidate best = new Candidate
            {
                Name = "Hold storage",
                Destination = Home,
                Score = 8f,
                Valid = true
            };

            for (int i = 0; i < options.Count; i++)
            {
                Candidate option = options[i];
                if (option.Valid && option.Score > best.Score)
                    best = option;
            }

            return best;
        }

        private bool TrySnapToNavMesh(Vector3 point, out Vector3 snapped)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, navMeshSnapRadius, NavMesh.AllAreas))
            {
                snapped = hit.position;
                return true;
            }

            snapped = point;
            return false;
        }

        private bool TryMeasureRoute(Vector3 destination, out float routeCost)
        {
            routeCost = 0f;
            WaypointGraph graph = GameSession.Instance != null ? GameSession.Instance.Graph : null;
            if (graph == null)
                return false;

            List<Vector3> route = graph.FindRoute(transform.position, destination);
            if (route == null || route.Count == 0)
                return false;

            Vector3 previous = transform.position;
            for (int i = 0; i < route.Count; i++)
            {
                routeCost += Vector3.Distance(previous, route[i]);
                previous = route[i];
            }

            return true;
        }

        private float LateralQuality(Vector3 candidate, Vector3 target)
        {
            Vector3 originalFromTarget = transform.position - target;
            Vector3 candidateFromTarget = candidate - target;
            originalFromTarget.y = 0f;
            candidateFromTarget.y = 0f;

            if (originalFromTarget.sqrMagnitude < 0.05f || candidateFromTarget.sqrMagnitude < 0.05f)
                return 0f;

            float angle = Vector3.Angle(originalFromTarget, candidateFromTarget);
            return Mathf.Clamp01(1f - Mathf.Abs(angle - 90f) / 90f);
        }

        private float RangeQuality(Vector3 candidate, Vector3 target)
        {
            float candidateDistance = Vector3.Distance(candidate, target);
            float error = Mathf.Abs(candidateDistance - preferredAttackDistance);
            return Mathf.Clamp01(1f - error / Mathf.Max(preferredAttackDistance, 0.1f));
        }

        private bool HasClearLineToRememberedTarget(Vector3 candidate, Vector3 target)
        {
            Vector3 origin = candidate + Vector3.up * 1.25f;
            Vector3 destination = target + Vector3.up * 1f;
            Vector3 vector = destination - origin;
            float distance = vector.magnitude;
            if (distance < 0.1f)
                return true;

            return !Physics.Raycast(
                origin,
                vector.normalized,
                distance - 0.15f,
                ~0,
                QueryTriggerInteraction.Ignore);
        }
    }
}

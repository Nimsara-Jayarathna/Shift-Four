using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    /// <summary>
    /// IT24103464 - Flanker checkpoint 1.
    /// Builds left/right tactical candidates from legitimate target memory,
    /// snaps them to the NavMesh and scores side/range quality.
    /// </summary>
    public sealed class FlankerBrain : DroneBrain
    {
        [Header("Target memory")]
        [SerializeField, Min(0.5f)] private float memorySeconds = 6f;

        [Header("Candidate generation")]
        [SerializeField, Min(2f)] private float flankRadius = 5f;
        [SerializeField, Min(0.5f)] private float navMeshSnapRadius = 2.2f;
        [SerializeField, Min(1f)] private float preferredAttackDistance = 5f;

        [Header("Checkpoint 1 utility weights")]
        [SerializeField, Min(0f)] private float lateralWeight = 30f;
        [SerializeField, Min(0f)] private float rangeWeight = 12f;
        [SerializeField, Min(0f)] private float travelPenaltyPerMetre = 0.35f;

        protected override DecisionOption Decide(bool visible, Vector3 playerPosition, float distance)
        {
            bool remembersTarget = visible || Time.time - LastSeenAt <= memorySeconds;
            Vector3 target = visible ? playerPosition : LastKnown;

            if (!remembersTarget)
                return Option("Hold storage", Home, 18f);

            if (visible && distance <= 3.5f)
                return Option("Engage close target", target, 95f);

            Vector3 fromAgentToTarget = target - transform.position;
            fromAgentToTarget.y = 0f;
            if (fromAgentToTarget.sqrMagnitude < 0.05f)
                fromAgentToTarget = transform.forward.sqrMagnitude > 0.05f
                    ? transform.forward
                    : Vector3.forward;

            Vector3 direct = fromAgentToTarget.normalized;
            Vector3 lateral = new Vector3(-direct.z, 0f, direct.x);

            DecisionOption left = EvaluateFlank(
                "Flank left",
                target + lateral * flankRadius,
                target);

            DecisionOption right = EvaluateFlank(
                "Flank right",
                target - lateral * flankRadius,
                target);

            DecisionOption pursue = Option(
                visible ? "Pursue visible target" : "Pursue last sighting",
                target,
                visible ? 48f : 36f);

            return Best(
                Option("Hold storage", Home, 16f),
                pursue,
                left,
                right);
        }

        private DecisionOption EvaluateFlank(string label, Vector3 rawPosition, Vector3 target)
        {
            if (!NavMesh.SamplePosition(
                rawPosition,
                out NavMeshHit hit,
                navMeshSnapRadius,
                NavMesh.AllAreas))
            {
                return Option(label, rawPosition, -1f);
            }

            Vector3 snapped = hit.position;
            if (!Reachable(snapped))
                return Option(label, snapped, -1f);

            float lateralQuality = LateralQuality(snapped, target);
            float rangeQuality = RangeQuality(snapped, target);
            float travelDistance = Vector3.Distance(transform.position, snapped);

            float score = 42f
                + lateralQuality * lateralWeight
                + rangeQuality * rangeWeight
                - travelDistance * travelPenaltyPerMetre;

            return Option(label, snapped, score);
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
    }
}

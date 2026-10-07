using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    /// <summary>
    /// IT24103464 - Flanker IS agent.
    ///
    /// The Flanker does not simply chase the player. It generates tactical candidate
    /// positions on both sides of the latest observed player position, validates them
    /// against the NavMesh and shared A* graph, assigns a utility score, then commits
    /// to the best useful action for a short period so it does not oscillate every
    /// decision tick.
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
        [Tooltip("Reward for approaching the target from roughly 90 degrees to the current direct line.")]
        [SerializeField, Min(0f)] private float lateralWeight = 30f;
        [Tooltip("Reward for a candidate that would have an unobstructed firing line to the remembered target point.")]
        [SerializeField, Min(0f)] private float firingLineWeight = 14f;
        [Tooltip("Reward for ending near the desired attack distance.")]
        [SerializeField, Min(0f)] private float rangeWeight = 12f;
        [Tooltip("Penalty applied per metre of A* route length.")]
        [SerializeField, Min(0f)] private float routeCostWeight = 1.15f;

        [Header("Decision stability")]
        [Tooltip("Minimum time a valid flank choice is kept before normal switching is allowed.")]
        [SerializeField, Min(0f)] private float minimumCommitSeconds = 1.4f;
        [Tooltip("A new option must beat the current option by this score after the commit window.")]
        [SerializeField, Min(0f)] private float switchMargin = 6f;

        [Header("Viva / diagnostics")]
        [SerializeField] private bool logDecisionChanges = true;
        [SerializeField] private bool drawCandidateGizmos = true;

        private readonly List<Candidate> candidates = new List<Candidate>(5);

        private string committedName = string.Empty;
        private Vector3 committedDestination;
        private float committedScore = float.NegativeInfinity;
        private float commitUntil;
        private string lastLoggedDecision = string.Empty;

        private Vector3 lastLeftCandidate;
        private Vector3 lastRightCandidate;
        private bool lastLeftValid;
        private bool lastRightValid;

        public string DecisionTrace { get; private set; } = "No decision yet";

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

            // No recent information: return to our assigned storage post instead of
            // pretending the agent knows the player's hidden live position.
            if (!remembersTarget)
            {
                Candidate hold = BuildSimpleCandidate("Hold storage", Home, 18f);
                return CommitOrKeep(hold, true).ToOption();
            }

            // A visible player already inside close combat range is an emergency
            // override. Flanking at this distance would look irrational.
            if (visible && distance <= 3.5f)
            {
                Candidate engage = BuildSimpleCandidate("Engage close target", target, 95f);
                return CommitOrKeep(engage, true).ToOption();
            }

            Vector3 fromAgentToTarget = target - transform.position;
            fromAgentToTarget.y = 0f;
            if (fromAgentToTarget.sqrMagnitude < 0.05f)
                fromAgentToTarget = transform.forward.sqrMagnitude > 0.05f
                    ? transform.forward
                    : Vector3.forward;

            Vector3 direct = fromAgentToTarget.normalized;
            Vector3 lateral = new Vector3(-direct.z, 0f, direct.x);

            Candidate left = EvaluateFlankCandidate("Flank left", target + lateral * flankRadius, target);
            Candidate right = EvaluateFlankCandidate("Flank right", target - lateral * flankRadius, target);
            candidates.Add(left);
            candidates.Add(right);

            lastLeftCandidate = left.Destination;
            lastRightCandidate = right.Destination;
            lastLeftValid = left.Valid;
            lastRightValid = right.Valid;

            // Pursuit is deliberately weaker than a healthy flank, but remains a
            // sensible fallback when shelves, a door state, or graph connectivity
            // makes both lateral positions unavailable.
            Candidate pursue = EvaluateFallbackRoute(
                visible ? "Pursue visible target" : "Pursue last sighting",
                target,
                visible ? 48f : 36f);
            candidates.Add(pursue);

            Candidate holdStorage = EvaluateFallbackRoute("Hold storage", Home, 16f);
            candidates.Add(holdStorage);

            Candidate best = BestValidCandidate(candidates);
            return CommitOrKeep(best, false).ToOption();
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

            // Utility score = useful tactical properties minus travel expense.
            // Keeping the components explicit makes the decision explainable in viva.
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

        private Candidate BuildSimpleCandidate(string label, Vector3 destination, float score)
        {
            if (TrySnapToNavMesh(destination, out Vector3 snapped))
                destination = snapped;

            return new Candidate
            {
                Name = label,
                Destination = destination,
                Score = score,
                RouteCost = 0f,
                Valid = true
            };
        }

        private Candidate BestValidCandidate(List<Candidate> options)
        {
            Candidate best = BuildSimpleCandidate("Hold storage", Home, 8f);
            for (int i = 0; i < options.Count; i++)
            {
                Candidate option = options[i];
                if (option.Valid && option.Score > best.Score)
                    best = option;
            }
            return best;
        }

        private Candidate CommitOrKeep(Candidate best, bool forceSwitch)
        {
            bool committedStillReachable = !string.IsNullOrEmpty(committedName)
                && TryMeasureRoute(committedDestination, out _);

            Candidate selected = best;
            bool keepExisting = false;

            if (!forceSwitch && committedStillReachable)
            {
                if (Time.time < commitUntil)
                {
                    keepExisting = true;
                }
                else if (TryFindCandidate(committedName, out Candidate currentCandidate)
                    && currentCandidate.Valid
                    && best.Score < currentCandidate.Score + switchMargin)
                {
                    keepExisting = true;
                    committedScore = currentCandidate.Score;
                    committedDestination = currentCandidate.Destination;
                }
            }

            if (keepExisting)
            {
                selected = new Candidate
                {
                    Name = committedName,
                    Destination = committedDestination,
                    Score = committedScore,
                    Valid = true
                };
            }
            else
            {
                committedName = best.Name;
                committedDestination = best.Destination;
                committedScore = best.Score;
                commitUntil = Time.time + minimumCommitSeconds;
            }

            UpdateDiagnostics(selected);
            return selected;
        }

        private bool TryFindCandidate(string name, out Candidate candidate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Name == name)
                {
                    candidate = candidates[i];
                    return true;
                }
            }

            candidate = default;
            return false;
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
            // 90 degrees is a true side approach. 0 or 180 is not useful flanking.
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

            // This ray is aimed at the observed/remembered target point, not at the
            // player's hidden live Transform, so it does not grant wall-hack knowledge.
            return !Physics.Raycast(origin, vector.normalized, distance - 0.15f,
                ~0, QueryTriggerInteraction.Ignore);
        }

        private void UpdateDiagnostics(Candidate selected)
        {
            string left = CandidateSummary("L", "Flank left");
            string right = CandidateSummary("R", "Flank right");
            string pursue = CandidateSummary("P", "Pursue visible target");
            if (pursue.EndsWith("invalid"))
                pursue = CandidateSummary("P", "Pursue last sighting");

            DecisionTrace = $"{selected.Name} ({selected.Score:0.0}) | {left} | {right} | {pursue}";

            if (logDecisionChanges && selected.Name != lastLoggedDecision)
            {
                Debug.Log($"[Flanker:{AgentName}] {DecisionTrace}", this);
                lastLoggedDecision = selected.Name;
            }
        }

        private string CandidateSummary(string shortName, string candidateName)
        {
            if (!TryFindCandidate(candidateName, out Candidate candidate) || !candidate.Valid)
                return $"{shortName}:invalid";
            return $"{shortName}:{candidate.Score:0.0}/path:{candidate.RouteCost:0.0}";
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawCandidateGizmos)
                return;

            if (lastLeftValid)
            {
                Gizmos.DrawWireSphere(lastLeftCandidate + Vector3.up * 0.2f, 0.45f);
                Gizmos.DrawLine(transform.position, lastLeftCandidate);
            }

            if (lastRightValid)
            {
                Gizmos.DrawWireSphere(lastRightCandidate + Vector3.up * 0.2f, 0.45f);
                Gizmos.DrawLine(transform.position, lastRightCandidate);
            }
        }
    }
}

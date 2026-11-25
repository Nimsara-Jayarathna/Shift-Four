using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace ShiftFour
{
    [Serializable]
    internal sealed class InterceptorLearningSettings
    {
        public bool enabled = true;
        [Range(.05f, .20f)] public float learningRate = .12f;
        [Range(.1f, .5f)] public float timingTolerance = .3f;
        [Range(1f, 10f)] public float errorScale = 3f;
        [Range(.01f, .2f)] public float decayRate = .05f;
    }

    internal enum InterceptorOutcome
    {
        SuccessfulPrediction, FailedPrediction, Unevaluated, MissedArrivalWindow, InvalidDestination,
        InvalidGraphRoute, IncompleteNavMeshRoute, PhysicalObstruction, StaleObservation,
        FailedSearch, InsufficientInspection, SuccessfulReacquisition, AssignmentConflict
    }

    internal sealed class InterceptorTacticalMemory
    {
        internal readonly struct Entry
        {
            internal readonly int Decision, Graph, Route, SourceUnit;
            internal readonly Vector3 Destination;
            internal readonly float Time, Expires, Error;
            internal readonly InterceptorOutcome Outcome;
            internal readonly InterceptorPredictionEvaluator.Result? Evaluation;

            internal Entry(InterceptorOutcome outcome, Vector3 destination, float time, float expires,
                int graph, int route, int sourceUnit, int decision, float error, InterceptorPredictionEvaluator.Result? evaluation)
            {
                Outcome = outcome; Destination = destination; Time = time; Expires = expires;
                Graph = graph; Route = route; SourceUnit = sourceUnit; Decision = decision; Error = error; Evaluation = evaluation;
            }
        }

        private static readonly ProfilerMarker Marker = new("ShiftFour.TacticalMemory");
        private readonly List<Entry> entries = new(64);
        internal IReadOnlyList<Entry> Entries => entries;

        internal void Expire(float now)
        {
            using var sample = Marker.Auto();
            RemoveExpired(now);
        }

        private void RemoveExpired(float now)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i].Expires <= now) entries.RemoveAt(i);
        }

        internal bool Add(InterceptorOutcome outcome, Vector3 destination, float now, int graph,
            int route = 0, int sourceUnit = 0, int decision = 0, float lifetime = 30f, float error = float.NaN, InterceptorPredictionEvaluator.Result? evaluation = null)
        {
            using var sample = Marker.Auto();
            RemoveExpired(now);
            if (!float.IsFinite(destination.sqrMagnitude) || !float.IsFinite(now)) return false;
            if (decision == 0)
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    var previous = entries[i];
                    if (Mathf.Abs(now - previous.Time) >= 1f) continue;
                    if (previous.Outcome == outcome && previous.Graph == graph && previous.Route == route &&
                        previous.SourceUnit == sourceUnit && Near(previous.Destination, destination)) return false;
                }
            if (entries.Count == 64) entries.RemoveAt(0);
            entries.Add(new Entry(outcome, destination, now, now + lifetime, graph, route, sourceUnit, decision, error, evaluation));
            return true;
        }

        internal bool Cooling(Vector3 point, int graph, int route, float now, float cooldown, float observedAt)
        {
            foreach (var entry in entries)
            {
                if (entry.Graph != graph || now >= entry.Expires || now >= entry.Time + cooldown ||
                    !Near(point, entry.Destination)) continue;
                if (entry.Outcome == InterceptorOutcome.MissedArrivalWindow && observedAt <= entry.Time) return true;
                if (IsNavigation(entry.Outcome) && (entry.Route == 0 || entry.Route == route)) return true;
            }
            return false;
        }

        internal float SearchPenalty(Vector3 point, int graph, float now, float decay)
        {
            float penalty = 0f;
            foreach (var entry in entries)
                if (entry.Graph == graph && entry.Expires > now && Near(point, entry.Destination) &&
                    entry.Outcome is InterceptorOutcome.FailedSearch or InterceptorOutcome.InsufficientInspection)
                    penalty += .09f * Mathf.Exp(-decay * Mathf.Max(0f, now - entry.Time));
            return Mathf.Min(.18f, penalty);
        }

        internal static bool IsNavigation(InterceptorOutcome outcome) =>
            outcome is InterceptorOutcome.InvalidDestination or InterceptorOutcome.InvalidGraphRoute or
                InterceptorOutcome.IncompleteNavMeshRoute or InterceptorOutcome.PhysicalObstruction;

        internal static InterceptorOutcome Classify(string reason) => reason switch
        {
            "NoGraphRoute" => InterceptorOutcome.InvalidGraphRoute,
            "Navigation" => InterceptorOutcome.IncompleteNavMeshRoute,
            "PhysicalObstruction" => InterceptorOutcome.PhysicalObstruction,
            _ => InterceptorOutcome.InvalidDestination
        };

        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1f;

        // Omit the origin, which moves between replans. Preserve the approach bearing and
        // the ordered interior waypoints, quantized to tolerate small NavMesh sampling variations.
        internal static int RouteSignature(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 2) return 0;
            Vector3 heading = points[points.Count - 1] - points[points.Count - 2];
            int hash = 17 + Mathf.RoundToInt(Mathf.Atan2(heading.x, heading.z) * 4f / Mathf.PI);
            unchecked
            {
                for (int i = 1; i < points.Count - 1; i++)
                {
                    hash = hash * 31 + Mathf.RoundToInt(points[i].x);
                    hash = hash * 31 + Mathf.RoundToInt(points[i].z);
                }
            }
            return hash == 0 ? 1 : hash;
        }
    }
}

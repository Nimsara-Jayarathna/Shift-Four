using System;
using Unity.Profiling;
using UnityEngine;

namespace ShiftFour
{
    [Serializable]
    internal sealed class InterceptorDecisionSettings
    {
        [Range(8, 24)] public int beliefRegions = 16;
        [Range(.25f, .8f)] public float decisionInterval = .4f;
        [Range(2f, 7f)] public float searchSeconds = 4.5f;
        [Range(0f, 2f)] public float commitmentSeconds = .8f;
        [Range(0f, .3f)] public float switchingMargin = .10f;
        [Range(0f, .5f)] public float travelPenalty = .24f;
        [Range(0f, .5f)] public float uncertaintyPenalty = .14f;
        [Range(0f, .5f)] public float informationWeight = .26f;
        [Range(0f, .2f)] public float stabilityBonus = .05f;
    }

    internal enum InterceptorAction { Patrol, ObserveTarget, Intercept, PursueLastKnown, InvestigateSound,
        SearchBeliefRegion, Reposition, ReturnToPatrol, Attack }

    // One decision frame and one commitment. OutdoorInterceptorResponse still owns lifecycle states.
    internal sealed class InterceptorUtilityDecision
    {
        internal readonly struct Choice
        {
            internal readonly InterceptorAction Action;
            internal readonly Vector3 Destination;
            internal readonly float Utility, Evidence, Travel, Uncertainty, Stability;
            internal readonly string Reason;
            internal bool Eligible => float.IsFinite(Utility);

            internal Choice(InterceptorAction action, Vector3 destination, float utility, string reason,
                float evidence = 0f, float travel = 0f, float uncertainty = 0f, float stability = 0f)
            {
                Action = action;
                Destination = destination;
                Utility = utility;
                Reason = reason;
                Evidence = evidence;
                Travel = travel;
                Uncertainty = uncertainty;
                Stability = stability;
            }
        }

        private static readonly ProfilerMarker DecisionMarker = new("ShiftFour.Interceptor.Utility");
        private static readonly string[] NormalRejections =
        {
            "Response in progress", "No confirmed sight", "No feasible interception", "No validated recent evidence",
            "No reachable fresh sound", "No useful reachable region", "No clear reposition point",
            "Response still useful", "Sight, range, or burst timing"
        };
        private readonly Choice[] choices = new Choice[9];
        private readonly InterceptorDecisionSettings settings;
        private float commitUntil;
        private bool selected;
        private Choice incumbent;
        internal Choice Current { get; private set; }
        internal InterceptorAction Previous { get; private set; }
        internal string Transition { get; private set; } = "Initial decision";
        internal float Remaining(float now) => Mathf.Max(0f, commitUntil - now);
        internal System.Collections.Generic.IReadOnlyList<Choice> Choices => choices;

        internal InterceptorUtilityDecision(InterceptorDecisionSettings settings)
        {
            this.settings = settings;
            Begin();
        }

        internal void Begin()
        {
            incumbent = new Choice(default, default, float.NegativeInfinity, "Previous action unavailable");
            for (int i = 0; i < choices.Length; i++)
                choices[i] = new Choice((InterceptorAction)i, default, float.NegativeInfinity, NormalRejections[i]);
        }

        internal void Reject(InterceptorAction action, string reason)
        {
            choices[(int)action] = new Choice(action, default, float.NegativeInfinity, reason);
            if (incumbent.Action == action)
                incumbent = choices[(int)action];
        }

        internal void Offer(InterceptorAction action, Vector3 point, float value, float travel,
            float uncertainty, string reason)
        {
            if (!float.IsFinite(point.sqrMagnitude) || !float.IsFinite(value) ||
                !float.IsFinite(travel) || !float.IsFinite(uncertainty)) return;
            float stability = selected && SamePlan(Current, action, point) ? settings.stabilityBonus : 0f;
            float score = Score(value, travel, uncertainty, stability);
            var choice = new Choice(action, point, score, reason, value, travel, uncertainty, stability);
            if (choice.Utility >= choices[(int)action].Utility) choices[(int)action] = choice;
            if (selected && SamePlan(Current, action, point) && choice.Utility > incumbent.Utility) incumbent = choice;
        }

        internal float Score(float value, float travel, float uncertainty, float stability = 0f)
            => Mathf.Clamp01(value - settings.travelPenalty * Mathf.Clamp01(travel) -
                settings.uncertaintyPenalty * Mathf.Clamp01(uncertainty) + stability);

        internal Choice Select(float now, bool newVisual = false)
        {
            using var sample = DecisionMarker.Auto();
            Choice best = default;
            bool found = false;
            foreach (var choice in choices)
                if (choice.Eligible && (!found || choice.Utility > best.Utility)) { best = choice; found = true; }
            if (!found)
            {
                Previous = Current.Action;
                Current = choices[(int)InterceptorAction.ReturnToPatrol];
                Transition = "No eligible action, recover safely";
                selected = false;
                commitUntil = now;
                return Current;
            }
            bool valid = selected && incumbent.Eligible;
            string reason = !valid ? "Previous action unavailable or complete" : "Higher utility";
            // An immediate combat opportunity and fresh visual evidence can replace a search
            // commitment, but only after the caller has certified the new action's eligibility.
            if (valid && !newVisual && best.Action != InterceptorAction.Attack &&
                (now < commitUntil || best.Utility < incumbent.Utility + settings.switchingMargin))
            {
                best = incumbent;
                reason = "Commitment or switching margin";
            }
            if (!selected || !SamePlan(Current, best.Action, best.Destination))
            {
                Previous = Current.Action;
                commitUntil = now + settings.commitmentSeconds;
                Transition = reason;
            }
            Current = best;
            selected = true;
            return best;
        }

        private static bool SamePlan(Choice first, InterceptorAction action, Vector3 destination)
            => first.Action == action && (action is not (InterceptorAction.SearchBeliefRegion or InterceptorAction.InvestigateSound) ||
                (first.Destination - destination).sqrMagnitude < 1f);
    }
}

using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace ShiftFour
{
    internal readonly struct InterceptorEvidence
    {
        internal readonly InterceptorObservationSource Source;
        internal readonly Vector3 Position;
        internal readonly float Time, Expires, Radius, Reliability;
        internal readonly int Sender, Graph, Route;

        internal InterceptorEvidence(InterceptorObservationSource source, Vector3 position, float time, float expires,
            float radius, float reliability, int sender = 0)
            : this(source, position, time, expires, radius, reliability, sender, -1, 0) { }

        internal InterceptorEvidence(InterceptorObservationSource source, Vector3 position, float time, float expires,
            float radius, float reliability, int sender, int graph, int route)
        {
            Source = source;
            Position = position;
            Time = time;
            Expires = expires;
            Radius = radius;
            Reliability = reliability;
            Sender = sender;
            Graph = graph;
            Route = route;
        }

        internal bool Fresh(float now) => float.IsFinite(Time) && float.IsFinite(Expires) && float.IsFinite(Radius) &&
            float.IsFinite(Reliability) && Reliability > 0f && Radius > 0f && float.IsFinite(Position.sqrMagnitude) &&
            Time <= now && now < Expires;

        internal float Strength(float now) => Fresh(now) ?
            Mathf.Clamp01(Reliability) * Mathf.Exp(-.45f * (now - Time)) : 0f;
    }

    internal enum InterceptorInspection { Unexamined, Occluded, Inspected, Unreachable }

    internal sealed class InterceptorBeliefState
    {
        internal struct Region
        {
            internal int Id;
            internal Vector3 Position;
            internal float Probability, Radius;
            internal InterceptorEvidence Evidence;
            internal InterceptorHypothesisKind Hypothesis;
            internal InterceptorInspection Inspection;
            internal InterceptorHypothesis? Motion;
        }

        private static readonly ProfilerMarker UpdateMarker = new("ShiftFour.Interceptor.Belief");
        private static readonly Vector3[] Offsets = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        private readonly List<Region> regions = new(24);
        private readonly int limit;
        private int nextId;
        private float lastVisual = float.NegativeInfinity, lastSound = float.NegativeInfinity;
        private float propagatedAt;
        private bool spreadSighting;
        private InterceptorEvidence latestScout, latestSharedSight;
        internal IReadOnlyList<Region> Regions => regions;
        internal float Entropy { get; private set; }
        internal float LatestVisual => lastVisual;

        internal InterceptorBeliefState(int regionLimit) => limit = Mathf.Clamp(regionLimit, 8, 24);

        internal void Clear()
        {
            regions.Clear();
            Entropy = 0f;
        }

        internal void Observe(InterceptorEvidence sight, IReadOnlyList<InterceptorHypothesis> hypotheses)
        {
            using var sample = UpdateMarker.Auto();
            if (!sight.Fresh(sight.Time) || sight.Source != InterceptorObservationSource.DirectVisual || sight.Time <= lastVisual) return;
            lastVisual = propagatedAt = sight.Time;
            spreadSighting = false;
            regions.Clear();
            Add(sight, sight.Position, .72f, null);
            foreach (var hypothesis in hypotheses)
                if (hypothesis.Weight > 0f && hypothesis.Kind != InterceptorHypothesisKind.Stop)
                    Add(sight, sight.Position, .28f * hypothesis.Weight, hypothesis);
            Normalize();
        }

        internal bool Fuse(InterceptorEvidence evidence, float now, Func<Vector3, Vector3?> project,
            Func<Vector3, Vector3, Vector3[]> connection)
        {
            using var sample = UpdateMarker.Auto();
            if (evidence.Source == InterceptorObservationSource.InspectedRegion || !evidence.Fresh(now) || evidence.Time <= lastVisual || now - lastVisual < .75f) return false;
            bool sound = evidence.Source == InterceptorObservationSource.ApproximateSound;
            var previousEvidence = evidence.Source == InterceptorObservationSource.DirectVisual ? latestSharedSight : latestScout;
            if (sound ? evidence.Time <= lastSound : evidence.Time < previousEvidence.Time ||
                evidence.Time == previousEvidence.Time && evidence.Sender == previousEvidence.Sender) return false;

            Vector3? center = project(evidence.Position);
            if (!center.HasValue) return false;
            RemoveExpired(now);
            if (sound) lastSound = evidence.Time;
            else if (evidence.Source == InterceptorObservationSource.DirectVisual) latestSharedSight = evidence;
            else latestScout = evidence;
            float strength = evidence.Strength(now) * (evidence.Source == InterceptorObservationSource.DirectVisual ? .75f : 1f);
            // A small amount of proposal mass permits genuine new evidence to seed new regions.
            float birth = regions.Count > 0 ? .28f * strength : 1f;
            int previous = regions.Count;
            for (int i = 0; i < previous; i++)
            {
                Region item = regions[i];
                item.Probability *= 1f - birth;
                regions[i] = item;
            }
            Add(evidence, center.Value, birth, null);
            foreach (var offset in Offsets)
            {
                Vector3? point = project(center.Value + offset * Mathf.Clamp(evidence.Radius, 2f, 6f));
                if (!point.HasValue || !InterceptorPredictionModel.Complete(connection(center.Value, point.Value),
                    center.Value, point.Value)) continue;
                Add(evidence, point.Value, birth * .24f, null);
            }

            // Clutter mixture keeps the posterior defined when approximate evidence disagrees.
            for (int i = 0; i < regions.Count; i++)
            {
                Region item = regions[i];
                float variance = Mathf.Max(.25f, evidence.Radius * evidence.Radius + item.Radius * item.Radius * .25f);
                float likelihood = Mathf.Exp(-(item.Position - center.Value).sqrMagnitude / (2f * variance));
                item.Probability *= 1f - strength + strength * likelihood;
                regions[i] = item;
            }
            Normalize();
            return true;
        }

        internal void Propagate(float now, float speedLimit, float uncertaintyGrowth,
            Func<InterceptorHypothesis, float, Vector3> forecast, Func<Vector3, Vector3?> project,
            Func<Vector3, Vector3, Vector3[]> connection)
        {
            using var sample = UpdateMarker.Auto();
            RemoveExpired(now);
            float dt = Mathf.Max(0f, now - propagatedAt);
            propagatedAt = now;
            if (dt <= 0f) { Normalize(); return; }

            for (int i = 0; i < regions.Count; i++)
            {
                Region item = regions[i];
                item.Radius = Mathf.Min(10f, item.Evidence.Radius + uncertaintyGrowth * (now - item.Evidence.Time));
                float age = now - item.Evidence.Time;
                item.Probability *= Mathf.Exp(-dt * (.22f + .12f * age));
                // Each hypothesis transfers its own mass along its validated route. If the
                // next region is unreachable the mass stays put instead of crossing the obstacle.
                if (item.Motion is InterceptorHypothesis hypothesis && item.Inspection == InterceptorInspection.Unexamined)
                {
                    Vector3? target = project(forecast(hypothesis, age));
                    if (target.HasValue && (target.Value - item.Position).sqrMagnitude > .25f)
                    {
                        var leg = connection(item.Position, target.Value);
                        if (InterceptorPredictionModel.Complete(leg, item.Position, target.Value) &&
                            InterceptorPolicy.Length(leg) <= speedLimit * dt + .45f)
                            item.Position = target.Value;
                    }
                }
                regions[i] = item;
            }
            // With no visible movement, small reachable alternatives still become
            // plausible after loss of sight. Transfer mass once per genuine sighting.
            if (!spreadSighting && regions.Count > 0 && regions[0].Evidence.Source == InterceptorObservationSource.DirectVisual &&
                now - lastVisual >= .60f)
                SpreadSight(now, speedLimit, project, connection);
            Normalize();
        }

        private void SpreadSight(float now, float speedLimit, Func<Vector3, Vector3?> project,
            Func<Vector3, Vector3, Vector3[]> connection)
        {
            spreadSighting = true;
            Region origin = regions[0];
            if (origin.Inspection == InterceptorInspection.Inspected) return;
            float maximum = Mathf.Min(4.5f, (now - lastVisual) * speedLimit);
            float transfer = origin.Probability * .065f;
            foreach (var offset in Offsets)
            {
                Vector3? point = project(origin.Position + offset * (maximum * .85f));
                if (!point.HasValue || (point.Value - origin.Position).sqrMagnitude < 1f) continue;
                var route = connection(origin.Position, point.Value);
                if (!InterceptorPredictionModel.Complete(route, origin.Position, point.Value) ||
                    InterceptorPolicy.Length(route) > maximum) continue;
                if (Add(origin.Evidence, point.Value, transfer, null)) origin.Probability -= transfer;
            }
            // The parent is the strongest member and retains its slot under the bounded transfer.
            regions[0] = origin;
        }

        internal void Inspect(int index, bool covered)
        {
            Region region = regions[index];
            region.Inspection = covered ? InterceptorInspection.Inspected : InterceptorInspection.Occluded;
            if (covered) region.Probability *= .18f;
            regions[index] = region;
            if (covered)
                for (int i = 0; i < regions.Count; i++)
                {
                    Region other = regions[i];
                    if (i == index || other.Inspection == InterceptorInspection.Inspected ||
                        (other.Position - region.Position).sqrMagnitude > .04f || other.Radius > region.Radius) continue;
                    other.Probability *= .18f;
                    other.Inspection = InterceptorInspection.Inspected;
                    regions[i] = other;
                }
            Normalize();
        }

        internal void ApplyInspection(InterceptorEvidence inspection)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                if (region.Inspection == InterceptorInspection.Inspected || region.Evidence.Time > inspection.Time ||
                    Vector3.Distance(region.Position, inspection.Position) + region.Radius > inspection.Radius) continue;
                Inspect(i, true);
            }
        }

        internal void SetReachable(int index, bool reachable)
        {
            Region region = regions[index];
            if (region.Inspection is InterceptorInspection.Inspected or InterceptorInspection.Occluded) return;
            region.Inspection = reachable ? InterceptorInspection.Unexamined : InterceptorInspection.Unreachable;
            regions[index] = region;
        }

        private bool Add(InterceptorEvidence evidence, Vector3 point, float weight, InterceptorHypothesis? hypothesis)
        {
            if (regions.Count == limit)
            {
                int weakest = 0;
                for (int i = 1; i < regions.Count; i++)
                    if (regions[i].Probability < regions[weakest].Probability) weakest = i;
                if (weight <= regions[weakest].Probability) return false;
                regions.RemoveAt(weakest);
            }
            regions.Add(new Region { Id = ++nextId, Position = point, Probability = weight, Radius = evidence.Radius,
                Evidence = evidence, Motion = hypothesis, Hypothesis = hypothesis?.Kind ?? InterceptorHypothesisKind.Stop });
            return true;
        }

        private void RemoveExpired(float now)
        {
            for (int i = regions.Count - 1; i >= 0; i--)
                if (!regions[i].Evidence.Fresh(now)) regions.RemoveAt(i);
        }

        private void Normalize()
        {
            Entropy = 0f;
            float total = 0f;
            foreach (var region in regions) total += region.Probability;
            if (!float.IsFinite(total) || total <= 0f) { regions.Clear(); return; }
            for (int i = 0; i < regions.Count; i++)
            {
                Region region = regions[i];
                region.Probability /= total;
                Entropy -= region.Probability > 0f ? region.Probability * Mathf.Log(region.Probability) : 0f;
                regions[i] = region;
            }
        }
    }
}

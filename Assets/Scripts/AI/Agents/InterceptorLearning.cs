using System;
using Unity.Profiling;
using UnityEngine;

namespace ShiftFour
{
    // One instance per generated Interceptor, reused across responses and discarded with the world.
    internal sealed class InterceptorLearning
    {
        private const float Prior = .65f;
        private static readonly ProfilerMarker Marker = new("ShiftFour.InterceptorLearning");
        private readonly InterceptorLearningSettings settings;
        private readonly InterceptorPredictionEvaluator evaluator = new();
        private readonly Action<InterceptorPredictionEvaluator.Result, float> finish;
        private readonly float[] reliability = new float[6], learnedAt = new float[6], lastError = new float[6];
        private readonly float[] errors = new float[16];
        private readonly (int observer, int kind, float at)[] learnedEvidence = new (int, int, float)[24];
        private int evidenceIndex;
        private int sampledErrors, errorIndex, sequence;
        private float errorSum, nextCapture, lastEvaluationAt;
        private Vector3 lastPosition, lastVelocity;
        private float lastAt = float.NegativeInfinity, evidenceSince;
        private bool measuredVelocity;
        internal readonly InterceptorTacticalMemory Memory = new();
        internal int Unit { get; set; }
        internal bool Enabled => settings.enabled;
        internal float Rate => Bound(settings.learningRate, .12f, .05f, .20f);
        internal float Decay => Bound(settings.decayRate, .05f, .01f, .20f);
        private float Tolerance => Bound(settings.timingTolerance, .3f, .1f, .5f);
        private float Scale => Bound(settings.errorScale, 3f, 1f, 10f);
        internal int Evaluated => Successful + Failed;
        internal int Successful { get; private set; }
        internal int Failed { get; private set; }
        internal int Unevaluated { get; private set; }
        internal int Pending => evaluator.Count;
        internal float MeanError => sampledErrors == 0 ? 0f : errorSum / sampledErrors;
        internal float DirectionChanges { get; private set; }
        internal float Stopping { get; private set; }
        internal float Reversals { get; private set; }
        internal float MeanSpeed { get; private set; }
        internal float SpeedVariation { get; private set; }
        internal float Acceleration { get; private set; }
        internal float Horizon { get; private set; }
        internal string LastAdaptation { get; private set; } = "Waiting for evaluated evidence";
        internal InterceptorPredictionEvaluator.Result? LastResult { get; private set; }

        internal InterceptorLearning(InterceptorLearningSettings settings)
        {
            this.settings = settings;
            finish = Complete;
            Array.Fill(reliability, Prior);
            Array.Fill(lastError, float.NaN);
        }

        internal float Reliability(InterceptorHypothesisKind kind, float now)
        {
            int index = (int)kind;
            return Prior + (reliability[index] - Prior) * Mathf.Exp(-Decay * Mathf.Max(0f, now - learnedAt[index]));
        }

        internal float LastError(InterceptorHypothesisKind kind) => lastError[(int)kind];

        internal float Weight(InterceptorHypothesisKind kind, float now)
        {
            if (!Enabled) return 1f;
            float history = 1f + .5f * (Reliability(kind, now) - Prior);
            float motion = kind is InterceptorHypothesisKind.Left or InterceptorHypothesisKind.Right or InterceptorHypothesisKind.Alternate
                ? .14f * DirectionChanges : kind == InterceptorHypothesisKind.Continue ? -.12f * DirectionChanges : 0f;
            return Mathf.Clamp(history + motion, .77f, 1.18f);
        }

        internal float UtilityBias(InterceptorHypothesisKind kind, float now) =>
            Enabled ? .08f * (Reliability(kind, now) - Prior) : 0f;

        internal void OfferEvidence(float now, InterceptorEvidence evidence) => evaluator.Observe(now, evidence, Tolerance);

        internal void Tick(float now, InterceptorEvidence evidence = default, bool close = false)
        {
            using var sample = Marker.Auto();
            Memory.Expire(now);
            OfferEvidence(now, evidence);
            evaluator.Update(now, Tolerance, Scale, finish, close);
        }

        internal void Capture(InterceptorCandidate candidate, int graph, float now, float observedAt, float window)
        {
            if (candidate == null || now < nextCapture || observedAt != now ||
                !float.IsFinite(candidate.PlayerArrival) || candidate.PlayerArrival < .7f || candidate.PlayerArrival > 5f ||
                !float.IsFinite(candidate.Position.sqrMagnitude) || !float.IsFinite(candidate.DroneArrival) ||
                !float.IsFinite(candidate.Confidence) || !float.IsFinite(now) || !float.IsFinite(window) ||
                (uint)candidate.Hypothesis >= reliability.Length) return;
            nextCapture = now + 1f;
            // The validated hypothesis first reaches this endpoint at PlayerArrival.
            evaluator.Add(new InterceptorPredictionEvaluator.Snapshot(++sequence, now, now + candidate.PlayerArrival,
                observedAt, candidate.Position, candidate.Position, candidate.Hypothesis, candidate.Confidence,
                candidate.DroneArrival, window, graph));
        }

        private void Complete(InterceptorPredictionEvaluator.Result result, float now)
        {
            LastResult = result;
            var prediction = result.Prediction;
            Memory.Add(result.Outcome, prediction.Destination, now, prediction.Graph, sourceUnit: Unit,
                decision: prediction.Id, error: result.Error, evaluation: result);
            switch (result.Outcome)
            {
                case InterceptorOutcome.Unevaluated:
                    Unevaluated++;
                    LastAdaptation = "No time-matched sighting";
                    return;
                case InterceptorOutcome.SuccessfulPrediction: Successful++; break;
                case InterceptorOutcome.FailedPrediction: Failed++; break;
            }
            int index = (int)prediction.Kind;
            lastError[index] = result.Error;
            lastEvaluationAt = now;
            errorSum -= errors[errorIndex];
            errors[errorIndex] = result.Error;
            errorSum += result.Error;
            errorIndex = (errorIndex + 1) % errors.Length;
            sampledErrors = Mathf.Min(errors.Length, sampledErrors + 1);
            if (Enabled)
            {
                var evidence = (result.Observer, index, result.ObservedAt);
                foreach (var accepted in learnedEvidence)
                {
                    if (accepted != evidence) continue;
                    LastAdaptation = "Sighting already used for this model";
                    return;
                }
                learnedEvidence[evidenceIndex] = evidence;
                evidenceIndex = (evidenceIndex + 1) % learnedEvidence.Length;
                float prior = Reliability(prediction.Kind, now);
                reliability[index] = Mathf.Clamp(Mathf.Lerp(prior, result.Quality, Rate), .25f, .95f);
                learnedAt[index] = now;
                LastAdaptation = reliability[index] > prior ? "Matched sighting raised reliability" :
                    reliability[index] < prior ? "Matched sighting lowered reliability" : "Reliability held at bound";
            }
            else LastAdaptation = "Comparison mode held reliability at prior";
        }

        internal void ObserveMotion(Vector3 position, float now, InterceptorSettings policy)
        {
            float dt = now - lastAt;
            if (dt <= 0f || !float.IsFinite(position.sqrMagnitude)) return;
            Vector3 velocity = InterceptorMotionEstimator.Flat(position - lastPosition) / dt;
            lastPosition = position;
            lastAt = now;
            float speed = velocity.magnitude;
            if (dt > policy.historySeconds || speed > 1.25f * policy.maxPlayerSpeed)
            {
                measuredVelocity = false;
                evidenceSince = now;
                DirectionChanges = Stopping = Reversals = SpeedVariation = Acceleration = MeanSpeed = 0f;
                return;
            }
            float turn = measuredVelocity && lastVelocity.magnitude > policy.stoppedSpeed && speed > policy.stoppedSpeed
                ? Vector3.Angle(lastVelocity, velocity) : 0f;
            float alpha = 1f - Mathf.Exp(-dt / 2.4f);
            DirectionChanges = Mathf.Lerp(DirectionChanges, turn >= policy.sharpTurnDegrees ? 1f : 0f, alpha);
            Reversals = Mathf.Lerp(Reversals, turn >= 140f ? 1f : 0f, alpha);
            Stopping = Mathf.Lerp(Stopping, speed <= policy.stoppedSpeed ? 1f : 0f, alpha);
            SpeedVariation = Mathf.Lerp(SpeedVariation, Mathf.Abs(speed - MeanSpeed), alpha);
            MeanSpeed = measuredVelocity ? Mathf.Lerp(MeanSpeed, speed, alpha) : speed;
            if (measuredVelocity)
                Acceleration = Mathf.Lerp(Acceleration, Mathf.Min(policy.maxAcceleration, (velocity - lastVelocity).magnitude / dt), alpha);
            lastVelocity = velocity;
            measuredVelocity = true;
        }

        internal float AdaptHorizon(float ceiling, float now, float confidence, float uncertainty, bool hasRoute)
        {
            ceiling = Bound(ceiling, 4f, 1f, 5f);
            if (!Enabled || !float.IsFinite(lastAt) || now - evidenceSince < 1.2f) return Horizon = ceiling;
            // Only shrink from the configured limit. Stable evidence restores horizon slowly.
            float risk = .65f * DirectionChanges + .10f * Reversals + .08f * Stopping +
                .14f * Mathf.Clamp01(SpeedVariation / 3f) + .12f * Mathf.Clamp01(Acceleration / 8f) +
                .12f * Mathf.Clamp01(uncertainty / 3f) + .10f * (1f - Mathf.Clamp01(confidence)) +
                .08f * Mathf.Clamp01((now - lastAt) / 2f) +
                .12f * Mathf.Clamp01(MeanError / 6f) * Mathf.Exp(-Decay * Mathf.Max(0f, now - lastEvaluationAt)) + (hasRoute ? 0f : .06f);
            float desired = Mathf.Lerp(ceiling, 1f, Mathf.Clamp01(risk));
            if (Horizon < 1f) Horizon = ceiling;
            if (Mathf.Abs(Horizon - desired) > .08f)
                Horizon = Mathf.MoveTowards(Horizon, desired, desired < Horizon ? .30f : .12f);
            return Horizon = Mathf.Clamp(Horizon, 1f, ceiling);
        }

        private static float Bound(float value, float fallback, float min, float max) =>
            float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;
    }
}

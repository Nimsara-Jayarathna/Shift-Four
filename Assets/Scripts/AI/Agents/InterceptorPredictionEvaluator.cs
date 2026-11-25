using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace ShiftFour
{
    internal sealed class InterceptorPredictionEvaluator
    {
        internal readonly struct Snapshot
        {
            internal readonly float Created, Expected, EvidenceAt, Confidence, DroneEta, Window;
            internal readonly int Id, Graph;
            internal readonly Vector3 Point, Destination;
            internal readonly InterceptorHypothesisKind Kind;

            internal Snapshot(int id, float created, float expected, float evidenceAt, Vector3 point,
                Vector3 destination, InterceptorHypothesisKind kind, float confidence, float droneEta, float window, int graph)
            {
                Id = id; Created = created; Expected = expected; EvidenceAt = evidenceAt; Point = point;
                Destination = destination; Kind = kind; Confidence = confidence; DroneEta = droneEta; Window = window; Graph = graph;
            }
        }

        internal readonly struct Result
        {
            internal readonly Snapshot Prediction;
            internal readonly float Error, Quality, ObservedAt;
            internal readonly int Observer;
            internal readonly InterceptorEvidence Evidence;
            internal readonly Vector3 Observed;
            internal readonly InterceptorOutcome Outcome;

            internal Result(Snapshot prediction, InterceptorEvidence observation, float tolerance, float scale)
            {
                Prediction = prediction;
                ObservedAt = observation.Time;
                Observer = observation.Sender;
                Evidence = observation;
                Observed = observation.Position;
                bool evaluated = observation.Reliability > 0f;
                Error = evaluated ? Vector3.Distance(prediction.Point, Observed) : float.NaN;
                float relaxedScale = scale * (1f + .14f * (prediction.Expected - prediction.Created));
                float timing = 1f - .35f * Mathf.Clamp01(Mathf.Abs(ObservedAt - prediction.Expected) / tolerance);
                Quality = float.IsFinite(Error) ? Mathf.Clamp01(Mathf.Exp(-Error / relaxedScale) * observation.Reliability * timing) : 0f;
                Outcome = !float.IsFinite(Error) ? InterceptorOutcome.Unevaluated :
                    Error <= relaxedScale ? InterceptorOutcome.SuccessfulPrediction : InterceptorOutcome.FailedPrediction;
            }
        }

        private struct Pending
        {
            internal Snapshot Forecast;
            internal InterceptorEvidence Closest;
        }

        private static readonly ProfilerMarker Marker = new("ShiftFour.PredictionEvaluation");
        private readonly List<Pending> pending = new(24);
        internal int Count => pending.Count;

        internal void Add(Snapshot prediction)
        {
            if (pending.Count < 24) pending.Add(new Pending { Forecast = prediction });
        }

        internal void Observe(float now, InterceptorEvidence evidence, float tolerance)
        {
            if (evidence.Source != InterceptorObservationSource.DirectVisual || !evidence.Fresh(now) ||
                evidence.Reliability < .75f || evidence.Radius > 1.25f) return;
            using var sample = Marker.Auto();
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var evaluation = pending[i];
                var prediction = evaluation.Forecast;
                float offset = Mathf.Abs(evidence.Time - prediction.Expected);
                float previousOffset = Mathf.Abs(evaluation.Closest.Time - prediction.Expected);
                bool better = evaluation.Closest.Reliability == 0f || offset < previousOffset ||
                    (offset == previousOffset && evidence.Reliability > evaluation.Closest.Reliability);
                if (evidence.Time > prediction.EvidenceAt && offset <= tolerance && better)
                {
                    evaluation.Closest = evidence;
                    pending[i] = evaluation;
                }
            }
        }

        internal void Update(float now, float tolerance, float scale, Action<Result, float> finish, bool close = false)
        {
            using var sample = Marker.Auto();
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var evaluation = pending[i];
                if (!close && now < evaluation.Forecast.Expected + tolerance) continue;
                pending.RemoveAt(i);
                finish(new Result(evaluation.Forecast, evaluation.Closest, tolerance, scale), now);
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace ShiftFour.Tests
{
    public sealed class InterceptorAdaptivePredictionEditModeTests
    {
        private static readonly Type SettingsType = RuntimeType("InterceptorSettings");
        private static readonly Type PolicyType = RuntimeType("InterceptorPolicy");
        private static readonly MethodInfo StepMethod = PolicyType.GetMethod("StepWithRoutes");
        private static readonly MethodInfo ForecastMethod = PolicyType.GetMethod("Forecast");
        private static readonly MethodInfo UncertaintyMethod = PolicyType.GetMethod("MotionUncertainty");
        private static readonly Vector3 Home = new(7, 0, -2);

        private static Type RuntimeType(string name) => Type.GetType("ShiftFour." + name + ", Assembly-CSharp", true);
        private static object Read(object instance, string name) => instance.GetType().GetProperty(name).GetValue(instance);
        private static object Policy(bool adaptive = true) =>
            Activator.CreateInstance(PolicyType, Activator.CreateInstance(SettingsType), adaptive);
        private static Vector3 X(float x) => Vector3.right * x;
        private static Vector3[] Direct(Vector3 start, Vector3 end) => new[] { start, end };
        private static Vector3 Goal(object decision) => (Vector3)decision.GetType().GetField("Destination").GetValue(decision);
        private static object[] Hypotheses(object policy) => ((IEnumerable)Read(policy, "Hypotheses")).Cast<object>().ToArray();
        private static float Uncertainty(object policy, float time) => (float)UncertaintyMethod.Invoke(policy, new object[] { time });

        private static object Step(object policy, float time, bool visible, Vector3 measurement, Vector3[] nodes,
            Func<Vector3, Vector3, Vector3[]> playerRoute = null, Func<Vector3, Vector3, Vector3[]> droneRoute = null,
            int graphVersion = 0)
            => StepMethod.Invoke(policy, new object[] { time, visible, measurement, Home, Home, 5f, graphVersion,
                nodes, droneRoute ?? Direct, playerRoute ?? Direct });

        [Test]
        public void VariableIntervalsTrackAccelerationAndStopWithoutInventingMotion()
        {
            var policy = Policy();
            float[] times = { 0f, .15f, .48f, .71f, 1.12f, 1.38f, 1.76f, 2.1f };
            foreach (float time in times)
                Step(policy, time, true, X(time * time), Array.Empty<Vector3>());
            Assert.That(((Vector3)Read(policy, "EstimatedPosition")).x, Is.EqualTo(4.41f).Within(.35f));
            Assert.That(((Vector3)Read(policy, "EstimatedVelocity")).x, Is.InRange(3.1f, 4.3f));
            Assert.That(((Vector3)Read(policy, "EstimatedAcceleration")).x, Is.InRange(1.5f, 2.5f));
            Step(policy, 2.5f, true, X(4.41f), Array.Empty<Vector3>());
            Assert.That(Read(policy, "EstimatedVelocity"), Is.EqualTo(Vector3.zero));
            Assert.That(Hypotheses(policy).Select(h => Read(h, "Kind").ToString()), Is.EqualTo(new[] { "Stop" }));
        }

        [Test]
        public void SharpTurnReversalAndStaleGapDiscardUnsupportedVelocity()
        {
            var policy = Policy();
            for (int i = 0; i <= 5; i++) Step(policy, i * .3f, true, X(i * .9f), Array.Empty<Vector3>());
            Step(policy, 1.8f, true, new Vector3(4.5f, 0, .9f), Array.Empty<Vector3>());
            Assert.That(Vector3.Distance((Vector3)Read(policy, "EstimatedVelocity"), Vector3.forward * 3f), Is.LessThan(.001f));
            Step(policy, 2.1f, true, new Vector3(4.5f, 0, 0f), Array.Empty<Vector3>());
            Assert.That(Vector3.Distance((Vector3)Read(policy, "EstimatedVelocity"), Vector3.back * 3f), Is.LessThan(.001f));
            Step(policy, 6f, true, X(12), Array.Empty<Vector3>());
            Assert.That(Read(policy, "EstimatedVelocity"), Is.EqualTo(Vector3.zero));
            Assert.That(((IEnumerable)Read(policy, "History")).Cast<object>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void BranchesAreNormalizedAndUseOnlyCompleteRoutesWithinTheQueryBudget()
        {
            var policy = Policy();
            Vector3 left = new(8, 0, 3), right = new(8, 0, -3), alternate = new(9, 0, 2.5f);
            Vector3 blocked = new(8, 0, -6), partial = new(10, 0, 3);
            var junctions = new[] { X(11), left, right, alternate, blocked, partial }
                .Concat(Enumerable.Range(0, 24).Select(i => new Vector3(8 + i * .10f, 0, .2f))).ToArray();
            int calls = 0;
            Vector3[] Route(Vector3 from, Vector3 to)
            {
                calls++;
                if (to == blocked) return null;
                if (to == partial) return new[] { from, from + Vector3.right };
                if (to == alternate) return new[] { from, X(9), to };
                return Direct(from, to);
            }

            for (int i = 0; i <= 7; i++)
            {
                calls = 0;
                Step(policy, i * .3f, true, X(i * .9f), junctions, Route);
                Assert.That(calls, Is.LessThanOrEqualTo(10));
            }

            var hypotheses = Hypotheses(policy);
            Assert.That(hypotheses.Select(h => Read(h, "Kind").ToString()),
                Is.EquivalentTo(new[] { "Continue", "Left", "Right", "Stop", "Alternate" }));
            Assert.That(hypotheses.Sum(h => (float)Read(h, "Weight")), Is.EqualTo(1f).Within(.00001f));
            foreach (object hypothesis in hypotheses)
            {
                var route = ((IEnumerable)Read(hypothesis, "Route")).Cast<Vector3>().ToArray();
                Assert.That(Vector3.Distance(route[0], X(6.3f)), Is.LessThan(.001f));
                Assert.That(route.Last(), Is.Not.EqualTo(blocked).And.Not.EqualTo(partial));
            }
        }

        [Test]
        public void BrakingCanOpenAReachableReverseHypothesis()
        {
            var policy = Policy();
            var junctions = new[] { X(9), X(4.5f) };
            for (int i = 0; i <= 5; i++) Step(policy, i * .3f, true, X(i * 1.05f), junctions);
            Step(policy, 1.8f, true, X(6f), junctions);
            Step(policy, 2.1f, true, X(6.45f), junctions);
            Assert.That(Hypotheses(policy).Select(h => Read(h, "Kind").ToString()), Does.Contain("Reverse"));
        }

        [Test]
        public void DuplicateInvalidAndHiddenSamplesCannotRefreshTheTrack()
        {
            var first = Policy();
            var second = Policy();
            var junctions = new[] { X(8), X(11) };
            for (int i = 0; i <= 6; i++)
            {
                Step(first, i * .3f, true, X(i * .9f), junctions);
                Step(second, i * .3f, true, X(i * .9f), junctions);
            }

            float last = (float)Read(first, "LastObservedAt");
            float startNoise = Uncertainty(first, last);
            float confidence = (float)Read(first, "Confidence");
            Step(first, last, true, X(500), junctions);
            Step(second, last, true, X(-500), junctions);
            Vector3 a = Goal(Step(first, 2.1f, false, X(999), junctions));
            Vector3 b = Goal(Step(second, 2.1f, false, X(-999), junctions));
            Assert.That(a, Is.EqualTo(b));
            Assert.That(Uncertainty(first, 2.1f), Is.GreaterThan(startNoise));
            Assert.That((float)Read(first, "Confidence"), Is.LessThan(confidence));
            var samples = ((IEnumerable)Read(first, "History")).Cast<object>().ToArray();
            var id = samples[0].GetType().GetField("Identifier");
            Assert.That(samples.Select(sample => id.GetValue(sample)).Distinct().Count(), Is.EqualTo(samples.Length));
            Step(first, 2.5f, true, new Vector3(float.NaN, 0, 0), junctions);
            Step(first, float.PositiveInfinity, true, X(100), junctions);
            Assert.That(Read(first, "LastObservedAt"), Is.EqualTo(last));
            Step(first, 3f, false, X(999), junctions, graphVersion: 1);
            Assert.That(Hypotheses(first), Is.Empty);
            Step(second, 9f, false, X(-999), junctions);
            Assert.That(Hypotheses(second), Is.Empty);
            Assert.That(Read(second, "Confidence"), Is.EqualTo(0f));
        }

        [Test]
        public void AttractiveJunctionNeverWinsWithPartialDronePath()
        {
            var policy = Policy();
            for (int i = 0; i <= 7; i++)
                Step(policy, i * .3f, true, X(i * .9f), new[] { X(10) }, Direct,
                    (Vector3 from, Vector3 to) => to == X(10) ? new[] { from, X(9) } : Direct(from, to));
            Assert.That(Hypotheses(policy).Select(h => Read(h, "Kind").ToString()), Does.Contain("Continue"));
            Assert.That(Read(policy, "SelectedNode"), Is.EqualTo(-1));
        }

        [Test]
        public void CompareProductionBaselineAndAdaptivePolicyWithIdenticalInputs()
        {
            var csv = new StringBuilder("scenario,policy,horizon_s,samples,mean_error_m,median_error_m,selected_attempts,successful_attempts,invalid_selected_goals,route_queries,switches,replans,step_ms\n");
            float[] horizons = { 1f, 2f, 3f };
            var junctions = Enumerable.Range(-6, 16).Select(i => X(i * 4f))
                .Concat(Enumerable.Range(1, 8).SelectMany(i => new[]
                {
                    new Vector3(6, 0, i * 4f), new Vector3(6, 0, -i * 4f)
                })).ToArray();
            foreach (bool adaptive in new[] { false, true })
            {
                object warmup = Policy(adaptive);
                for (int i = 0; i < 6; i++)
                    Step(warmup, i * .3f, true, X(i * .9f), junctions);
                ForecastMethod.Invoke(warmup, new object[] { 1f });
            }
            foreach (string scenario in new[] { "straight", "accelerate", "left", "right", "stop", "reverse", "cover", "blocked" })
            foreach (bool adaptive in new[] { false, true })
            {
                object policy = Policy(adaptive);
                var errors = horizons.Select(_ => new List<float>()).ToArray();
                int requests = 0, previousNode = -1, attempts = 0, successes = 0, invalid = 0;
                double milliseconds = 0;
                bool Blocked(Vector3 end) => scenario == "blocked" && end == X(8);
                Vector3[] Route(Vector3 from, Vector3 to)
                {
                    requests++;
                    if (Blocked(to)) return null;
                    if ((Mathf.Abs(from.z) > .1f) != (Mathf.Abs(to.z) > .1f))
                        return new[] { from, X(6f), to };
                    return Direct(from, to);
                }

                for (int i = 0; i <= 24; i++)
                {
                    float now = i * .3f;
                    bool visible = scenario != "cover" || now < 2.1f || now > 3.6f;
                    Vector3 observed = visible ? Trace(scenario, now) : Vector3.one * 9000f;
                    long start = Stopwatch.GetTimestamp();
                    Step(policy, now, visible, observed, junctions, Route, Route);
                    milliseconds += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                    float age = now - (float)Read(policy, "LastObservedAt");
                    for (int h = 0; h < horizons.Length; h++)
                    {
                        Vector3 forecast = (Vector3)ForecastMethod.Invoke(policy, new object[] { age + horizons[h] });
                        errors[h].Add(Vector3.Distance(forecast, Trace(scenario, now + horizons[h])));
                    }
                    var candidate = Read(policy, "SelectedCandidate");
                    if (candidate == null) { previousNode = -1; continue; }
                    int node = (int)Read(policy, "SelectedNode");
                    if (Blocked(junctions[node])) invalid++;
                    if (node == previousNode) continue;
                    previousNode = node;
                    attempts++;
                    var estimatedArrival = (float)candidate.GetType().GetField("PlayerArrival").GetValue(candidate);
                    var droneArrival = (float)candidate.GetType().GetField("DroneArrival").GetValue(candidate);
                    if (droneArrival <= estimatedArrival &&
                        Vector3.Distance(junctions[node], Trace(scenario, now + estimatedArrival)) <= 1.1f)
                        successes++;
                }

                Assert.That(invalid, Is.Zero, scenario);
                for (int h = 0; h < horizons.Length; h++)
                {
                    errors[h].Sort();
                    csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4:F3},{5:F3},{6},{7},{8},{9},{10},{11},{12:F3}\n",
                        scenario, adaptive ? "adaptive" : "baseline", horizons[h], errors[h].Count,
                        errors[h].Average(), errors[h][errors[h].Count / 2], attempts, successes, invalid, requests,
                        Read(policy, "Switches"), Read(policy, "Replans"), milliseconds);
                }
            }

            const string directory = "Logs/InterceptorEvidence";
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "phase-f1-comparison.csv"), csv.ToString());
        }

        private static Vector3 Trace(string scenario, float time)
        {
            const float transition = 2f;
            const float speed = 3f;
            if (scenario == "accelerate")
                return X(time <= 3f ? time + .5f * time * time : 7.5f + 4f * (time - 3f));
            if (time <= transition) return X(time * speed);
            float beyond = (time - transition) * speed;
            return scenario switch
            {
                "left" => new Vector3(6f, 0f, beyond),
                "right" => new Vector3(6f, 0f, -beyond),
                "stop" => X(6f),
                "reverse" => X(6f - beyond),
                _ => X(time * speed)
            };
        }
    }
}

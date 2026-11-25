using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace ShiftFour.Tests
{
    // The same fixture runs in the disposable pre-F1 project and the integrated project.
    public sealed class InterceptorFinalReplayEditModeTests
    {
        private static readonly BindingFlags Member = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static Type T(string name, bool required = true) => Type.GetType($"ShiftFour.{name}, Assembly-CSharp", required);
        private static object New(Type type, params object[] args) => Activator.CreateInstance(type, Member, null, args, null);
        private static object Read(object obj, string name) => obj.GetType().GetProperty(name, Member).GetValue(obj);
        private static object Field(object obj, string name) => obj.GetType().GetField(name, Member)?.GetValue(obj);
        private static Vector3[] Edge(Vector3 from, Vector3 to) => new[] { from, to };
        private static readonly Vector3[] Nodes = Enumerable.Range(-16, 33).SelectMany(x =>
            Enumerable.Range(-7, 15).Select(z => new Vector3(x * 3f, 0f, z * 3f))).ToArray();
        private static readonly string[] Scenarios =
            { "straight", "acceleration", "stop", "left_right", "reversal", "visual_loss", "junctions" };

        private struct Frame
        {
            internal float Time, Step;
            internal Vector3 Position;
            internal bool Visible;
            internal string Scenario;
        }

        private sealed class Forecast
        {
            internal float Created, Expected, DroneEta, Confidence, ObservedAt, Error = float.NaN;
            internal string Scenario, Hypothesis;
            internal Vector3 Destination, Observed, DroneAtObservation;
        }

        private static Frame[] Trace(int seed)
        {
            var rng = new System.Random(seed);
            var frames = new Frame[252];
            Vector3 point = new(-12f, 0f, -6f);
            float time = 0f;
            for (int i = 0; i < frames.Length; i++)
            {
                int scenario = (i / 18) % Scenarios.Length, leg = i % 18;
                float dt = .30f + (float)rng.NextDouble() * .12f;
                time += dt;
                Vector3 velocity = scenario switch
                {
                    0 => Vector3.right * 3.1f,
                    1 => Vector3.right * (1.8f + .15f * leg),
                    2 => leg < 5 ? Vector3.forward * 2.8f : Vector3.zero,
                    3 => (leg is >= 5 and < 10 ? Vector3.left : Vector3.forward) * 3.2f,
                    4 => (leg < 7 ? Vector3.right : Vector3.left) * 3.4f,
                    5 => Vector3.left * 3.6f,
                    _ => (leg < 9 ? Vector3.back : (Vector3.back + Vector3.right).normalized) * 3.1f
                };
                point += velocity * dt;
                point.x = Mathf.Clamp(point.x, -43f, 43f);
                point.z = Mathf.Clamp(point.z, -19f, 19f);
                frames[i] = new Frame
                {
                    Time = time, Step = dt, Position = point, Visible = scenario != 5 || leg is < 4 or >= 14,
                    Scenario = Scenarios[scenario]
                };
            }
            return frames;
        }

        [Test]
        public void RecordIdenticalOriginalAndAdvancedPolicyTraces()
        {
            var rows = new StringBuilder("mode,seed,scenario,created_s,target_s,matched_s,horizon_s,predicted_xyz,observed_xyz,drone_at_observation_xyz,error_m,confidence,hypothesis,drone_eta_s,outcome,interception_proxy\n");
            var metrics = new StringBuilder("mode,seed,ticks,evaluated,unevaluated,mae_m,median_m,rmse_m,interception_proxy,first_interception_s,blocked_route_queries,repeated_blocked_endpoints,search_revisits,replans,destination_switches,subcommitment_switches,mean_decision_us,peak_decision_us,warm_alloc_events,warm_calls\n");
            bool original = T("InterceptorLearning", false) == null;
            foreach (string mode in original ? new[] { "original" } : new[] { "ablation", "advanced" })
                foreach (int seed in new[] { 1234, 5678, 9012 })
                    Replay(seed, mode, rows, metrics);
            Write("PREDICTION_RESULTS.csv", rows);
            Write("BASELINE_COMPARISON.csv", metrics);
        }

        private static void Write(string name, StringBuilder text)
        {
            string directory = Environment.GetEnvironmentVariable("SHIFTFOUR_F5_EVIDENCE");
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name), text.ToString());
        }

        private static string Vec(Vector3 value) =>
            FormattableString.Invariant($"{value.x:F3}|{value.y:F3}|{value.z:F3}");

        private static void Replay(int seed, string mode, StringBuilder rows, StringBuilder metrics)
        {
            object settings = New(T("InterceptorSettings"));
            object policy = mode == "original" ? New(T("InterceptorPolicy"), settings) : New(T("InterceptorPolicy"), settings, true);
            object learning = null;
            if (mode != "original")
            {
                var tuning = New(T("InterceptorLearningSettings"));
                tuning.GetType().GetField("enabled").SetValue(tuning, mode == "advanced");
                learning = New(T("InterceptorLearning"), tuning);
                policy.GetType().GetProperty("Learning", Member).SetValue(policy, learning);
            }

            // Return data stays in the harness. No production transform or input is read.
            float time = 0f;
            int blocked = 0, blockedRepeat = 0;
            var blockedCells = new HashSet<Vector3>();
            Func<Vector3, Vector3, Vector3[]> droneRoute = (from, to) =>
            {
                if (time is > 5f and < 12f && to.x is >= -3f and <= 3f && to.z > -9f)
                {
                    blocked++;
                    if (!blockedCells.Add(to)) blockedRepeat++;
                    return null;
                }
                return Edge(from, to);
            };
            var frameArgs = Expression.Parameter(typeof(Frame));
            var originArg = Expression.Parameter(typeof(Vector3));
            var velocityArg = Expression.Parameter(typeof(Vector3));
            var policyExpr = Expression.Constant(policy);
            var step = Expression.Call(policyExpr, policy.GetType().GetMethod("StepWithMotion"), Expression.Field(frameArgs, "Time"),
                Expression.Field(frameArgs, "Visible"), Expression.Condition(Expression.Field(frameArgs, "Visible"),
                    Expression.Field(frameArgs, "Position"), Expression.Constant(Vector3.zero)), originArg, Expression.Constant(new Vector3(-5f, 0, -3f)),
                Expression.Constant(5f), velocityArg, Expression.Constant(10f), Expression.Constant(1),
                Expression.Constant(Nodes), Expression.Constant(droneRoute), Expression.Constant((Func<Vector3, Vector3, Vector3[]>)Edge));
            var run = Expression.Lambda<Func<Frame, Vector3, Vector3, object>>(Expression.Convert(step, typeof(object)),
                frameArgs, originArg, velocityArg).Compile();
            var runUnboxed = Expression.Lambda<Action<Frame, Vector3, Vector3>>(Expression.Block(step, Expression.Empty()),
                frameArgs, originArg, velocityArg).Compile();
            var candidateGetter = policy.GetType().GetProperty("SelectedCandidate").GetGetMethod();
            var getCandidate = Expression.Lambda<Func<object>>(Expression.Call(policyExpr, candidateGetter)).Compile();
            Action<object, float> capture = (_, _) => { };
            if (learning != null)
            {
                var timeArg = Expression.Parameter(typeof(float));
                var candidateArg = Expression.Parameter(typeof(object));
                capture = Expression.Lambda<Action<object, float>>(Expression.Call(Expression.Constant(learning),
                    learning.GetType().GetMethod("Capture", Member), Expression.Convert(candidateArg, T("InterceptorCandidate")),
                    Expression.Constant(1), timeArg, timeArg, Expression.Constant(.6f)), candidateArg, timeArg).Compile();
            }
            var trace = Trace(seed);
            var forecasts = new List<Forecast>(64);
            var searchDestinations = new HashSet<Vector3>();
            Vector3 drone = new(-5, 0, -3), velocity = default;
            Vector3? previousIntercept = null;
            float lastChange = float.NegativeInfinity, nextCapture = 0f;
            int switches = 0, fastSwitches = 0, searchRevisits = 0;
            Vector3? previousSearch = null;
            long cost = 0, peak = 0;
            run(new Frame { Time = -1, Visible = false }, drone, velocity);
            foreach (var frame in trace)
            {
                time = frame.Time;
                long start = Stopwatch.GetTimestamp();
                object decision = run(frame, drone, velocity);
                object candidate = getCandidate();
                bool issue = frame.Visible && candidate != null && time >= nextCapture;
                if (issue) capture(candidate, time);
                long ticks = Stopwatch.GetTimestamp() - start;
                cost += ticks;
                peak = Math.Max(peak, ticks);

                Vector3 target = (Vector3)Field(decision, "Destination");
                Vector3 old = drone;
                // Scripted accepted-motion surrogate with the same speed and acceleration in all modes.
                Vector3 desired = Vector3.ClampMagnitude((target - drone) / frame.Step, 5f);
                velocity = Vector3.MoveTowards(velocity, desired, 10f * frame.Step);
                drone += Vector3.ClampMagnitude(target - drone, velocity.magnitude * frame.Step);
                velocity = (drone - old) / frame.Step;

                if (candidate != null)
                {
                    Vector3 junction = (Vector3)Field(candidate, "Position");
                    if (previousIntercept != junction)
                    {
                        if (previousIntercept.HasValue)
                        {
                            switches++;
                            if (time - lastChange < 1.2f) fastSwitches++;
                        }
                        lastChange = time;
                        previousIntercept = junction;
                    }
                    float eta = (float)Field(candidate, "PlayerArrival");
                    if (issue && eta is >= .7f and <= 5f)
                    {
                        nextCapture = time + 1f;
                        forecasts.Add(new Forecast { Created = time, Expected = time + eta, Destination = junction,
                            Confidence = (float)Field(candidate, "Confidence"), DroneEta = (float)Field(candidate, "DroneArrival"),
                            Hypothesis = Field(candidate, "Hypothesis")?.ToString() ?? "legacy_heading", Scenario = frame.Scenario });
                    }
                }
                else previousIntercept = null;

                if (Read(policy, "State").ToString() == "SearchLastSeenArea")
                {
                    if (previousSearch != target && !searchDestinations.Add(target)) searchRevisits++;
                    previousSearch = target;
                }
                else previousSearch = null;

                if (!frame.Visible) continue;
                foreach (var forecast in forecasts)
                {
                    float offset = Mathf.Abs(time - forecast.Expected);
                    if (time <= forecast.Created || offset > .30f ||
                        (float.IsFinite(forecast.Error) && offset >= Mathf.Abs(forecast.ObservedAt - forecast.Expected))) continue;
                    forecast.ObservedAt = time;
                    forecast.Observed = frame.Position;
                    forecast.DroneAtObservation = drone;
                    forecast.Error = Vector3.Distance(forecast.Destination, frame.Position);
                }
            }

            var errors = new List<float>();
            int hits = 0;
            float first = float.NaN;
            foreach (var forecast in forecasts)
            {
                float horizon = forecast.Expected - forecast.Created;
                bool evaluated = float.IsFinite(forecast.Error);
                bool intercept = evaluated && forecast.Error <= 2f && Vector3.Distance(forecast.Destination, forecast.DroneAtObservation) <= 2f;
                if (evaluated) errors.Add(forecast.Error);
                if (intercept)
                {
                    hits++;
                    if (!float.IsFinite(first)) first = forecast.ObservedAt;
                }
                string outcome = !evaluated ? "Unevaluated" : forecast.Error <= 3f * (1f + .14f * horizon) ? "SuccessfulPrediction" : "FailedPrediction";
                rows.Append(FormattableString.Invariant($"{mode},{seed},{forecast.Scenario},{forecast.Created:F4},{forecast.Expected:F4},"));
                if (evaluated)
                    rows.Append(FormattableString.Invariant($"{forecast.ObservedAt:F4},{horizon:F4},{Vec(forecast.Destination)},{Vec(forecast.Observed)},{Vec(forecast.DroneAtObservation)},{forecast.Error:F4},"));
                else rows.Append(FormattableString.Invariant($",{horizon:F4},{Vec(forecast.Destination)},,,,"));
                rows.AppendLine(FormattableString.Invariant($"{forecast.Confidence:F4},{forecast.Hypothesis},{forecast.DroneEta:F4},{outcome},{intercept}"));
            }
            Assert.That(errors.Count, Is.Positive, $"{mode} {seed} must produce matched forecasts");
            double factor = 1e6 / Stopwatch.Frequency;
            int replans = (int)Read(policy, "Replans");
            long allocationEvents = CountPolicyAllocations(runUnboxed, trace);
            errors.Sort();
            metrics.Append(FormattableString.Invariant($"{mode},{seed},{trace.Length},{errors.Count},{forecasts.Count - errors.Count},"));
            metrics.Append(FormattableString.Invariant($"{errors.Average():F4},{(errors[(errors.Count - 1) / 2] + errors[errors.Count / 2]) * .5f:F4},{Math.Sqrt(errors.Average(e => e * e)):F4},"));
            metrics.AppendLine(FormattableString.Invariant($"{hits},{first:F4},{blocked},{blockedRepeat},{searchRevisits},{replans},{switches},{fastSwitches},{cost * factor / trace.Length:F4},{peak * factor:F4},{allocationEvents},{trace.Length}"));
            TestContext.Out.WriteLine(metrics.ToString().TrimEnd().Split('\n').Last());
        }

        private static long CountPolicyAllocations(Action<Frame, Vector3, Vector3> run, Frame[] trace)
        {
            var origin = new Vector3(-5f, 0f, -3f);
            bool previous = Profiler.enabled;
            Profiler.enabled = true;
            try
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 65536,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                var calibration = new byte[4096];
                foreach (var source in trace)
                {
                    var frame = source;
                    frame.Time += 300f;
                    run(frame, origin, Vector3.zero);
                }
                recorder.Stop();
                GC.KeepAlive(calibration);
                Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
                long samples = 0;
                for (int i = 0; i < recorder.Count; i++) samples += recorder.GetSample(i).Count;
                Assert.That(samples, Is.GreaterThan(0), "The calibration array should be captured.");
                return samples - 1;
            }
            finally { Profiler.enabled = previous; }
        }
    }
}

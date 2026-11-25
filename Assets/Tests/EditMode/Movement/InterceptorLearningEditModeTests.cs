using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace ShiftFour.Tests
{
    public sealed class InterceptorLearningEditModeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type TypeOf(string name) => Type.GetType("ShiftFour." + name + ", Assembly-CSharp", true);
        private static object EnumOf(string type, string name) => Enum.Parse(TypeOf(type), name);
        private static object New(Type type, params object[] args) => Activator.CreateInstance(type, Members, null, args, null);
        private static object New(string type, params object[] args) => New(TypeOf(type), args);
        private static object Read(object obj, string member) => obj.GetType().GetProperty(member, Members).GetValue(obj);
        private static object Field(object obj, string member) => obj.GetType().GetField(member, Members).GetValue(obj);
        private static object Call(object obj, string method, params object[] args) =>
            obj.GetType().GetMethod(method, Members).Invoke(obj, args);
        private static object[] Items(object obj, string property) => ((IEnumerable)Read(obj, property)).Cast<object>().ToArray();
        private static object Evidence(string type, Vector3 position, float at, int sender = 2) =>
            New("InterceptorEvidence", EnumOf("InterceptorObservationSource", type), position, at, at + 2f, .2f, 1f, sender);
        private static object Empty => New("InterceptorEvidence");
        private static object Learning(bool enabled = true, float decay = .05f)
        {
            object settings = New("InterceptorLearningSettings");
            settings.GetType().GetField("enabled").SetValue(settings, enabled);
            settings.GetType().GetField("decayRate").SetValue(settings, decay);
            return New("InterceptorLearning", settings);
        }
        private static object Forecast(object learning, float now, float eta, Vector3 position, string kind = "Continue")
        {
            object candidate = New("InterceptorCandidate");
            foreach (var pair in new (string, object)[] { ("PlayerArrival", eta), ("Position", position), ("Hypothesis",
                EnumOf("InterceptorHypothesisKind", kind)), ("Confidence", .8f), ("DroneArrival", .8f) })
                candidate.GetType().GetField(pair.Item1).SetValue(candidate, pair.Item2);
            Call(learning, "Capture", candidate, 1, now, now, .6f);
            return candidate;
        }
        private static void Tick(object learning, float now, object evidence = null, bool close = false) =>
            Call(learning, "Tick", now, evidence ?? Empty, close);
        private static float Reliability(object learning, float now) =>
            (float)Call(learning, "Reliability", EnumOf("InterceptorHypothesisKind", "Continue"), now);

        [Test]
        public void NearestCompatibleNewVisualEvidenceEvaluatesOriginalForecastExactlyOnce()
        {
            var learning = Learning();
            Forecast(learning, 0f, 2f, Vector3.right * 6f);
            Tick(learning, 1.7f, Evidence("DirectVisual", Vector3.one * 100f, 1.2f));
            Tick(learning, 1.8f, Evidence("DirectVisual", Vector3.one * 50f, 1.8f));
            Tick(learning, 2.04f, Evidence("DirectVisual", Vector3.right * 7.5f, 2.04f));
            Tick(learning, 2.4f);
            Tick(learning, 3f, Evidence("DirectVisual", Vector3.right * 7.5f, 2.04f));
            Assert.That(Read(learning, "Evaluated"), Is.EqualTo(1));
            Assert.That(Read(learning, "Successful"), Is.EqualTo(1));
            Assert.That(Read(learning, "MeanError"), Is.EqualTo(1.5f).Within(.0001f));
            Assert.That(Field(Field(Read(learning, "LastResult"), "Prediction"), "Point"), Is.EqualTo(Vector3.right * 6f));
        }

        [Test]
        public void ExpiredApproximateAndNonFiniteSamplesLeaveForecastUnevaluated()
        {
            var learning = Learning();
            Forecast(learning, 0f, 2f, Vector3.right * 6f);
            Tick(learning, 2f, Evidence("ScoutReport", Vector3.one, 2f));
            Tick(learning, 2.1f, Evidence("DirectVisual", new Vector3(float.NaN, 0, 0), 2.1f));
            Tick(learning, 2.4f, Evidence("DirectVisual", Vector3.one, 1.1f));
            Assert.That(Read(learning, "Unevaluated"), Is.EqualTo(1));
            Assert.That(Read(learning, "Evaluated"), Is.Zero);
            Assert.That(Reliability(learning, 2.4f), Is.EqualTo(.65f));
        }

        [TestCase(0f, .95f)]
        [TestCase(14f, .25f)]
        public void RepeatedOutcomesChangeOnlyEvaluatedModelAndThenDecay(float error, float limit)
        {
            var learning = Learning(decay: .01f);
            for (int i = 0; i < 80; i++)
            {
                float t = i * 2f;
                Forecast(learning, t, 1f, Vector3.zero);
                Tick(learning, t + 1f, Evidence("DirectVisual", Vector3.right * error, t + 1f));
                Tick(learning, t + 1.31f);
            }
            float adapted = Reliability(learning, 159.31f);
            Assert.That(adapted, Is.EqualTo(limit).Within(.005f));
            Assert.That((float)Call(learning, "Reliability", EnumOf("InterceptorHypothesisKind", "Alternate"), 159.31f), Is.EqualTo(.65f));
            Assert.That(Reliability(learning, 1000f), Is.EqualTo(.65f).Within(.005f));
        }

        [Test]
        public void SameSharedEvidenceCannotUpdateSameModelTwice()
        {
            var learning = Learning();
            Forecast(learning, 0f, 2f, Vector3.zero);
            Forecast(learning, 1f, 1f, Vector3.zero);
            var shared = Evidence("DirectVisual", Vector3.zero, 2f);
            Tick(learning, 2.4f, shared);
            Tick(learning, 2.5f, shared);
            Assert.That(Read(learning, "Evaluated"), Is.EqualTo(2));
            Assert.That(Reliability(learning, 2.4f), Is.EqualTo(.65f + .12f * .35f).Within(.00001f));
        }

        [Test]
        public void RouteFailuresAreScopedToSignatureAndGraphAndMemoryExpires()
        {
            var learning = Learning();
            var memory = Field(learning, "Memory");
            var failure = EnumOf("InterceptorOutcome", "PhysicalObstruction");
            Call(memory, "Add", failure, Vector3.right, 1f, 2, 77, 1, 0, 3f, float.NaN, null);
            bool Cooling(int graph, int route, float t) => (bool)Call(memory, "Cooling", Vector3.right, graph, route, t, 3f, 0f);
            Assert.That(Cooling(2, 77, 2f), Is.True);
            Assert.That(Cooling(2, 22, 2f) || Cooling(3, 77, 2f) || Cooling(2, 77, 4f), Is.False);
            Assert.That(Reliability(learning, 2f), Is.EqualTo(.65f));
            var classifier = memory.GetType().GetMethod("Classify", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var reason in new[] { "NoGraphRoute", "Navigation", "PhysicalObstruction", "Unavailable" })
                Assert.That(classifier.Invoke(null, new object[] { reason }).ToString(), Is.Not.Empty);
            for (int i = 0; i < 80; i++)
                Call(memory, "Add", failure, Vector3.right * i, 10f + i * .01f, 2, 77, 1, 0, 4f, float.NaN, null);
            Assert.That(Items(memory, "Entries"), Has.Length.EqualTo(64));
            Call(memory, "Expire", 15f);
            Assert.That(Items(memory, "Entries"), Is.Empty);
        }

        private static object Model(object learning)
        {
            object policy = New("InterceptorPolicy", New("InterceptorSettings"), true);
            policy.GetType().GetProperty("Learning", Members).SetValue(policy, learning);
            return policy;
        }

        private static Vector3[] Direct(Vector3 a, Vector3 b) => new[] { a, b };
        private static Vector3[] Grid() => Enumerable.Range(-16, 33).SelectMany(x => Enumerable.Range(-4, 9)
            .Select(z => new Vector3(x * 3f, 0f, z * 3f))).ToArray();
        private static void Step(object policy, float at, bool visible, Vector3 player, Vector3 drone, Vector3[] grid,
            Func<Vector3, Vector3, Vector3[]> route)
            => Call(policy, "StepWithRoutes", at, visible, player, drone, drone, 5f, 1, grid, route,
                (Func<Vector3, Vector3, Vector3[]>)Direct);

        [Test]
        public void StableMovementKeepsLongerHorizonThanRepeatedTurnsAndNeverReadsHiddenPositions()
        {
            var grid = Grid();
            var stable = Learning();
            var turning = Learning();
            var hidden = Learning();
            var a = Model(stable);
            var b = Model(turning);
            var c = Model(hidden);
            Vector3 player = default;
            for (int i = 0; i < 36; i++)
            {
                float t = i * .38f;
                player += (i % 2 == 0 ? Vector3.forward : Vector3.right) * 1.2f;
                Step(a, t, true, Vector3.right * t * 3f, Vector3.right * 6f + Vector3.back * 3f, grid, Direct);
                Step(b, t, true, player, player + Vector3.back * 6f, grid, Direct);
                Step(c, t, true, player, player + Vector3.back * 6f, grid, Direct);
                if (i > 9)
                    Assert.That(Items(b, "Hypotheses").Sum(h => (float)Read(h, "Weight")), Is.EqualTo(1f).Within(.0001f));
            }
            Assert.That((float)Read(turning, "Horizon"), Is.InRange(1f, 5f).And.LessThan((float)Read(stable, "Horizon")));
            Assert.That((float)Read(turning, "DirectionChanges"), Is.GreaterThan(.7f));
            Step(b, 14f, false, new Vector3(float.NaN, 0, 0), player + Vector3.back * 6f, grid, Direct);
            Step(c, 14f, false, Vector3.one * 10000f, player + Vector3.back * 6f, grid, Direct);
            Assert.That(Read(b, "LastObserved"), Is.EqualTo(Read(c, "LastObserved")));
            Assert.That(Read(b, "State"), Is.EqualTo(Read(c, "State")));
            Assert.That(Read(turning, "Evaluated"), Is.EqualTo(Read(hidden, "Evaluated")));
        }

        [Test]
        public void BlockedRouteCooldownStopsReselectionAndTopologyChangeAllowsRetry()
        {
            var results = new List<int>();
            foreach (bool enabled in new[] { false, true })
            {
                var learning = Learning(enabled);
                var policy = Model(learning);
                var node = Vector3.right * 12f;
                var drone = new Vector3(9f, 0f, -4f);
                for (int i = 0; i <= 4; i++)
                    Step(policy, i * .4f, true, Vector3.right * i * 1.2f, drone, new[] { node }, Direct);
                var committed = Read(policy, "SelectedCandidate");
                Assert.That(committed, Is.Not.Null);
                var signatureMethod = TypeOf("InterceptorTacticalMemory").GetMethod("RouteSignature", BindingFlags.Static | BindingFlags.NonPublic);
                int signature = (int)signatureMethod.Invoke(null, new[] { Field(committed, "DroneRoute") });
                var memory = Field(learning, "Memory");
                Call(memory, "Add", EnumOf("InterceptorOutcome", "PhysicalObstruction"), node, 1.65f, 1,
                    signature, 1, 0, 3f, float.NaN, null);
                int attempts = 0;
                for (int i = 5; i <= 7; i++)
                {
                    Step(policy, i * .4f, true, Vector3.right * i * 1.2f, drone, new[] { node }, Direct);
                    if (Read(policy, "SelectedCandidate") != null) attempts++;
                }
                results.Add(attempts);
                Call(policy, "StepWithRoutes", 3.2f, true, Vector3.right * 9.6f, new Vector3(12, 0, -3.1f), drone,
                    5f, 2, new[] { node }, (Func<Vector3, Vector3, Vector3[]>)Direct, (Func<Vector3, Vector3, Vector3[]>)Direct);
                Assert.That(Read(policy, "SelectedCandidate"), Is.Not.Null, "Revalidate the same destination on the changed graph.");
                Assert.That(Reliability(learning, 3.2f), Is.EqualTo(.65f));
            }
            TestContext.Out.WriteLine($"F4 blocked-route fixture · repeated approach selections baseline={results[0]}, adaptive={results[1]} over three decisions.");
            Assert.That(results[0], Is.GreaterThan(0));
            Assert.That(results[1], Is.Zero);
        }

        [Test]
        public void RecentlyInspectedRegionLosesUtilityAgainstACloseAlternative()
        {
            var repeated = new List<int>();
            foreach (bool enabled in new[] { false, true })
            {
                var learning = Learning(enabled);
                var memory = Field(learning, "Memory");
                var first = Vector3.right * 8f;
                var second = Vector3.forward * 8f;
                Call(memory, "Add", EnumOf("InterceptorOutcome", "FailedSearch"), first, 1f, 1,
                    0, 1, 0, 30f, float.NaN, null);
                var action = EnumOf("InterceptorAction", "SearchBeliefRegion");
                int revisits = 0;
                // New belief after a sighting can reintroduce a region at the same location.
                for (int i = 0; i < 4; i++)
                {
                    float now = 2f + i * 2f;
                    var utility = New("InterceptorUtilityDecision", New("InterceptorDecisionSettings"));
                    float penalty = enabled ? (float)Call(memory, "SearchPenalty", first, 1, now, .05f) : 0f;
                    Call(utility, "Offer", action, first, .68f - penalty, .1f, .1f, "Reintroduced region");
                    Call(utility, "Offer", action, second, .63f, .1f, .1f, "Uninspected alternative");
                    var choice = Call(utility, "Select", now, false);
                    if ((Vector3)Field(choice, "Destination") == first) revisits++;
                }
                repeated.Add(revisits);
            }
            Assert.That(repeated, Is.EqualTo(new[] { 4, 0 }));
            TestContext.Out.WriteLine($"F4 search fixture · revisits baseline={repeated[0]}, adaptive={repeated[1]} over four fresh decisions.");
        }

        [Test]
        public void HeldOutTraceComparison()
        {
            foreach (bool enabled in new[] { false, true })
                foreach (int seed in new[] { 1234, 5678, 9012 })
                    Replay(seed, enabled);
        }

        [Test]
        public void HeldOutOffsetApproachComparison()
        {
            foreach (bool enabled in new[] { false, true })
                foreach (int seed in new[] { 4523, 8707 })
                    Replay(seed, enabled, offsetRoute: true);
        }

        private static void Replay(int seed, bool enabled, bool offsetRoute = false)
        {
            var random = new System.Random(seed);
            var learning = Learning(enabled);
            var policy = Model(learning);
            var grid = Grid();
            var errors = new List<(float Error, float Horizon, bool Late)>();
            Vector3 player = Vector3.zero;
            Vector3 drone = Vector3.right * 7f + Vector3.back * 5f;
            Vector3? previous = null;
            int switches = 0, subcommitment = 0, blockedAttempts = 0, repeated = 0, lostPlans = 0, trials = 0, reachedInTime = 0;
            float switchedAt = float.NegativeInfinity;
            float? failedAt = null;
            var recovery = new List<float>();
            var blockedOnce = new HashSet<Vector3>();
            var watch = new Stopwatch();
            float at = 0f;
            var recorded = new HashSet<int>();
            object memory = Field(learning, "Memory");
            Func<Vector3, Vector3, Vector3[]> route = (start, end) =>
            {
                if (at is > 2f and < 7f && end.x is >= 9f and <= 15f && end.z >= -3f)
                {
                    blockedAttempts++;
                    if (!blockedOnce.Add(end)) repeated++;
                    return null;
                }
                return Direct(start, end);
            };
            for (int i = 0; i < 180; i++)
            {
                int phase = offsetRoute ? ((i + 9) / 17) % 6 : (i / 15) % 6;
                Vector3 heading = phase switch
                {
                    0 => Vector3.right, 1 => Vector3.right, 2 => Vector3.forward, 3 => Vector3.zero,
                    4 => (Vector3.left + Vector3.back).normalized, _ => Vector3.left
                };
                if (offsetRoute)
                    heading = phase switch
                    {
                        0 => Vector3.back, 1 => Vector3.right, 2 => (Vector3.right + Vector3.forward).normalized,
                        3 => Vector3.forward, 4 => Vector3.zero, _ => (Vector3.left + Vector3.back).normalized
                    };
                float dt = .30f + (float)random.NextDouble() * .13f;
                at += dt;
                player += heading * dt * (phase == 1 ? 4.4f : 3.1f);
                if (Mathf.Abs(player.x) > 43f) player.x = Mathf.Sign(player.x) * 43f;
                if (Mathf.Abs(player.z) > 10f) player.z = Mathf.Sign(player.z) * 10f;
                bool visible = i % 33 is < 24 or > 29;
                watch.Start();
                Step(policy, at, visible, visible ? player : Vector3.one * 10000f, drone, grid, route);
                var candidate = Read(policy, "SelectedCandidate");
                if (candidate != null && visible) Call(learning, "Capture", candidate, 1, at, at, .6f);
                watch.Stop();
                if (candidate != null)
                {
                    Vector3 destination = (Vector3)Field(candidate, "Position");
                    if (destination != previous)
                    {
                        if (previous != null)
                        {
                            switches++;
                            if (at - switchedAt < 1.2f) subcommitment++;
                        }
                        switchedAt = at;
                    }
                    previous = destination;
                    drone = Vector3.MoveTowards(drone, destination, dt * 5f);
                }
                else
                {
                    if (previous != null) lostPlans++;
                    previous = null;
                    drone = Vector3.MoveTowards(drone, (Vector3)Read(policy, "LastObserved") + Vector3.right * 4f, dt * 5f);
                }
                Record(i);
            }
            Tick(learning, at + 1f, close: true);
            Record(180);

            void Record(int step)
            {
                foreach (object entry in Items(memory, "Entries"))
                {
                    object result = Field(entry, "Evaluation");
                    if (result == null) continue;
                    object snapshot = Field(result, "Prediction");
                    if (!recorded.Add((int)Field(snapshot, "Id"))) continue;
                    float error = (float)Field(result, "Error");
                    float horizon = (float)Field(snapshot, "Expected") - (float)Field(snapshot, "Created");
                    if (!float.IsFinite(error)) continue;
                    errors.Add((error, horizon, step >= 90));
                    trials++;
                    if (error <= 2f && (float)Field(snapshot, "DroneEta") <= horizon + .6f) reachedInTime++;
                    if (Field(result, "Outcome").ToString() == "FailedPrediction") failedAt ??= at;
                    else if (failedAt.HasValue)
                    {
                        recovery.Add(at - failedAt.Value);
                        failedAt = null;
                    }
                }
            }
            Assert.That(errors, Is.Not.Empty);
            string Metrics(IEnumerable<float> values)
            {
                float[] ordered = values.OrderBy(value => value).ToArray();
                if (ordered.Length == 0) return "0 samples";
                float median = (ordered[(ordered.Length - 1) / 2] + ordered[ordered.Length / 2]) * .5f;
                return $"n={ordered.Length} MAE={ordered.Average():F3} median={median:F3} RMSE={Math.Sqrt(ordered.Average(e => e * e)):F3}";
            }
            TestContext.Out.WriteLine($"F4 trace {seed} {(enabled ? "adaptive" : "baseline")} · " +
                $"{Metrics(errors.Select(e => e.Error))}; early {Metrics(errors.Where(e => !e.Late).Select(e => e.Error))}; " +
                $"late {Metrics(errors.Where(e => e.Late).Select(e => e.Error))}; " +
                $"<2s {Metrics(errors.Where(e => e.Horizon < 2f).Select(e => e.Error))}; " +
                $"≥2s {Metrics(errors.Where(e => e.Horizon >= 2f).Select(e => e.Error))}; " +
                $"evaluated={Read(learning, "Evaluated")} unevaluated={Read(learning, "Unevaluated")}; " +
                $"ETA-qualified 2m forecasts={reachedInTime}/{trials}; " +
                $"blocked query attempts={blockedAttempts} repeats={repeated}; candidate switches={switches} ({subcommitment} within 1.2s) lost plans={lostPlans}; " +
                $"failure to next successful evaluation={recovery.Count} intervals" +
                (recovery.Count == 0 ? "; " : $" avg={recovery.Average():F2}s; ") +
                $"reflected policy replay {watch.Elapsed.TotalMilliseconds:F2} ms / 180 decisions.");
        }

        [Test]
        public void MeasureAllocatedLearningStorageAtConstruction()
        {
            var settings = New("InterceptorLearningSettings");
            var model = TypeOf("InterceptorLearning");
            var create = Expression.Lambda<Func<object>>(Expression.New(model.GetConstructors(Members).Single(),
                Expression.Constant(settings))).Compile();
            GC.KeepAlive(create());
            var hold = new object[256];
            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < hold.Length; i++) hold[i] = create();
            long bytes = GC.GetTotalMemory(true) - before;
            GC.KeepAlive(hold);
            Assert.That(bytes / hold.Length, Is.InRange(4000L, 25000L));
            TestContext.Out.WriteLine($"F4 retained managed heap delta: {bytes / hold.Length} bytes per unit, {hold.Length} instances.");
        }

        [Test]
        public void WarmLearningLoopHasBoundedStorageAndIsProfiled()
        {
            foreach (bool enabled in new[] { false, true })
            {
                var learning = Learning(enabled);
                object candidate = Forecast(learning, 0f, 1f, Vector3.zero);
                var time = Expression.Parameter(typeof(float));
                var pos = Expression.Parameter(typeof(Vector3));
                var obj = Expression.Constant(learning);
                var type = learning.GetType();
                var sample = TypeOf("InterceptorEvidence");
                var evidence = Expression.New(sample.GetConstructors(Members).Single(c => c.GetParameters().Length == 7),
                    Expression.Constant(EnumOf("InterceptorObservationSource", "DirectVisual")),
                    pos, Expression.Add(time, Expression.Constant(1f)), Expression.Add(time, Expression.Constant(3f)),
                    Expression.Constant(.2f), Expression.Constant(1f), Expression.Constant(2));
                var run = Expression.Lambda<Action<float, Vector3>>(Expression.Block(
                    Expression.Call(obj, type.GetMethod("Capture", Members), Expression.Constant(candidate), Expression.Constant(1),
                        time, time, Expression.Constant(.6f)),
                    Expression.Call(obj, type.GetMethod("Tick", Members), Expression.Add(time, Expression.Constant(1.4f)),
                        evidence, Expression.Constant(false))), time, pos).Compile();
                for (int i = 1; i <= 50; i++) run(i * 2f, Vector3.zero);
                var watch = new Stopwatch();
                byte[] calibration = null;
                bool wasEnabled = Profiler.enabled;
                Profiler.enabled = true;
                try
                {
                    using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 4096,
                        ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                    calibration = new byte[4096];
                    watch.Start();
                    for (int i = 51; i <= 1050; i++) run(i * 2f, Vector3.zero);
                    watch.Stop();
                    gc.Stop();
                    GC.KeepAlive(calibration);
                    Assert.That(gc.Valid && !gc.WrappedAround, Is.True);
                    long count = Enumerable.Range(0, gc.Count).Sum(i => gc.GetSample(i).Count);
                    Assert.That(count, Is.EqualTo(1), "Only the calibration array is expected.");
                    TestContext.Out.WriteLine($"F4 {(enabled ? "adaptive" : "baseline")} warm capture+evaluation: " +
                        $"{watch.Elapsed.TotalMilliseconds:F3} ms / 1000; {count - 1} GC allocation samples in loop.");
                }
                finally { Profiler.enabled = wasEnabled; }
                Assert.That(Items(Field(learning, "Memory"), "Entries").Length, Is.LessThanOrEqualTo(64));
            }
        }
    }
}

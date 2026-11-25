using System;
using System.Collections;
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
    public sealed class InterceptorSquadEditModeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Coordinator = T("InterceptorSquadCoordinator");
        private static readonly Type Task = T("InterceptorTask");
        private static readonly Type Row = typeof(System.Collections.Generic.List<>).MakeGenericType(Task);
        private static Type T(string name) => Type.GetType("ShiftFour." + name + ", Assembly-CSharp", true);
        private static object New(Type type, params object[] args) => Activator.CreateInstance(type, Members, null, args, null);
        private static object Call(object instance, string method, params object[] args) =>
            instance.GetType().GetMethod(method, Members).Invoke(instance, args);
        private static object Read(object instance, string property) => instance.GetType().GetProperty(property, Members).GetValue(instance);
        private static object Field(object instance, string field) => instance.GetType().GetField(field, Members).GetValue(instance);
        private static Vector3 X(float x) => Vector3.right * x;

        private static Array Matrix()
        {
            var matrix = Array.CreateInstance(Row, 4);
            for (int i = 0; i < matrix.Length; i++) matrix.SetValue(New(Row), i);
            return matrix;
        }

        private static object Add(Array matrix, int id, float point, float score, float eta = 2f,
            float until = 10f, bool activates = false)
        {
            var task = New(Task, id, X(point), score, eta, until, 3,
                Enum.Parse(T("InterceptorAction"), "SearchBeliefRegion"), activates);
            ((IList)matrix.GetValue(id - 1)).Add(task);
            return task;
        }

        private static object Selected(object coordinator, int id) => Call(coordinator, "Selected", id);
        private static void Solve(object coordinator, Array matrix, int mask = 15, int active = 0, int limit = 2, float time = 1f)
            => Call(coordinator, "Solve", matrix, mask, active, limit, time);
        private static Vector3 Destination(object coordinator, int id) => (Vector3)Field(Selected(coordinator, id), "Destination");

        [Test]
        public void GlobalOptimumChoosesComplementarySecondBestInsteadOfDuplicatePriority()
        {
            var matrix = Matrix();
            Add(matrix, 1, 0, .98f, 1.4f);
            Add(matrix, 1, 10, .83f, 2f);
            Add(matrix, 2, 0, .94f, 1.5f);
            Add(matrix, 2, 20, .25f, 3f);
            var coordinator = New(Coordinator);
            Solve(coordinator, matrix, active: 2);
            Assert.That(Destination(coordinator, 1), Is.EqualTo(X(10)));
            Assert.That(Destination(coordinator, 2), Is.EqualTo(X(0)));
            Assert.That(Read(coordinator, "GroupUtility"), Is.EqualTo(1.77f).Within(.001f));
        }

        [Test]
        public void AvailabilityHardValidityAndResponseBudgetPrecedeUtility()
        {
            var matrix = Matrix();
            Add(matrix, 1, 0, 1f, activates: true);
            Add(matrix, 2, 10, .94f, until: 1f, activates: true);
            Add(matrix, 3, 20, .9f, activates: true);
            Add(matrix, 4, 30, .8f, activates: true);
            var coordinator = New(Coordinator);
            Solve(coordinator, matrix, mask: 14, active: 1);
            Assert.That(Selected(coordinator, 1), Is.Null);
            Assert.That(Selected(coordinator, 2), Is.Null);
            Assert.That(Destination(coordinator, 3), Is.EqualTo(X(20)));
            Assert.That(Selected(coordinator, 4), Is.Null);
        }

        [Test]
        public void TiesFollowStableIdAndAnUnavailableOwnerReleasesTheDestination()
        {
            var matrix = Matrix();
            var first = Add(matrix, 1, 0, .8f);
            Add(matrix, 2, 0, .8f);
            var coordinator = New(Coordinator);
            Solve(coordinator, matrix);
            Assert.That(Selected(coordinator, 2), Is.Null);
            Assert.That(Destination(coordinator, 1), Is.EqualTo(X(0)));
            Call(coordinator, "Commit", first, 1f);
            Assert.That(Call(coordinator, "Conflicts", 2, X(0), 1.5f), Is.True);
            Assert.That(Call(coordinator, "Conflicts", 2, X(0), 2.2f), Is.False, "The short lease naturally expires.");
            Call(coordinator, "Release", 1);
            Solve(coordinator, matrix, mask: 14, time: 1.5f);
            Assert.That(Destination(coordinator, 2), Is.EqualTo(X(0)));
        }

        [Test]
        public void CommitmentKeepsValidPlanAndYieldsImmediatelyWhenItIsRemoved()
        {
            var matrix = Matrix();
            var first = Add(matrix, 1, 0, .6f);
            Add(matrix, 1, 10, .96f);
            var coordinator = New(Coordinator);
            Call(coordinator, "Commit", first, 1f);
            Solve(coordinator, matrix, time: 1.2f);
            Assert.That(Destination(coordinator, 1), Is.EqualTo(X(0)));
            ((IList)matrix.GetValue(0)).RemoveAt(0);
            Solve(coordinator, matrix, time: 1.3f);
            Assert.That(Destination(coordinator, 1), Is.EqualTo(X(10)));
        }

        [Test]
        public void SharedObservationKeepsOriginalValuesAndRejectsDuplicateExpiredOrInvalidReports()
        {
            var board = New(T("InterceptorSharedKnowledge"));
            object Sight(Vector3 position, float time, float expire) => New(T("InterceptorEvidence"),
                Enum.Parse(T("InterceptorObservationSource"), "DirectVisual"), position, time, expire, 1.25f, .9f, 1);
            var evidence = Sight(X(10), 1f, 4f);
            Assert.That(Call(board, "Publish", evidence, 1.5f), Is.True);
            Assert.That(Call(board, "Publish", evidence, 2f), Is.False);
            Assert.That(Call(board, "Publish", Sight(new Vector3(float.NaN, 0, 0), 2f, 4f), 2f), Is.False);
            var record = ((IEnumerable)Read(board, "Records")).Cast<object>().Single();
            Assert.That(Field(record, "Id"), Is.EqualTo(1));
            Assert.That(Field(Field(record, "Evidence"), "Time"), Is.EqualTo(1f));
            Call(board, "Expire", 4f);
            Assert.That(Read(board, "Records"), Is.Empty);
            Assert.That(Call(board, "Publish", evidence, 4f), Is.False);
        }

        [Test]
        public void ControlledComparisonAndBoundedSolverTiming()
        {
            // Same certified routes and ETAs for both policies. Success here means a unique
            // reachable area assignment, with arrival within the fixture's 2.2-second window.
            var rows = Matrix();
            var f2First = Add(rows, 1, 0, .98f, 1.4f);
            Add(rows, 1, 10, .83f, 2f);
            var f2Second = Add(rows, 2, 0, .94f, 1.5f);
            Add(rows, 2, 20, .25f, 3f);
            var coordinator = New(Coordinator);
            Solve(coordinator, rows, active: 2);
            object[] baseline = { f2First, f2Second };
            object[] cooperative = { Selected(coordinator, 1), Selected(coordinator, 2) };
            void Report(string name, object[] tasks)
            {
                int covered = tasks.Select(task => Field(task, "Destination")).Distinct().Count();
                float meanEta = tasks.Average(task => (float)Field(task, "Eta"));
                int success = tasks.Where(task => (float)Field(task, "Eta") <= 2.2f)
                    .Select(task => Field(task, "Destination")).Distinct().Count();
                TestContext.Out.WriteLine($"F3 comparison {name}: {tasks.Length} valid proposals chosen, {tasks.Length - covered} duplicate, " +
                    $"{covered} unique regions, {success} unique arrivals by deadline, {meanEta:F3}s mean ETA.");
            }
            Report("F2 independent", baseline);
            Report("F3 cooperative", cooperative);
            var direct = Expression.Lambda<System.Action>(
                Expression.Call(Expression.Constant(coordinator, Coordinator), Coordinator.GetMethod("Solve", Members),
                    Expression.Constant(rows, rows.GetType()), Expression.Constant(15), Expression.Constant(2),
                    Expression.Constant(2), Expression.Constant(1f))).Compile();
            direct();
            bool wasEnabled = Profiler.enabled;
            Profiler.enabled = true;
            try
            {
                long Measure(System.Action work)
                {
                    using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal,
                        "GC.Alloc", 4096, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                    work();
                    recorder.Stop();
                    Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
                    long count = 0;
                    for (int i = 0; i < recorder.Count; i++) count += recorder.GetSample(i).Count;
                    return count;
                }

                long calibration = Measure(() => GC.KeepAlive(new byte[1 << 16]));
                Assert.That(calibration, Is.GreaterThan(0));
                var clock = new Stopwatch();
                long allocations = Measure(() =>
                {
                    clock.Start();
                    for (int i = 0; i < 1000; i++) direct();
                    clock.Stop();
                });
                TestContext.Out.WriteLine($"F3 fixture: 1000 group solves in {clock.Elapsed.TotalMilliseconds:F3} ms, " +
                    $"{allocations} managed allocation events, {calibration} calibration events, " +
                    $"{Read(coordinator, "Evaluations")} compatible combinations per solve.");
            }
            finally { Profiler.enabled = wasEnabled; }
            Assert.That(Destination(coordinator, 1), Is.EqualTo(X(10)));
        }
    }
}

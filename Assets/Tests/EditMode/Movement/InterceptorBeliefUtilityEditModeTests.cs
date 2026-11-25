using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ShiftFour.Tests
{
    public sealed class InterceptorBeliefUtilityEditModeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Belief = RuntimeType("InterceptorBeliefState");
        private static readonly Type Evidence = RuntimeType("InterceptorEvidence");
        private static readonly Type Source = RuntimeType("InterceptorObservationSource");
        private static readonly Type Hypothesis = RuntimeType("InterceptorHypothesis");
        private static readonly Type Picker = RuntimeType("InterceptorUtilityDecision");
        private static readonly Type Settings = RuntimeType("InterceptorDecisionSettings");
        private static readonly Type Action = RuntimeType("InterceptorAction");
        private static readonly Func<Vector3, Vector3?> Project = p => p;
        private static readonly Func<Vector3, Vector3, Vector3[]> Path = (a, b) => new[] { a, b };
        private static readonly Array NoHypotheses = Array.CreateInstance(Hypothesis, 0);
        private static readonly Delegate Forecast = CreateForecast();
        private static Type RuntimeType(string name) => Type.GetType("ShiftFour." + name + ", Assembly-CSharp", true);
        private static object New(Type type, params object[] args) => Activator.CreateInstance(type, Members, null, args, null);
        private static object Call(object owner, string name, params object[] args) =>
            owner.GetType().GetMethod(name, Members).Invoke(owner, args);
        private static object Read(object owner, string name) => owner.GetType().GetProperty(name, Members).GetValue(owner);
        private static object Field(object owner, string name) => owner.GetType().GetField(name, Members).GetValue(owner);
        private static Vector3 X(float value) => Vector3.right * value;
        private static object[] Regions(object belief) => ((IEnumerable)Read(belief, "Regions")).Cast<object>().ToArray();
        private static float Probability(object region) => (float)Field(region, "Probability");
        private static object Sample(string source, Vector3 point, float time, float expiry, float radius, float strength, int sender = 1)
            => New(Evidence, Enum.Parse(Source, source), point, time, expiry, radius, strength, sender);
        private static bool Fuse(object belief, object sample, float now)
            => (bool)Call(belief, "Fuse", sample, now, Project, Path);
        private static void Update(object belief, float now, Func<Vector3, Vector3, Vector3[]> route)
            => Call(belief, "Propagate", now, 6f, .7f, Forecast, Project, route);
        private static Delegate CreateForecast()
        {
            var hypothesis = Expression.Parameter(Hypothesis);
            var age = Expression.Parameter(typeof(float));
            var advance = Expression.Multiply(Expression.Constant(Vector3.right),
                Expression.Multiply(age, Expression.Property(hypothesis, "Speed")));
            return Expression.Lambda(typeof(Func<,,>).MakeGenericType(Hypothesis, typeof(float), typeof(Vector3)),
                advance, hypothesis, age).Compile();
        }

        [Test]
        public void FreshVisionOverridesOlderOrRecentConflictingSound()
        {
            var state = New(Belief, 16);
            var visual = Sample("DirectVisual", X(0), 5f, 12f, .65f, 1f);
            Call(state, "Observe", visual, NoHypotheses);
            Assert.That(Fuse(state, Sample("ApproximateSound", X(12), 4.9f, 10f, 3f, .7f), 5.1f), Is.False);
            Assert.That(Fuse(state, Sample("ApproximateSound", X(12), 5.1f, 10f, 3f, .7f), 5.2f), Is.False);
            Assert.That(Regions(state).Length, Is.EqualTo(1));
            Assert.That(Probability(Regions(state)[0]), Is.EqualTo(1f));
            Assert.That(Fuse(state, Sample("ApproximateSound", X(8), 6f, 10f, 3f, .7f), 6f), Is.True);
            Assert.That(Regions(state).Length, Is.GreaterThan(1));
            Call(state, "Observe", Sample("DirectVisual", X(6), 6.5f, 12f, .65f, 1f), NoHypotheses);
            Assert.That(Field(Regions(state).Single(), "Position"), Is.EqualTo(X(6)));
        }

        [Test]
        public void ScoutAndSoundUseOriginalTimestampsAndDeduplicateWithoutExtendingExpiry()
        {
            var state = New(Belief, 8);
            var expired = Sample("ScoutReport", X(5), 1f, 2f, 2f, .8f);
            Assert.That(Fuse(state, expired, 3f), Is.False);
            var report = Sample("ScoutReport", X(5), 3f, 6f, 2f, .8f);
            Assert.That(Fuse(state, report, 3f), Is.True);
            Assert.That(Fuse(state, report, 4f), Is.False);
            var sound = Sample("ApproximateSound", X(9), 4f, 6f, 3f, .6f);
            Assert.That(Fuse(state, sound, 4f), Is.True);
            Assert.That(Fuse(state, sound, 4.2f), Is.False);
            var probabilities = Regions(state).Select(Probability).ToArray();
            Assert.That(probabilities.Length, Is.InRange(1, 8));
            Assert.That(probabilities.All(p => float.IsFinite(p) && p >= 0f), Is.True);
            Assert.That(probabilities.Sum(), Is.EqualTo(1f).Within(.001f));
            Update(state, 6f, Path);
            Assert.That(Regions(state), Is.Empty);
            Assert.That(Read(state, "Entropy"), Is.EqualTo(0f));
        }

        [Test]
        public void PropagationMovesOnlyAlongValidConnectionsAndRetainsEvidenceTime()
        {
            var state = New(Belief, 16);
            var hypothesis = New(Hypothesis, Enum.Parse(RuntimeType("InterceptorHypothesisKind"), "Continue"),
                1f, new[] { X(0), X(12) }, 3f, 0f);
            var hypotheses = Array.CreateInstance(Hypothesis, 1);
            hypotheses.SetValue(hypothesis, 0);
            Call(state, "Observe", Sample("DirectVisual", X(0), 1f, 8f, .65f, 1f), hypotheses);
            var blocked = (Func<Vector3, Vector3, Vector3[]>)((_, _) => null);
            Update(state, 1.3f, blocked);
            Assert.That(Regions(state).All(r => (Vector3)Field(r, "Position") == X(0)), Is.True);
            var firstRadius = (float)Field(Regions(state)[0], "Radius");
            Update(state, 1.8f, Path);
            var moved = Regions(state).Single(r => Field(r, "Hypothesis").ToString() == "Continue");
            Assert.That(((Vector3)Field(moved, "Position")).x, Is.EqualTo(2.4f).Within(.001f));
            Assert.That(Field(Field(moved, "Evidence"), "Time"), Is.EqualTo(1f));
            Assert.That((float)Field(moved, "Radius"), Is.GreaterThan(firstRadius));
            Assert.That(Regions(state).Sum(Probability), Is.EqualTo(1f).Within(.001f));
        }

        [Test]
        public void OcclusionPreservesProbabilityWhileCoveredInspectionReducesIt()
        {
            var state = New(Belief, 16);
            Assert.That(Fuse(state, Sample("ApproximateSound", X(0), 1f, 6f, 3f, .65f), 1f), Is.True);
            float prior = Probability(Regions(state)[0]);
            Call(state, "Inspect", 0, false);
            Assert.That(Probability(Regions(state)[0]), Is.EqualTo(prior).Within(.001f));
            Assert.That(Field(Regions(state)[0], "Inspection").ToString(), Is.EqualTo("Occluded"));
            Call(state, "Inspect", 0, true);
            Assert.That(Probability(Regions(state)[0]), Is.LessThan(prior));
            Assert.That(Field(Regions(state)[0], "Inspection").ToString(), Is.EqualTo("Inspected"));
            Assert.That(Regions(state).Sum(Probability), Is.EqualTo(1f).Within(.001f));
        }

        private static void Offer(object picker, string action, Vector3 destination, float value)
            => Call(picker, "Offer", Enum.Parse(Action, action), destination, value, 0f, 0f, "Controlled action");

        [Test]
        public void UtilityRespectsHardRejectionAndCommitment()
        {
            var picker = New(Picker, New(Settings));
            Offer(picker, "SearchBeliefRegion", X(5), .55f);
            Assert.That(Field(Call(picker, "Select", 1f, false), "Action").ToString(), Is.EqualTo("SearchBeliefRegion"));
            Call(picker, "Begin");
            Offer(picker, "SearchBeliefRegion", X(5), .55f);
            Offer(picker, "SearchBeliefRegion", X(8), .80f);
            Offer(picker, "Reposition", X(4), .90f);
            Assert.That(Field(Call(picker, "Select", 1.2f, false), "Destination"), Is.EqualTo(X(5)));
            Call(picker, "Reject", Enum.Parse(Action, "SearchBeliefRegion"), "Path blocked");
            Assert.That(Field(Call(picker, "Select", 1.25f, false), "Action").ToString(), Is.EqualTo("Reposition"));
            Call(picker, "Begin");
            Offer(picker, "ObserveTarget", X(4), .7f);
            Assert.That(Field(Call(picker, "Select", 1.3f, true), "Action").ToString(), Is.EqualTo("ObserveTarget"));
            var actions = ((IEnumerable)Read(picker, "Choices")).Cast<object>().ToArray();
            Assert.That(actions.Length, Is.EqualTo(9));
            Assert.That(Read(actions.Single(c => Field(c, "Action").ToString() == "Attack"), "Eligible"), Is.False);
            Call(picker, "Begin");
            Assert.That(Read(Call(picker, "Select", 1.4f, false), "Eligible"), Is.False,
                "A previous action cannot survive when all choices are rejected.");
        }

        [Test]
        public void MeasureBoundedBeliefAndUtilityFixture()
        {
            var state = New(Belief, 16);
            var picker = New(Picker, New(Settings));
            void Run(int steps)
            {
                for (int i = 1; i <= steps; i++)
                {
                    float now = i * .4f;
                    Fuse(state, Sample("ApproximateSound", X(i % 7), now, now + 3f, 3f, .6f), now);
                    Update(state, now + .1f, Path);
                    Call(picker, "Begin");
                    Offer(picker, "SearchBeliefRegion", X(3), .55f);
                    Offer(picker, "ReturnToPatrol", X(0), .3f);
                    Call(picker, "Select", now, false);
                }
            }
            Run(40);
            state = New(Belief, 16);
            var clock = Stopwatch.StartNew();
            Run(1000);
            clock.Stop();
            Assert.That(Regions(state).Length, Is.InRange(1, 16));
            TestContext.Out.WriteLine($"F2 fixture: 1000 fused/propagated selections, {clock.Elapsed.TotalMilliseconds:F3} ms including reflection.");
        }
    }
}

using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace ShiftFour
{
    public enum InterceptorHypothesisKind { Continue, Left, Right, Stop, Reverse, Alternate }

    public readonly struct InterceptorHypothesis
    {
        public InterceptorHypothesisKind Kind { get; }
        public float Weight { get; }
        public float Support { get; }
        public IReadOnlyList<Vector3> Route { get; }
        public float Speed { get; }
        public float Acceleration { get; }

        internal InterceptorHypothesis(InterceptorHypothesisKind kind, float weight, Vector3[] route,
            float speed, float acceleration) : this(kind, weight, route, speed, acceleration, weight) { }

        internal InterceptorHypothesis(InterceptorHypothesisKind kind, float weight, Vector3[] route,
            float speed, float acceleration, float support)
        {
            Kind = kind;
            Weight = weight;
            Support = support;
            Route = route;
            Speed = speed;
            Acceleration = acceleration;
        }
    }

    // Only accepted visual measurements enter this filter. All forecasts are derived values.
    internal sealed class InterceptorMotionEstimator
    {
        internal Vector3 Position { get; private set; }
        internal Vector3 Velocity { get; private set; }
        internal Vector3 Acceleration { get; private set; }
        internal float Quality { get; private set; }
        private Vector3 previous, previousSpeed;
        private float sampledAt = float.NegativeInfinity;
        private float noise = .35f;

        internal float Uncertainty(float now, InterceptorSettings settings)
        {
            float age = Mathf.Clamp(now - sampledAt, 0f, settings.observationExpiry);
            float drift = age * settings.uncertaintyGrowth * (1f + .2f * Velocity.magnitude);
            return Mathf.Sqrt(noise * noise + drift * drift);
        }

        internal string Observe(float now, Vector3 measurement, InterceptorSettings settings)
        {
            float dt = now - sampledAt;
            sampledAt = now;
            if (dt > settings.historySeconds)
            {
                Reset(measurement);
                return null;
            }

            Vector3 raw = Flat(measurement - previous) / Mathf.Max(dt, .02f);
            previous = measurement;
            float speed = raw.magnitude;
            if (speed > settings.maxPlayerSpeed * 1.25f)
            {
                Reset(measurement);
                noise = 1.5f;
                return "Implausible observation displacement";
            }

            float turn = Velocity.magnitude > settings.stoppedSpeed && speed > settings.stoppedSpeed
                ? Vector3.Angle(Velocity, raw) : 0f;
            string change = speed <= settings.stoppedSpeed ? "Player stopped" :
                turn > 140f ? "Player reversed" : turn > settings.sharpTurnDegrees ? "Sharp direction change" : null;
            Vector3 measuredSpeed = Vector3.ClampMagnitude(raw, settings.maxPlayerSpeed);
            Vector3 measuredAcceleration = Vector3.ClampMagnitude(
                (measuredSpeed - previousSpeed) / Mathf.Max(dt, .02f), settings.maxAcceleration);
            Vector3 forecast = Position + Velocity * dt + Acceleration * (.5f * dt * dt);
            noise = Mathf.Lerp(noise, Mathf.Clamp(Flat(measurement - forecast).magnitude, .20f, 3f), Gain(dt, 3f));
            previousSpeed = measuredSpeed;
            if (change != null)
            {
                Position = measurement;
                Acceleration = change == "Player stopped" ? measuredAcceleration : Vector3.zero;
                Velocity = change == "Player stopped" ? Vector3.zero : measuredSpeed;
                Quality = .12f;
                return change;
            }

            float velocityGain = 1f - Mathf.Pow(1f - Mathf.Clamp01(settings.velocityBlend), dt / .3f);
            Velocity = Vector3.Lerp(Velocity, measuredSpeed, velocityGain);
            Acceleration = Vector3.Lerp(Acceleration, measuredAcceleration, Gain(dt, 4f));
            Position = Vector3.Lerp(forecast, measurement, Gain(dt, 9f));
            Position = new Vector3(Position.x, measurement.y, Position.z);
            Quality = Mathf.MoveTowards(Quality, 1f, dt * .95f);
            return null;
        }

        private void Reset(Vector3 measurement)
        {
            Position = previous = measurement;
            Velocity = Acceleration = previousSpeed = Vector3.zero;
            Quality = 0f;
            noise = .35f;
        }

        private static float Gain(float dt, float rate) => 1f - Mathf.Exp(-dt * rate);
        internal static Vector3 Flat(Vector3 point) => new(point.x, 0f, point.z);
    }

    // Keep two cheap probes per heading group. Expensive routing is then bounded independently
    // of the world size, and each public hypothesis has a concrete validated path.
    internal sealed class InterceptorPredictionModel
    {
        private const int HeadingGroups = 5;
        private const int ProbesPerGroup = 2;
        private const int ProbeLimit = HeadingGroups * ProbesPerGroup;
        private const int HypothesisLimit = 6;
        private static readonly ProfilerMarker BuildMarker = new("ShiftFour.Interceptor.Prediction");
        private readonly Probe[] shortlist = new Probe[ProbeLimit];
        private readonly List<RouteOption> routes = new(ProbeLimit);
        private readonly RouteOption[] representative = new RouteOption[HypothesisLimit];
        private readonly List<InterceptorHypothesis> hypotheses = new(HypothesisLimit);
        private readonly Vector3[] stopRoute = new Vector3[1];

        private struct Probe
        {
            internal int Index;
            internal float Priority;
        }

        internal readonly struct RouteOption
        {
            internal readonly int Node;
            internal readonly InterceptorHypothesisKind Kind;
            internal readonly Vector3[] Path;
            internal readonly float Speed, Acceleration, Arrival, Heading, Support;

            internal RouteOption(int node, InterceptorHypothesisKind kind, Vector3[] path, float speed,
                float acceleration, float arrival, float heading, float support)
            {
                Node = node;
                Kind = kind;
                Path = path;
                Speed = speed;
                Acceleration = acceleration;
                Arrival = arrival;
                Heading = heading;
                Support = support;
            }
        }

        internal IReadOnlyList<RouteOption> Routes => routes;
        internal IReadOnlyList<InterceptorHypothesis> Hypotheses => hypotheses;
        internal float Horizon { get; private set; }
        private float maximumSpeed;

        internal void Clear()
        {
            routes.Clear();
            hypotheses.Clear();
        }

        internal void Build(Vector3 sighting, InterceptorMotionEstimator motion, InterceptorSettings settings,
            IReadOnlyList<Vector3> junctions, Func<Vector3, Vector3, Vector3[]> playerRoute, int incumbent,
            float horizon, InterceptorLearning learning, float now)
        {
            using var sample = BuildMarker.Auto();
            Clear();
            Array.Clear(representative, 0, representative.Length);
            for (int index = 0; index < shortlist.Length; index++) shortlist[index].Index = -1;
            Horizon = Mathf.Clamp(horizon, 1f, 5f);
            maximumSpeed = settings.maxPlayerSpeed;
            float speed = motion.Velocity.magnitude;
            Vector3 heading = motion.Velocity.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float alongAcceleration = Vector3.Dot(motion.Acceleration, heading);
            float lateralAcceleration = Vector3.Dot(motion.Acceleration, right);
            float braking = Mathf.Max(0f, -alongAcceleration);
            if (speed > settings.stoppedSpeed)
            {
                float reach = DistanceAt(speed, Mathf.Max(0f, alongAcceleration), maximumSpeed, Horizon);
                for (int node = 0; node < junctions.Count; node++)
                {
                    Vector3 offset = InterceptorMotionEstimator.Flat(junctions[node] - sighting);
                    float distance = offset.magnitude;
                    if (!float.IsFinite(distance) || distance < 1f || distance > reach) continue;
                    float projection = Vector3.Dot(offset / distance, heading);
                    if (projection < -.4f && braking < .8f) continue;
                    float side = Vector3.Dot(offset / distance, right);
                    int group = projection < -.4f ? 4 : projection >= .86f ? 0 :
                        projection >= .64f ? 3 : side < 0f ? 1 : 2;
                    float priority = node == incumbent ? 10f : distance / reach + .35f * Mathf.Abs(projection);
                    AddProbe(group, node, priority);
                }
                foreach (var probe in shortlist)
                    if (probe.Index >= 0) AddRoute(probe.Index, sighting, junctions[probe.Index], heading, right,
                        speed, alongAcceleration, lateralAcceleration, braking, playerRoute);
            }

            var forward = representative[(int)InterceptorHypothesisKind.Continue];
            stopRoute[0] = sighting;
            var stopPath = forward.Path ?? stopRoute;
            float stopSpeed = forward.Path != null ? speed : 0f;
            var stop = new RouteOption(-1, InterceptorHypothesisKind.Stop, stopPath, stopSpeed,
                -Mathf.Max(1.5f, braking), 0f, 0f, speed <= settings.stoppedSpeed ? 1f : .12f + .16f * braking);
            representative[(int)InterceptorHypothesisKind.Stop] = stop;

            float support = 0f, weighted = 0f;
            foreach (var option in representative)
            {
                support += option.Support;
                weighted += option.Support * (learning?.Weight(option.Kind, now) ?? 1f);
            }
            foreach (var option in representative)
                if (option.Path != null)
                    hypotheses.Add(new InterceptorHypothesis(option.Kind,
                        option.Support * (learning?.Weight(option.Kind, now) ?? 1f) / weighted, option.Path,
                        option.Speed, option.Acceleration, option.Support / support));
        }

        private void AddProbe(int group, int node, float priority)
        {
            int end = (group + 1) * ProbesPerGroup - 1;
            for (int index = group * ProbesPerGroup; index <= end; index++)
            {
                if (shortlist[index].Index >= 0 && shortlist[index].Priority >= priority) continue;
                for (int shift = end; shift > index; shift--) shortlist[shift] = shortlist[shift - 1];
                shortlist[index] = new Probe { Index = node, Priority = priority };
                break;
            }
        }

        private void AddRoute(int node, Vector3 sighting, Vector3 point, Vector3 heading, Vector3 right, float speed,
            float acceleration, float lateral, float braking, Func<Vector3, Vector3, Vector3[]> validate)
        {
            Vector3[] path = validate(sighting, point);
            if (!Complete(path, sighting, point)) return;
            Vector3 first = FirstDirection(path);
            float alignment = Vector3.Dot(first, heading);
            Vector3 offset = InterceptorMotionEstimator.Flat(point - sighting);
            float direction = Vector3.Dot(offset.normalized, heading);
            float side = Vector3.Dot(offset.normalized, right);
            Vector3 last = InterceptorMotionEstimator.Flat(path[path.Length - 1] - path[path.Length - 2]).normalized;
            InterceptorHypothesisKind kind;
            if (direction < -.4f)
            {
                if (braking < .8f || alignment > -.25f) return;
                kind = InterceptorHypothesisKind.Reverse;
            }
            else
            {
                if (alignment < -.12f || (alignment < .35f && direction > .65f)) return;
                kind = direction >= .86f && Vector3.Dot(last, heading) >= .8f ? InterceptorHypothesisKind.Continue :
                    direction >= .64f ? InterceptorHypothesisKind.Alternate :
                    side < 0f ? InterceptorHypothesisKind.Left : InterceptorHypothesisKind.Right;
            }

            float length = InterceptorPolicy.Length(path);
            float efficiency = Mathf.Clamp01(offset.magnitude / length);
            float routeSpeed = speed;
            float along = acceleration;
            float support;
            switch (kind)
            {
                case InterceptorHypothesisKind.Continue:
                    support = 1f / (1f + .18f * braking + .12f * Mathf.Abs(lateral));
                    break;
                case InterceptorHypothesisKind.Reverse:
                    routeSpeed *= .6f;
                    along = 0f;
                    support = Mathf.Min(.35f, braking * .08f);
                    break;
                default:
                    routeSpeed *= .88f;
                    along *= .5f;
                    support = .34f + Mathf.Clamp01(lateral * Mathf.Sign(side) * .16f) * .48f;
                    break;
            }
            float arrival = Arrival(length, routeSpeed, along);
            if (!float.IsFinite(arrival)) return;
            var option = new RouteOption(node, kind, path, routeSpeed, along, arrival,
                Mathf.Clamp01(kind == InterceptorHypothesisKind.Reverse ? -alignment : (1f + alignment) * .5f),
                support * efficiency);
            routes.Add(option);
            var previous = representative[(int)kind];
            if (previous.Path == null || option.Support * (1f + .2f * arrival / Horizon) >
                previous.Support * (1f + .2f * previous.Arrival / Horizon))
                representative[(int)kind] = option;
        }

        internal float Arrival(float distance, float speed, float acceleration)
        {
            if (DistanceAt(speed, acceleration, maximumSpeed, Horizon) < distance) return float.PositiveInfinity;
            float low = 0f, high = Horizon;
            for (int i = 0; i < 12; i++)
            {
                float mid = (low + high) * .5f;
                if (DistanceAt(speed, acceleration, maximumSpeed, mid) < distance) low = mid;
                else high = mid;
            }
            return high;
        }

        internal Vector3 Forecast(IReadOnlyList<Vector3> route, float speed, float acceleration, float seconds)
            => Along(route, DistanceAt(speed, acceleration, maximumSpeed, Mathf.Clamp(seconds, 0f, Horizon)));

        private static float DistanceAt(float speed, float acceleration, float limit, float seconds)
        {
            if (Mathf.Abs(acceleration) < .001f) return speed * seconds;
            float final = acceleration > 0f ? limit : 0f;
            float moving = Mathf.Min(seconds, Mathf.Max(0f, (final - speed) / acceleration));
            return speed * moving + .5f * acceleration * moving * moving + (seconds - moving) * final;
        }

        internal static bool Complete(Vector3[] path, Vector3 start, Vector3 end)
            => InterceptorPolicy.ValidRoute(path) &&
                (path[0] - start).sqrMagnitude <= .45f * .45f && (path[path.Length - 1] - end).sqrMagnitude <= .45f * .45f;

        internal static Vector3 FirstDirection(Vector3[] path)
        {
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 offset = InterceptorMotionEstimator.Flat(path[i] - path[0]);
                if (offset.sqrMagnitude >= .01f) return offset.normalized;
            }
            return Vector3.zero;
        }

        internal static Vector3 Along(IReadOnlyList<Vector3> path, float distance)
        {
            for (int i = 1; i < path.Count; i++)
            {
                float leg = Vector3.Distance(path[i - 1], path[i]);
                if (distance <= leg) return Vector3.Lerp(path[i - 1], path[i], leg > 0f ? distance / leg : 0f);
                distance -= leg;
            }
            return path[path.Count - 1];
        }
    }
}

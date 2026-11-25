using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace ShiftFour
{
    internal enum InterceptorRole { AreaSecurity, Reserve, Primary, Secondary, Search, Return }

    internal readonly struct InterceptorTask
    {
        internal readonly int Unit, Graph;
        internal readonly Vector3 Destination;
        internal readonly float Utility, Eta, Expires;
        internal readonly InterceptorAction Action;
        internal readonly bool Activates;

        internal InterceptorTask(int unit, Vector3 destination, float utility, float eta, float expires, int graph,
            InterceptorAction action, bool activates = false)
        {
            Unit = unit;
            Destination = destination;
            Utility = utility;
            Eta = eta;
            Expires = expires;
            Graph = graph;
            Action = action;
            Activates = activates;
        }

        internal bool Valid(float now) => Unit is >= 1 and <= 4 && float.IsFinite(Destination.sqrMagnitude) &&
            float.IsFinite(Utility) && Utility > 0f && Utility <= 1f && float.IsFinite(Eta) && Eta >= 0f && float.IsFinite(Expires) && Expires > now;
        internal bool Matches(InterceptorTask other) => Unit == other.Unit && Action == other.Action &&
            Graph == other.Graph && Conflicts(Destination, other.Destination, 1.1f);

        internal static bool Conflicts(Vector3 a, Vector3 b, float radius = 4f)
        {
            var delta = a - b;
            return delta.x * delta.x + delta.z * delta.z < radius * radius;
        }
    }

    internal sealed class InterceptorSquadCoordinator
    {
        internal struct Reservation
        {
            internal int Id;
            internal float Created, Until;
            internal InterceptorTask Task;
            internal bool Active(float now) => Id != 0 && Until > now && Task.Valid(now);
        }

        private static readonly ProfilerMarker Assignment = new("ShiftFour.InterceptorAssignment");
        private readonly InterceptorTask?[] current = new InterceptorTask?[4], best = new InterceptorTask?[4];
        private readonly Reservation[] reservations = new Reservation[4];
        private int sequence, committed;
        internal IReadOnlyList<Reservation> Reservations => reservations;
        internal float GroupUtility { get; private set; }
        internal int Evaluations { get; private set; }
        internal int Updates { get; private set; }
        internal int Reassignments { get; private set; }

        internal bool Conflicts(int unit, Vector3 point, float now)
        {
            foreach (var reservation in reservations)
                if (reservation.Active(now) && reservation.Task.Unit != unit &&
                    InterceptorTask.Conflicts(point, reservation.Task.Destination)) return true;
            return false;
        }

        internal void Release(int unit) => reservations[unit - 1] = default;

        internal void Commit(InterceptorTask task, float now)
        {
            ref Reservation lease = ref reservations[task.Unit - 1];
            if (lease.Active(now) && lease.Task.Matches(task))
            {
                lease.Task = task;
                lease.Until = Mathf.Min(task.Expires, now + 1.1f);
                return;
            }
            if (lease.Active(now)) Reassignments++;
            lease = new Reservation { Id = ++sequence, Task = task, Created = now, Until = Mathf.Min(task.Expires, now + 1.1f) };
        }

        internal void Solve(List<InterceptorTask>[] rows, int available, int active, int limit, float now)
        {
            using var sample = Assignment.Auto();
            Updates++;
            Evaluations = 0;
            GroupUtility = -1f;
            committed = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                var lease = reservations[i];
                if ((available & (1 << i)) == 0 || !lease.Active(now) || now - lease.Created >= .8f) continue;
                foreach (var task in rows[i])
                    if (task.Valid(now) && lease.Task.Matches(task))
                    {
                        committed |= 1 << i;
                        break;
                    }
            }
            Visit(rows, available, active, limit, now, 0, 0f);
        }

        internal InterceptorTask? Selected(int unit) => best[unit - 1];

        private void Visit(List<InterceptorTask>[] rows, int available, int active, int limit, float now, int index, float utility)
        {
            if (index == rows.Length)
            {
                Evaluations++;
                if (utility > GroupUtility + .00001f)
                {
                    GroupUtility = utility;
                    System.Array.Copy(current, best, best.Length);
                }
                return;
            }

            var lease = reservations[index];
            bool keepAssignment = (committed & (1 << index)) != 0;
            bool placed = false;
            if ((available & (1 << index)) != 0)
                foreach (var task in rows[index])
                {
                    if (task.Unit != index + 1 || !task.Valid(now) || task.Activates && active >= limit ||
                        keepAssignment && !lease.Task.Matches(task) || Overlaps(index, task.Destination, now)) continue;
                    current[index] = task;
                    placed = true;
                    float keep = lease.Active(now) && lease.Task.Matches(task) ? .10f : 0f;
                    Visit(rows, available, active + (task.Activates ? 1 : 0), limit, now, index + 1, utility + task.Utility + keep);
                }
            if (keepAssignment && placed) return;
            current[index] = null;
            Visit(rows, available, active, limit, now, index + 1, utility);
        }

        private bool Overlaps(int index, Vector3 point, float now)
        {
            for (int i = 0; i < reservations.Length; i++)
            {
                if (i == index) continue;
                if (i < index && current[i] is InterceptorTask chosen && InterceptorTask.Conflicts(point, chosen.Destination))
                    return true;
                // Previously committed destinations and immediate claims are exclusive.
                var lease = reservations[i];
                if ((committed & (1 << i)) != 0 && lease.Active(now) &&
                    InterceptorTask.Conflicts(point, lease.Task.Destination)) return true;
            }
            return false;
        }
    }

    internal sealed class InterceptorSharedKnowledge
    {
        internal readonly struct Record
        {
            internal readonly int Id;
            internal readonly InterceptorEvidence Evidence;
            internal Record(int id, InterceptorEvidence evidence) { Id = id; Evidence = evidence; }
        }

        private static readonly ProfilerMarker PublishMarker = new("ShiftFour.SharedObservation");
        private readonly List<Record> records = new(24);
        private int sequence;
        internal IReadOnlyList<Record> Records => records;

        internal void Expire(float now)
        {
            for (int i = records.Count - 1; i >= 0; i--)
                if (!records[i].Evidence.Fresh(now)) records.RemoveAt(i);
        }

        internal bool Publish(InterceptorEvidence evidence, float now)
        {
            using var sample = PublishMarker.Auto();
            if (!evidence.Fresh(now) || evidence.Source is not (InterceptorObservationSource.DirectVisual or
                InterceptorObservationSource.ApproximateSound or InterceptorObservationSource.ScoutReport or
                InterceptorObservationSource.InspectedRegion or InterceptorObservationSource.BlockedRoute)) return false;
            Expire(now);
            foreach (var record in records)
            {
                var source = record.Evidence;
                if (source.Source != evidence.Source) continue;
                if (source.Time == evidence.Time &&
                    (source.Sender == evidence.Sender || source.Source == InterceptorObservationSource.ApproximateSound)) return false;
                if (source.Source == InterceptorObservationSource.DirectVisual && source.Sender == evidence.Sender &&
                    evidence.Time - source.Time < .55f) return false;
                if (source.Source == InterceptorObservationSource.BlockedRoute && source.Route == evidence.Route &&
                    source.Graph == evidence.Graph && InterceptorTask.Conflicts(source.Position, evidence.Position, 1f)) return false;
                if (source.Source == InterceptorObservationSource.InspectedRegion &&
                    InterceptorTask.Conflicts(source.Position, evidence.Position, 1f)) return false;
            }
            if (records.Count == 24) records.RemoveAt(0);
            records.Add(new Record(++sequence, evidence));
            return true;
        }
    }
}

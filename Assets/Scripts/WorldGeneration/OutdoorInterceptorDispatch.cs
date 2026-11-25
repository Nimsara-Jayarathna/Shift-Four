using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // One dispatcher per generated world, listening to the same confirmed sightings as the Guard zones.
    [DisallowMultipleComponent]
    internal sealed class OutdoorInterceptorDispatch : MonoBehaviour
    {
        internal const int MaxStations = 4;
        private static readonly string[] BranchSites = { "Checkpoint", "Server", "Control", "Storage" };
        private static readonly string[] StationNames =
            { "01 Checkpoint Approach", "02 Central Avenue", "03 Control Exterior", "04 Storage Approach" };
        private static readonly float[] PatrolOffsets = { 0, -6, 6, -12, 12, -18, 18 };
        private static readonly float[] PatrolDepths = { 0, -5, 4, -10, 8 };

        private readonly List<InterceptorBrain> stations = new(MaxStations);
        private readonly List<InterceptorBrain> responders = new(MaxStations);
        private OutdoorScoutEncounter alerts;
        private float nextOrderAt, cooldown;
        private int responseLimit;
        private float warningUntil;
        private Vector3 lastReport;
        private float suppressReportUntil;
        private static readonly ProfilerMarker Coordination = new("ShiftFour.InterceptorCoordination");
        private readonly InterceptorSquadCoordinator coordinator = new(), recruiter = new();
        private readonly InterceptorSharedKnowledge knowledge = new();
        private readonly InterceptorBrain[] units = new InterceptorBrain[MaxStations];
        private readonly List<InterceptorTask>[] matrix = { new(6), new(6), new(6), new(6) };
        private readonly List<InterceptorTask>[] recruitment = { new(1), new(1), new(1), new(1) };
        private float coordinateAt;
        private int lastRecruitment;
        private int recruitedUnit;
        internal InterceptorSquadCoordinator Coordinator => coordinator;
        internal InterceptorSharedKnowledge Knowledge => knowledge;
        internal IReadOnlyList<List<InterceptorTask>> Matrix => matrix;
        internal InterceptorBrain Unit(int index) => units[index];
        internal int ConfiguredCount { get; private set; }
        internal int SpawnedCount => stations.Count;
        internal int ResponseLimit => responseLimit;
        internal int AvailableCount { get; private set; }
        internal int ActiveCount { get; private set; }

        internal bool HasWarning => isActiveAndEnabled && responders.Count > 0 &&
            Time.unscaledTime < warningUntil;

        internal static OutdoorInterceptorDispatch Deploy(Transform world, OutdoorScoutEncounter alerts, GameObject prefab,
            PlayerController player, float cooldown, float pursuitTime, float pursuitDistance, float leadHorizon, float visionDistance,
            int count, int responseLimit, int seed)
        {
            var hub = Group("OutdoorInterceptors", world).gameObject.AddComponent<OutdoorInterceptorDispatch>();
            var agent = prefab != null ? prefab.GetComponent<NavMeshAgent>() : null;
            var body = prefab != null ? prefab.GetComponent<DroneBody>() : null;
            if (agent == null || body == null || prefab.GetComponent<InterceptorBrain>() == null)
            {
                Debug.LogWarning("Open World: assign the original Interceptor prefab to deploy rapid response.", hub);
                return hub;
            }

            hub.ConfiguredCount = count;
            hub.cooldown = cooldown;
            hub.responseLimit = responseLimit;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Transform roads = world.Find("Roads");
            hub.alerts = alerts;
            var roadPosts = RoadPosts(roads, alerts, filter);
            var assignedPosts = new List<Vector3>(MaxStations * 4);
            for (int slot = 0; slot < count; slot++)
            {
                if (!FindPatrol(roads, slot, alerts, filter, body, assignedPosts, out Vector3[] posts))
                {
                    Debug.LogWarning($"Open World: no clear Interceptor patrol for {StationNames[slot]} on seed {seed}.", hub);
                    continue;
                }

                Transform station = Group(StationNames[slot], hub.transform);
                station.position = posts[0];
                var response = new OutdoorInterceptorResponse(alerts, hub, filter, posts, roadPosts,
                    pursuitTime, pursuitDistance, leadHorizon, visionDistance);
                response.UnitId = slot + 1;
                response.BuildGraph(station);
                var brain = OutdoorDroneEncounter.Spawn<InterceptorBrain>(world, prefab, player, BranchSites[slot], slot,
                    $"Interceptor {slot + 1:00}", seed, station, "Response Unit", interceptor: response);
                if (brain != null)
                {
                    hub.stations.Add(brain);
                    hub.units[slot] = brain;
                    assignedPosts.AddRange(posts);
                }
                else
                {
                    station.gameObject.SetActive(false);
                    Destroy(station.gameObject);
                }
            }

            if (Debug.isDebugBuild)
                hub.gameObject.AddComponent<OutdoorInterceptorDiagnostics>().Bind(player, hub.stations);
            alerts.InterceptorAlert += hub.OnScoutAlert;
            return hub;
        }

        private static bool FindPatrol(Transform roads, int slot, OutdoorScoutEncounter alerts, NavMeshQueryFilter filter,
            DroneBody body, List<Vector3> assignedPosts, out Vector3[] posts)
        {
            bool central = slot == 1;
            Transform branch = roads.Find(BranchSites[slot] + " branch");
            Transform avenue = roads.Find("Main avenue");
            Transform approach = roads.Find(BranchSites[slot] + " approach");
            posts = new Vector3[4];
            if (branch == null || avenue == null || approach == null) return false;

            // The central loop spans the two north junctions. Entrance loops follow the
            // branch-to-approach junction at their own building.
            float side = Mathf.Sign(approach.localPosition.x);
            float otherJunction = central ? roads.Find("Control branch").localPosition.z : branch.localPosition.z;
            Vector3 anchor = new(central ? avenue.localPosition.x : approach.localPosition.x - side * 6f,
                0f, (branch.localPosition.z + otherJunction) * .5f - (central ? 0f : 1f));
            float halfDepth = central ? Mathf.Abs(branch.localPosition.z - otherJunction) * .5f + 3.2f : 3.2f;
            Vector3 margin = body.EnvelopeExtents + Vector3.one * .06f;
            foreach (float depth in PatrolDepths)
            foreach (float offset in PatrolOffsets)
            {
                Vector3 center = anchor + new Vector3(central ? offset * .45f : -side * offset, 0f, depth);
                bool clear = true;
                for (int i = 0; i < posts.Length; i++)
                {
                    Vector3 corner = center + new Vector3(i is 0 or 3 ? -3.8f : 3.8f, 0f, i < 2 ? -halfDepth : halfDepth);
                    Vector3 probe = roads.TransformPoint(corner);
                    if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 1f, filter) ||
                        Mathf.Abs(hit.position.y - probe.y) > .25f || !alerts.AllowsTarget(hit.position) ||
                        assignedPosts.Exists(other => OutdoorScoutEncounter.FlatDistanceSquared(other, hit.position) < 6f * 6f) ||
                        Physics.CheckBox(hit.position + body.EnvelopeCenter, margin, Quaternion.identity,
                            WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore))
                    {
                        clear = false;
                        break;
                    }
                    posts[i] = hit.position;
                }
                if (!clear) continue;

                for (int i = 0; i < posts.Length && clear; i++)
                {
                    Vector3 from = posts[i], to = posts[(i + 1) % posts.Length], leg = to - from;
                    float length = leg.magnitude;
                    clear = length >= 4f && !NavMesh.Raycast(from, to, out _, filter) &&
                        !Physics.BoxCast(from + body.EnvelopeCenter, margin, leg / length, Quaternion.identity,
                            length, WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore);
                    int samples = Mathf.CeilToInt(length);
                    for (int step = 1; step < samples && clear; step++)
                        clear = alerts.AllowsTarget(Vector3.Lerp(from, to, (float)step / samples));
                }
                if (clear) return true;
            }
            return false;
        }

        private static List<Vector3> RoadPosts(Transform roads, OutdoorScoutEncounter alerts,
            NavMeshQueryFilter filter)
        {
            var points = new List<Vector3>();
            foreach (Transform road in roads)
            {
                if (!road.TryGetComponent<BoxCollider>(out var pavement) || !pavement.enabled) continue;
                Vector3 size = road.localScale;
                bool alongX = size.x > size.z;
                float length = alongX ? size.x : size.z;
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 7f));
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 offset = (alongX ? Vector3.right : Vector3.forward) * ((length - 2f) * (i / (float)steps - .5f));
                    Vector3 probe = roads.TransformPoint(road.localPosition + offset);
                    if (NavMesh.SamplePosition(probe, out NavMeshHit hit, .9f, filter) && alerts.AllowsTarget(hit.position) &&
                        !points.Exists(other => OutdoorScoutEncounter.FlatDistanceSquared(other, hit.position) < 3f * 3f))
                        points.Add(hit.position);
                }
            }
            return points;
        }

        internal float Cooldown => cooldown;

        private bool HasSlot()
        {
            int active = 0;
            foreach (var unit in responders)
                if (unit != null && unit.IsResponding && unit.CoordinationAvailable && ++active >= responseLimit) return false;
            return gameObject.activeInHierarchy;
        }

        internal bool TryReserve(InterceptorBrain unit)
        {
            if (unit == null || !unit.CoordinationAvailable || !HasSlot()) return false;
            // A returning unit can finish its patrol return before the dispatcher has ticked.
            if (!responders.Contains(unit)) responders.Add(unit);
            return true;
        }

        internal void Share(InterceptorEvidence evidence)
        {
            if (isActiveAndEnabled && alerts.AllowsTarget(evidence.Position)) knowledge.Publish(evidence, Time.time);
        }

        internal bool Claim(InterceptorTask task)
        {
            if (!isActiveAndEnabled) return true;
            float now = Time.time;
            if (!task.Valid(now) || coordinator.Conflicts(task.Unit, task.Destination, now)) return false;
            var lease = coordinator.Reservations[task.Unit - 1];
            // If the optimizer selected another validated proposal, let the local agent consume
            // that assignment before overwriting its reservation with an immediate preference.
            if (lease.Active(now) && now - lease.Created < .8f && !lease.Task.Matches(task)) return false;
            coordinator.Commit(task, now);
            return true;
        }

        internal void Release(int unit)
        {
            if (unit is < 1 or > MaxStations) return;
            if (coordinator.Reservations[unit - 1].Id == 0) return;
            coordinator.Release(unit);
            coordinateAt = Time.time;
        }

        private void OnScoutAlert(OutdoorScout scout, InterceptorEvidence evidence)
        {
            if (!isActiveAndEnabled || scout == null || scout.State == OutdoorScout.Activity.Destroyed ||
                evidence.Source != InterceptorObservationSource.ScoutReport || !alerts.AllowsTarget(evidence.Position) ||
                !knowledge.Publish(evidence, Time.time)) return;
            // Keep the immediate Scout response and retain the original report when both slots are busy.
            Recruit(knowledge.Records[knowledge.Records.Count - 1], Time.time);
        }

        private bool Recruit(InterceptorSharedKnowledge.Record record, float now)
        {
            var evidence = record.Evidence;
            if (!HasSlot() || now < nextOrderAt || record.Id <= lastRecruitment || !evidence.Fresh(now) ||
                evidence.Source is not (InterceptorObservationSource.ScoutReport or InterceptorObservationSource.DirectVisual))
                return false;
            if (now < suppressReportUntil && InterceptorTask.Conflicts(evidence.Position, lastReport, 8f)) return false;

            int available = 0;
            for (int i = 0; i < MaxStations; i++)
            {
                recruitment[i].Clear();
                var unit = units[i];
                if (unit == null || !unit.CoordinationAvailable || unit.IsResponding ||
                    !unit.TryPlanSquadResponse(evidence.Position, out var goal, out float length)) continue;
                float eta = length / unit.ResponseTravelSpeed;
                // Keep a fixed reaction margin inside the original observation deadline.
                if (now + eta + .6f >= evidence.Expires) continue;
                float utility = Mathf.Clamp01(.56f + .35f * evidence.Strength(now) - .22f * Mathf.Clamp01(eta / 12f));
                recruitment[i].Add(new InterceptorTask(i + 1, goal, utility, eta, evidence.Expires,
                    unit.DiagnosticGraph.Version, InterceptorAction.PursueLastKnown, true));
                available |= 1 << i;
            }

            recruiter.Solve(recruitment, available, 0, 1, now);
            for (int i = 0; i < MaxStations; i++)
            {
                if (recruiter.Selected(i + 1) is not InterceptorTask task ||
                    coordinator.Conflicts(task.Unit, task.Destination, now)) continue;
                var unit = units[i];
                if (!TryReserve(unit)) return false;
                lastReport = evidence.Position;
                lastRecruitment = record.Id;
                recruitedUnit = i + 1;
                nextOrderAt = now + cooldown;
                warningUntil = Time.unscaledTime + 2.8f;
                coordinator.Commit(task, now);
                unit.DispatchResponse(evidence.Position, task.Destination, task.Eta * unit.ResponseTravelSpeed, evidence);
                return true;
            }
            return false;
        }

        private void LateUpdate()
        {
            using var sample = Coordination.Auto();
            float now = Time.time;
            int available = 0, active = 0;
            bool unavailable = false;
            AvailableCount = 0;
            for (int i = 0; i < MaxStations; i++)
            {
                var unit = units[i];
                if (unit == null || !unit.CoordinationAvailable)
                {
                    if (coordinator.Reservations[i].Id != 0) coordinator.Release(i + 1);
                    if (recruitedUnit == i + 1)
                    {
                        unavailable = true;
                        lastRecruitment = 0; // A fresh report may justify a replacement.
                        recruitedUnit = 0;
                        coordinateAt = now;
                    }
                    continue;
                }
                AvailableCount++;
                available |= 1 << i;
                if (unit.IsResponding) active++;
                else coordinator.Release(i + 1);
                var lease = coordinator.Reservations[i];
                if (lease.Id != 0 && (!lease.Active(now) || lease.Task.Graph != unit.DiagnosticGraph.Version))
                {
                    coordinator.Release(i + 1);
                    coordinateAt = now;
                }
            }
            ActiveCount = active;
            if (responders.RemoveAll(unit => unit == null || !unit.IsResponding || !unit.CoordinationAvailable) != 0 && !unavailable)
            {
                nextOrderAt = Mathf.Max(nextOrderAt, now + Mathf.Min(2.5f, cooldown));
                suppressReportUntil = now + cooldown;
            }
            if (Time.timeScale <= 0f || now < coordinateAt) return;
            coordinateAt = now + .5f;
            knowledge.Expire(now);

            for (int i = 0; i < MaxStations; i++)
            {
                matrix[i].Clear();
                var unit = units[i];
                if ((available & (1 << i)) != 0 && unit.IsResponding)
                    unit.DiagnosticResponse.CopyTasks(matrix[i], now);
            }
            coordinator.Solve(matrix, available, active, responseLimit, now);
            // Release obsolete leases before publishing the whole selected batch.
            for (int i = 0; i < MaxStations; i++)
                if (matrix[i].Count > 0 && coordinator.Selected(i + 1) == null) coordinator.Release(i + 1);
            for (int i = 0; i < MaxStations; i++)
                if (coordinator.Selected(i + 1) is InterceptorTask task) coordinator.Commit(task, now);

            if (active >= responseLimit || now < nextOrderAt) return;
            // Prefer fresh direct vision. A queued Scout report remains usable until its original expiry.
            var records = knowledge.Records;
            int attempts = 0;
            for (int i = records.Count - 1; i >= 0 && attempts < 2; i--)
            {
                var record = records[i];
                if (record.Id <= lastRecruitment || record.Evidence.Source is not
                    (InterceptorObservationSource.DirectVisual or InterceptorObservationSource.ScoutReport)) continue;
                attempts++;
                if (Recruit(record, now)) break;
            }
        }

        internal InterceptorRole Role(int unit, float now)
        {
            var brain = units[unit - 1];
            if (brain == null) return InterceptorRole.Reserve;
            var response = brain.DiagnosticResponse;
            if (response.State == OutdoorInterceptorResponse.Phase.Return) return InterceptorRole.Return;
            if (!brain.IsResponding) return ActiveCount >= ResponseLimit ? InterceptorRole.Reserve : InterceptorRole.AreaSecurity;
            var own = coordinator.Reservations[unit - 1];
            if (own.Active(now) && own.Task.Action is InterceptorAction.SearchBeliefRegion or InterceptorAction.InvestigateSound)
                return InterceptorRole.Search;
            float value = own.Active(now) ? PrimaryValue(own.Task) : 0f;
            foreach (var other in coordinator.Reservations)
                if (other.Active(now) && other.Task.Unit != unit &&
                    other.Task.Action is not (InterceptorAction.SearchBeliefRegion or InterceptorAction.InvestigateSound) &&
                    (PrimaryValue(other.Task) > value || PrimaryValue(other.Task) == value && other.Task.Unit < unit))
                    return InterceptorRole.Secondary;
            return InterceptorRole.Primary;
        }

        private static float PrimaryValue(InterceptorTask task)
            => (task.Action == InterceptorAction.Intercept ? 1f : 0f) + task.Utility;

        private void OnEnable()
        {
            if (alerts != null) alerts.InterceptorAlert += OnScoutAlert;
        }

        private void OnDisable()
        {
            if (alerts != null) alerts.InterceptorAlert -= OnScoutAlert;
            for (int slot = 1; slot <= MaxStations; slot++) coordinator.Release(slot);
        }
    }
}

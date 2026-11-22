using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // One dispatcher per generated world, listening to the same confirmed sightings as the Guard zones.
    [DisallowMultipleComponent]
    internal sealed class OutdoorInterceptorDispatch : MonoBehaviour
    {
        private readonly List<InterceptorBrain> stations = new List<InterceptorBrain>(3);
        private readonly List<InterceptorBrain> responders = new List<InterceptorBrain>(3);
        private OutdoorScoutEncounter alerts;
        private float nextOrderAt, cooldown;
        private int responseLimit;
        private float warningUntil;
        private Vector3 lastReport;
        private float suppressReportUntil;

        internal bool HasWarning => isActiveAndEnabled && responders.Count > 0 &&
            Time.unscaledTime < warningUntil;

        internal static OutdoorInterceptorDispatch Deploy(Transform world, OutdoorScoutEncounter alerts, GameObject prefab,
            PlayerController player, float boundary, float cooldown, float pursuitTime, float pursuitDistance, float leadHorizon,
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

            hub.cooldown = cooldown;
            hub.responseLimit = responseLimit;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Transform roads = world.Find("Roads");
            var roadPosts = RoadPosts(world, roads, alerts, filter);
            float south = roads.Find("Checkpoint branch").localPosition.z;
            float north = roads.Find("Control branch").localPosition.z;
            for (int slot = 0; slot < count; slot++)
            {
                Vector3 anchor = slot switch
                {
                    0 => new Vector3(10f, 0f, Mathf.Lerp(south, north, .55f)),
                    1 => new Vector3(boundary - 8.5f, 0f, boundary * .46f),
                    _ => new Vector3(-boundary + 9f, 0f, boundary * .5f)
                };
                if (!FindStation(world, anchor, slot, boundary, alerts, filter, body, out Vector3 point))
                {
                    Debug.LogWarning($"Open World: no clear Interceptor station {slot + 1} on seed {seed}.", hub);
                    continue;
                }

                Transform station = Group(slot switch
                {
                    0 => "01 Central Station",
                    1 => "02 Perimeter Station",
                    _ => "03 West Station"
                }, hub.transform);
                station.position = point;
                var response = new OutdoorInterceptorResponse(alerts, filter, point, roadPosts, pursuitTime, pursuitDistance,
                    leadHorizon);
                response.BuildGraph(station);
                var brain = OutdoorDroneEncounter.Spawn<InterceptorBrain>(world, prefab, player, "Control", 2,
                    $"Interceptor {slot + 1:00}", seed, station, "Response Unit",
                    interceptor: response);
                if (brain != null) hub.stations.Add(brain);
            }

            hub.alerts = alerts;
            alerts.AlertRaised += hub.OnScoutAlert;
            return hub;
        }

        private static bool FindStation(Transform world, Vector3 anchor, int slot, float boundary,
            OutdoorScoutEncounter alerts, NavMeshQueryFilter filter, DroneBody body, out Vector3 point)
        {
            var deployed = world.GetComponentsInChildren<DroneBody>();
            var pavement = world.Find("Roads").GetComponentsInChildren<BoxCollider>();
            foreach (float radius in new[] { 0f, 4f, 8f, 12f, 17f, 23f })
            for (int direction = 0; direction < (radius == 0f ? 1 : 12); direction++)
            {
                Vector3 local = anchor + Quaternion.Euler(0f, direction * 30f, 0f) * Vector3.forward * radius;
                if ((slot == 1 && local.x < boundary - 22f) || (slot == 2 && local.x > -boundary + 22f)) continue;
                if (NavMesh.SamplePosition(world.TransformPoint(local), out NavMeshHit hit, 1.5f, filter) &&
                    Mathf.Abs(world.InverseTransformPoint(hit.position).y) < .35f && alerts.AllowsTarget(hit.position) &&
                    System.Array.TrueForAll(deployed, other =>
                        OutdoorScoutEncounter.FlatDistanceSquared(hit.position, other.transform.position) >= 4f * 4f) &&
                    System.Array.TrueForAll(pavement, road => !OverPavement(road, hit.position, body.HorizontalRadius)) &&
                    !Physics.CheckBox(hit.position + body.EnvelopeCenter, body.EnvelopeExtents, Quaternion.identity,
                        WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore))
                {
                    point = hit.position;
                    return true;
                }
            }
            point = default;
            return false;
        }

        private static bool OverPavement(BoxCollider road, Vector3 point, float clearance)
        {
            if (!road.enabled) return false;
            Bounds footprint = road.bounds;
            return point.x >= footprint.min.x - clearance && point.x <= footprint.max.x + clearance &&
                point.z >= footprint.min.z - clearance && point.z <= footprint.max.z + clearance;
        }

        private static List<Vector3> RoadPosts(Transform world, Transform roads, OutdoorScoutEncounter alerts,
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
                    Vector3 probe = world.TransformPoint(road.localPosition + offset);
                    if (NavMesh.SamplePosition(probe, out NavMeshHit hit, .9f, filter) && alerts.AllowsTarget(hit.position) &&
                        !points.Exists(other => OutdoorScoutEncounter.FlatDistanceSquared(other, hit.position) < 3f * 3f))
                        points.Add(hit.position);
                }
            }
            return points;
        }

        private void OnScoutAlert(OutdoorScout scout, Vector3 report)
        {
            if (!isActiveAndEnabled || scout == null || scout.State == OutdoorScout.Activity.Destroyed ||
                Time.time < nextOrderAt || responders.Count >= responseLimit || !alerts.AllowsTarget(report)) return;
            if (Time.time < suppressReportUntil &&
                OutdoorScoutEncounter.FlatDistanceSquared(report, lastReport) < 8f * 8f) return;
            InterceptorBrain nearest = null;
            Vector3 destination = default;
            float shortest = float.PositiveInfinity;
            foreach (InterceptorBrain station in stations)
            {
                if (station == null || station.IsResponding ||
                    !station.TryPlanResponse(report, out Vector3 goal, out float cost) || cost >= shortest)
                    continue;
                nearest = station;
                shortest = cost;
                destination = goal;
            }
            if (nearest == null)
            {
                nextOrderAt = Time.time + .5f;
                return;
            }

            responders.Add(nearest);
            lastReport = report;
            nextOrderAt = Time.time + cooldown;
            warningUntil = Time.unscaledTime + 2.8f;
            nearest.DispatchResponse(report, destination, shortest);
            Debug.Log($"{nearest.name} responding to {scout.name} at {report} ({shortest:0} m route).", nearest);
        }

        private void LateUpdate()
        {
            if (responders.RemoveAll(unit => unit == null || !unit.IsResponding) == 0) return;
            nextOrderAt = Mathf.Max(nextOrderAt, Time.time + Mathf.Min(2.5f, cooldown));
            suppressReportUntil = Time.time + cooldown;
        }

        private void OnDisable()
        {
            if (alerts != null) alerts.AlertRaised -= OnScoutAlert;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    // Owns the shared outdoor navigation registration for the lifetime of this generated world.
    [DisallowMultipleComponent]
    public sealed class OutdoorScoutEncounter : MonoBehaviour
    {
        private NavMeshData navigation;
        private NavMeshDataInstance registration;

        private Transform world;
        private Vector3 spawnPosition;
        private Transform cache;
        private BoxCollider[] foundations;
        private float boundary;
        private float alertUntil, nextShot;
        private int attackLimit;
        private const float AttackLeaseSeconds = 1.35f;
        private readonly List<(Health Drone, float Expires)> firing = new List<(Health, float)>(3);
        private OutdoorScout notifyingScout;
        internal event Action<OutdoorScout, Vector3> AlertRaised;
        internal bool HasAlert => isActiveAndEnabled && notifyingScout != null &&
            notifyingScout.State != OutdoorScout.Activity.Destroyed && Time.unscaledTime < alertUntil;

        internal static OutdoorScoutEncounter Build(Transform world, GameObject prefab, Vector3 spawn,
            float boundary, int seed, int attackLimit, params GameObject[] otherDrones)
        {
            var encounter = PrototypeGeometry.Group("OutdoorScoutPatrols", world).gameObject.AddComponent<OutdoorScoutEncounter>();
            encounter.world = world;
            encounter.spawnPosition = spawn;
            encounter.cache = world.Find(OutdoorSupplyCache.HierarchyName);
            encounter.boundary = boundary;
            encounter.attackLimit = attackLimit;
            try
            {
                var foundations = new List<BoxCollider>();
                foreach (Transform building in world.Find("Buildings"))
                    foundations.Add(building.Find("Foundation").GetComponent<BoxCollider>());
                encounter.foundations = foundations.ToArray();
                encounter.BakeNavigation(prefab, otherDrones);
                return encounter;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Open World drone navigation failed for seed {seed}: {error.Message}", encounter);
                encounter.gameObject.SetActive(false);
                Destroy(encounter.gameObject);
                return null;
            }
        }

        internal bool AllowsTarget(Vector3 point)
        {
            if (!InsidePerimeter(point) || FlatDistanceSquared(point, spawnPosition) < 14f * 14f ||
                (cache != null && FlatDistanceSquared(point, cache.position) < 11f * 11f)) return false;
            foreach (BoxCollider floor in foundations)
            {
                Vector3 local = floor.transform.InverseTransformPoint(point) - floor.center;
                if (Mathf.Abs(local.x) <= floor.size.x * .5f && Mathf.Abs(local.z) <= floor.size.z * .5f)
                    return false;
            }
            return true;
        }

        internal bool InsidePerimeter(Vector3 point)
        {
            Vector3 local = world.InverseTransformPoint(point);
            return Mathf.Abs(local.x) < boundary - 1.5f && Mathf.Abs(local.z) < boundary - 1.5f;
        }

        internal void PublishAlert(OutdoorScout scout, Vector3 lastSeen)
        {
            if (!HasAlert)
            {
                notifyingScout = scout;
                alertUntil = Time.unscaledTime + 2.6f;
            }
            AlertRaised?.Invoke(scout, lastSeen);
        }

        internal bool TryClaimShot()
        {
            if (Time.time < nextShot) return false;
            nextShot = Time.time + .75f;
            return true;
        }

        internal bool CanFire(Health drone)
        {
            float now = Time.time;
            for (int i = firing.Count - 1; i >= 0; i--)
            {
                var lease = firing[i];
                if (lease.Expires <= now || lease.Drone == null ||
                    !lease.Drone.IsAlive || !lease.Drone.gameObject.activeInHierarchy) firing.RemoveAt(i);
            }
            return firing.Count < attackLimit || FiringSlot(drone) >= 0;
        }

        internal void RecordFire(Health drone)
        {
            int index = FiringSlot(drone);
            var lease = (drone, Time.time + AttackLeaseSeconds);
            if (index < 0) firing.Add(lease);
            else firing[index] = lease;
        }

        private int FiringSlot(Health drone)
        {
            for (int i = 0; i < firing.Count; i++)
                if (firing[i].Drone == drone) return i;
            return -1;
        }

        private void BakeNavigation(GameObject scout, GameObject[] others)
        {
            var navigationPrefab = HasNavigation(scout) ? scout : Array.Find(others, HasNavigation);
            if (navigationPrefab == null) throw new InvalidOperationException("No assigned drone prefab has a NavMeshAgent and DroneBody.");
            var settings = NavMesh.GetSettingsByID(navigationPrefab.GetComponent<NavMeshAgent>().agentTypeID);
            settings.agentRadius = 0f;
            settings.agentHeight = 0f;
            IncludeFootprint(ref settings, navigationPrefab);
            foreach (var other in others)
            {
                if (!HasNavigation(other) || other.GetComponent<NavMeshAgent>().agentTypeID != settings.agentTypeID) continue;
                IncludeFootprint(ref settings, other);
            }
            settings.agentClimb = .25f;
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(world, WeaponController.GameplayRaycastMask, NavMeshCollectGeometry.PhysicsColliders,
                0, new List<NavMeshBuildMarkup>(), sources);
            if (sources.Count == 0) throw new InvalidOperationException("Generated geometry provided no navigation sources.");

            foreach (BoxCollider foundation in foundations)
                Exclude(sources, foundation.bounds, 6f);
            foreach (string group in new[] { "Trees", "Rocks", "Crates", "Fences" })
                foreach (Collider solid in world.Find("Environment/" + group).GetComponentsInChildren<Collider>())
                    if (solid.enabled && !solid.isTrigger)
                    {
                        Bounds footprint = solid.bounds;
                        if (group == "Trees")
                        {
                            // Rendered conifer crowns extend beyond the collidable trunks.
                            float diameter = (footprint.size.y + 2.6f) * .57f;
                            footprint.size = new Vector3(diameter, footprint.size.y, diameter);
                        }
                        else if (group == "Rocks")
                        {
                            // A flying hull must also clear the wider visible rock silhouette.
                            float diameter = OutdoorScenery.BoulderSize.x * 2f * 1.05f;
                            footprint.size = new Vector3(diameter, footprint.size.y, diameter);
                        }
                        Exclude(sources, footprint, 5f);
                    }

            Transform storage = world.Find("Buildings/Storage");
            // Keep aerial routes outside the dock's loading lane and tall cargo as a unit.
            Exclude(sources, new Bounds(storage.TransformPoint(new Vector3(11f, 1f,
                -ResearchBuildingBuilder.FrontOffset(3) - 4.3f)), new Vector3(10f, 4f, 7.5f)), 5f);

            Bounds ground = world.Find("Ground").GetComponent<Collider>().bounds;
            var bounds = new Bounds(world.position + Vector3.up, new Vector3(ground.size.x, 4f, ground.size.z));
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("Runtime NavMesh bake returned no data.");
            registration = NavMesh.AddNavMeshData(navigation);
            if (!registration.valid) throw new InvalidOperationException("Runtime NavMesh could not be registered.");
        }

        private static bool HasNavigation(GameObject prefab)
            => prefab != null && prefab.GetComponent<NavMeshAgent>() != null && prefab.GetComponent<DroneBody>() != null;

        private static void IncludeFootprint(ref NavMeshBuildSettings settings, GameObject prefab)
        {
            var body = prefab.GetComponent<DroneBody>();
            settings.agentRadius = Mathf.Max(settings.agentRadius, body.HorizontalRadius * Mathf.Sqrt(2f) + .04f);
            settings.agentHeight = Mathf.Max(settings.agentHeight, prefab.GetComponent<NavMeshAgent>().height,
                body.EnvelopeCenter.y + body.EnvelopeExtents.y);
        }

        internal OutdoorScout Deploy(GameObject prefab, PlayerController player, int seed, float height, float range,
            float fieldOfView, float pursuitDistance, float searchDuration, PrototypeMaterials materials, int count)
        {
            if (!HasNavigation(prefab) || prefab.GetComponent<ScoutBrain>() == null ||
                prefab.GetComponentInChildren<DroneRotorVisuals>() == null || player == null || player.ViewCamera == null ||
                player.GetComponent<Health>() == null)
            {
                Debug.LogWarning("Outdoor Scouts need the original Scout prefab and an FPS player with a camera and Health.", this);
                return null;
            }

            var agent = prefab.GetComponent<NavMeshAgent>();
            var body = prefab.GetComponent<DroneBody>();
            float heightOffset = height - body.EnvelopeCenter.y;
            var routes = new OutdoorScoutRoutes(world, boundary, seed, body.EnvelopeCenter + Vector3.up * heightOffset,
                body.EnvelopeExtents, new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask });
            OutdoorScout extractionScout = null;
            for (int index = 0; index < count; index++)
            {
                Vector3[] patrol = routes.Find(index);
                if (patrol == null)
                {
                    Debug.LogWarning($"Outdoor Scout {index + 1:00}: no safe {OutdoorScoutRoutes.Names[index]} route for seed {seed}.", this);
                    continue;
                }

                Transform station = PrototypeGeometry.Group($"{index + 1:00} {OutdoorScoutRoutes.Names[index]}", transform);
                Transform waypoints = PrototypeGeometry.Group("Patrol Waypoints", station);
                for (int i = 0; i < patrol.Length; i++)
                    PrototypeGeometry.Group($"Post {i + 1}", waypoints).position = patrol[i];

                station.gameObject.SetActive(false);
                var scout = Instantiate(prefab, patrol[0], Quaternion.LookRotation(patrol[1] - patrol[0]), station);
                scout.name = $"Outdoor Scout {index + 1:00}";
                var original = scout.GetComponent<ScoutBrain>();
                original.enabled = false;
                scout.GetComponent<DroneBody>().SetFlightOffset(heightOffset);
                var presentation = scout.GetComponentInChildren<DroneRotorVisuals>(true);
                presentation.transform.position += Vector3.up * heightOffset;
                var marker = PrototypeGeometry.Box("Alert Sensor", scout.transform,
                    new Vector3(0f, body.EnvelopeCenter.y + heightOffset + .35f, .34f),
                    new Vector3(.24f, .15f, .22f), materials.Checkpoint, false);
                var adapter = scout.AddComponent<OutdoorScout>();
                adapter.Configure(original, player, patrol, this, range, fieldOfView, pursuitDistance, searchDuration,
                    marker.transform, marker.GetComponent<Renderer>());
                var scoutAgent = scout.GetComponent<NavMeshAgent>();
                scoutAgent.avoidancePriority = 36 + index * 4;
                scoutAgent.autoRepath = false;
                station.gameObject.SetActive(true);

                if (!scoutAgent.isOnNavMesh || presentation.WeaponMount == null ||
                    presentation.Effects == null || !Array.Exists(presentation.GetComponentsInChildren<MeshRenderer>(),
                        mesh => mesh.enabled && mesh.sharedMaterial != null && mesh.GetComponent<MeshFilter>()?.sharedMesh != null))
                {
                    Debug.LogWarning($"{scout.name} could not initialize its navigation, visible model or weapon.", scout);
                    station.gameObject.SetActive(false);
                    Destroy(station.gameObject);
                    continue;
                }

                // The Checkpoint-area Scout continues to count toward the existing extraction objective.
                if (index == 4) extractionScout = adapter;
                Debug.Log($"{scout.name} spawned for seed {seed} on {station.name} with {patrol.Length} connected posts.", scout);
            }
            return extractionScout;
        }

        internal static float FlatDistanceSquared(Vector3 first, Vector3 second)
        {
            Vector3 delta = first - second;
            return delta.x * delta.x + delta.z * delta.z;
        }

        private static void Exclude(List<NavMeshBuildSource> sources, Bounds footprint, float height)
        {
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.Translate(footprint.center),
                size = new Vector3(footprint.size.x, Mathf.Max(height, footprint.size.y), footprint.size.z),
                area = 1 // Unity's built-in Not Walkable area.
            });
        }

        private void OnDisable()
        {
            if (registration.valid) registration.Remove();
        }

        private void OnDestroy()
        {
            if (navigation != null) Destroy(navigation);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static ShiftFour.PrototypeGeometry;
using Random = System.Random;

namespace ShiftFour
{
    internal readonly struct FlankerAmbushSettings
    {
        internal readonly float Detection, Damage, ShotInterval, Recovery, Radius, Pursuit, AttackRange;

        internal FlankerAmbushSettings(float detection, float damage, float shotInterval, float recovery,
            float radius, float pursuit, float attackRange)
        {
            Detection = detection;
            Damage = damage;
            ShotInterval = shotInterval;
            Recovery = recovery;
            Radius = radius;
            Pursuit = pursuit;
            AttackRange = attackRange;
        }
    }

    [DisallowMultipleComponent]
    internal sealed class OutdoorFlankerAmbushes : MonoBehaviour
    {
        private static readonly string[] Names = { "Checkpoint to Server", "Server to Control", "Control to Storage" };
        private static readonly string[] Buildings = { "Checkpoint", "Control", "Storage" };
        private static readonly int[] BuildingIndices = { 0, 2, 3 };

        private Transform world;
        private BoxCollider[] roads;
        private float boundary;
        private Vector3 spawn;
        private Transform supply;
        private readonly Vector3[] centers = new Vector3[3];
        private readonly Vector3[] preferredPosts = new Vector3[3];
        private OutdoorScoutEncounter outdoors;
        private FlankerBrain active;
        private Health activeHealth;
        private float nextAmbush;
        internal FlankerBrain Warning => LiveAmbush && active.IsAmbushWarning ? active : null;

        private bool LiveAmbush => active != null && active.isActiveAndEnabled && activeHealth != null && activeHealth.IsAlive;

        internal static OutdoorFlankerAmbushes Prepare(Transform world, Vector3 spawn, float boundary,
            PrototypeMaterials materials, int seed)
        {
            var result = Group("OutdoorFlankerAmbushes", world).gameObject.AddComponent<OutdoorFlankerAmbushes>();
            result.world = world;
            result.spawn = spawn;
            result.boundary = boundary;
            result.supply = world.Find(OutdoorSupplyCache.HierarchyName);
            result.roads = world.Find("Roads").GetComponentsInChildren<BoxCollider>();
            result.PlaceShelters(materials, seed);
            return result;
        }

        private void PlaceShelters(PrototypeMaterials materials, int seed)
        {
            Transform roadRoot = world.Find("Roads");
            var random = new Random(seed ^ 0x1984af);
            Vector3 checkpoint = roadRoot.Find("Checkpoint branch").localPosition;
            Vector3 server = roadRoot.Find("Server branch").localPosition;
            centers[0] = world.TransformPoint(new Vector3(0f, 0f, Mathf.Lerp(checkpoint.z, server.z, .43f)));
            centers[1] = BranchCenter("Control branch", .53f);
            centers[2] = BranchCenter("Storage branch", .62f);

            for (int i = 0; i < centers.Length; i++)
            {
                Vector3 along = i == 0 ? world.forward : world.right;
                Vector3 shoulder = i == 0 ? -world.right : i == 1 ? world.forward : -world.forward;
                float side = i == 0 ? 12.4f : 8.5f;
                preferredPosts[i] = centers[i] + shoulder * side;
                float jitter = (float)random.NextDouble() * 1.4f - .7f;
                foreach (float offset in new[] { 0f, 3.5f, -3.5f, 6f, -6f })
                {
                    Vector3 shelter = preferredPosts[i] + along * (jitter + offset - 2.8f);
                    Vector3 staged = shelter + along * 3.2f;
                    Quaternion rotation = Quaternion.LookRotation(along);
                    if (!ClearShoulder(shelter) || !ClearShoulder(staged) ||
                        Physics.CheckBox(shelter + Vector3.up * 1.42f, new Vector3(2.12f, 1.26f, .75f),
                            rotation, WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore)) continue;

                    var group = Group($"Shelter {i + 1:00}", transform);
                    group.SetPositionAndRotation(shelter, rotation);
                    Box("Utility windbreak", group, Vector3.up * 1.25f, new Vector3(4f, 2.5f, .45f), materials.Metal);
                    Box("Warning stripe", group, new Vector3(0f, 1.8f, -.245f), new Vector3(3.7f, .17f, .035f),
                        materials.Checkpoint, false);
                    preferredPosts[i] = staged;
                    break;
                }
            }
        }

        private Vector3 BranchCenter(string branchName, float fraction)
        {
            Vector3 road = world.Find("Roads/" + branchName).localPosition;
            return world.TransformPoint(new Vector3(road.x * 2f * fraction, 0f, road.z));
        }

        private bool ClearShoulder(Vector3 position)
        {
            Vector3 local = world.InverseTransformPoint(position);
            if (Mathf.Abs(local.x) > boundary - 3.5f || Mathf.Abs(local.z) > boundary - 3.5f ||
                OutdoorScoutEncounter.FlatDistanceSquared(position, spawn) < 16f * 16f ||
                (supply != null && OutdoorScoutEncounter.FlatDistanceSquared(position, supply.position) < 13f * 13f))
                return false;
            // Pavement or a footprint can be visually obstructed above the actual floor collider.
            foreach (BoxCollider road in roads)
            {
                if (!road.enabled) continue;
                Bounds footprint = road.bounds;
                footprint.Expand(new Vector3(3f, 5f, 3f));
                if (footprint.Contains(position)) return false;
            }
            foreach (Transform building in world.Find("Buildings"))
            {
                Bounds footprint = building.Find("Foundation").GetComponent<BoxCollider>().bounds;
                footprint.Expand(new Vector3(5f, 5f, 5f));
                if (footprint.Contains(position)) return false;
            }
            return true;
        }

        internal FlankerBrain Deploy(GameObject prefab, PlayerController player, OutdoorScoutEncounter outdoorNavigation,
            FlankerAmbushSettings settings, PrototypeMaterials materials, Font font, int seed)
        {
            outdoors = outdoorNavigation;
            var agent = prefab != null ? prefab.GetComponent<NavMeshAgent>() : null;
            var body = prefab != null ? prefab.GetComponent<DroneBody>() : null;
            if (agent == null || body == null || prefab.GetComponent<FlankerBrain>() == null)
            {
                Debug.LogWarning("Open World: assign the original Flanker prefab to deploy road ambushes.", this);
                return null;
            }

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            FlankerBrain extractionFlanker = null;
            for (int i = 0; i < centers.Length; i++)
            {
                var zone = new OutdoorFlankerAmbush(this, centers[i], i, settings, filter);
                if (!FindPost(zone, preferredPosts[i], body, filter, out Vector3 post))
                {
                    Debug.LogWarning($"Open World: no clear {Names[i]} Flanker staging position on seed {seed}.", this);
                    continue;
                }

                zone.SetPost(post);
                Transform group = Group($"{i + 1:00} {Names[i]}", transform);
                group.position = centers[i];
                WaypointGraph graph = BuildGraph(group, zone, post, body, filter);
                zone.SetGraph(graph, graph.NodeCount > 1 ? graph.NodePosition(1) : post);
                Material accent = i == 0 ? materials.Checkpoint : i == 1 ? materials.Control : materials.Storage;
                FlankerBrain flanker = OutdoorDroneEncounter.Spawn<FlankerBrain>(world, prefab, player, Buildings[i],
                    BuildingIndices[i], $"Flanker {i + 1:00}", accent, materials, font, seed, group, "Ambush Post",
                    ambush: zone);
                if (i == 2) extractionFlanker = flanker;
            }
            return extractionFlanker;
        }

        private bool FindPost(OutdoorFlankerAmbush zone, Vector3 preferred, DroneBody body, NavMeshQueryFilter filter,
            out Vector3 post)
        {
            var occupied = world.GetComponentsInChildren<DroneBody>();
            foreach (float radius in new[] { 0f, 3f, 5f, 8f, 11f, 14f })
            for (int angle = 0; angle < (radius == 0f ? 1 : 12); angle++)
            {
                Vector3 candidate = preferred + Quaternion.Euler(0f, angle * 30f, 0f) * Vector3.forward * radius;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.2f, filter) && ClearShoulder(hit.position) &&
                    zone.CanTravel(hit.position) && System.Array.TrueForAll(occupied,
                        drone => OutdoorScoutEncounter.FlatDistanceSquared(drone.transform.position, hit.position) >= 3.5f * 3.5f) &&
                    !Physics.CheckBox(hit.position + body.EnvelopeCenter, body.EnvelopeExtents, Quaternion.identity,
                        WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore))
                {
                    post = hit.position;
                    return true;
                }
            }

            post = default;
            return false;
        }

        private static WaypointGraph BuildGraph(Transform group, OutdoorFlankerAmbush zone, Vector3 post,
            DroneBody body, NavMeshQueryFilter filter)
        {
            Transform root = Group("Ambush Waypoints", group);
            var nodes = new List<Transform>();
            AddPost(post);
            for (int i = 0; i < 8; i++)
            {
                Vector3 candidate = post + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 5f;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.3f, filter) &&
                    zone.CanConnect(post, hit.position) &&
                    !Physics.CheckBox(hit.position + body.EnvelopeCenter, body.EnvelopeExtents, Quaternion.identity,
                        WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore))
                    AddPost(hit.position);
            }

            var edges = new List<GraphEdge>();
            for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
                if (zone.CanConnect(nodes[i].position, nodes[j].position)) edges.Add(new GraphEdge(i, j, -1));

            var graph = root.gameObject.AddComponent<WaypointGraph>();
            graph.Configure(nodes.ToArray(), edges.ToArray());
            graph.SetConnectionValidator(zone.CanConnect);
            return graph;

            void AddPost(Vector3 point)
            {
                Transform node = Group($"Post {nodes.Count + 1}", root);
                node.position = point;
                nodes.Add(node);
            }
        }

        internal bool IsOutdoorTarget(Vector3 point) => outdoors != null && outdoors.AllowsTarget(point);

        internal bool CanEngage(FlankerBrain flanker)
            => LiveAmbush ? active == flanker : Time.time >= nextAmbush;

        internal bool Claim(FlankerBrain flanker)
        {
            if (!CanEngage(flanker)) return false;
            active = flanker;
            activeHealth = flanker.GetComponent<Health>();
            return true;
        }

        internal void Release(FlankerBrain flanker)
        {
            if (active != flanker) return;
            active = null;
            activeHealth = null;
            nextAmbush = Time.time + 1.15f;
        }
    }
}

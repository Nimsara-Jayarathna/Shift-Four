using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // Supplies an outdoor post, waypoints and player to an original prefab brain.
    internal sealed class OutdoorDroneEncounter : MonoBehaviour
    {
        private Transform marker;
        private Camera playerView;
        private Health health;

        internal static T Spawn<T>(Transform world, GameObject prefab, PlayerController player, string building,
            int buildingIndex, string role, Material accent, PrototypeMaterials materials, Font font, int seed,
            Transform parent = null, string post = null, IndoorGuardZone zone = null, int slot = 0,
            OutdoorFlankerAmbush ambush = null, OutdoorInterceptorResponse interceptor = null)
            where T : DroneBrain
        {
            Transform area = world.Find($"Buildings/{building}");
            var encounter = Group(post ?? building + " Security", parent != null ? parent : world)
                .gameObject.AddComponent<OutdoorDroneEncounter>();
            try
            {
                if (area == null) throw new InvalidOperationException($"The generated {building} building is missing.");
                if (prefab == null) throw new InvalidOperationException($"Assign the original {role} Prefab on World Generator.");
                var agent = prefab.GetComponent<NavMeshAgent>();
                var body = prefab.GetComponent<DroneBody>();
                if (agent == null || body == null || prefab.GetComponent<T>() == null ||
                    prefab.GetComponent<DroneMotor>() == null || prefab.GetComponent<Health>() == null ||
                    prefab.GetComponentInChildren<DroneRotorVisuals>() == null)
                    throw new InvalidOperationException($"The prefab needs {typeof(T).Name}, NavMeshAgent, DroneBody, DroneMotor, Health and rotor visuals.");
                if (player == null || player.ViewCamera == null || player.GetComponent<Health>() == null)
                    throw new InvalidOperationException("Spawn needs an outdoor player with a camera and Health.");

                T brain = encounter.Build<T>(area, prefab, player, agent, body, buildingIndex, role, accent,
                    materials, font, zone, slot, ambush, interceptor);
                Debug.Log($"Open World: {role} spawned by {building}, {encounter.name}, on seed {seed} using {typeof(T).Name}.", encounter);
                return brain;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Open World: {role} spawn failed by {building} on seed {seed}. {error.Message}", encounter);
                encounter.gameObject.SetActive(false);
                Destroy(encounter.gameObject);
                return null;
            }
        }

        private T Build<T>(Transform area, GameObject prefab, PlayerController player, NavMeshAgent templateAgent,
            DroneBody body, int buildingIndex, string role, Material accent, PrototypeMaterials materials, Font font,
            IndoorGuardZone zone, int slot, OutdoorFlankerAmbush ambush, OutdoorInterceptorResponse interceptor)
            where T : DroneBrain
        {
            float side = buildingIndex == 1 ? 1f : -1f;
            float front = -ResearchBuildingBuilder.FrontOffset(buildingIndex);
            var filter = new NavMeshQueryFilter { agentTypeID = templateAgent.agentTypeID, areaMask = templateAgent.areaMask };
            var route = new NavMeshPath();
            Vector3[] patrol = interceptor?.Posts ?? ambush?.Posts ??
                (zone != null ? zone.Patrol(slot) : FindPatrol(area, body, front, side, filter, route));
            if (patrol == null) throw new InvalidOperationException("No clear four-post patrol loop on the outdoor NavMesh.");
            var graph = interceptor?.Graph ?? ambush?.Graph ??
                (zone != null ? zone.Graph : BuildGraph(area, body, patrol, front, side, filter, route));

            Transform staging = Group(role + " Unit", transform);
            staging.gameObject.SetActive(false);
            Vector3 heading = ambush != null ? Vector3.ProjectOnPlane(ambush.Center - patrol[0], Vector3.up) :
                patrol[1] - patrol[0];
            var instance = Instantiate(prefab, patrol[0], heading.sqrMagnitude > .001f ?
                Quaternion.LookRotation(heading) : Quaternion.identity, staging);
            instance.name = (zone != null ? "Indoor " : "Outdoor ") + role;
            T brain = instance.GetComponent<T>();
            brain.Configure(instance.name, patrol[0]);
            brain.ConfigureOutdoor(player, graph, patrol);
            if (zone != null && brain is GuardBrain guard)
            {
                guard.ConfigureZone(zone, slot);
                var navigation = instance.GetComponent<NavMeshAgent>();
                navigation.avoidancePriority = 46 + slot * 3;
                navigation.autoRepath = false;
            }
            if (ambush != null && brain is FlankerBrain flanker)
            {
                flanker.ConfigureAmbush(ambush, player);
                instance.GetComponent<NavMeshAgent>().autoRepath = false;
            }
            if (interceptor != null && brain is InterceptorBrain responder)
            {
                responder.ConfigureResponse(interceptor);
                instance.GetComponent<NavMeshAgent>().autoRepath = false;
            }
            playerView = player.ViewCamera;
            health = instance.GetComponent<Health>();
            marker = OutdoorScoutEncounter.CreateMarker(instance.transform, materials, font, playerView,
                role.ToUpperInvariant() + " DRONE", accent);
            staging.gameObject.SetActive(true);
            if (!instance.GetComponent<NavMeshAgent>().isOnNavMesh)
                throw new InvalidOperationException("The prefab could not bind to the baked NavMesh.");
            var visuals = instance.GetComponentInChildren<DroneRotorVisuals>();
            if (visuals.WeaponMount == null || visuals.Effects == null ||
                !Array.Exists(visuals.GetComponentsInChildren<MeshRenderer>(), mesh => mesh.enabled &&
                    mesh.GetComponent<MeshFilter>()?.sharedMesh != null))
                throw new InvalidOperationException("The prefab failed to initialize its visible model, weapon mount or shot effects.");
            return brain;
        }

        private static Vector3[] FindPatrol(Transform area, DroneBody body, float front, float side,
            NavMeshQueryFilter filter, NavMeshPath route)
        {
            var corners = new Vector3[4];
            foreach (float shift in new[] { 0f, .85f, -.85f })
            {
                float near = front - 7f + shift;
                float far = near + 3f;
                if (!TryPost(area, new Vector3(side * 10.4f, 0, near), body, filter, out corners[0]) ||
                    !TryPost(area, new Vector3(side * 10.4f, 0, far), body, filter, out corners[1]) ||
                    !TryPost(area, new Vector3(side * 5.5f, 0, far), body, filter, out corners[2]) ||
                    !TryPost(area, new Vector3(side * 5.5f, 0, near), body, filter, out corners[3])) continue;
                bool connected = true;
                for (int i = 0; i < corners.Length; i++)
                    connected &= Connected(corners[i], corners[(i + 1) % corners.Length], filter, route);
                if (connected) return corners;
            }
            return null;
        }

        private WaypointGraph BuildGraph(Transform area, DroneBody body, Vector3[] patrol, float front,
            float side, NavMeshQueryFilter filter, NavMeshPath route)
        {
            Transform root = Group("Outdoor Waypoint Graph", transform);
            var nodes = new List<Transform>();
            foreach (Vector3 post in patrol) AddNode(root, nodes, post);
            foreach (Vector3 local in new[]
            {
                new Vector3(0f, 0, front - 10f), new Vector3(side * 14.5f, 0, front - 10f),
                new Vector3(side * 15.5f, 0, front - 2.5f), new Vector3(side * 16.5f, 0, front + 2.5f),
                new Vector3(side * 8f, 0, front - 15f)
            })
                if (TryPost(area, local, body, filter, out Vector3 point)) AddNode(root, nodes, point);

            var edges = new List<GraphEdge>();
            for (int from = 0; from < nodes.Count; from++)
            for (int to = from + 1; to < nodes.Count; to++)
                if (Connected(nodes[from].position, nodes[to].position, filter, route))
                    edges.Add(new GraphEdge(from, to, -1));
            var graph = root.gameObject.AddComponent<WaypointGraph>();
            graph.Configure(nodes.ToArray(), edges.ToArray());
            return graph;
        }

        private static void AddNode(Transform root, List<Transform> nodes, Vector3 point)
        {
            Transform node = Group($"Post {nodes.Count + 1}", root);
            node.position = point;
            nodes.Add(node);
        }

        internal static bool TryPost(Transform area, Vector3 local, DroneBody body, NavMeshQueryFilter filter, out Vector3 point)
        {
            point = default;
            if (!NavMesh.SamplePosition(area.TransformPoint(local), out NavMeshHit hit, .9f, filter) ||
                Physics.CheckBox(hit.position + body.EnvelopeCenter, body.EnvelopeExtents, Quaternion.identity,
                    WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore)) return false;
            point = hit.position;
            return true;
        }

        private static bool Connected(Vector3 from, Vector3 to, NavMeshQueryFilter filter, NavMeshPath route)
            => NavMesh.CalculatePath(from, to, filter, route) && route.status == NavMeshPathStatus.PathComplete;

        private void LateUpdate()
        {
            if (marker == null || playerView == null || health == null) return;
            if (!health.IsAlive) { marker.gameObject.SetActive(false); enabled = false; return; }
            Vector3 facing = marker.position - playerView.transform.position;
            if (facing.sqrMagnitude > .001f) marker.rotation = Quaternion.LookRotation(facing);
        }
    }
}

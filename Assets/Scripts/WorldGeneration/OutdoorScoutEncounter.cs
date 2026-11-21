using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    // Owns one runtime bake, scoped to the Checkpoint patrol and pursuit area.
    [DisallowMultipleComponent]
    public sealed class OutdoorScoutEncounter : MonoBehaviour
    {
        private NavMeshData navigation;
        private NavMeshDataInstance registration;

        internal static Transform Spawn(Transform world, GameObject prefab, PlayerController player,
            float detectionRange, PrototypeMaterials materials, Font font, int seed)
        {
            Transform checkpoint = world.Find("Buildings/Checkpoint");
            var encounter = PrototypeGeometry.Group("Checkpoint Security", world).gameObject.AddComponent<OutdoorScoutEncounter>();
            try
            {
                if (prefab == null) throw new InvalidOperationException("World Generator has no Scout Prefab assigned.");
                if (checkpoint == null) throw new InvalidOperationException("Generated Checkpoint building is missing.");
                if (player == null || player.ViewCamera == null || player.GetComponent<Health>() == null)
                    throw new InvalidOperationException("Spawn has no configured FPS player, camera or Health.");
                return encounter.Build(world, checkpoint, prefab, player, detectionRange, materials, font, seed);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Outdoor Scout spawn failed for seed {seed}: {error.Message}", encounter);
            }
            encounter.gameObject.SetActive(false);
            Destroy(encounter.gameObject);
            return null;
        }

        private Transform Build(Transform world, Transform checkpoint, GameObject prefab, PlayerController player,
            float range, PrototypeMaterials materials, Font font, int seed)
        {
            var templateAgent = prefab.GetComponent<NavMeshAgent>();
            var templateBody = prefab.GetComponent<DroneBody>();
            if (templateAgent == null || templateBody == null || prefab.GetComponent<ScoutBrain>() == null
                || prefab.GetComponentInChildren<DroneRotorVisuals>() == null)
                throw new InvalidOperationException("Scout Prefab needs its NavMeshAgent, DroneBody, ScoutBrain and rotor visuals.");
            range = float.IsNaN(range) ? 24f : Mathf.Clamp(range, 6f, 40f);
            float leash = Mathf.Max(30f, range + 5f);

            var settings = NavMesh.GetSettingsByID(templateAgent.agentTypeID);
            settings.agentRadius = templateBody.HorizontalRadius * Mathf.Sqrt(2f) + .04f;
            settings.agentHeight = Mathf.Max(templateAgent.height, templateBody.EnvelopeCenter.y + templateBody.EnvelopeExtents.y);
            settings.agentClimb = .25f;
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(world, WeaponController.GameplayRaycastMask, NavMeshCollectGeometry.PhysicsColliders,
                0, new List<NavMeshBuildMarkup>(), sources);
            if (sources.Count == 0) throw new InvalidOperationException("Generated geometry provided no navigation sources.");

            // Keep indoor floors and raised prop surfaces out of this outdoor navigation.
            foreach (Transform building in world.Find("Buildings"))
                Exclude(sources, building.Find("Foundation").GetComponent<BoxCollider>().bounds, 6f);
            foreach (string group in new[] { "Trees", "Rocks", "Crates", "Fences" })
                foreach (Collider solid in world.Find("Environment/" + group).GetComponentsInChildren<Collider>())
                    if (solid.enabled && !solid.isTrigger) Exclude(sources, solid.bounds, 4f);

            var bounds = new Bounds(checkpoint.position + Vector3.up,
                new Vector3((leash + 3f) * 2f, 4f, (leash + 3f) * 2f));
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("Runtime NavMesh bake returned no data.");
            registration = NavMesh.AddNavMeshData(navigation);
            if (!registration.valid) throw new InvalidOperationException("Runtime NavMesh could not be registered.");

            var filter = new NavMeshQueryFilter { agentTypeID = templateAgent.agentTypeID, areaMask = templateAgent.areaMask };
            var patrol = new Vector3[4];
            // The avenue approaches this west-side building from the east. Keep the loop in
            // the forecourt on that same side, leaving the doorway center and road clear.
            float front = -ResearchBuildingBuilder.FrontOffset(0);
            Vector3[] corners =
            {
                new Vector3(10.5f, 0, front - 5.8f), new Vector3(10.5f, 0, front - 2.8f),
                new Vector3(4.9f, 0, front - 2.8f), new Vector3(4.9f, 0, front - 5.8f)
            };
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 candidate = checkpoint.TransformPoint(corners[i]);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1f, filter))
                    throw new InvalidOperationException($"No NavMesh within 1 m of Checkpoint patrol post {i + 1} at {candidate}.");
                if (Physics.CheckBox(hit.position + templateBody.EnvelopeCenter, templateBody.EnvelopeExtents,
                    Quaternion.identity, WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException($"A collider blocks Checkpoint patrol post {i + 1} at {hit.position}.");
                patrol[i] = hit.position;
            }
            var route = new NavMeshPath();
            for (int i = 0; i < patrol.Length; i++)
                if (!NavMesh.CalculatePath(patrol[i], patrol[(i + 1) % patrol.Length], filter, route) ||
                    route.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException($"NavMesh has no complete path from patrol post {i + 1} to {(i + 1) % patrol.Length + 1}.");

            Transform waypoints = PrototypeGeometry.Group("Patrol Waypoints", transform);
            for (int i = 0; i < patrol.Length; i++)
                PrototypeGeometry.Group($"Post {i + 1}", waypoints).position = patrol[i];

            Transform staging = PrototypeGeometry.Group("Scout Unit", transform);
            staging.gameObject.SetActive(false);
            var scout = Instantiate(prefab, patrol[0], Quaternion.LookRotation(patrol[1] - patrol[0]), staging);
            scout.name = "Outdoor Scout";
            var original = scout.GetComponent<ScoutBrain>();
            original.enabled = false;
            Transform marker = CreateMarker(scout.transform, materials, font, player.ViewCamera);
            scout.AddComponent<OutdoorScout>().Configure(original, player, patrol, checkpoint.position, range, leash, marker);
            staging.gameObject.SetActive(true);
            if (!scout.GetComponent<NavMeshAgent>().isOnNavMesh)
                throw new InvalidOperationException($"Scout agent could not bind to the NavMesh at {patrol[0]}.");

            var visual = scout.GetComponentInChildren<DroneRotorVisuals>();
            int visibleMeshes = 0;
            float meshBottom = float.PositiveInfinity;
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.sharedMaterial == null || renderer.transform.lossyScale.sqrMagnitude < .001f ||
                    renderer.GetComponent<MeshFilter>()?.sharedMesh == null) continue;
                visibleMeshes++;
                meshBottom = Mathf.Min(meshBottom, renderer.bounds.min.y);
            }
            float clearance = meshBottom - scout.transform.position.y;
            if (visibleMeshes == 0 || clearance <= 0f)
                throw new InvalidOperationException("Scout has no visible, nonzero-scale meshes clear of the ground.");
            Debug.Log($"Outdoor Scout spawned for seed {seed} at {patrol[0]} beside the Checkpoint entrance. " +
                $"NavMesh ready, {patrol.Length} connected posts, {visibleMeshes} visible meshes, {clearance:0.00} m ground clearance.", scout);
            return marker;
        }

        private static Transform CreateMarker(Transform scout, PrototypeMaterials materials, Font font, Camera camera)
        {
            var marker = PrototypeGeometry.Group("SCOUT DRONE marker", scout, Vector3.up * 2.75f);
            marker.rotation = Quaternion.LookRotation(marker.position - camera.transform.position);
            PrototypeGeometry.Box("Marker backplate", marker, Vector3.zero, new Vector3(3.95f, .7f, .08f), materials.Metal, false);
            PrototypeGeometry.Box("Marker stripe", marker, new Vector3(-1.88f, 0, -.053f),
                new Vector3(.1f, .57f, .025f), materials.Checkpoint, false);
            PrototypeGeometry.Label("SCOUT DRONE", marker, new Vector3(.04f, 0, -.053f), .16f, font, 3.55f, .46f, materials.Lettering);
            return marker;
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

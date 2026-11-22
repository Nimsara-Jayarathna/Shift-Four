using System.Collections.Generic;
using System.Linq;
using System.Text;
using ShiftFour;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class OpenWorldCollisionDiagnostics
{
    [MenuItem("Tools/Shift Four/Report Open World Collisions")]
    public static void Report()
    {
        if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().path != OpenWorldPrototypeCreator.ScenePath)
        {
            Debug.LogWarning("Collision report: open OpenWorldPrototype and enter Play Mode.");
            return;
        }

        var generator = Object.FindAnyObjectByType<WorldGenerator>();
        Transform world = generator != null ? generator.transform.Find("GeneratedWorld") : null;
        var player = generator != null ? generator.GetComponentInChildren<PlayerController>() : null;
        if (world == null || player == null || !player.TryGetComponent(out CharacterController capsule))
        {
            Debug.LogWarning("Collision report: generated world or player capsule is missing.");
            return;
        }

        Physics.SyncTransforms();
        var output = new StringBuilder($"Open World collision report · seed {generator.GeneratedSeed}\n");
        var issues = new List<string>();
        output.AppendLine($"Player {capsule.height:F2} m tall, radius {capsule.radius:F2} m, step {capsule.stepOffset:F2} m, " +
            $"slope {capsule.slopeLimit:F0}°, skin {capsule.skinWidth:F2} m.");
        if (player.ViewCamera != null)
            output.AppendLine($"Camera {(player.EyePosition - player.transform.position).y:F2} m above feet, " +
                $"near clip {player.ViewCamera.nearClipPlane:F3} m.");
        else issues.Add("Player camera missing.");
        if (player.GetComponent<Rigidbody>() != null) issues.Add("Player has a Rigidbody alongside the CharacterController.");

        Transform spawn = generator.transform.Find("Spawn");
        if (spawn == null)
            issues.Add("Spawn marker missing.");
        else
        {
            if (!Physics.Raycast(spawn.position + Vector3.up * .1f, Vector3.down, out RaycastHit floor,
                capsule.stepOffset + .2f, WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore) ||
                !floor.collider.transform.IsChildOf(world))
                issues.Add("Spawn lacks nearby generated ground.");
            CheckClear("Spawn", spawn.position);
        }

        string[] sites = { "Checkpoint", "Server", "Control", "Storage" };
        for (int i = 0; i < sites.Length; i++)
        {
            string site = sites[i];
            Transform building = world.Find("Buildings/" + site);
            var barrier = building != null ? building.Find(site + " Access Door")?.GetComponent<BoxCollider>() : null;
            if (building == null || barrier == null || !IsSolid(building.Find("Foundation")) ||
                !IsSolid(building.Find("Roof cap")) || building.Find("Walls")?.GetComponentsInChildren<BoxCollider>()
                    .Count(wall => wall.enabled && !wall.isTrigger) != 6)
            {
                issues.Add($"{site} is missing a structural collider or door barrier.");
                continue;
            }

            float front = -ResearchBuildingBuilder.FrontOffset(i);
            bool clear = true;
            for (float z = front - 6f; z <= front + 1.5f; z += .5f)
            {
                Vector3 feet = building.TransformPoint(new Vector3(0f, ResearchBuildingBuilder.FloorHeight, z));
                if (!CheckClear(site + " doorway", feet, barrier)) { clear = false; break; }
            }

            output.AppendLine($"{site} · door {(barrier.enabled ? "solid" : "open")}, centre lane {(clear ? "clear" : "blocked")}.");
        }

        Transform gate = world.Find("Final Extraction Gate");
        if (gate == null || !IsSolid(gate.Find("Extraction shutter/Shutter body")))
            issues.Add("Extraction gate has no shutter collider.");
        else CheckClear("Extraction approach", gate.TransformPoint(new Vector3(0f, .11f, -2f)));

        Transform scenery = world.Find("Environment");
        if (scenery != null)
        {
            int trees = CheckProps("Trees", tree => IsSolidCollider(tree.Find("Trunk")?.GetComponent<Collider>()));
            int rocks = CheckProps("Rocks", rock => IsSolidCollider(rock.GetComponent<CapsuleCollider>()));
            int crates = CheckProps("Crates", crate => IsSolid(crate.Find("Crate body")));
            int fences = CheckProps("Fences", IsSolid);
            output.AppendLine($"Scenery · {trees} trees, {rocks} boulders, {crates} crates, {fences} fence spans.");
            if (fences != 5 || !IsSolid(world.Find("Ground")) ||
                !IsSolid(world.Find("Buildings/Storage/ExteriorDetails/Loading dock/Loading platform")))
                issues.Add("World boundary, ground or loading platform collider missing.");

            int CheckProps(string name, System.Func<Transform, bool> hasCollider)
            {
                Transform props = scenery.Find(name);
                if (props == null)
                {
                    issues.Add($"Missing {name} hierarchy.");
                    return 0;
                }
                foreach (Transform prop in props)
                    if (!hasCollider(prop)) issues.Add($"{name}/{prop.name} lacks a solid collider at {prop.position}.");
                return props.childCount;
            }
        }
        else issues.Add("Environment hierarchy missing.");

        int checkedDrones = 0;
        foreach (DroneBrain drone in world.GetComponentsInChildren<DroneBrain>())
        {
            if (!drone.TryGetComponent(out Health health) || !health.IsAlive) continue;
            checkedDrones++;
            if (!drone.TryGetComponent(out NavMeshAgent agent) || !agent.isOnNavMesh ||
                !drone.TryGetComponent(out DroneBody body) || !drone.TryGetComponent(out CapsuleCollider hull) ||
                !hull.enabled || !body.CanOccupy(drone.transform.position))
                issues.Add($"{drone.name} is missing a hull, off NavMesh or overlapping a solid.");
        }
        output.AppendLine($"Checked {checkedDrones} living drone hulls.");

        if (issues.Count == 0) Debug.Log(output.Append("No collision issues found in this snapshot.").ToString(), generator);
        else Debug.LogWarning(output.AppendJoin("\n", issues).ToString(), generator);

        bool CheckClear(string label, Vector3 feet, Collider allowed = null)
        {
            // Probe above the step-up region so walkable aprons, roads and foundations are excluded.
            float radius = capsule.radius - capsule.skinWidth;
            Vector3 lower = feet + Vector3.up * (radius + .08f);
            Vector3 upper = feet + Vector3.up * (capsule.height - radius);
            var blocker = Physics.OverlapCapsule(lower, upper, radius, WeaponController.GameplayRaycastMask,
                QueryTriggerInteraction.Ignore).FirstOrDefault(hit => hit != allowed && !hit.transform.IsChildOf(player.transform));
            if (blocker == null) return true;
            issues.Add($"{label} is obstructed by {blocker.name} at {feet}.");
            return false;
        }
    }

    private static bool IsSolid(Transform target)
        => target != null && IsSolidCollider(target.GetComponent<BoxCollider>());

    private static bool IsSolidCollider(Collider collider) => collider != null && collider.enabled && !collider.isTrigger;
}

using System.Linq;
using System.Text;
using ShiftFour;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class OpenWorldDroneDiagnostics
{
    [MenuItem("Tools/Shift Four/Report Open World AI")]
    public static void Report() => Inspect(false);

    [MenuItem("Tools/Shift Four/Report Open World QA")]
    public static void ReportQuality() => Inspect(true);

    private static void Inspect(bool summary)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != OpenWorldPrototypeCreator.ScenePath || !EditorApplication.isPlaying)
        {
            Debug.LogWarning("Open World AI report: open OpenWorldPrototype and enter Play Mode.");
            return;
        }

        var generator = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<WorldGenerator>())
            .FirstOrDefault();
        Transform world = generator != null ? generator.transform.Find("GeneratedWorld") : null;
        if (world == null)
        {
            Debug.LogWarning("Open World AI report: the active scene has no generated world.");
            return;
        }

        var output = new StringBuilder($"Open World {(summary ? "QA" : "AI")} report · seed {generator.GeneratedSeed}\n");
        var drones = world.GetComponentsInChildren<DroneBrain>(true);
        var settings = new SerializedObject(generator);
        bool issues = false;
        Count<GuardBrain>("Indoor Guards", 10, "guardPrefab");
        Count<ScoutBrain>("Outdoor Scouts", generator.OutdoorScoutCount, "scoutPrefab");
        Count<FlankerBrain>("Outdoor Flankers", generator.OutdoorFlankerCount, "flankerPrefab");
        Count<InterceptorBrain>("Outdoor Interceptors", generator.OutdoorInterceptorCount, "interceptorPrefab");
        var sites = new[] { "Checkpoint", "Server", "Control", "Storage" };
        for (int i = 0; i < sites.Length; i++)
        {
            Transform zone = world.Find("Buildings/" + sites[i] + "/IndoorGuards");
            int placed = zone != null ? zone.GetComponentsInChildren<GuardBrain>(true).Length : 0;
            output.AppendLine($"  {sites[i]} Guards: {placed}/{i + 1}");
            issues |= placed != i + 1;
        }
        output.AppendLine($"Outdoor firing limit {generator.OutdoorAttackLimit}; response limit {generator.InterceptorResponseLimit}.");

        var extraction = world.GetComponentInChildren<OpenWorldMissionManager>();
        if (extraction == null || !extraction.HasPlayer ||
            extraction.RegisteredBuildingMissions != OpenWorldMissionManager.RequiredMissions ||
            extraction.RegisteredDrones != OpenWorldMissionManager.RequiredDrones)
        {
            output.AppendLine("Extraction manager, player, building mission or required drone references missing.");
            issues = true;
        }

        foreach (DroneBrain drone in drones)
        {
            var health = drone.GetComponent<Health>();
            var agent = drone.GetComponent<NavMeshAgent>();
            var visuals = drone.GetComponentInChildren<DroneRotorVisuals>(true);
            bool missing = health == null || agent == null || visuals == null ||
                visuals.WeaponMount == null || visuals.Effects == null ||
                (drone is ScoutBrain ? !drone.TryGetComponent<OutdoorScout>(out _) : health.IsAlive && !drone.enabled);
            bool alive = IsAlive(drone);
            bool navigationMissing = alive && !AgentIsReady(agent);
            if (summary && !missing && !navigationMissing && (!alive || visuals.isActiveAndEnabled)) continue;
            missing |= alive && visuals != null && !visuals.isActiveAndEnabled;
            string decision = drone switch
            {
                ScoutBrain when drone.TryGetComponent(out OutdoorScout scout) => scout.State.ToString(),
                GuardBrain guard => guard.IndoorState + " / " + guard.CurrentDecision,
                FlankerBrain flanker => flanker.DecisionTrace,
                InterceptorBrain interceptor => interceptor.SensorDecision,
                _ => drone.CurrentDecision
            };
            output.AppendLine($"  {drone.name} · {(alive ? decision : "defeated or inactive")}" +
                (agent == null ? " [missing NavMeshAgent]" : "") +
                (visuals == null || visuals.WeaponMount == null || visuals.Effects == null ? " [missing animation or effect rig]" : "") +
                (missing ? " [incomplete drone setup]" : "") + (navigationMissing ? " [off NavMesh]" : ""));
            issues |= missing || navigationMissing;
        }

        if (summary)
        {
            int worlds = generator.transform.Cast<Transform>().Count(t => t.gameObject.activeInHierarchy && t.name == "GeneratedWorld");
            int gates = generator.GetComponentsInChildren<OpenWorldExtractionGate>().Length;
            int managers = generator.GetComponentsInChildren<OpenWorldMissionManager>().Length;
            var huds = generator.GetComponentsInChildren<PrototypePlayerFeedback>();
            bool bindings = worlds == 1 && gates == 1 && managers == 1 && huds.Length == 1 &&
                huds[0].ExtractionManager == extraction;
            output.AppendLine($"Active worlds {worlds}, gates {gates}, managers {managers}, HUDs {huds.Length}" +
                (bindings ? "." : " [missing, duplicate or disconnected]."));
            issues |= !bindings;
            var renderers = world.GetComponentsInChildren<Renderer>();
            output.AppendLine($"Layout renderers {renderers.Length}, enabled shadow casters " +
                $"{renderers.Count(r => r.enabled && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)}, " +
                $"enabled lights {world.GetComponentsInChildren<Light>().Count(l => l.enabled)}. " +
                $"Editor draw calls in last render: {UnityStats.drawCalls}.");
            output.AppendLine(OpenWorldCollisionDiagnostics.Inspect(world,
                generator.GetComponentInChildren<PlayerController>(), out bool collisionIssues));
            issues |= collisionIssues;
        }

        if (issues) Debug.LogWarning(output.ToString(), generator);
        else Debug.Log(output.ToString(), generator);

        void Count<T>(string label, int expected, string field) where T : DroneBrain
        {
            var role = drones.OfType<T>().ToArray();
            int difference = expected - role.Length;
            bool assigned = settings.FindProperty(field)?.objectReferenceValue != null;
            output.AppendLine($"{label} · {role.Count(IsAlive)} active, {role.Length}/{expected} spawned" +
                (difference > 0 ? $" ({difference} spawn failures)" : difference < 0 ? $" ({-difference} extra)" : "") +
                (assigned ? "" : " [prefab missing]"));
            issues |= difference != 0 || !assigned;
        }
    }

    private static bool IsAlive(DroneBrain drone) =>
        drone.gameObject.activeInHierarchy && drone.TryGetComponent(out Health health) && health.IsAlive;

    private static bool AgentIsReady(NavMeshAgent agent) => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
}

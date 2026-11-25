using System.Linq;
using ShiftFour;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OpenWorldExtractionDiagnostics
{
    [MenuItem("Tools/Shift Four/Diagnose Final Extraction")]
    public static void Report()
    {
        Scene scene = SceneManager.GetActiveScene();
        var startScene = EditorSceneManager.playModeStartScene;
        string overridePath = startScene != null ? AssetDatabase.GetAssetPath(startScene) : "(unset)";
        Debug.Log($"Final extraction diagnostics\nAssets: {Application.dataPath}\nActive scene: {scene.path}\n" +
            $"Play Mode start scene: {overridePath}\nMode: {(EditorApplication.isPlaying ? "Play" : "Edit")}");
        if (scene.path != OpenWorldPrototypeCreator.ScenePath)
        {
            Debug.LogWarning("Final extraction: open Tools > Shift Four > Open Outdoor Prototype before testing.");
            return;
        }

        var roots = scene.GetRootGameObjects();
        var generator = roots.SelectMany(root => root.GetComponentsInChildren<WorldGenerator>(true)).FirstOrDefault();
        Transform generated = generator != null ? generator.transform.Find("GeneratedWorld") : null;
        var gates = roots.SelectMany(root => root.GetComponentsInChildren<OpenWorldExtractionGate>(true)).ToArray();
        var managers = roots.SelectMany(root => root.GetComponentsInChildren<OpenWorldMissionManager>(true)).ToArray();
        var huds = roots.SelectMany(root => root.GetComponentsInChildren<PrototypePlayerFeedback>(true)).ToArray();
        Debug.Log($"Final extraction: generator {(Available(generator) ? "enabled" : "missing or disabled")}; " +
            $"active generated world: {(generated != null && generated.gameObject.activeInHierarchy ? "yes" : "no")}; " +
            $"active gates {gates.Count(Available)}, managers {managers.Count(Available)}, prototype HUDs {huds.Count(Available)}.", generator);

        foreach (var gate in gates)
            Debug.Log($"Final extraction: {HierarchyPath(gate.transform)} at {gate.transform.position}, " +
                $"{(Available(gate) ? "enabled" : "inactive")}, {gate.Status ?? "saved preview"}.", gate);

        if (!EditorApplication.isPlaying)
        {
            Debug.Log("Final extraction: Edit Mode shows the saved preview. Press Play to inspect live mission and drone bindings.");
            return;
        }

        var manager = managers.FirstOrDefault(Available);
        var hud = huds.FirstOrDefault(Available);
        if (gates.Count(Available) != 1 || managers.Count(Available) != 1 ||
            hud == null || huds.Count(Available) != 1 || manager == null || hud.ExtractionManager != manager)
        {
            Debug.LogWarning("Final extraction: expected one active gate, manager and HUD bound to the same layout.");
            return;
        }

        Debug.Log($"Final extraction: bound to HUD; player {(manager.HasPlayer ? "registered" : "missing")}; " +
            $"building missions {manager.BuildingMissionsCompleted}/{OpenWorldMissionManager.RequiredMissions} " +
            $"({manager.RegisteredBuildingMissions} controllers registered); security drones " +
            $"{manager.DronesDefeated}/{OpenWorldMissionManager.RequiredDrones} ({manager.RegisteredDrones} registered); " +
            $"escaped {manager.Escaped}.", manager);
        if (generated == null)
            Debug.LogWarning("Final extraction: Fixed Layout is active. Use Generate World on World Generator to spawn the security drones.", manager);
        else if (manager.RegisteredBuildingMissions != OpenWorldMissionManager.RequiredMissions ||
            manager.RegisteredDrones != OpenWorldMissionManager.RequiredDrones || !manager.HasPlayer)
            Debug.LogWarning("Final extraction: a required reference is missing. Check the extraction initialization and drone spawn warnings.", manager);
    }

    private static bool Available(Behaviour component)
        => component != null && component.enabled && component.gameObject.activeInHierarchy;

    private static string HierarchyPath(Transform target)
        => target.parent == null ? target.name : HierarchyPath(target.parent) + "/" + target.name;
}

using ShiftFour;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Safe to run again: changes VISUAL children only; never resets AI, colliders or scene gameplay.
public static class ShiftFourModelInstaller
{
    private const string DroneFolder = "Assets/Models/";
    private const string DoorPath = "Assets/Models/SF_SlidingLabDoor.obj";
    private const string ScenePath = "Assets/Scenes/MainLab.unity";

    public static GameObject AddDroneVisual(Transform root, string type)
    {
        string path = DroneFolder + "SF_" + type + "Drone.obj";
        GameObject result = ImportVisual(path, root, "Drone visual - " + type);
        if (result == null) return null;
        result.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        result.transform.localRotation = Quaternion.identity;
        result.transform.localScale = Vector3.one;
        result.AddComponent<DroneRotorVisuals>();
        return result;
    }

    public static GameObject AddDoorVisual(Transform root)
    {
        GameObject result = ImportVisual(DoorPath, root, "Door visual - UV mapped original");
        if (result == null) return null;
        result.transform.localPosition = Vector3.zero;
        result.transform.localRotation = Quaternion.identity;
        result.transform.localScale = Vector3.one;
        return result;
    }

    private static GameObject ImportVisual(string assetPath, Transform parent, string name)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (model == null)
        {
            Debug.LogError("Missing model: " + assetPath);
            return null;
        }
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider, true);
        return instance;
    }

    [MenuItem("Tools/Shift Four/Install Drone + Door Models")]
    public static void Install()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath) == null ||
            System.Array.Exists(new[] { "Scout", "Flanker", "Guard", "Interceptor" },
                type => AssetDatabase.LoadAssetAtPath<GameObject>(DroneFolder + "SF_" + type + "Drone.obj") == null))
        {
            EditorUtility.DisplayDialog("Missing models", "Reimport Assets/Models and retry.", "OK");
            return;
        }
        string[] types = { "Scout", "Flanker", "Guard", "Interceptor" };
        foreach (string type in types)
        {
            string path = "Assets/Prefabs/Agents/" + type + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            ReplaceDrone(contents, type);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Sliding shortcut door") ReplaceDoor(root);
                // Existing scene agents may have scene-level overrides; update their visual too.
                if (root.name.EndsWith(" drone"))
                {
                    // Replacing a prefab instance visual is an intentional scene override.
                    // The prefab itself was updated above, so only fix old placeholder overrides.
                    if (root.GetComponent<DroneMotor>() != null)
                        ReplaceDrone(root, root.name.Split(' ')[0]);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Shift Four", "Drone and sliding door models installed. Open MainLab and press Play.", "OK");
    }

    private static void ReplaceDrone(GameObject root, string type)
    {
        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = root.transform.GetChild(i);
            if (child.name.Contains("placeholder") || child.name.StartsWith("Drone visual -"))
                Object.DestroyImmediate(child.gameObject);
        }
        GameObject visual = AddDroneVisual(root.transform, type);
        if (visual != null) root.GetComponent<DroneMotor>()?.Configure(visual.transform);
    }

    private static void ReplaceDoor(GameObject root)
    {
        foreach (Transform child in root.transform)
            if (child.name.Contains("placeholder") || child.name.StartsWith("Door visual -"))
            { Object.DestroyImmediate(child.gameObject); break; }
        AddDoorVisual(root.transform);
    }
}

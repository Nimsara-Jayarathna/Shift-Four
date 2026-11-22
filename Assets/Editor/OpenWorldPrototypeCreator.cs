using System;
using System.IO;
using ShiftFour;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Explicit authoring of a separate scene. Never runs on import or changes existing generators/settings.
public static class OpenWorldPrototypeCreator
{
    public const string ScenePath = "Assets/Scenes/OpenWorldPrototype.unity";
    private const string MaterialsPath = "Assets/Materials/OpenWorldPrototype";
    private const string OpenMenuPath = "Tools/Shift Four/Open Outdoor Prototype";

    [MenuItem(OpenMenuPath, true)]
    private static bool CanOpenPrototype() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(OpenMenuPath)]
    public static void OpenPrototype()
    {
        if (!CanOpenPrototype()) return;
        var prototype = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (prototype == null)
        {
            EditorUtility.DisplayDialog("Outdoor prototype missing",
                "Use Tools > Shift Four > Create Open World Prototype to generate the scene.", "OK");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || SceneManager.sceneCount != 1)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        var startScene = EditorSceneManager.playModeStartScene;
        if (startScene != null && startScene != prototype)
        {
            string overridePath = AssetDatabase.GetAssetPath(startScene);
            Debug.Log($"Outdoor prototype opened. The editor's Play Mode Start Scene is set to {overridePath}.");
            if (!EditorUtility.DisplayDialog("Play Mode Start Scene",
                $"Play Mode is set to start in {overridePath}. Clear this editor-only override to play the opened outdoor scene.",
                "Clear override", "Keep override")) return;
            EditorSceneManager.playModeStartScene = null;
        }
        else
        {
            Debug.Log(startScene == null
                ? "Outdoor prototype opened. Play Mode Start Scene is unset."
                : "Outdoor prototype opened. Play Mode Start Scene points to the outdoor prototype.");
        }

        Selection.activeGameObject = Array.Find(scene.GetRootGameObjects(), root => root.GetComponent<WorldGenerator>() != null);
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(0, 3, 8), Quaternion.Euler(35, -25, 0), 130);
    }

    [MenuItem("Tools/Shift Four/Create Open World Prototype")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before creating the outdoor prototype.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                const string message = "An open scene has unsaved changes. Save or discard those edits yourself, then run this tool again. No scene has been changed.";
                if (Application.isBatchMode) throw new InvalidOperationException(message);
                EditorUtility.DisplayDialog("Unsaved scene edits", message, "OK");
                return;
            }
        if (File.Exists(ScenePath))
        {
            if (Application.isBatchMode)
                throw new InvalidOperationException("OpenWorldPrototype already exists. Replacement requires confirmation in the Unity Editor.");
            if (!EditorUtility.DisplayDialog("Replace Open World Prototype?",
                "Replace " + ScenePath + " with the fixed Phase 1 layout?\n\nEdits in that prototype scene will be replaced. Existing prototype materials will be reused.",
                "Replace prototype", "Cancel")) return;
        }
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader sky = Shader.Find("Skybox/Procedural");
        if (lit == null || sky == null)
            throw new InvalidOperationException("The installed URP/Lit and built-in procedural sky shaders must be available.");
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) throw new InvalidOperationException("The built-in label font is unavailable.");

        EnsureFolder("Assets/Scenes");
        EnsureFolder(MaterialsPath);
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            // Only clean scenes are closed; the tool never saves an existing open scene.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = new GameObject("World").transform;
            PrototypeMaterials materials = CreateMaterials(lit);
            OpenWorldPrototypeLayout.Build(world, materials, font);
            CreateLighting(world.Find("Environment"), sky);
            AttachGenerator(world, materials);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save " + ScenePath);
            Selection.activeGameObject = world.gameObject;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 3, 8), Quaternion.Euler(35, -25, 0), 130);
            Debug.Log("Open World Prototype created: " + ScenePath + ". Press Play to explore the research buildings.");
        }
        catch
        {
            // Restore the user's previous scene setup without saving the failed staging scene.
            if (previous.Length > 0 && !string.IsNullOrEmpty(previous[0].path))
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            throw;
        }
    }

    internal static void AttachGenerator(Transform world, PrototypeMaterials materials)
    {
        // The original layout is a usable fallback, including after the generated layout is cleared.
        RenderSettings.sun.transform.SetParent(world, true);
        Transform fixedLayout = PrototypeGeometry.Group("Fixed Layout", world);
        foreach (string name in new[] { "Ground", "Roads", "Buildings", "Environment", "Final Extraction Gate" })
            world.Find(name).SetParent(fixedLayout, false);
        var scout = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agents/Scout.prefab");
        var generator = world.gameObject.AddComponent<WorldGenerator>();
        generator.Configure(fixedLayout, world.Find("Spawn"), materials, scout);
        generator.ConfigureDronePrefabs(
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agents/Guard.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agents/Interceptor.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agents/Flanker.prefab"));
    }

    internal static PrototypeMaterials CreateMaterials(Shader lit)
    {
        return new PrototypeMaterials
        {
            Ground = Material("Ground", lit, new Color(.20f, .26f, .23f)),
            Road = Material("Road", lit, new Color(.10f, .13f, .16f)),
            Concrete = Material("Concrete", lit, new Color(.40f, .44f, .45f)),
            Wall = Material("Wall", lit, new Color(.66f, .70f, .68f)),
            Metal = Material("Metal", lit, new Color(.15f, .20f, .24f), .4f),
            Glass = Material("Opaque glass", lit, new Color(.075f, .24f, .29f), .75f),
            Marking = Material("Road marking", lit, new Color(.75f, .76f, .66f)),
            Wood = Material("Tree trunk", lit, new Color(.28f, .24f, .19f)),
            Foliage = Material("Tree canopy", lit, new Color(.16f, .29f, .24f)),
            Rock = Material("Rock", lit, new Color(.36f, .40f, .40f)),
            Crate = Material("Cargo crate", lit, new Color(.45f, .36f, .25f)),
            Checkpoint = Material("Checkpoint amber", lit, new Color(.72f, .51f, .25f)),
            Server = Material("Server blue", lit, new Color(.24f, .53f, .64f)),
            Control = Material("Control teal", lit, new Color(.30f, .61f, .51f)),
            Storage = Material("Storage rust", lit, new Color(.66f, .37f, .25f)),
            ServerRack = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Sections/ServerRack.prefab"),
            Lettering = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/WorldBuilder/Room lettering.mat"),
            CheckpointSignal = Material("Checkpoint signal", Shader.Find("Universal Render Pipeline/Unlit"), new Color(.24f, .98f, 1f))
        };
    }

    private static Material Material(string name, Shader shader, Color color, float smoothness = .15f)
    {
        string path = MaterialsPath + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing; // Preserve manual material edits on regeneration.
        if (File.Exists(path)) throw new IOException("An incompatible asset already exists at " + path);
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void CreateLighting(Transform environment, Shader shader)
    {
        string path = MaterialsPath + "/Outdoor sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (sky == null)
        {
            if (File.Exists(path)) throw new IOException("An incompatible asset already exists at " + path);
            sky = new Material(shader) { name = "Outdoor sky" };
            sky.SetColor("_SkyTint", new Color(.57f, .65f, .72f));
            sky.SetColor("_GroundColor", new Color(.28f, .32f, .30f));
            sky.SetFloat("_AtmosphereThickness", .9f);
            sky.SetFloat("_Exposure", 1.1f);
            sky.SetFloat("_SunDisk", 2);
            sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
            AssetDatabase.CreateAsset(sky, path);
        }
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.50f, .60f, .70f);
        RenderSettings.ambientEquatorColor = new Color(.38f, .43f, .45f);
        RenderSettings.ambientGroundColor = new Color(.24f, .26f, .23f);
        RenderSettings.ambientIntensity = 1;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(.56f, .65f, .69f);
        RenderSettings.fogDensity = .0022f;
        var sun = PrototypeGeometry.Group("Sun", environment.Find("Lighting")).gameObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1, .96f, .88f);
        sun.intensity = 1.2f;
        sun.shadows = LightShadows.Hard;
        sun.transform.rotation = Quaternion.Euler(48, -30, 0);
        RenderSettings.sun = sun;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
            throw new IOException("Could not create asset folder " + path);
    }
}

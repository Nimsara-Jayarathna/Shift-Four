using System;
using System.Collections.Generic;
using UnityEngine;
using static ShiftFour.PrototypeGeometry;
using Random = System.Random;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class WorldGenerator : MonoBehaviour
    {
        private const float MinimumSize = 120f;
        private const float MaximumSize = 500f;
        private const float DefaultBuildingSpacing = 18f;
        private const float MaximumBuildingSpacing = 100f;
        private const float PlotHalfWidth = 16f;
        private const float PlotHalfDepth = 14f;
        private const float SouthEdgeSetback = 24f;
        private const float RowOffset = 1f;
        private const float AvenueWidth = 12f;
        private const float AccessWidth = 6f;
        private const float PropClearance = 3.5f;

        [SerializeField] private int seed = 1;
        [SerializeField] private bool randomSeedOnStart;
        [SerializeField, Range(MinimumSize, MaximumSize)] private float worldSize = OpenWorldPrototypeLayout.WorldSize;
        [SerializeField, Range(0, MaximumBuildingSpacing)]
        [Tooltip("Minimum clearance in metres between building foundations, subject to the selected world size.")]
        private float buildingSpacing = DefaultBuildingSpacing;
        [SerializeField, Range(0, 4)] private float objectDensity = 1;
        [SerializeField] private GameObject scoutPrefab;
        [SerializeField, Range(6f, 40f)] private float scoutDetectionRange = 24f;
        [SerializeField] private Transform fixedLayout;
        [SerializeField] private Transform spawn;
        [SerializeField] private PrototypeMaterials materials;
        [SerializeField, HideInInspector] private Transform generatedContent;

        public void Configure(Transform fallback, Transform playerSpawn, PrototypeMaterials palette, GameObject scout = null)
        {
            fixedLayout = fallback;
            spawn = playerSpawn;
            materials = palette;
            scoutPrefab = scout;
        }

        private void Awake()
        {
            int startupSeed = randomSeedOnStart ? BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) : seed;
            GenerateLayout(startupSeed, faceScoutAtSpawn: true);
        }

        [ContextMenu("Generate World")]
        public void Generate() => GenerateLayout(seed);

        [ContextMenu("Clear Generated World")]
        public void Clear()
        {
            if (!HasGeneratedContent()) return;
            Discard(generatedContent);
            generatedContent = null;
            if (fixedLayout != null) fixedLayout.gameObject.SetActive(true);
            ResetPlayer(fixedLayout);
            MarkSceneDirty();
        }

        private void GenerateLayout(int layoutSeed, bool faceScoutAtSpawn = false)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (fixedLayout == null || spawn == null || font == null || !HasMaterials())
            {
                Debug.LogWarning("Open World: assign the fixed layout, spawn and prototype materials before generating. Keeping the current layout.", this);
                return;
            }

            float size = float.IsNaN(worldSize) ? OpenWorldPrototypeLayout.WorldSize : Mathf.Clamp(worldSize, MinimumSize, MaximumSize);
            float density = float.IsNaN(objectDensity) ? 1 : Mathf.Clamp(objectDensity, 0, 4);
            if (size != worldSize)
                Debug.LogWarning($"Open World: using a {size} m world. The supported range is {MinimumSize}–{MaximumSize} m so the buildings, roads and spawn fit.", this);
            if (density != objectDensity)
                Debug.LogWarning($"Open World: using object density {density}. The supported range is 0–4.", this);

            float boundary = size * .5f - 4f;
            float usable = boundary - 5f;
            // The south row must fit between its approach road and the requested gap.
            float maxSpacing = Mathf.Min(MaximumBuildingSpacing, 2f * (usable - SouthEdgeSetback + RowOffset - PlotHalfDepth));
            float spacing = float.IsNaN(buildingSpacing) ? DefaultBuildingSpacing : Mathf.Clamp(buildingSpacing, 0, maxSpacing);
            if (spacing != buildingSpacing)
                Debug.LogWarning($"Open World: requested building spacing {buildingSpacing:g} m. Using {spacing:g} m in the {size:g} m world so all four buildings and their approaches fit.", this);

            var random = new Random(layoutSeed);
            // Anchor the layout to the existing spawn, including when previewing a different size.
            Vector3 entry = new Vector3(0, .12f, -usable + 9f);
            Vector3 origin = transform.InverseTransformPoint(spawn.position) - entry;
            Transform next = Group("GeneratedWorld", transform, origin);
            next.gameObject.SetActive(false);
            try
            {
                Box("Ground", next, new Vector3(0, -.4f, 0), new Vector3(size, .8f, size), materials.Ground);
                Vector3[] positions = PlaceBuildings(random, usable, spacing);
                var reserved = new List<Rect>();
                ResearchBuildingBuilder.Build(Group("Buildings", next), materials, font, positions);
                BuildRoads(Group("Roads", next), usable, positions, reserved);
                Transform environment = Group("Environment", next);
                BuildFence(Group("Fences", environment), boundary);
                Scatter(environment, random, usable, density, reserved);
            }
            catch (Exception exception)
            {
                Discard(next);
                Debug.LogWarning($"Open World: generation failed for seed {layoutSeed}. Keeping the current layout. {exception.Message}", this);
                return;
            }

            if (HasGeneratedContent()) Discard(generatedContent);
            fixedLayout.gameObject.SetActive(false);
            generatedContent = next;
            next.gameObject.SetActive(true);
            ResetPlayer(next);
            MarkSceneDirty();
            Debug.Log($"Open World: generated seed {layoutSeed} ({size} × {size} m).", this);
            if (Application.isPlaying)
            {
                var player = spawn.GetComponentInChildren<PlayerController>();
                Transform marker = OutdoorScoutEncounter.Spawn(next, scoutPrefab, player,
                    scoutDetectionRange, materials, font, layoutSeed);
                if (faceScoutAtSpawn && marker != null &&
                    !Physics.Linecast(player.EyePosition, marker.position, WeaponController.GameplayRaycastMask,
                        QueryTriggerInteraction.Ignore))
                {
                    Vector3 heading = Vector3.ProjectOnPlane(marker.position - player.transform.position, Vector3.up);
                    if (heading.sqrMagnitude > .01f) player.transform.rotation = Quaternion.LookRotation(heading);
                }
            }
        }

        private static Vector3[] PlaceBuildings(Random random, float usable, float spacing)
        {
            // Reserve the central avenue and keep northern access roads clear of the southern foundations.
            float minX = Mathf.Max(AvenueWidth * .5f + PlotHalfWidth + 2f, PlotHalfWidth + RowOffset + spacing * .5f);
            float halfRowSeparation = Mathf.Max(21f, PlotHalfDepth + spacing * .5f);
            var positions = new Vector3[4];
            for (int i = 0; i < positions.Length; i++)
            {
                bool west = i < 2;
                bool south = i == 0 || i == 3;
                float x = Between(random, minX, usable - PlotHalfWidth) * (west ? -1 : 1);
                float z = south
                    ? Between(random, -usable + SouthEdgeSetback, RowOffset - halfRowSeparation)
                    : Between(random, RowOffset + halfRowSeparation, usable - PlotHalfDepth);
                positions[i] = new Vector3(x, 0, z);
            }
            return positions;
        }

        private void BuildRoads(Transform roads, float usable, Vector3[] positions, List<Rect> reserved)
        {
            // Every access road connects to this spine. The spine also contains the spawn.
            float avenueEnd = usable - AvenueWidth * .5f;
            Road("Main avenue", roads, new Vector2(0, -avenueEnd), new Vector2(0, avenueEnd), AvenueWidth, .08f, reserved);
            for (float z = -usable + 5f; z < usable - 3f; z += 10f)
                Box("Lane marking", roads, new Vector3(0, .083f, z), new Vector3(.18f, .006f, 3), materials.Marking, false);

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 plot = positions[i];
                float front = plot.z - ResearchBuildingBuilder.FrontOffset(i);
                float branchZ = front - 10f;
                string building = i == 0 ? "Checkpoint" : i == 1 ? "Server" : i == 2 ? "Control" : "Storage";
                Road(building + " branch", roads, new Vector2(0, branchZ), new Vector2(plot.x, branchZ), AccessWidth, .075f, reserved);
                Road(building + " approach", roads, new Vector2(plot.x, branchZ), new Vector2(plot.x, front - 4.5f), AccessWidth, .07f, reserved);
                // Include the entire entrance apron, facade details and foundation in the prop exclusion area.
                // Keep the Checkpoint service ground and patrol forecourt clear.
                float rearClearance = i == 0 && scoutPrefab != null ? 16f : PlotHalfDepth;
                reserved.Add(Rect.MinMaxRect(plot.x - PlotHalfWidth, front - 8f, plot.x + PlotHalfWidth, plot.z + rearClearance));
            }
        }

        private void Road(string name, Transform parent, Vector2 start, Vector2 end, float width,
            float height, List<Rect> reserved)
        {
            Vector2 center = (start + end) * .5f;
            // The width added at each axis-aligned joint fills the corner with no gap.
            Vector2 size = new Vector2(Mathf.Abs(end.x - start.x) + width, Mathf.Abs(end.y - start.y) + width);
            Box(name, parent, new Vector3(center.x, height * .5f, center.y), new Vector3(size.x, height, size.y), materials.Road);
            reserved.Add(new Rect(center - size * .5f, size));
        }

        private void BuildFence(Transform fences, float edge)
        {
            var southWest = new Vector3(-edge, 0, -edge);
            var northWest = new Vector3(-edge, 0, edge);
            var northEast = new Vector3(edge, 0, edge);
            var southEast = new Vector3(edge, 0, -edge);
            OpenWorldPrototypeLayout.Fence(fences, southWest, northWest, materials);
            OpenWorldPrototypeLayout.Fence(fences, northWest, northEast, materials);
            OpenWorldPrototypeLayout.Fence(fences, northEast, southEast, materials);
            OpenWorldPrototypeLayout.Fence(fences, southEast, southWest, materials);
        }

        private void Scatter(Transform environment, Random random, float usable, float density, List<Rect> reserved)
        {
            Transform trees = Group("Trees", environment);
            Transform rocks = Group("Rocks", environment);
            Transform crates = Group("Crates", environment);
            int requested = 0;
            int placed = 0;
            for (int kind = 0; kind < 3; kind++)
            {
                int count = Mathf.RoundToInt((kind == 0 ? 12 : kind == 1 ? 8 : 7) * density);
                requested += count;
                for (int i = 0; i < count; i++)
                {
                    if (!TryPlaceProp(random, usable, reserved, out Vector3 position)) break;
                    if (kind == 0)
                    {
                        Transform tree = Group("Tree", trees, position);
                        float height = Between(random, 4.5f, 6.5f);
                        Cylinder("Trunk", tree, Vector3.up * (height * .5f), .65f, height, materials.Wood);
                        Sphere("Canopy", tree, Vector3.up * (height + 1), new Vector3(5, 5.5f, 5), materials.Foliage, false);
                    }
                    else if (kind == 1)
                    {
                        var rock = Sphere("Rock", rocks, position + Vector3.up * .8f, new Vector3(3.2f, 2, 2.8f), materials.Rock);
                        rock.transform.localRotation = Quaternion.Euler(12, Between(random, 0, 360), 9);
                    }
                    else
                    {
                        Transform crate = Group("Cargo crate", crates, position);
                        crate.localRotation = Quaternion.Euler(0, Between(random, 0, 360), 0);
                        Box("Crate body", crate, Vector3.up * .9f, Vector3.one * 1.8f, materials.Crate);
                        Box("Retention band", crate, Vector3.up * .9f, new Vector3(.15f, 1.85f, 1.85f), materials.Metal, false);
                    }
                    placed++;
                }
            }
            if (placed < requested)
                Debug.LogWarning($"Open World: placed {placed} of {requested} props. Reduced density to keep roads, entrances and spawn clear.", this);
        }

        private static bool TryPlaceProp(Random random, float usable, List<Rect> reserved, out Vector3 position)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                float x = Between(random, -usable, usable);
                float z = Between(random, -usable, usable);
                var footprint = new Rect(x - PropClearance, z - PropClearance, 2 * PropClearance, 2 * PropClearance);
                if (reserved.Exists(area => area.Overlaps(footprint))) continue;
                reserved.Add(footprint);
                position = new Vector3(x, 0, z);
                return true;
            }
            position = default;
            return false;
        }

        private static float Between(Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        private bool HasMaterials() => materials != null && materials.Ground && materials.Road
            && materials.Concrete && materials.Wall && materials.Metal && materials.Glass && materials.Marking
            && materials.Wood && materials.Foliage && materials.Rock && materials.Crate
            && materials.Checkpoint && materials.Server && materials.Control && materials.Storage;

        private bool HasGeneratedContent() => generatedContent != null && generatedContent.parent == transform;

        private void ResetPlayer(Transform layout)
        {
            if (!Application.isPlaying || spawn == null) return;
            var body = spawn.GetComponentInChildren<CharacterController>();
            if (body == null) return;
            bool enabled = body.enabled;
            if (enabled) body.enabled = false;
            body.transform.position = spawn.position;
            if (enabled) body.enabled = true;
            body.GetComponent<PlayerController>()?.ApplyCameraPose();
            var checkpoint = layout != null ? layout.GetComponentInChildren<CheckpointSecurityScanner>() : null;
            if (layout == fixedLayout && checkpoint != null) checkpoint.ResetMission();
            var server = layout != null ? layout.GetComponentInChildren<ServerNetworkPuzzle>() : null;
            if (layout == fixedLayout && server != null) server.ResetPuzzle();
            var control = layout != null ? layout.GetComponentInChildren<ControlLockdownMission>() : null;
            if (layout == fixedLayout && control != null) control.ResetMission();
            var storage = layout != null ? layout.GetComponentInChildren<StoragePowerMission>() : null;
            if (layout == fixedLayout && storage != null) storage.ResetMission();
            body.GetComponent<PrototypePlayerFeedback>()?.SetMissions(checkpoint, server, control, storage);
            Physics.SyncTransforms();
        }

        private void Discard(Transform content)
        {
            content.gameObject.SetActive(false);
            if (Application.isPlaying)
            {
                // Destroy is deferred. Free the hierarchy name before returning to the caller.
                content.name = "Retired GeneratedWorld";
                Destroy(content.gameObject);
            }
            else DestroyImmediate(content.gameObject);
        }

        private void MarkSceneDirty()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }
    }
}

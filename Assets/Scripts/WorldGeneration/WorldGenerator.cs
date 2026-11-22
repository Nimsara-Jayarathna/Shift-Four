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
        private const string GeneratedWorldName = "GeneratedWorld";
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
        [Header("Outdoor scenery")]
        [Tooltip("Scales the perimeter forest and the fraction of eligible trees inside the facility.")]
        [SerializeField, Range(0, 1.5f)] private float treeDensity = 1;
        [Tooltip("Scales patches of grass, undergrowth and loose stones in open ground.")]
        [SerializeField, Range(0, 2)] private float grassDensity = 1;
        [SerializeField] private OutdoorScenery.Detail sceneryDetail = OutdoorScenery.Detail.Standard;
        [Header("Encounters")]
        [SerializeField] private GameObject scoutPrefab;
        [SerializeField] private GameObject guardPrefab;
        [SerializeField] private GameObject interceptorPrefab;
        [SerializeField] private GameObject flankerPrefab;
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

        public void ConfigureDronePrefabs(GameObject guard, GameObject interceptor, GameObject flanker)
        {
            guardPrefab = guard;
            interceptorPrefab = interceptor;
            flankerPrefab = flanker;
        }

        private void Awake()
        {
            int startupSeed = randomSeedOnStart ? BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) : seed;
            if (!GenerateLayout(startupSeed, faceScoutAtSpawn: true))
                BindMissions(FindGeneratedContent() ?? fixedLayout);
        }

        [ContextMenu("Generate World")]
        public void Generate() => GenerateLayout(seed);

        [ContextMenu("Clear Generated World")]
        public void Clear()
        {
            Transform previous = FindGeneratedContent();
            if (previous == null) return;
            Discard(previous);
            generatedContent = null;
            if (fixedLayout != null) fixedLayout.gameObject.SetActive(true);
            ResetPlayer(fixedLayout);
            MarkSceneDirty();
        }

        private bool GenerateLayout(int layoutSeed, bool faceScoutAtSpawn = false)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (fixedLayout == null || spawn == null || font == null || !HasMaterials())
            {
                Debug.LogWarning("Open World: assign the fixed layout, spawn and prototype materials before generating. Keeping the current layout.", this);
                return false;
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
            Transform previous = FindGeneratedContent();
            Transform next = Group(GeneratedWorldName, transform, origin);
            next.gameObject.SetActive(false);
            try
            {
                Box("Ground", next, new Vector3(0, -.4f, 0), new Vector3(size, .8f, size), materials.Ground);
                Vector3[] positions = PlaceBuildings(random, usable, spacing);
                var reserved = new List<Rect>();
                ResearchBuildingBuilder.Build(Group("Buildings", next), materials, font, positions);
                BuildRoads(Group("Roads", next), usable, boundary, positions, reserved);
                OpenWorldExtractionGate.Build(next, -boundary, materials, font);
                Transform environment = Group("Environment", next);
                BuildFence(Group("Fences", environment), boundary);
                float trees = Mathf.Clamp(treeDensity, 0, 1.5f);
                Scatter(environment, random, usable, density, reserved, trees, layoutSeed);
                environment.gameObject.AddComponent<OutdoorScenery>().Build(layoutSeed, size, trees,
                    Mathf.Clamp(grassDensity, 0, 2), sceneryDetail, reserved, materials.Foliage);
            }
            catch (Exception exception)
            {
                Discard(next);
                Debug.LogWarning($"Open World: generation failed for seed {layoutSeed}. Keeping the current layout. {exception}", this);
                return false;
            }

            if (previous != null) Discard(previous);
            fixedLayout.gameObject.SetActive(false);
            generatedContent = next;
            next.gameObject.SetActive(true);
            if (Application.isPlaying && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                OutdoorScenery.ApplyLighting(RenderSettings.sun);
            ResetPlayer(next);
            MarkSceneDirty();
            Debug.Log($"Open World: generated seed {layoutSeed} ({size} × {size} m).", this);
            if (Application.isPlaying)
            {
                var player = spawn.GetComponentInChildren<PlayerController>();
                Transform marker = OutdoorScoutEncounter.Spawn(next, scoutPrefab, player,
                    scoutDetectionRange, materials, font, layoutSeed, out bool navigationReady,
                    guardPrefab, interceptorPrefab, flankerPrefab);
                if (navigationReady)
                {
                    OutdoorDroneEncounter.Spawn<GuardBrain>(next, guardPrefab, player, "Server", 1,
                        "Guard", materials.Server, materials, font, layoutSeed);
                    OutdoorDroneEncounter.Spawn<InterceptorBrain>(next, interceptorPrefab, player, "Control", 2,
                        "Interceptor", materials.Control, materials, font, layoutSeed);
                    OutdoorDroneEncounter.Spawn<FlankerBrain>(next, flankerPrefab, player, "Storage", 3,
                        "Flanker", materials.Storage, materials, font, layoutSeed);
                }
                var extractionGate = next.GetComponentInChildren<OpenWorldExtractionGate>();
                if (extractionGate != null)
                    Debug.Log($"Open World extraction: spawned {extractionGate.name} at {extractionGate.transform.position} on seed {layoutSeed}, behind Spawn at the south end of the avenue.", extractionGate);
                else
                    Debug.LogWarning($"Open World extraction: the generated gate is missing on seed {layoutSeed}.", next);
                var extraction = next.GetComponentInChildren<OpenWorldMissionManager>();
                if (extraction != null) extraction.BindDrones(next);
                else Debug.LogWarning($"Open World extraction: no active mission manager on seed {layoutSeed}.", next);
                if (faceScoutAtSpawn && marker != null &&
                    !Physics.Linecast(player.EyePosition, marker.position, WeaponController.GameplayRaycastMask,
                        QueryTriggerInteraction.Ignore))
                {
                    Vector3 heading = Vector3.ProjectOnPlane(marker.position - player.transform.position, Vector3.up);
                    if (heading.sqrMagnitude > .01f) player.transform.rotation = Quaternion.LookRotation(heading);
                }
            }
            return true;
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

        private void BuildRoads(Transform roads, float usable, float boundary, Vector3[] positions, List<Rect> reserved)
        {
            // Every access road connects to this spine. The spine also contains the spawn.
            float avenueEnd = usable - AvenueWidth * .5f;
            Road("Main avenue", roads, new Vector2(0, -avenueEnd), new Vector2(0, avenueEnd), AvenueWidth, .08f, reserved);
            Road("Extraction approach", roads, new Vector2(0, -boundary + 1f), new Vector2(0, -avenueEnd), 10f, .09f, reserved);
            reserved.Add(Rect.MinMaxRect(-7f, -boundary - 4f, 7f, -boundary + 10f));
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
            // The extraction gate replaces this span of the south perimeter.
            float gap = OpenWorldExtractionGate.HalfWidth;
            OpenWorldPrototypeLayout.Fence(fences, southWest, new Vector3(-gap, 0, -edge), materials);
            OpenWorldPrototypeLayout.Fence(fences, new Vector3(gap, 0, -edge), southEast, materials);
        }

        private void Scatter(Transform environment, Random random, float usable, float density, List<Rect> reserved,
            float treeDensityScale, int layoutSeed)
        {
            var treeSelection = new Random(layoutSeed ^ 0x4a752);
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
                        float height = Between(random, 4.5f, 6.5f);
                        // Consume the layout draw before filtering trees so scenery controls never move crates or rocks.
                        if (treeSelection.NextDouble() >= treeDensityScale)
                        {
                            requested--;
                            continue;
                        }
                        Transform tree = Group("Tree", trees, position);
                        Cylinder("Trunk", tree, Vector3.up * (height * .5f), .65f, height, materials.Wood);
                    }
                    else if (kind == 1)
                    {
                        var rock = Sphere("Rock", rocks, position + Vector3.up * .8f, new Vector3(3.2f, 2, 2.8f), materials.Rock);
                        rock.GetComponent<MeshRenderer>().enabled = false;
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

        private Transform FindGeneratedContent()
        {
            if (generatedContent != null && generatedContent.parent == transform && generatedContent.name == GeneratedWorldName)
                return generatedContent;

            // A stale serialized reference must not leave the prior generated layout active.
            Transform existing = transform.Find(GeneratedWorldName);
            if (existing == null || existing.Find("Buildings") == null || existing.Find("Ground") == null) return null;
            Debug.LogWarning("Open World missions: recovered the generated layout from a missing or stale reference.", existing);
            return existing;
        }

        private void ResetPlayer(Transform layout)
        {
            if (!Application.isPlaying) return;
            var body = spawn != null ? spawn.GetComponentInChildren<CharacterController>() : null;
            if (body != null)
            {
                bool enabled = body.enabled;
                if (enabled) body.enabled = false;
                body.transform.position = spawn.position;
                if (enabled) body.enabled = true;
                body.GetComponent<PlayerController>()?.ApplyCameraPose();
            }
            BindMissions(layout);
            Physics.SyncTransforms();
        }

        private void BindMissions(Transform layout)
        {
            if (layout == null)
            {
                Debug.LogWarning("Open World missions: no active layout to initialize.", this);
                return;
            }

            Transform buildings = layout.Find("Buildings");
            var checkpoint = layout.GetComponentInChildren<CheckpointSecurityScanner>();
            if (layout == fixedLayout && checkpoint != null) checkpoint.ResetMission();
            var server = layout.GetComponentInChildren<ServerNetworkPuzzle>();
            if (layout == fixedLayout && server != null) server.ResetPuzzle();
            var control = layout.GetComponentInChildren<ControlLockdownMission>();
            if (layout == fixedLayout && control != null) control.ResetMission();
            var storage = layout.GetComponentInChildren<StoragePowerMission>();
            if (layout == fixedLayout && storage != null) storage.ResetMission();
            var extraction = layout.GetComponentInChildren<OpenWorldMissionManager>();
            if (extraction != null)
                extraction.BindMissions(checkpoint, server, control, storage, spawn != null ? spawn.GetComponentInChildren<PlayerController>() : null);

            var feedback = spawn != null ? spawn.GetComponentInChildren<PrototypePlayerFeedback>() : null;
            // The generator's Awake can run before the enabled HUD receives OnEnable.
            bool hasHud = feedback != null && feedback.enabled;
            if (hasHud)
                feedback.SetMissions(buildings, checkpoint, server, control, storage, extraction);
            else
                Debug.LogWarning("Open World missions: no active objective HUD under Spawn.", this);

            bool ready = CheckTargets<CheckpointSecurityScanner>(layout, "Checkpoint", 1) &
                         CheckTargets<SecurityAccessCard>(layout, "Checkpoint", 1) &
                         CheckTargets<ServerNetworkPuzzle>(layout, "Server", 1) &
                         CheckTargets<ServerTerminal>(layout, "Server", 3, terminal => terminal.IsOnline) &
                         CheckTargets<ControlLockdownMission>(layout, "Control", 1) &
                         CheckTargets<LockdownSecuritySwitch>(layout, "Control", 3, securitySwitch => securitySwitch.IsActivated) &
                         CheckTargets<StoragePowerMission>(layout, "Storage", 1) &
                         CheckTargets<PowerCellPickup>(layout, "Storage", 2);
            if ((checkpoint != null && checkpoint.IsComplete) || (server != null && server.IsComplete) ||
                (control != null && control.IsComplete) || (storage != null && storage.IsComplete))
            {
                Debug.LogWarning($"Open World missions: a mission was already complete while initializing {layout.name}.", layout);
                ready = false;
            }
            if (ready && hasHud)
                Debug.Log($"Open World missions ready in {layout.name}. Checkpoint: {checkpoint.Objective}. " +
                    $"Server: {server.Objective}. Control: {control.Objective}. Storage: {storage.Objective}.", layout);
        }

        private static bool CheckTargets<T>(Transform layout, string building, int expected, Func<T, bool> hasProgress = null)
            where T : MonoBehaviour, IInteractable
        {
            T[] targets = layout.GetComponentsInChildren<T>(true);
            Transform room = layout.Find($"Buildings/{building}");
            bool valid = targets.Length == expected && room != null;
            if (!valid)
                Debug.LogWarning($"Open World missions: {layout.name}/{building} expected {expected} {typeof(T).Name}, found {targets.Length}.", layout);
            foreach (T target in targets)
            {
                bool hasCollider = Array.Exists(target.GetComponentsInChildren<Collider>(), collider =>
                    collider.enabled && !collider.isTrigger &&
                    (WeaponController.GameplayRaycastMask & (1 << collider.gameObject.layer)) != 0);
                string issue = null;
                if (!target.enabled || !target.gameObject.activeInHierarchy) issue = "is disabled";
                else if (room == null || !target.transform.IsChildOf(room)) issue = "is outside its building";
                else if (!hasCollider) issue = "has no collider for the interaction ray";
                else if (hasProgress != null && hasProgress(target)) issue = "has progress before the player interacts";
                if (issue == null) continue;
                Debug.LogWarning($"Open World missions: {layout.name}/{building}/{target.name} {issue}.", target);
                valid = false;
            }
            return valid;
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

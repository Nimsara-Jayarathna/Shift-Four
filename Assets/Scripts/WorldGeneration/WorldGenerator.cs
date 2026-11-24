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
        [Tooltip("Exponential-squared distance haze. Zero gives clear air. Applied when the world generates.")]
        [SerializeField, Range(0, .012f)] private float fogDensity = .0072f;
        [Header("Prototype knife")]
        [SerializeField, Range(.5f, 3f)] private float knifeRange = 1.85f;
        [SerializeField, Range(1f, 100f)] private float knifeDamage = 35f;
        [SerializeField, Range(.3f, 2f)] private float knifeCooldown = .58f;
        [Header("Encounters")]
        [SerializeField] private GameObject scoutPrefab;
        [SerializeField] private GameObject guardPrefab;
        [SerializeField] private GameObject interceptorPrefab;
        [SerializeField] private GameObject flankerPrefab;
        [SerializeField, Range(6f, 40f)] private float scoutDetectionRange = 24f;
        [SerializeField, Range(1.7f, 3.5f)] private float scoutFlightHeight = 2.1f;
        [SerializeField, Range(40f, 170f)] private float scoutFieldOfView = 115f;
        [SerializeField, Range(8f, 35f)] private float scoutPursuitDistance = 22f;
        [SerializeField, Range(.5f, 10f)] private float scoutSearchDuration = 3.5f;
        [SerializeField, Range(.5f, 1.5f), Tooltip("Scales detection range for all four indoor Guard zones.")]
        private float guardDetectionScale = 1f;
        [SerializeField, Range(90f, 175f)] private float guardFieldOfView = 160f;
        [SerializeField] private Transform fixedLayout;
        [SerializeField] private Transform spawn;
        [SerializeField] private PrototypeMaterials materials;
        [SerializeField, HideInInspector] private Transform generatedContent;

        [Header("Outdoor Flanker ambushes")]
        [SerializeField, Range(8f, 40f)] private float flankerDetectionRange = 28f;
        [SerializeField, Range(2f, 25f)] private float flankerDamage = 9f;
        [SerializeField, Range(.25f, 1.2f)] private float flankerShotInterval = .42f;
        [SerializeField, Range(1.6f, 8f)] private float flankerRecoveryCooldown = 3.6f;
        [SerializeField, Range(12f, 35f)] private float flankerAmbushRadius = 23f;
        [SerializeField, Range(18f, 45f)] private float flankerPursuitLimit = 33f;
        [SerializeField, Range(5f, 15f)] private float flankerAttackDistance = 9f;
        [SerializeField, Range(.25f, 1.2f)] private float flankerReplanInterval = .45f;
        [SerializeField, Range(0f, 1f)] private float flankerPredictionSeconds = .45f;

        [Header("Outdoor Interceptor response")]
        [SerializeField, Range(2f, 30f)] private float interceptorResponseCooldown = 8f;
        [SerializeField, Range(8f, 45f)] private float interceptorPursuitTime = 28f;
        [SerializeField, Range(25f, 180f)] private float interceptorPursuitDistance = 110f;
        [SerializeField, Range(1.5f, 6f)] private float interceptorLeadHorizon = 4f;

        [Header("Prototype drone combat")]
        [SerializeField, Range(5, 7)] private int outdoorScoutCount = 7;
        [SerializeField, Range(3, 5)] private int outdoorFlankerCount = 5;
        [SerializeField, Range(1, 3)] private int outdoorInterceptorCount = 3;
        [Tooltip("Maximum distinct outdoor attackers in a rolling 1.35 second window.")]
        [SerializeField, Range(1, 3)] private int outdoorAttackLimit = 2;
        [Tooltip("Maximum Interceptors actively responding to Scout sightings at once.")]
        [SerializeField, Range(1, 3)] private int interceptorResponseLimit = 1;
        [SerializeField] private DroneCombatTempo guardTempo = new DroneCombatTempo(1.2f, .34f);
        [SerializeField] private DroneCombatTempo scoutTempo = new DroneCombatTempo(1.4f, .46f);
        [SerializeField] private DroneCombatTempo flankerTempo = new DroneCombatTempo(1.55f, .63f);
        [SerializeField] private DroneCombatTempo interceptorTempo = new DroneCombatTempo(1.65f, .52f);

        private OutdoorPlayerEquipment equipment;
        private WeaponDefinition supplyPistol;
        private DroneBrain[] combatDrones = Array.Empty<DroneBrain>();
        private OutdoorScoutEncounter outdoorCombat;

        public int OutdoorScoutCount => Mathf.Clamp(outdoorScoutCount, 5, OutdoorScoutRoutes.Names.Length);
        public int OutdoorFlankerCount => Mathf.Clamp(outdoorFlankerCount, 3, 5);
        public int OutdoorInterceptorCount => Mathf.Clamp(outdoorInterceptorCount, 1, 3);
        public int OutdoorAttackLimit => Mathf.Clamp(outdoorAttackLimit, 1, 3);
        public int InterceptorResponseLimit => Mathf.Clamp(interceptorResponseLimit, 1, OutdoorInterceptorCount);
        public int GeneratedSeed { get; private set; }

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
            supplyPistol = Resources.Load<WeaponDefinition>("Weapons/FNFiveSeven");
            var player = spawn != null ? spawn.GetComponentInChildren<PlayerController>() : null;
            if (player != null && player.ViewCamera != null && HasMaterials())
            {
                equipment = player.gameObject.AddComponent<OutdoorPlayerEquipment>();
                equipment.Configure(supplyPistol, knifeRange, knifeDamage, knifeCooldown);
                equipment.BuildKnife(player.ViewCamera, materials);
            }
            if (supplyPistol == null)
                Debug.LogWarning("Open World supply station: the existing pistol definition is missing.", this);

            int startupSeed = randomSeedOnStart ? BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) : seed;
            if (!GenerateLayout(startupSeed, faceScoutAtSpawn: true))
                BindMissions(FindGeneratedContent() ?? fixedLayout);

            if (gameObject.scene.path == OutdoorMissionMenu.ScenePath && player != null)
                gameObject.AddComponent<OutdoorMissionMenu>().Initialize(player);
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
            combatDrones = Array.Empty<DroneBrain>();
            outdoorCombat = null;
            spawn?.GetComponentInChildren<PrototypePlayerFeedback>()?.SetDroneTargets(combatDrones);
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
                Transform buildings = Group("Buildings", next);
                ResearchBuildingBuilder.Build(buildings, materials, font, positions);
                CheckpointAccessDoor.Build(buildings.Find("Checkpoint"), equipment, materials, font);
                BuildingAccessDoor.Build(buildings.Find("Server"), BuildingAccessDoor.Site.Server, equipment, materials, font);
                BuildingAccessDoor.Build(buildings.Find("Control"), BuildingAccessDoor.Site.Control, equipment, materials, font);
                BuildingAccessDoor.Build(buildings.Find("Storage"), BuildingAccessDoor.Site.Storage, equipment, materials, font);
                BuildRoads(Group("Roads", next), usable, boundary, positions, reserved);
                if (!OutdoorSupplyCache.TryBuild(next, entry, boundary, reserved, materials, font, equipment, supplyPistol))
                    Debug.LogWarning($"Open World supply cache: no clear roadside placement for seed {layoutSeed}.", this);
                OpenWorldExtractionGate.Build(next, -boundary, materials, font);
                Transform environment = Group("Environment", next);
                var scenery = environment.gameObject.AddComponent<OutdoorScenery>();
                BuildFence(Group("Fences", environment), boundary, scenery);
                float trees = Mathf.Clamp(treeDensity, 0, 1.5f);
                Scatter(environment, random, usable, density, reserved, trees, layoutSeed);
                scenery.Build(layoutSeed, size, trees,
                    Mathf.Clamp(grassDensity, 0, 2), sceneryDetail, reserved, materials);
                if (TryGetComponent(out OutdoorMissionMenu menu) && menu.Preferences?.Applied.Quality == OutdoorSettings.Preset.Low)
                    scenery.SetReducedGrass(true);
            }
            catch (Exception exception)
            {
                // Unity skips OnDestroy on components whose staged world was never activated.
                next.GetComponentInChildren<OutdoorScenery>(true)?.ReleaseMeshes();
                foreach (var hall in next.GetComponentsInChildren<HallInstrumentation>(true)) hall.ReleaseInspectionMeshes();
                Discard(next);
                Debug.LogWarning($"Open World: generation failed for seed {layoutSeed}. Keeping the current layout. {exception}", this);
                return false;
            }

            if (previous != null) Discard(previous);
            fixedLayout.gameObject.SetActive(false);
            generatedContent = next;
            GeneratedSeed = layoutSeed;
            next.gameObject.SetActive(true);
            if (Application.isPlaying && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                OutdoorScenery.ApplyLighting(RenderSettings.sun, fogDensity);
            ResetPlayer(next);
            MarkSceneDirty();
            Debug.Log($"Open World: generated seed {layoutSeed} ({size} × {size} m).", this);
            var exterior = next.Find("Buildings/Checkpoint/ExteriorDetails");
            if (exterior != null)
                Debug.Log($"Open World Checkpoint exterior: activated {exterior.GetComponentsInChildren<Transform>().Length - 1} objects " +
                    $"with {exterior.GetComponentsInChildren<Renderer>().Length} renderers at {exterior.position}, seed {layoutSeed}, scene {gameObject.scene.path}.", exterior);
            else
                Debug.LogWarning($"Open World Checkpoint exterior: ExteriorDetails is missing on seed {layoutSeed}.", next);
            if (Application.isPlaying)
            {
                var player = spawn.GetComponentInChildren<PlayerController>();
                var ambushes = flankerPrefab != null
                    ? OutdoorFlankerAmbushes.Prepare(next, spawn.position, boundary, materials, layoutSeed, OutdoorFlankerCount) : null;
                var patrols = OutdoorScoutEncounter.Build(next, scoutPrefab, spawn.position, boundary, layoutSeed, OutdoorAttackLimit,
                    guardPrefab, interceptorPrefab, flankerPrefab);
                OutdoorScout extractionScout = patrols != null ? patrols.Deploy(scoutPrefab, player, layoutSeed,
                    EncounterSetting(scoutFlightHeight, 2.1f, 1.7f, 3.5f), EncounterSetting(scoutDetectionRange, 24f, 6f, 40f),
                    EncounterSetting(scoutFieldOfView, 115f, 40f, 170f), EncounterSetting(scoutPursuitDistance, 22f, 8f, 35f),
                    EncounterSetting(scoutSearchDuration, 3.5f, .5f, 10f), materials, OutdoorScoutCount) : null;
                FlankerBrain extractionFlanker = null;
                if (patrols != null && ambushes != null)
                {
                    float radius = EncounterSetting(flankerAmbushRadius, 23f, 12f, 35f);
                    var settings = new FlankerAmbushSettings(EncounterSetting(flankerDetectionRange, 28f, 8f, 40f),
                        EncounterSetting(flankerDamage, 9f, 2f, 25f), EncounterSetting(flankerShotInterval, .42f, .25f, 1.2f),
                        EncounterSetting(flankerRecoveryCooldown, 3.6f, 1.6f, 8f), radius,
                        EncounterSetting(flankerPursuitLimit, 33f, radius + 2f, 45f),
                        EncounterSetting(flankerAttackDistance, 9f, 5f, 15f),
                        EncounterSetting(flankerReplanInterval, .45f, .25f, 1.2f),
                        EncounterSetting(flankerPredictionSeconds, .45f, 0f, 1f));
                    extractionFlanker = ambushes.Deploy(flankerPrefab, player, patrols, settings, layoutSeed);
                }
                var interceptors = patrols != null ? OutdoorInterceptorDispatch.Deploy(next, patrols, interceptorPrefab,
                    player, boundary, EncounterSetting(interceptorResponseCooldown, 8f, 2f, 30f),
                    EncounterSetting(interceptorPursuitTime, 28f, 8f, 45f),
                    EncounterSetting(interceptorPursuitDistance, 110f, 25f, 180f),
                    EncounterSetting(interceptorLeadHorizon, 4f, 1.5f, 6f),
                    OutdoorInterceptorCount, InterceptorResponseLimit, layoutSeed) : null;
                player?.GetComponent<PrototypePlayerFeedback>()?.SetOutdoorEncounters(patrols, ambushes, interceptors);
                float detectionScale = float.IsNaN(guardDetectionScale) ? 1f : Mathf.Clamp(guardDetectionScale, .5f, 1.5f);
                GuardBrain extractionGuard = IndoorGuardZone.Deploy(next, guardPrefab, player,
                    detectionScale, EncounterSetting(guardFieldOfView, 160f, 90f, 175f), layoutSeed);
                if (patrols != null)
                    foreach (var zone in next.GetComponentsInChildren<IndoorGuardZone>())
                        zone.ListenForScoutAlerts(patrols);
                ConfigureDroneCombat(next, patrols, player != null ? player.ViewCamera : null);
                player?.GetComponent<PrototypePlayerFeedback>()?.SetDroneTargets(combatDrones);
                var extractionGate = next.GetComponentInChildren<OpenWorldExtractionGate>();
                if (extractionGate != null)
                    Debug.Log($"Open World extraction: spawned {extractionGate.name} at {extractionGate.transform.position} on seed {layoutSeed}, behind Spawn at the south end of the avenue.", extractionGate);
                else
                    Debug.LogWarning($"Open World extraction: the generated gate is missing on seed {layoutSeed}.", next);
                var extraction = next.GetComponentInChildren<OpenWorldMissionManager>();
                if (extraction != null) extraction.BindDrones(next, extractionGuard, extractionScout, extractionFlanker);
                else Debug.LogWarning($"Open World extraction: no active mission manager on seed {layoutSeed}.", next);
                Transform marker = extractionScout != null ? extractionScout.Marker : null;
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

        private void OnEnable() => SoundEvents.NoiseEmitted += OnPrototypeGunfire;
        private void OnDisable() => SoundEvents.NoiseEmitted -= OnPrototypeGunfire;

        private void ConfigureDroneCombat(Transform layout, OutdoorScoutEncounter outdoors, Camera viewer)
        {
            outdoorCombat = outdoors;
            Font stencilFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            combatDrones = layout.GetComponentsInChildren<DroneBrain>();
            for (int slot = 0; slot < combatDrones.Length; slot++)
            {
                DroneBrain drone = combatDrones[slot];
                var tuning = drone switch
                {
                    GuardBrain => guardTempo.Validated(new DroneCombatTempo(1.2f, .34f)),
                    ScoutBrain => scoutTempo.Validated(new DroneCombatTempo(1.4f, .46f)),
                    FlankerBrain => flankerTempo.Validated(new DroneCombatTempo(1.55f, .63f)),
                    _ => interceptorTempo.Validated(new DroneCombatTempo(1.65f, .52f))
                };
                // 1911 body shots deal about 18.3 HP after the shared combat duration scaling.
                float maximum = drone switch { ScoutBrain => 35f, GuardBrain => 48f, FlankerBrain => 54f, _ => 51f };
                drone.ConfigurePrototypeCombat(tuning, slot, maximum, drone is GuardBrain ? null : outdoors);
                drone.GetComponentInChildren<DroneRotorVisuals>()?.ConfigureOutdoorFlight(viewer, slot, stencilFont, materials.Lettering);
            }
        }

        private void OnPrototypeGunfire(NoiseEvent noise)
        {
            // WeaponController.ReportShot uses an empty source. Drone weapons report their own component.
            if (!Application.isPlaying || noise.Category != NoiseCategory.Gunshot || noise.Source != null) return;
            bool outside = outdoorCombat != null && outdoorCombat.AllowsTarget(noise.Position);
            foreach (DroneBrain drone in combatDrones)
            {
                if (drone == null || !drone.gameObject.activeInHierarchy ||
                    (drone.transform.position - noise.Position).sqrMagnitude > 14f * 14f ||
                    (drone is GuardBrain ? !drone.CanReactToGunfire(noise.Position) : !outside)) continue;
                drone.ReactToThreat();
                if (drone is ScoutBrain && drone.TryGetComponent<OutdoorScout>(out var scout))
                    scout.HearPlayerGunfire(noise.Position);
            }
        }

        private static float EncounterSetting(float value, float fallback, float min, float max)
            => float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;

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
                Box("Lane marking", roads, new Vector3(0, .083f, z), new Vector3(.18f, .006f, 3), materials.Marking, false)
                    .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

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

        private void BuildFence(Transform fences, float edge, OutdoorScenery scenery)
        {
            var southWest = new Vector3(-edge, 0, -edge);
            var northWest = new Vector3(-edge, 0, edge);
            var northEast = new Vector3(edge, 0, edge);
            var southEast = new Vector3(edge, 0, -edge);
            OpenWorldPrototypeLayout.Fence(fences, southWest, northWest, materials, scenery);
            OpenWorldPrototypeLayout.Fence(fences, northWest, northEast, materials, scenery);
            OpenWorldPrototypeLayout.Fence(fences, northEast, southEast, materials, scenery);
            // The extraction gate replaces this span of the south perimeter.
            float gap = OpenWorldExtractionGate.HalfWidth;
            OpenWorldPrototypeLayout.Fence(fences, southWest, new Vector3(-gap, 0, -edge), materials, scenery);
            OpenWorldPrototypeLayout.Fence(fences, new Vector3(gap, 0, -edge), southEast, materials, scenery);
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
                        Group("Rock", rocks, position + Vector3.up * .8f);
                        // Keep the seeded prop layout sequence, including the original rock yaw draw.
                        _ = random.NextDouble();
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
            equipment?.ResetForWorld();
            var body = spawn != null ? spawn.GetComponentInChildren<CharacterController>() : null;
            if (body != null)
            {
                bool enabled = body.enabled;
                if (enabled) body.enabled = false;
                body.transform.position = spawn.position;
                if (enabled) body.enabled = true;
                body.GetComponent<PlayerController>()?.ResetSpawnMotion();
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
            equipment?.BindCheckpointMission(layout == fixedLayout ? null : checkpoint);
            var server = layout.GetComponentInChildren<ServerNetworkPuzzle>();
            if (layout == fixedLayout && server != null) server.ResetPuzzle();
            equipment?.BindServerMission(layout == fixedLayout ? null : server);
            var control = layout.GetComponentInChildren<ControlLockdownMission>();
            if (layout == fixedLayout && control != null) control.ResetMission();
            equipment?.BindControlMission(layout == fixedLayout ? null : control);
            var storage = layout.GetComponentInChildren<StoragePowerMission>();
            if (layout == fixedLayout && storage != null) storage.ResetMission();
            var extraction = layout.GetComponentInChildren<OpenWorldMissionManager>();
            var actor = spawn != null ? spawn.GetComponentInChildren<PlayerController>() : null;
            if (extraction != null) extraction.BindMissions(checkpoint, server, control, storage, actor);
            if (Application.isPlaying) HallAudio.Bind(buildings, actor, checkpoint, server, control, storage);

            GetComponent<OutdoorMissionMenu>()?.BindExtraction(extraction);
            var feedback = spawn != null ? spawn.GetComponentInChildren<PrototypePlayerFeedback>() : null;
            // The generator's Awake can run before the enabled HUD receives OnEnable.
            bool hasHud = feedback != null && feedback.enabled;
            if (hasHud)
            {
                feedback.SetMissions(layout, spawn, checkpoint, server, control, storage, extraction);
                buildings?.Find("Control/Interior/Hall instrumentation")?.GetComponent<HallInstrumentation>()
                    ?.BindCommandMap(feedback.Map, extraction);
                feedback.SetProgression(layout.GetComponentInChildren<OutdoorSupplyCache>(),
                    layout.GetComponentInChildren<CheckpointAccessDoor>(),
                    buildings?.Find("Server/Server Access Door")?.GetComponent<BuildingAccessDoor>(),
                    buildings?.Find("Control/Control Access Door")?.GetComponent<BuildingAccessDoor>(),
                    buildings?.Find("Storage/Storage Access Door")?.GetComponent<BuildingAccessDoor>());
            }
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

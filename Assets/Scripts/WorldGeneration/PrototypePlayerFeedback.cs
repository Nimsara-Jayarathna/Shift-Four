using UnityEngine;

namespace ShiftFour
{
    // Outdoor shooting feedback uses the shared audio and weapon state without a mission session.
    [RequireComponent(typeof(PlayerController), typeof(GameplayAudio))]
    public sealed partial class PrototypePlayerFeedback : MonoBehaviour
    {
        private PlayerController player;
        private readonly GUIContent interactionText = new();
        private CheckpointSecurityScanner checkpoint;
        private ServerNetworkPuzzle server;
        private ControlLockdownMission control;
        private StoragePowerMission storage;
        private OpenWorldMissionManager extraction;
        private OutdoorSupplyCache supplyCache;
        private OutdoorScoutEncounter scoutPatrols;
        private OutdoorFlankerAmbushes flankerAmbushes;
        private OutdoorInterceptorDispatch interceptorDispatch;
        private CheckpointAccessDoor checkpointDoor;
        private BuildingAccessDoor serverDoor, controlDoor, storageDoor;
        private OutdoorPlayerEquipment equipment;
        private CombatHud droneBars;
        internal FacilityMap Map { get; } = new FacilityMap();
        private Objective lastObjective;
        private float updatedUntil;
        internal string FocusedPrompt => player != null && Cursor.lockState == CursorLockMode.Locked ? player.InteractionPrompt : string.Empty;
        internal bool ObjectiveUpdated => Time.time < updatedUntil;

        public OpenWorldMissionManager ExtractionManager => extraction;

        internal void SetDroneTargets(DroneBrain[] targets)
        {
            if (player != null) GetComponent<GameplayAudio>().BindFlightLoops(targets);
            if (droneBars == null && !TryGetComponent(out droneBars)) droneBars = gameObject.AddComponent<CombatHud>();
            if (droneBars != null) droneBars.InitializePrototype(GetComponent<PlayerController>(), targets);
        }

        internal void SetOutdoorEncounters(OutdoorScoutEncounter patrols, OutdoorFlankerAmbushes ambushes,
            OutdoorInterceptorDispatch interceptors)
        {
            scoutPatrols = patrols;
            flankerAmbushes = ambushes;
            interceptorDispatch = interceptors;
        }

        internal void SetMissions(Transform layout, Transform spawn, CheckpointSecurityScanner scanner, ServerNetworkPuzzle network,
            ControlLockdownMission lockdown, StoragePowerMission power, OpenWorldMissionManager finalMission)
        {
            UnsubscribeFromMissions();
            notices.Clear();
            noticeAge = 0f;
            checkpoint = scanner;
            server = network;
            control = lockdown;
            storage = power;
            extraction = finalMission;
            Map.Bind(layout, spawn);
            lastObjective = default;
            updatedUntil = 0f;
            SubscribeToMissions();
        }

        internal void SetProgression(OutdoorSupplyCache cache, CheckpointAccessDoor door,
            BuildingAccessDoor serverAccess, BuildingAccessDoor controlAccess, BuildingAccessDoor storageAccess)
        {
            if (supplyCache != null) supplyCache.Discovered -= SuppliesTaken;
            supplyCache = cache;
            if (isActiveAndEnabled && supplyCache != null) supplyCache.Discovered += SuppliesTaken;
            checkpointDoor = door;
            serverDoor = serverAccess;
            controlDoor = controlAccess;
            storageDoor = storageAccess;
        }

        private void Start()
        {
            player = GetComponent<PlayerController>();
            if (droneBars == null) SetDroneTargets(System.Array.Empty<DroneBrain>());
            equipment = GetComponent<OutdoorPlayerEquipment>();
            GetComponent<GameplayAudio>().Initialize(player, null);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || player == null || player.MenuBlocksInput) return;
            if (extraction != null && extraction.Escaped) return;

            if (interceptorDispatch != null && interceptorDispatch.HasWarning)
                GUI.Box(new Rect((Screen.width - 210f) * .5f, Screen.height * .5f - 138f, 210f, 25f),
                    "INTERCEPTOR RESPONDING");
            if (scoutPatrols != null && scoutPatrols.HasAlert)
            {
                Color previous = GUI.contentColor;
                GUI.contentColor = new Color(1f, .68f, .25f);
                GUI.Box(new Rect((Screen.width - 126f) * .5f, Screen.height * .5f - 78f, 126f, 25f), "SCOUT ALERT");
                GUI.contentColor = previous;
            }
            FlankerBrain warning = flankerAmbushes != null ? flankerAmbushes.Warning : null;
            if (warning != null)
            {
                Vector3 offset = warning.transform.position - player.transform.position;
                float ahead = Vector3.Dot(player.transform.forward, offset);
                float side = Vector3.Dot(player.transform.right, offset);
                string direction = ahead < -Mathf.Abs(side) ? "BEHIND" :
                    Mathf.Abs(side) < Mathf.Abs(ahead) * .35f ? "AHEAD" : side < 0f ? "LEFT" : "RIGHT";
                Color previous = GUI.contentColor;
                GUI.contentColor = new Color(1f, .65f, .28f);
                GUI.Box(new Rect((Screen.width - 196f) * .5f, Screen.height * .5f - 108f, 196f, 25f),
                    "FLANKER THREAT · " + direction);
                GUI.contentColor = previous;
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float x = Screen.width * .5f;
                float y = Screen.height * .5f;
                GUI.DrawTexture(new Rect(x - 4, y, 9, 1), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x, y - 4, 1, 9), Texture2D.whiteTexture);
                if (!string.IsNullOrEmpty(player.InteractionPrompt))
                {
                    interactionText.text = player.InteractionPrompt;
                    Vector2 size = GUI.skin.box.CalcSize(interactionText);
                    var prompt = new Rect(x - size.x * .5f - 8, y + 18, size.x + 16, size.y + 4);
                    GUI.Box(prompt, interactionText);
                    Color previous = GUI.color;
                    GUI.color = player.InteractionPrompt.StartsWith("E:", System.StringComparison.Ordinal)
                        ? new Color(.3f, .86f, .88f) : new Color(.95f, .75f, .43f);
                    GUI.DrawTexture(new Rect(prompt.x, prompt.y, 2, prompt.height), Texture2D.whiteTexture);
                    GUI.color = previous;
                }
            }

        }

        internal enum Site { Supply, Checkpoint, Server, Control, Storage, Extraction }
        internal enum Status { Locked, Available, InProgress, Complete, Unavailable }

        internal readonly struct Objective
        {
            internal readonly Site Site;
            internal readonly Status Status;
            internal readonly string Instruction;
            internal readonly int Count, Total;

            internal Objective(Site site, Status status, string instruction, int count = 0, int total = 0)
            {
                Site = site;
                Status = status;
                Instruction = instruction;
                Count = count;
                Total = total;
            }
        }

        internal Site CurrentSite
        {
            get
            {
                if (supplyCache != null && !supplyCache.IsDiscovered) return Site.Supply;
                if (checkpoint != null && !checkpoint.IsComplete) return Site.Checkpoint;
                if (server != null && !server.IsComplete) return Site.Server;
                if (control != null && !control.IsComplete) return Site.Control;
                if (storage != null && !storage.IsComplete) return Site.Storage;
                return Site.Extraction;
            }
        }

        internal Objective ReadObjective(Site site)
        {
            switch (site)
            {
                case Site.Supply when supplyCache != null:
                    return new(site, supplyCache.IsDiscovered ? Status.Complete : Status.Available,
                        supplyCache.IsDiscovered ? "Pistol and Checkpoint key collected." : "Locate the supply station and collect your gear.",
                        supplyCache.IsDiscovered ? 1 : 0, 1);
                case Site.Checkpoint when checkpoint != null:
                    return Building(site, checkpoint.IsComplete, checkpointDoor == null || checkpointDoor.IsOpen,
                        equipment != null && equipment.HasCheckpointKey, checkpoint.Objective, checkpoint.IsComplete ? 2 : checkpoint.HasCard ? 1 : 0, 2);
                case Site.Server when server != null:
                    return Building(site, server.IsComplete, serverDoor == null || serverDoor.IsOpen,
                        equipment != null && equipment.HasServerKey, "Activate terminals in the displayed order.",
                        server.OnlineCount, server.TerminalCount);
                case Site.Control when control != null:
                    return Building(site, control.IsComplete, controlDoor == null || controlDoor.IsOpen,
                        equipment != null && equipment.HasControlKey,
                        control.ActivatedCount == control.TotalSwitches ? "Use the lockdown override panel." : "Activate the lockdown switches.",
                        control.ActivatedCount, control.TotalSwitches);
                case Site.Storage when storage != null:
                    return Building(site, storage.IsComplete, storageDoor == null || storageDoor.IsOpen,
                        equipment != null && equipment.HasStorageKey,
                        storage.CollectedCount == storage.TotalCells ? "Restore emergency generator power." : "Collect the two power cells.",
                        storage.CollectedCount, storage.TotalCells);
                case Site.Extraction when extraction != null:
                    return new(site, extraction.Escaped ? Status.Complete : extraction.IsReady ? Status.Available : Status.Locked,
                        extraction.Escaped ? "Facility escaped." : extraction.IsReady ? "Reach the extraction gate." :
                        extraction.BuildingMissionsCompleted < OpenWorldMissionManager.RequiredMissions ? "Complete the four building missions." :
                        "Defeat the designated Scout, Guard and Flanker.", extraction.DronesDefeated, OpenWorldMissionManager.RequiredDrones);
                default:
                    return new(site, Status.Unavailable, "Unavailable in this layout.");
            }
        }

        private static Objective Building(Site site, bool complete, bool open, bool key, string task, int count = 0, int total = 0)
        {
            if (complete) return new(site, Status.Complete, site switch
            {
                Site.Checkpoint => "Security card authenticated at the scanner.",
                Site.Server => "Network restored. All terminals online.",
                Site.Control => "Security switches disabled and lockdown lifted.",
                _ => "Both power cells installed. Generator online."
            }, count, total);
            if (open) return new(site, Status.InProgress, task, count, total);
            if (key) return new(site, Status.Available, "Reach the building and unlock its entrance.", count, total);
            return new(site, Status.Locked, task, count, total);
        }

        private void LateUpdate()
        {
            if (player == null || player.MenuBlocksInput) return;
            AdvanceNotice(Time.unscaledDeltaTime);
            var next = ReadObjective(CurrentSite);
            if (lastObjective.Instruction != null && (lastObjective.Site != next.Site || lastObjective.Status != next.Status ||
                lastObjective.Count != next.Count || lastObjective.Instruction != next.Instruction))
                updatedUntil = Time.time + 2f;
            lastObjective = next;
        }

        internal string InteractionNotice
        {
            get
            {
                if (extraction != null && !string.IsNullOrEmpty(extraction.Notice)) return extraction.Notice;
                if (server != null && !server.HasWarning && !string.IsNullOrEmpty(server.Notice)) return server.Notice;
                if (control != null && !string.IsNullOrEmpty(control.Notice)) return control.Notice;
                if (storage != null && !string.IsNullOrEmpty(storage.Notice)) return storage.Notice;
                if (checkpoint != null && !string.IsNullOrEmpty(checkpoint.Notice)) return checkpoint.Notice;
                if (storageDoor != null && !string.IsNullOrEmpty(storageDoor.Notice)) return storageDoor.Notice;
                if (controlDoor != null && !string.IsNullOrEmpty(controlDoor.Notice)) return controlDoor.Notice;
                if (serverDoor != null && !string.IsNullOrEmpty(serverDoor.Notice)) return serverDoor.Notice;
                if (checkpointDoor != null && !string.IsNullOrEmpty(checkpointDoor.Notice)) return checkpointDoor.Notice;
                if (equipment != null && equipment.StorageKeyNotice.Length > 0) return equipment.StorageKeyNotice;
                if (equipment != null && equipment.ControlKeyNotice.Length > 0) return equipment.ControlKeyNotice;
                if (equipment != null && equipment.ServerKeyNotice.Length > 0) return equipment.ServerKeyNotice;
                return supplyCache != null ? supplyCache.Notice : string.Empty;
            }
        }
    }
}

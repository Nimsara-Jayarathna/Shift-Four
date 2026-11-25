using System.Collections.Generic;
using UnityEngine;

namespace ShiftFour
{
    // One instance per generated layout. Room controllers and drone Health own their progress.
    [DisallowMultipleComponent]
    public sealed class OpenWorldMissionManager : MonoBehaviour
    {
        public const string VictoryMessage = "MISSION COMPLETE - FACILITY ESCAPED";
        public const int RequiredMissions = 4;
        public const int RequiredDrones = 3;
        private static readonly string[] DroneNames = { "Scout", "Guard", "Flanker" };

        private CheckpointSecurityScanner checkpoint;
        private ServerNetworkPuzzle server;
        private ControlLockdownMission control;
        private StoragePowerMission storage;
        private readonly Health[] drones = new Health[RequiredDrones];
        [SerializeField] private OpenWorldExtractionGate gate;
        private PlayerController player;
        private Health playerHealth;
        private string notice;
        private float noticeUntil;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisibility;
        private bool suspendedPlayer;

        internal bool CheckpointComplete => checkpoint != null && checkpoint.IsComplete;
        internal bool ServerComplete => server != null && server.IsComplete;
        internal bool ControlComplete => control != null && control.IsComplete;
        internal bool StorageComplete => storage != null && storage.IsComplete;

        public int BuildingMissionsCompleted => (CheckpointComplete ? 1 : 0) + (ServerComplete ? 1 : 0) +
            (ControlComplete ? 1 : 0) + (StorageComplete ? 1 : 0);
        public int RegisteredBuildingMissions => (Available(checkpoint) ? 1 : 0) + (Available(server) ? 1 : 0) +
            (Available(control) ? 1 : 0) + (Available(storage) ? 1 : 0);
        public int RegisteredDrones
        {
            get
            {
                int registered = 0;
                foreach (Health drone in drones)
                    if (drone != null) registered++;
                return registered;
            }
        }
        public bool HasPlayer => player != null && playerHealth != null;
        public int DronesDefeated
        {
            get
            {
                int defeated = 0;
                foreach (Health drone in drones)
                    if (drone != null && !drone.IsAlive) defeated++;
                return defeated;
            }
        }
        public bool IsReady => BuildingMissionsCompleted == RequiredMissions && DronesDefeated == RequiredDrones;
        internal event System.Action Extracted;
        internal event System.Action DesignatedTargetDefeated;
        public bool Escaped { get; private set; }
        public string Notice => !IsReady && Time.unscaledTime < noticeUntil ? notice : string.Empty;

        internal OpenWorldExtractionGate Gate => gate;

        internal void BindGate(OpenWorldExtractionGate extractionGate) => gate = extractionGate;

        internal void BindMissions(CheckpointSecurityScanner scanner, ServerNetworkPuzzle network,
            ControlLockdownMission lockdown, StoragePowerMission power, PlayerController owner)
        {
            checkpoint = scanner;
            server = network;
            control = lockdown;
            storage = power;
            player = owner;
            playerHealth = owner != null ? owner.GetComponent<Health>() : null;

            WarnIfUnavailable(checkpoint, "Checkpoint");
            WarnIfUnavailable(server, "Server");
            WarnIfUnavailable(control, "Control");
            WarnIfUnavailable(storage, "Storage");
            if (!HasPlayer)
                Debug.LogWarning("Open World extraction: the player or its Health reference is missing.", this);
        }

        internal void BindDrones(Transform layout, GuardBrain serverGuard, OutdoorScout checkpointScout, FlankerBrain storageFlanker)
        {
            ListenToDrones(false);
            drones[0] = checkpointScout != null ? checkpointScout.GetComponent<Health>() : null;
            // The original Server Guard remains the extraction target for this archetype.
            drones[1] = serverGuard != null ? serverGuard.GetComponent<Health>() : null;
            drones[2] = storageFlanker != null ? storageFlanker.GetComponent<Health>() : null;
            ListenToDrones(true);

            for (int i = 0; i < drones.Length; i++)
                if (drones[i] == null)
                    Debug.LogWarning($"Open World extraction: {DroneNames[i]} Health was not registered. Check the spawn log.", this);
            Debug.Log($"Open World extraction initialized in {layout.name}: {RegisteredBuildingMissions}/{RequiredMissions} building mission controllers, " +
                $"{RegisteredDrones}/{RequiredDrones} security drones.", this);
        }

        private static bool Available(Behaviour source)
            => source != null && source.enabled && source.gameObject.activeInHierarchy;

        private void WarnIfUnavailable(Behaviour source, string building)
        {
            if (!Available(source))
                Debug.LogWarning($"Open World extraction: the {building} mission controller is missing or disabled.", this);
        }

        internal void TryEscape()
        {
            if (Escaped || player == null || !player.isActiveAndEnabled || playerHealth == null || !playerHealth.IsAlive)
                return;
            if (!IsReady)
            {
                notice = RemainingObjectives();
                noticeUntil = Time.unscaledTime + 6f;
                return;
            }

            Escaped = true;
            previousCursorLock = Cursor.lockState;
            previousCursorVisibility = Cursor.visible;
            suspendedPlayer = true;
            player.enabled = false;
            if (gate != null) gate.Refresh();
            Debug.Log(VictoryMessage, this);
            if (Extracted != null) Extracted.Invoke();
            else
            {
                gate?.SetOpening(1f);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        internal string RemainingObjectives()
        {
            var buildings = new List<string>();
            if (!CheckpointComplete) buildings.Add("Checkpoint");
            if (!ServerComplete) buildings.Add("Server");
            if (!ControlComplete) buildings.Add("Control");
            if (!StorageComplete) buildings.Add("Storage");
            var remainingDrones = new List<string>();
            for (int i = 0; i < drones.Length; i++)
                if (drones[i] == null || drones[i].IsAlive) remainingDrones.Add(DroneNames[i]);

            string missionsText = buildings.Count > 0 ? "Building missions: " + string.Join(", ", buildings) : string.Empty;
            string dronesText = remainingDrones.Count > 0 ? "Drones: " + string.Join(", ", remainingDrones) : string.Empty;
            return missionsText.Length > 0 && dronesText.Length > 0 ? missionsText + "\n" + dronesText : missionsText + dronesText;
        }

        private void OnEnable() => ListenToDrones(true);

        private void OnDisable()
        {
            ListenToDrones(false);
            if (!suspendedPlayer || player == null) return;
            suspendedPlayer = false;
            player.enabled = true;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisibility;
        }

        private void ListenToDrones(bool listen)
        {
            foreach (Health drone in drones)
            {
                if (drone == null) continue;
                if (listen) drone.Died += DroneDefeated;
                else drone.Died -= DroneDefeated;
            }
        }

        private void DroneDefeated(Health _)
        {
            DesignatedTargetDefeated?.Invoke();
            if (gate != null) gate.Refresh();
        }
    }
}

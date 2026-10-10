using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShiftFour
{
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }
        public PlayerController Player { get; private set; }
        public WaypointGraph Graph { get; private set; }
        public int ConsolesOnline { get; private set; }
        public int DronesRemaining { get; private set; }
        public bool Ended { get; private set; }

        private string outcome = string.Empty;
        private readonly string[] roomOrder = { "Checkpoint", "Server", "Control", "Storage" };
        private readonly string[] droneOrder = { "Scout", "Guard", "Interceptor", "Flanker" };
        private DroneBrain[] drones;
        private readonly System.Collections.Generic.HashSet<string> activatedRooms = new System.Collections.Generic.HashSet<string>();

        private void Awake()
        {
            Instance = this;
            Graph = FindAnyObjectByType<WaypointGraph>();
            Player = FindAnyObjectByType<PlayerController>();
        }

        private void Start()
        {
            drones = FindObjectsByType<DroneBrain>(FindObjectsInactive.Exclude);
            DronesRemaining = drones.Length;
            if (Player != null) Player.GetComponent<Health>().Died += _ => Finish("SYSTEM FAILURE — R to restart");
        }

        public void ConsoleActivated(string room)
        {
            if (string.IsNullOrEmpty(room) || !activatedRooms.Add(room)) return;
            ConsolesOnline++;
        }
        public bool IsRoomComplete(string room, string droneName)
        {
            if (!activatedRooms.Contains(room) || drones == null) return false;
            foreach (DroneBrain drone in drones)
                if (drone.AgentName == droneName)
                    return !drone.GetComponent<Health>().IsAlive;
            return false;
        }
        public string RoomProgress(string room, string droneName)
        {
            bool console = activatedRooms.Contains(room);
            bool defeated = false;
            if (drones != null) foreach (DroneBrain drone in drones)
                if (drone.AgentName == droneName) defeated = !drone.GetComponent<Health>().IsAlive;
            return defeated ? (console ? "ACCESS GRANTED" : "Activate " + room + " console") : "Defeat " + droneName + " drone";
        }
        public void DroneDestroyed() => DronesRemaining = Mathf.Max(0, DronesRemaining - 1);

        public void TryExit()
        {
            if (ConsolesOnline == 4 && DronesRemaining == 0)
                Finish("SHIFT COMPLETE — R to restart");
        }

        private void Finish(string message)
        {
            if (Ended) return;
            Ended = true;
            outcome = message;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (!Ended || !Input.GetKeyDown(KeyCode.R)) return;

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.buildIndex >= 0)
                SceneManager.LoadScene(activeScene.buildIndex);
            else
                SceneManager.LoadScene(activeScene.name);
        }

        private void OnGUI()
        {
            if (Player == null) return;
            GUI.Box(new Rect(12, 12, 420, 104),
                $"SHIFT FOUR\nHealth: {Player.GetComponent<Health>().Current:0} / 100    Consoles: {ConsolesOnline}/4    Drones: {DronesRemaining}\nWASD move  |  Mouse aim  |  Click fire  |  E interact  |  Escape unlock cursor");
            GUI.Label(new Rect(Screen.width / 2 - 8, Screen.height / 2 - 14, 24, 24), "+");
            string objective = "Reach the exit";
            for (int i = 0; i < roomOrder.Length; i++)
                if (!IsRoomComplete(roomOrder[i], droneOrder[i]))
                {
                    objective = "ROOM " + (i + 1) + ": " + RoomProgress(roomOrder[i], droneOrder[i]);
                    break;
                }
            GUI.Box(new Rect(12, 120, 420, 42), objective);
            if (drones != null)
            {
                string status = "AGENT DECISIONS (viva view)\n";
                foreach (DroneBrain drone in drones)
                    status += drone.AgentName + ": " +
                        (drone.GetComponent<Health>().IsAlive ? drone.CurrentDecision : "Disabled") + "\n";
                GUI.Box(new Rect(Screen.width - 302, 12, 290, 126), status);
            }
            if (!Ended)
            {
                string prompt = Player.InteractionPrompt;
                if (!string.IsNullOrEmpty(prompt)) GUI.Box(new Rect(12, Screen.height - 62, 360, 42), prompt);
                if (ConsolesOnline == 4 && DronesRemaining == 0)
                    GUI.Box(new Rect(Screen.width / 2 - 160, 12, 320, 38), "Proceed to the green exit");
            }
            else GUI.Box(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 50, 400, 100), outcome);
        }
    }
}

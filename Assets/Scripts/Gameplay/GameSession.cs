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
        private DroneBrain[] drones;

        private void Awake()
        {
            Instance = this;
            Graph = FindFirstObjectByType<WaypointGraph>();
            Player = FindFirstObjectByType<PlayerController>();
        }

        private void Start()
        {
            drones = FindObjectsByType<DroneBrain>(FindObjectsSortMode.None);
            DronesRemaining = drones.Length;
            if (Player != null) Player.GetComponent<Health>().Died += _ => Finish("SYSTEM FAILURE — R to restart");
        }

        public void ConsoleActivated() => ConsolesOnline++;
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
            if (Ended && Input.GetKeyDown(KeyCode.R))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                    ? SceneManager.GetActiveScene().buildIndex
                    : SceneManager.GetActiveScene().name);
        }

        private void OnGUI()
        {
            if (Player == null) return;
            GUI.Box(new Rect(12, 12, 420, 104),
                $"SHIFT FOUR\nHealth: {Player.GetComponent<Health>().Current:0} / 100    Consoles: {ConsolesOnline}/4    Drones: {DronesRemaining}\nWASD move  |  Mouse aim  |  Click fire  |  E interact  |  Escape unlock cursor");
            GUI.Label(new Rect(Screen.width / 2 - 8, Screen.height / 2 - 14, 24, 24), "+");
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

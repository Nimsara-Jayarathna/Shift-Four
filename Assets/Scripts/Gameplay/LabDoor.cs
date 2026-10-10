using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    // Replaceable visual placeholder: keep named panel pivots when importing Blender FBX.
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class LabDoor : MonoBehaviour, IInteractable
    {
        [SerializeField] private int gateId;
        [SerializeField] private string room = "Checkpoint";
        [SerializeField] private string drone = "Scout";
        [SerializeField] private float unlockSeconds = 0.45f;
        [SerializeField] private float slideSeconds = 1.35f;
        [SerializeField] private float panelTravel = 1.60f;
        private Transform leftPanel, rightPanel, leftBolt, rightBolt;
        private Vector3 leftHome, rightHome, boltLeftHome, boltRightHome;
        private Renderer[] indicators;
        private MaterialPropertyBlock statusBlock;
        private BoxCollider blocker;
        private NavMeshObstacle obstacle;
        private float progress;
        private float unlockProgress;
        private bool prepared;
        private bool passable;

        public string Prompt => prepared ? "Security gate opening" :
            (GameSession.Instance == null ? "Security gate offline" : GameSession.Instance.RoomProgress(room, drone));
        public bool IsOpen => progress >= 0.99f;
        public void Configure(int id, string roomName, string droneName)
        { gateId = id; room = roomName; drone = droneName; }
        public void Interact() { /* Gate unlocks ONLY from room objective completion, not from pressing E here. */ }

        private void Awake()
        {
            blocker = GetComponent<BoxCollider>();
            obstacle = GetComponent<NavMeshObstacle>();
            Rigidbody rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            leftPanel = transform.Find("Door assembly/LeftPanel");
            rightPanel = transform.Find("Door assembly/RightPanel");
            leftBolt = transform.Find("Door assembly/LeftBolt");
            rightBolt = transform.Find("Door assembly/RightBolt");
            if (leftPanel != null) leftHome = leftPanel.localPosition;
            if (rightPanel != null) rightHome = rightPanel.localPosition;
            if (leftBolt != null) boltLeftHome = leftBolt.localPosition;
            if (rightBolt != null) boltRightHome = rightBolt.localPosition;
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (string path in new[] { "Door assembly/StatusLightLeft", "Door assembly/StatusLightRight", "Door assembly/AccessTerminal/Beacon" })
            {
                Transform t = transform.Find(path);
                if (t != null && t.TryGetComponent(out Renderer r)) list.Add(r);
            }
            indicators = list.ToArray();
            statusBlock = new MaterialPropertyBlock();
        }

        private void Update()
        {
            GameSession session = GameSession.Instance;
            if (session == null || session.Ended) return;
            if (!prepared && session.IsRoomComplete(room, drone)) prepared = true;
            if (!prepared) return;

            if (unlockProgress < 1f)
                unlockProgress = Mathf.MoveTowards(unlockProgress, 1f, Time.deltaTime / Mathf.Max(.1f, unlockSeconds));
            else
                progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime / Mathf.Max(.2f, slideSeconds));

            if (leftBolt != null) leftBolt.localPosition = boltLeftHome + Vector3.left * .22f * unlockProgress;
            if (rightBolt != null) rightBolt.localPosition = boltRightHome + Vector3.right * .22f * unlockProgress;
            if (leftPanel != null) leftPanel.localPosition = leftHome + Vector3.left * (panelTravel * Mathf.SmoothStep(0f, 1f, progress));
            if (rightPanel != null) rightPanel.localPosition = rightHome + Vector3.right * (panelTravel * Mathf.SmoothStep(0f, 1f, progress));
            foreach (Renderer indicator in indicators)
            {
                indicator.GetPropertyBlock(statusBlock);
                statusBlock.SetColor("_BaseColor", progress >= .98f ? new Color(.2f, .9f, .45f) : new Color(1f, .65f, .15f));
                indicator.SetPropertyBlock(statusBlock);
            }
            if (!passable && progress >= .98f)
            {
                passable = true;
                if (blocker != null) blocker.enabled = false;
                if (obstacle != null) obstacle.enabled = false;
                session.Graph?.SetGateOpen(gateId, true);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class LabDoor : MonoBehaviour, IInteractable
    {
        [SerializeField] private Vector3 localOpenOffset = new Vector3(0f, 0f, 3f);
        [SerializeField] private float secondsToOpen = 0.65f;
        private Vector3 closedPosition;
        private Rigidbody body;
        private NavMeshObstacle obstacle;
        private bool requestedOpen;
        private float fraction;

        public string Prompt => requestedOpen ? "E: close shortcut" : "E: open shortcut";
        public bool IsOpen => fraction >= 0.999f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            obstacle = GetComponent<NavMeshObstacle>();
            closedPosition = transform.position;
        }

        public void Interact() => requestedOpen = !requestedOpen;

        private void FixedUpdate()
        {
            float target = requestedOpen ? 1f : 0f;
            float previous = fraction;
            fraction = Mathf.MoveTowards(fraction, target, Time.fixedDeltaTime / Mathf.Max(0.1f, secondsToOpen));
            if (Mathf.Approximately(previous, fraction)) return;
            body.MovePosition(closedPosition + transform.TransformDirection(localOpenOffset) * fraction);
            // The graph shortcut becomes usable only after the collider clears the opening.
            if (previous < 0.999f && IsOpen)
            {
                if (obstacle != null) obstacle.enabled = false;
                GameSession.Instance?.Graph?.SetDoorOpen(true);
            }
            else if (previous >= 0.999f && !IsOpen)
            {
                if (obstacle != null) obstacle.enabled = true;
                GameSession.Instance?.Graph?.SetDoorOpen(false);
            }
        }
    }
}

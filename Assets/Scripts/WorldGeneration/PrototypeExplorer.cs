using UnityEngine;

namespace ShiftFour
{
    // Isolated exploration only: no weapons, health, missions, interaction input or AI dependencies.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PrototypeExplorer : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField, Min(.1f)] private float walkSpeed = 7f;
        [SerializeField, Min(.1f)] private float sprintSpeed = 14f;
        [SerializeField, Min(.1f)] private float mouseSensitivity = 2f;
        private CharacterController controller;
        private float pitch;
        private float verticalSpeed;
        private GUIStyle titleStyle;
        private GUIStyle textStyle;

        public void Configure(Camera camera) => view = camera;

        private void Start()
        {
            controller = GetComponent<CharacterController>();
            if (view == null) view = GetComponentInChildren<Camera>();
            if (view != null) pitch = Mathf.DeltaAngle(0, view.transform.localEulerAngles.x);
            CaptureCursor();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) ReleaseCursor();
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (Input.GetMouseButtonDown(0)) CaptureCursor();
                return;
            }
            if (view == null || controller == null || !controller.enabled) return;

            transform.Rotate(0, Input.GetAxis("Mouse X") * mouseSensitivity, 0);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -80, 80);
            view.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            Vector3 move = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0,
                Input.GetAxisRaw("Vertical")), 1);
            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? sprintSpeed : walkSpeed;
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2f;
            verticalSpeed = Mathf.Max(verticalSpeed - 20f * Time.deltaTime, -40f);
            controller.Move((transform.TransformDirection(move) * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        private static void CaptureCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable() => ReleaseCursor();
        private void OnApplicationFocus(bool focused) { if (!focused) ReleaseCursor(); }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            textStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            titleStyle.normal.textColor = new Color(.86f, .94f, .94f);
            textStyle.normal.textColor = new Color(.7f, .8f, .82f);
            GUI.color = new Color(.035f, .065f, .085f, .88f);
            GUI.DrawTexture(new Rect(18, 18, 350, 82), Texture2D.whiteTexture);
            float width = Mathf.Min(660, Screen.width - 36);
            GUI.DrawTexture(new Rect(18, Screen.height - 62, width, 44), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(32, 28, 325, 30), "SHIFT-FOUR / FACILITY GROUNDS", titleStyle);
            GUI.Label(new Rect(32, 61, 325, 30), "CHECKPOINT   /   SERVER   /   CONTROL   /   STORAGE", textStyle);
            string help = Cursor.lockState == CursorLockMode.Locked
                ? "WASD / arrows: walk     Shift: sprint     Mouse: look     Esc: release cursor"
                : "Click the Game view to explore.  WASD: walk  /  Shift: sprint  /  Esc: release cursor";
            GUI.Label(new Rect(32, Screen.height - 54, width - 28, 38), help, textStyle);
        }
    }
}

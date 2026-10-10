using UnityEngine;

namespace ShiftFour
{
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float gunRange = 30f;
        [SerializeField] private float gunDamage = 25f;
        [SerializeField] private float fireInterval = 0.24f;

        private CharacterController controller;
        private Health health;
        private float pitch;
        private float verticalSpeed;
        private float nextShot;
        public string InteractionPrompt { get; private set; } = string.Empty;
        public Vector3 EyePosition => view != null ? view.transform.position : transform.position + Vector3.up * 1.5f;

        public void Configure(Camera playerCamera) => view = playerCamera;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<Health>();
        }

        private void Start() => LockCursor();

        private void Update()
        {
            if (GameSession.Instance != null && GameSession.Instance.Ended) return;
            if (!health.IsAlive || view == null) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (Input.GetMouseButtonDown(0)) LockCursor();
                return;
            }

            float yaw = Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -80f, 80f);
            transform.Rotate(Vector3.up * yaw);
            view.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            Vector3 move = transform.right * Input.GetAxisRaw("Horizontal") + transform.forward * Input.GetAxisRaw("Vertical");
            if (move.sqrMagnitude > 1f) move.Normalize();
            verticalSpeed = controller.isGrounded ? -1f : verticalSpeed - 20f * Time.deltaTime;
            controller.Move((move * walkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);

            Ray ray = new Ray(view.transform.position, view.transform.forward);
            IInteractable target = null;
            if (Physics.Raycast(ray, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                foreach (MonoBehaviour component in hit.collider.GetComponentsInParent<MonoBehaviour>())
                    if (component is IInteractable interactable) { target = interactable; break; }
            }
            InteractionPrompt = target?.Prompt ?? string.Empty;
            if (target != null && Input.GetKeyDown(KeyCode.E)) target.Interact();
            if (Input.GetMouseButton(0) && Time.time >= nextShot) Shoot(ray);
        }

        private void Shoot(Ray ray)
        {
            nextShot = Time.time + fireInterval;
            SoundEvents.ReportShot(transform.position);
            if (!Physics.Raycast(ray, out RaycastHit hit, gunRange, ~0, QueryTriggerInteraction.Ignore)) return;
            Health victim = hit.collider.GetComponentInParent<Health>();
            if (victim != null && victim != health) victim.TakeDamage(gunDamage);
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}

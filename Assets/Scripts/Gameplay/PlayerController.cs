using UnityEngine;

namespace ShiftFour
{
    /// <summary>
    /// First-person player controller owned by the Systems Engineer (Nimsara).
    ///
    /// Branch: feature/gv-nimsara-player-physics
    /// Scope for this branch: movement, CharacterController collision, mouse look,
    /// cursor handling, and the reusable interaction raycast. Shooting remains the
    /// existing baseline behavior and is refined on the later combat/health branch.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera view;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 5f;
        [SerializeField, Min(0.1f)] private float acceleration = 26f;
        [SerializeField, Min(0.1f)] private float deceleration = 32f;
        [SerializeField] private float gravity = -24f;
        [SerializeField, Min(0.1f)] private float groundedStickSpeed = 2f;
        [SerializeField, Min(1f)] private float maximumFallSpeed = 40f;

        [Header("Mouse Look")]
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 2f;
        [SerializeField, Range(1f, 89f)] private float maximumPitch = 80f;

        [Header("Interaction")]
        [SerializeField, Min(0.1f)] private float interactionRange = 3f;
        [SerializeField] private LayerMask interactionMask = ~0;

        [Header("Baseline Combat - refined on the next Nimsara branch")]
        [SerializeField, Min(0.1f)] private float gunRange = 30f;
        [SerializeField, Min(0.1f)] private float gunDamage = 25f;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.24f;

        private CharacterController controller;
        private Health health;
        private Vector3 horizontalVelocity;
        private float pitch;
        private float verticalVelocity;
        private float nextShot;

        public string InteractionPrompt { get; private set; } = string.Empty;
        public Vector3 EyePosition => view != null
            ? view.transform.position
            : transform.position + Vector3.up * 1.5f;

        public bool CanAcceptInput =>
            health != null &&
            health.IsAlive &&
            (GameSession.Instance == null || !GameSession.Instance.Ended);

        public bool IsGrounded => controller != null && controller.isGrounded;
        public float CurrentHorizontalSpeed => new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;

        public void Configure(Camera playerCamera) => view = playerCamera;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<Health>();

            // CharacterController ignores movements smaller than minMoveDistance.
            // Setting it to zero prevents tiny frame-to-frame movement from being lost.
            controller.minMoveDistance = 0f;
        }

        private void Start()
        {
            if (view != null)
                pitch = NormalizePitch(view.transform.localEulerAngles.x);

            LockCursor();
        }

        private void Update()
        {
            bool cursorStateChanged = HandleCursorState();

            if (!CanAcceptInput || view == null)
            {
                StopPlayerInput();
                return;
            }

            // When Escape releases the mouse, clicking once only re-locks the cursor.
            // The click used to change cursor state is consumed by this frame, so it
            // cannot also move, interact, or fire.
            if (cursorStateChanged || Cursor.lockState != CursorLockMode.Locked)
            {
                StopPlayerInput();
                HandleGravityOnly();
                return;
            }

            HandleMouseLook();
            HandleMovement();
            HandleInteractionAndBaselineFire();
        }

        private bool HandleCursorState()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                UnlockCursor();
                return true;
            }

            if (Cursor.lockState != CursorLockMode.Locked &&
                CanAcceptInput &&
                Input.GetMouseButtonDown(0))
            {
                LockCursor();
                return true;
            }

            return false;
        }

        private void HandleMouseLook()
        {
            float yawInput = Input.GetAxis("Mouse X") * mouseSensitivity;
            float pitchInput = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(0f, yawInput, 0f, Space.Self);

            pitch = Mathf.Clamp(pitch - pitchInput, -maximumPitch, maximumPitch);
            view.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void HandleMovement()
        {
            // Raw input avoids built-in smoothing. ClampMagnitude prevents the classic
            // diagonal-speed boost when both horizontal and vertical axes are pressed.
            Vector2 input = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));
            input = Vector2.ClampMagnitude(input, 1f);

            Vector3 desiredDirection = transform.right * input.x + transform.forward * input.y;
            Vector3 desiredVelocity = desiredDirection * walkSpeed;

            float rate = input.sqrMagnitude > 0.0001f ? acceleration : deceleration;
            horizontalVelocity = Vector3.MoveTowards(
                horizontalVelocity,
                desiredVelocity,
                rate * Time.deltaTime);

            MoveCharacter(horizontalVelocity);
        }

        private void HandleGravityOnly()
        {
            // Releasing the cursor disables player input, but gravity/collision should
            // continue to simulate instead of freezing the controller in mid-air.
            horizontalVelocity = Vector3.zero;
            MoveCharacter(Vector3.zero);
        }

        private void MoveCharacter(Vector3 horizontalMotion)
        {
            // Keep the controller gently pressed against the ground instead of
            // accumulating a large downward speed while standing still.
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -groundedStickSpeed;

            verticalVelocity += gravity * Time.deltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, -maximumFallSpeed);

            Vector3 motion = horizontalMotion + Vector3.up * verticalVelocity;
            CollisionFlags flags = controller.Move(motion * Time.deltaTime);

            // controller.isGrounded is updated by Move. Reset downward speed after
            // a confirmed floor collision so slopes/steps remain stable.
            if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
                verticalVelocity = -groundedStickSpeed;

            // If a ceiling is hit, immediately stop upward vertical motion. The current
            // scoped controller has no jump, but this keeps the movement state robust.
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;
        }

        private void HandleInteractionAndBaselineFire()
        {
            Ray viewRay = new Ray(view.transform.position, view.transform.forward);
            IInteractable interactable = FindInteractable(viewRay);

            InteractionPrompt = interactable?.Prompt ?? string.Empty;

            if (interactable != null && Input.GetKeyDown(KeyCode.E))
                interactable.Interact();

            // Kept unchanged in purpose for this branch. Combat-specific behavior is
            // intentionally refined later in feature/gv-nimsara-combat-health.
            if (Input.GetMouseButton(0) && Time.time >= nextShot)
                Shoot(viewRay);
        }

        private IInteractable FindInteractable(Ray ray)
        {
            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    interactionRange,
                    interactionMask,
                    QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            // Interactable scripts normally live on the root while the ray can hit a
            // child collider. Searching parents keeps the interaction contract reusable.
            MonoBehaviour[] components = hit.collider.GetComponentsInParent<MonoBehaviour>();
            foreach (MonoBehaviour component in components)
            {
                if (component is IInteractable interactable)
                    return interactable;
            }

            return null;
        }

        private void Shoot(Ray ray)
        {
            nextShot = Time.time + fireInterval;
            SoundEvents.ReportShot(transform.position);

            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    gunRange,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Health victim = hit.collider.GetComponentInParent<Health>();
            if (victim != null && victim != health)
                victim.TakeDamage(gunDamage);
        }

        private void StopPlayerInput()
        {
            InteractionPrompt = string.Empty;
            horizontalVelocity = Vector3.zero;
        }

        private static float NormalizePitch(float angle)
        {
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

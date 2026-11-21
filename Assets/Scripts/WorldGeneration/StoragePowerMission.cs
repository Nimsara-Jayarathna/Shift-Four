using UnityEngine;

namespace ShiftFour
{
    // Storage state stays with this generator and the cells in its layout.
    [DisallowMultipleComponent]
    public sealed class StoragePowerMission : MonoBehaviour, IInteractable
    {
        private const int RequiredCells = 2;
        internal const string PowerRestored = "EMERGENCY POWER RESTORED";
        internal const string CompletionMessage = "STORAGE MISSION COMPLETE";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color OfflineColor = new Color(1f, .12f, .08f);
        private static readonly Color RestoredColor = new Color(.16f, 1f, .38f);

        [SerializeField] private PowerCellPickup[] cells;
        [SerializeField] private Renderer indicator;
        [SerializeField] private Renderer[] installedCells;
        [SerializeField] private TextMesh status;
        [SerializeField] private Light powerLight;
        private MaterialPropertyBlock properties;
        private int collected;
        private float deniedUntil;

        public bool IsComplete { get; private set; }
        private bool HasCells => collected == RequiredCells;
        public string Prompt => IsComplete ? PowerRestored : "E: Restore Emergency Generator Power";
        public string Objective => IsComplete ? CompletionMessage :
            HasCells ? "Restore Emergency Generator Power" : $"Collect Power Cells ({collected}/{RequiredCells})";
        public string Hint => IsComplete ? PowerRestored :
            HasCells ? "Use the generator at the back of Storage" : "Look on the left crate and right shelving";
        public string Notice => Time.unscaledTime < deniedUntil ? "TWO POWER CELLS REQUIRED" : string.Empty;

        internal void Configure(PowerCellPickup[] pickups, Renderer lamp, Renderer[] sockets, TextMesh display, Light light)
        {
            cells = pickups;
            indicator = lamp;
            installedCells = sockets;
            status = display;
            powerLight = light;
            ResetMission();
        }

        private void Awake() => ResetMission();

        internal void Collect(PowerCellPickup cell)
        {
            if (!isActiveAndEnabled || IsComplete || HasCells || !cell.isActiveAndEnabled) return;
            collected++;
            deniedUntil = 0f;
            cell.gameObject.SetActive(false);
        }

        public void Interact()
        {
            if (!isActiveAndEnabled || IsComplete) return;
            if (!HasCells)
            {
                deniedUntil = Time.unscaledTime + 3f;
                return;
            }

            IsComplete = true;
            deniedUntil = 0f;
            UpdateDisplay();
            Debug.Log(CompletionMessage, this);
        }

        internal void ResetMission()
        {
            collected = 0;
            deniedUntil = 0f;
            IsComplete = false;
            if (cells != null)
                foreach (var cell in cells)
                    if (cell != null) cell.gameObject.SetActive(true);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            Color color = IsComplete ? RestoredColor : OfflineColor;
            SetTint(indicator, color);
            if (installedCells != null)
                foreach (var cell in installedCells)
                    if (cell != null) cell.enabled = IsComplete;
            if (status != null)
            {
                status.text = IsComplete ? PowerRestored + "\n" + CompletionMessage : "POWER OFFLINE";
                SetTint(status.GetComponent<Renderer>(), color);
            }
            if (powerLight != null)
            {
                powerLight.color = RestoredColor;
                powerLight.enabled = IsComplete;
            }
        }

        private void SetTint(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            properties ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(properties);
        }
    }
}

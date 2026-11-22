using UnityEngine;

namespace ShiftFour
{
    // Optional mission state belongs to the generated Control room.
    [DisallowMultipleComponent]
    public sealed class ControlLockdownMission : MonoBehaviour, IInteractable
    {
        private const int SwitchCount = 3;
        internal const string LockdownDisabled = "LOCKDOWN DISABLED";
        internal const string CompletionMessage = "CONTROL ROOM MISSION COMPLETE";
        private const string SwitchesRequired = "ACTIVATE ALL SECURITY SWITCHES";
        internal static readonly Color LockedColor = new Color(1f, .09f, .06f);
        internal static readonly Color DisabledColor = new Color(.15f, 1f, .34f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private LockdownSecuritySwitch[] switches;
        [SerializeField] private Renderer indicator;
        [SerializeField] private TextMesh status;
        [SerializeField] private Renderer[] warningIndicators;
        [SerializeField] private Light[] warningLights;
        private MaterialPropertyBlock properties;
        private int activated;
        private float deniedUntil;

        public bool IsComplete { get; private set; }
        internal event System.Action<ControlLockdownMission> Completed;
        private bool IsReady => activated == SwitchCount;
        public string SwitchProgress => $"Disable Security Switches ({activated}/{SwitchCount})";
        public string Objective => IsComplete ? CompletionMessage :
            IsReady ? "Disable Emergency Lockdown" : SwitchProgress;
        public string Hint => IsComplete ? LockdownDisabled :
            IsReady ? SwitchProgress : "Find the three switches inside Control";
        public string Prompt => IsComplete ? LockdownDisabled : "E: Use Lockdown Override";
        public string Notice => Time.unscaledTime < deniedUntil ? SwitchesRequired : string.Empty;

        internal void Configure(LockdownSecuritySwitch[] roomSwitches, Renderer lamp, TextMesh display,
            Renderer[] warningLamps, Light[] roomWarningLights)
        {
            switches = roomSwitches;
            indicator = lamp;
            status = display;
            warningIndicators = warningLamps;
            warningLights = roomWarningLights;
            ResetMission();
        }

        private void Awake() => ResetMission();

        public void Interact()
        {
            if (!isActiveAndEnabled || IsComplete) return;
            if (!IsReady)
            {
                deniedUntil = Time.unscaledTime + 3f;
                return;
            }

            IsComplete = true;
            deniedUntil = 0f;
            UpdateDisplay();
            Completed?.Invoke(this);
            Debug.Log(CompletionMessage, this);
        }

        internal void Activate(LockdownSecuritySwitch roomSwitch)
        {
            if (!isActiveAndEnabled || IsReady || roomSwitch.IsActivated) return;
            roomSwitch.SetActivated(true);
            activated++;
            deniedUntil = 0f;
        }

        internal void ResetMission()
        {
            activated = 0;
            deniedUntil = 0f;
            IsComplete = false;
            if (switches != null)
                foreach (var roomSwitch in switches)
                    if (roomSwitch != null) roomSwitch.SetActivated(false);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            Color color = IsComplete ? DisabledColor : LockedColor;
            SetTint(indicator, color, ref properties);
            if (status != null)
            {
                status.text = IsComplete ? LockdownDisabled + "\n" + CompletionMessage : "LOCKDOWN ACTIVE";
                SetTint(status.GetComponent<Renderer>(), color, ref properties);
            }
            if (warningIndicators != null)
                foreach (var lamp in warningIndicators) SetTint(lamp, color, ref properties);
            if (warningLights != null)
                foreach (var light in warningLights)
                    if (light != null) light.color = color;
        }

        internal static void SetTint(Renderer renderer, Color color, ref MaterialPropertyBlock block)
        {
            if (renderer == null) return;
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(block);
        }
    }
}

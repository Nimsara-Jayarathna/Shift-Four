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

        internal int ActivatedCount => activated;
        internal int TotalSwitches => SwitchCount;
        public bool IsComplete { get; private set; }
        internal event System.Action<ControlLockdownMission> Completed;
        internal event System.Action<LockdownSecuritySwitch> SwitchActivated;
        internal const float VerificationDuration = 1.2f;
        private float restoredAt;
        private bool lightingSettled;
        internal float Verification => IsComplete ? Mathf.Clamp01((Time.time - restoredAt) / VerificationDuration) : 0f;
        private bool IsReady => activated == SwitchCount;
        public string SwitchProgress => $"Disable Security Switches ({activated}/{SwitchCount})";
        public string Objective => IsComplete ? CompletionMessage :
            IsReady ? "Disable Emergency Lockdown" : SwitchProgress;
        public string Hint => IsComplete ? LockdownDisabled :
            IsReady ? SwitchProgress : "Find the three switches inside Control";
        public string Prompt => IsComplete ? LockdownDisabled : IsReady ? "E: Use Lockdown Override" : SwitchesRequired;
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

            restoredAt = Time.time;
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
            UpdateDisplay();
            deniedUntil = 0f;
            SwitchActivated?.Invoke(roomSwitch);
        }

        internal void ResetMission()
        {
            lightingSettled = false;
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
            Color color = IsComplete ? DisabledColor : IsReady ? Color.cyan : LockedColor;
            SetTint(indicator, color, ref properties);
            if (status != null)
            {
                status.text = IsComplete ? LockdownDisabled + "\n" + CompletionMessage :
                    $"SECURITY SUBSYSTEMS: {activated} / {SwitchCount}\n" +
                    (IsReady ? "OVERRIDE AUTHORIZED\nE: DISABLE LOCKDOWN" : "LOCKDOWN ACTIVE\nOVERRIDE UNAVAILABLE");
                SetTint(status.GetComponent<Renderer>(), color, ref properties);
            }
            if (warningIndicators != null)
                foreach (var lamp in warningIndicators) SetTint(lamp, IsComplete ? DisabledColor : LockedColor, ref properties);
        }

        private void Update()
        {
            if (warningLights == null || lightingSettled || Time.timeScale == 0f) return;
            float blend = Mathf.SmoothStep(0, 1, Verification);
            Color tint = Color.Lerp(LockedColor, new Color(.22f, .85f, .65f), blend);
            float strength = Mathf.Lerp(3.3f + .6f * Mathf.Sin(Time.time * 1.9f), 1.8f, blend);
            lightingSettled = IsComplete && blend >= 1f;
            foreach (var fixture in warningLights)
                if (fixture != null) { fixture.color = tint; fixture.intensity = strength; }
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

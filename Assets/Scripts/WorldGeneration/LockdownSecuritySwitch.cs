using UnityEngine;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    public sealed class LockdownSecuritySwitch : MonoBehaviour, IInteractable
    {
        [SerializeField] private ControlLockdownMission mission;
        [SerializeField] private int number;
        [SerializeField] private Renderer indicator;
        [SerializeField] private TextMesh status;
        private MaterialPropertyBlock properties;

        internal int Number => number;
        public bool IsActivated { get; private set; }
        public string Prompt => IsActivated ? $"SWITCH {number} DISABLED" : $"E: Activate SWITCH {number}";

        internal void Configure(ControlLockdownMission owner, int switchNumber, Renderer lamp, TextMesh display)
        {
            mission = owner;
            number = switchNumber;
            indicator = lamp;
            status = display;
            SetActivated(false);
        }

        public void Interact()
        {
            if (isActiveAndEnabled && mission != null) mission.Activate(this);
        }

        internal void SetActivated(bool active)
        {
            IsActivated = active;
            var color = active ? ControlLockdownMission.DisabledColor : ControlLockdownMission.LockedColor;
            ControlLockdownMission.SetTint(indicator, color, ref properties);
            if (status != null) status.text = active ? "DISABLED" : "ARMED";
        }
    }
}

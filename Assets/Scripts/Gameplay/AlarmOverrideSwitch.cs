using UnityEngine;

namespace ShiftFour
{
    // Optional Control Room interaction; independent of mission progression.
    public sealed class AlarmOverrideSwitch : MonoBehaviour, IInteractable
    {
        [SerializeField] private Renderer indicator;
        [SerializeField] private Material disabledIndicatorMaterial;
        private bool alarmDisabled;

        public string Prompt => alarmDisabled ? "Alarm disabled" : "E: disable emergency alarm";

        public void Interact()
        {
            if (alarmDisabled || !isActiveAndEnabled) return;

            alarmDisabled = true;
            if (indicator != null && disabledIndicatorMaterial != null)
                indicator.sharedMaterial = disabledIndicatorMaterial;

            Debug.Log("Control Room emergency alarm disabled.", this);
        }
    }
}

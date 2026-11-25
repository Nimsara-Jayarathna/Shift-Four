using UnityEngine;

namespace ShiftFour
{
    public sealed class SecurityAccessCard : MonoBehaviour, IInteractable
    {
        [SerializeField] private CheckpointSecurityScanner scanner;

        public string Prompt => "E: Pick up Security Card";

        internal void Configure(CheckpointSecurityScanner checkpointScanner) => scanner = checkpointScanner;

        public void Interact()
        {
            if (isActiveAndEnabled && scanner != null && scanner.TryCollect(this))
                gameObject.SetActive(false);
        }
    }
}

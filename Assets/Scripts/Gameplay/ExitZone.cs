using UnityEngine;

namespace ShiftFour
{
    public sealed class ExitZone : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<PlayerController>() != null)
                GameSession.Instance?.TryExit();
        }
    }
}

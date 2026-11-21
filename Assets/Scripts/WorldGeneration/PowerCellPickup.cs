using UnityEngine;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    public sealed class PowerCellPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private StoragePowerMission mission;

        public string Prompt => "E: Collect Power Cell";

        internal void Configure(StoragePowerMission owner) => mission = owner;

        public void Interact()
        {
            if (isActiveAndEnabled && mission != null) mission.Collect(this);
        }
    }
}

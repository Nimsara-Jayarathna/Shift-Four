using UnityEngine;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    internal sealed class ControlExteriorBeacons : MonoBehaviour
    {
        private const float FlashInterval = .65f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private ControlLockdownMission mission;
        [SerializeField] private Renderer[] lenses = System.Array.Empty<Renderer>();
        private bool illuminated;

        internal void Configure(ControlLockdownMission lockdown, Renderer[] beacons)
        {
            mission = lockdown;
            lenses = beacons;
            // Tint the lenses when generating an Edit Mode preview.
            TintLenses();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            TintLenses();
            SetIlluminated(mission != null && !mission.IsComplete);
            InvokeRepeating(nameof(Tick), FlashInterval, FlashInterval);
        }

        private void OnDisable() => CancelInvoke(nameof(Tick));

        private void Tick()
        {
            bool lockdown = mission != null && !mission.IsComplete;
            if (lockdown || illuminated)
                SetIlluminated(lockdown && !illuminated);
        }

        private void TintLenses()
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColor, ControlLockdownMission.LockedColor);
            foreach (Renderer lens in lenses)
                if (lens != null) lens.SetPropertyBlock(properties);
        }

        private void SetIlluminated(bool visible)
        {
            illuminated = visible;
            foreach (Renderer lens in lenses)
                if (lens != null) lens.enabled = visible;
        }
    }
}

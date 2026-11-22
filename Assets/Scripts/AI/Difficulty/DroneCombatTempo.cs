using System;
using UnityEngine;

namespace ShiftFour
{
    [Serializable]
    internal struct DroneCombatTempo
    {
        [Range(1f, 2f)] public float speedMultiplier;
        [Range(.2f, 1.2f)] public float reactionSeconds;

        internal DroneCombatTempo(float speed, float reaction)
        {
            speedMultiplier = speed;
            reactionSeconds = reaction;
        }

        internal DroneCombatTempo Validated(DroneCombatTempo fallback) => new DroneCombatTempo(
            float.IsFinite(speedMultiplier) && speedMultiplier > 0f ? Mathf.Clamp(speedMultiplier, 1f, 2f) : fallback.speedMultiplier,
            float.IsFinite(reactionSeconds) && reactionSeconds > 0f ? Mathf.Clamp(reactionSeconds, .2f, 1.2f) : fallback.reactionSeconds);

        internal DroneDifficultySettings Apply(DroneDifficultySettings profile)
        {
            profile.patrolSpeed *= speedMultiplier;
            profile.investigationSpeed *= speedMultiplier;
            profile.chaseSpeed *= speedMultiplier;
            profile.acceleration *= Mathf.Lerp(1f, 1.5f, speedMultiplier - 1f);
            profile.angularSpeed *= Mathf.Lerp(1f, 1.3f, speedMultiplier - 1f);
            return profile;
        }
    }

    // Evidence changes readiness and response cadence. The role still owns navigation and shot authorization.
    internal sealed class DroneCombatReadiness
    {
        internal readonly DroneCombatTempo Tuning;
        private float aggressiveUntil = float.NegativeInfinity;
        private float pulseUntil = float.NegativeInfinity;
        private float lastSightAt = float.NegativeInfinity;
        private float firstShotAt = float.PositiveInfinity;

        internal DroneCombatReadiness(DroneCombatTempo tuning) => Tuning = tuning;
        internal bool Aggressive => Time.time < aggressiveUntil;
        internal bool Pulse => Time.time < pulseUntil;
        internal bool ReadyToFire => Time.time >= firstShotAt;

        internal void React()
        {
            if (!Aggressive) pulseUntil = Time.time + .95f;
            aggressiveUntil = Time.time + 4f;
        }

        internal void ConfirmSight()
        {
            float now = Time.time;
            if (now - lastSightAt > .45f) firstShotAt = now + Tuning.reactionSeconds;
            lastSightAt = now;
            React();
        }
    }
}

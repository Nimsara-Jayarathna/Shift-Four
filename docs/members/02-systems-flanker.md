# Nimsara - Student 2: Systems Engineer and Flanker

**Official GV role:** Systems Engineer
**Section:** Storage
**IS agent:** Flanker

Your GV evidence is centered on **player physics and environmental interaction**. Your IS evidence is the Flanker's distinct side-route decision logic. The complete locked sequence is in [NIMSARA_IMPLEMENTATION_PLAN.md](NIMSARA_IMPLEMENTATION_PLAN.md).

## Branch sequence

1. `feature/gv-nimsara-player-physics`
2. `feature/gv-nimsara-combat-health`
3. `feature/gv-nimsara-door-physics`
4. `feature/gv-nimsara-storage-section`
5. `feature/is-nimsara-flanker-scoring`
6. `feature/is-nimsara-flanker-fallback`
7. `fix/nimsara-integration-polish` only for genuine integration defects

Finish, test, PR, and merge one branch before creating the next from updated `develop`.

## Current branch: player physics

The implementation notes and test checklist are in:

[feature/gv-nimsara-player-physics](../branches/feature-gv-nimsara-player-physics.md)

This branch owns:

- first-person WASD movement;
- normalized diagonal movement;
- `CharacterController` collision;
- gravity and grounded handling;
- mouse yaw + clamped camera pitch;
- cursor lock/release/relock behavior;
- forward interaction raycast and range;
- stopping player input after death/end state.

It intentionally leaves the later combat/health, door, Storage-layout, and Flanker work to their own branches.

## Your later assessed work

After this branch is merged, continue with combat/health/cover, then the physical shortcut door and its navigation-state handoff, then Storage geometry, and finally the Flanker's utility-scored left/right route selection and fallback behavior.

**Do not replace your required Systems Engineer work with UI tasks.** Nimthara owns the reusable console/HUD/outcome presentation as the team's workload balance.

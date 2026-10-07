# Student 2 / IT24103464 - Systems Engineer and Flanker

**Official GV role:** Systems Engineer  
**Section:** Storage  
**IS agent:** Flanker

Your GV evidence is player physics/environmental interaction. Your IS evidence is the Flanker's distinct tactical decision logic.

## Exact branch names

Current/created branches:

```text
feature/gv-IT24103464-player-physics
feature/IS-IT24103464-flanker-scoring
```

Planned follow-up branches:

```text
feature/gv-IT24103464-combat-health
feature/gv-IT24103464-door-physics
feature/gv-IT24103464-storage-section
feature/IS-IT24103464-flanker-fallback
fix/IT24103464-integration-polish
```

Normal PR target: `dev`.

## Current IS branch

`feature/IS-IT24103464-flanker-scoring` is split into three implementation checkpoints so the code can be developed and understood progressively instead of arriving as one giant change.

Read:

- [`../branches/IT24103464_FLANKER_3_DAY_COMMIT_PLAN.md`](../branches/IT24103464_FLANKER_3_DAY_COMMIT_PLAN.md)
- [`../branches/feature-IS-IT24103464-flanker-scoring.md`](../branches/feature-IS-IT24103464-flanker-scoring.md)

The final Flanker should:

- use visible or remembered target information rather than hidden live position;
- create meaningful left/right tactical candidates;
- validate them on NavMesh and the shared route graph;
- include actual A* route length in utility scoring;
- use a clear firing-line/range/lateral rationale;
- fall back safely when no flank is valid;
- avoid decision jitter through a small commitment and switch threshold;
- expose enough diagnostics that the decision can be explained in the viva.

Shared A* search remains Asmadala's implementation responsibility. This branch owns the **Flanker's tactical use of that route information**.

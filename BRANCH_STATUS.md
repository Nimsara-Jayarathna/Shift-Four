# Branch Snapshot Status - Checkpoint 1

**Branch:** `feature/IS-IT24103464-flanker-scoring`  
**Base / PR target:** `dev`  
**Checkpoint:** 1 of 3  
**Recommended commit:** `feat(is): add remembered navmesh flank candidates`

Implemented in this snapshot:

- target memory using `LastSeenAt` / `LastKnown`;
- left/right flank generation around the legitimate target point;
- NavMesh snapping;
- shared graph reachability check;
- lateral-quality and preferred-range utility terms;
- close-range and Storage fallback behavior.

Not implemented yet:

- actual A* route-length scoring;
- firing-line quality;
- route-aware fallback scoring;
- commitment / hysteresis;
- decision diagnostics / candidate gizmos.

Open and test this snapshot before committing it. Then move to Checkpoint 2.

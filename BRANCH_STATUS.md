# Branch Snapshot Status - Checkpoint 2

**Branch:** `feature/IS-IT24103464-flanker-scoring`  
**Base / PR target:** `dev`  
**Checkpoint:** 2 of 3  
**Recommended commit:** `feat(is): score A-star flank routes and firing positions`

Adds on top of Checkpoint 1:

- actual `WaypointGraph.FindRoute()` route measurement;
- invalid/no-route candidate rejection;
- A* route-length penalty;
- firing-line reward;
- route-aware pursuit and Storage fallbacks;
- best-valid-candidate selection.

Not implemented yet:

- commitment window;
- switch margin / hysteresis;
- decision-change logging;
- Scene-view candidate gizmos.

Open and test this snapshot before committing it. Then move to Checkpoint 3.

# Branch: `feature/IS-IT24103464-flanker-scoring` - Checkpoint 2

**Student ID:** IT24103464  
**Module:** SE3062 Intelligent Systems  
**Agent:** Flanker  
**PR target:** `dev`

## Goal

Make the candidate selection depend on the team's shared A* route result rather than only direct distance.

## Added in this checkpoint

1. Call `WaypointGraph.FindRoute(transform.position, destination)`.
2. Treat an empty/no-route result as an invalid candidate.
3. Measure the length of the ordered returned route.
4. Reward a clear firing line to the observed/remembered target point.
5. Penalize long A* routes.
6. Validate pursuit and Storage fallbacks through the same route pipeline.
7. Pick the best **valid** option rather than assuming a flank exists.

Checkpoint 2 flank formula:

```text
score = 42
      + lateralQuality * 30
      + rangeQuality * 12
      + clearFiringLine * 14
      - AStarRouteLength * 1.15
```

## Local tests before commit

- One side has a longer/blocked path -> the other side or fallback should win.
- Close the shortcut door -> route cost/reachability should respond to the graph state.
- Both flank candidates invalid -> pursue or Hold Storage; do not freeze.
- Hide behind cover -> firing-line test uses the remembered point, not a live wall-hack target.

Recommended commit: `feat(is): score A-star flank routes and firing positions`.

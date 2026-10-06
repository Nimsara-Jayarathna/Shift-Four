# IT24103464 - Flanker Viva Notes - Checkpoint 2

## What exists now

The Flanker uses the shared A* result as part of its own tactical decision. It measures the actual ordered route length rather than only straight-line distance.

Checkpoint 2 score:

```text
score = 42
      + lateralQuality * 30
      + rangeQuality * 12
      + clearFiringLine * 14
      - AStarRouteLength * 1.15
```

## A* connection

Asmadala owns the shared A* implementation. This agent consumes `WaypointGraph.FindRoute()`.

Know these terms:

- `g`: accumulated travel cost from start;
- `h`: estimate to goal;
- `f = g + h`: selection value;
- parent links: reconstruct the route after reaching the goal;
- empty/no route: candidate is invalid, so the Flanker chooses another option.

## Tactical terms

- lateral quality -> side approach;
- range quality -> useful firing distance;
- firing-line bonus -> candidate is less obstructed relative to the remembered point;
- route-length penalty -> discourages impractically long flanks.

## Not final yet

The agent can still switch too eagerly when left/right scores are nearly equal. Checkpoint 3 adds commitment, switch margin and diagnostics.

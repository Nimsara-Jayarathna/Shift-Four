# IT24103464 - Flanker Viva Notes - Checkpoint 1

## What exists now

The Flanker generates left/right side positions around the latest legitimate target point, snaps those positions to the NavMesh, checks reachability, and scores them numerically.

Checkpoint 1 score:

```text
score = 42
      + lateralQuality * 30
      + rangeQuality * 12
      - straightLineTravel * 0.35
```

## Key explanation

- `LastKnown` is used after sight is lost, so the agent does not track the hidden live player position.
- `lateralQuality` is highest near a 90-degree side approach.
- `rangeQuality` rewards finishing around the preferred attack distance.
- NavMesh snapping prevents choosing obviously non-walkable raw points.
- shared graph `Reachable()` rejects a candidate with no route.

## Not final yet

Actual A* route length, firing-line quality, route-aware fallbacks, commitment/hysteresis and diagnostics are added in later checkpoints.

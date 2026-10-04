# Branch: `feature/IS-IT24103464-flanker-scoring` - Checkpoint 1

**Student ID:** IT24103464  
**Module:** SE3062 Intelligent Systems  
**Agent:** Flanker  
**PR target:** `dev`

## Goal

Turn the starter side-choice into an explicit tactical candidate system without adding the full route-cost/stability logic yet.

## Implemented

1. Use the current player position only while visible.
2. Use `LastKnown` for a limited memory period after sight is lost.
3. Generate left and right flank positions around that legitimate target point.
4. Snap each raw point to the NavMesh.
5. Reject a candidate if the shared graph says it is not reachable.
6. Score valid positions using side-approach quality, preferred attack range, and a small travel-distance penalty.
7. Use close-range engagement and Storage/pursuit fallbacks.

Checkpoint 1 score:

```text
score = 42
      + lateralQuality * 30
      + rangeQuality * 12
      - straightLineTravel * 0.35
```

This is deliberately an intermediate implementation. Actual returned A* route length and firing-line quality are added in Checkpoint 2.

## Local tests before commit

- Visible player -> one of the side candidates can win.
- Move behind cover -> target becomes remembered `LastKnown`, not hidden live position.
- Stay hidden beyond `memorySeconds` -> Hold Storage.
- Put a raw candidate off the walkable area -> NavMesh snap/rejection prevents an invalid tactical point.

Recommended commit: `feat(is): add remembered navmesh flank candidates`.

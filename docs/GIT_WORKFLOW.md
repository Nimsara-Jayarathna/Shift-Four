# Git Workflow - `main` + `dev` + Short Student Branches

## Permanent branches

- `main` - stable/demo/submission branch. Do not develop directly here.
- `dev` - shared team integration branch. Normal feature PRs target `dev`.

## Student 2 naming convention

Use the exact student-ID naming format already present in the repository:

```text
feature/gv-IT24103464-player-physics
feature/gv-IT24103464-combat-health
feature/gv-IT24103464-door-physics
feature/gv-IT24103464-storage-section
feature/IS-IT24103464-flanker-scoring
feature/IS-IT24103464-flanker-fallback
fix/IT24103464-integration-polish
```

Do not create a second repo for IS. Both modules remain in the same Unity project.

## Start the Flanker scoring branch

```bash
git switch dev
git pull origin dev
git switch -c feature/IS-IT24103464-flanker-scoring
```

If the branch already exists:

```bash
git switch feature/IS-IT24103464-flanker-scoring
git pull origin feature/IS-IT24103464-flanker-scoring
```

## Three implementation checkpoints

Follow [`docs/branches/IT24103464_FLANKER_3_DAY_COMMIT_PLAN.md`](branches/IT24103464_FLANKER_3_DAY_COMMIT_PLAN.md).

Recommended truthful checkpoint commits, after each version is actually opened and tested locally:

```text
feat(is): add remembered navmesh flank candidates
feat(is): score A-star flank routes and firing positions
feat(is): stabilize flanker decisions and add diagnostics
```

Do not backdate commits or manufacture history. The snapshots exist to let the code be implemented, tested, and understood incrementally rather than dropped in as one giant commit.

## PR target

```text
feature/IS-IT24103464-flanker-scoring
                  |
                  v
                 dev
```

Use a normal reviewed PR. Preserve the genuine incremental commits instead of squashing the entire branch into a single unexplained commit if your team's assessment workflow expects visible history.

## Unity ownership

| File or area | Normal editor |
| --- | --- |
| `Assets/Scenes/MainLab.unity` and NavMesh integration | Pamudi / World Builder |
| Checkpoint + Scout | Pamudi |
| Storage + `FlankerBrain.cs` + player/door/gameplay | Student 2 / IT24103464 |
| Server + Guard + models | Nimthara |
| Control + Interceptor + shared route follower / A* implementation | Asmadala |
| Shared interfaces | Named owner with affected-member review |

A branch may **consume** shared A* and movement APIs without taking over their ownership.

## Before each commit

1. Open the same Unity editor patch used by the team.
2. Wait for compilation/import.
3. Confirm no new red Console errors.
4. Run the behavior relevant to that checkpoint.
5. Test the fallback/edge case listed in the checkpoint guide.
6. Inspect `git diff` so only intended files are staged.
7. Commit with your own Git identity.

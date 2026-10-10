# Branching Strategy - GV + IS Joint Unity Project

## 1. Why this strategy

The grading in both modules rewards visible individual work and a stable integrated game. Unity also has scene/prefab files that are unpleasant to merge after several people edit them at once. The branch model therefore has two goals:

1. keep `main` safe and demo-ready;
2. preserve each student's real commit history while integrating frequently through `develop`.

Use **one repository**. Do not maintain separate GV and IS codebases.

## 2. Permanent branches

### `main` - stable release branch

`main` represents the version the team is willing to demonstrate or submit.

Rules:

- no direct development on `main`;
- no direct push except emergency repository administration;
- merge only a tested `develop -> main` pull request;
- never merge a known broken Unity project into `main`;
- tag the exact submitted commit, for example `gv-submission-2026-10-21`;
- do not rewrite/force-push its history.

### `develop` - integration branch

`develop` is where completed member work is combined before release.

Rules:

- feature/fix/docs branches start from current `develop`;
- all normal PRs target `develop`;
- keep it playable at the end of each integration session;
- another team member reviews each PR;
- use **Create a merge commit**, not squash, so individual commits remain visible for assessment;
- after a batch of merges, run the shared playthrough before promoting to `main`.

## 3. Short-lived branch types

Use one branch for one understandable piece of work.

| Type | Use | Example |
| --- | --- | --- |
| `feature/gv-<name>-<task>` | Official Graphics role work | `feature/gv-nimsara-door-physics` |
| `feature/is-<name>-<task>` | Personal agent/IS logic | `feature/is-nimsara-flanker-scoring` |
| `feature/shared-<task>` | Shared interfaces with named owner/reviewer | `feature/shared-a-star-routing` |
| `fix/<name>-<problem>` | Bug/regression | `fix/asmadala-door-route-loop` |
| `docs/<topic>` | Documentation only | `docs/branching-plan` |

Use lowercase words separated by hyphens. Delete the remote branch after its PR is safely merged.

## 4. Recommended branches per member

These are examples, not a requirement to create all at once.

### Pamudi - World Builder / Scout

- `feature/gv-pamudi-level-layout`
- `feature/gv-pamudi-lighting-textures`
- `feature/gv-pamudi-navmesh`
- `feature/is-pamudi-scout-investigation`
- `feature/is-pamudi-scout-lost-target`

### Nimsara - Systems Engineer / Flanker

- `feature/gv-nimsara-player-physics`
- `feature/gv-nimsara-door-physics`
- `feature/gv-nimsara-combat-health`
- `feature/is-nimsara-flanker-scoring`
- `feature/is-nimsara-flanker-fallback`

### Nimthara - Core Developer / Guard

- `feature/gv-nimthara-drone-model`
- `feature/gv-nimthara-door-model`
- `feature/gv-nimthara-uv-import`
- `feature/shared-nimthara-console-hud`
- `feature/is-nimthara-guard-cover`

### Asmadala - Agent Controller / Interceptor

- `feature/shared-asmadala-a-star`
- `feature/gv-asmadala-route-following`
- `feature/gv-asmadala-drone-animation`
- `feature/is-asmadala-interceptor-prediction`
- `feature/is-asmadala-interceptor-fallback`

## 5. Daily Git flow

Before starting a task:

```bash
git switch develop
git pull origin develop
git switch -c feature/is-nimsara-flanker-scoring
```

Work and commit at truthful checkpoints:

```bash
git status
git add Assets/Scripts/AI/Agents/FlankerBrain.cs
git commit -m "feat(is): score reachable flank positions"

git add Assets/Scripts/AI/Agents/FlankerBrain.cs
git commit -m "fix(is): fall back when flank route is blocked"

git push -u origin feature/is-nimsara-flanker-scoring
```

Open a PR to `develop`. A teammate reviews and tests. Merge with **Create a merge commit**. Do not squash the member's development history into one commit.

For the next task, return to an updated `develop` and create a new branch.

## 6. Promotion to `main`

Only the team integrator starts this after a full `develop` regression pass:

```bash
git switch develop
git pull origin develop
# run Unity integration tests/playthrough first
```

Open a PR:

```text
develop -> main
```

The PR description must state:

- Unity editor version used;
- full playthrough result;
- known minor issues, if any;
- whether the desktop build was tested;
- commit used for the video/demo candidate.

After merge:

```bash
git switch main
git pull origin main
git tag -a gv-submission-2026-10-21 -m "GV final submission"
git push origin gv-submission-2026-10-21
```

If IS has a later official submission date, make a separate later tag from the version actually submitted for IS. Do not move or rewrite the GV tag.

## 7. GitHub protection settings

Recommended repository settings:

### Protect `main`

- Require a pull request before merging.
- Require at least 1 approving review.
- Block force pushes and deletion.
- Require conversation resolution.
- Prefer **Create a merge commit** for this assignment because it preserves the feature branch's commit chain.

### Protect `develop`

- Require a pull request before merging.
- Require at least 1 review for shared files or main-scene changes.
- Block force pushes.

If the GitHub plan does not provide a particular protection control, enforce the same rule manually as a team.

## 8. Unity ownership to prevent merge conflicts

| Area | Routine owner | Review/handoff rule |
| --- | --- | --- |
| `Assets/Scenes/MainLab.unity` + NavMesh data | **Pamudi** | Others request integration instead of editing it casually |
| Checkpoint prefab + `ScoutBrain.cs` | **Pamudi** | Review shared graph impacts with Asmadala |
| Storage prefab + player/door/health/combat | **Nimsara** | Coordinate route-state interface with Pamudi/Asmadala |
| Server prefab + models + console/HUD/outcomes + `GuardBrain.cs` | **Nimthara** | Coordinate door model with Nimsara; animation hooks with Asmadala |
| Control prefab + `DroneMotor.cs` + A* implementation + animations + `InterceptorBrain.cs` | **Asmadala** | Graph-node changes reviewed with Pamudi |
| `WaypointGraph.cs`, `DroneBrain.cs`, shared prefabs/interfaces | Named owner per PR | At least one reviewer from another affected area |

Never solve a `.unity` or prefab conflict by blindly accepting "ours" or "theirs". The relevant owners agree on the intended result, reopen Unity, repair references if needed, and test the merged state.

## 9. Commit rules for assessment evidence

Good history shows development, not artificial activity.

Use commit messages like:

```text
feat(gv): add physical sliding door collision
feat(is): score guard cover by exposure and path cost
fix(nav): replan when shortcut closes
refactor(ai): separate perception from goal selection
docs: add flanker viva notes
```

Avoid:

- one giant final commit;
- fake/no-op edits to create history;
- committing another person's work under your account;
- committing `Library/`, `Temp/`, builds, or caches;
- rewriting teammate history before submission.

## 10. Emergency fix after `main`

If a release-candidate defect is found after promotion:

```bash
git switch main
git pull origin main
git switch -c fix/nimsara-door-release-blocker
```

Fix only the blocker, test it, PR to `main`, then immediately merge the same fix back into `develop` so the branches do not diverge.

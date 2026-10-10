# Git Workflow - One Repo, Four Accountable Contributors

This file is the command-level companion to [BRANCHING_STRATEGY.md](BRANCHING_STRATEGY.md).

## 1. Baseline repository setup - once

One teammate opens the project in Unity, generates the greybox once, presses Play, saves, and confirms there are no blocking Console errors. Then initialize/push the repository if it is not already in Git:

```bash
git init -b main
git add .
git commit -m "chore: initialize Shift Four Unity baseline"
git remote add origin https://github.com/YOUR-ACCOUNT/shift-four.git
git push -u origin main
```

Inspect `git status` and `git diff --cached --stat` before the first commit. The baseline must include generated Unity assets, all required `.meta` files, `Packages`, `ProjectSettings`, docs, and `ArtSource/`; it must not include `Library`, `Temp`, logs, editor caches, or builds.

Create the integration branch:

```bash
git switch -c develop
git push -u origin develop
```

All normal development starts from `develop`, not `main`.

## 2. Branch naming

Use short branches that identify the module and owner when useful:

```text
feature/gv-pamudi-level-layout
feature/is-pamudi-scout-search
feature/gv-nimsara-door-physics
feature/is-nimsara-flanker-scoring
feature/gv-nimthara-drone-model
feature/is-nimthara-guard-cover
feature/gv-asmadala-route-following
feature/is-asmadala-interceptor-prediction
feature/shared-asmadala-a-star
fix/nimsara-door-route-state
docs/development-plan
```

Do not create one long-lived branch per student or separate GV/IS codebases. Short branches integrate more safely and produce clearer evidence.

## 3. Start one task

```bash
git switch develop
git pull origin develop
git switch -c feature/is-nimsara-flanker-scoring
```

Make only the intended change, test it in Unity, then commit at natural checkpoints:

```bash
git status
git add Assets/Scripts/AI/Agents/FlankerBrain.cs
git commit -m "feat(is): score reachable flank positions"

git add Assets/Scripts/AI/Agents/FlankerBrain.cs
git commit -m "fix(is): add blocked-flank fallback"

git push -u origin feature/is-nimsara-flanker-scoring
```

Open a pull request to **`develop`** and complete `.github/PULL_REQUEST_TEMPLATE.md`.

## 4. Review and merge

A reviewer checks:

- project opens/compiles;
- no new red Console errors;
- changed behavior works in Play mode;
- at least one relevant edge case works;
- no accidental main-scene/shared-file conflict exists;
- the student's commits are real and understandable.

Merge using **Create a merge commit**. Avoid squashing assessed development into one commit, because both assignments value visible individual contribution/history.

After merge, delete the short-lived feature branch.

## 5. Unity ownership

| File or area | Routine owner |
| --- | --- |
| `Assets/Scenes/MainLab.unity`, final section assembly, NavMesh integration | **Pamudi** |
| Checkpoint prefab + `ScoutBrain.cs` | **Pamudi** |
| Storage prefab + player/door/health/combat + `FlankerBrain.cs` | **Nimsara** |
| Server prefab + `GuardBrain.cs` + `ArtSource/` + `Assets/Models/` + console/HUD/outcomes | **Nimthara** |
| Control prefab + A* implementation + `DroneMotor.cs` + animations + `InterceptorBrain.cs` | **Asmadala** |
| `WaypointGraph.cs`, `DroneBrain.cs`, shared prefabs/interfaces | named owner in that PR + reviewer |

A changed prefab instance can also modify `MainLab.unity`, so coordinate main-scene changes with Pamudi. Never rerun the greybox generator to obtain teammate changes; fetch/merge through Git.

## 6. Handling Unity conflicts

If a `.unity`, `.prefab`, `.asset`, or `.meta` conflict occurs:

1. stop and identify the file's normal owner;
2. do not blindly choose "ours" or "theirs";
3. compare what each branch intended to change;
4. let the relevant owner integrate/repair the intended final state;
5. reopen Unity and wait for import;
6. verify references/components/colliders;
7. run the affected behavior before completing the merge.

Keep the exact same Unity patch and committed package manifest on all machines.

## 7. Promote `develop` to `main`

`main` is not the everyday integration branch. Promote only after the full [INTEGRATION.md](INTEGRATION.md) checklist passes.

Open one PR:

```text
develop -> main
```

The release PR should record the editor version, desktop-build result, full playthrough result, and known minor issues. After review, merge with a merge commit.

For the GV submission candidate:

```bash
git switch main
git pull origin main
git tag -a gv-submission-2026-10-21 -m "GV final submission"
git push origin gv-submission-2026-10-21
```

If the official IS deadline is later, create a separate later tag for the exact IS submission. Do not move/rewrite the GV tag.

## 8. Commit quality

Good examples:

```text
feat(gv): add physical sliding door collision
feat(is): make scout search last known position
feat(is): score guard cover by exposure and path cost
fix(nav): replan when shortcut closes
refactor(ai): separate perception from goal selection
docs: add interceptor viva evidence
```

Avoid one giant last-minute upload, fake/no-op commits, committing teammate work as your own, or rewriting teammate history.

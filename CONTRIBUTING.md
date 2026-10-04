# Contributing to Shift Four

This is a four-student assessed Unity project. Contribution history is part of the evidence, so keep work attributable and merge carefully.

## Before coding

Read:

1. `docs/PROJECT_PLAN.md`
2. `docs/BRANCHING_STRATEGY.md`
3. `docs/TEAM_RESPONSIBILITIES.md`
4. your file in `docs/members/`

Then update `develop` and create one short task branch.

```bash
git switch develop
git pull origin develop
git switch -c feature/gv-yourname-short-task
```

Use `feature/is-*` for your autonomous-agent logic, `feature/gv-*` for your prescribed Graphics role, `feature/shared-*` for agreed shared work, `fix/*` for defects, and `docs/*` for documentation.

## Before opening a PR

- Run the changed behavior in Unity Play mode.
- Check the Console for new red errors.
- Test one meaningful edge case.
- Do not casually edit `MainLab.unity`; Pamudi coordinates the main scene.
- Commit generated `.meta` files for assets you add/change.
- Do not commit `Library/`, `Temp/`, builds, or local IDE files.
- Fill in the PR template completely.

Normal PR target: `develop`. Merge with **Create a merge commit**, not squash, so the student's real commit sequence remains visible. Promote `develop` to `main` only after the full integration checklist passes.

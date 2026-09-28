# One repo, four accountable contributors

## The initial push (done by one teammate)

First open the extracted project in Unity, generate `MainLab`, press Play, and save everything. Create a **private** repository named `shift-four` on GitHub and add the four students as collaborators. In the `shift-four` directory run:

```bash
git init -b main
git add .
git commit -m "chore: initialize Shift Four Unity greybox"
git remote add origin https://github.com/YOUR-ACCOUNT/shift-four.git
git push -u origin main
```

Replace `YOUR-ACCOUNT` with the real owner or organization. If Git is already initialized, skip `git init`. Inspect `git status` and `git diff --cached --stat` before the initial commit. The first commit must include `Assets`, all `.meta` files, `Packages`, `ProjectSettings`, `docs`, and `ArtSource` when it contains models; it must **not** include `Library`, `Temp`, local build folders or editor caches. Use your own Git name/email so individual work is attributed correctly.

## Each task after that

Every member **clones the same repo**. Do not create four copies of the Unity project or a second AI repo. For one small task:

```bash
git switch main
git pull
git switch -c feature/scout-investigation
# Edit your assigned files in Unity and save. Run the changed behaviour.
git status
git add Assets/Scripts/AI/Agents/ScoutBrain.cs
git commit -m "feat(ai): make scout search last known position"
git push -u origin feature/scout-investigation
```

Open a pull request to `main`, have another member review it, then merge it **without squash** so the sequence of individual commits remains visible. Start a fresh branch from updated `main` for the next task. Replace Scout with your assigned agent/role in the example.

Commit at natural checkpoints: section shape, first agent goal choice, blocked-route fallback, model topology, UVs, animations and bug fixes. A series of truthful commits is much stronger viva evidence than a last-day upload. Do not rename teammates' files solely to create a commit.

## Unity file ownership

| File or area | Normal editor |
| --- | --- |
| `Assets/Scenes/MainLab.unity` and NavMesh data | Member 1, coordinating scene integration |
| `Assets/Prefabs/Sections/Checkpoint.prefab`, `ScoutBrain.cs` | Member 1 |
| `Assets/Prefabs/Sections/Storage.prefab`, `FlankerBrain.cs`, player/door/gameplay scripts | Member 2 |
| `Assets/Prefabs/Sections/Server.prefab`, `GuardBrain.cs`, `ArtSource/`, `Assets/Models/` | Member 3 |
| `Assets/Prefabs/Sections/Control.prefab`, `InterceptorBrain.cs`, `DroneMotor.cs`, animations | Member 4 |
| `WaypointGraph.cs`, `DroneBrain.cs`, shared prefab changes | Named owner and reviewer agreed in the PR |

Prefab changes are separate files. A changed prefab instance in `MainLab.unity` can still modify the main scene, so coordinate it with Member 1. **Never run the greybox generator again** to pull teammate changes. Fetch and merge through Git.

If a pull request conflicts in a `.unity` scene or prefab, do not blindly select one side. Have the relevant owners open Unity together, agree on the intended state, and test it after the merge. Merging text-based Unity files is possible, but a clean launch and playthrough is the final check. Keep the exact same Unity Editor patch and committed package manifest on all machines.

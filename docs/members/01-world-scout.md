# Member 1 — World Builder and Scout

**Your IS assessment:** the Scout's decisions. **Your GV assessment:** overall 3D layout, walkability/NavMesh, lighting, texturing, and visual coherence. You also arrange the Checkpoint section and integrate `MainLab.unity` after teammates finish their prefabs. Put your real name in `docs/SCOPE.md`.

## Start from the baseline

Open `Assets/Prefabs/Sections/Checkpoint.prefab` in Prefab Mode. The floor, fixed cover, and console are placeholders. Open `Assets/Scripts/AI/Agents/ScoutBrain.cs`: it starts with patrol, gunshot investigation, and last-known-position search. Read `WaypointGraph.cs` and `DroneBrain.cs` to understand the shared interfaces before changing Scout. Do **not** rerun the greybox generator after the scene is committed.

## Work in this order

1. Sketch the four-section loop, the two alternate routes, cover sight lines, door shortcut and exit. Keep the four compact rooms. Adjust your own Checkpoint prefab and integrate teammates' prefabs by changing only the shared main scene when coordinated.
2. Re-bake the NavMesh after major level changes: select **NavMesh surface — baked greybox** in `MainLab.unity`, bake in its Inspector, save the scene and NavMesh data. Walk every corridor and doorway with both the player and an agent. Make sure the door shortcut has another route when closed.
3. Improve the shared materials and lighting, giving rooms recognisable visual cues without four unrelated art styles. Check cover collider heights: a full-height rack/pillar should block the drone's shot ray.
4. In `ScoutBrain.cs`, show why a recent sound outranks patrol, why visible player sight outranks sound, and when stale information expires. Tune hearing range, search delay and patrol points. Keep its observations honest: if the player is behind a wall, the Scout uses the last known location rather than reading the current location.
5. Test Scout with shots from outside its view, a hidden player, and the open/closed shortcut. Ensure it eventually returns to patrol and never waits at an unreachable point forever.

## Evidence to produce

Show the examiner the alternate routes and baked NavMesh, one lighting/material choice and its performance effect, and Scout decisions in the HUD/code. Capture a small sequence of commits such as `feat(level): shape checkpoint alternate route`, `feat(ai): scout investigates gunfire`, `fix(nav): rebake room connections`.

**Done when:** all four sections remain reachable; the level looks like one facility; Scout patrols, investigates, searches, recovers from a failed search and replans on a changed route. You can trace one Scout choice and one A* route aloud.

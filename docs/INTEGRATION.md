# Integration, Release, Submission, and Viva Checks

## 1. Once per feature PR

- Import/compile succeeds with no new red Console errors.
- The edited prefab still has required colliders, script components, references, and `.meta` files.
- The changed feature works in Play mode in its normal case.
- At least one meaningful edge/fallback case is tested.
- The change does not stop the other agents or break shared state.
- If geometry changed, Pamudi checks/rebakes NavMesh and tests affected routes.
- If door/navigation interfaces changed, Nimsara + Pamudi + Asmadala verify open/closed routing.
- If drone visual hierarchy changed, Nimthara + Asmadala verify movement/animation/components remain attached correctly.
- A teammate reviews before merging to `develop`.

## 2. Integration order after major features

1. Pull the latest `develop` and let Unity finish import.
2. Verify all four section prefabs load in `MainLab.unity`.
3. Verify player movement, shooting, health, interactions, and door.
4. Verify A* returns a route with the shortcut open and an alternate route with it closed.
5. Verify all four agents request destinations through the shared route interface.
6. Verify models/animations do not change gameplay colliders/components accidentally.
7. Verify consoles/HUD/outcomes receive correct shared state.
8. Run all four agents together.

## 3. Full release playthrough

1. Start at the Checkpoint. All four named agents appear/operate simultaneously.
2. Traverse Checkpoint, Storage, Server, and Control. Confirm no holes, impossible doorways, or trapped drones.
3. Shoot near the Scout while hidden. It investigates sound/last-known location rather than reading the player through a wall.
4. Show Flanker selecting a side route and changing choice if its preferred route is invalid.
5. Show Guard selecting cover and relocating when exposed; remove/disable a candidate and confirm fallback.
6. Show Interceptor selecting a reachable junction ahead of observed movement, then handling a failed prediction.
7. Close the shortcut while a drone is travelling. The route changes/recalculates or safely falls back; the drone does not clip through the door.
8. Hide behind fixed cover. Enemy shots stop damaging through solid geometry. After the configured delay, player health recovers.
9. Disable one drone while others are active. It stops attacking/moving and shared counters update once without null references.
10. Activate each console once. Re-activate attempts must not duplicate progress.
11. Enter the exit early and confirm it does not win.
12. Complete all objectives, enter the exit, see win state, then test restart.
13. Let player health reach zero, see loss state, then test restart.
14. Watch the Console and frame pacing for the complete run.

The supplied source baseline was generated outside a running Unity Editor environment. The first local Play mode pass is a real project milestone; fix compile/import/runtime issues before relying on the baseline for assessment.

## 4. Desktop build check

Before promotion to `main`:

- make a desktop build for the actual demo machine;
- launch the built executable/application outside the Editor;
- repeat the critical route/door/combat/win path;
- confirm mouse/input behavior, resolution, and restart;
- confirm no missing model/material/scene references.

## 5. Release flow

When `develop` passes the full checklist:

1. freeze feature work;
2. open `develop -> main` PR;
3. another member verifies the release candidate;
4. merge with a merge commit;
5. test/record from the exact `main` commit;
6. tag the submitted commit, e.g. `gv-submission-2026-10-21`.

Do not record the video from one commit and submit source from a materially different one.

## 6. Individual grading evidence

### Pamudi - World Builder / Scout

Show level/NavMesh/lighting/textures and explain one optimization/design choice. In IS, show Scout observations, scores/priorities, last-known-position search, fallback, and an A* route.

### Nimsara - Systems Engineer / Flanker

Show player/collision/door physical interaction, cover/health behavior, and explain the chosen interaction approach. In IS, show candidate flank positions, scoring/reachability/path cost, fallback, and its route.

### Nimthara - Core Developer / Guard

Show both original Blender models, topology, UVs, Unity import, and polygon/texture choices. Also show console/HUD/outcome integration. In IS, show Guard cover scoring, relocation, invalid-cover fallback, and its route.

### Asmadala - Agent Controller / Interceptor

Show ordered route points becoming smooth movement/rotation/animations and explain path update frequency. Explain A* implementation. In IS, show prediction/junction scoring and failed-prediction fallback.

## 7. Three-minute GV demonstration outline

| Time | Show |
| --- | --- |
| 0:00-0:25 | One connected lab, player controls, HUD/objective |
| 0:25-0:55 | Door, console, shooting, fixed cover, health recovery |
| 0:55-2:15 | Scout, Flanker, Guard, Interceptor distinct choices + one route change |
| 2:15-2:40 | Original models, lighting/textures, smooth movement/animations |
| 2:40-3:00 | Final progress, exit, win state |

Rehearse the exact path so required evidence is visible within three minutes.

## 8. Final reminder

The supplied GV brief states source code + a three-minute demo video, concept/prototype/final showcase milestones, and a 21 October 2026 deadline. The supplied IS brief states a five-week duration but does not list a separate date/submission format. Confirm CourseWeb or lecturer updates before final IS submission.

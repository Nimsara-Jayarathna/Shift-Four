# Integration, submission and viva checks

## Once per pull request

- Import succeeds without red Console errors.
- The edited prefab still has its colliders, script components and references.
- The feature works in Play mode on its own section and does not stop the other three agents.
- If geometry changed, Member 1 rebakes and commits the NavMesh data, then tests all four routes.
- A teammate reviews the code/asset and runs the changed behaviour before merging.

## Full playthrough before recording

1. Start at the Checkpoint. All four named agents appear in the HUD simultaneously.
2. Traverse Checkpoint, Storage, Server and Control through both available room connections. Observe no holes, impassable doorways or trapped drones.
3. Shoot in the Scout's hearing range while hidden; it investigates the sound rather than reading the player's location through a wall.
4. Show the Flanker selecting a side route, the Guard changing cover, and the Interceptor selecting a junction ahead of recent motion. Check their HUD decisions and relevant code.
5. Close then open the door; show A* graph route choice/replanning. Check agents do not attempt to cross the door collider while closed.
6. Use fixed cover to stop incoming fire. Take damage and verify recovery only after an uninterrupted delay. Let health reach zero and test R restart.
7. Activate every console once, disable every drone, enter the exit, see the win state and test R restart. Enter the exit early and confirm it does not win.
8. Make a desktop build and repeat the critical path on the actual demonstration computer. Watch for Console errors and major performance drops.

The provided source has **not** been run in a Unity Editor in the generation environment. Make the first local Play mode pass a team milestone, fix any compile/import/runtime problem there, and only then base final work on it.

## Individual grading evidence

For IS, each member should point to their own agent file and demonstrate an observation, at least two candidate goals, the reason the winning goal scored higher, an A* route, and a change when information or a route becomes invalid. Be able to explain `g`, `h`, the parent chain, the closed door edge, and what happens if no route exists.

For GV, show each prescribed role, with work committed over time: Member 1's level/NavMesh/lighting; Member 2's player/collision/interactive door; Member 3's two original Blender models and UVs; Member 4's path movement/turning/animations. Each person should explain a design choice and a practical optimization.

## Three-minute demonstration outline

| Time | Show |
| --- | --- |
| 0:00–0:25 | Four connected sections, player movement, HUD and objective |
| 0:25–0:55 | Door, console, shooting, fixed cover and health recovery |
| 0:55–2:20 | Scout, Flanker, Guard, Interceptor choices and a changed route |
| 2:20–2:45 | Original models, coherent lighting, smooth movement/animations |
| 2:45–3:00 | Final console, exit and win condition |

The GV brief cited in the supplied scope asks for source code and a three-minute video, a concept pitch, a prototype, a final showcase, and individual viva. Its stated deadline is 21 October 2026. Check CourseWeb for the actual IS submission format/date and any updated announcements. Keep the recorded footage to what the build really does; use the live project/code in the viva.

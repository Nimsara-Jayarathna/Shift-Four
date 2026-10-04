# Member 4 — Agent Controller and Interceptor

**Your IS assessment:** the Interceptor's prediction and fallback. **Your GV assessment:** smooth movement, turns and visible animation along paths. You own the Control section and shared `DroneMotor.cs`.

## Start from the baseline

Open `Assets/Prefabs/Sections/Control.prefab`, `Assets/Scripts/AI/Agents/InterceptorBrain.cs`, and `Assets/Scripts/AI/Movement/DroneMotor.cs`. The motor already follows a list of A* points with `NavMeshAgent`, rotates toward velocity and adds a small hover motion. These are a starting point, not your complete animation evidence.

## Work in this order

1. Arrange the Control room and exit marker, leaving both entrance routes reachable. Coordinate edits to the exit/main scene with Member 1. Test drones returning from another room without clipping walls or turning abruptly.
2. Improve waypoint arrival, turning speed, braking and path failure handling in `DroneMotor.cs`. The ordered points must turn into smooth motion. Test an open and closed shortcut, an unreachable goal and a moving player.
3. Add simple, visible hover, fire, hit and shutdown/death feedback using a small Animator/Animation Clip setup or controlled procedural animation. Work with Member 3's drone model; preserve the colours identifying each agent. Explain how animation responds to motor/brain events.
4. In `InterceptorBrain.cs`, score reachable junctions ahead of observed player movement, handle a player who stops or reverses direction, and fall back to last known position. Prediction uses **recent observed positions**, not a hidden read of the player's movement through a wall.
5. Observe all four agents together for a full playthrough. Fix route loops, spinning, stuttering, and visible movement through a closed door. Keep route recalculation event-driven or on modest intervals.

## Evidence to produce

Show the ordered path points, turning and animation transitions in the scene, a prediction that works, and a failed prediction with a reasonable fallback. Example commits: `feat(movement): smooth waypoint turns`, `feat(animation): drone fire and shutdown feedback`, `feat(ai): intercept ahead of player`.

**Done when:** all four agents move and animate convincingly along their chosen routes, Interceptor visibly picks a junction ahead, and you can explain both its prediction and a recovery case.

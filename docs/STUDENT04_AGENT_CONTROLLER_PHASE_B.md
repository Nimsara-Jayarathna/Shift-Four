# Student 04 — Agent Controller, Phase B

**Responsibilities** · Asmadala · GV Agent Controller · Control section · IS Interceptor

The four drone policies choose tactical destinations. The shared controller takes that intent through the team-authored waypoint graph, the native NavMeshAgent and the drone's raised collision envelope. The existing animation system consumes accepted motion and applies cosmetic banking to the model. Phase A added outdoor Interceptor prediction and the F6 overlay. Phase B adds shared route execution and recovery, and extends that overlay with controller evidence.

## A* and endpoint attachment

The original graph chooses the nearest usable start and destination nodes, honors gate flags and reconstructs an ordered list of stops. An off-node endpoint could attach successfully to a nearby isolated node and still get an empty route even with a usable node in range.

Phase B tries the preferred attachment first to preserve existing routes. On failure it can consider up to four nearby alternatives per off-node endpoint within four metres, with at most four local NavMesh probes per endpoint and up to four accepted attachments including the preferred one. An endpoint exactly at an authored node stays pinned to that node. Alternatives require a direct NavMesh connector. When a gate partitions a connected region, the fallback excludes alternatives across the closed partition even before native carving catches up.

- **G** starts with the Euclidean distance from the world origin to its attached waypoint. Each open graph edge adds the Euclidean distance between its two waypoints.
- **H** is the Euclidean distance to the goal waypoint in the preferred search. In the fallback it is the distance to the requested world destination. This lower bound is consistent with the modeled Euclidean connector and edge costs.
- **F = G + H**. The search selects the lowest open F, closes that node and updates distances and parents when it finds a cheaper path. The fallback seeds one A* search with all validated start attachments. The first goal attachment popped minimizes the modeled total cost, including its Euclidean tail.
- Follow parents from the reached waypoint to the first attachment and reverse the order. The motor keeps the logical waypoint order. A failed search returns an empty list. The closed set bounds the number of node expansions in each search.

The modeled graph distance is distinct from distance traveled around physical obstacles. NavMesh validates local navigation, and the body still sweeps its actual flight envelope.

## Native movement and physical recovery

For exactly aligned forward waypoints, the motor can request one clear, straight native leg and keep a separate logical waypoint index. It advances that index only after reaching the waypoint tolerance on a clear local segment. If avoidance leaves the corridor and passes the waypoint outside tolerance, the motor requests that waypoint again. A turn, a short segment, a vertical break or a failed NavMesh ray ends the straight leg. The native agent retains acceleration and braking, with the authored stopping radius at the final goal. The merge guard uses `d = v² / (2a)`, plus the endpoint stopping radius and waypoint transition clearance, to leave room to reach the intermediate waypoint before stopping.

The motor measures displacement after DroneBody has accepted the frame's movement. The next update turns toward that accepted velocity, with low-speed hysteresis. It clamps the signed yaw increment against the authored angular cap. Combat aiming takes priority. The animation layer continues using accepted movement, with visual banking separate from the collision root.

A gate or graph version change invalidates an active route before replanning. Incomplete native paths and pending timeouts also invalidate routes. The no-progress timer now requires meaningful improvement in remaining distance. Physical obstruction uses a 0.3 m improvement threshold to avoid counting small sidesteps as progress. Ordinary stalls get two seconds and a player or drone standing in the corridor gets up to seven seconds without improvement before a replan. The motor makes at most two timed retries per recovery cycle. An exhausted physical block can rearm when its collider disappears, is disabled or changes bounds by the configured threshold. Current intent and graph changes can also start a new cycle. Doors and actors remain protected by the raised body sweep.

## Reproduce the demonstration

1. Open `Assets/Scenes/OpenWorldPrototype.unity`, enter Play Mode, start the game, and focus the Game view. Press **F6** and move onto an outdoor road until a Scout alerts an Interceptor.
2. Follow the green route. Pale yellow marks the active waypoint and accepted movement direction. Purple marks the final destination. Red marks a rejected or invalid waypoint. **Shift+F6** cycles units and returns to automatic selection.
3. In **Agent Controller**, watch the mode, actual and target speed, waypoint number, remaining distance, graph version, native path status, block reason and route replans. When the selected unit performs a graph search, the panel shows its recent expanded nodes with G, H, F and parent node. Direct connectors are labeled because they do not expand A* nodes. Closing F6 discards the trace and stops collecting new visits.
4. Run **Window > General > Test Runner**. In **EditMode**, run `WaypointGraphTests`. In **PlayMode**, run `DroneMotorPlayModeTests` and `Student4UnifiedPlayModeTests.All400GateEndpointQueries_MatchIndependentDijkstra`. The in-memory motor fixtures cover a gate closing midflight, a raised beam and a temporary player hold without scene edits.

## Regression evidence

The new fixtures cover fallback attachment in both directions, a closed partition on an uncarved mesh, graph search evidence, a gate closing during movement, straight-waypoint speed, a temporary drone blocker and recovery after a permanent obstruction is removed. The baseline failed the two alternate-attachment cases and stayed in Following against the raised permanent blocker. The graph trace fixture recorded G = 3 m, H = 4 m and F = 7 m at its middle node and verified that the traced route matched the ordinary route. The 400-query MainLab gate matrix matched an independent Dijkstra calculation for every endpoint pair and gate combination. The outdoor tests exercised generated collision, Scout dispatch and the extraction handoff. The broad Play Mode run passed 79 of 89 cases. Its ten failures matched the pre-Phase-B failure set exactly. Nine use earlier movement profile expectations and one expects a cover fixture to leave visual history empty. The targeted controller and outdoor rerun passed all 37 cases. After the final yaw precision adjustment, the 123-case Edit Mode suite and the six-case movement and benchmark rerun both passed.

## Measurements and limitations

Measured in Unity 6000.6.4f1 on an Apple M1, in batch Play Mode with graphics disabled. The existing `AllocationMicrobench_LiveComponents` fixture warmed each component, then called it 100 times in the same frame. The graph query used the same all-open MainLab nodes and 73 m route in both runs.

| Workload | Before Phase B | Phase B |
| --- | ---: | ---: |
| A* managed allocation events per query | 10 | 7 |
| A* total wall time for 100 queries | 0.747 ms | 0.591 ms |
| Motor and body allocation events per 100 calls, each archetype | 0 | 0 |
| Motor Update wall time per 100 calls, range across archetypes | 0.279–0.332 ms | 0.205–0.272 ms |

The graph no longer needs an intermediate parent-index list during reconstruction, and its default endpoint checks reuse a native path object. The A* allocation count was seven events per query in every instrumented Phase B run. The wall-time measurements varied, including 0.995 ms for 100 A* calls in the broad run, so the timings do not establish a CPU speedup. A separate 720-frame live fixture completed with all four agents, but Unity's batch recorder returned no samples for the selected route, motor and body markers. Live marker timing and a field performance benchmark across multiple outdoor seeds remain open measurements. In the Editor CPU Profiler, use `ShiftFour.FindRoute`, `ShiftFour.CanConnect`, `ShiftFour.DroneMotor` and `ShiftFour.DroneBody` to inspect those workloads.

The alternate attachment budget deliberately limits recovery to nearby local connectors. A physically incomplete native leg with no new graph version, intent or observable collider change can remain blocked after its retries are exhausted. The F6 trace shows the last search executed while tracing the selected drone, with the most recent four visits in the panel.

## Files changed

- `Assets/Scripts/AI/Core/WaypointGraph.cs`
- `Assets/Scripts/AI/Movement/DroneMotor.cs`
- `Assets/Scripts/AI/Movement/DroneBody.cs`
- `Assets/Scripts/AI/Agents/InterceptorBrain.cs` for the diagnostics accessor
- `Assets/Scripts/WorldGeneration/OutdoorInterceptorDiagnostics.cs`
- `Assets/Tests/EditMode/WaypointGraphTests.cs`
- `Assets/Tests/PlayMode/DroneMotorPlayModeTests.cs`
- `Assets/Tests/PlayMode/OpenWorldCollisionPlayModeTests.cs` for the cinematic shutter's asynchronous opening
- `docs/STUDENT04_AGENT_CONTROLLER_PHASE_B.md`

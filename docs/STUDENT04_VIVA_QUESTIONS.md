# Student 04 · Interceptor viva

| Question | Answer and source |
| --- | --- |
| 1. What is A*? | A search that expands the open node with minimum estimated total cost. It returns a least-cost path between the chosen graph endpoints for the implemented Euclidean edge costs and admissible heuristic. See `WaypointGraph.Search`. |
| 2. What are G, H and F? | G is the travelled cost from the attached start, H is Euclidean distance to the goal, and F = G + H. A closed gate removes its edge from traversal. |
| 3. Why A* and NavMesh together? | The graph handles topology and gate conditions. Complete NavMesh segments and raised-envelope checks keep the drone route physically usable between anchors. See `ControlTacticalRouteValidator.RouteCore`. |
| 4. How is the route reconstructed? | The search stores the cheapest predecessor for each relaxed node. It follows parents from the selected goal, reverses the resulting chain and attaches the exact destination. |
| 5. How does invalidation work? | Opening a gate increments the graph version. The motor checks its planned version and recalculates. Unavailable, blocked and stale plans follow bounded recovery or safe stopping. See `DroneMotor` and `InterceptorTacticalMemory`. |
| 6. How does a drone brake? | The motor sets a stopping distance and consumes accepted travel from the NavMeshAgent and collision body. The speed profile approaches its mode target and the animation reads actual speed and deceleration. |
| 7. How is the animation derived? | `DroneRotorVisuals` reads motor velocity, turn and stance, then updates model-local tilt, rotors and sensor cues. The collision and navigation roots stay with movement. |
| 8. How does the Interceptor predict player movement? | `InterceptorMotionEstimator` filters genuine samples into velocity and acceleration. `InterceptorPredictionModel` generates reachable route hypotheses and estimates player and drone ETAs. |
| 9. How is a prediction evaluated? | `InterceptorPredictionEvaluator` finds the nearest time-compatible direct sighting for the immutable forecast and records spatial error. A forecast with insufficient observations becomes unevaluated. |
| 10. What is prediction confidence? | A current-evidence quantity. Recent, consistent motion supports it. Age, uncertainty and route fit reduce it. It is separate from historical hypothesis reliability. |
| 11. What is Utility AI here? | Eligible actions receive a score incorporating evidence, travel, uncertainty and stability. `InterceptorUtilityDecision` retains a valid commitment unless the switch margin or a priority condition is met. |
| 12. How is belief represented? | A normalized set of bounded weighted regions associated with dated evidence. Reachable paths guide propagation. Negative evidence requires actual coverage. See `InterceptorBeliefState` and `OutdoorInterceptorResponse.HasCoverage`. |
| 13. How do units share knowledge? | A bounded board preserves source, time and expiry for visual, Scout, sound, inspection and obstruction reports. The receiver filters duplicates and stale reports before use. |
| 14. How does the assignment optimizer work? | It considers bounded combinations of each station's task proposals plus no assignment. It maximizes summed utility under response and spatial exclusivity constraints. See `InterceptorSquadCoordinator.Visit`. |
| 15. What prevents duplicate goals? | Leases reject destinations within the four-metre exclusion radius of another active claim. Destroyed or invalid units release leases. |
| 16. What is adaptive memory? | Bounded time-decayed experience weighting. The evaluated model updates toward measured quality. Route failures have separate cooling records, and a new world clears the state. |
| 17. How is hidden-position cheating avoided in the prototype? | The policy receives a position only under confirmed visibility. Loss of vision uses authorized, time-stamped reports and regions. The offline comparison has its own fixture observations. |
| 18. What are the computational limits? | Small graph searches use a linear open-set scan and edge scan. Route probes, belief regions, squad candidates and pending evaluations are bounded. Physics and NavMesh generally cost more than score arithmetic. |
| 19. How was it tested? | Independent Dijkstra comparisons, movement and collision fixtures, repeated seeded scenes, sampled prediction traces, authentic Unity XML and profiled before/after route allocation. See the final evidence package. |
| 20. What is my individual contribution? | Agent Controller movement and validation, the Control section, and Interceptor intelligence and presentation are the assigned areas. The final work integrates and evaluates those contributions with the team's shared scene, navigation, damage and mission systems. |

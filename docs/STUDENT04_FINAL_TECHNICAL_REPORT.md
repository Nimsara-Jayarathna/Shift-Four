# Student 04 · Final technical report

## 1. Introduction

Shift-Four uses a shared Agent Controller for Guard, Scout, Flanker and Interceptor. The procedural prototype deploys four Interceptors with individual patrols and a shared response budget. This report documents the integrated implementation, its observed behavior and the final measurements. [Evidence package](evidence/student04-final/README.md)

## 2. Individual responsibilities

Student 04 owns the **Agent Controller** graphics and vision contribution, the **Control** section, and **Interceptor** intelligent behavior. The team responsibility record identifies these assignments. [Team responsibilities](TEAM_RESPONSIBILITIES.md#student-4---asmadala)

## 3. Agent Controller architecture

`DroneMotor` owns accepted navigation goals, waypoint progress, speed mode and stopping. A `NavMeshAgent` provides local movement. `DroneBody` tests the raised flight envelope against geometry and handles physical corrections and retry. The controller publishes accepted velocity. Brain policy, health, aesthetics and scoring remain distinct.

## 4. A* pathfinding

`WaypointGraph.FindRouteWithTrace` selects endpoint attachments, then searches with `F = G + H`. G adds Euclidean edge lengths and start connector cost. H is Euclidean distance to the attached goal or world endpoint. Open and closed arrays track search state. Relaxation records parents, which are followed backward, reversed and attached to the exact destination. Gate changes increment the version and remove closed edges from traversal. The final validation compares all 16 gate masks and 25 exact endpoint pairs against independent Dijkstra.

## 5. Route following and movement

The motor samples complete NavMesh segments, faces the current waypoint, updates movement mode and stops within the accepted arrival tolerance. Physical clearance can reject an otherwise complete ground mesh route. The regression fixtures cover control-room arcs, real actor and raised-beam obstruction, pressure recovery and all four drone types.

## 6. Drone animation

`DroneRotorVisuals` and `DroneAnimationPose` use accepted movement to express hover, braking, turns and speed. Renderer and rotor transforms are model-local. The animation report exposes the state and the tests check body envelope clearance, damage state and shutdown. The generated Interceptor has the premium airframe and model-local role markings.

## 7. Interceptor perception

`DroneBrain` performs vision through angular and physical line-of-sight checks. The generated response uses direct sightings, approximate player-origin sound and time-stamped Scout reports. `InterceptorBrain.Decide` masks the position supplied to the outdoor response when there is no confirmed visibility. Offensive authorization remains in the shared muzzle, range and cooldown path.

## 8. Adaptive motion estimator

`InterceptorMotionEstimator` filters incoming position data into velocity and acceleration. Abrupt stops, turns and reversals alter the estimate promptly. Stale history decays and physically implausible jumps reset the filter. Route-level estimates use real accepted drone movement to calculate travel feasibility.

## 9. Multi-hypothesis prediction

F1 generates reachable Continue, Left, Right, Stop, Reverse and Alternate hypotheses when the relevant motion and geometry support them. Candidates use route ETA, heading, confidence and stability. A local deadline guards against committing to expired interception predictions. [F1 report](STUDENT04_ADVANCED_INTERCEPTOR_AI.md)

## 10. Probabilistic belief state

`InterceptorBeliefState` normalizes a bounded set of weighted observations or regions. It moves probability along reachable paths with growing uncertainty. Fresh direct sight takes precedence over weak sound. Inspections reduce a region only when the sensor samples adequate coverage. An occluded region retains uncertainty. Search stops when its budget expires.

## 11. Utility AI

`InterceptorUtilityDecision` scores eligible actions using evidence, travel, uncertainty and stability. Eligible options include attack, observation, interception, last-known pursuit, sound investigation, belief search, repositioning, patrol and return. The commitment and switch margin limit churn. Existing combat gates independently confirm actual shot execution.

## 12. Cooperative coordination

`OutdoorInterceptorDispatch` owns the squad and shared source-tagged evidence. Independent sightings and Scout reports enter the same response budget. Each unit has a station ID and local estimate. The board preserves report age and expiry. Destroyed agents release their leases while previously valid observations remain available until expiry.

## 13. Assignment optimization

The squad enumerates the option of continuing without a proposal and up to six proposals per station. It maximizes summed utility subject to valid tasks, response slots and four-metre destination exclusivity. Hard invalidation removes stale route versions. The exhaustive bound is 7⁴ combinations before pruning, and the actual assignment tests compare with a known optimum. [F3 report](STUDENT04_INTERCEPTOR_PHASE_F3.md)

## 14. Adaptive tactical memory

F4 stores immutable forecasts, pairs them with time-compatible sightings, and exponentially weights observed error, reliability and timing. A missing observation yields an unevaluated forecast. A moving average updates the evaluated hypothesis toward quality with bounded reliability and decay toward a neutral prior. Blocked routes have a separate, versioned cooldown, and recent inspections can lower repeated search priority. Regeneration and restart reset this state. [F4 report](STUDENT04_INTERCEPTOR_PHASE_F4.md)

## 15. Control Hall

The generated Control building uses the existing operations desk, display and projection instrumentation with lockdown lighting and spatial audio. The three switches and final override continue to drive the mission and presentation transition. The test suite exercises these through the existing progression. [Control Hall implementation](STUDENT04_CONTROL_HALL_PHASE_D.md)

## 16. Identification

The shared HUD reads the correct agent type and owner. Clear aim and damage can reveal the appropriate identification or health display. Geometry occludes the display. The generated appearance and gameplay labels are supported by original rendered captures and a four-role cover regression.

## 17. Testing methodology

The final run uses public Unity Test Runner surfaces with controlled runtime placement where necessary. The A* oracle is an independent Dijkstra calculation. Motion records sample accepted displacement. The baseline project is a disposable archive of `eae136b7ecc3ef19677fdce55946f10cf30abd71`. Replay controls send identical timestamped sight inputs to each version. The replay measures policy and controlled motion rather than a player-controlled live match. [Methods and test results](evidence/student04-final/TEST_RESULTS.md)

## 18. Experimental results

The authenticated XML supplies test counts and exact assertion outcomes. The graph trace records expanded nodes, timing and selected waypoints. The prediction CSV retains individual sample error and source timestamps. The squad records fleet count, assignments, combinations, utility and conflicts from live procedural runs. [Evidence directory](evidence/student04-final/README.md)

## 19. Baseline comparison

Three modes share seeded movement, visibility and navigation inputs. The archived build supplies the original policy. The current policy runs with adaptation on and off. Scenario-dependent cooperation is tested separately on the full generated scene. The comparison distinguishes issued forecasts, matches and the simulated interception proxy. [Baseline comparison](evidence/student04-final/BASELINE_COMPARISON.csv) · [Metric definitions](evidence/student04-final/PERFORMANCE_RESULTS.md)

## 20. Performance

F5 reuses physics hit buffers, a NavMeshPath, its corners and route-building scratch space in tactical validation. Results still return an independent array. Full physics or corner buffers fall back to complete queries. The dashboard formats only the selected page, captures A* visits only on the controller page and records timers only on the performance page. Measured timings include recorder overhead and may overlap. [Performance measurements](evidence/student04-final/PERFORMANCE_RESULTS.md)

## 21. Technical limitations

A* guarantees the optimal route for the chosen graph attachment and Euclidean graph cost. Off-node attachment is local and bounded, so graph optimality does not guarantee globally optimal 3D physical travel. NavMesh assumes the remaining ground connectivity. Procedural cover and sight windows vary. Controlled replay sample sizes are small and forecast issuance differs across modes. Existing MainLab regressions are documented with archived reproduction. [Test details](evidence/student04-final/TEST_RESULTS.md)

## 22. Future improvements

Expand the trace corpus with human movement and off-node obstacle variants. Capture evaluation coverage across more scene seeds. Collect longer runs on target hardware. Investigate the inherited MainLab failures as a separately authorized task with its own game-rule checks.

## 23. Individual contribution

The contribution encompasses gate-aware path planning and usable agent motion, the Control Hall's gameplay presentation, and the Interceptor estimate, search, coordination, adaptation and presentation. Shared player movement, weapon controls, mission and extraction rules, navigation and other AI archetypes are team systems used by the contribution.

## 24. Exact changed files

- `Assets/Scripts/AI/Agents/ControlTacticalNavigation.cs`
- `Assets/Scripts/WorldGeneration/OutdoorInterceptorDiagnostics.cs`
- `Assets/Tests/EditMode/Movement/InterceptorFinalReplayEditModeTests.cs` and `.meta`
- `Assets/Tests/PlayMode/InterceptorFinalValidationPlayModeTests.cs` and `.meta`
- `Assets/Tests/PlayMode/Student4UnifiedPlayModeTests.cs`
- `Assets/Tests/PlayMode/Student4UnifiedMotionEvidencePlayModeTests.cs`
- `docs/STUDENT04_FINAL_TECHNICAL_REPORT.md`
- `docs/STUDENT04_VIVA_QUESTIONS.md`
- Evidence files tabulated in [the evidence inventory](evidence/student04-final/README.md) and listed individually in its manifest.

## 25. References

- [Team responsibilities](TEAM_RESPONSIBILITIES.md)
- [Agent Controller evaluation](STUDENT04_AGENT_CONTROLLER_PHASE_B.md)
- [Interceptor animation](STUDENT04_INTERCEPTOR_ANIMATION_PHASE_C.md)
- [Control Hall design](STUDENT04_CONTROL_HALL_PHASE_D.md)
- [Motion and prediction](STUDENT04_ADVANCED_INTERCEPTOR_AI.md)
- [Cooperative coordination](STUDENT04_INTERCEPTOR_PHASE_F3.md)
- [Adaptive tactical memory](STUDENT04_INTERCEPTOR_PHASE_F4.md)
- [Final evidence and reproducible tests](evidence/student04-final/README.md)

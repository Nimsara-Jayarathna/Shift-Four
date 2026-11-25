# Student 04 · Phase F3 · Cooperative Interceptors

**GV Agent Controller · Control section · IS Interceptor**

Phase F3 coordinates the four outdoor Interceptors in **OpenWorldPrototype**. Their generated patrol stations are Checkpoint, Central Avenue, Control and Storage. The station slot provides a stable ID from 01 to 04, independent of the current squad role.

## Architecture

In F2, each Interceptor applied its own F1 predictor and F2 belief and utility policy. A Scout alert selected one reachable responder, and direct sight and hearing could independently use a response slot. Units chose destinations independently. F3 adds one coordinator to the existing world dispatcher. Each drone still senses, proposes only its own valid destinations, chooses an authorized action and executes it through its existing motor. The squad uses proposals and recorded evidence as inputs. The coordinator has no player Transform reference.

The bounded blackboard holds up to **24** timestamped observations. A record has a sequential world-local ID, original sender and source, position, uncertainty, reliability, original timestamp and expiry. Valid sources are confirmed Interceptor sight, original Scout reports, the existing approximate player-sound sensor and sensor-covered inspection. Replays of the same timestamp and source are rejected. Direct reports from each observer are paced, and two sensors that hear one timed player emission share one entry. Recipients fuse reports into F2 belief with the preserved source and age. Only their own direct sightings update F1 motion history. Shared negative evidence reduces an older belief only when the inspected footprint covers it. Recipients do not publish received records again.

## Assignment and reservations

The dispatcher updates the matrix about every **0.5 seconds** and can refresh earlier after a reservation is released or a graph version invalidates it. A row is an available unit. An active row has up to **six** locally certified proposals for interception or search. The scores use existing F2 terms and the validated ETA. Recruitment uses the original report and clearance-checked approach routes. It compares age-discounted reliability and travel time, then picks at most one additional unit in that pass. Scout alerts can trigger that pass immediately, and the blackboard holds fresh queued evidence when the squad is full.

The solver enumerates one proposal or no assignment from each row. It maximizes the sum of proposal scores plus a **0.10** preference for a retained reservation. It rejects expired tasks, unavailable units, excess responders and destinations less than **4 m** apart in the horizontal plane. A validated assignment has a **0.8 s** minimum commitment. Missing or invalid proposals release that protection. Stable row IDs and proposal order resolve ties. With four rows of six choices plus a no-assignment branch, the upper bound is **7⁴ = 2,401** leaves before constraints. Each branch compares at most four destinations. Local route validation and recruitment probes can cost more than the combinatorial search.

A reservation records the assignment ID, unit ID, destination, creation time, expiry and graph version. Valid leases can renew with new route evidence. A changed graph, invalid proposal, arrival after inspection, combat takeover, return, destruction or temporary unavailability can release a lease. A new generated world creates its own coordinator. Selected batches retire obsolete leases before committing replacements. Local decisions can claim a clear destination between squad updates, which closes the gap where two agents might otherwise choose the same point.

Squad roles follow the live assignment and response state. Primary favors predictive interception and then compares utility. Secondary covers a complementary destination. Search identifies belief or sound assignments. Area Security and Reserve stay on their original patrols. Return guides a unit back to its own station. A fresh authorized report can justify recruiting an available unit after another is lost. Both individual and shared responses use the existing response cap of **two** and all shots still use the outdoor combat gate. If the dispatcher is disabled, the existing individual policy and response cap can continue.

## Controlled comparison

The F2 comparison independently takes the highest valid proposal from each unit. F3 receives the same two rows with the same feasible routes, scores and ETAs. A success in this fixture counts a unique region whose route ETA meets the **2.2 s** deadline. The comparison measures assignment behavior with certified fixture routes.

| Measure | F2 independent | F3 cooperative |
|---|---:|---:|
| Valid proposals chosen | 2 | 2 |
| Duplicate destinations | 1 | 0 |
| Unique regions assigned | 1 | 2 |
| Unique regions meeting the deadline | 1 | 2 |
| Mean route ETA | 1.45 s | 1.75 s |
| Invalid route selected | 0 | 0 |

F3 traded **0.30 s** of mean estimated travel for the second distinct region in this fixture. The selected regression and the generated-scene profiler results are below. Actual interception success rates, physical travel-time comparisons and a broad encounter benchmark require separate runs with ground truth, multiple maps and repeated player trajectories.

The selected Unity 6000.6.4f1 suites passed **119 Edit Mode tests** and **39 Play Mode tests**. The open-world suites exercised four-unit deployment, navmesh and collision, independent sight plus Scout dispatch into distinct simultaneous reservations, the shared response cap, graph-version invalidation, destruction, pause, regeneration, and extraction. The focused Edit Mode fixtures verified deterministic ties, optimal complementary search choices, unavailable-agent rejection, commitment, lease expiry and shared evidence deduplication. Two simultaneous belief-region searches and replacement flight after destruction still need a dedicated live encounter test.

The warmed optimizer solved the controlled matrix **1,000** times in **2.717 ms**, with **zero** managed allocation events counted after a deliberate allocation calibrated the recorder. It considered **eight** compatible combinations per solve. This measures the pure solver with prebuilt proposals. Route collection and recruitment are separate costs.

The generated-scene coordinator marker recorded **115 samples**, with **1.8 μs mean** and **10.5 μs peak**.

The generated-scene assignment solver marker recorded **4 samples**, with **2.6 μs mean** and **3.2 μs peak**.

## Unity and assessment demonstration

1. In Unity 6000.6.4f1 open **Assets/Scenes/OpenWorldPrototype.unity** and start the mission. Inspect **World/GeneratedWorld/OutdoorInterceptors** for the four stations. Repeat with seeds **1234**, **5678** and **9012**.
2. Press **F6** for the squad page. Approach Central Avenue in clear sight. The red or orange markers show response reservations and the yellow markers show observations and search assignments. The panel lists the chosen destinations and the current proposal matrix.
3. Move along the road to produce a predicted junction, then take cover. A Scout can provide another sighting. **Shift+F6** cycles the viewed unit and **Ctrl+F6** switches between squad and individual evidence. Compare the planned destinations with the routes the drones are following.
4. Destroy a responding Interceptor and watch its lease disappear. A surviving unit can accept a fresh reachable assignment if cooldown and capacity allow. Regenerate to verify that the old blackboard and leases are retired.
5. Run **InterceptorSquadEditModeTests** and **OutdoorInterceptorPlayModeTests** from **Window → General → Test Runner**. The tests exercise the same optimizer, generated agents, sensors and lifecycle. They include the preserved F1 and F2 regression cases.

## Changed files

- Assets/Scripts/AI/Agents/InterceptorSquadCoordinator.cs and its .meta
- Assets/Scripts/AI/Agents/InterceptorBeliefState.cs, InterceptorBrain.cs, InterceptorPolicy.cs, and InterceptorUtilityDecision.cs
- Assets/Scripts/WorldGeneration/OutdoorInterceptorDispatch.cs, OutdoorInterceptorResponse.cs, and OutdoorInterceptorDiagnostics.cs
- Assets/Tests/EditMode/Movement/InterceptorSquadEditModeTests.cs and its .meta
- Assets/Tests/PlayMode/OutdoorInterceptorPlayModeTests.cs
- docs/STUDENT04_INTERCEPTOR_PHASE_F3.md

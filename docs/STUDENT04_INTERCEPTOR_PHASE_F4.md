# Student 04 · Phase F4 · Interceptor tactical memory

## Runtime architecture

`OpenWorldPrototype` generates four Interceptors at Checkpoint Approach, Central Avenue, Control Exterior and Storage Approach. F1 supplies filtered position, velocity and acceleration, up to six validated motion hypotheses, motion-aware arrival times and junction selection. F2 ranks eligible actions and maintains bounded search beliefs. F3 allocates up to two response slots, reserves distinct destinations and publishes time-stamped evidence. `OutdoorInterceptorResponse` now owns one F4 learning instance per generated unit. Separate responses reuse that instance. Regeneration and mission restart create fresh instances with neutral model reliability.

The existing route validator and A* graph still certify routes and honor gates. `DroneMotor` exposes its existing blocking collider to distinguish physical scenery from traffic when sharing a failure. The actual Agent Controller still performs physical recovery. Existing response duration, pursuit limits, combat gates and execution budgets apply to every learned choice.

## Prediction evaluation

When the response executes an intercept selected by the local policy or claimed through the squad, it can capture one forecast per second. A pending snapshot preserves its ID, observation time, creation and expected times, predicted junction, selected hypothesis, confidence, drone ETA, arrival slack and graph version. The junction is the predicted position at the original player ETA. The snapshot is immutable.

The evaluator accepts a fresh, compatible direct sighting based on its original time and reliability. It considers the unit's own vision and other units' confirmed visual reports. It chooses the closest sighting within ±0.30 s of the expected arrival by default, preferring stronger evidence on a time tie. Evaluation waits until the window closes, or until the response ends. It uses the 3D distance from the original forecast to that sighting. Sound, Scout estimates, negative search evidence and blocked-route messages never resolve a position forecast. If nothing fits, it records `Unevaluated`. The evaluator removes each pending forecast once.

## Weight and horizon adaptation

For error `e` and forecast duration `h`, the defaults are:

```text
scale(h)       = 3 m × (1 + 0.14 × h)
timingFactor   = 1 − 0.35 × clamp01(|observedTime − expectedTime| / timingTolerance)
quality        = clamp01(exp(−e / scale(h)) × sightReliability × timingFactor)
agedReliability = 0.65 + (storedReliability − 0.65) × exp(−decayRate × age)
newReliability = clamp(lerp(agedReliability, quality, learningRate), 0.25, 0.95)
```

An evaluated error at or below the horizon-scaled distance is recorded as a successful prediction. Reliability is continuous, so an error near that threshold can lower reliability. The default learning rate is **0.12** and the default decay is **0.05/s**. The per-unit evidence cache deduplicates reliability updates for the same source, time and hypothesis. It can still record the errors of two distinct forecasts evaluated against the same sighting.

Raw F1 observation support remains separate from historical reliability and overall observation confidence. F4 multiplies each valid model's support by a factor bounded to **0.77–1.18**, then normalizes the available weights. A history bonus cannot make an unsupported path or hypothesis eligible. Rapid direction changes slightly increase branch support. Observed stops and reversals also use F1's immediate velocity correction.

A rolling motion estimate with a 2.4 s exponential time constant tracks speed, speed and acceleration variation, turns, stops and reversals. The horizon moves between **1 second** and the authored lead horizon, hard-capped at **5 seconds**. Observation age, uncertainty, recent error, confidence and the previous validated routes affect the target horizon. A deadband and slower upward recovery damp changes. An issued intercept keeps its F1 arrival checks and graph revalidation during visual loss. A shorter future horizon alone cannot cancel its commitment.

## Outcomes and recovery

| Observation or failure | Response |
| --- | --- |
| Matched prediction success or error | Record the actual result, update the evaluated model, and rebuild alternatives using subsequent evidence. |
| No compatible sighting | Record unevaluated. Reliability ages toward its prior. |
| Invalid destination, graph route failure, incomplete NavMesh route, physical obstruction | Record independently of prediction error. Cool the matching destination and approach signature for about three seconds. Routes with a different signature can compete. Retry passes through the current validator. Graph changes invalidate old cooldowns. |
| Missed arrival window | Record the window expiry separately. A newer sighting can supersede the timing failure. |
| Inspected region with coverage | Retain the F2 belief update and share the negative evidence. Reduce the rank of a recently inspected region if a new belief introduces it again. |
| Insufficient coverage | Preserve uncertainty and record the incomplete inspection. Search still expires on the existing schedule. A recent sighting can justify briefly holding position while blocked alternatives clear. |
| Stale observation | Follow declining observation confidence and return once response or search limits expire. |
| Assignment conflict | Keep F3's reservation check, record the rejection and select another eligible action. |
| Destruction | Close pending evaluations using available evidence and release the reservation. Confirmed observations already on the shared board retain their original expiration. |

Utility bonuses and repetition penalties apply to validated actions. The maximum search penalty is **0.18**. F2 commitment and switching margins still apply. The route signature quantizes the approach bearing and ordered internal waypoints. Route cooldowns and shared physical obstruction reports are scoped to the graph version. Shared reports retain their sender, observation time, expiry, reliability and route signature. The existing 24-item F3 board deduplicates publications and the receiver processes each publication once. A remote navigation failure affects only route selection. It cannot train movement hypotheses.

## Bounds and cost

Each unit reserves space for **64 tactical entries**, **24 pending forecasts**, **24 learned evidence keys** and **16 recent errors**. The F1 model count is unchanged. Tactical entries expire after 30 seconds by default and shared route reports expire in three seconds. A prediction step scans at most 24 pending evaluations per incoming sighting. Fixed-size history lookups and route cooldown checks are bounded by their respective capacities. Existing A* and NavMesh queries remain the larger cost. Tactical updates run on the response planning cadence with sparse pruning in patrol or return.

Profiler markers are `ShiftFour.InterceptorLearning`, `ShiftFour.PredictionEvaluation` and `ShiftFour.TacticalMemory`. The warmed capture/evaluation loop took **2.300 ms per 1,000 calls** for the instrumented F3 baseline and **2.500 ms** with F4, with **zero allocation events** in both loops after calibration. Across 256 retained learning instances, the managed heap increased by about **16,432 bytes per unit**. This estimates the learning object and its reserved buffers independently of route planning, diagnostics strings and Unity objects.

The generated Central Avenue Play Mode fixture captured 11 positive frames per marker. Learning averaged **29.15 µs** with a **189.38 µs** peak. Prediction evaluation averaged **27.96 µs** with a **188.25 µs** peak. Tactical memory averaged **0.20 µs** with a **0.75 µs** peak. The learning marker includes evaluator work, so those times overlap. The 11-frame sample covers one observed encounter.

## Repeatable F3 versus F4 comparison

`InterceptorLearningEditModeTests.HeldOutTraceComparison` runs 180 decisions per seed and mode. Both modes run the same fixed-seed stop, reversal, turn, speed-change and partial-visibility traces. F3 uses identical telemetry with `Outdoor Learning > Enabled` cleared. The replay uses deterministic direct-route geometry and a scripted flight follower. Seeds identify trace jitter here. Separate Play Mode tests load and regenerate the actual maps.

Distances below are metres. Qualified forecasts required measured error of at most 2 m and a drone ETA within the predicted window. This is a proxy for interception opportunity in the fixture.

| Seed | Mode | Evaluated / unevaluated | Mean absolute error | Median | RMSE | ETA-qualified |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1234 | F3 | 15 / 6 | 7.515 | 5.781 | 8.692 | 1/15 |
| 1234 | F4 | 18 / 4 | 5.656 | 4.300 | 7.216 | 3/18 |
| 5678 | F3 | 13 / 7 | 6.905 | 5.965 | 7.928 | 1/13 |
| 5678 | F4 | 20 / 4 | 5.240 | 4.335 | 6.136 | 4/20 |
| 9012 | F3 | 20 / 3 | 6.953 | 4.750 | 8.574 | 2/20 |
| 9012 | F4 | 16 / 5 | 6.016 | 4.443 | 7.538 | 2/16 |

| Seed | F3 early / late MAE | F4 early / late MAE | F3 MAE under 2 s / 2 s or longer | F4 MAE under 2 s / 2 s or longer |
| --- | ---: | ---: | ---: | ---: |
| 1234 | 5.584 / 9.204 | 4.047 / 6.944 | 3.716 / 8.099 | 2.731 / 6.492 |
| 5678 | 5.345 / 8.242 | 4.203 / 5.798 | 2.021 / 7.312 | 4.293 / 5.750 |
| 9012 | 5.947 / 7.624 | 3.349 / 8.091 | 3.639 / 7.321 | 3.387 / 6.892 |

The pooled mean absolute error was **7.12 m** for 48 F3 forecasts and **5.61 m** for 54 F4 forecasts. Forecast availability differs between modes, so this comparison includes selection effects. Most short-horizon samples came from F4. Arrival-window qualification increased from **4/48** to **9/54**. Losing the current intercept rose from **26 to 35** events. Candidate switches rose from **21 to 22**, with **12 versus 13** switches occurring within 1.2 s of the last selection. That timing metric includes valid handoffs and invalidations. The mean time from a failed to the next successful evaluation was **14.12 s across seven observed intervals** for F3 and **10.73 s across eight** for F4. The 9012 recovery interval mean increased from **7.52 s to 14.81 s**. Unresolved recoveries are excluded. These traces support a bounded adaptation benefit on forecast error with a tradeoff in intercept continuity.

The additional `HeldOutOffsetApproachComparison` used diagonal entries and offset turns after tuning was complete. MAE was nearly unchanged when these results were pooled. Seed 4523 worsened by 0.095 m, while both offset traces produced more ETA-qualified forecasts.

| Trace seed | F3 evaluated / unevaluated | F4 evaluated / unevaluated | F3 MAE / median / RMSE | F4 MAE / median / RMSE | F3 to F4 ETA-qualified |
| --- | ---: | ---: | ---: | ---: | ---: |
| 4523 | 13 / 4 | 15 / 2 | 3.744 / 3.119 / 4.178 | 3.839 / 2.828 / 4.558 | 0/13 → 3/15 |
| 8707 | 16 / 3 | 16 / 3 | 3.584 / 3.161 / 4.003 | 3.514 / 2.828 / 4.071 | 1/16 → 4/16 |

In the separate blocked-route fixture the same approach was chosen three more times by baseline and zero times by F4 over the following three decisions. The updated topology allowed it to compete again. The mixed traces issued the same 16 rejected route queries in total and had no repeated blocked endpoint queries. This distinguishes avoided movement attempts from planner cost. In the separate search utility fixture F3 selected the reintroduced region four times and F4 selected it zero times across four fresh decisions with a similarly scored alternative.

## Unity verification and demonstration

1. Open `Assets/Scenes/OpenWorldPrototype.unity` in Unity **6000.6.4f1**, press **Play**, then select **Play** on the title screen. The generated components configure themselves.
2. In the Hierarchy, select **World**. On **World Generator**, enter seed **1234** and use its component menu **Generate World** while playing. Inspect **World / GeneratedWorld / OutdoorInterceptors** for four stations. Repeat with **5678** and **9012**.
3. Approach **02 Central Avenue** along an open road. Use **F6** to open diagnostics, **Ctrl+F6** twice from the first page for **Adaptive Memory**, and **Shift+F6** to cycle units. Keep moving in sight long enough to see a pending junction forecast become evaluated. Compare support, adjusted weight, reliability, error and the last adaptation reason.
4. Repeat with short stops, a reversal or a side turn. Break line of sight briefly, then emerge before the response ends. The search should use observed or reported locations and display genuine reacquisition. If cover prevents a compatible sighting until after the forecast window closes, the unevaluated counter should increment.
5. For comparison, select the generated **Response Unit** at each station and clear **Interceptor Brain > Outdoor Learning > Enabled**. That reverts the adaptive score and route changes while retaining measurement. Reproduce the same visible approach. Re-enabling adaptation within the encounter resumes it from the retained memory. A scene restart or new generated world gives a clean comparison and restores the authored enabled default, so apply the comparison toggle after generation.
6. To demonstrate recovery and coordination, run the focused route, search and squad tests in **Window > General > Test Runner**. The Coordinator and Unit pages display valid reservations, navigation diagnostics, and the failed destination reported on the Adaptive page when a failure is recorded. For a live physical obstruction, use a temporary solid obstacle along a claimed route in Play Mode and inspect the motor's report and the route cooldown. Remove it and allow revalidation. Use the Profiler CPU view to inspect the three F4 markers.
7. Use **Pause > Restart Mission** or generate another seed and check that all four counters start at zero.

Unity 6000.6.4f1 passed **132 Edit Mode tests** and **40 Play Mode tests** in the selected regression suites. The adaptive Play Mode test checked fresh memory for all four units after regenerating seeds **1234**, **5678** and **9012** and after restarting the mission.

Automated Play Mode checks cover physical line-of-sight loss and reacquisition, issued forecasts, a stop, turns and a reversal, all four stations, separation and release of reservations, fixed-gate graph cases, motor collision recovery, world regeneration, and menu and HUD regressions. The trace study is a controlled policy replay. A wider sample of human movement, completed interceptions during ordinary gameplay, and simultaneous live physical obstruction at multiple squad stations remain external to the measurement.

## Files changed for F4

- `Assets/Scripts/AI/Agents/InterceptorLearning.cs` and `.meta`
- `Assets/Scripts/AI/Agents/InterceptorPredictionEvaluator.cs` and `.meta`
- `Assets/Scripts/AI/Agents/InterceptorTacticalMemory.cs` and `.meta`
- `Assets/Scripts/AI/Agents/InterceptorBeliefState.cs`
- `Assets/Scripts/AI/Agents/InterceptorBrain.cs`
- `Assets/Scripts/AI/Agents/InterceptorPolicy.cs`
- `Assets/Scripts/AI/Agents/InterceptorPredictionModel.cs`
- `Assets/Scripts/AI/Agents/InterceptorSquadCoordinator.cs`
- `Assets/Scripts/AI/Movement/DroneMotor.cs`
- `Assets/Scripts/WorldGeneration/OutdoorInterceptorDiagnostics.cs`
- `Assets/Scripts/WorldGeneration/OutdoorInterceptorResponse.cs`
- `Assets/Tests/EditMode/Movement/InterceptorLearningEditModeTests.cs` and `.meta`
- `Assets/Tests/PlayMode/OutdoorInterceptorPlayModeTests.cs`
- `docs/STUDENT04_INTERCEPTOR_PHASE_F4.md`

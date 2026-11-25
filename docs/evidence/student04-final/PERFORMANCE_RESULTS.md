# Phase F5 · Performance and comparison

The recorded environment was Unity **6000.6.4f1** on **Apple M1**, **8 GiB**, **macOS 27.0.1**, **Metal**, batch editor. The live prototype viewport was **640 × 480**. The archived baseline comes from commit `eae136b7ecc3ef19677fdce55946f10cf30abd71`. The selected local F1–F5 source is identified by hashes in `logs/source-sha256.txt`.

## Tactical route before and after

The same controlled route fixture ran serially against the archived validator and current validator. It warms 16 decisions, then performs **500 fresh decisions** with one same-frame cached repeat in each. The return data remains a usable two-corner route. The allocator recorder is calibrated with a deliberate array and records events on the test thread. The obstruction case separately verifies a full hit buffer still rejects external geometry and accepts the path once the obstacle is disabled.

| Validator | Allocation events | Mean inclusive decision | Peak |
| --- | ---: | ---: | ---: |
| Original | 5,000 | 5.705 µs | 44.700 µs |
| F5 | 2,000 | 4.056 µs | 51.200 µs |

The controlled sample shows six fewer allocation events per fresh decision and a lower mean. The measured peak was higher. Allocation counts describe the route fixture, which includes A* connector and result allocations. They are separate from whole-frame GC. [Raw comparison](logs/ROUTE_VALIDATION.csv)

## A* and accepted controller motion

All **400** authored endpoints and gate combinations matched independent Dijkstra. The traced queries expanded **626** nodes in total and averaged **3.585 µs** with a **125.4 µs** max, including reflection overhead and diagnostic tracing. One fully open N1→N5 case returns an ordered route costing **73.000 m** through five expanded nodes. Closing every gate for the same endpoints yields no path. A* compares G + H using a consistent Euclidean H. An exact-node no-path query may expand only its reachable component. The local connector stage is limited and can report no path even when a different global off-node attachment might work. [Graph data](logs/graph-400-queries.csv)

| Archetype | Configured patrol target | Measured peak | Arrival error | Completion |
| --- | ---: | ---: | ---: | ---: |
| Guard | 2.85 m/s | 2.85 m/s | 0.013 m | 4.450 s |
| Scout | 2.65 m/s | 2.65 m/s | 0.014 m | 4.633 s |
| Flanker | 3.45 m/s | 3.45 m/s | 0.014 m | 3.867 s |
| Interceptor | 3.25 m/s | 3.25 m/s | 0.013 m | 3.817 s |

All four observed turn and braking poses. The controlled fixed-step route measured 16.004 m/s² peak accepted acceleration against a 16 m/s² setting and 239.454°/s peak turn against 240°/s. Each logged one replan and travelled approximately 11 m. The separate 240-frame actor and raised-beam runs saw zero measured penetration and recovered after obstacle removal. Their frame-by-frame traces capture recovery timing at 60 Hz. [Motion CSV](logs/actual-motion-states.csv) · [Beam trace](logs/beam-collision.csv) · [Actor trace](logs/player-collision.csv)

## Three-mode replay

The same test source gave all modes a seeded 252-tick movement and visibility trace, the same graph points, blocker rules and initial position, and a scripted 5 m/s accepted-flight surrogate. It does not issue combat shots. The original runs the archived policy. Ablation runs current multi-hypothesis policy with F4 adaptation off. Advanced enables F4. This controlled policy experiment leaves live Utility AI and the squad coordinator to separate Play Mode fixtures.

Metrics recorded before comparison:

- **Coverage** is matched forecasts divided by issued forecasts. A match requires new sight within ±0.30 s of the expected time. Missing pairs remain unevaluated. **MAE**, **median** and **RMSE** use spatial error on matched rows.
- **Interception proxy** requires both the scripted player and scripted drone within 2 m of the forecast junction at the matched observation. **First interception** is elapsed simulated time to its first qualifying event. There is no combat encounter in this fixture.
- **Blocked route queries** and **repeated blocked endpoints** count rejection events in the fixture. **Search revisits** count moving back to a previously selected search destination after leaving it.
- **Replans** come from the policy. **Destination switches** count consecutive intercept destination changes. A switch within 1.2 s is a diagnostic frequency measure that can include necessary switches.
- **Decision timing** includes dispatch via the test delegate, candidate retrieval and capture where enabled. **Warm allocation events** come from a separate unboxed policy replay with the same 252 observations and a fixed start, after calibration. Those events exclude rendering, collider simulation and unrelated Unity updates.

| Mode | Matched / issued | Overall MAE | Median | RMSE | Interception proxy |
| --- | ---: | ---: | ---: | ---: | ---: |
| Original | 11 / 11 | 1.649 m | 1.978 m | 1.893 m | 7 / 11 |
| Current, F4 off | 17 / 17 | 1.121 m | 1.023 m | 1.268 m | 13 / 17 |
| Current, F4 on | 12 / 12 | 1.316 m | 1.245 m | 1.492 m | 10 / 12 |

For horizons below 2 s, the mean errors were **0.613 m** original (1 sample), **1.142 m** ablation (2) and **1.335 m** advanced (3). For horizons of at least 2 s they were **1.752 m** original (10), **1.118 m** ablation (15), and **1.310 m** advanced (9).

All seven movement patterns ran, but issued and matched forecasts in this three-way sample occurred in the junction segment. No forecasts expired unmatched within that subset. The original, ablation and advanced modes each revisited 13 search destinations with this surrogate. The rejected-route and destination-switch columns registered zero in this small run. Separate F4 fixtures exercise stop, reversal, loss, missed matches and repeated blocked approaches. The results here are consistent with lower mean error for the current policy relative to original. They do not support a further mean-error improvement from adaptation in this sample. [Per-seed results](BASELINE_COMPARISON.csv) · [Forecast rows](PREDICTION_RESULTS.csv) · [F4 measurements](../../STUDENT04_INTERCEPTOR_PHASE_F4.md)

## Live scene

The sampled outdoor scene had all **26** brains enabled and the four Interceptors configured. Each seeded workload had a responding central drone. Peak observed response count was **1** in the profiling harness, with no conflicting simultaneous claims. A separate Play Mode test validated two simultaneous responders, exclusive reservations and cleanup using real sight plus a Scout report. The runtime dashboard snapshot shows the configured counts, actual utility proposals, assigned ETA, role and lease. [Coordination data](COORDINATION_RESULTS.csv) · [Dashboard text](logs/dashboard-capture.txt)

The full sampled marker output includes CPU costs for A*, physical route validation, motion, body checks, animation, decision, assignment and prediction evaluation. Values are scene-wide inclusive costs for positive marker frames, and nested scopes overlap. The more expensive windows include candidate validation and recruitment. Shared body and routing work remain important costs on this hardware. The archived and integrated tests support repeatable tactical allocation counts. End-to-end indoor and outdoor frame budgets still need target-build profiling. [Outdoor marker results](logs/OUTDOOR_PROFILE.csv) · [Shared frame trace](logs/profile-frames.csv.gz) · [Shared profile context](logs/profile-context.txt)

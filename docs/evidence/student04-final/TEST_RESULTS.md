# Final Unity test results

Unity 6000.6.4f1, Metal, batch test runner. These are selected relevant suites from the project's wider inventory.

| Execution | Discovered | Executed | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: | ---: |
| Final integrated Edit Mode | 202 | 202 | 202 | 0 | 0 |
| Final integrated Play Mode | 140 | 140 | 129 | 11 | 0 |
| Focused final Play Mode with the sightline fixture fallback and idle recorder cleanup | 2 | 2 | 2 | 0 | 0 |
| Integrated pre-F5 Play Mode audit | 138 | 138 | 127 | 11 | 0 |
| Archived commit, legacy reproduction and old tactical validator | 20 | 20 | 9 | 11 | 0 |
| Archived commit, original-policy replay | 1 | 1 | 1 | 0 | 0 |

The authentic compressed XML in `test-results/` contains fixture names, duration, failures and traces. Use `gzip -dc test-results/final-play.xml.gz` to read the full Play Mode output.

## Verified

- Both F5 Play Mode fixtures passed. The prototype generated four active Interceptors and 26 drone brains in seeds 1234, 5678 and 9012. The fixture validates one diagnostics component, seven populated pages, and valid observed response counts.
- All six Outdoor Interceptor tests passed. They cover independent sight, Scout response, two distinct simultaneous reservations, blocked sight, sound-only search, observable motion adaptation, deployment clearance, visual events and regeneration.
- The seeded progression test invoked real mission interactions and reached the cinematic and victory screen using existing defeat requirements. All 12 Outdoor Mission Menu tests passed, including loss, pause, settings, map, missions and restart.
- All 13 Open World Collision tests, four Outdoor Weapon Handling tests, six HUD tests, nine Animation tests, and the Hall Audio test passed. The role-identification regression checks visibility through cover. The batch viewport was 640 × 480.
- The 400 exact-node A* cases matched independent Dijkstra. All four archetypes completed the controlled shared motor route with observable turn, braking and arrival. Physical actor and raised-beam recovery both completed without collision penetration.
- F1–F4 Edit Mode checks covered time matching, confidence, normalization, expiry, duplicate evidence, memory bounds, search eligibility and known-optimum squad assignments. The F4 Play Mode fixture exercised observable reacquisition and world-state reset.

## Archived failures

The 11 broad Play Mode failures matched the pre-F5 audit and archived `eae136b7ecc3ef19677fdce55946f10cf30abd71` run by test and assertion.

| Fixture | Count | Observed failure |
| --- | ---: | --- |
| `ControlTacticalNavigationPlayModeTests` | 1 | Junction fixture expects 5.40 m/s. The loaded profile reports 4.65 m/s. |
| `DroneMovementSpeedsPlayModeTests` | 4 | Three 30/60/120 Hz traces expect over 5.80 m/s and reach about 5.05 m/s. The straight fixture expects 4.20 m/s and measures about 3.45 m/s. |
| `InterceptorSensorIntegrationPlayModeTests` | 5 | Four assert older speed limits. The fifth retains eight historic observations after expecting the legacy MainLab history to expire. Earlier cover and no-fresh-sighting assertions had passed before that point. |
| `DroneDestructionPlayModeTests` | 1 | The old full MainLab rendered combat sequence rejects a test player shot in `PlayerShot`. Six other destruction cases and the outdoor Interceptor shutdown fixture passed. See the full stack trace in the XML. |

During development the tactical overlap fixture was aligned with its measured flight envelope. A normal frame yield also made camera readback work in batch. The focused final scene run includes a NavMesh sightline fallback if no authored junction is visible. The fallback still requires the real vision query to accept the player's test position.

## Manual follow-up

The progression run uses scripted interactions and controlled damage. A continuous human mission, live simultaneous physical failures at multiple squad stations, final dashboard layout at other resolutions, and complete interception rates during regular gameplay need interactive coverage. [Demonstration guide](DEMONSTRATION_GUIDE.md)

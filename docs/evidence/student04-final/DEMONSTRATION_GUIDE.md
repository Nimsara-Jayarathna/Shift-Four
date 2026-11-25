# Student 04 · Live demonstration

Use Unity **6000.6.4f1**. Open `Assets/Scenes/OpenWorldPrototype.unity`, enter Play Mode and start the mission from the title screen. Use **World → World Generator → Generate World** after setting seed **1234** for the primary demonstration. Repeat deployment for **5678** and **9012**. The generated parent is `World/GeneratedWorld/OutdoorInterceptors`.

**F6** opens the existing overlay. **Ctrl+F6** or **Command+F6** cycles its seven pages. **Shift+F6** pins the next unit. **Page Up / Page Down** scrolls long lists.

| Demonstration | Reproduction |
| --- | --- |
| A* and reconstruction | From Overview, advance four pages to **Agent Controller**. Pin the central unit and wait for a new route. Read the waypoint index and latest traced G/H/F values. In Test Runner execute `Student4UnifiedPlayModeTests.All400GateEndpointQueries_MatchIndependentDijkstra` for exact graph nodes and all gate masks. |
| Actual motion | Watch an enabled drone turn onto a road and slow toward a post on the controller page. The controlled `RenderedAllFour_ActualMotorTurningAndBraking` fixture records all four archetypes on one route and checks that the motor reaches its destination. |
| Predictive interception | Approach Central Avenue on an open road. Move steadily along it. Open **Prediction** to compare observed velocity, hypotheses, destination and ETAs. |
| Error and recovery | Move steadily until a forecast is pending, then turn while still visible. On **Adaptive Memory**, wait for a matched error. Move to a different reachable route and compare new evidence. A lost sighting may yield an unevaluated forecast instead. |
| Belief search | Step behind a solid building after a confirmed sighting. On **Utility and Belief**, inspect eligible actions and region probabilities. Stay hidden until the response expires, then repeat and re-emerge during the search to demonstrate reacquisition. |
| Squad response | Open **Coordination** with all patrols enabled. Let a Scout spot you while an Interceptor already responds. Compare accepted proposals and reservations. The controlled `DirectSightStartsPredictionWithoutScoutAndSolidCoverBlocksDetection` test stages one direct sighting and one Scout alert and checks both slots and four-metre exclusion. |
| Adaptive evaluation | Compare **Prediction** and **Adaptive Memory** after a time-matched outcome. For a comparison run, clear **Outdoor Learning → Enabled** on the four generated brains after generation. Restart or regenerate before beginning a clean run. |
| Animation and destruction | Observe the generation-time labels while aiming in clear sight, then damage a visible Interceptor with the issued gun. Use cover to check occlusion. See the station captures for operation, confirmed response, damage and disabled aftermath. |
| Control Hall | Complete Checkpoint and Server, unlock Control, then activate the three switches and the override. Inspect the operating desk displays, emergency lights, mission progress and sound transition. The ordered mission integration fixture drives real interactions and extraction with controlled placement and damage. |

For a capture run, set `SHIFTFOUR_INTERCEPTOR_CAPTURE` to an empty output directory and run `OutdoorInterceptorPlayModeTests`. This hooks a temporary Unity camera into the generated world. It produces PPM close-ups with scene rendering rather than painted overlays. Set `SHIFTFOUR_F5_EVIDENCE` for the final fixture's CSV data, selected dashboard text and player-camera render. Set `SHIFTFOUR_EVIDENCE` for the existing Agent Controller motion, recovery and A* fixtures. Batch capture uses Metal and requires the graphics device.

To generate the archived baseline, export `eae136b7ecc3ef19677fdce55946f10cf30abd71` with `git archive` to a disposable project and copy the final replay and tactical-validation test fixtures into its test directories. Execute `InterceptorFinalReplayEditModeTests` and the `TacticalValidatorAllocationsAndSaturatedClearance` Play Mode test. Point `SHIFTFOUR_F5_EVIDENCE` at a separate location for each project. The CSV header is identical across modes.

The included Interceptor close-ups are from the real generated standby, response, damage and disabled states. The outdoor player view is the camera render from the seeded profile fixture. The MainLab image comes from the existing rendered prediction fixture. Control Hall lockdown and restored renders were captured for Phase D and are available with their original results. [Control Hall captures](../control-phase-d/)

# Student 04 · Interceptor flight presentation

**GV role** Agent Controller · **Section** Control · **IS agent** Interceptor

## Contribution

The original FBX, alternating rotors, gimballed weapon, accepted-shot effects, flight integrator and physical wreck are team systems. The prediction policy and accepted motion come from Phases A and B. Phase C adds graphite shoulder armor, bevelled steel louvres, four identification stripes per shoulder, illuminated fan shrouds and a separately articulated optical head. Five collider-free mesh batches use a shared URP Lit material. Their cached renderers use property blocks for the lens, armor impact flash and propulsion illumination.

The normal Interceptor prefab constructs this kit in DroneRotorVisuals.Awake. OutdoorInterceptorDispatch spawns the actual response units and WorldGenerator.ConfigureDroneCombat applies their existing outdoor flight role. Expand a response station and find **Interceptor IV airframe** under the unit’s model. The original visual owner animates the kit after physical movement. Its wreck builder excludes the new decorative meshes when building collision shapes.

## Motion and decisions

Accepted horizontal velocity comes from DroneMotor. The model owner measures signed yaw rate and differences successive velocity samples in model space. The existing pose blends pitch and bank toward their targets with weight `1 − exp(−9 Δt)`. Forward acceleration pitches the nose down, braking lifts it, and signed turn rate and lateral acceleration drive banking. The final combined outdoor pose is limited to 14°. Phase C scales the existing hover by `1 − 0.85 × clamp01(speed/referenceSpeed)`, reducing the full-speed excursion from 20 mm to 3 mm. The emissive engine rings follow the smoothed rotor-speed signal. Distance throttling and pause apply to the new animation.

```mermaid
stateDiagram-v2
    Standby --> Alerted: Scout report
    Alerted --> Intercept: validated route
    Intercept --> Combat: attack decision
    Combat --> Intercept: reposition
    Intercept --> Search: lost sight
    Search --> Return: search expires
    Return --> Standby: arrive
    Standby --> Investigate: indoor observation or sound
    Investigate --> Intercept: policy selects intercept
    Combat --> Critical: fatal damage from any live state
    Critical --> Falling
    Falling --> Impact
    Impact --> Disabled
```

Standby and return use cyan. A dispatch or combat turns the sensor red. Investigation, interception, repositioning and search use amber. The optical head sweeps ±12° in standby. Search adds a ±18° sweep around the last recorded area. Interception follows an accepted waypoint and combat can focus on the brain’s authorized visible aim point. The indoor mapping reads observation, prediction, sound investigation and return from the existing policy. Sensor yaw and color approach new targets exponentially.

The accepted ShotFired event continues to trigger the original flash, tracer and receiver recoil. The existing combat HUD places sparks at the real hitscan contact. Accepted nonfatal damage briefly warms the new armor and lens as the pose stabilizes. Fatal damage cancels firing, reuses the spark renderer for failure and starts the existing fall and rotor spin-down. First contact produces sparks and a low relay sound. Final settling turns the new lights fully dark. The existing flight loop shifts between 0.91× and 1.13× pitch with speed. Dispatch, failure and contact share GameplayAudio’s spatial voice pool and settings. The alert and contact sounds reuse hall advisory and relay recordings. Dedicated Interceptor motor and crash recordings are absent.

## Unity and assessment demonstration

1. Open Assets/Scenes/OpenWorldPrototype.unity in Unity 6000.6.4f1, enter Play and start the mission. In the Editor or a Development Build, press **F6** and **Shift+F6** to inspect and cycle the actual responders. Frame the unit with **F** in Scene view to see its cyan lens scanning independently.
2. Let a Scout detect the player outdoors. Follow the dispatched unit with F6 in automatic mode. Move laterally, then take cover. Correlate lens direction and color with the decision, speed, signed yaw rate, forward acceleration, pitch, bank and rotor RPM. Wait for search and return.
3. Enter clear firing range, observe the aligned tracer and shoot once for the amber hit reaction. Deliver lethal damage and follow the same wreck to contact and shutdown. Pause and resume. Compare graphics presets and audio balance in the human-played Game view.
4. For a **controlled** demonstration, run DroneAnimationEditModeTests under Window → General → Test Runner for deterministic acceleration, turns, braking and shutdown. Run OutdoorInterceptorPlayModeTests for generated response stations, genuine dispatch, pause and controlled damage. Set SHIFTFOUR_INTERCEPTOR_CAPTURE to a writable folder before starting the Editor to reproduce the close-ups. The temporary capture camera exists only in the test. [Standby](evidence/interceptor-phase-c/standby.png) · [Dispatch](evidence/interceptor-phase-c/intercept.png) · [Damage](evidence/interceptor-phase-c/hit.png) · [Shutdown](evidence/interceptor-phase-c/shutdown.png)

## Current outdoor deployment

Open `Assets/Scenes/OpenWorldPrototype.unity` in Unity 6000.6.4f1, enter Play Mode, and start the mission. Look under `World/GeneratedWorld/OutdoorInterceptors` for the four stations.

| Unit | Station | Assignment |
|---|---|---|
| 01 | `01 Checkpoint Approach` | Patrols the Checkpoint exterior access road. |
| 02 | `02 Central Avenue` | Patrols the avenue between the Server and Control junctions. During pursuit, it uses observed player motion to choose a reachable junction ahead. |
| 03 | `03 Control Exterior` | Patrols the Control Hall exterior access road. |
| 04 | `04 Storage Approach` | Patrols the Storage Hall approach and can respond to Scout reports elsewhere. |

Each station builds four posts from the generated roads. The generator checks every patrol leg against outdoor NavMesh boundaries and sweeps the drone's flight envelope for clearance. Regeneration replaces the units and their station graphs. The prototype starts with **26 drones** across the four archetypes.

The World Generator exposes a population of **4**, a shared limit of **2 active Interceptor responses**, a **4-second** response cooldown, a **40-second** pursuit window, a **140 m** leash from the assigned station, and a **35 m** sight range. Interceptors can engage on direct sight or a Scout report. Only confirmed visual observations add to prediction history. The two response sources share the same slots, and shots still use the global outdoor firing limit.

To inspect a patrol, press **F6** for diagnostics and **Shift+F6** to cycle units. Approach the central avenue for direct detection, move along a road to give the policy a series of sightings, then take solid cover to observe lost-contact behavior. To check Scout dispatch, let a Scout see the player from a road and watch for **INTERCEPTOR RESPONDING**. Use **World Generator → Generate World** with seeds **1234**, **5678**, and **9012** to repeat the checks.

### Deployment verification

Unity 6000.6.4f1 passed **38 Play Mode tests** and **89 Edit Mode tests** for this update. Seeds 1234, 5678 and 9012 each produced four distinct stations and valid four-post loops. All four units crossed their first leg, and regeneration retired the old units, station graphs and flight effects. A separate Play Mode fixture verified direct sight beyond 25 m, cover occlusion, selection of a forward road junction from actual player movement, Scout dispatch into the second response slot and rejection of a third reservation. The run also covered health-bar visibility, drone destruction, flight visuals, the 400-query waypoint graph comparison and all three seeded extraction scenarios.

## Original Phase C verification and limitations

Across the selected suites, **15/15 distinct Edit Mode cases and 69/79 distinct Play Mode cases passed**, counting the latest result for each case. Both new or extended outdoor tests and the warmed allocation benchmark passed together on the final code. Tests verified generated geometry on three units per world, clearance at the 14° outdoor limit, regeneration on seeds 1234 and 5678, response to real Scout reports, accepted damage, pause, single death and zero final lens emission. Extraction passed on seeds 1234, 5678 and 9012. [Per-test results and comparison](evidence/interceptor-phase-c/results.csv)

The warmed Editor benchmark measured **18.695 ms for 4,000 mixed-role animation ticks**, with zero detected managed allocation events and one calibration event. This is a CPU fixture measurement. Standalone GPU performance, human audio balance and artistic review across Low, Balanced and High remain unverified.

Four speed tests and five sensing tests had the same failures before Phase C, including earlier speed expectations and a hidden-target observation count. The legacy MainLab rendered montage stopped at its Flanker player-shot assertion on both the candidate and a pre-Phase-C comparison run. The independent outdoor capture fixture produced all four images.

## Exact Phase C files

| Directory | Files |
|---|---|
| Assets/Scripts/Gameplay | InterceptorAirframe.cs, InterceptorAirframe.cs.meta, DroneAnimationPose.cs, DroneRotorVisuals.cs, DroneWreckMotion.cs, GameplayAudio.cs |
| Assets/Scripts/AI/Agents | InterceptorBrain.cs |
| Assets/Scripts/WorldGeneration | OutdoorInterceptorResponse.cs, OutdoorInterceptorDiagnostics.cs |
| Assets/Tests/PlayMode | OutdoorInterceptorPlayModeTests.cs |
| docs | STUDENT04_INTERCEPTOR_ANIMATION_PHASE_C.md |
| docs/evidence/interceptor-phase-c | standby.png, intercept.png, hit.png, shutdown.png, results.csv |

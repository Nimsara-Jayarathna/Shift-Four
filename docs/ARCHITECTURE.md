# What the starter code does

## A web developer's mental model

| Familiar idea | Here |
| --- | --- |
| App page | Unity scene, `MainLab.unity` |
| Reusable UI component | Prefab, e.g. a section, drone, or console |
| Business logic | C# `MonoBehaviour` components on game objects |
| Application state | `GameSession` for progress and win/loss |
| Event | `SoundEvents.ShotFired`, `Health.Died`, door change |
| Shared service | `WaypointGraph.FindRoute()` for A* |

Unity calls `Awake`, `Start`, `Update` and physics callbacks at different times. Do not call another script's `Start` manually. The greybox generator creates ordinary scene objects and assets once; gameplay runs from the scene after that.

## Runtime flow

1. `PlayerController` reads input, moves its `CharacterController`, raycasts to shoot, and asks a nearby `IInteractable` object to respond to E.
2. `Health` handles damage and the player's delayed regeneration. `GameSession` tracks consoles, drone deaths, exit, and restart.
3. Each `DroneBrain` samples visibility at a short interval. Its subclass (`ScoutBrain`, `FlankerBrain`, `GuardBrain`, or `InterceptorBrain`) returns a **named destination with a score**. The four subclasses are separate assessed IS contributions.
4. `WaypointGraph.FindRoute` selects room waypoints with A*: `f = g + h`, where `g` is distance travelled and `h` is straight-line distance to the goal. When the door closes, the Checkpoint–Storage graph edge is unavailable; the three other connections still offer a route.
5. `DroneMotor` follows the ordered room points with Unity's `NavMeshAgent`, turns smoothly and gives the visual a simple hover. `LabDoor` moves its kinematic rigidbody, changes the carved obstacle, and updates the graph version. On a version change the motor recalculates its route.
6. Each drone's shot checks the ray again at firing time. A solid wall, shelf, or pillar intercepts it; that is why the player can take cover and later recover.

The A* graph chooses a **high-level sequence of sections**; NavMeshAgent moves locally around geometry within those sections. Do not replace each member's distinct decision logic with one shared `if (distance < x)` script. Do not calculate an expensive path every rendered frame.

## Editable assets and ownership

- `Assets/Scenes/MainLab.unity`: overall assembly, walls, graph, door, player, NavMesh surface. Member 1 integrates; coordinate shared edits.
- `Assets/Prefabs/Sections/<Section>.prefab`: each room's floor, cover, console. One owner per prefab.
- `Assets/Prefabs/Agents/<Agent>.prefab`: one owner per drone and its visual changes; model importer coordinates shared artwork.
- `Assets/Scripts/AI/Agents/`: one policy file per student.
- `Assets/Scripts/AI/Core/`: shared perception/A* contract. Assign an owner and reviewer for changes.
- `Assets/Scripts/AI/Movement/`: shared movement and animation, led by Member 4.
- `Assets/Scripts/Gameplay/`: player, door, console, health, exit, led by Member 2.
- `ArtSource/`: original editable `.blend` source kept by Member 3; exported Unity models go in `Assets/Models/` after import.

This baseline intentionally uses placeholder cubes. Students replace or develop them through their own assessable contributions. The small graph is easy to explain but still needs route demonstrations, tests and tuning in the actual game.

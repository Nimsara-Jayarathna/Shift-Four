# Member 2 — Systems Engineer and Flanker

**Your IS assessment:** the Flanker's distinct goal selection. **Your GV assessment:** player movement/collision, shooting, and environmental interaction with a working door. You own the Storage section, player health/recovery, and reusable console behaviour.

## Start from the baseline

Open `Assets/Prefabs/Sections/Storage.prefab`, `Assets/Scripts/AI/Agents/FlankerBrain.cs`, and `Assets/Scripts/Gameplay/`. The starter uses `CharacterController`, raycast shooting, `Health`, `ConsoleSwitch`, and a kinematic `Rigidbody` sliding door. The cover is fixed; keep it fixed. The door and player already have colliders so the interactions have physical consequences.

## Work in this order

1. Arrange Storage shelves so **two usable approach routes** and fixed hiding places remain. Test the player can walk around shelves and the NavMeshAgent can reach both flanking sides. Coordinate with Member 1 before main-scene changes or a rebake.
2. Test movement, aim, fire, damage and recovery. The default is 100 player health, 10 damage per drone hit, three seconds without damage before recovery at eight health per second. Tune these values only to improve playability and keep them documented.
3. Open and close the shortcut door while the player and agents are nearby. Check its collider blocks the player when closed, its visual actually moves aside, and the route changes without trapping agents. The `LabDoor` code uses `Rigidbody.MovePosition` in `FixedUpdate`; be ready to explain that controlled physical movement.
4. In `FlankerBrain.cs`, improve how it evaluates two sides: lateral angle, route cost, reachability and a fallback if a side is blocked. The player should visibly encounter a side approach; do not merely change the Scout's colour or patrol points.
5. Verify consoles cannot increment progress twice and the game cannot declare victory before four consoles and four disabled drones. Verify death and restart.

## Evidence to produce

Record a short cover/health demonstration and a door route-change example. Show candidate flank scores and why one won. Commit in small pieces such as `feat(player): add damage and recovery`, `feat(door): replan after shortcut closes`, `feat(ai): score flanking approaches`.

**Done when:** movement/shooting/interactions feel reliable; cover actually blocks shots; health recovery is readable; the door affects a valid route; Flanker chooses and changes side approaches without getting stuck.

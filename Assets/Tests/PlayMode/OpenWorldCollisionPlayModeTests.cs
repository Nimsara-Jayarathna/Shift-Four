#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShiftFour.Tests
{
    public sealed class OpenWorldCollisionPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/OpenWorldPrototype.unity";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] Sites = { "Checkpoint", "Server", "Control", "Storage" };
        private Component generator;
        private Component player;
        private CharacterController capsule;
        private Camera camera;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            generator = Find("WorldGenerator").Single();
            player = Find("PlayerController").Single();
            capsule = player.GetComponent<CharacterController>();
            camera = player.GetComponentInChildren<Camera>();
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Scene current = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("After open world collision test"));
            if (current.path == ScenePath) yield return SceneManager.UnloadSceneAsync(current);
        }

        [UnityTest]
        public IEnumerator FacadesStopTheCapsuleAtVisibleSurfacesAcrossSeeds()
        {
            Transform previousFacade = null;
            foreach (int seed in new[] { 1234, 5678, 9012, 4, 1234 })
            {
                Generate(seed);
                yield return null;
                Assert.That(previousFacade == null, Is.True, "Regeneration removed the previous facade volumes.");
                Assert.That(Find("OutdoorScout").Length, Is.EqualTo(7), $"Seed {seed} keeps all Scout routes.");

                var checkpoint = World.Find("Buildings/Checkpoint");
                var exterior = checkpoint.Find("ExteriorDetails");
                var facade = exterior.Find("Facade collision");
                Assert.That(facade, Is.Not.Null);
                Assert.That(exterior.Cast<Transform>().Count(child => child.name == "Facade collision"), Is.EqualTo(1));
                previousFacade = facade;

                var east = exterior.Find("Armored shell/East armor and glazing").GetComponent<Renderer>();
                var rib = facade.Find("East armor").GetComponentsInChildren<BoxCollider>()
                    .Single(box => box.name == "Rib facing" && Mathf.Abs(box.transform.localPosition.x) < .01f);
                Assert.That(east.bounds.max.x - rib.bounds.max.x, Is.InRange(0f, .06f), "Rib collider meets the actual FBX silhouette.");

                Across(checkpoint, new Vector3(13.02f, 0f, 0f), Vector3.right, 0f, "Checkpoint east rib");
                Across(checkpoint, new Vector3(-13.02f, 0f, 0f), Vector3.left, 0f, "Checkpoint west rib");
                Across(checkpoint, new Vector3(0f, 0f, 13.02f), Vector3.forward, 0f, "Checkpoint rear rib");
                foreach (float angle in new[] { -35f, 0f, 35f })
                {
                    Across(checkpoint, new Vector3(12.33f, 0f, -9f), Vector3.right, angle, "Checkpoint side armor panel");
                    Across(checkpoint, new Vector3(3f, 0f, 12.33f), Vector3.forward, angle, "Checkpoint rear armor panel");
                    Across(checkpoint, new Vector3(9.15f, 0f, -13.405f), Vector3.back, angle, "Checkpoint forecourt barrier");
                }
                Across(checkpoint, new Vector3(-2.73f, 0f, -13.28f), Vector3.back, 0f, "Checkpoint west entrance jamb");

                CheckFront("Server", "Entrance/Reinforced jamb", -2.69f);
                CheckFront("Control", "Main entrance/Facade security barrier/Armored barrier foot", -8.35f);
                CheckFront("Storage", "Warehouse entrance/Portal upright", -5.73f);
            }

            void CheckFront(string site, string part, float x)
            {
                Transform building = World.Find("Buildings/" + site);
                Transform piece = building.Find("ExteriorDetails/" + part);
                Assert.That(piece, Is.Not.Null, $"{site}/{part}");
                var solid = piece.GetComponent<BoxCollider>();
                AssertSolid(solid);
                float face = building.InverseTransformPoint(solid.bounds.min).z;
                Across(building, new Vector3(x, 0f, face), Vector3.back, 0f, site + " " + solid.name);
            }

            void Across(Transform building, Vector3 localSurface, Vector3 localOutward, float angle, string label)
            {
                Vector3 outward = building.TransformDirection(localOutward);
                Vector3 travel = Quaternion.Euler(0f, angle, 0f) * -outward;
                Vector3 surface = building.TransformPoint(localSurface);
                Place(surface - travel * 1.6f + Vector3.up * .12f);
                Walk(travel, 1.85f);
                float distance = Vector3.Dot(player.transform.position - surface, outward);
                Assert.That(distance, Is.InRange(.26f, .58f), $"{label} seed {generator.GetType().GetProperty("GeneratedSeed").GetValue(generator)}, angle {angle}.");

                camera.transform.rotation = Quaternion.LookRotation(-outward) * Quaternion.Euler(-75f, 0f, 0f);
                Call(player, "ApplyCameraPose");
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 cornerPoint = camera.ViewportToWorldPoint(new Vector3(corner & 1, corner >> 1, camera.nearClipPlane));
                    Assert.That(Vector3.Dot(cornerPoint - surface, outward), Is.GreaterThan(0f), label + " near-plane clearance");
                }
            }
        }

        [UnityTest]
        public IEnumerator CheckpointPipesAndRailsStopTheCapsuleAndKeepTheWallWalkable()
        {
            Transform previousFacade = null;
            foreach (int seed in new[] { 1234, 5678 })
            {
                Generate(seed);
                yield return null;
                Assert.That(previousFacade == null, Is.True, "Regeneration removed the pipe and rail colliders with the facade.");
                var checkpoint = World.Find("Buildings/Checkpoint");
                var facade = checkpoint.Find("ExteriorDetails/Facade collision");
                previousFacade = facade;
                Assert.That(facade.GetComponentsInChildren<CapsuleCollider>().Length, Is.EqualTo(18));

                foreach (float side in new[] { -1f, 1f })
                {
                    string wing = side < 0f ? "West" : "East";
                    Vector3 outward = Vector3.right * side;
                    var manifold = facade.Find(wing + " armor/Utility manifold pipes");
                    VerifyBar(manifold.Find("Lower coolant pipe/Straight run"), 14.8f, .14f, outward);
                    VerifyBar(manifold.Find("Upper coolant pipe/Straight run"), 14.8f, .105f, outward);
                    foreach (float angle in new[] { -40f, 0f, 40f })
                    {
                        Vector3 approach = Quaternion.Euler(0f, angle, 0f) * -outward;
                        Vector3 surface = new Vector3(side * 12.8f, .16f, side * 3f);
                        Place(checkpoint.TransformPoint(surface - approach * 1.8f));
                        Walk(checkpoint.TransformDirection(approach), 2.05f);
                        float clearance = side * checkpoint.InverseTransformPoint(player.transform.position).x - 12.8f;
                        Assert.That(clearance, Is.InRange(.28f, .45f), $"{wing} pipes on seed {seed}, angle {angle}");
                    }

                    Vector3 feet = checkpoint.InverseTransformPoint(player.transform.position);
                    Walk(checkpoint.TransformDirection(Vector3.forward * side), 1.7f);
                    Vector3 after = checkpoint.InverseTransformPoint(player.transform.position);
                    Assert.That(side * (after.z - feet.z), Is.GreaterThan(1.6f), "The player can slide along the pipe.");
                    Assert.That(side * after.x - 12.8f, Is.InRange(.28f, .45f), "Sliding does not enter the collision volume.");

                    Transform rails = facade.Find($"Front armor and forecourt/{wing}/Forecourt guard rails");
                    foreach (Transform rail in rails) VerifyBar(rail, 3.68f, .055f, Vector3.back);
                    // Sample a jump-height pose above the separate concrete base. Otherwise the
                    // base would stop the capsule before this test reached the horizontal rails.
                    Place(checkpoint.TransformPoint(new Vector3(side * 9.15f, .94f, -15.1f)));
                    for (int i = 0; i < 65; i++) capsule.Move(checkpoint.forward * .035f);
                    float railClearance = -13.035f - checkpoint.InverseTransformPoint(player.transform.position).z;
                    Assert.That(railClearance, Is.InRange(.28f, .45f), $"{wing} forecourt rails at jump height");
                }

                void VerifyBar(Transform part, float length, float radius, Vector3 localOutward)
                {
                    Assert.That(part, Is.Not.Null);
                    var solid = part.GetComponent<CapsuleCollider>();
                    AssertSolid(solid);
                    Assert.That(Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, solid.gameObject.layer), Is.False);
                    Assert.That(solid.height, Is.EqualTo(length).Within(.002f));
                    Assert.That(solid.radius, Is.EqualTo(radius).Within(.001f));
                    Vector3 outward = checkpoint.TransformDirection(localOutward);
                    var ray = new Ray(part.position + outward, -outward);
                    Assert.That(solid.Raycast(ray, out RaycastHit hit, 1.5f), Is.True);
                    Assert.That(hit.distance, Is.EqualTo(1f - radius).Within(.002f));
                }
            }
        }

        [UnityTest]
        public IEnumerator SeededObstaclesAndSecurityDoors_BlockAndClearThePlayerCapsule()
        {
            foreach (int seed in new[] { 1234, 5678, 9012 })
            {
                Transform retired = World;
                Generate(seed);
                Assert.That(retired.gameObject.activeInHierarchy, Is.False);
                yield return null;
                Assert.That(retired == null, Is.True, $"Seed {seed} retires old colliders");
                Assert.That(Find("WorldGenerator").Length, Is.EqualTo(1));
                var drones = Find("DroneBody");
                Assert.That(drones.Length, Is.EqualTo(25));
                foreach (var drone in drones)
                {
                    Assert.That(drone.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True, drone.name);
                    Assert.That(Call(drone, "CanOccupy", drone.transform.position), Is.True, drone.name);
                }
                Assert.That(player.GetComponent<Rigidbody>(), Is.Null);
                Assert.That(capsule.height, Is.EqualTo(1.8f).Within(.001f));
                Assert.That(capsule.radius, Is.EqualTo(.35f).Within(.001f));
                Assert.That(capsule.stepOffset, Is.EqualTo(.3f).Within(.001f));
                Assert.That(capsule.slopeLimit, Is.EqualTo(45f).Within(.001f));
                Assert.That(capsule.skinWidth, Is.EqualTo(.05f).Within(.001f));
                Call(player.GetComponent(RuntimeType("Health")), "Configure", 10000f, false);

                var spawn = generator.transform.Find("Spawn");
                capsule.Move(Vector3.down * .25f);
                Assert.That(capsule.isGrounded, Is.True, $"Seed {seed} spawn support");
                Assert.That(Vector3.Distance(spawn.position, player.transform.position), Is.LessThan(.23f));

                var fence = World.Find("Environment/Fences").GetChild(0).GetComponent<BoxCollider>();
                AssertSolid(fence);
                Vector3 inward = Vector3.ProjectOnPlane(World.position - fence.bounds.center, Vector3.up).normalized;
                Vector3 fencePoint = fence.bounds.center + inward * 2f;
                fencePoint.y = .12f;
                Place(fencePoint);
                Walk(-inward, 4f);
                Assert.That(Vector3.Dot(player.transform.position - fence.bounds.center, inward), Is.GreaterThan(.28f));
                Assert.That(World.Find("Environment/Perimeter fence infill").GetComponentInChildren<Collider>(), Is.Null);

                var rock = World.Find("Environment/Rocks").GetChild(0).GetComponent<CapsuleCollider>();
                Assert.That(rock, Is.Not.Null);
                Assert.That(rock.bounds.max.y - World.position.y, Is.InRange(1.8f, 2.02f), "Boulder contact stays near its visible height.");
                Assert.That(rock.bounds.size.x, Is.LessThan(2.2f), "The hidden sphere's worst-axis inflation was removed.");
                var tree = World.Find("Environment/Trees").GetChild(0).GetChild(0).GetComponent<Collider>();
                var crate = World.Find("Environment/Crates").GetChild(0).Find("Crate body").GetComponent<BoxCollider>();
                BlockedBy(rock, Vector3.forward);
                BlockedBy(tree, Vector3.forward);
                BlockedBy(crate, crate.transform.forward);

                var dock = World.Find("Buildings/Storage/ExteriorDetails/Loading dock");
                var platform = dock.Find("Loading platform").GetComponent<BoxCollider>();
                var freight = dock.Find("Cargo containers/Freight container/Steel shell").GetComponent<BoxCollider>();
                AssertSolid(platform);
                AssertSolid(freight);
                BlockedBy(freight, dock.forward, .18f);

                var checkpoint = World.Find("Buildings/Checkpoint");
                Transform walls = checkpoint.Find("Walls");
                var wall = walls.GetChild(0).GetComponent<BoxCollider>();
                AssertSolid(checkpoint.Find("Roof cap").GetComponent<BoxCollider>());
                BlockedBy(wall, Vector3.ProjectOnPlane(checkpoint.position - wall.bounds.center, Vector3.up).normalized);
                VerifyNearPlane(wall);

                var gate = World.Find("Final Extraction Gate");
                BlockedBy(gate.Find("Extraction shutter/Shutter body").GetComponent<BoxCollider>(), gate.forward);

                // Each entrance starts locked and physically blocks a controller even after E is attempted.
                for (int i = 0; i < Sites.Length; i++)
                {
                    Transform door = Door(i);
                    AimAtDoor(door);
                    Call(Action(door), "Interact");
                    AssertSolid(door.GetComponent<BoxCollider>());
                    Walk(door.forward, 3.5f);
                    Assert.That(door.InverseTransformPoint(player.transform.position).z, Is.LessThan(-.29f), Sites[i] + " closed");
                }
                var reader = Door(0).Find("Lock reader housing").GetComponent<BoxCollider>();
                AssertSolid(reader);
                Vector3 readerFeet = reader.bounds.center - reader.transform.forward * 1.5f;
                readerFeet.y = .12f;
                Place(readerFeet);
                camera.transform.LookAt(reader.bounds.center);
                Assert.That(Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit readerHit, 3f) &&
                    readerHit.collider == reader, Is.True, "Checkpoint lock reader is an independent interaction target");

                Call(Action(World.Find("OutdoorSupplyCache01/Supply crate")), "Interact");
                for (int i = 0; i < Sites.Length; i++)
                {
                    Transform door = Door(i);
                    AimAtDoor(door);
                    Call(Action(door), "Interact");
                    AssertSolid(door.GetComponent<BoxCollider>());
                    Walk(door.forward, 3.5f);
                    Assert.That(door.InverseTransformPoint(player.transform.position).z, Is.LessThan(-.29f),
                        Sites[i] + " opening retains the collision barrier");
                    yield return new WaitForSecondsRealtime(.28f);
                    AssertSolid(door.GetComponent<BoxCollider>());
                    Walk(door.forward, .4f);
                    Assert.That(door.InverseTransformPoint(player.transform.position).z, Is.LessThan(-.29f),
                        Sites[i] + " moving shutter cannot push the player into the opening");
                    yield return new WaitForSecondsRealtime(.44f);
                    Assert.That(door.GetComponent<BoxCollider>().enabled, Is.False, Sites[i] + " barrier opens");
                    Walk(door.forward, 3.5f);
                    Assert.That(door.InverseTransformPoint(player.transform.position).z, Is.GreaterThan(1f), Sites[i] + " entry");
                    Assert.That(capsule.isGrounded, Is.True);
                    Walk(-door.forward, 4.5f);
                    Assert.That(door.InverseTransformPoint(player.transform.position).z, Is.LessThan(-1f), Sites[i] + " exit");
                    Complete(i);
                }

                var mission = World.GetComponentInChildren(RuntimeType("OpenWorldMissionManager"));
                Assert.That(mission.GetType().GetProperty("BuildingMissionsCompleted").GetValue(mission), Is.EqualTo(4));
                const string bodyType = "Health";
                foreach (string target in new[] {
                    "Buildings/Server/IndoorGuards/Server Racks",
                    "OutdoorScoutPatrols/05 Checkpoint Approach",
                    "OutdoorFlankerAmbushes/03 Control to Storage" })
                    Call(World.Find(target).GetComponentInChildren(RuntimeType(bodyType)), "TakeDamage", 1000f);
                Vector3 gateApproach = gate.position - gate.forward * 2.2f + Vector3.up * .12f;
                Place(gateApproach);
                camera.transform.LookAt(gate.position + Vector3.up * 1.4f);
                Assert.That(Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit gateHit, 3f) &&
                    gateHit.collider.transform.IsChildOf(gate), Is.True, "Extraction E ray reaches the gate from the road.");
                Call(Action(gate), "Interact");
                Assert.That(mission.GetType().GetProperty("Escaped").GetValue(mission), Is.True);
                Physics.SyncTransforms();
                Walk(gate.forward, 3.6f);
                Assert.That(gate.InverseTransformPoint(player.transform.position).z, Is.GreaterThan(1f),
                    "Raised extraction shutter leaves ground-level passage");
            }
        }

        [UnityTest]
        public IEnumerator RegeneratingInMidJumpClearsVerticalVelocity()
        {
            for (int i = 0; i < 15; i++) Call(player, "MovePlayer", Vector3.zero, 1f / 60f);
            Assert.That(capsule.isGrounded, Is.True);
            Assert.That(Call(player, "TryJump"), Is.True);
            Call(player, "MovePlayer", Vector3.zero, 1f / 60f);
            Generate(5678);
            float spawnY = player.transform.position.y;
            Call(player, "MovePlayer", Vector3.zero, 1f / 60f);
            Assert.That(player.transform.position.y, Is.LessThanOrEqualTo(spawnY + .003f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllDroneHullsStartClearAndDefeatedDronesStopAndSettle()
        {
            Generate(1234);
            yield return null;
            yield return null;
            Component[] bodies = Find("DroneBody");
            Assert.That(bodies.Length, Is.EqualTo(25));
            foreach (var body in bodies)
            {
                Assert.That(body.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True, body.name);
                Assert.That(Call(body, "CanOccupy", body.transform.position), Is.True, body.name);
            }

            // Collision-ignored objects still appear in the overlap and cast queries. They
            // must not stall a live hull while the separate falling wreck is still solid.
            var sample = bodies[0];
            var blocker = new GameObject("Collision-ignored fixture").AddComponent<BoxCollider>();
            blocker.transform.position = sample.transform.position +
                (Vector3)sample.GetType().GetProperty("EnvelopeCenter").GetValue(sample);
            var hull = sample.GetComponent<CapsuleCollider>();
            Physics.SyncTransforms();
            Assert.That(Call(sample, "CanOccupy", sample.transform.position), Is.False);
            Physics.IgnoreCollision(hull, blocker);
            Assert.That(Call(sample, "CanOccupy", sample.transform.position), Is.True);
            Object.Destroy(blocker.gameObject);

            var targets = new[] { "GuardBrain", "ScoutBrain", "FlankerBrain", "InterceptorBrain" }
                .Select(role => Find(role).First()).ToArray();
            Vector3[] positions = targets.Select(target => target.transform.position).ToArray();
            foreach (var target in targets) Call(target.GetComponent(RuntimeType("Health")), "TakeDamage", 1000f);
            yield return null;
            yield return null;
            foreach (var target in targets)
            {
                Assert.That(target.GetComponent<NavMeshAgent>().enabled, Is.False, target.name + " navigation stopped");
                Assert.That(target.GetComponent<CapsuleCollider>().enabled, Is.False, target.name + " living hull disabled");
            }

            var wrecks = Find("DroneWreckMotion");
            Assert.That(wrecks.Length, Is.EqualTo(4));
            foreach (var wreck in wrecks)
            {
                var rigidbody = wreck.GetComponent<Rigidbody>();
                Assert.That(rigidbody, Is.Not.Null);
                Assert.That(Physics.GetIgnoreCollision(wreck.GetComponent<Collider>(), capsule), Is.True);
            }

            float limit = Time.time + 8f;
            while (Time.time < limit && wrecks.Any(wreck => !wreck.GetComponent<Rigidbody>().isKinematic))
                yield return null;
            foreach (var wreck in wrecks)
            {
                Assert.That(wreck.GetComponent<Rigidbody>().isKinematic, Is.True, wreck.name + " settled");
                Assert.That(wreck.GetComponents<Collider>().Any(shape => shape.enabled), Is.False);
                Assert.That((int)wreck.GetType().GetProperty("ImpactCount").GetValue(wreck), Is.EqualTo(1));
            }
            for (int i = 0; i < targets.Length; i++)
                Assert.That(Vector3.Distance(positions[i], targets[i].transform.position), Is.LessThan(.02f));
        }

        private Transform World => generator.transform.Find("GeneratedWorld");
        private Transform Door(int index) => World.Find($"Buildings/{Sites[index]}/{Sites[index]} Access Door");
        private static Type RuntimeType(string name) => Type.GetType("ShiftFour." + name + ", Assembly-CSharp", true);
        private static Component[] Find(string name) =>
            Object.FindObjectsByType(RuntimeType(name), FindObjectsSortMode.None).Cast<Component>().ToArray();
        private static object Call(Component component, string method, params object[] args) =>
            component.GetType().GetMethod(method, Members).Invoke(component, args);
        private static MonoBehaviour Action(Transform target) => target.GetComponents<MonoBehaviour>()
            .Single(component => RuntimeType("IInteractable").IsInstanceOfType(component));

        private void Generate(int seed)
        {
            generator.GetType().GetField("seed", Members).SetValue(generator, seed);
            Call(generator, "Generate");
        }

        private void Place(Vector3 feet)
        {
            capsule.enabled = false;
            player.transform.position = feet;
            capsule.enabled = true;
            Call(player, "ApplyCameraPose");
            Physics.SyncTransforms();
        }

        private void Walk(Vector3 direction, float metres)
        {
            for (float walked = 0f; walked < metres; walked += .065f)
                capsule.Move(direction * .065f + Vector3.down * .032f);
            Call(player, "ApplyCameraPose");
        }

        private void BlockedBy(Collider solid, Vector3 direction, float feetHeight = .12f)
        {
            AssertSolid(solid);
            Vector3 center = solid.bounds.center;
            // Start beyond the near face. Project AABB onto the test direction even for rotated crates.
            Vector3 absolute = new Vector3(Mathf.Abs(direction.x), 0f, Mathf.Abs(direction.z));
            Vector3 start = center - direction * (Vector3.Dot(solid.bounds.extents, absolute) + 1.2f);
            start.y = feetHeight;
            Place(start);
            Walk(direction, Vector3.Distance(Vector3.ProjectOnPlane(start - center, Vector3.up), Vector3.zero) + .5f);
            Vector3 sample = player.transform.position + Vector3.up * (capsule.height * .5f);
            Assert.That(Vector3.Dot(sample - center, direction), Is.LessThan(-.25f), solid.name + " blocks actual movement");
        }

        private void VerifyNearPlane(BoxCollider wall)
        {
            camera.aspect = 32f / 9f;
            Vector3 towards = Vector3.ProjectOnPlane(wall.bounds.center - camera.transform.position, Vector3.up).normalized;
            foreach (int pitch in new[] { -80, 0, 80 })
            {
                camera.transform.rotation = Quaternion.LookRotation(towards) * Quaternion.Euler(pitch, 0, 0);
                Call(player, "ApplyCameraPose");
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 point = camera.ViewportToWorldPoint(new Vector3(corner & 1, corner >> 1, camera.nearClipPlane));
                    Assert.That(wall.bounds.Contains(point), Is.False, "Ultrawide near-plane corner stays clear of the wall");
                }
            }
            camera.ResetAspect();
        }

        private void AimAtDoor(Transform door)
        {
            Vector3 feet = door.position - door.forward * 2.2f;
            feet.y = .12f;
            Place(feet);
            camera.transform.LookAt(door.position + Vector3.up * 1.3f);
            Assert.That(Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit, 3f) &&
                hit.collider == door.GetComponent<BoxCollider>(), Is.True, door.name + " uses the original E ray");
        }

        private void Complete(int site)
        {
            switch (site)
            {
                case 0:
                    Call(Find("SecurityAccessCard").Single(), "Interact");
                    Call(Find("CheckpointSecurityScanner").Single(), "Interact");
                    break;
                case 1:
                    var terminals = Find("ServerTerminal").OrderBy(terminal => terminal.name).ToArray();
                    foreach (int index in new[] { 1, 0, 2 }) Call(terminals[index], "Interact");
                    break;
                case 2:
                    foreach (var roomSwitch in Find("LockdownSecuritySwitch")) Call(roomSwitch, "Interact");
                    Call(Find("ControlLockdownMission").Single(), "Interact");
                    break;
                default:
                    foreach (var cell in Find("PowerCellPickup")) Call(cell, "Interact");
                    Call(Find("StoragePowerMission").Single(), "Interact");
                    break;
            }
        }

        private static void AssertSolid(Collider collider) =>
            Assert.That(collider != null && collider.enabled && !collider.isTrigger, Is.True,
                (collider != null ? collider.name : "Missing collider") + " should be solid");
    }
}
#endif

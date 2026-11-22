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
            yield return OutdoorMissionMenuPlayModeTests.StartMission();
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
        public IEnumerator HallDisplaysFollowMissionsAndRetireWithTheirRooms()
        {
            var material = Resources.Load<Material>("Hall Instrumentation");
            Assert.That(material, Is.Not.Null);
            var block = new MaterialPropertyBlock();
            float Level(string path)
            {
                World.Find("Buildings/" + path).GetComponent<Renderer>().GetPropertyBlock(block);
                return block.GetFloat("_Level");
            }

            const string checkpoint = "Checkpoint/Interior/Props/Scanner partition/Security Scanner Reader/Authentication ticks";
            const string server = "Server/Interior/Props/Network sequence display/Routing trace";
            const string control = "Control/Interior/Props/Emergency warning beacon/Lockdown annunciator";
            const string storage = "Storage/Interior/Props/Emergency generator/Energy core projection";
            foreach (int seed in new[] { 1234, 5678, 9012 })
            {
                Transform retired = World;
                Generate(seed);
                yield return null;
                Assert.That(retired == null, Is.True);
                foreach (string site in Sites)
                {
                    var room = World.Find("Buildings/" + site + "/Interior");
                    Assert.That(room.GetComponentsInChildren(RuntimeType("HallInstrumentation")).Length, Is.EqualTo(1));
                    var displays = room.GetComponentsInChildren<Renderer>().Where(r => r.sharedMaterial == material).ToArray();
                    Assert.That(displays.Length, Is.GreaterThan(0));
                    Assert.That(displays.All(r => r.GetComponent<Collider>() == null), Is.True, site);
                }
                Assert.That(Level(checkpoint), Is.LessThan(0));
                Assert.That(Level(server), Is.Zero);
                Assert.That(Level(control), Is.LessThan(0));
                Assert.That(Level(storage), Is.Zero);
                for (int i = 0; i < Sites.Length; i++) Complete(i);
                yield return new WaitForSeconds(.22f);
                Assert.That(Level(checkpoint), Is.EqualTo(1));
                Assert.That(Level(server), Is.EqualTo(1));
                Assert.That(Level(control), Is.GreaterThan(1), "Secured, steady lighting");
                Assert.That(Level(storage), Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator RegenerationReleasesFenceMeshesAndSharedDroneMaterials()
        {
            foreach (int seed in new[] { 1234, 5678, 9012 })
            {
                Generate(seed);
                yield return null;
                Transform fences = World.Find("Environment/Fences");
                Assert.That(fences.childCount, Is.EqualTo(5));
                Mesh fenceMesh = null;
                foreach (Transform span in fences)
                {
                    var collider = span.GetComponent<BoxCollider>();
                    AssertSolid(collider);
                    Assert.That(span.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(1));
                    fenceMesh = span.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(fenceMesh.bounds.size.x, Is.EqualTo(collider.size.x + .14f).Within(.01f));
                    Assert.That(fenceMesh.bounds.size.y, Is.EqualTo(2.6f).Within(.01f));
                }

                var renderers = World.GetComponentsInChildren<Renderer>(true);
                Material barrels = CheckShared("Weapon housing");
                Material traces = CheckShared("Accepted shot tracer");
                Call(generator, "Clear");
                yield return null;
                yield return null;
                Assert.That(fenceMesh == null && barrels == null && traces == null, Is.True,
                    $"Seed {seed} retained native rendering resources after clear.");

                Material CheckShared(string name)
                {
                    var matches = renderers.Where(r => r.name == name).ToArray();
                    Assert.That(matches.Length, Is.EqualTo(25), name);
                    var materials = matches.Select(r => r.sharedMaterial).Distinct().ToArray();
                    Assert.That(materials.Length, Is.EqualTo(1), name);
                    Assert.That(materials[0] != null, Is.True, name);
                    return materials[0];
                }
            }
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
        public IEnumerator GeneratedBouldersMatchTheirVisibleShapeAndKeepRoutesClear()
        {
            const int verticesPerBoulder = 7 * 3 * 3;
            const float probeRadius = 3.3f;
            const float surfaceTolerance = .23f;
            int[] seeds = { 1234, 5678, 9012, 1234 };
            Vector3[] firstPositions = null;
            int[] firstColliderCounts = null;
            Transform retiredRocks = null;
            foreach (int seed in seeds)
            {
                Generate(seed);
                yield return null;
                Assert.That(retiredRocks == null, Is.True, "Regeneration destroys the old rock compound.");
                Transform environment = World.Find("Environment");
                retiredRocks = environment.Find("Rocks");
                var rocks = retiredRocks.Cast<Transform>().ToArray();
                var batch = environment.Find("Boulders and scree").GetComponent<MeshFilter>();
                Assert.That(rocks, Is.Not.Empty);
                Assert.That(batch.GetComponent<Collider>(), Is.Null, "The render batch and scree have no collider.");
                Vector3[] vertices = batch.sharedMesh.vertices.Select(batch.transform.TransformPoint).ToArray();
                var pilot = Find("OutdoorScout")[0].GetComponent<NavMeshAgent>();
                var outdoor = new NavMeshQueryFilter { agentTypeID = pilot.agentTypeID, areaMask = pilot.areaMask };
                var counts = new int[rocks.Length];

                for (int index = 0; index < rocks.Length; index++)
                {
                    Transform rock = rocks[index];
                    var visible = new ArraySegment<Vector3>(vertices, index * verticesPerBoulder, verticesPerBoulder);
                    var pieces = rock.GetComponents<BoxCollider>();
                    counts[index] = pieces.Length;
                    Assert.That(pieces.Length, Is.GreaterThan(1));
                    Assert.That(Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, rock.gameObject.layer), Is.False);
                    Assert.That(pieces.All(piece => piece.enabled && !piece.isTrigger), Is.True);
                    Vector3 ground = new Vector3(rock.position.x, World.position.y, rock.position.z);
                    Assert.That(NavMesh.SamplePosition(ground, out _, .55f, outdoor), Is.False,
                        $"Seed {seed}, rock {index} excludes the drone route.");

                    var bounds = new Bounds(visible[0], Vector3.zero);
                    foreach (Vector3 point in visible) bounds.Encapsulate(point);
                    Bounds solid = pieces[0].bounds;
                    foreach (BoxCollider piece in pieces) solid.Encapsulate(piece.bounds);
                    Assert.That((bounds.min - solid.min).magnitude, Is.LessThan(.17f), $"Seed {seed}, rock {index} minimum");
                    Assert.That((bounds.max - solid.max).magnitude, Is.LessThan(.17f), $"Seed {seed}, rock {index} maximum");

                    for (int angle = 0; angle < 360; angle += 45)
                    {
                        Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                        foreach (float y in new[] { .35f, .85f, 1.3f })
                        {
                            var ray = new Ray(new Vector3(rock.position.x, World.position.y + y, rock.position.z) +
                                outward * probeRadius, -outward);
                            float rendered = NearestTriangle(ray, visible);
                            float collision = pieces.Min(piece => piece.Raycast(ray, out RaycastHit hit, probeRadius) ?
                                hit.distance : float.PositiveInfinity);
                            Assert.That(float.IsFinite(rendered), Is.True);
                            Assert.That(collision, Is.EqualTo(rendered).Within(surfaceTolerance),
                                $"Seed {seed}, rock {index}, heading {angle}, height {y}");
                        }

                        if (index != 0) continue;
                        Vector3 start = rock.position + outward * probeRadius;
                        start.y = World.position.y + .04f;
                        Place(start);
                        Walk(-outward, probeRadius + .4f);
                        Vector3 offset = Vector3.ProjectOnPlane(player.transform.position - rock.position, Vector3.up);
                        Assert.That(offset.magnitude, Is.GreaterThan(1.1f), $"Seed {seed}, heading {angle} stays outside the center.");
                        Vector3 finalDirection = offset.normalized;
                        var finalProbe = new Ray(new Vector3(rock.position.x, player.transform.position.y + capsule.radius, rock.position.z) +
                            finalDirection * probeRadius, -finalDirection);
                        float clearance = offset.magnitude - (probeRadius - NearestTriangle(finalProbe, visible));
                        Assert.That(clearance, Is.InRange(.13f, .68f), $"Seed {seed}, heading {angle} stops close to visible rock.");
                    }
                }

                // Walk a full perimeter at walking height, just beyond the widest contact profile.
                Vector3 center = rocks[0].position;
                center.y = World.position.y + .04f;
                Place(center + Vector3.forward * 2.1f);
                for (int angle = 15; angle <= 360; angle += 15)
                {
                    Vector3 goal = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.1f;
                    Vector3 displacement = Vector3.ProjectOnPlane(goal - player.transform.position, Vector3.up);
                    Walk(displacement.normalized, displacement.magnitude);
                    Assert.That(Vector3.ProjectOnPlane(goal - player.transform.position, Vector3.up).magnitude,
                        Is.LessThan(.09f), $"Seed {seed} remains walkable around the boulder.");
                }

                if (seed != seeds[0]) continue;
                Vector3[] positions = rocks.Select(rock => rock.position).ToArray();
                if (firstPositions == null)
                {
                    firstPositions = positions;
                    firstColliderCounts = counts;
                }
                else
                {
                    Assert.That(positions, Is.EqualTo(firstPositions), "The same seed reproduces each rock position.");
                    Assert.That(counts, Is.EqualTo(firstColliderCounts), "Regeneration creates the same compound once.");
                }
            }
        }

        private static float NearestTriangle(Ray ray, ArraySegment<Vector3> vertices)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < vertices.Count; i += 3)
            {
                Vector3 a = vertices[i], b = vertices[i + 1], c = vertices[i + 2];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                float alignment = Vector3.Dot(normal, ray.direction);
                if (Mathf.Abs(alignment) < .00001f) continue;
                float distance = Vector3.Dot(normal, a - ray.origin) / alignment;
                if (distance <= 0f || distance >= nearest) continue;
                Vector3 hit = ray.GetPoint(distance);
                if (Vector3.Dot(normal, Vector3.Cross(b - a, hit - a)) >= -1e-6f &&
                    Vector3.Dot(normal, Vector3.Cross(c - b, hit - b)) >= -1e-6f &&
                    Vector3.Dot(normal, Vector3.Cross(a - c, hit - c)) >= -1e-6f)
                    nearest = distance;
            }
            return nearest;
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

                var tree = World.Find("Environment/Trees").GetChild(0).GetChild(0).GetComponent<Collider>();
                var crate = World.Find("Environment/Crates").GetChild(0).Find("Crate body").GetComponent<BoxCollider>();
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
        public IEnumerator FirstGeneratedWorldShowsPistolSparksAtAcceptedContact()
        {
            var crate = World.Find("OutdoorSupplyCache01/Supply crate");
            Call(Action(crate), "Interact");
            var mount = player.GetComponent(RuntimeType("FirstPersonWeaponMount"));
            for (int i = 0; i < 150 && !(bool)mount.GetType().GetProperty("IsReady").GetValue(mount); i++)
                yield return null;

            var target = Find("ScoutBrain").First();
            var health = target.GetComponent(RuntimeType("Health"));
            Vector3 center = target.GetComponent<CapsuleCollider>().bounds.center;
            RaycastHit contact = default;
            bool clear = false;
            for (int a = 0; a < 360 && !clear; a += 45)
            {
                Vector3 eye = center + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 2.5f;
                capsule.enabled = false;
                player.transform.position += eye - camera.transform.position;
                capsule.enabled = true;
                camera.transform.LookAt(center);
                Physics.SyncTransforms();
                clear = Physics.Raycast(camera.transform.position, camera.transform.forward, out contact, 4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    contact.collider.GetComponentInParent(RuntimeType("Health")) == health;
            }
            Assert.That(clear, Is.True);
            var sparks = target.GetComponentsInChildren<LineRenderer>().Where(r => r.name.StartsWith("Impact spark")).ToArray();
            Assert.That(sparks.Length, Is.EqualTo(8));
            Assert.That(sparks.All(r => !r.enabled), Is.True);
            var hud = player.GetComponent(RuntimeType("CombatHud"));
            Call(hud, "Update");
            Assert.That(Call(hud, "IsDroneBarVisible", target), Is.False, "Aiming alone never reveals the bar");
            var weapon = player.GetComponent(RuntimeType("WeaponController"));
            Assert.That(Call(weapon, "TryFire"), Is.EqualTo(true));
            Call(hud, "Update");
            Assert.That(Call(hud, "IsDroneBarVisible", target), Is.True, "The hit reveals this Scout's bar");
            Assert.That(Call(weapon, "TryFire"), Is.EqualTo(false));
            yield return null;
            Assert.That(sparks.All(r => r.enabled), Is.True);
            Assert.That(Vector3.Distance(sparks[0].GetPosition(0), contact.point), Is.LessThan(.22f));
            yield return new WaitForSeconds(.4f);
            Assert.That(sparks.All(r => !r.enabled), Is.True);
            Call(health, "TakeDamage", 1000f);
            Call(hud, "Update");
            Assert.That(Call(hud, "IsDroneBarVisible", target), Is.False, "The bar hides immediately on death");
        }

        [UnityTest]
        public IEnumerator GeneratedFlightRigsKeepHitboxesOutsideTheAnimatedModelAcrossSeeds()
        {
            Transform previous = null;
            foreach (int seed in new[] { 1234, 5678, 9012 })
            {
                Generate(seed);
                yield return null;
                var visuals = World.GetComponentsInChildren(RuntimeType("DroneRotorVisuals"));
                Assert.That(visuals.Length, Is.EqualTo(25));
                Assert.That(previous == null, Is.True, "Regeneration retired the old rig");
                foreach (var visual in visuals)
                {
                    var root = visual.GetComponentInParent<NavMeshAgent>().transform;
                    Transform rig = root.Find("Flight hitboxes");
                    Assert.That(rig, Is.Not.Null, root.name);
                    Assert.That(root.Cast<Transform>().Count(t => t.name == rig.name), Is.EqualTo(1));
                    Assert.That(visual.GetComponentsInChildren<Collider>().Any(c => c.enabled), Is.False);
                    Assert.That(visual.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("Animated Rotor")), Is.EqualTo(4));
                    var brain = root.GetComponent(RuntimeType("DroneBrain"));
                    Assert.That(visual.GetType().GetField("flightRole", Members).GetValue(visual).ToString(),
                        Is.EqualTo(brain.GetType().Name.Replace("Brain", "")));
                    var shapes = rig.GetComponentsInChildren<MeshCollider>();
                    Assert.That(shapes.Length, Is.GreaterThanOrEqualTo(5));
                    var rest = shapes.Select(c => c.transform.localToWorldMatrix).ToArray();
                    var original = visual.transform.localRotation;
                    visual.transform.localRotation = original * Quaternion.Euler(3f, 0f, 2f);
                    for (int i = 0; i < shapes.Length; i++)
                    {
                        Assert.That(shapes[i].enabled && shapes[i].sharedMesh != null, Is.True);
                        Assert.That(shapes[i].transform.localToWorldMatrix, Is.EqualTo(rest[i]));
                    }
                    visual.transform.localRotation = original;
                    previous = rig;
                }
            }
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
                Assert.That(Physics.DefaultRaycastLayers & (1 << wreck.gameObject.layer), Is.Zero,
                    "Falling debris cannot intercept interaction rays");
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

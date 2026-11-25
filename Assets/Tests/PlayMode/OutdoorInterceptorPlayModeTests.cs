#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShiftFour.Tests
{
    public sealed class OutdoorInterceptorPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/OpenWorldPrototype.unity";
        private static readonly string[] StationNames =
            { "01 Checkpoint Approach", "02 Central Avenue", "03 Control Exterior", "04 Storage Approach" };
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type TypeOf(string name) => Type.GetType($"ShiftFour.{name}, Assembly-CSharp", true);
        private static object Read(object target, string property) => target.GetType().GetProperty(property, Members).GetValue(target);
        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Members).Invoke(target, args);
        private static Component FindOne(string type) => Object.FindFirstObjectByType(TypeOf(type)) as Component;

        [UnityTest]
        public IEnumerator RuntimeBindingsHandleMissingCachesAndRegeneration()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();

            var equipment = FindOne("OutdoorPlayerEquipment");
            var knife = equipment.GetComponentInChildren(TypeOf("OutdoorKnifeViewmodel"), true);
            Assert.That(knife, Is.Not.Null);
            var grip = knife.GetComponentInChildren(TypeOf("FirstPersonPistolGrip"), true);
            Assert.That(grip, Is.Not.Null, "The opening knife should use the authored arm rig.");
            grip.GetType().GetField("dominant", Members).SetValue(grip, null);
            grip.GetType().GetField("support", Members).SetValue(grip, null);
            Call(knife, "Present", true, 1f, float.NegativeInfinity, false);
            Assert.That(Read(grip, "IsReady"), Is.True);

            var drone = FindOne("InterceptorBrain");
            var brainType = TypeOf("DroneBrain");
            var awareness = brainType.GetProperty("Awareness", Members);
            var initializedAwareness = awareness.GetValue(drone);
            Assert.That(initializedAwareness, Is.Not.Null);
            try
            {
                awareness.SetValue(drone, null);
                Assert.DoesNotThrow(() => brainType.GetMethod("Update", Members).Invoke(drone, null));
            }
            finally { awareness.SetValue(drone, initializedAwareness); }

            var visual = (Behaviour)drone.GetComponentInChildren(TypeOf("DroneRotorVisuals"));
            var weaponMount = visual.GetType().GetProperty("WeaponMount", Members);
            var initializedMount = weaponMount.GetValue(visual);
            weaponMount.SetValue(visual, null);
            visual.enabled = false;
            weaponMount.SetValue(visual, initializedMount);
            visual.enabled = true;

            var oldLoop = drone.transform.Find("Drone flight loop");
            Assert.That(oldLoop != null, Is.True);
            var world = FindOne("WorldGenerator");
            Call(world, "Clear");
            yield return null;
            Assert.That(oldLoop == null, Is.True);
            Call(world, "Generate");
            yield return null;
            var regenerated = Object.FindObjectsByType(brainType).Cast<Component>().ToArray();
            Assert.That(regenerated, Is.Not.Empty);
            foreach (var unit in regenerated)
                Assert.That(unit.GetComponentsInChildren<AudioSource>().Count(source => source.name == "Drone flight loop"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AirframeFitsFullFlightEnvelopeAndResetsWithWorld()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();
            var world = FindOne("WorldGenerator");
            Transform old = null;
            Transform oldGraph = null;
            foreach (int seed in new[] { 1234, 5678, 9012 })
            {
                world.GetType().GetField("seed", Members).SetValue(world, seed);
                Call(world, "Generate");
                yield return null;
                Assert.That(old == null && oldGraph == null, Is.True, $"Seed {seed} retired the previous patrols.");
                var hub = FindOne("OutdoorInterceptorDispatch");
                var units = hub.GetComponentsInChildren(TypeOf("InterceptorBrain"));
                Assert.That(hub.transform.Cast<Transform>().Select(station => station.name), Is.EqualTo(StationNames));
                var previousPosts = new System.Collections.Generic.List<Vector3>();
                var initialPositions = units.Select(unit => unit.transform.position).ToArray();
                Assert.That(units.Length, Is.EqualTo(4));
                foreach (var unit in units)
                {
                    var visual = unit.GetComponentInChildren(TypeOf("DroneRotorVisuals"));
                    var frame = visual.transform.Find("Interceptor IV airframe");
                    Assert.That(frame, Is.Not.Null);
                    Assert.That(frame.GetComponentsInChildren<Collider>(), Is.Empty);
                    Assert.That(Read(visual, "AnimationReport").ToString(), Does.Contain("Sensor Standby"));
                    var body = unit.GetComponent(TypeOf("DroneBody"));
                    var station = hub.transform.Find(StationNames[Array.IndexOf(units, unit)]);
                    Assert.That(unit.transform.IsChildOf(station), Is.True);
                    var patrol = station.Find("Response Navigation");
                    oldGraph = patrol;
                    var posts = patrol.Cast<Transform>().Select(post => post.position).ToArray();
                    Assert.That(posts.Length, Is.EqualTo(4));
                    var agent = unit.GetComponent<NavMeshAgent>();
                    Assert.That(agent.isOnNavMesh, Is.True, station.name);
                    var navFilter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                    for (int index = 0; index < posts.Length; index++)
                    {
                        Vector3 from = posts[index], to = posts[(index + 1) % posts.Length];
                        Assert.That(previousPosts.All(other => Vector3.Distance(from, other) >= 5.99f), Is.True, station.name);
                        Assert.That(Call(body, "CanOccupy", from), Is.True, station.name);
                        Assert.That(NavMesh.Raycast(from, to, out _, navFilter), Is.False, station.name);
                    }
                    previousPosts.AddRange(posts);

                    var extents = (Vector3)Read(body, "EnvelopeExtents");
                    var center = (Vector3)Read(body, "EnvelopeCenter");
                    float yOffset = Mathf.Abs(visual.transform.localPosition.y - center.y);
                    foreach (var filter in frame.GetComponentsInChildren<MeshFilter>())
                    foreach (var v in filter.sharedMesh.vertices)
                    {
                        var offset = filter.transform.TransformPoint(v) - visual.transform.position;
                        float radius = new Vector2(offset.x, offset.z).magnitude;
                        // Bound every yaw and tilt axis, plus the existing full hover excursion.
                        float sine = Mathf.Sin(14f * Mathf.Deg2Rad);
                        Assert.That(Mathf.Abs(offset.y) + radius*sine + yOffset + .02f, Is.LessThan(extents.y));
                        Assert.That(radius + Mathf.Abs(offset.y)*sine, Is.LessThan(Mathf.Min(extents.x,extents.z)));
                    }
                    old = frame;
                }

                bool[] crossedLeg = new bool[units.Length];
                float deadline = Time.time + 9f;
                while (Time.time < deadline && crossedLeg.Any(crossed => !crossed))
                {
                    for (int index = 0; index < units.Length; index++)
                    {
                        var unit = units[index];
                        if (crossedLeg[index] || Vector3.Distance(initialPositions[index], unit.transform.position) < 4f) continue;
                        crossedLeg[index] = Read(unit, "CurrentDecision").Equals("Patrol post 3");
                    }
                    yield return null;
                }
                for (int index = 0; index < crossedLeg.Length; index++)
                    Assert.That(crossedLeg[index], Is.True, $"Seed {seed}: {units[index].name} physically reached its second patrol post.");

                string folder = Environment.GetEnvironmentVariable("SHIFTFOUR_INTERCEPTOR_CAPTURE");
                if (seed == 1234 && !string.IsNullOrEmpty(folder))
                    yield return CaptureStation(old, folder, "standby");
            }
        }

        // Opt-in close-up of an actual generated station. Camera lifetime is confined to the test.
        private static IEnumerator CaptureStation(Transform frame, string folder, string stage)
        {
            var camera = new GameObject("Station portrait camera").AddComponent<Camera>();
            var texture = new RenderTexture(800, 500, 24);
            var pixels = new Texture2D(800, 500, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.fieldOfView = 31;
                camera.nearClipPlane = .04f;
                camera.targetTexture = texture;
                camera.transform.position = frame.position + frame.rotation * new Vector3(.72f,.63f,1.95f);
                camera.transform.LookAt(frame.position);
                yield return null;
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0,0,800,500),0,0);
                pixels.Apply();
                Directory.CreateDirectory(folder);
                using var output = File.Create(Path.Combine(folder, stage + ".ppm"));
                byte[] header = System.Text.Encoding.ASCII.GetBytes("P6\n800 500\n255\n");
                output.Write(header,0,header.Length);
                byte[] rgb = pixels.GetRawTextureData();
                for (int y=499;y>=0;y--) output.Write(rgb,y*800*3,800*3);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.Destroy(camera.gameObject); Object.Destroy(pixels); Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator DirectSightStartsPredictionWithoutScoutAndSolidCoverBlocksDetection()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();
            var world = FindOne("WorldGenerator");
            world.GetType().GetField("seed", Members).SetValue(world, 1234);
            Call(world, "Generate");
            yield return null;

            var hub = FindOne("OutdoorInterceptorDispatch");
            var central = hub.transform.Find(StationNames[1]).GetComponentInChildren(TypeOf("InterceptorBrain"));
            var otherInterceptors = hub.GetComponentsInChildren(TypeOf("InterceptorBrain")).OfType<Behaviour>()
                .Where(unit => unit != central).ToArray();
            foreach (var unit in otherInterceptors) unit.enabled = false;
            var scouts = FindOne("OutdoorScoutEncounter");
            foreach (Behaviour scout in scouts.GetComponentsInChildren(TypeOf("OutdoorScout")))
                scout.enabled = false;
            var player = FindOne("PlayerController");
            var capsule = player.GetComponent<CharacterController>();
            var sensor = TypeOf("DroneBrain").GetMethod("CanSeePlayer", Members);
            Assert.That(Read(central, "VisionDistance"), Is.EqualTo(35f));
            Assert.That(Read(world, "InterceptorResponseLimit"), Is.EqualTo(2));
            Assert.That(Read(world, "OutdoorAttackLimit"), Is.EqualTo(2));
            Assert.That(Read(hub, "Cooldown"), Is.EqualTo(4f));
            var response = central.GetType().GetField("response", Members).GetValue(central);
            Assert.That(response.GetType().GetField("pursuitTime", Members).GetValue(response), Is.EqualTo(40f));
            Assert.That(response.GetType().GetField("pursuitDistance", Members).GetValue(response), Is.EqualTo(140f));

            void Place(Vector3 feet)
            {
                capsule.enabled = false;
                player.transform.position = feet;
                capsule.enabled = true;
                Call(player, "ApplyCameraPose");
                Physics.SyncTransforms();
            }

            Vector3 approach = ((IEnumerable)Read(central, "ResponseJunctions")).Cast<Vector3>()
                .Where(point => Vector3.Distance(central.transform.position, point) is > 25.5f and < 33f)
                .FirstOrDefault(point =>
                {
                    Place(point + Vector3.up * .2f);
                    return (bool)sensor.Invoke(central, null);
                });
            Assert.That(approach, Is.Not.EqualTo(Vector3.zero), "A clear road should be visible beyond the previous 25 m limit.");

            var visual = central.GetComponentInChildren(TypeOf("DroneRotorVisuals"));
            var mount = Read(visual, "WeaponMount");
            Vector3 muzzle = (Vector3)Read(mount, "MuzzlePosition");
            Vector3 eye = (Vector3)Read(player, "EyePosition");
            var cover = new GameObject("Interceptor line-of-sight test cover").AddComponent<BoxCollider>();
            cover.transform.position = Vector3.Lerp(muzzle, eye, .35f);
            cover.size = new Vector3(4f, 5f, 4f);
            Physics.SyncTransforms();
            try
            {
                Assert.That(sensor.Invoke(central, null), Is.False);
                Assert.That(Read(central, "IsResponding"), Is.False);
            }
            finally
            {
                cover.enabled = false;
                Object.Destroy(cover.gameObject);
            }

            float limit = Time.time + 3f;
            while (Time.time < limit)
            {
                object policy = Read(central, "Policy");
                if (policy != null && float.IsFinite((float)Read(policy, "LastObservedAt"))) break;
                yield return null;
            }
            Assert.That(Read(scouts, "HasAlert"), Is.False);
            Assert.That(Read(central, "IsResponding"), Is.True);
            var prediction = Read(central, "Policy");
            Assert.That(prediction, Is.Not.Null);
            Assert.That(Read(prediction, "History") as IEnumerable, Is.Not.Empty);
            Vector3 observed = (Vector3)Read(prediction, "LastObserved");
            Assert.That(Vector3.Distance(observed, approach), Is.LessThan(1f));
            Assert.That(Read(central, "SensorDecision").ToString(), Does.Match("Pursue last known area|Intercept ahead on road"));

            using var squadProfile = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "ShiftFour.InterceptorCoordination",
                256, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            using var assignmentProfile = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "ShiftFour.InterceptorAssignment",
                256, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            Vector3 heading = Mathf.Sign(central.transform.position.z - approach.z) * Vector3.forward;
            object candidate = null;
            limit = Time.time + 3f;
            while (Time.time < limit && candidate == null)
            {
                capsule.Move((heading * 3f + Vector3.down * 4f) * Time.deltaTime);
                Physics.SyncTransforms();
                yield return null;
                candidate = Read(prediction, "SelectedCandidate");
            }
            Assert.That(candidate, Is.Not.Null, "Steady motion along the avenue should select a forward junction.");
            var hypotheses = ((IEnumerable)Read(prediction, "Hypotheses")).Cast<object>().ToArray();
            Assert.That(hypotheses.Length, Is.InRange(1, 6));
            Assert.That(hypotheses.Sum(hypothesis => (float)Read(hypothesis, "Weight")), Is.EqualTo(1f).Within(.0001f));
            Assert.That((float)candidate.GetType().GetField("HypothesisWeight").GetValue(candidate), Is.GreaterThan(0f));
            Vector3 intercept = (Vector3)candidate.GetType().GetField("Position").GetValue(candidate);
            observed = (Vector3)Read(prediction, "LastObserved");
            Assert.That(Vector3.Dot(intercept - observed, heading), Is.GreaterThan(1f));

            var coordinator = Read(hub, "Coordinator");
            var board = Read(hub, "Knowledge");
            var shared = ((IEnumerable)Read(board, "Records")).Cast<object>().ToArray();
            Assert.That(shared.Any(record =>
            {
                var evidence = record.GetType().GetField("Evidence", Members).GetValue(record);
                return evidence.GetType().GetField("Source", Members).GetValue(evidence).ToString() == "DirectVisual";
            }), Is.True, "Independent sight must publish authorized evidence.");

            object Lease(Component member)
            {
                var id = (int)Read(Read(member, "DiagnosticResponse"), "UnitId");
                return ((IEnumerable)Read(coordinator, "Reservations")).Cast<object>().ElementAt(id - 1);
            }
            int LeaseId(Component member)
            {
                var lease = Lease(member);
                return (int)lease.GetType().GetField("Id", Members).GetValue(lease);
            }
            Vector3 ReservedPoint(Component member)
            {
                var lease = Lease(member);
                var task = lease.GetType().GetField("Task", Members).GetValue(lease);
                return (Vector3)task.GetType().GetField("Destination", Members).GetValue(task);
            }
            Assert.That(LeaseId(central), Is.Positive, "An executed interception must claim its destination.");

            // Exercise both ways of filling the same two slots.
            foreach (var unit in otherInterceptors) unit.enabled = true;
            var reporter = scouts.GetComponentInChildren(TypeOf("OutdoorScout"));
            Vector3 report = otherInterceptors.SelectMany(unit =>
                ((IEnumerable)Read(unit, "ResponseJunctions")).Cast<Vector3>())
                .FirstOrDefault(point => otherInterceptors.Any(unit =>
                    Vector3.Distance(unit.transform.position, point) > 10f &&
                    (bool)Call(unit, "TryPlanResponse", point, default(Vector3), 0f)));
            Assert.That(report, Is.Not.EqualTo(Vector3.zero));
            Call(scouts, "PublishAlert", reporter, report);
            var responding = otherInterceptors.Where(unit => (bool)Read(unit, "IsResponding")).ToArray();
            Assert.That(responding.Length, Is.EqualTo(1), "Scout dispatch should use the second slot.");
            Assert.That(Read(central, "IsResponding"), Is.True);
            var waiting = otherInterceptors.First(unit => !(bool)Read(unit, "IsResponding"));
            Assert.That(Call(hub, "TryReserve", waiting), Is.False, "Sightings and Scout reports share the limit.");

            var second = responding.Single();
            Assert.That(LeaseId(second), Is.Positive);
            Assert.That(Vector3.Distance(ReservedPoint(central), ReservedPoint(second)), Is.GreaterThanOrEqualTo(4f));
            int beforePause = (int)Read(coordinator, "Updates");
            float timeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                yield return null;
                Assert.That(Read(coordinator, "Updates"), Is.EqualTo(beforePause));
            }
            finally { Time.timeScale = timeScale; }
            int previousLease = LeaseId(second);
            Call(Read(second, "DiagnosticGraph"), "SetGateOpen", 0, true);
            yield return null;
            Assert.That(LeaseId(second), Is.Not.EqualTo(previousLease), "A version change must retire its old reservation.");

            void Record(ProfilerRecorder profiler, string label)
            {
                profiler.Stop();
                var samples = profiler.Valid ? profiler.ToArray().Where(sample => sample.Value > 0).ToArray()
                    : Array.Empty<ProfilerRecorderSample>();
                if (samples.Length > 0)
                    TestContext.Out.WriteLine($"F3 live {label}: {samples.Length} sampled frames, " +
                        $"{samples.Average(sample => sample.Value) / 1000d:F1} µs mean, {samples.Max(sample => sample.Value) / 1000d:F1} µs peak.");
            }
            Record(squadProfile, "coordinator");
            Record(assignmentProfile, "assignment solver");
            Call(central.GetComponent(TypeOf("Health")), "TakeDamage", 10000f);
            Assert.That(LeaseId(central), Is.Zero, "Destruction releases the reservation immediately.");
            Call(world, "Generate");
            yield return null;
            Assert.That(hub == null, Is.True);
            var next = FindOne("OutdoorInterceptorDispatch");
            Assert.That(((IEnumerable)Read(Read(next, "Knowledge"), "Records")).Cast<object>(), Is.Empty);
            Assert.That(next.GetComponentsInChildren(TypeOf("InterceptorBrain")).Length, Is.EqualTo(4));

        }

        [UnityTest]
        public IEnumerator ApproximatePlayerSoundUsesBeliefSearchWithoutFireAndExpires()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();
            var world = FindOne("WorldGenerator");
            world.GetType().GetField("seed", Members).SetValue(world, 1234);
            Call(world, "Generate");
            yield return null;
            var hub = FindOne("OutdoorInterceptorDispatch");
            var unit = hub.transform.Find(StationNames[1]).GetComponentInChildren(TypeOf("InterceptorBrain"));
            var scouts = FindOne("OutdoorScoutEncounter");
            var reporter = scouts.GetComponentInChildren(TypeOf("OutdoorScout"));
            var response = Read(unit, "DiagnosticResponse");
            var utility = Read(response, "Utility");
            var belief = Read(response, "Belief");
            var player = FindOne("PlayerController");
            var health = player.GetComponent(TypeOf("Health"));
            float initialHealth = (float)Read(health, "Current");
            var noiseType = TypeOf("NoiseEvent");
            var category = Enum.Parse(TypeOf("NoiseCategory"), "Gunshot");
            var evidenceType = TypeOf("InterceptorEvidence");
            var sourceType = TypeOf("InterceptorObservationSource");
            var graph = unit.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = graph.agentTypeID, areaMask = graph.areaMask };
            Vector3 emitted = default;
            Vector3 Mask(Vector3 v) => new(v.x, 0f, v.z);
            for (int i = 0; i < 8 && emitted == default; i++)
            {
                Vector3 probe = unit.transform.position + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 5f;
                if (!NavMesh.SamplePosition(probe, out var nav, .85f, filter) ||
                    !(bool)Call(scouts, "AllowsTarget", nav.position)) continue;
                float now = Time.time;
                object expired = Activator.CreateInstance(evidenceType, Members, null, new object[]
                {
                    Enum.Parse(sourceType, "ScoutReport"), nav.position, now - 7f, now - .1f, 2f, .8f, 99
                }, null);
                Call(hub, "OnScoutAlert", reporter, expired);
                Assert.That(Read(unit, "IsResponding"), Is.False, "An expired report cannot reserve a response slot.");
                object noise = Activator.CreateInstance(noiseType, nav.position, category, 1f, now, player);
                if ((bool)Call(Read(unit, "Hearing"), "ObserveAt", noise, now)) emitted = nav.position;
                else yield return null;
            }
            Assert.That(emitted, Is.Not.EqualTo(Vector3.zero), "The existing hearing sensor should accept a nearby emission.");

            string Action()
            {
                object choice = Read(utility, "Current");
                return choice.GetType().GetField("Action", Members).GetValue(choice).ToString();
            }

            using var profiler = ProfilerRecorder.StartNew(ProfilerCategory.Scripts,
                "ShiftFour.Interceptor.TacticalPlan", 1024, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            int peakRoutes = 0;
            bool heard = false, checkedApproximation = false;
            float deadline = Time.time + 10f;
            while (Time.time < deadline)
            {
                string current = Action();
                peakRoutes = Mathf.Max(peakRoutes, (int)Read(response, "TacticalRouteQueries"));
                Assert.That(current, Is.Not.EqualTo("Attack"));
                heard |= current == "InvestigateSound";
                if (heard && !checkedApproximation)
                {
                    var regions = ((IEnumerable)Read(belief, "Regions")).Cast<object>().ToArray();
                    if (regions.Length > 0)
                    {
                        float sum = regions.Sum(region => (float)region.GetType().GetField("Probability", Members).GetValue(region));
                        Assert.That(sum, Is.EqualTo(1f).Within(.001f));
                        foreach (var region in regions)
                        {
                            object evidence = region.GetType().GetField("Evidence", Members).GetValue(region);
                            Assert.That(evidence.GetType().GetField("Source", Members).GetValue(evidence).ToString(),
                                Is.EqualTo("ApproximateSound"));
                            var estimate = (Vector3)evidence.GetType().GetField("Position", Members).GetValue(evidence);
                            Assert.That(Mask(estimate - emitted).magnitude, Is.GreaterThan(.1f));
                        }
                        checkedApproximation = true;
                    }
                }
                if (heard && Read(unit, "ResponsePhase").ToString() is "Return" or "Patrol") break;
                yield return null;
            }
            profiler.Stop();
            var samples = profiler.Valid ? profiler.ToArray().Where(sample => sample.Value > 0).ToArray()
                : Array.Empty<ProfilerRecorderSample>();
            if (samples.Length > 0)
                TestContext.Out.WriteLine($"F2 sound response: {samples.Length} timed planning samples, " +
                    $"{samples.Average(sample => sample.Value) / 1000d:F1} µs mean, " +
                    $"{samples.Max(sample => sample.Value) / 1000d:F1} µs max, {peakRoutes} peak route queries per decision.");
            Assert.That(heard && checkedApproximation, Is.True);
            Assert.That(Read(unit, "IsResponding"), Is.False, "The sound-only response must end.");
            Assert.That(Read(Read(unit, "Policy"), "LastObservedAt"), Is.EqualTo(float.NegativeInfinity));
            Assert.That(Read(health, "Current"), Is.EqualTo(initialHealth));
        }

        [UnityTest]
        public IEnumerator TacticalMemoryUsesActualSightAndResetsForAllFourStations()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();

            var world = FindOne("WorldGenerator");
            var seed = world.GetType().GetField("seed", Members);
            seed.SetValue(world, 1234);
            Call(world, "Generate");
            yield return null;

            var hub = FindOne("OutdoorInterceptorDispatch");
            var unit = hub.transform.Find(StationNames[1]).GetComponentInChildren(TypeOf("InterceptorBrain"));
            foreach (Behaviour drone in hub.GetComponentsInChildren(TypeOf("InterceptorBrain")))
                if (drone != unit) drone.enabled = false;
            var scouts = FindOne("OutdoorScoutEncounter");
            foreach (Behaviour scout in scouts.GetComponentsInChildren(TypeOf("OutdoorScout")))
                scout.enabled = false;

            var player = FindOne("PlayerController");
            var capsule = player.GetComponent<CharacterController>();
            var diagnostic = Read(unit, "DiagnosticResponse");
            var learning = Read(diagnostic, "Learning");
            var memory = learning.GetType().GetField("Memory", Members).GetValue(learning);
            var sensor = TypeOf("DroneBrain").GetMethod("CanSeePlayer", Members);
            Vector3 approach = ((IEnumerable)Read(unit, "ResponseJunctions")).Cast<Vector3>()
                .Where(point => Vector3.Distance(unit.transform.position, point) is > 25.5f and < 33f)
                .FirstOrDefault(point =>
                {
                    capsule.enabled = false;
                    player.transform.position = point + Vector3.up * .2f;
                    capsule.enabled = true;
                    Call(player, "ApplyCameraPose");
                    Physics.SyncTransforms();
                    return (bool)sensor.Invoke(unit, null);
                });
            Assert.That(approach, Is.Not.EqualTo(Vector3.zero));

            void Move(Vector3 direction)
            {
                capsule.Move((direction * 3f + Vector3.down * 4f) * Time.deltaTime);
                Physics.SyncTransforms();
            }

            Vector3 heading = Mathf.Sign(unit.transform.position.z - approach.z) * Vector3.forward;
            float deadline = Time.time + 5f;
            while ((int)Read(learning, "Pending") == 0 && Time.time < deadline)
            {
                Move(heading);
                yield return null;
            }
            Assert.That(Read(learning, "Pending"), Is.GreaterThan(0),
                "A visible interception actually accepted by the squad should create a forecast.");
            var policy = Read(unit, "Policy");

            var markers = new[] { "ShiftFour.InterceptorLearning", "ShiftFour.PredictionEvaluation", "ShiftFour.TacticalMemory" };
            var profiles = markers.Select(marker => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, marker,
                8192, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame)).ToArray();
            for (int leg = 0; leg < 4; leg++)
            {
                Vector3 movement = leg switch { 0 => Vector3.zero, 1 => -heading, 2 => heading, _ => Vector3.left };
                float until = Time.time + .64f;
                while (Time.time < until)
                {
                    Move(movement);
                    yield return null;
                }
            }
            Assert.That(Read(learning, "Stopping"), Is.GreaterThan(0f));
            Assert.That(Read(learning, "DirectionChanges"), Is.GreaterThan(0f));
            Assert.That(Read(learning, "Reversals"), Is.GreaterThan(0f));
            deadline = Time.time + 3.5f;
            while ((int)Read(learning, "Evaluated") == 0 && Time.time < deadline)
            {
                Move(Vector3.zero);
                yield return null;
            }
            Assert.That(Read(learning, "Evaluated"), Is.GreaterThan(0),
                $"A later time-compatible sighting should evaluate the prediction. Pending {Read(learning, "Pending")}, " +
                $"unevaluated {Read(learning, "Unevaluated")}, sight {sensor.Invoke(unit, null)}.");

            var cover = new GameObject("F4 observation cover").AddComponent<BoxCollider>();
            try
            {
                cover.size = new Vector3(3f, 4f, 3f);
                Vector3 eye = (Vector3)Read(player, "EyePosition");
                cover.transform.position = Vector3.Lerp(unit.transform.position + Vector3.up * 2f, eye, .52f);
                Physics.SyncTransforms();
                Assert.That(sensor.Invoke(unit, null), Is.False);
                deadline = Time.time + .9f;
                Vector3 confirmed = (Vector3)Read(policy, "LastObserved");
                while (Time.time < deadline && (bool)diagnostic.GetType().GetField("sightLastTick", Members).GetValue(diagnostic))
                    yield return null;
                Assert.That(Read(policy, "LastObserved"), Is.EqualTo(confirmed));
                Assert.That(diagnostic.GetType().GetField("sightLastTick", Members).GetValue(diagnostic), Is.False);
            }
            finally
            {
                cover.enabled = false;
                Object.Destroy(cover.gameObject);
                Physics.SyncTransforms();
            }

            string Outcome(object entry) => entry.GetType().GetField("Outcome", Members).GetValue(entry).ToString();
            deadline = Time.time + 2f;
            bool reacquired = false;
            while (!reacquired && Time.time < deadline)
            {
                yield return null;
                reacquired = ((IEnumerable)Read(memory, "Entries")).Cast<object>()
                    .Any(entry => Outcome(entry) == "SuccessfulReacquisition");
            }
            Assert.That(reacquired, Is.True, "Removing physical cover should allow a genuine visual reacquisition. " +
                $"Sensor {sensor.Invoke(unit, null)}, decision {Read(unit, "SensorDecision")}, phase {Read(unit, "ResponsePhase")}.");

            for (int index = 0; index < profiles.Length; index++)
            {
                var recorder = profiles[index];
                recorder.Stop();
                var samples = recorder.Valid ? recorder.ToArray().Where(sample => sample.Value > 0).ToArray()
                    : Array.Empty<ProfilerRecorderSample>();
                if (samples.Length > 0)
                    TestContext.Out.WriteLine($"F4 Play {markers[index]}: {samples.Length} frames, " +
                        $"{samples.Average(sample => sample.Value) / 1000d:F2} µs mean, " +
                        $"{samples.Max(sample => sample.Value) / 1000d:F2} µs peak.");
                recorder.Dispose();
            }

            var panel = hub.GetComponent(TypeOf("OutdoorInterceptorDiagnostics"));
            Call(panel, "Follow", unit);
            string adaptive = (string)Call(panel, "ReadAdaptive");
            Assert.That(adaptive, Does.Contain("ADAPTIVE MEMORY").And.Contain("RELIABILITY"));
            Assert.That(adaptive, Does.Contain($"Evaluated {Read(learning, "Evaluated")}"));
            Call(panel, "Follow", null as Component);

            void CheckFreshStations()
            {
                var stations = hub.GetComponentsInChildren(TypeOf("InterceptorBrain"));
                Assert.That(stations.Length, Is.EqualTo(4));
                foreach (var station in stations)
                {
                    var model = Read(Read(station, "DiagnosticResponse"), "Learning");
                    Assert.That(Read(model, "Evaluated"), Is.Zero);
                    Assert.That(Read(model, "Pending"), Is.Zero);
                    Assert.That((IEnumerable)Read(model.GetType().GetField("Memory", Members).GetValue(model), "Entries"), Is.Empty);
                }
            }
            foreach (int value in new[] { 5678, 9012, 1234 })
            {
                seed.SetValue(world, value);
                Call(world, "Generate");
                yield return null;
                Assert.That(hub == null, Is.True);
                hub = FindOne("OutdoorInterceptorDispatch");
                CheckFreshStations();
            }

            var menu = FindOne("OutdoorMissionMenu");
            Call(menu, "Pause");
            Call(menu, "RestartMission");
            deadline = Time.realtimeSinceStartup + 12f;
            while (hub != null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(hub == null, Is.True);
            yield return null;
            hub = FindOne("OutdoorInterceptorDispatch");
            Assert.That(hub, Is.Not.Null);
            CheckFreshStations();
        }

        [UnityTest]
        public IEnumerator ScoutAlertRunsRealResponseWithoutInventingVisualEvidence()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();
            var world = FindOne("WorldGenerator");
            const int seed = 1234;
            world.GetType().GetField("seed", Members).SetValue(world, seed);
            Call(world, "Generate");
            yield return null;

            var hub = FindOne("OutdoorInterceptorDispatch");
            var scouts = FindOne("OutdoorScoutEncounter");
            var reporter = FindOne("OutdoorScout");
            var units = hub.GetComponentsInChildren(TypeOf("InterceptorBrain"));
            Assert.That(units.Length, Is.EqualTo(4));
            var diagnostics = hub.GetComponent(TypeOf("OutdoorInterceptorDiagnostics"));
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics.GetType().GetField("shown", Members).GetValue(diagnostics), Is.False);

            Vector3 report = default;
            bool found = false;
            foreach (var unit in units)
            {
                foreach (var post in (IEnumerable)Read(unit, "ResponseJunctions"))
                {
                    var p = (Vector3)post;
                    if (Vector3.Distance(unit.transform.position, p) < 9f) continue;
                    object[] args = { p, default(Vector3), 0f };
                    if (!(bool)Call(unit, "TryPlanResponse", args)) continue;
                    report = p;
                    found = true;
                    break;
                }
                if (found) break;
            }
            Assert.That(found, Is.True, "The seeded station needs a reachable road report.");
            Call(scouts, "PublishAlert", reporter, report);
            Call(scouts, "PublishAlert", reporter, report);
            var responding = units.Where(u => (bool)Read(u, "IsResponding")).ToArray();
            Assert.That(responding.Length, Is.EqualTo(1));
            var responder = responding[0];
            var policy = Read(responder, "Policy");
            var optics = responder.GetComponentInChildren(TypeOf("DroneRotorVisuals"));
            Vector3 start = responder.transform.position;
            float limit = Time.realtimeSinceStartup + 12;
            while (Vector3.Distance(start, responder.transform.position) < .75f && Time.realtimeSinceStartup < limit)
                yield return null;
            Assert.That(Vector3.Distance(start, responder.transform.position), Is.GreaterThanOrEqualTo(.75f));
            Assert.That(Read(policy, "LastObservedAt"), Is.EqualTo(float.NegativeInfinity));
            Assert.That(Read(optics, "AnimationReport").ToString(), Does.Match("Sensor (Alerted|Intercept|Search)"));
            Assert.That(Read(policy, "SelectedNode"), Is.EqualTo(-1));
            TestContext.Out.WriteLine($"Seed {seed}; {responder.name}; {Read(responder, "CurrentDecision")}; " +
                $"actual travel {Vector3.Distance(start, responder.transform.position):F2} m; report {report}; no visual ETA");

            var frame = optics.transform.Find("Interceptor IV airframe");
            string folder = Environment.GetEnvironmentVariable("SHIFTFOUR_INTERCEPTOR_CAPTURE");
            if (!string.IsNullOrEmpty(folder)) yield return CaptureStation(frame, folder, "intercept");
            var head = frame.Find("Scanning optical head");
            var rotation = head.localRotation;
            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                yield return null;
                Assert.That(head.localRotation, Is.EqualTo(rotation));
            }
            finally { Time.timeScale = previousTimeScale; }

            var health = responder.GetComponent(TypeOf("Health"));
            Call(health, "TakeDamage", 1f);
            float hitDeadline = Time.realtimeSinceStartup + 1f;
            while (Read(optics, "State").ToString() != "Damaged" && Time.realtimeSinceStartup < hitDeadline)
                yield return null;
            Assert.That(Read(optics, "State").ToString(), Is.EqualTo("Damaged"));
            if (!string.IsNullOrEmpty(folder)) yield return CaptureStation(frame, folder, "hit");
            Call(health, "TakeDamage", 10000f);
            float deadline = Time.time + 8f;
            while (Read(optics, "State").ToString() != "Disabled" && Time.time < deadline) yield return null;
            Assert.That(Read(optics, "DestructionCount"), Is.EqualTo(1));
            Assert.That(Read(optics, "State").ToString(), Is.EqualTo("Disabled"));
            var block = new MaterialPropertyBlock();
            head.Find("Rangefinder lenses").GetComponent<Renderer>().GetPropertyBlock(block);
            Assert.That(block.GetColor("_EmissionColor").maxColorComponent, Is.Zero);
            if (!string.IsNullOrEmpty(folder)) yield return CaptureStation(frame, folder, "shutdown");

            Call(world, "Generate");
            yield return null;
            Assert.That(diagnostics == null, Is.True);
            Assert.That(FindOne("OutdoorInterceptorDiagnostics"), Is.Not.Null);
            Assert.That(FindOne("OutdoorInterceptorDispatch").GetComponentsInChildren(TypeOf("InterceptorBrain")).Length, Is.EqualTo(4));
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) yield break;
            SceneManager.SetActiveScene(SceneManager.CreateScene("After outdoor Interceptor test"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif

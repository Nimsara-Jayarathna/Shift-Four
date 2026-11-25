#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace ShiftFour.Tests
{
    public sealed class InterceptorFinalValidationPlayModeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Prototype = "Assets/Scenes/OpenWorldPrototype.unity";
        private static string Output => Environment.GetEnvironmentVariable("SHIFTFOUR_F5_EVIDENCE");
        private static Type T(string n) => Type.GetType($"ShiftFour.{n}, Assembly-CSharp", true);
        private static Component Find(string n) => Object.FindFirstObjectByType(T(n)) as Component;
        private static object Read(object obj, string name) => obj.GetType().GetProperty(name, Members).GetValue(obj);
        private static FieldInfo Field(object obj, string name) => obj.GetType().GetField(name, Members);
        private static object Call(object obj, string name, params object[] args) =>
            obj.GetType().GetMethod(name, Members).Invoke(obj, args);
        private static void Write(string name, string text)
        {
            if (string.IsNullOrEmpty(Output)) return;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, name), text);
        }

        [UnityTest]
        public IEnumerator TacticalValidatorAllocationsAndSaturatedClearance()
        {
            var coordinates = new Vector3(5000f, 0f, 0f);
            var footprint = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(30f, .2f, 12f), area = 0, transform = Matrix4x4.Translate(coordinates + Vector3.down * .1f) };
            var settings = NavMesh.GetSettingsByIndex(0);
            var data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { footprint },
                new Bounds(coordinates, new Vector3(32f, 5f, 14f)), Vector3.zero, Quaternion.identity);
            var mesh = NavMesh.AddNavMeshData(data);
            var root = new GameObject("Tactical validation fixture");
            try
            {
                Vector3 a = coordinates + Vector3.left * 8f, b = coordinates + Vector3.right * 8f;
                var agentRoot = new GameObject("Probe drone");
                agentRoot.transform.SetParent(root.transform);
                agentRoot.transform.position = a;
                var agent = agentRoot.AddComponent<NavMeshAgent>();
                agent.agentTypeID = settings.agentTypeID;
                var graph = root.AddComponent(T("WaypointGraph"));
                var waypoints = new[] { new GameObject("A").transform, new GameObject("B").transform };
                for (int i = 0; i < waypoints.Length; i++) waypoints[i].SetParent(root.transform);
                waypoints[0].position = a;
                waypoints[1].position = b;
                var edges = Array.CreateInstance(T("GraphEdge"), 1);
                edges.SetValue(Activator.CreateInstance(T("GraphEdge"), 0, 1, -1), 0);
                Call(graph, "Configure", waypoints, edges);
                Call(graph, "SetConnectionValidator", (Func<Vector3, Vector3, bool>)((_, _) => true));
                yield return null;
                Assert.That(agent.Warp(a), Is.True);
                var validator = Activator.CreateInstance(T("ControlTacticalRouteValidator"), agent, graph, .04f);
                Action begin = (Action)validator.GetType().GetMethod("BeginDecision").CreateDelegate(typeof(Action), validator);
                Action end = (Action)validator.GetType().GetMethod("EndDecision").CreateDelegate(typeof(Action), validator);
                Func<Vector3, Vector3, Vector3[]> route = (Func<Vector3, Vector3, Vector3[]>)
                    validator.GetType().GetMethod("Route").CreateDelegate(typeof(Func<Vector3, Vector3, Vector3[]>), validator);
                Vector3[] Query()
                {
                    begin();
                    var path = route(a, b);
                    var cached = route(a, b);
                    end();
                    return ReferenceEquals(path, cached) ? path : null;
                }
                for (int i = 0; i < 16; i++) Assert.That(Query().Length, Is.EqualTo(2));
                bool previous = Profiler.enabled;
                Profiler.enabled = true;
                try
                {
                    using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 65536,
                        ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                    var calibrate = new byte[4096];
                    long start = Stopwatch.GetTimestamp(), peak = 0;
                    for (int i = 0; i < 500; i++)
                    {
                        long tick = Stopwatch.GetTimestamp();
                        if (Query() == null) throw new InvalidOperationException("Clear route vanished");
                        peak = Math.Max(peak, Stopwatch.GetTimestamp() - tick);
                    }
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    gc.Stop();
                    GC.KeepAlive(calibrate);
                    Assert.That(gc.Valid && !gc.WrappedAround, Is.True);
                    long events = Enumerable.Range(0, gc.Count).Sum(i => gc.GetSample(i).Count);
                    Assert.That(events, Is.GreaterThan(0L));
                    Write("ROUTE_VALIDATION.csv", "decisions,cached_repeats,mean_us,peak_us,allocation_events\n" +
                        FormattableString.Invariant($"500,500,{elapsed * 1e6 / Stopwatch.Frequency / 500:F4},{peak * 1e6 / Stopwatch.Frequency:F4},{events - 1}\n"));
                }
                finally { Profiler.enabled = previous; }

                var moving = agentRoot.GetComponent<NavMeshAgent>();
                moving.isStopped = true;
                var obstacle = new GameObject("External obstacle").AddComponent<BoxCollider>();
                obstacle.transform.SetParent(root.transform);
                obstacle.transform.position = Vector3.Lerp(a, b, .5f) + Vector3.up;
                obstacle.size = new Vector3(.4f, 2f, 2f);
                // Self-owned colliders along the sweep saturate a small query buffer.
                for (int i = 0; i < 40; i++)
                {
                    var own = new GameObject("Self fixture collider").AddComponent<BoxCollider>();
                    own.transform.SetParent(agentRoot.transform);
                    own.transform.position = obstacle.transform.position;
                    own.size = obstacle.size;
                }
                Physics.SyncTransforms();
                Assert.That(Query(), Is.Null, "A saturated sweep must still reject an external obstruction.");
                obstacle.transform.position = b + Vector3.up;
                foreach (Transform child in agentRoot.transform) child.position = obstacle.transform.position;
                Physics.SyncTransforms();
                Assert.That(Query(), Is.Null, "A saturated destination overlap must still reject an external obstruction.");
                obstacle.enabled = false;
                Physics.SyncTransforms();
                Assert.That(Query(), Is.Not.Null, "The same route recovers and ignores self-owned geometry.");
            }
            finally { Object.Destroy(root); mesh.Remove(); Object.Destroy(data); }
        }

        [UnityTest]
        public IEnumerator SeededFleetUsesOneDashboardAndRecordsLiveProfilerOutput()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Prototype, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();
            var world = Find("WorldGenerator");
            var player = Find("PlayerController");
            var cc = player.GetComponent<CharacterController>();
            var sensor = T("DroneBrain").GetMethod("CanSeePlayer", Members);
            var metrics = new StringBuilder("seed,interceptors,total_brains,max_responders,max_candidates,max_combinations,max_group_utility,assignment_updates,reassignments,conflict_frames\n");
            var profile = new StringBuilder("seed,marker,positive_frames,mean_us,peak_us\n");
            string[] markers =
            {
                "Decision", "FindRoute", "CanConnect", "DroneMotor", "DroneBody", "DroneAnimation",
                "Interceptor.TacticalPlan", "Interceptor.RouteValidation", "InterceptorCoordination",
                "InterceptorAssignment", "PredictionEvaluation", "TacticalMemory"
            };
            foreach (int seed in new[] { 1234, 5678, 9012 })
            {
                Field(world, "seed").SetValue(world, seed);
                Call(world, "Generate");
                yield return null;
                var squad = Find("OutdoorInterceptorDispatch");
                var fleet = squad.GetComponentsInChildren(T("InterceptorBrain"));
                var brains = Object.FindObjectsByType(T("DroneBrain"));
                Assert.That(fleet, Has.Length.EqualTo(4));
                Assert.That(brains, Has.Length.EqualTo(26));
                Assert.That(fleet.Cast<Behaviour>().All(unit => unit.isActiveAndEnabled), Is.True);
                var panel = squad.GetComponents(T("OutdoorInterceptorDiagnostics")).Single();
                Assert.That(Field(panel, "shown").GetValue(panel), Is.False);
                var central = (Component)Call(squad, "Unit", 1);

                bool PlaceVisible(Vector3 point)
                {
                    cc.enabled = false;
                    player.transform.position = point + Vector3.up * .2f;
                    cc.enabled = true;
                    Call(player, "ApplyCameraPose");
                    Physics.SyncTransforms();
                    return (bool)sensor.Invoke(central, null);
                }
                bool found = ((IEnumerable)Read(central, "ResponseJunctions")).Cast<Vector3>()
                    .Where(point => Vector3.Distance(central.transform.position, point) is >= 20f and <= 33f)
                    .Any(PlaceVisible);
                if (!found)
                {
                    var agent = central.GetComponent<NavMeshAgent>();
                    var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                    foreach (float distance in new[] { 21f, 15f, 27f, 11f })
                    {
                        foreach (float yaw in new[] { 0f, 11f, -11f, 22f, -22f, 33f, -33f })
                        {
                            Vector3 point = central.transform.position +
                                Quaternion.Euler(0, yaw, 0) * central.transform.forward * distance;
                            if (!NavMesh.SamplePosition(point, out var hit, 1f, filter) || !PlaceVisible(hit.position)) continue;
                            found = true;
                            break;
                        }
                        if (found) break;
                    }
                }
                Assert.That(found, Is.True, $"Seed {seed} needs a real clear sightline from the central unit.");
                Vector3 heading = Mathf.Sign(central.transform.position.z - player.transform.position.z) * Vector3.forward;
                float deadline = Time.time + 2.5f;
                while (!(bool)Read(central, "IsResponding") && Time.time < deadline) yield return null;
                Assert.That(Read(central, "IsResponding"), Is.True);

                Call(panel, "Follow", central);
                var pageField = Field(panel, "page");
                foreach (var page in Enum.GetValues(pageField.FieldType))
                {
                    pageField.SetValue(panel, page);
                    string output = (string)Call(panel, "ReadPage");
                    Assert.That(output, Is.Not.Empty);
                    if (page.ToString() == "Overview") Assert.That(output, Does.Contain("Deployed 4/4"));
                    if (page.ToString() == "Controller") Assert.That(output, Does.Contain("Replans"));
                    if (page.ToString() == "Performance") Assert.That(output, Does.Contain("RouteValidation"));
                }

                var counters = markers.Select(name => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "ShiftFour." + name,
                    8192, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame)).ToArray();
                var coordinator = Read(squad, "Coordinator");
                int maxActive = 0, maxProposals = 0, maxCombinations = 0, conflicts = 0;
                float bestGroup = 0f;
                deadline = Time.time + 3f;
                while (Time.time < deadline)
                {
                    cc.Move((heading * 3f + Vector3.down * 4f) * Time.deltaTime);
                    yield return null;

                    maxActive = Math.Max(maxActive, (int)Read(squad, "ActiveCount"));
                    maxCombinations = Math.Max(maxCombinations, (int)Read(coordinator, "Evaluations"));
                    bestGroup = Mathf.Max(bestGroup, (float)Read(coordinator, "GroupUtility"));
                    int count = 0;
                    foreach (ICollection row in (IEnumerable)Read(squad, "Matrix")) count += row.Count;
                    maxProposals = Math.Max(maxProposals, count);
                    var reservations = ((IEnumerable)Read(coordinator, "Reservations")).Cast<object>()
                        .Where(lease => (bool)Call(lease, "Active", Time.time)).ToArray();
                    if (reservations.Length == 2)
                    {
                        object x = Field(reservations[0], "Task").GetValue(reservations[0]);
                        object y = Field(reservations[1], "Task").GetValue(reservations[1]);
                        var a = (Vector3)Field(x, "Destination").GetValue(x);
                        var b = (Vector3)Field(y, "Destination").GetValue(y);
                        if (Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)) < 4f) conflicts++;
                    }
                }
                Assert.That(maxActive, Is.InRange(1, 2));
                Assert.That(conflicts, Is.Zero);
                metrics.AppendLine(FormattableString.Invariant($"{seed},{fleet.Length},{brains.Length},{maxActive},{maxProposals},{maxCombinations},{bestGroup:F4},{Read(coordinator, "Updates")},{Read(coordinator, "Reassignments")},{conflicts}"));
                for (int i = 0; i < counters.Length; i++)
                {
                    counters[i].Stop();
                    var samples = counters[i].Valid ? counters[i].ToArray().Where(sample => sample.Value > 0).ToArray()
                        : Array.Empty<ProfilerRecorderSample>();
                    profile.AppendLine(samples.Length == 0 ? $"{seed},{markers[i]},0,N/A,N/A" :
                        FormattableString.Invariant($"{seed},{markers[i]},{samples.Length},{samples.Average(s => s.Value) / 1000d:F3},{samples.Max(s => s.Value) / 1000d:F3}"));
                    counters[i].Dispose();
                }
                if (seed == 1234)
                {
                    var details = new StringBuilder();
                    foreach (var page in Enum.GetValues(pageField.FieldType))
                    {
                        pageField.SetValue(panel, page);
                        Call(panel, "Follow", central);
                        details.AppendLine((string)Call(panel, "ReadPage"));
                    }
                    Write("dashboard-capture.txt", details.ToString());
                    pageField.SetValue(panel, Enum.Parse(pageField.FieldType, "Overview"));
                    Field(panel, "shown").SetValue(panel, true);
                    yield return CaptureGameView(player, "outdoor-player-view");
                }
            }
            Write("COORDINATION_RESULTS.csv", metrics.ToString());
            Write("OUTDOOR_PROFILE.csv", profile.ToString());
            Write("environment.txt", $"Unity {Application.unityVersion}\n{SystemInfo.operatingSystem}\n{SystemInfo.processorType}, {SystemInfo.systemMemorySize} MB\n{SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}\n{Screen.width} x {Screen.height}\n");
        }

        private static IEnumerator CaptureGameView(Component player, string name)
        {
            if (string.IsNullOrEmpty(Output)) yield break;
            var view = (Camera)Read(player, "ViewCamera");
            var previous = view.targetTexture;
            var oldActive = RenderTexture.active;
            var render = new RenderTexture(960, 540, 24);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                view.targetTexture = render;
                yield return null;
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                pixels.Apply();
                using var output = File.Create(Path.Combine(Output, name + ".ppm"));
                byte[] header = Encoding.ASCII.GetBytes("P6\n960 540\n255\n");
                output.Write(header, 0, header.Length);
                byte[] data = pixels.GetRawTextureData();
                for (int row = 539; row >= 0; row--) output.Write(data, row * 2880, 2880);
            }
            finally
            {
                view.targetTexture = previous;
                RenderTexture.active = oldActive;
                Object.Destroy(render);
                Object.Destroy(pixels);
            }
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Prototype) yield break;
            SceneManager.SetActiveScene(SceneManager.CreateScene("After final outdoor validation"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif

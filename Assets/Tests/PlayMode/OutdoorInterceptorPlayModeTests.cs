#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShiftFour.Tests
{
    public sealed class OutdoorInterceptorPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/OpenWorldPrototype.unity";
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
            foreach (int seed in new[] { 1234, 5678 })
            {
                world.GetType().GetField("seed", Members).SetValue(world, seed);
                Call(world, "Generate");
                yield return null;
                Assert.That(old == null, Is.True);
                var units = FindOne("OutdoorInterceptorDispatch").GetComponentsInChildren(TypeOf("InterceptorBrain"));
                Assert.That(units.Length, Is.EqualTo(3));
                foreach (var unit in units)
                {
                    var visual = unit.GetComponentInChildren(TypeOf("DroneRotorVisuals"));
                    var frame = visual.transform.Find("Interceptor IV airframe");
                    Assert.That(frame, Is.Not.Null);
                    Assert.That(frame.GetComponentsInChildren<Collider>(), Is.Empty);
                    Assert.That(Read(visual, "AnimationReport").ToString(), Does.Contain("Sensor Standby"));
                    var body = unit.GetComponent(TypeOf("DroneBody"));
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
            Assert.That(units.Length, Is.EqualTo(3));
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
            Assert.That(FindOne("OutdoorInterceptorDispatch").GetComponentsInChildren(TypeOf("InterceptorBrain")).Length, Is.EqualTo(3));
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

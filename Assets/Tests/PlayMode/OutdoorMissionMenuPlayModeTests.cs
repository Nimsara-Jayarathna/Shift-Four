#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShiftFour.Tests
{
    public sealed class OutdoorMissionMenuPlayModeTests
    {
        private const string Path = "Assets/Scenes/OpenWorldPrototype.unity";
        private static Type T(string name) => Type.GetType("ShiftFour." + name + ", Assembly-CSharp", true);
        private static Component Single(string type) => Object.FindObjectsByType(T(type), FindObjectsSortMode.None).Cast<Component>().Single();
        private static Component Menu => Single("OutdoorMissionMenu");
        private static object Read(object source, string key) => source.GetType().GetProperty(key).GetValue(source);
        private static void Press(string action) => Menu.GetType().GetMethod(action).Invoke(Menu, null);
        private static string State => Read(Menu, "Screen").ToString();

        internal static IEnumerator StartMission()
        {
            Assert.That(State, Is.EqualTo("Title"));
            Press("Play");
            yield return Await(() => State == "Playing");
        }

        private static IEnumerator Await(Func<bool> condition, float timeout = 12f)
        {
            float limit = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(limit), "Menu transition timed out");
                yield return null;
            }
        }

        private float originalScale;
        private bool originalAudioPause;

        [UnitySetUp]
        public IEnumerator Load()
        {
            originalScale = Time.timeScale;
            originalAudioPause = AudioListener.pause;
            Time.timeScale = .75f;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Scene current = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("After outdoor menu test"));
            if (current.path == Path) yield return SceneManager.UnloadSceneAsync(current);
            Time.timeScale = originalScale;
            AudioListener.pause = originalAudioPause;
        }

        [UnityTest]
        public IEnumerator TitlePauseSettingsAndResumePreserveTheLiveSession()
        {
            var player = Single("PlayerController");
            var camera = player.GetComponentInChildren<Camera>();
            var health = player.GetComponent(T("Health"));
            Assert.That(State, Is.EqualTo("Title"));
            Assert.That(camera.transform.position.y - player.transform.position.y, Is.GreaterThan(5f));
            Assert.That(Read(player, "CanAct"), Is.False);
            Assert.That(Read(Read(player, "Weapons"), "ActiveWeapon"), Is.Null);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Press("Settings"); Assert.That(State, Is.EqualTo("Settings"));
            Press("Back"); yield return StartMission();
            Assert.That(Time.timeScale, Is.EqualTo(.75f));
            Assert.That(Read(player, "CanAct"), Is.True);
            Assert.That(Vector3.Distance(camera.transform.position, player.transform.position), Is.LessThan(2f));

            Press("Pause"); Press("Settings");
            var positions = Object.FindObjectsByType(T("DroneBrain"), FindObjectsSortMode.None).Cast<Component>()
                .Select(d => (d.transform, d.transform.position)).ToArray();
            Assert.That(positions.Length, Is.EqualTo(26));
            var hp = Read(health, "Current");
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(Read(player, "CanAct"), Is.False);
            Assert.That(Read(health, "Current"), Is.EqualTo(hp));
            foreach (var (root, position) in positions)
                Assert.That(Vector3.Distance(root.position, position), Is.LessThan(.02f), root.name);
            Press("Back"); Assert.That(State, Is.EqualTo("Paused"));
            Press("Play"); Press("Play"); yield return Await(() => State == "Playing");
            Assert.That(Time.timeScale, Is.EqualTo(.75f));
            Assert.That(AudioListener.pause, Is.EqualTo(originalAudioPause));
            health.GetType().GetMethod("TakeDamage").Invoke(health, new object[] { 100000f });
            Press("Pause"); Assert.That(State, Is.EqualTo("Dying"));
        }

        [UnityTest]
        public IEnumerator BriefingsFreezeInputAndFollowEachGeneratedLayout()
        {
            Press("OpenMap"); Press("OpenMissions");
            Assert.That(State, Is.EqualTo("Title"));
            yield return StartMission();
            var generator = Single("WorldGenerator");
            var player = Single("PlayerController");
            var feedback = Single("PrototypePlayerFeedback");
            object map = feedback.GetType().GetProperty("Map", Internal).GetValue(feedback);
            var project = map.GetType().GetMethod("Project", Internal);
            var marker = map.GetType().GetMethod("Marker", Internal);
            var siteType = marker.GetParameters()[0].ParameterType;
            var frame = new Rect(20, 30, 400, 400);
            Vector2 ToMap(Vector3 pos) => (Vector2)project.Invoke(map, new object[] { pos, frame });
            foreach (int seed in new[] { 1234, 5678, 9012 })
            {
                generator.GetType().GetField("seed", Internal).SetValue(generator, seed);
                generator.GetType().GetMethod("Generate").Invoke(generator, null);
                yield return null;
                var world = generator.transform.Find("GeneratedWorld");
                var ground = world.Find("Ground").GetComponent<BoxCollider>().bounds;
                Assert.That(ToMap(ground.center), Is.EqualTo(frame.center));
                Assert.That(ToMap(ground.center + Vector3.forward).y, Is.LessThan(frame.center.y));
                Assert.That(ToMap(ground.center + Vector3.right).x, Is.GreaterThan(frame.center.x));
                foreach (var site in new[] { "Checkpoint", "Server", "Control", "Storage", "Supply", "Extraction" })
                {
                    var pin = (Transform)marker.Invoke(map, new[] { Enum.Parse(siteType, site) });
                    string path = site == "Supply" ? "OutdoorSupplyCache01" : site == "Extraction" ? "Final Extraction Gate" : "Buildings/" + site;
                    Assert.That(pin, Is.EqualTo(world.Find(path)));
                    Assert.That(frame.Contains(ToMap(pin.position)), Is.True);
                }
                var current = feedback.GetType().GetProperty("CurrentSite", Internal);
                Assert.That(current.GetValue(feedback).ToString(), Is.EqualTo("Supply"));
                foreach (var screen in new[] { "Map", "Missions" })
                {
                    Press("Open" + screen); Press("Pause");
                    Assert.That(State, Is.EqualTo(screen));
                    Assert.That(Time.timeScale, Is.Zero);
                    Assert.That(Read(player, "CanAct"), Is.False);
                    Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
                    yield return null;
                    Press("Play");
                    yield return Await(() => State == "Playing");
                    Assert.That(Time.timeScale, Is.EqualTo(.75f));
                    Assert.That(Read(player, "CanAct"), Is.True);
                }
                Interact(Single("OutdoorSupplyCache"));
                Assert.That(current.GetValue(feedback).ToString(), Is.EqualTo("Checkpoint"));
            }
        }

        [UnityTest]
        public IEnumerator MissionNoticesFollowAcceptedInteractionsAndResetWithTheLayout()
        {
            yield return StartMission();
            var hud = Single("PrototypePlayerFeedback");
            var generator = Single("WorldGenerator");
            var active = hud.GetType().GetProperty("ActiveNotice", Internal);
            var notices = (IList)hud.GetType().GetField("notices", Internal).GetValue(hud);
            string Field(string name)
            {
                object entry = active.GetValue(hud);
                return (string)entry.GetType().GetField(name, Internal).GetValue(entry);
            }
            void Expect(string title, string detail = null, string next = null)
            {
                Assert.That(Field("Title"), Is.EqualTo(title));
                if (detail != null) Assert.That(Field("Detail"), Does.Contain(detail));
                if (next != null) Assert.That(Field("Next"), Does.Contain(next));
            }
            void Expire() => hud.GetType().GetMethod("AdvanceNotice", Internal).Invoke(hud, new object[] { 3f });
            foreach (int seed in new[] { 1234, 5678, 9012 })
            {
                generator.GetType().GetField("seed", Internal).SetValue(generator, seed);
                generator.GetType().GetMethod("Generate").Invoke(generator, null);
                yield return null;
                Assert.That(notices, Is.Empty);
                Interact(Single("OutdoorSupplyCache"));
                Expect("SUPPLIES COLLECTED", next: "Checkpoint");
                Expire();
                var reader = Single("CheckpointSecurityScanner");
                Interact(reader);
                Assert.That(notices, Is.Empty);
                Interact(Single("SecurityAccessCard"));
                Expect("CREDENTIAL ACQUIRED");
                Interact(reader); Interact(reader);
                Expect("CHECKPOINT SECURED", next: "Server");
                Assert.That(notices.Count, Is.EqualTo(1));
                Press("OpenMap");
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That((float)hud.GetType().GetProperty("NoticeOpacity", Internal).GetValue(hud), Is.Zero);
                Press("Play");
                yield return Await(() => State == "Playing");
                Expire();
                var terminals = Object.FindObjectsByType(T("ServerTerminal"), FindObjectsSortMode.None)
                    .Cast<Component>().OrderBy(t => t.name).ToArray();
                Interact(terminals[1]);
                Expect("SERVER 2 ONLINE", "1 / 3");
                Interact(terminals[2]);
                Expect("INCORRECT SEQUENCE", "0 / 3");
                Interact(terminals[1]); Interact(terminals[1]); Interact(terminals[0]);
                Expect("SERVER 1 ONLINE", "2 / 3");
                Interact(terminals[2]);
                Expect("SERVER NETWORK RESTORED", "3 / 3", "Control");
                var panel = Single("ControlLockdownMission");
                Interact(panel);
                Assert.That(notices.Count, Is.EqualTo(1));
                int count = 0;
                foreach (Component item in Object.FindObjectsByType(T("LockdownSecuritySwitch"), FindObjectsSortMode.None))
                {
                    Interact(item); Interact(item);
                    if (++count == 1) Expire(); // The Server completion has priority over partial progress.
                    Expect(count == 3 ? "OVERRIDE AUTHORIZED" : "SECURITY SUBSYSTEM DISABLED", $"{count} / 3");
                }
                Interact(panel); Interact(panel);
                Expect("CONTROL SYSTEMS RESTORED", next: "Storage");
                Expire();
                count = 0;
                foreach (Component item in Object.FindObjectsByType(T("PowerCellPickup"), FindObjectsSortMode.None))
                {
                    Interact(item); Interact(item);
                    count++;
                    Expect(count == 2 ? "GENERATOR READY" : "POWER CELL COLLECTED", $"{count} / 2");
                }
                Interact(Single("StoragePowerMission"));
                Expect("GENERATOR ONLINE", next: "designated");
                Assert.That(notices.Count, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator OutdoorVitalsFollowRealDamageAndRecovery()
        {
            yield return StartMission();
            var health = Single("PlayerController").GetComponent(T("Health"));
            var hud = Single("CombatHud");
            float maximum = (float)Read(health, "Maximum");
            Assert.That(Read(hud, "DisplayedHealthFraction"), Is.EqualTo(1f).Within(.001f));
            health.GetType().GetMethod("TakeDamage").Invoke(health, new object[] { 40f });
            yield return null;
            Assert.That(Read(hud, "PlayerHealthFraction"), Is.EqualTo((maximum - 40) / maximum).Within(.001f));
            Assert.That((float)Read(hud, "DamageTrailFraction"), Is.GreaterThan((float)Read(hud, "DisplayedHealthFraction")));
            yield return Await(() => (bool)Read(hud, "IsRecovering"));
            Assert.That((float)Read(health, "Current"), Is.GreaterThan(maximum - 40f));
        }

        private static void Interact(Component target) => target.GetType().GetMethod("Interact").Invoke(target, null);
        private static void Damage(Component health) => health.GetType().GetMethod("TakeDamage").Invoke(health, new object[] { 10000f });
        private const System.Reflection.BindingFlags Internal = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private static int Subscriptions(Component source, string eventName)
        {
            var listeners = (Delegate)source.GetType().GetField(eventName, Internal).GetValue(source);
            return listeners.GetInvocationList().Count(listener => ReferenceEquals(listener.Target, Menu));
        }

        private static readonly bool[] RestartChoices = { true, false };

        [UnityTest]
        public IEnumerator DeathFinishesCameraFallBeforeResults([ValueSource(nameof(RestartChoices))] bool retry)
        {
            yield return StartMission();
            var player = Single("PlayerController");
            var health = player.GetComponent(T("Health"));
            var camera = player.GetComponentInChildren<Camera>();
            Interact(Single("OutdoorSupplyCache"));
            Interact(Single("SecurityAccessCard"));
            var reader = Single("CheckpointSecurityScanner");
            Interact(reader);
            Assert.That(reader.GetType().GetProperty("IsVerifying", Internal).GetValue(reader), Is.True);
            Vector3 root = player.transform.position;
            float eyeHeight = camera.transform.position.y;
            Assert.That(Subscriptions(health, "Died"), Is.EqualTo(1));
            Damage(health);
            Assert.That(State, Is.EqualTo("Dying"));
            Assert.That(Time.timeScale, Is.EqualTo(.75f));
            Assert.That(Read(player, "CanAct"), Is.False);
            Damage(health);
            Press("Pause"); Press("Settings");
            Assert.That(State, Is.EqualTo("Dying"));
            yield return Await(() => State == "GameOver");
            Assert.That(Read(player.GetComponent(T("PlayerDeathPresentation")), "Progress"), Is.EqualTo(1f));
            Assert.That(camera.transform.position.y, Is.LessThan(eyeHeight - .4f));
            Assert.That(player.transform.position, Is.EqualTo(root));
            Assert.That(player.GetComponent<CharacterController>().enabled, Is.True);
            Damage(health);
            Assert.That(Menu.transform.Find("Credits music"), Is.Null);
            yield return VerifyResultNavigation("GameOver", retry);
        }

        [UnityTest]
        public IEnumerator OnlyRealExtractionYieldsVictory([Values(1234, 5678, 9012, 3456)] int seed)
        {
            yield return StartMission();
            var generator = Single("WorldGenerator");
            generator.GetType().GetField("seed", Internal).SetValue(generator, seed);
            generator.GetType().GetMethod("Generate").Invoke(generator, null);
            yield return null;
            var gate = Single("OpenWorldExtractionGate");
            var mission = Single("OpenWorldMissionManager");
            Assert.That(Subscriptions(mission, "Extracted"), Is.EqualTo(1));
            Interact(gate);
            Assert.That(State, Is.EqualTo("Playing"));
            var feedback = Single("PrototypePlayerFeedback");
            string CurrentSite() => feedback.GetType().GetProperty("CurrentSite", Internal).GetValue(feedback).ToString();
            Assert.That(CurrentSite(), Is.EqualTo("Supply"));
            Interact(Single("OutdoorSupplyCache"));
            Assert.That(CurrentSite(), Is.EqualTo("Checkpoint"));
            Interact(Single("SecurityAccessCard")); Interact(Single("CheckpointSecurityScanner"));
            Assert.That(CurrentSite(), Is.EqualTo("Server"));
            var terminals = Object.FindObjectsByType(T("ServerTerminal"), FindObjectsSortMode.None)
                .Cast<Component>().OrderBy(t => t.name).ToArray();
            foreach (int i in new[] { 1, 0, 2 }) Interact(terminals[i]);
            Assert.That(CurrentSite(), Is.EqualTo("Control"));
            foreach (Component item in Object.FindObjectsByType(T("LockdownSecuritySwitch"), FindObjectsSortMode.None)) Interact(item);
            Interact(Single("ControlLockdownMission"));
            Assert.That(CurrentSite(), Is.EqualTo("Storage"));
            foreach (Component item in Object.FindObjectsByType(T("PowerCellPickup"), FindObjectsSortMode.None)) Interact(item);
            Interact(Single("StoragePowerMission"));
            Assert.That(CurrentSite(), Is.EqualTo("Extraction"));
            Assert.That(Read(mission, "BuildingMissionsCompleted"), Is.EqualTo(4));
            var world = Single("WorldGenerator").transform.Find("GeneratedWorld");
            foreach (string path in new[] { "OutdoorScoutPatrols/05 Checkpoint Approach", "Buildings/Server/IndoorGuards/Server Racks",
                "OutdoorFlankerAmbushes/03 Control to Storage" })
            {
                Interact(gate);
                Assert.That(Read(mission, "Escaped"), Is.False);
                Damage(world.Find(path).GetComponentInChildren(T("Health")));
            }
            Assert.That(Read(mission, "DronesDefeated"), Is.EqualTo(3));
            var player = Single("PlayerController");
            var capsule = player.GetComponent<CharacterController>();
            var camera = player.GetComponentInChildren<Camera>();
            var shutter = gate.transform.Find("Extraction shutter");
            var solid = shutter.GetComponentInChildren<BoxCollider>();
            capsule.enabled = false;
            player.transform.position = gate.transform.TransformPoint(new Vector3(0, .9f, -2.4f));
            capsule.enabled = true;
            player.GetType().GetMethod("ApplyCameraPose").Invoke(player, null);
            camera.transform.LookAt(gate.transform.TransformPoint(Vector3.up * 2f));
            Vector3 eye = camera.transform.localPosition;
            Quaternion rotation = camera.transform.localRotation;
            int mask = camera.cullingMask;
            float originalFog = RenderSettings.fogDensity;
            float fov = camera.fieldOfView;
            var hp = player.GetComponent(T("Health"));
            object health = Read(hp, "Current");
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit,
                3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(hit.collider.GetComponentInParent(gate.GetType()), Is.SameAs(gate));
            Assert.That(gate.GetComponentsInChildren<Light>().All(lamp => lamp.color.g > lamp.color.r), Is.True);

            Interact(gate); Interact(gate);
            Assert.That(Read(mission, "Escaped"), Is.True);
            Assert.That(State, Is.EqualTo("Ending"));
            var timeAtExtraction = Menu.GetType().GetProperty("ActiveTime", Internal).GetValue(Menu);
            Assert.That(Cursor.visible, Is.False);
            Press("Pause"); Press("OpenMap"); Press("OpenMissions");
            Assert.That(State, Is.EqualTo("Ending"));
            Assert.That(Read(player, "CanAct"), Is.False);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(shutter.localPosition.y, Is.Zero);
            Assert.That(camera.cullingMask & (1 << 2), Is.Zero);
            Assert.That(Object.FindObjectsByType<AudioListener>().Length, Is.EqualTo(1));
            var sources = gate.GetComponentsInChildren<AudioSource>().Where(a => a.ignoreListenerPause).ToArray();
            Assert.That(sources.Length, Is.EqualTo(2));
            Assert.That(sources.Single(a => a.name == "Extraction score").clip, Is.Not.Null);
            yield return Await(() => shutter.localPosition.y > .1f);
            Assert.That(shutter.localPosition.y, Is.LessThan(4f));
            Physics.SyncTransforms();
            Assert.That(solid.bounds.min.y, Is.EqualTo(gate.transform.position.y + shutter.localPosition.y).Within(.01f));

            if (seed == 9012)
            {
                generator.GetType().GetMethod("Generate").Invoke(generator, null);
                yield return null;
                Assert.That(State, Is.EqualTo("Playing"));
                Assert.That(Time.timeScale, Is.EqualTo(.75f));
                Assert.That(Read(player, "CanAct"), Is.True);
                Assert.That(camera.cullingMask, Is.EqualTo(mask));
                Assert.That(Single("OpenWorldExtractionGate").transform.Find("Extraction shutter").localPosition.y, Is.Zero);
                yield break;
            }
            var finishEnding = Menu.GetType().GetMethod("FinishEnding", Internal);
            var advanceCredits = Menu.GetType().GetMethod("AdvanceCredits", Internal);
            if (seed != 1234) finishEnding.Invoke(Menu, new object[] { true });
            else
            {
                yield return Await(() => (float)Menu.GetType().GetProperty("EndingTime", Internal).GetValue(Menu) > 8.1f);
                foreach (Transform site in world.Find("Buildings"))
                {
                    var point = camera.WorldToViewportPoint(site.position);
                    Assert.That(point.z, Is.Positive);
                    Assert.That(point.x, Is.InRange(0f, 1f));
                    Assert.That(point.y, Is.InRange(0f, 1f));
                }
                yield return Await(() => State == "Credits");
            }
            Assert.That(State, Is.EqualTo("Credits"));
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(Read(player, "CanAct"), Is.False);
            Assert.That(Cursor.visible, Is.False);
            var config = Menu.GetType().GetProperty("CreditsConfig", Internal).GetValue(Menu);
            Assert.That(config, Is.SameAs(Resources.Load("OutdoorCredits")));
            Assert.That(config.GetType().GetProperty("GroupLine", Internal).GetValue(config), Is.EqualTo("DEVELOPED BY GROUP - 26"));
            var roster = (Array)config.GetType().GetProperty("Team", Internal).GetValue(config);
            Assert.That(roster.Cast<object>().Select(m => m.GetType().GetField("name").GetValue(m)),
                Is.EqualTo(new[] { "PAMUDI", "NIMSARA", "NIMTHARA", "ASMADALA" }));
            var music = Menu.transform.Find("Credits music").GetComponent<AudioSource>();
            Assert.That(music.clip, Is.SameAs(Resources.Load<AudioClip>("Audio/BackgroundMusic")));
            Assert.That(music.ignoreListenerPause, Is.True);
            Assert.That(music.ignoreListenerVolume, Is.False);
            finishEnding.Invoke(Menu, new object[] { true });
            Interact(gate);
            Press("Play"); Press("Pause"); Press("MainMenu");
            Assert.That(Menu.GetComponentsInChildren<AudioSource>().Count(a => a.name == "Credits music"), Is.EqualTo(1));
            if (seed == 5678)
            {
                void Tick(bool down, bool held) => advanceCredits.Invoke(Menu, new object[] { .016f, down, held });
                Tick(true, true); // Space that cut the extraction, in the same frame.
                Assert.That(State, Is.EqualTo("Credits"));
                yield return null;
                Tick(false, true);
                Assert.That(State, Is.EqualTo("Credits"));
                Tick(false, false);
                Tick(true, true); // A new press skips directly to the results.
            }
            else if (seed == 3456)
            {
                generator.GetType().GetMethod("Generate").Invoke(generator, null);
                yield return null;
                Assert.That(State, Is.EqualTo("Playing"));
                Assert.That(music == null, Is.True);
                Assert.That(Time.timeScale, Is.EqualTo(.75f));
                Assert.That(Read(player, "CanAct"), Is.True);
                Assert.That(camera.cullingMask, Is.EqualTo(mask));
                yield break;
            }
            else
            {
                float Age() => (float)Menu.GetType().GetProperty("CreditsTime", Internal).GetValue(Menu);
                yield return Await(() => Age() >= 2f);
                Assert.That(sources.All(a => a == null), Is.True);
                float plateau = music.volume;
                var prefs = Menu.GetType().GetProperty("Preferences", Internal).GetValue(Menu);
                var applied = prefs.GetType().GetProperty("Applied", Internal).GetValue(prefs);
                Assert.That(plateau, Is.EqualTo((int)applied.GetType().GetField("Music", Internal).GetValue(applied) * .0025f).Within(.001f));
                yield return Await(() => Age() >= 16.5f, 18f);
                Assert.That(music.volume, Is.LessThanOrEqualTo(plateau * .4f));
                yield return Await(() => State == "Victory");
            }
            Assert.That(State, Is.EqualTo("Victory"));
            Assert.That(shutter.localPosition.y, Is.EqualTo(4f));
            Assert.That(camera.cullingMask, Is.EqualTo(mask));
            Assert.That(camera.fieldOfView, Is.EqualTo(fov));
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(originalFog));
            Assert.That(Menu.GetType().GetProperty("ActiveTime", Internal).GetValue(Menu), Is.EqualTo(timeAtExtraction));
            Assert.That(camera.transform.localPosition, Is.EqualTo(eye));
            Assert.That(Quaternion.Angle(camera.transform.localRotation, rotation), Is.LessThan(.1f));
            Assert.That(Read(hp, "Current"), Is.EqualTo(health));
            yield return null;
            Assert.That(sources.All(a => a == null), Is.True);
            Assert.That(music == null, Is.True);
            yield return VerifyResultNavigation("Victory", seed == 1234);
        }

        private static IEnumerator VerifyResultNavigation(string result, bool retry)
        {
            var previous = Menu;
            Press("Pause"); Press("Settings"); Press("Back"); Press("Play");
            Assert.That(State, Is.EqualTo(result));
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(AudioListener.pause, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Read(Single("PlayerController"), "CanAct"), Is.False);
            var prefs = Menu.GetType().GetProperty("Preferences", Internal);
            object Applied() => prefs.PropertyType.GetProperty("Applied", Internal).GetValue(prefs.GetValue(Menu));
            object settings = Applied();
            float volume = AudioListener.volume;
            string action = retry ? "RestartMission" : "MainMenu";
            Press(action); Press(action);
            yield return Await(() => previous == null);
            yield return null;
            var player = Single("PlayerController");
            var health = player.GetComponent(T("Health"));
            Assert.That(State, Is.EqualTo(retry ? "Playing" : "Title"));
            Assert.That(Time.timeScale, Is.EqualTo(retry ? .75f : 0f));
            Assert.That(AudioListener.volume, Is.EqualTo(volume));
            Assert.That(Applied(), Is.EqualTo(settings));
            var equipment = player.GetComponent(T("OutdoorPlayerEquipment"));
            foreach (string key in new[] { "HasCheckpointKey", "HasServerKey", "HasControlKey", "HasStorageKey" })
                Assert.That(equipment.GetType().GetProperty(key, Internal).GetValue(equipment), Is.False);
            Assert.That(Subscriptions(health, "Died"), Is.EqualTo(1));
            Assert.That(Read(player, "CanAct"), Is.EqualTo(retry));
            Assert.That(Read(health, "Current"), Is.EqualTo(Read(health, "Maximum")));
            Assert.That(Read(Read(player, "Weapons"), "ActiveWeapon"), Is.Null);
            Assert.That(Read(Single("OpenWorldMissionManager"), "DronesDefeated"), Is.EqualTo(0));
            Assert.That(Read(Single("OpenWorldMissionManager"), "BuildingMissionsCompleted"), Is.EqualTo(0));
            Assert.That(Object.FindObjectsByType(T("DroneBrain"), FindObjectsSortMode.None).Length, Is.EqualTo(26));
            Assert.That(Object.FindObjectsByType<AudioListener>().Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FailedReportKeepsPartialProgressAndExcludesPausedTime()
        {
            yield return StartMission();
            Interact(Single("SecurityAccessCard")); Interact(Single("CheckpointSecurityScanner"));
            var world = Single("WorldGenerator").transform.Find("GeneratedWorld");
            Damage(world.Find("OutdoorScoutPatrols/05 Checkpoint Approach").GetComponentInChildren(T("Health")));
            // Measure the active interval on both sides of a pause longer than the timer's precision.
            double started = Time.unscaledTimeAsDouble;
            yield return new WaitForSecondsRealtime(1.1f);
            Press("Pause"); double paused = Time.unscaledTimeAsDouble;
            Press("Settings"); yield return new WaitForSecondsRealtime(1.2f);
            Press("Back"); Press("Play");
            yield return Await(() => State == "Playing");
            Damage(Single("PlayerController").GetComponent(T("Health")));
            yield return Await(() => State == "GameOver");
            var type = Menu.GetType();
            Assert.That(type.GetProperty("ActiveTime", Internal).GetValue(Menu), Is.EqualTo($"00:{(int)(paused - started):00}"));
            Assert.That(type.GetProperty("TargetCount", Internal).GetValue(Menu), Is.EqualTo("1 / 3"));
            Assert.That(type.GetProperty("BuildingCount", Internal).GetValue(Menu), Is.EqualTo("1 / 4"));
            Assert.That(type.GetField("BuildingResults", Internal).GetValue(Menu), Is.EqualTo(new[] { "Complete", "Incomplete", "Incomplete", "Incomplete" }));
        }

        [UnityTest]
        public IEnumerator AppliedSettingsSurviveReloadAndAffectOnlyPresentation()
        {
            const string prefix = "ShiftFour.OpenWorld.Settings.v1.";
            string[] keys = { "master", "music", "sfx", "sensitivity", "quality" };
            var originals = keys.ToDictionary(k => k, k => PlayerPrefs.HasKey(prefix + k)
                ? (float?)(k == "sensitivity" ? PlayerPrefs.GetFloat(prefix + k) : PlayerPrefs.GetInt(prefix + k)) : null);
            const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object Preferences() => Menu.GetType().GetProperty("Preferences", members).GetValue(Menu);
            void Stage(string name, object value)
            {
                var settings = Preferences();
                var field = settings.GetType().GetField("Draft", members);
                var draft = field.GetValue(settings);
                var setting = draft.GetType().GetField(name, members);
                setting.SetValue(draft, setting.FieldType.IsEnum ? Enum.ToObject(setting.FieldType, value) : value);
                field.SetValue(settings, draft);
            }
            void Apply() => Preferences().GetType().GetMethod("Apply", members).Invoke(Preferences(), null);
            IEnumerator ReloadScene()
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Path, new LoadSceneParameters(LoadSceneMode.Single));
                yield return null;
            }
            try
            {
                PlayerPrefs.SetFloat(prefix + "sensitivity", float.NaN);
                PlayerPrefs.SetInt(prefix + "quality", 90);
                PlayerPrefs.SetInt(prefix + "master", 250);
                PlayerPrefs.SetInt(prefix + "music", 100);
                yield return ReloadScene();
                var player = Single("PlayerController");
                var sensitivity = player.GetType().GetProperty("MouseSensitivity", members);
                Assert.That(sensitivity.GetValue(player), Is.EqualTo(2f));
                Assert.That(AudioListener.volume, Is.EqualTo(1));
                var world = Single("WorldGenerator").transform.Find("GeneratedWorld");
                var grass = world.GetComponentsInChildren<MeshFilter>().First(m => m.name.StartsWith("Grasses and shrubs")).sharedMesh;
                uint full = grass.GetIndexCount(0);
                var originalPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                var music = player.GetComponentsInChildren<AudioSource>().Single(s => s.name == "Background music");
                int sourceCount = Object.FindObjectsByType<AudioSource>().Length;
                Press("Settings");
                Stage("Music", 0); Press("Back");
                Assert.That(music.volume, Is.GreaterThan(0f), "Back discards a staged edit.");
                Press("Settings"); Stage("Quality", 0); Stage("Sensitivity", 4f);
                Stage("Master", 40); Stage("Music", 0); Stage("Effects", 25);
                Apply();
                Assert.That(grass.GetIndexCount(0), Is.InRange(1u, full - 1));
                Assert.That(sensitivity.GetValue(player), Is.EqualTo(4f));
                Assert.That(AudioListener.volume, Is.EqualTo(.4f).Within(.001f));
                Assert.That(music.volume, Is.Zero);
                Assert.That(player.GetComponentsInChildren<AudioSource>().Single(s => s.name == "Player reload audio").volume,
                    Is.EqualTo(.65f * .25f).Within(.001f));
                var loop = world.GetComponentsInChildren<AudioSource>().First(s => s.name == "Drone flight loop");
                Assert.That(loop.volume, Is.EqualTo(.45f * .25f).Within(.001f));
                Assert.That(loop.spatialBlend, Is.EqualTo(1f));
                var runtime = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                Assert.That(runtime, Is.Not.SameAs(originalPipeline));
                Assert.That(Read(runtime, "shadowDistance"), Is.EqualTo(28f));
                Stage("Quality", 2); Apply();
                Assert.That(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline, Is.SameAs(runtime));
                Assert.That(Read(runtime, "shadowDistance"), Is.EqualTo(75f));
                Assert.That(grass.GetIndexCount(0), Is.EqualTo(full));
                Assert.That(world, Is.SameAs(Single("WorldGenerator").transform.Find("GeneratedWorld")));
                Assert.That(Object.FindObjectsByType<AudioSource>().Length, Is.EqualTo(sourceCount));
                yield return ReloadScene();
                Assert.That(sensitivity.GetValue(Single("PlayerController")), Is.EqualTo(4f));
                Assert.That(AudioListener.volume, Is.EqualTo(.4f).Within(.001f));
                Press("Settings");
                var settings = Preferences();
                settings.GetType().GetField("Draft", members).SetValue(settings,
                    settings.GetType().GetMethod("Defaults", members).Invoke(settings, null));
                Apply();
                Assert.That(sensitivity.GetValue(Single("PlayerController")), Is.EqualTo(2f));
                Assert.That(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline, Is.SameAs(originalPipeline));
                Assert.That(Read(originalPipeline, "shadowDistance"), Is.EqualTo(50f));
                Assert.That(AudioListener.volume, Is.EqualTo(1));
            }
            finally
            {
                foreach (string k in keys)
                {
                    if (!originals[k].HasValue) PlayerPrefs.DeleteKey(prefix + k);
                    else if (k == "sensitivity") PlayerPrefs.SetFloat(prefix + k, originals[k].Value);
                    else PlayerPrefs.SetInt(prefix + k, (int)originals[k].Value);
                }
                PlayerPrefs.Save();
            }
        }

        [UnityTest]
        public IEnumerator RestartAndMainMenuLoadFreshSingleWorlds()
        {
            yield return StartMission();
            var player = Single("PlayerController");
            var cache = Single("OutdoorSupplyCache");
            cache.GetType().GetMethod("Interact").Invoke(cache, null);
            Assert.That(Read(Read(player, "Weapons"), "ActiveWeapon"), Is.Not.Null);
            var previous = Menu;
            Press("Pause"); Press("RestartMission");
            yield return Await(() => previous == null);
            yield return null;
            Assert.That(State, Is.EqualTo("Playing"));
            player = Single("PlayerController");
            Assert.That(Read(Read(player, "Weapons"), "ActiveWeapon"), Is.Null);
            Assert.That(Read(player.GetComponent(T("Health")), "Current"), Is.EqualTo(Read(player.GetComponent(T("Health")), "Maximum")));
            Assert.That(Read(Single("OpenWorldMissionManager"), "BuildingMissionsCompleted"), Is.EqualTo(0));
            previous = Menu;
            Press("Pause"); Press("MainMenu");
            yield return Await(() => previous == null);
            yield return null;
            Assert.That(State, Is.EqualTo("Title"));
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(Object.FindObjectsByType(T("DroneBrain"), FindObjectsSortMode.None).Length, Is.EqualTo(26));
            Assert.That(Single("WorldGenerator").transform.Find("GeneratedWorld"), Is.Not.Null);
            Assert.That(Single("PrototypePlayerFeedback"), Is.Not.Null);
        }
    }
}
#endif

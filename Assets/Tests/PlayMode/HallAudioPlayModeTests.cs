#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShiftFour.Tests
{
    public sealed class HallAudioPlayModeTests
    {
        const string ScenePath = "Assets/Scenes/OpenWorldPrototype.unity";
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static Type T(string name) => Type.GetType("ShiftFour." + name + ", Assembly-CSharp", true);
        static Component One(string type) => (Component)Object.FindFirstObjectByType(T(type));
        static void Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Members).Invoke(target, args);
        static void Interact(Component target) => Call(target, "Interact");
        static Component[] InRoom(Transform hall, string type) => hall.GetComponentsInChildren(T(type), true).Cast<Component>().ToArray();

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("After hall audio test"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator HallsFadeRespondToRealInteractionsAndReleaseSources()
        {
            var world = One("WorldGenerator");
            var player = One("PlayerController");
            var menu = One("OutdoorMissionMenu");
            var capsule = player.GetComponent<CharacterController>();
            var mix = One("GameplayAudio");
            Call(player.GetComponent(T("Health")), "Configure", 10000f, false);
            Call(mix, "SetCategoryVolumes", 1f, 1f);
            string[] halls = { "Checkpoint", "Server", "Control", "Storage" };
            string[] beds = { "Security", "Cooling", "Monitoring", "Ventilation" };

            void Place(Vector3 position)
            {
                capsule.enabled = false;
                player.transform.position = position;
                capsule.enabled = true;
                Call(player, "ApplyCameraPose");
                Physics.SyncTransforms();
            }

            foreach (int seed in new[] { 1234, 5678 })
            {
                var old = world.transform.Find("GeneratedWorld/Buildings/Storage/Interior/Hall audio");
                world.GetType().GetField("seed", Members).SetValue(world, seed);
                Call(world, "Generate");
                yield return null;
                Assert.That(old == null, Is.True);
                var buildings = world.transform.Find("GeneratedWorld/Buildings");
                Assert.That(InRoom(buildings, "HallAudio").Length, Is.EqualTo(4));
                Assert.That(InRoom(buildings, "GuardBrain").Length, Is.EqualTo(10));
                for (int i = 0; i < halls.Length; i++)
                {
                    var hall = buildings.Find(halls[i]);
                    var audio = hall.Find("Interior/Hall audio");
                    var sources = audio.GetComponentsInChildren<AudioSource>();
                    Assert.That(sources.Length, Is.EqualTo(i == 3 ? 3 : 2));
                    Assert.That(sources.All(s => s.spatialBlend == 1 && !s.ignoreListenerPause && !s.playOnAwake), Is.True);
                    var bed = audio.Find("Equipment ambience").GetComponent<AudioSource>();
                    var signal = audio.Find("Equipment signal").GetComponent<AudioSource>();
                    Assert.That(bed.clip.name, Is.EqualTo(beds[i]));
                    Assert.That(bed.volume, Is.Zero);
                    Place(hall.TransformPoint(Vector3.up * .21f));
                    yield return new WaitForSeconds(.8f);
                    Assert.That(bed.isPlaying, Is.True, halls[i]);
                    float full = bed.volume;
                    Assert.That(full, Is.GreaterThan(.15f));

                    if (i == 0)
                    {
                        var reader = InRoom(hall, "CheckpointSecurityScanner").Single();
                        var completed = reader.GetType().GetField("Completed", Members);
                        var owner = audio.GetComponent(T("HallAudio"));
                        Assert.That(((Delegate)completed.GetValue(reader)).GetInvocationList().Count(d => d.Target == (object)owner), Is.EqualTo(1));
                        Interact(reader);
                        Assert.That(signal.clip.name, Is.EqualTo("Reject"));
                        Interact(InRoom(hall, "SecurityAccessCard").Single());
                        Assert.That(signal.clip.name, Is.EqualTo("Relay"));
                        Interact(reader);
                        Assert.That(signal.clip.name, Is.EqualTo("Relay"));
                        Assert.That((bool)reader.GetType().GetProperty("IsComplete").GetValue(reader), Is.True);
                        Call(menu, "Pause");
                        yield return new WaitForSecondsRealtime(1.4f);
                        Assert.That(signal.clip.name, Is.EqualTo("Relay"));
                        Call(menu, "Play");
                        yield return new WaitUntil(() => !AudioListener.pause);
                        yield return new WaitForSeconds(1.45f);
                        Assert.That(signal.clip.name, Is.EqualTo("Authenticate"));
                        Assert.That(Vector3.Distance(signal.transform.position, reader.transform.position), Is.LessThan(.001f));
                        Call(menu, "Pause");
                        Assert.That(AudioListener.pause, Is.True);
                        yield return new WaitForSecondsRealtime(.1f);
                        int pausedSample = bed.timeSamples;
                        yield return new WaitForSecondsRealtime(.12f);
                        Assert.That(bed.timeSamples, Is.EqualTo(pausedSample));
                        foreach (float gain in new[] { 0f, .5f, 1f })
                        {
                            var settings = menu.GetType().GetProperty("Preferences", Members).GetValue(menu);
                            var values = settings.GetType().GetProperty("Applied", Members).GetValue(settings);
                            foreach (string field in new[] { "Master", "Effects" })
                                values.GetType().GetField(field, Members).SetValue(values, Mathf.RoundToInt(100 * gain));
                            values.GetType().GetField("Music", Members).SetValue(values, 0);
                            Call(settings, "ApplySoundAndControls", values);
                            yield return null;
                            Assert.That(bed.volume, Is.EqualTo(full * gain).Within(.001f));
                            Assert.That(AudioListener.volume, Is.EqualTo(gain).Within(.001f));
                        }
                        Call(menu, "Play");
                        yield return new WaitUntil(() => !AudioListener.pause);
                        yield return new WaitForSeconds(.12f);
                        Assert.That(bed.isPlaying, Is.True);
                    }
                    else if (i == 1)
                    {
                        var terminals = InRoom(hall, "ServerTerminal").OrderBy(t => t.name).ToArray();
                        Interact(terminals[0]);
                        Assert.That(signal.clip.name, Is.EqualTo("Reject"));
                        foreach (int n in new[] { 1, 0, 2 })
                        {
                            Interact(terminals[n]);
                            Assert.That(signal.clip.name, Is.EqualTo(n == 2 ? "Authenticate" : "Relay"));
                            Assert.That(Vector3.Distance(signal.transform.position, terminals[n].transform.position + Vector3.up * 1.5f), Is.LessThan(.001f));
                        }
                    }
                    else if (i == 2)
                    {
                        yield return new WaitForSeconds(3.3f);
                        Assert.That(signal.clip.name, Is.EqualTo("Advisory"));
                        int switched = 0;
                        foreach (var toggle in InRoom(hall, "LockdownSecuritySwitch"))
                        {
                            Interact(toggle);
                            Assert.That(signal.clip.name, Is.EqualTo(++switched == 3 ? "Authenticate" : "Relay"));
                        }
                        Interact(InRoom(hall, "ControlLockdownMission").Single());
                        Assert.That(signal.clip.name, Is.EqualTo("Relay"));
                        yield return new WaitForSeconds(1.35f);
                        Assert.That(signal.clip.name, Is.EqualTo("Authenticate"));
                    }
                    else
                    {
                        var motor = audio.Find("Generator motor").GetComponent<AudioSource>();
                        Assert.That(motor.volume, Is.Zero);
                        foreach (var cell in InRoom(hall, "PowerCellPickup"))
                        {
                            Interact(cell);
                            Assert.That(signal.clip.name, Is.EqualTo("Relay"));
                            Assert.That(Vector3.Distance(signal.transform.position, cell.transform.position), Is.LessThan(.001f));
                        }
                        Interact(InRoom(hall, "StoragePowerMission").Single());
                        Assert.That(signal.clip.name, Is.EqualTo("Authenticate"));
                        yield return new WaitForSeconds(.4f);
                        Assert.That(bed.volume, Is.InRange(.001f, full - .001f));
                        Assert.That(motor.volume, Is.GreaterThan(0));
                        yield return new WaitForSeconds(1f);
                        Assert.That(bed.volume, Is.Zero);
                        Assert.That(motor.clip.name, Is.EqualTo("Generator"));
                        bed = motor;
                        full = motor.volume;
                    }
                    Place(world.transform.Find("Spawn").position);
                    yield return new WaitForSeconds(.18f);
                    Assert.That(bed.volume, Is.InRange(.001f, full - .001f));
                    yield return new WaitForSeconds(.65f);
                    Assert.That(bed.volume, Is.Zero);
                    Assert.That(bed.isPlaying, Is.False);
                }
            }
        }
    }
}
#endif

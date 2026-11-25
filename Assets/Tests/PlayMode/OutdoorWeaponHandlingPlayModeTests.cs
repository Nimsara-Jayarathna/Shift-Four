#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShiftFour.Tests
{
    public sealed class OutdoorWeaponHandlingPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/OpenWorldPrototype.unity";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private float previousDelta;
        private Component equipment, mount, weapons, generator;
        private Camera camera;
        private Transform knife;

        private static Type RuntimeType(string name) => Type.GetType("ShiftFour." + name + ", Assembly-CSharp", true);
        private static object Call(object target, string name) => target.GetType().GetMethod(name, Members).Invoke(target, null);
        private static object Read(object target, string name) => target.GetType().GetProperty(name, Members).GetValue(target);
        private object State => Read(weapons, "ActiveWeapon");

        [UnitySetUp]
        public IEnumerator Load()
        {
            previousDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return OutdoorMissionMenuPlayModeTests.StartMission();
            equipment = (Component)Object.FindFirstObjectByType(RuntimeType("OutdoorPlayerEquipment"));
            mount = equipment.GetComponent(RuntimeType("FirstPersonWeaponMount"));
            weapons = equipment.GetComponent(RuntimeType("WeaponController"));
            generator = (Component)Object.FindFirstObjectByType(RuntimeType("WorldGenerator"));
            camera = equipment.GetComponentInChildren<Camera>();
            knife = camera.transform.Find("First Person Knife");
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.captureDeltaTime = previousDelta;
            Scene current = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("After outdoor handling test"));
            if (current.path == ScenePath) yield return SceneManager.UnloadSceneAsync(current);
        }

        private void CheckSinglePresentation()
        {
            Component pistol = (Component)Read(mount, "Instance");
            Assert.That(knife.gameObject.activeInHierarchy && pistol != null && pistol.gameObject.activeInHierarchy, Is.False);
        }

        private IEnumerator WaitForPistol()
        {
            for (int i = 0; i < 100 && !(bool)Read(mount, "IsReady"); i++)
            {
                CheckSinglePresentation();
                Assert.That(Call(weapons, "TryFire"), Is.EqualTo(false), "Outdoor shots wait until the draw completes");
                yield return null;
            }
            Assert.That(Read(mount, "IsReady"), Is.EqualTo(true));
            Assert.That(knife.gameObject.activeInHierarchy, Is.False);
        }

        private void OpenCache()
        {
            var crate = generator.transform.Find("GeneratedWorld/OutdoorSupplyCache01/Supply crate");
            Call(crate.GetComponent(RuntimeType("OutdoorSupplyCache")), "Interact");
        }

        [UnityTest]
        public IEnumerator KnifeContactUsesTheExistingDroneHitboxOnceOnTheAcceptedFrame()
        {
            yield return null;
            var scout = (Component)Object.FindFirstObjectByType(RuntimeType("ScoutBrain"));
            var target = scout.GetComponent<CapsuleCollider>();
            var health = scout.GetComponent(RuntimeType("Health"));
            var capsule = equipment.GetComponent<CharacterController>();
            var player = equipment.GetComponent(RuntimeType("PlayerController"));
            bool inSight = false;
            for (int angle = 0; angle < 360 && !inSight; angle += 45)
            {
                Vector3 eye = target.bounds.center + Quaternion.Euler(0, angle, 0) * Vector3.forward * 1.4f;
                capsule.enabled = false;
                equipment.transform.position += eye - camera.transform.position;
                capsule.enabled = true;
                Call(player, "ApplyCameraPose");
                camera.transform.LookAt(target.bounds.center);
                Physics.SyncTransforms();
                inSight = Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit, 1.85f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent(RuntimeType("Health")) == health;
            }
            Assert.That(inSight, Is.True);
            float hp = (float)Read(health, "Current");
            Assert.That(Call(equipment, "TrySlash"), Is.EqualTo(true));
            Assert.That(Read(health, "Current"), Is.EqualTo(Mathf.Max(0f, hp - 35f)));
            Assert.That(Call(equipment, "TrySlash"), Is.EqualTo(false));
        }

        [UnityTest]
        public IEnumerator LastAcceptedShotKeepsItsFlashAndReloadsExactlyOnce()
        {
            OpenCache();
            yield return WaitForPistol();
            Component model = (Component)Read(mount, "Instance");
            Component presenter = model.GetComponent(RuntimeType("PistolPresentation"));
            var flash = (Renderer)Read(presenter, "MuzzleFlash");
            int shots = 0;
            for (int i = 0; i < 900 && shots < 20; i++)
            {
                if ((bool)Call(weapons, "TryFire")) shots++;
                if (shots < 20) yield return null;
            }
            Assert.That(shots, Is.EqualTo(20));
            Assert.That(Read(State, "RoundsInMagazine"), Is.EqualTo(0));
            Assert.That(Read(State, "ReloadState").ToString(), Is.EqualTo("Reloading"));
            Assert.That(flash.enabled, Is.True);
            Assert.That(Call(weapons, "TryFire"), Is.EqualTo(false));
            yield return new WaitForSeconds(.1f);
            Assert.That(flash.enabled, Is.False);
            yield return new WaitForSeconds(1.6f);
            Assert.That(Read(State, "RoundsInMagazine"), Is.EqualTo(20));
            Assert.That(Read(State, "ReserveRounds"), Is.EqualTo(140));
        }

        [UnityTest]
        public IEnumerator LosingFocusPausesAnInProgressReloadUntilResume()
        {
            OpenCache();
            yield return WaitForPistol();
            Assert.That(Call(weapons, "TryFire"), Is.EqualTo(true));
            Assert.That(Call(weapons, "RequestReload"), Is.EqualTo(true));
            var ammo = State;
            var menu = generator.GetComponent(RuntimeType("OutdoorMissionMenu"));
            menu.SendMessage("OnApplicationFocus", false);
            Assert.That(Read(menu, "Screen").ToString(), Is.EqualTo("Paused"));
            menu.SendMessage("OnApplicationFocus", true);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(Read(ammo, "ReloadState").ToString(), Is.EqualTo("Reloading"));
            Assert.That(Read(ammo, "RoundsInMagazine"), Is.EqualTo(19));
            Assert.That(Call(weapons, "TryFire"), Is.EqualTo(false));
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Call(menu, "Play");
            for (int i = 0; i < 240 && Read(ammo, "ReloadState").ToString() == "Reloading"; i++)
                yield return null;
            Assert.That(Read(ammo, "RoundsInMagazine"), Is.EqualTo(20));
        }

        [UnityTest]
        public IEnumerator SupplyDrawReloadInterruptionReversalAndRegeneration()
        {
            Assert.That(State, Is.Null);
            Assert.That(knife.gameObject.activeInHierarchy, Is.True);
            Assert.That(Call(equipment, "SelectPistol"), Is.EqualTo(false));
            Assert.That(Call(equipment, "TrySlash"), Is.EqualTo(true));
            Assert.That(Call(equipment, "TrySlash"), Is.EqualTo(false));
            OpenCache();
            var ammo = State;
            Assert.That(Read(ammo, "ReserveRounds"), Is.EqualTo(160));
            Assert.That(Read(ammo, "RoundsInMagazine"), Is.EqualTo(20));
            Assert.That(Call(weapons, "TryFire"), Is.EqualTo(false), "Knife is still lowering");
            yield return WaitForPistol();
            Assert.That(Call(weapons, "TryFire"), Is.EqualTo(true));
            Assert.That(Call(weapons, "RequestReload"), Is.EqualTo(true));
            yield return new WaitForSeconds(.80f);
            Assert.That(Call(weapons, "TryFire"), Is.EqualTo(false));
            var pistol = (Component)Read(mount, "Instance");
            var presentation = pistol.GetComponent(RuntimeType("PistolPresentation"));
            var mag = (Transform)Read(presentation, "MagazinePart");
            var bounds = mag.GetComponent<Renderer>().bounds;
            foreach (int x in new[] {-1, 1})
            foreach (int y in new[] {-1, 1})
            foreach (int z in new[] {-1, 1})
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                Vector3 v = camera.WorldToViewportPoint(corner);
                Assert.That(v.x, Is.InRange(0f, 1f));
                Assert.That(v.y, Is.InRange(0f, 1f));
                Assert.That(v.z, Is.GreaterThan(camera.nearClipPlane));
            }
            Call(equipment, "SelectKnife");
            Assert.That(Read(ammo, "ReloadState").ToString(), Is.EqualTo("Idle"));
            Assert.That(pistol.gameObject.activeInHierarchy, Is.True, "Outgoing pistol must lower before it disappears");
            yield return null;
            Assert.That(Call(equipment, "SelectPistol"), Is.EqualTo(true));
            yield return WaitForPistol();
            Call(equipment, "SelectKnife");
            for (int i = 0; i < 60; i++) { CheckSinglePresentation(); yield return null; }
            Assert.That(State, Is.Null);
            Assert.That(knife.gameObject.activeInHierarchy, Is.True);
            Assert.That(Call(equipment, "SelectPistol"), Is.EqualTo(true));
            yield return WaitForPistol();
            Assert.That(State, Is.SameAs(ammo));
            Assert.That(Read(ammo, "RoundsInMagazine"), Is.EqualTo(19));
            Assert.That(Read(ammo, "ReserveRounds"), Is.EqualTo(160));
            Call(generator, "Generate");
            yield return null;
            Assert.That(State, Is.Null);
            Assert.That(equipment.GetComponentsInChildren(RuntimeType("OutdoorKnifeViewmodel"), true).Length, Is.EqualTo(1));
            Assert.That(knife.gameObject.activeInHierarchy, Is.True);
            Assert.That(Call(equipment, "SelectPistol"), Is.EqualTo(false));
        }
    }
}
#endif

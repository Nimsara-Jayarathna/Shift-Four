using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    internal sealed class OutdoorSupplyCache : MonoBehaviour, IInteractable
    {
        internal const string HierarchyName = "OutdoorSupplyCache01";
        internal const string AcquiredMessage = "WEAPON ACQUIRED\nCHECKPOINT ACCESS KEY ACQUIRED";
        private static readonly Quaternion OpenLid = Quaternion.Euler(105f, 0f, 0f);

        [SerializeField] private Transform lid;
        [SerializeField] private GameObject supplies;
        private OutdoorPlayerEquipment equipment;
        [SerializeField] private Light beacon;
        [SerializeField] private GameObject sealedStatus;
        [SerializeField] private GameObject discoveredStatus;
        private float noticeUntil;
        private const float IdleBeaconIntensity = 0.85f;

        internal event System.Action Discovered;
        internal bool IsDiscovered { get; private set; }
        internal string Objective => IsDiscovered ? "Reach Checkpoint" : "Find Supply Station";
        internal string Notice => IsDiscovered && Time.unscaledTime < noticeUntil ? AcquiredMessage : string.Empty;
        public string Prompt => IsDiscovered ? "SUPPLIES COLLECTED" : "E: TAKE WEAPON AND CHECKPOINT KEY";

        // Both candidates face the avenue. The fallback fits just inside the south perimeter,
        // south of the earliest possible Checkpoint branch and off the extraction spur.
        internal static bool TryBuild(Transform parent, Vector3 entry, float boundary, List<Rect> reserved,
            PrototypeMaterials materials, Font font, OutdoorPlayerEquipment equipment, WeaponDefinition pistol)
        {
            foreach (float offset in new[] { -7.9f, -11.85f })
            {
                var position = new Vector3(-17f, 0f, entry.z + offset);
                var footprint = new Rect(position.x - 2.25f, position.z - 1.75f, 4.5f, 3.5f);
                if (footprint.yMin <= -boundary + .2f || reserved.Exists(area => area.Overlaps(footprint)))
                    continue;

                reserved.Add(footprint);
                Build(parent, position, materials, font, equipment, pistol);
                return true;
            }

            return false;
        }

        private static void Build(Transform parent, Vector3 position, PrototypeMaterials m, Font font,
            OutdoorPlayerEquipment equipment, WeaponDefinition pistol)
        {
            Transform station = Group(HierarchyName, parent, position);
            station.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            Box("Concrete platform", station, new Vector3(0, .05f, 0), new Vector3(3.38f, .1f, 4.08f), m.Concrete, false);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Steel platform edging", station, new Vector3(side * 1.60f, .108f, 0),
                    new Vector3(.15f, .022f, 4f), m.Metal, false);
                Box("Sign upright", station, new Vector3(side * 1.31f, 1.34f, 1.44f),
                    new Vector3(.17f, 2.48f, .19f), m.Metal, false);
                for (int stripe = 0; stripe < 3; stripe++)
                {
                    var line = Box("Warning stripe", station, new Vector3(side * (1.14f + stripe * .19f), .11f, -1.62f),
                        new Vector3(.11f, .014f, .45f), m.Checkpoint, false);
                    line.transform.localRotation = Quaternion.Euler(0f, 24f, 0f);
                }
            }

            Box("Research station sign", station, new Vector3(0, 2.43f, 1.44f), new Vector3(3.24f, .69f, .18f), m.Metal, false);
            Box("Cyan identification strip", station, new Vector3(0, 2.73f, 1.33f), new Vector3(3.08f, .074f, .10f), signal, false);
            Label("SUPPLY CACHE 01", station, new Vector3(0, 2.48f, 1.33f), .34f, font, 3.05f, .39f, m.Lettering);
            Label("RESEARCH SUPPLIES", station, new Vector3(0, 2.23f, 1.33f), .15f, font, 2.5f, .15f, m.Lettering);
            Box("Warning sign backing", station, new Vector3(-1.14f, 1.51f, 1.35f), new Vector3(.78f, .43f, .14f), m.Metal, false);
            Box("Warning sign border", station, new Vector3(-1.14f, 1.71f, 1.26f), new Vector3(.77f, .07f, .07f), m.Checkpoint, false);
            Label("CAUTION", station, new Vector3(-1.14f, 1.53f, 1.26f), .135f, font, .69f, .16f, m.Lettering);

            Box("Beacon lens", station, new Vector3(1.28f, 2.91f, 1.44f), new Vector3(.23f, .25f, .23f), signal, false);
            var light = Group("Cache beacon", station, new Vector3(1.28f, 2.82f, 1.17f)).gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.18f, .87f, 1f);
            light.range = 3.2f;
            light.intensity = IdleBeaconIntensity;
            light.shadows = LightShadows.None;

            Transform crate = Group("Supply crate", station, new Vector3(0, .1f, -.22f));
            Box("Lower steel shell", crate, new Vector3(0, .31f, 0), new Vector3(2.04f, .62f, 1.39f), m.Metal, false);
            var target = crate.gameObject.AddComponent<BoxCollider>();
            target.center = Vector3.up * .62f;
            target.size = new Vector3(2.12f, 1.24f, 1.42f);
            Box("Interior tray", crate, new Vector3(0, .64f, 0), new Vector3(1.82f, .05f, 1.16f), m.Glass, false);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Side reinforcement", crate, new Vector3(side * .96f, .53f, 0),
                    new Vector3(.19f, .33f, 1.42f), m.Concrete, false);
                Box("Tray rim", crate, new Vector3(0, .65f, side * .66f),
                    new Vector3(1.90f, .13f, .13f), m.Metal, false);
                Box("Latch", crate, new Vector3(side * .69f, .63f, -.72f),
                    new Vector3(.18f, .24f, .095f), m.Checkpoint, false);
            }

            Transform lid = Group("Hinged crate lid", crate, new Vector3(0, .73f, .7f));
            Box("Steel lid", lid, new Vector3(0, .07f, -.7f), new Vector3(2.13f, .16f, 1.45f), m.Server, false);
            Box("Lid faceplate", lid, new Vector3(0, .15f, -.72f), new Vector3(1.68f, .05f, 1.11f), m.Metal, false);
            Box("Lid cyan stripe", lid, new Vector3(0, .183f, -1.21f), new Vector3(1.55f, .026f, .065f), signal, false);

            Box("Crate display", crate, new Vector3(0, .42f, -.717f), new Vector3(1.22f, .28f, .09f), m.Glass, false);
            var displayFace = new Vector3(0, .42f, -.77f);
            var sealedLabel = Label("SUPPLIES READY", crate, displayFace, .14f, font, .9f, .14f, m.Lettering);
            var discoveredLabel = Label("SUPPLIES COLLECTED", crate, displayFace, .14f, font, .9f, .14f, m.Lettering);
            discoveredLabel.gameObject.SetActive(false);

            var activity = crate.gameObject.AddComponent<OutdoorSupplyCache>();
            activity.equipment = equipment;
            activity.supplies = BuildSupplies(crate, m, signal, font, pistol).gameObject;
            activity.lid = lid;
            activity.beacon = light;
            activity.sealedStatus = sealedLabel.gameObject;
            activity.discoveredStatus = discoveredLabel.gameObject;
        }

        private static Transform BuildSupplies(Transform crate, PrototypeMaterials m, Material signal, Font font, WeaponDefinition pistol)
        {
            Transform items = Group("Equipment pickup", crate);
            Box("Display cradle", items, new Vector3(0, .936f, 0), new Vector3(1.8f, .068f, .81f), m.Glass, false);
            var gun = Group("Pistol pickup", items, new Vector3(-.49f, 1.10f, .02f));
            gun.localRotation = Quaternion.Euler(0f, 78f, 68f);
            var model = pistol?.ViewmodelPrefab?.transform.Find("RecoilPivot/FN_Five_Seven");
            if (model != null)
            {
                gun.localScale = Vector3.one * 2.6f;
                CopyPickupMeshes(model, gun);
            }
            else
            {
                Box("Slide", gun, new Vector3(0f, .04f, .11f), new Vector3(.15f, .13f, .62f), m.Metal, false);
                Box("Grip", gun, new Vector3(0f, -.09f, -.11f), new Vector3(.14f, .28f, .18f), m.Concrete, false);
            }

            Transform key = Group("Checkpoint Access Key", items, new Vector3(.49f, 1.02f, .03f));
            key.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            Box("Key shell", key, Vector3.zero, new Vector3(.49f, .068f, .34f), m.Checkpoint, false);
            Box("Key inset", key, Vector3.up * .042f, new Vector3(.38f, .024f, .25f), m.Metal, false);
            Box("Key identifier", key, new Vector3(-.13f, .057f, 0), new Vector3(.065f, .015f, .18f), signal, false);
            Box("E contact pins", key, new Vector3(.238f, .0f, 0), new Vector3(.07f, .035f, .16f), m.Concrete, false);
            Label("PISTOL", items, new Vector3(-.48f, 1.015f, -.44f), .09f, font, .65f, .13f, m.Lettering);
            Label("CHECKPOINT ACCESS KEY", items, new Vector3(.45f, 1.015f, -.44f), .09f, font, .93f, .13f, m.Lettering);
            return items;
        }

        // Copy the geometry and materials only. The world pickup has no weapon or viewmodel behavior.
        private static void CopyPickupMeshes(Transform source, Transform destination)
        {
            if (source.TryGetComponent<MeshFilter>(out var filter) &&
                source.TryGetComponent<MeshRenderer>(out var renderer))
            {
                destination.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                destination.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }

            foreach (Transform child in source)
            {
                var copy = Group(child.name, destination, child.localPosition);
                copy.localRotation = child.localRotation;
                copy.localScale = child.localScale;
                CopyPickupMeshes(child, copy);
            }
        }

        public void Interact()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsDiscovered) return;
            if (equipment == null || !equipment.CollectSupplies()) return;
            IsDiscovered = true;
            supplies.SetActive(false);
            var target = GetComponent<BoxCollider>();
            target.center = Vector3.up * .38f;
            target.size = new Vector3(2.12f, .76f, 1.42f);
            noticeUntil = Time.unscaledTime + 3.5f;
            sealedStatus.SetActive(false);
            discoveredStatus.SetActive(true);
            StartCoroutine(ShowOpening());
            Discovered?.Invoke();
        }

        private void OnEnable()
        {
            if (!IsDiscovered) return;
            lid.localRotation = OpenLid;
            beacon.intensity = IdleBeaconIntensity;
        }

        private IEnumerator ShowOpening()
        {
            const float duration = .48f;
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < duration)
            {
                float progress = (Time.unscaledTime - start) / duration;
                lid.localRotation = Quaternion.Slerp(Quaternion.identity, OpenLid, Mathf.SmoothStep(0f, 1f, progress));
                beacon.intensity = Mathf.Lerp(4.4f, IdleBeaconIntensity, progress);
                yield return null;
            }
            lid.localRotation = OpenLid;
            beacon.intensity = IdleBeaconIntensity;
        }
    }
}

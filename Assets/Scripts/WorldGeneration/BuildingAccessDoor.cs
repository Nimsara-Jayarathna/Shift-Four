using System.Collections;
using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    internal sealed class BuildingAccessDoor : MonoBehaviour, IInteractable
    {
        internal enum Site { Server = 1, Control = 2, Storage = 3 }
        private const string Granted = "ACCESS GRANTED";

        private OutdoorPlayerEquipment equipment;
        private Site site;
        private string unlockPrompt;
        private Transform shutter;
        private BoxCollider barrier;
        private Renderer[] indicators;
        private bool opening;
        private float noticeUntil;

        internal bool IsOpen { get; private set; }
        internal string KeyRequired { get; private set; }
        private bool HasKey => equipment != null && (site switch
        {
            Site.Server => equipment.HasServerKey,
            Site.Control => equipment.HasControlKey,
            Site.Storage => equipment.HasStorageKey,
            _ => false
        });
        internal string Notice
        {
            get
            {
                if (Time.unscaledTime >= noticeUntil) return string.Empty;
                if (opening || IsOpen) return Granted;
                return HasKey ? string.Empty : KeyRequired;
            }
        }

        public string Prompt => opening || IsOpen ? Granted : unlockPrompt;

        internal static void Build(Transform building, Site site, OutdoorPlayerEquipment owner, PrototypeMaterials materials, Font font)
        {
            string name = site.ToString();
            string title = name.ToUpperInvariant();
            float front = -ResearchBuildingBuilder.FrontOffset((int)site);
            float width = ResearchBuildingBuilder.EntranceWidth((int)site);
            Transform root = Group(name + " Access Door", building,
                new Vector3(0f, ResearchBuildingBuilder.FloorHeight, front - .18f));

            var door = root.gameObject.AddComponent<BuildingAccessDoor>();
            door.equipment = owner;
            door.site = site;
            door.KeyRequired = title + " ACCESS KEY REQUIRED";
            door.unlockPrompt = "E: UNLOCK " + title + " DOOR";
            // Keep the closed doorway sealed during the animation. The moving visual has no colliders.
            door.barrier = root.GetComponent<BoxCollider>();
            door.barrier.center = Vector3.up * 1.9f;
            door.barrier.size = new Vector3(width + .06f, 3.8f, .31f);

            door.shutter = Group(name + " security shutter", root);
            Box("Armored panel", door.shutter, Vector3.up * 1.9f,
                new Vector3(width + .02f, 3.79f, .29f), materials.Metal, false);
            Box("Inset panel", door.shutter, new Vector3(0f, 1.91f, -.16f),
                new Vector3(width - .41f, 3.21f, .082f), materials.Concrete, false);
            for (int i = 0; i < 4; i++)
                Box("Horizontal reinforcement", door.shutter, new Vector3(0f, .51f + i * .86f, -.224f),
                    new Vector3(width - .34f, .095f, .062f), materials.Metal, false);
            Box("Door plaque", door.shutter, new Vector3(0f, 2.64f, -.235f),
                new Vector3(site == Site.Storage ? 4.2f : 2.94f, .54f, .052f), materials.Metal, false);
            Label(site == Site.Storage ? "STORAGE LOCKED" : title + " ACCESS", door.shutter,
                new Vector3(0f, 2.64f, -.27f), .22f, font, site == Site.Storage ? 3.99f : 2.76f, .29f, materials.Lettering);

            Material signal = materials.CheckpointSignal != null ? materials.CheckpointSignal : materials.Marking;
            door.indicators = new Renderer[2];
            // Control has a deeper portal frame than Server.
            float readerInset = site == Site.Control ? -.22f : 0f;
            float readerSide = site == Site.Storage ? 5.73f : 2.69f;
            for (int i = 0; i < door.indicators.Length; i++)
            {
                float x = i == 0 ? -readerSide : readerSide;
                // Stand proud of the existing decorative jamb so the lenses remain visible from the approach.
                Box(name + " lock housing", root, new Vector3(x, 2.42f, -1.20f + readerInset),
                    new Vector3(.49f, .54f, .20f), materials.Metal, false);
                // The visible reader is also an E target. Its solid hitbox stays outside the passage.
                var reader = root.gameObject.AddComponent<BoxCollider>();
                reader.center = new Vector3(x, 2.42f, -1.25f + readerInset);
                reader.size = new Vector3(.53f, .58f, .26f);
                door.indicators[i] = Box(name + " lock indicator", root, new Vector3(x, 2.42f, -1.33f + readerInset),
                    new Vector3(.30f, .32f, .074f), signal, false).GetComponent<Renderer>();
            }
            door.SetIndicators(new Color(1f, .095f, .075f));
        }

        public void Interact()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || opening || IsOpen) return;
            noticeUntil = Time.unscaledTime + 3.5f;
            if (!HasKey) return;

            opening = true;
            equipment.GetComponent<GameplayAudio>()?.PlayOutdoorDoor(transform.position);
            SetIndicators(new Color(.16f, 1f, .36f));
            StartCoroutine(Open());
        }

        private void SetIndicators(Color color)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            foreach (Renderer indicator in indicators) indicator.SetPropertyBlock(properties);
        }

        private IEnumerator Open()
        {
            const float duration = .66f;
            const float rise = 3.9f;
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < duration)
            {
                float progress = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - start) / duration);
                shutter.localPosition = Vector3.up * (progress * rise);
                yield return null;
            }

            shutter.localPosition = Vector3.up * rise;
            barrier.enabled = false;
            IsOpen = true;
            opening = false;
        }
    }
}
